#!/usr/bin/env python3
"""Smoke test client for the cloud bridge (Phase 1). Standard library only.

Talks MCP over HTTP to the test server, directly or through Cloudflare, and
prints the round-trip time of every step. Run it

  * on the PC against http://127.0.0.1:30201/mcp   -> proves the server
  * on the PC against https://bridge-test.<domain>/mcp -> proves tunnel + Access
  * inside the cloud session                           -> proves the whole chain

Headers come from the environment, the same variables the cloud session uses:
  CLOUD_BRIDGE_URL         default http://127.0.0.1:30201/mcp
  CF_ACCESS_CLIENT_ID      Cloudflare Access service token id
  CF_ACCESS_CLIENT_SECRET  Cloudflare Access service token secret
  BRIDGE_TEST_KEY          X-Bridge-Key value

  --no-auth       send no headers at all; passes only if the server answers 401/403
  --no-cf         send only X-Bridge-Key (expect Cloudflare 403 through the tunnel)
  --no-key        send only the Access headers (expect 401 from the server)
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.request


def build_headers(mode: str) -> dict:
    h = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream"}
    cid = os.environ.get("CF_ACCESS_CLIENT_ID", "")
    csec = os.environ.get("CF_ACCESS_CLIENT_SECRET", "")
    key = os.environ.get("BRIDGE_TEST_KEY", "")
    if mode in ("full", "no-key") and cid and csec:
        h["CF-Access-Client-Id"] = cid
        h["CF-Access-Client-Secret"] = csec
    if mode in ("full", "no-cf") and key:
        h["X-Bridge-Key"] = key
    return h


class Client:
    def __init__(self, url: str, headers: dict, timeout: float):
        self.url, self.headers, self.timeout = url, headers, timeout
        self.session_id = None
        self.next_id = 1

    def post(self, body: dict | None):
        data = json.dumps(body).encode("utf-8")
        headers = dict(self.headers)
        if self.session_id:
            headers["Mcp-Session-Id"] = self.session_id
        req = urllib.request.Request(self.url, data=data, headers=headers, method="POST")
        t0 = time.perf_counter()
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                raw = resp.read()
                status = resp.status
                sid = resp.headers.get("Mcp-Session-Id")
                ctype = resp.headers.get("Content-Type", "")
        except urllib.error.HTTPError as e:
            raw, status, sid, ctype = e.read(), e.code, None, e.headers.get("Content-Type", "")
        ms = (time.perf_counter() - t0) * 1000
        if sid:
            self.session_id = sid
        parsed = None
        if raw and ctype.startswith("application/json"):
            try:
                parsed = json.loads(raw)
            except json.JSONDecodeError:
                pass
        elif raw and ctype.startswith("text/event-stream"):
            for line in raw.decode("utf-8", "replace").splitlines():
                if line.startswith("data:"):
                    try:
                        parsed = json.loads(line[5:].strip())
                    except json.JSONDecodeError:
                        pass
        return status, parsed, raw, ms

    def request(self, method: str, params: dict | None = None):
        msg = {"jsonrpc": "2.0", "id": self.next_id, "method": method}
        self.next_id += 1
        if params is not None:
            msg["params"] = params
        return self.post(msg)

    def notify(self, method: str, params: dict | None = None):
        msg = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            msg["params"] = params
        return self.post(msg)


def step(label: str, status: int, ms: float, ok: bool, detail: str = "") -> bool:
    mark = "PASS" if ok else "FAIL"
    print(f"{mark:4} {label:<28} HTTP {status:<3} {ms:7.1f} ms  {detail}")
    return ok


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--url", default=os.environ.get("CLOUD_BRIDGE_URL", "http://127.0.0.1:30201/mcp"))
    p.add_argument("--timeout", type=float, default=30.0)
    p.add_argument("--file", default="hello.txt", help="file name for read_file")
    g = p.add_mutually_exclusive_group()
    g.add_argument("--no-auth", action="store_true", help="expect rejection: no headers at all")
    g.add_argument("--no-cf", action="store_true", help="expect rejection: no Access headers")
    g.add_argument("--no-key", action="store_true", help="expect rejection: no X-Bridge-Key")
    args = p.parse_args(argv)

    mode = "no-auth" if args.no_auth else "no-cf" if args.no_cf else "no-key" if args.no_key else "full"
    c = Client(args.url, build_headers(mode), args.timeout)
    print(f"target {args.url}  mode={mode}")

    init_params = {"protocolVersion": "2025-06-18", "capabilities": {},
                   "clientInfo": {"name": "cloud-bridge-smoke", "version": "0.1"}}
    try:
        status, parsed, raw, ms = c.request("initialize", init_params)
    except (urllib.error.URLError, TimeoutError, OSError) as e:
        print(f"FAIL connect: {e}")
        return 1

    if mode != "full":
        ok = status in (401, 403)
        step("rejected without auth", status, ms, ok, raw[:80].decode("utf-8", "replace").replace("\n", " "))
        return 0 if ok else 1

    ok = status == 200 and parsed and "result" in parsed
    all_ok = step("initialize", status, ms, ok,
                  f"server={parsed['result'].get('serverInfo', {}).get('name')} "
                  f"protocol={parsed['result'].get('protocolVersion')}" if ok else raw[:200].decode("utf-8", "replace"))
    if not ok:
        return 1

    status, _, _, ms = c.notify("notifications/initialized")
    all_ok &= step("notifications/initialized", status, ms, status in (200, 202, 204))

    status, parsed, raw, ms = c.request("tools/list")
    tools = [t["name"] for t in parsed["result"]["tools"]] if status == 200 and parsed and "result" in parsed else []
    all_ok &= step("tools/list", status, ms, bool(tools), ", ".join(tools))

    def call(name: str, arguments: dict, expect_error: bool = False):
        nonlocal all_ok
        status, parsed, raw, ms = c.request("tools/call", {"name": name, "arguments": arguments})
        res = parsed.get("result") if status == 200 and parsed else None
        text = res["content"][0]["text"] if res and res.get("content") else raw[:200].decode("utf-8", "replace")
        is_err = bool(res and res.get("isError"))
        ok = res is not None and is_err == expect_error
        all_ok &= step(f"tools/call {name}", status, ms, ok, text.replace("\n", " ")[:90])
        return text

    call("echo", {"text": "ping from smoke client"})
    call("time", {})
    call("read_file", {"name": args.file})
    call("read_file", {"name": "../test_echo_server.py"}, expect_error=True)   # path traversal must be refused

    status, parsed, raw, ms = c.request("tools/call", {"name": "unreal_python", "arguments": {}})
    all_ok &= step("unknown tool refused", status, ms, status == 200 and parsed and "error" in parsed)

    print("RESULT", "PASS" if all_ok else "FAIL")
    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main())

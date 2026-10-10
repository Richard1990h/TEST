#!/usr/bin/env python3
"""Phase 1 test server for the cloud bridge: MCP over HTTP (Streamable HTTP
transport) with three harmless tools. Standard library only, no Unreal.

It stands in for cloud_gateway.py so the chain

    cloud session -> .mcp.json (http) -> Cloudflare Access -> Cloudflare Tunnel
    -> cloudflared on the PC -> http://127.0.0.1:30201/mcp (this file)

can be proven end to end before anything touches the editor.

Tools:  echo(text)  time()  read_file(name)   (read_file is confined to --test-dir)

Locks it enforces itself (Cloudflare Access is the lock in front of it):
  * optional gateway key in the X-Bridge-Key header (--key-file or BRIDGE_TEST_KEY)
  * request body cap 1 MB, reply cap for file contents
  * binds to 127.0.0.1 only; refuses browser Origins that are not localhost
  * every call is appended to a log: time, caller, tool, args summary, size, ms

Run:    python Tools/CloudBridge/test_echo_server.py --gen-key   (once, writes the key file)
        python Tools/CloudBridge/test_echo_server.py
Stop:   Ctrl+C (kill switch for the PC side; the Access token is the kill switch at the edge)
"""
from __future__ import annotations

import argparse
import datetime as dt
import hmac
import json
import os
import secrets
import sys
import threading
import time
import uuid
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

SERVER_NAME = "cloud-bridge-test-echo"
SERVER_VERSION = "0.1.0"
SUPPORTED_PROTOCOL_VERSIONS = ("2025-06-18", "2025-03-26", "2024-11-05")
MAX_BODY_BYTES = 1 * 1024 * 1024        # plan: gateway max body 1 MB
MAX_FILE_REPLY_BYTES = 256 * 1024       # reply size cap, like the local bridge
MCP_PATH = "/mcp"

HERE = Path(__file__).resolve().parent
DEFAULT_TEST_DIR = HERE / "testdata"
DEFAULT_LOG = HERE / "test_echo_server.log"
DEFAULT_KEY_FILE = HERE / "test_echo_server.key"   # git-ignored; never commit

TOOLS = [
    {
        "name": "echo",
        "description": "Returns the given text unchanged. Proves the round trip cloud -> Cloudflare -> PC.",
        "inputSchema": {
            "type": "object",
            "properties": {"text": {"type": "string", "description": "Text to echo back."}},
            "required": ["text"],
        },
    },
    {
        "name": "time",
        "description": "Returns the PC's current time (UTC and local) and the server uptime.",
        "inputSchema": {"type": "object", "properties": {}},
    },
    {
        "name": "read_file",
        "description": "Reads a text file from the server's test folder (file name only, no paths).",
        "inputSchema": {
            "type": "object",
            "properties": {"name": {"type": "string", "description": "File name inside the test folder, e.g. hello.txt"}},
            "required": ["name"],
        },
    },
]


class State:
    def __init__(self, args: argparse.Namespace):
        self.started = time.time()
        self.calls = 0
        self.test_dir = Path(args.test_dir).resolve()
        self.log_path = Path(args.log_file)
        self.log_lock = threading.Lock()
        self.key = load_key(args)

    def log(self, line: str) -> None:
        stamp = dt.datetime.now().isoformat(timespec="seconds")
        with self.log_lock:
            with self.log_path.open("a", encoding="utf-8") as fh:
                fh.write(f"{stamp} {line}\n")
        print(f"{stamp} {line}", flush=True)


def load_key(args: argparse.Namespace) -> str | None:
    env_key = os.environ.get("BRIDGE_TEST_KEY", "").strip()
    if env_key:
        return env_key
    key_file = Path(args.key_file)
    if key_file.exists():
        key = key_file.read_text(encoding="utf-8").strip()
        if key:
            return key
    return None


def gen_key(path: Path) -> None:
    if path.exists():
        print(f"key file already exists, not overwriting: {path}")
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(secrets.token_urlsafe(32) + "\n", encoding="utf-8")
    try:
        os.chmod(path, 0o600)
    except OSError:
        pass
    print(f"wrote new gateway key to {path}")
    print("put the same value into the cloud environment's secrets as BRIDGE_TEST_KEY. Never commit it, never paste it in chat.")


# ----------------------------------------------------------------------------- tools

def tool_echo(state: State, args: dict) -> str:
    text = args.get("text")
    if not isinstance(text, str):
        raise ValueError("'text' must be a string")
    return text


def tool_time(state: State, args: dict) -> str:
    now = dt.datetime.now(dt.timezone.utc)
    return json.dumps(
        {
            "utc": now.isoformat(timespec="seconds"),
            "local": now.astimezone().isoformat(timespec="seconds"),
            "epoch": int(now.timestamp()),
            "uptime_s": int(time.time() - state.started),
            "calls_served": state.calls,
        },
        indent=2,
    )


def tool_read_file(state: State, args: dict) -> str:
    name = args.get("name")
    if not isinstance(name, str) or not name:
        raise ValueError("'name' must be a non-empty string")
    if name != Path(name).name or name in (".", ".."):
        raise ValueError("file name only, no directories")
    target = (state.test_dir / name).resolve()
    if target.parent != state.test_dir:
        raise ValueError("file name only, no directories")
    if not target.is_file():
        raise FileNotFoundError(f"no such file in test folder: {name}")
    data = target.read_bytes()
    truncated = len(data) > MAX_FILE_REPLY_BYTES
    text = data[:MAX_FILE_REPLY_BYTES].decode("utf-8", errors="replace")
    if truncated:
        text += f"\n\n[truncated: file is {len(data)} bytes, reply cap is {MAX_FILE_REPLY_BYTES}]"
    return text


TOOL_IMPL = {"echo": tool_echo, "time": tool_time, "read_file": tool_read_file}


# ----------------------------------------------------------------------------- JSON-RPC

def rpc_result(req_id, result):
    return {"jsonrpc": "2.0", "id": req_id, "result": result}


def rpc_error(req_id, code: int, message: str, data=None):
    err = {"code": code, "message": message}
    if data is not None:
        err["data"] = data
    return {"jsonrpc": "2.0", "id": req_id, "error": err}


def summarize_args(args: dict) -> str:
    s = json.dumps(args, ensure_ascii=False, separators=(",", ":"))
    return s if len(s) <= 120 else s[:117] + "..."


def handle_message(state: State, msg: dict, caller: str) -> dict | None:
    """Returns a JSON-RPC response, or None for notifications."""
    if not isinstance(msg, dict) or msg.get("jsonrpc") != "2.0":
        return rpc_error(None, -32600, "invalid request")
    method = msg.get("method")
    req_id = msg.get("id")
    params = msg.get("params") or {}

    if method is None:
        return None  # a response from the client; nothing to do
    if req_id is None:
        # notification (e.g. notifications/initialized, notifications/cancelled)
        return None

    if method == "initialize":
        requested = params.get("protocolVersion")
        version = requested if requested in SUPPORTED_PROTOCOL_VERSIONS else SUPPORTED_PROTOCOL_VERSIONS[0]
        client = params.get("clientInfo") or {}
        state.log(f"initialize caller={caller} client={client.get('name')}/{client.get('version')} protocol={version}")
        return rpc_result(req_id, {
            "protocolVersion": version,
            "capabilities": {"tools": {"listChanged": False}},
            "serverInfo": {"name": SERVER_NAME, "version": SERVER_VERSION},
            "instructions": "Test server for the cloud bridge. Tools: echo, time, read_file (test folder only).",
        })
    if method == "ping":
        return rpc_result(req_id, {})
    if method == "tools/list":
        return rpc_result(req_id, {"tools": TOOLS})
    if method == "tools/call":
        name = params.get("name")
        args = params.get("arguments") or {}
        impl = TOOL_IMPL.get(name)
        if impl is None:
            state.log(f"tools/call caller={caller} tool={name!r} REFUSED unknown tool")
            return rpc_error(req_id, -32602, f"unknown tool: {name}")
        t0 = time.perf_counter()
        try:
            text = impl(state, args if isinstance(args, dict) else {})
            is_error = False
        except Exception as exc:  # tool errors travel inside the result, per MCP
            text = f"{type(exc).__name__}: {exc}"
            is_error = True
        ms = (time.perf_counter() - t0) * 1000
        state.calls += 1
        state.log(f"tools/call caller={caller} tool={name} args={summarize_args(args)} "
                  f"result_bytes={len(text.encode('utf-8'))} error={is_error} ms={ms:.1f}")
        return rpc_result(req_id, {"content": [{"type": "text", "text": text}], "isError": is_error})
    if method in ("resources/list", "resources/templates/list"):
        return rpc_result(req_id, {"resources": []} if method == "resources/list" else {"resourceTemplates": []})
    if method == "prompts/list":
        return rpc_result(req_id, {"prompts": []})
    return rpc_error(req_id, -32601, f"method not found: {method}")


# ----------------------------------------------------------------------------- HTTP

class Handler(BaseHTTPRequestHandler):
    server_version = f"{SERVER_NAME}/{SERVER_VERSION}"
    protocol_version = "HTTP/1.1"
    state: State  # set on the class by main()

    def log_message(self, fmt, *args):  # quiet the default access log; we write our own
        pass

    # -- helpers
    def caller(self) -> str:
        ip = self.headers.get("CF-Connecting-IP") or self.client_address[0]
        cid = self.headers.get("CF-Access-Client-Id")
        return f"{ip}" + (f" access_client={cid[:8]}..." if cid else "")

    def send_json(self, status: int, payload, extra_headers: dict | None = None) -> None:
        body = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        for k, v in (extra_headers or {}).items():
            self.send_header(k, v)
        self.end_headers()
        self.wfile.write(body)

    def send_empty(self, status: int) -> None:
        self.send_response(status)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def deny(self, status: int, why: str) -> None:
        self.state.log(f"DENY {status} caller={self.caller()} {self.command} {self.path} {why}")
        self.send_json(status, {"error": why})

    def checks(self) -> bool:
        """Origin, path, key. Returns True when the request may proceed."""
        origin = self.headers.get("Origin")
        if origin:
            host = origin.split("://", 1)[-1].split(":", 1)[0].lower()
            if host not in ("localhost", "127.0.0.1", "[::1]"):
                self.deny(HTTPStatus.FORBIDDEN, f"origin not allowed: {origin}")
                return False
        if self.state.key is not None:
            given = self.headers.get("X-Bridge-Key", "")
            if not hmac.compare_digest(given.encode("utf-8"), self.state.key.encode("utf-8")):
                self.deny(HTTPStatus.UNAUTHORIZED, "missing or wrong X-Bridge-Key")
                return False
        return True

    # -- verbs
    def do_GET(self):
        if self.path == "/health":
            self.send_json(HTTPStatus.OK, {
                "ok": True, "server": SERVER_NAME, "version": SERVER_VERSION,
                "key_required": self.state.key is not None,
                "uptime_s": int(time.time() - self.state.started), "calls_served": self.state.calls,
            })
            return
        if self.path == MCP_PATH:
            if not self.checks():
                return
            # No server-initiated stream in this test server (allowed by the spec).
            self.send_empty(HTTPStatus.METHOD_NOT_ALLOWED)
            return
        self.deny(HTTPStatus.NOT_FOUND, "not found")

    def do_DELETE(self):
        if self.path != MCP_PATH:
            self.deny(HTTPStatus.NOT_FOUND, "not found")
            return
        if not self.checks():
            return
        self.state.log(f"session closed caller={self.caller()} session={self.headers.get('Mcp-Session-Id')}")
        self.send_empty(HTTPStatus.OK)

    def do_POST(self):
        if self.path != MCP_PATH:
            self.deny(HTTPStatus.NOT_FOUND, "not found")
            return
        if not self.checks():
            return
        try:
            length = int(self.headers.get("Content-Length") or 0)
        except ValueError:
            self.deny(HTTPStatus.BAD_REQUEST, "bad Content-Length")
            return
        if length > MAX_BODY_BYTES:
            self.deny(HTTPStatus.REQUEST_ENTITY_TOO_LARGE, f"body over {MAX_BODY_BYTES} bytes")
            return
        raw = self.rfile.read(length) if length else b""
        try:
            payload = json.loads(raw.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError):
            self.send_json(HTTPStatus.BAD_REQUEST, rpc_error(None, -32700, "parse error"))
            return

        messages = payload if isinstance(payload, list) else [payload]
        caller = self.caller()
        responses = [r for r in (handle_message(self.state, m, caller) for m in messages) if r is not None]

        extra = {}
        if any(isinstance(m, dict) and m.get("method") == "initialize" for m in messages):
            extra["Mcp-Session-Id"] = uuid.uuid4().hex

        if not responses:
            self.send_empty(HTTPStatus.ACCEPTED)   # notifications / responses only
            return
        if isinstance(payload, list):
            self.send_json(HTTPStatus.OK, responses, extra)
        else:
            self.send_json(HTTPStatus.OK, responses[0], extra)


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--host", default="127.0.0.1", help="bind address (keep 127.0.0.1; cloudflared connects locally)")
    p.add_argument("--port", type=int, default=30201)
    p.add_argument("--test-dir", default=str(DEFAULT_TEST_DIR), help="folder read_file may read from")
    p.add_argument("--log-file", default=str(DEFAULT_LOG))
    p.add_argument("--key-file", default=str(DEFAULT_KEY_FILE),
                   help="file holding the X-Bridge-Key value (env BRIDGE_TEST_KEY overrides)")
    p.add_argument("--gen-key", action="store_true", help="create a random key file and exit")
    args = p.parse_args(argv)

    if args.gen_key:
        gen_key(Path(args.key_file))
        return 0

    state = State(args)
    if not state.test_dir.is_dir():
        print(f"test dir does not exist: {state.test_dir}", file=sys.stderr)
        return 2
    if args.host not in ("127.0.0.1", "localhost", "::1"):
        print("WARNING: binding to a non-loopback address. The plan says nothing listens on the internet.", file=sys.stderr)
    if state.key is None:
        print("WARNING: no gateway key configured (run with --gen-key first, or set BRIDGE_TEST_KEY). "
              "Only Cloudflare Access protects this server.", file=sys.stderr)

    Handler.state = state
    httpd = ThreadingHTTPServer((args.host, args.port), Handler)
    httpd.daemon_threads = True
    state.log(f"START {SERVER_NAME} {SERVER_VERSION} on http://{args.host}:{args.port}{MCP_PATH} "
              f"test_dir={state.test_dir} key_required={state.key is not None} log={state.log_path}")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        state.log("STOP")
        httpd.server_close()
    return 0


if __name__ == "__main__":
    sys.exit(main())

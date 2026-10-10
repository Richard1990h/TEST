# Cloud bridge - Phase 1 test kit

Proves, on a throw-away project and without Unreal, that a Claude Code **cloud** session
(claude.ai/code) can call MCP tools served from the owner's PC through Cloudflare:

```
cloud session  -> .mcp.json (type http, headers from the environment's secrets)
               -> bridge-test.<domain>  (Cloudflare DNS -> Tunnel; Access policy: service token only)
               -> cloudflared on the PC (outbound connection, no open port)
               -> http://127.0.0.1:30201/mcp  = Tools/CloudBridge/test_echo_server.py
```

Plan: `CLOUD_BRIDGE_PLAN_2026-10-10.md` (owner's copy). This folder is Phase 1 only. Phase 2 replaces
the echo server with `cloud_gateway.py` in front of the real ClaudeBridge and uses a second tunnel.

## Files

| File | What |
|------|------|
| `test_echo_server.py` | MCP over HTTP (Streamable HTTP) server, stdlib only. Tools `echo`, `time`, `read_file` (confined to `testdata/`). Checks `X-Bridge-Key`, caps body at 1 MB and file replies at 256 KB, refuses non-localhost browser Origins, logs every call. |
| `mcp_smoke_client.py` | Stdlib client: initialize, tools/list, calls each tool, times each round trip, checks that missing auth is rejected. Works on the PC and inside the cloud session. |
| `config.yml.example` | cloudflared tunnel config template. Copy to `config.yml` (git-ignored). |
| `testdata/hello.txt` | The file `read_file` serves. |
| `../../.mcp.json` | Cloud side: the remote MCP server entry. URL and headers come from environment variables, so no domain, token or key is in the repo. |

Git-ignored on purpose: `config.yml`, `*.key`, `*.log`. The tunnel credentials JSON lives in
`%USERPROFILE%\.cloudflared\`, outside the repo.

## Verified so far (in a cloud container, local loopback, 10 Oct 2026)

- `mcp_smoke_client.py`: all steps pass; without the key the server answers 401; path traversal
  (`../x`) and unknown tools are refused.
- curl: foreign `Origin` -> 403, 2 MB body -> 413, `GET /mcp` -> 405.
- Official MCP Python SDK 2.2 Streamable HTTP client: initialize, list, call all work.
- Claude Code CLI 2.1.296 reading this repo's `.mcp.json`: `claude mcp list` reports
  `cloud-bridge-test ... Connected`, with `${CLOUD_BRIDGE_URL:-default}` expanded and the
  `X-Bridge-Key` header delivered (the server would have answered 401 otherwise).

Not yet verified (needs the owner's PC and domain): Cloudflare Tunnel, Access service-token policy,
and the cloud environment's network allowlist. That is Phase 0/1 below.

## Phase 0 - owner, once (about 15 min)

1. Pick a domain whose nameservers are at Cloudflare. Test host: `bridge-test.<domain>`.
2. Cloudflare Zero Trust (free plan): <https://one.dash.cloudflare.com> -> create the team once.
3. On the PC (PowerShell):
   ```powershell
   winget install Cloudflare.cloudflared
   cloudflared tunnel login                      # browser opens; pick the domain
   cloudflared tunnel create lo-bridge-test      # prints the tunnel UUID + credentials path
   cloudflared tunnel route dns lo-bridge-test bridge-test.<domain>
   ```
4. Copy `config.yml.example` to `config.yml`, fill in `<TUNNEL_UUID>`, `<you>`, `<domain>`.
5. Zero Trust -> Access -> **Service Auth** -> Service Tokens -> Create: name `cloud-bridge-test`.
   Store Client ID and Client Secret in a password manager. They are shown once.
6. Zero Trust -> Access -> **Applications** -> Add -> Self-hosted:
   domain `bridge-test.<domain>`, session duration irrelevant, one policy:
   action **Service Auth**, include **Service Token** = `cloud-bridge-test`. No other policy.
7. Optional, recommended: Security -> WAF -> Rate limiting rule for `bridge-test.<domain>`, 60 requests / min.

## Phase 1 - run the test

On the PC, two terminals:

```powershell
python Tools\CloudBridge\test_echo_server.py --gen-key      # once; writes test_echo_server.key
python Tools\CloudBridge\test_echo_server.py                # terminal 1
cloudflared tunnel --config Tools\CloudBridge\config.yml run lo-bridge-test   # terminal 2
```

Check from the PC (replace the placeholders; secrets come from the password manager, not from a file in the repo):

```powershell
# a) server alone
$env:BRIDGE_TEST_KEY = Get-Content Tools\CloudBridge\test_echo_server.key
python Tools\CloudBridge\mcp_smoke_client.py                              # expect RESULT PASS
python Tools\CloudBridge\mcp_smoke_client.py --no-auth                    # expect 401

# b) through Cloudflare
$env:CLOUD_BRIDGE_URL = "https://bridge-test.<domain>/mcp"
$env:CF_ACCESS_CLIENT_ID = "<client id>"; $env:CF_ACCESS_CLIENT_SECRET = "<client secret>"
python Tools\CloudBridge\mcp_smoke_client.py --no-cf                      # expect 403 from Cloudflare
python Tools\CloudBridge\mcp_smoke_client.py --no-key                     # expect 401 from the server
python Tools\CloudBridge\mcp_smoke_client.py                              # expect RESULT PASS, note the ms
```

Cloud side (claude.ai/code):

1. Environment -> Edit -> **Network access**: either a level that allows `bridge-test.<domain>`, or
   add it under Allowed domains (keep package managers allowed). The "Trusted" level may block it.
2. Environment -> Edit -> **Network secrets** (or environment variables):
   `CLOUD_BRIDGE_URL=https://bridge-test.<domain>/mcp`, `CF_ACCESS_CLIENT_ID`,
   `CF_ACCESS_CLIENT_SECRET`, `BRIDGE_TEST_KEY`. A new session picks them up.
3. Start a session on this repo. Approve the project MCP server `cloud-bridge-test` when asked.
   Then: list tools, call `echo`, `time`, `read_file hello.txt`; run
   `python3 Tools/CloudBridge/mcp_smoke_client.py` for timings.
4. Kill switch test: revoke or delete the service token in Zero Trust, then call a tool again
   from the cloud -> Cloudflare 403. Stop `test_echo_server.py` -> cloudflared returns 502.

## Pass criteria

- Tools work from the cloud session; round-trip times recorded.
- Without the service token: Cloudflare 403. Without the key: server 401.
- `Tools/CloudBridge/test_echo_server.log` shows every call with caller, tool, args, size, ms.
- Kill switch works (token revoked -> 403; server stopped -> no access).

## Never

- Commit `config.yml`, `*.key`, the tunnel credentials JSON, or the service token.
- Paste the token or key into a chat. They go into the cloud environment's secrets only.
- Bind the server to anything but `127.0.0.1`.

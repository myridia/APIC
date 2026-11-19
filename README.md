<img src="APIC.svg" alt="APIC" width="120">
# APIC
APIC API: Application Programming Interface Connector - API (for QuickBooks Desktop)

APIC is a small .NET 10 HTTP API that forwards qbXML to a QuickBooks Desktop
company file through the QBXMLRP2 COM request processor, on the same Windows
machine that runs QuickBooks. No Intuit SDK or interop DLLs are required —
the COM component is registered by QuickBooks itself, and the app talks to it
by late binding.

## Requirements

- Windows machine with QuickBooks Desktop installed and the company file open (or openable)
- .NET 10 SDK (to build)
- A QuickBooks company file (`.qbw`) — set its full path in `apic_config.toml`
  (next to the exe; override with `--config <path>` or `APIC_CONFIG=<path>`)

## Run it standalone (console)

Double-click `apic.exe` — the console window stays open, shows the log, and
opens a HTTP listener, exactly as during development. Stop with `Ctrl+C` or
by closing the window.

## Run it as a service (production)

```cmd
sc.exe create APIC binPath= "C:\path\to\apic.exe --service" start= auto obj= ".\serviceaccount" password= "<the service account password>"
sc.exe failure APIC reset= 86400 actions= restart/60000/restart/60000/restart/60000
sc.exe start APIC
```

- `... --service` is required — without it the exe runs as a console app and
  a service installation would fail its start dispatcher timeout.
- Run it as the account that owns the QuickBooks file and the share — the grant
  lives in the company file, but the account needs share access to UNC path.
  Set `quickbooks_file` in `apic_config.toml` to the UNC path, e.g.
  `\\server\share\Path\To\Company File.qbw`.
- Logs go to `logs\apic_server.txt` next to the exe (day-rolled).
- `sc.exe stop APIC` stops the service, `sc.exe query APIC` shows state.
- In service mode there is no console window; in console mode everything is
  identical to before.

## Build

```bash
./ask.sh    # task 1 — publish and copy apic.exe to the repo root
./build.sh  # same, without the menu
```

Produces a single-file `apic.exe` at the repository root (gitignored).

## Deploy

Set the `APIC_TOKEN` environment variable (user, machine, or service-owned — never in
the repo) to require an `X-APIc-Token: <token>` header on all QuickBooks endpoints
(`GET /`, `POST /`, `GET /test/`). Any request without a matching header gets a 401
`{"error":"unauthorized"}`. If `APIC_TOKEN` is unset, the token from `apic_config.toml`
(`token = "..."`) is used; if that too is empty, the gateway stays open. Env var wins
over the toml value. Set it before exposing the port beyond a trusted network. The
toml file itself is gitignored — keep it that way. On first run the exe now generates
and prints a random 64-hex token and writes it into the default config — read it from
the console, then set the same value in Sapir's `APIC_TOKEN` (env var wins over the
toml value; set either to empty to run open).

`./ask.sh` → task 6. On first run it asks for the host/IP, SSH user and
password, saves them to `.env` (gitignored, never committed), then uploads
`apic.exe` to the host's Desktop and signs it there with `MySPC.pfx`.
The repo contains no host names or credentials.

## HOW to sign the apic with powershell 

QuickBooks binds its automatic-login grant to the **signed identity** of the
exe. An unsigned or differently-signed `apic.exe` fails with
`0x8004041D` ("not allowed to log into this QuickBooks company data file
automatically") even though the code is identical. Sign every rebuilt exe.

Releases built by CI are **unsigned** — sign the exe yourself (below), or build
from source and sign.

### Make the Root Auth
```
makecert -r -pe -n "CN=My CA" -ss CA -a sha256 -cy authority -sky signature -sv MyCA.pvk MyCA.cer
certutil -user -addstore Root MyCA.cer
```

Keep `MyCA.pvk` and `MySPC.pfx` private — the `.gitignore` excludes `*.pfx` and `*.pvk`. Never commit them.

## Make a cert
```
makecert -pe -n "CN=My SPC" -a sha256 -cy end -sky signature -ic MyCA.cer -iv MyCA.pvk -sv MySPC.pvk MySPC.cer
pvk2pfx -pvk MySPC.pvk -spc MySPC.cer -pfx MySPC.pfx
```

## Sing it 
```
signtool sign /v /f MySPC.pfx /t http://timestamp.digicert.com /fd sha256 apic.exe
signtool verify /pa "apic.exe"
```

If `signtool verify /pa` reports the chain as untrusted, install the CA into
the machine stores:

```
certutil -addstore Root MyCA.cer
certutil -addstore TrustedPublisher MyCA.cer
```

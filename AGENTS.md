# AGENTS.md — APIC

## What this is
APIC (Application Programming Interface Connector) — a .NET API server that
bridges an application to **QuickBooks Desktop** via the QuickBooks QBXMLRP2
COM request processor (in-process, on the Windows machine that hosts the
company file). Not QuickBooks Online, not the Web Connector.

## Stack
- C# / .NET 10 (`net10.0-windows`), WatsonWebserver
- Talks to QuickBooks via **late-bound COM** (`dynamic` dispatch against
  `QBXMLRP2.RequestProcessor`, fallback `QBXMLRP2.RequestProcessor.1`) — **no
  Intuit interop DLLs are referenced or shipped**; nothing from the QB SDK is
  needed, QuickBooks Desktop itself registers the COM component
- Serilog, Newtonsoft.Json, Tommy (TOML), Watson

## Requirements
- Windows machine with QuickBooks Desktop installed and the company file
  open (or openable) — the COM component comes with QuickBooks
- .NET 10 SDK for building
- A self-signed cert for **signing the exe** (see README.md): QuickBooks binds
  the `APIc` automatic-login grant to the signed identity; an unsigned exe
  fails with `0x8004041D` ("not allowed to log into this QuickBooks company
  data file automatically")

## Configuration
Config lives as `apic_config.toml` **next to the exe** (`AppContext.BaseDirectory`),
self-contained with the binary and `logs/`. On first run, if the exe-folder config is
missing but a legacy `%USERPROFILE%\Documents\apic_config.toml` exists, it is copied
over automatically; otherwise a default is created. Override the location with:

- `--config <path>` argument, or
- `APIC_CONFIG=<path>` environment variable.

Fill in:

```toml
title = "APIc"
quickbooks_file = "C:\\Path\\To\\company.qbw"
port = 8000
ipaddress = ""
auto_ipaddress = true
logfile = "logs/apic_server.txt"
host = ""
token = "<generated at first run>"
```

Set `quickbooks_file` to the full path of your `.qbw` before first sign-in — for a
Windows service, point it at a UNC path (`\\\\server\\share\\...`), mapped drive
letters don't exist for services.

## Build (Windows)

```bash
./ask.sh   # task 1 — publish and copy apic.exe to the repo root
./build.sh # same, without the menu
```

`dotnet publish` produces a single-file `apic.exe`
(`apic/bin/Release/net10.0-windows/win-x64/publish/apic.exe`); both wrappers also copy
it to the repository root as `./apic.exe` (gitignored).
`PublishTrimmed` is deliberately off — trimmed builds disable .NET's built-in COM
interop and break the QuickBooks COM calls.

## Structure
- `apic/apic.csproj` — project file (no Intuit references)
- `apic/Program.cs` — entry point and gateway logic; COM access via
  `CreateRequestProcessor()` (late binding), marshalled onto a dedicated STA
  thread per call (`RunOnSta`); `QbFileOpenMultiUser = 2`. One **persistent
  session** (`Ensure_session()`) is kept open across requests and reused —
  on any QuickBooks failure it's marked dead (`Mark_session_dead()`) and the
  next request reopens it lazily. All QuickBooks calls (GET
  `/?xml|base64=`, POST `/`, `/test/`) are serialized through one
  `SemaphoreSlim` gate (`Gate`, 600 s timeout) — requests wait for the gate,
  requests that outlast it get a "QuickBooks is busy" JSON. Session is closed
  on shutdown (console `ProcessExit` / service `StopAsync`).
- `ask.sh` + `ask.d/` — task menu (1 Build, 2 Run, 3 Restore, 4 List packages,
  5 Clean, 6 Deploy). Deploy uploads `apic.exe` to the host and signs it; the
  first run prompts for host/user/password and saves them to `.env`
- `ask.d/lib.sh` / `ask.d/askpass` — deploy helpers: shared SSH transport
  (sshpass if present, else `SSH_ASKPASS_REQUIRE=force`), interactive `.env`
  setup
- `ask.d/sign.ps1` — remote signtool step (Windows host), queries Windows SDK
  paths itself
- `build.sh` — win-x64 publish one-liner
- `.github/workflows/windows.yml` — CI build + release on push to `main`
  (unsigned; see README)

## Conventions
- `.env` is gitignored and holds the deploy target (`APIC_HOST`, `APIC_USER`,
  `SSHPASS`); it is created interactively by the deploy task. Never commit it,
  and never put hostnames or credentials in the repo files.
- Dual mode: `apic.exe` runs as console (development), `apic.exe --service`
  runs as the Windows service `APIC` (production) — same binary, same code
  path, only the lifetime differs.
- Logs are written both to console and to `logs/apic_server.txt` (day-rolled,
  next to the exe).
- The token does **not** rotate on recompile/redeploy — it lives in the
  persisted `apic_config.toml` (or `APIC_TOKEN`); only deleting/clearing the
  config generates a new one, printed once to the console.
- No comments in code unless asked.
- Builds, verification and `dotnet` invocations are run by the user on the
  target machine — agents only edit files, they never build or run.

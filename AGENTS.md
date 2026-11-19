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
First run creates `apic_config.toml` in the user's My Documents
(`%USERPROFILE%\Documents\apic_config.toml`). Fill in:

```toml
title = "APIc"
quickbooks_file = "C:\\Path\\To\\company.qbw"
port = 8000
ipaddress = ""
auto_ipaddress = true
logfile = "logs/apic_server.txt"
host = ""
```

Set `quickbooks_file` to the full path of your `.qbw` before first sign-in.

## Build (Windows)

```bash
./ask.sh   # task 1 — publish and copy apic.exe to the repo root
./build.sh # same, without the menu
```

`dotnet publish` alone already produces a single-file `apic.exe`
(`apic/bin/Release/net10.0-windows/win-x64/publish/apic.exe`); both wrappers also copy
it to the repository root as `./apic.exe` (gitignored).

## Structure
- `apic/apic.csproj` — project file (no Intuit references)
- `apic/Program.cs` — entry point and gateway logic; COM access via
  `CreateRequestProcessor()` (late binding), marshalled onto a dedicated STA
  thread per call (`RunOnSta`); `QbFileOpenMultiUser = 2`
- `ask.sh` + `ask.d/` — task menu (1 Build, 2 Run, 3 Restore, 4 List packages, 5 Clean)
- `build.sh` — win-x64 publish one-liner
- `.github/workflows/windows.yml` — CI build + release on push to `main`
  (unsigned; see README)

## Conventions
- No comments in code unless asked.
- Builds, verification and `dotnet` invocations are run by the user on the
  target machine — agents only edit files, they never build or run.

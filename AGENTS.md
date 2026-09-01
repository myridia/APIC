# AGENTS.md — APIC

## What this is
APIC (Application Programming Interface Connector) — a .NET API server that bridges the application to QuickBooks Desktop via the QuickBooks Web Connector, plus PostgreSQL/SQL Server/CouchDB data access.

## Stack
- C# / .NET 10.0
- QuickBooks QBXMLRP2 (Web Connector) interop
- Newtonsoft.Json, Npgsql, System.Data.SqlClient, MyCouch.CloudantIAM
- Serilog (logging), Watson, Tommy (TOML)

## Build
```bash
dotnet restore
dotnet publish apic/apic.csproj -c Release -r linux-x64 /p:PublishSingleFile=true /p:PublishTrimmed=true
```

## Run
```bash
apic/bin/Release/net10.0/linux-x64/apic
```

## Structure
- `apic/apic.csproj` — project file and dependencies
- `apic/Program.cs` — main entry point
- `apic/libs/` — QBXMLRP interop DLLs
- `ask.sh` — interactive build/run/restore menu
- `build.sh` — build script

## Conventions
- No comments in code unless asked.
- Verify: `dotnet build apic/apic.csproj`
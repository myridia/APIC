# how_to_service.md — APIC as a Windows Service

`apic.exe` runs in two modes from the same binary:

- **console mode** (no arguments) — interactive window, for development and debugging
- **service mode** (`apic.exe --service`) — runs under the Service Control Manager

The service mode is the production setup: starts at boot, restarts on crash, no window.

## 1. Before registering — prepare the machine

1. Build and deploy on the QuickBooks machine as usual:
   - `./ask.sh` → task 1 (build)
   - `./ask.sh` → task 6 (upload + sign) — the service runs exactly what task 6 uploads
2. Point `apic_config.toml` at the **UNC path** of the company file — services never see
   mapped drive letters (e.g. `Q:\`), only UNC paths:
   ```toml
   quickbooks_file = "\\\\YOUR-DC\\your$\\Path\\To\\Company File.qbw"
   ```
   The service account (see below) must be able to read/write that share.
3. Stop any running console instance of `apic.exe` — port 8000 can only be held once.

## 2. Register the service

```cmd
sc.exe create APIC binPath= "C:\path\to\apic.exe --service" start= auto obj= ".\serviceaccount" password= "<the service account password>"
sc.exe description APIC "APIC - QuickBooks Desktop API bridge"
sc.exe failure APIC reset= 86400 actions= restart/60000/restart/60000/restart/60000
sc.exe start APIC
```

- The spaces after `binPath=` and `start=` are mandatory `sc.exe` syntax.
- `obj= ".\serviceaccount"` runs the service as the account that can reach the QuickBooks
  share — without it, QB can't open the file and `/test/` fails.
- The account password appears in cmd history once — use `Alt+F7` or close the window.
- `failure` adds automatic restart-on-crash (3 attempts, 60 s apart, counter resets after a day).

## 3. Verify

```cmd
sc.exe query APIC
```
Expected: `STATE: 4 RUNNING`. Then hit the test endpoint (e.g. `./ask.sh` → task 25 in the
matching project): `{"testing quickbooks file access":"True"}`.

Logs are written to `logs\apic_server.txt` next to the exe (day-rolled) — in service mode
there is no console to watch.

## 4. Maintenance

- Code change → task 1 → task 6 → `sc.exe stop APIC` → overwrite the exe → `sc.exe start APIC`.
- `sc.exe stop` / `start` / `restart` APIC for maintenance windows.
- `sc.exe delete APIC` removes the service entirely (binary stays on disk).
- To debug interactively: `sc.exe stop APIC`, then run `apic.exe` without arguments —
  stop the service first or port 8000 conflicts.

## 5. What does not change

- QuickBooks automatic-login grant — the grant keys on the signing certificate + app name,
  not on process type; no re-authorization is needed for a service.
- Same binary, same code, same port 8000, same endpoints — `sapir` / `stock` clients unchanged.
- One persistent QuickBooks session per process (created on first QB call, reused afterwards,
  closed on service stop).

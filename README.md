# AR Fast SNTP — Install, Sync, Monitor

Native .NET 8 Windows Service + lightweight WinForms live monitor.

**Primary flow: INSTALL & SYNC → Windows Service RUNNING → dashboard monitors.**
No uninstall/reinstall to change from Monitor Only to clock synchronization.
The installer updates an existing ARFastSNTP service **in place**.

> Clock adjustment can jump the PC date by years. Use the clock-sync mode
> only on an administrator-approved **isolated certification test PC**, not
> a production SCADA/SOE/historian system. NTPv4 unicast does not authenticate
> the time source; use your verified trusted GPS LAN.

## Quick start

1. Download `ARFastSNTP-Windows-x64` from the [latest successful Windows CI](https://github.com/masarray/arintp/actions).
   Extract **all** files to a new folder, not directly from inside the ZIP.
2. Open `dashboard/ARFastSNTP.Dashboard.exe`.
3. Enter your NTP/GPS server IP (default `192.16.1.175`) and year reference (`2026`).
4. Click **INSTALL & SYNC**, approve **one Windows Administrator/UAC prompt**.
   The setup starts automatically, installing the service if absent or
   updating the already-installed monitor service to clock-sync mode.
5. Once the setup reports **RUNNING / LAB CLOCK CORRECTION**, use the dashboard
   to watch SNTP replies, server/PC UTC times and dates, offset, RTT and correction events.
   The green synchronized lamp only follows a **fresh verified SNTP measurement**.

Standalone installation: double-click `ARFastSNTP.Setup.exe`, then **INSTALL & SYNC**.
Or run `install-sync.cmd` for the same configuration. No PowerShell required.
The separate `install-monitor.cmd` is an **optional diagnostic monitor-only** mode.

### Already installed Monitor Only?

There is no need to remove it. Just use **INSTALL & SYNC** with the desired
server IP; setup stops the owned service, updates its SCM ImagePath, restarts,
and verifies that it is running. If a configuration change fails, the setup
tries to restore the previous mode and shows the exact error.

### Dashboard CONNECT vs INSTALL & SYNC

**CONNECT** creates a temporary unprivileged *monitor* process; it never
changes Windows time. **INSTALL & SYNC** starts/updates the persistent
Windows Service with time correction enabled. The dashboard is read-only;
the worker owns corrections and runs after the GUI closes or Windows reboots.

### Reliability notes

- First suspicious offsets require three coherent NTP request/reply samples.
- Stable connections use adaptive low-rate polling, a monotonic step cooldown,
  and bounded recovery/backoff for errors and NTP KoD codes.
- A bounded 16-event ring and atomic JSON status snapshots prevent unbounded
  memory growth. Only the service modifies Windows time.
- Broadcast is UNKNOWN unless a separate broadcast receiver is implemented.
- The server clock in the dashboard is an **estimated** UTC time, not a
  separate authenticated GPS reference.
- If another time-setting service (e.g. W32Time/Meinberg) is running, setup
  **warns** about the conflict but never secretly stops it. Arrange test-PC
  time-service ownership under your normal maintenance procedure.
- Windows setup diagnostics: `%ProgramData%\ARFastSNTP\setup.log`.
- Run `uninstall.cmd` or use the setup's **REMOVE SERVICE** control to remove.
  Existing correction/status evidence in ProgramData is retained.

## Development status

GitHub CI compiles worker, dashboard, graphical setup, deterministic SNTP
regressions and worker lifecycle; it does not step the CI runner's clock.
Actual GPS communication, Windows Service reconfiguration and time correction
still require acceptance testing on the authorized test PC. No automatic merge
to main or production deployment.

[Acceptance procedure](docs/LAB-ACCEPTANCE.md) ·
[Windows Actions](https://github.com/masarray/arintp/actions)

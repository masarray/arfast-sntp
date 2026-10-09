# AR Fast SNTP — Install, Sync, Monitor

**License: GPL-2.0-or-later.** Full text: [LICENSE](LICENSE).

## Windows Inno smart installer (candidate)

The Windows CI now builds `ARFastSNTP-Setup-Windows-x64.exe`. The
single Inno Setup wizard configures trusted SNTP IP and NTP reference
year, installs or upgrades the Windows Service, verifies RUNNING,
and offers to open Avalonia Studio. Closing Studio does not stop sync.

Upgrades detect the owned service and preserve its IP/year. Before
replacing binaries, setup saves the previous executable and service
configuration for automatic recovery if installation is interrupted or
activation fails. Existing ProgramData logs and correction history remain.

Inno requires **one Windows Administrator approval**, with no
PowerShell or separate graphical setup. For silent installations on
authorized isolated test PCs:
`ARFastSNTP-Setup-Windows-x64.exe /VERYSILENT /SERVER=192.16.1.175 /YEAR=2026`.
Use `/LABSYNC=0` for diagnostic monitor-only mode.

**Important:** default sync mode is for an approved isolated certification
PC; do not deploy on active SCADA/SOE/historian systems. Real upgrade and
rollback acceptance tests are still pending.



## Windows-only Avalonia Studio preview

The new `src/ARFastSNTP.Studio` is a Windows Avalonia shell using pinned
design tokens from `masarray/arssyut`. The latest successful Windows artifact
contains `studio/ARFastSNTP.Studio.exe` alongside the existing worker and
WinForms fallback. Open Studio to inspect **real service status**: two UTC
clocks with dates, validated SNTP health, offset/RTT, request counts and
recent correction events. Use INSTALL & SYNC to invoke the existing C# setup.

This is **not yet** the final Inno installer or a functional multi-server
manager. The current engine has one server: Studio deliberately disables
Add Server until the canonical multi-peer worker lands. No fake live rows or
nonfunctional enable toggles are presented.

Design provenance and licensing: [DESIGN-TOKEN-PROVENANCE.md](docs/DESIGN-TOKEN-PROVENANCE.md).
Windows-only migration sequence: [WINDOWS-PRODUCT-PLAN.md](docs/WINDOWS-PRODUCT-PLAN.md).



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

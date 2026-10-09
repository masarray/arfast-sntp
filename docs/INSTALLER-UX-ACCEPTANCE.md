# AR Fast SNTP — installer and compact UI acceptance matrix

Scope: Windows x64 only. This document is the release gate for PR #1.
**Do not merge/release to production SCADA based solely on green CI.**

## Installer ownership and migration

The canonical Windows service is `ARFastSNTP`; its process executable is
`ARFastSNTP.exe`. The installer has an explicit allowlist, not a wildcard
process-name cleanup. Known installer-owned paths:

| Legacy origin | Expected executable location | Expected action |
|---|---|---|
| PowerShell monitor/lab installer | `%ProgramFiles%\ARFastSNTP\worker\ARFastSNTP.exe` | Stop, upgrade same SCM service, verify |
| WinForms C# setup | `%ProgramFiles%\ARFastSNTP\worker\ARFastSNTP.exe` | Stop, upgrade same SCM service, verify |
| Early root installation | `%ProgramFiles%\ARFastSNTP\ARFastSNTP.exe` | Stop, migrate ImagePath, verify |
| 32-bit Program Files install | `%ProgramFiles(x86)%\ARFastSNTP\worker\ARFastSNTP.exe` | Stop, migrate ImagePath, verify |
| Inno 0.9.0 and newer | `%ProgramFiles%\ARFastSNTP\worker\ARFastSNTP.exe` | In-place upgrade, preserve IP/year |
| Any other service/ImagePath | Unrecognized | Refuse to modify, report reason |

A loose binary started manually from `C:\arintp\worker` is NOT a
known installer-owned service and is **not automatically deleted/killed**.
Identically named non-project software is never adopted. Third-party
Windows Time, Meinberg and Siemens services are not silently stopped or
uninstalled; engineers must avoid competing time authorities.

## Rollback contract

Before copying new worker binaries, setup takes a recovery snapshot:
- exact previous SCM ImagePath with server and mode flags;
- exact previous worker executable (from the recognized old location);
- SCM startup type and whether it was running.

Installer then stops only the owned service, copies new binaries, registers
or updates the same service, starts it, and checks `RUNNING`. After successful
activation, stale WinForms desktop shortcuts are deleted **only** if their
resolved TargetPath matches a known old ARFastSNTP dashboard path.

If installation is cancelled or activation fails, attempt to restore previous
worker executable, ImagePath, SCM startup type **and** running/stopped state.
If restoration fails, retain the snapshot in
`%ProgramData%\ARFastSNTP\installer-recovery` and fail loudly.
Never delete the snapshot before a verified transition. No third-party
service modification or orphaned dual SNTP worker is intended.

## CI gates (automated, no clock mutation)

1. Build all .NET projects with warnings as errors.
2. SNTP protocol, coherent sample, clock-discontinuity regression suite.
3. Inno helper self-test of current, historical Program Files, x86, root,
   invalid executable-suffix and foreign ImagePaths.
4. Worker monitor lifecycle against localhost without NTP or step privileges.
5. Avalonia XAML build; compact layout has no year/install action or fake
   disabled Add Server control.
6. Inno Setup compiler produces one executable and packaging includes GPL.
7. No CI test installs a service or modifies GitHub runner clock.

## Physical Windows acceptance (not completed by GitHub CI)

- Clean install: one UAC, server IP entry, automatic-start service,
  valid reply, correction measured on isolated certification PC.
- Upgrade running PowerShell release, then WinForms release, then Inno 0.9.0:
  previous service stops, no duplicate remains, installed service RUNNING,
  previous server IP/year reused, Studio opens exactly once.
- Upgrade existing STOPPED / Manual-start service: preflight does not
  mis-detect it as a clean install; rollback restores previous stopped state.
- Simulate a failed worker activation: verify previous worker and SCM flags
  restored and log retained.
- Cancel setup while unpacking: verify old service is recovered.
- Foreign service using ARFastSNTP name: setup refuses adoption.
- Close Studio: worker/service continues syncing. Reboot: service starts.
- Clock jump 2023 -> 2026: monitor avoids time-of-day-only false success,
  the worker independently validates a correction, Studio recovers its
  freshness indicator from monotonic snapshot updates.
- UI visual/keyboard/DPI at Windows 100, 125, 150 and 200 percent: no text
  clipping, labels overlap, keyboard traps or hidden controls.
- Manual and background WinForms processes are closed by a user-approved
  Windows application shutdown; no unrelated process is killed.

## Studio contract

PC Time + Sync Status are first-class. The table displays only measured
server IP, reply health, stratum, offset, estimated server UTC and
broadcast availability. Broadcast = `—` until a dedicated broadcast
receiver exists. History is collapsed by default; a Lucide chevron
expands a bounded list of real worker correction events.

**Multi-server support is a separate canonical worker milestone**.
Do not display functional enable toggles or Add Server until the worker
supports multiple peer health/selection and persisted configuration.

## Source toggle acceptance — service stays installed and RUNNING

- A single approved installer elevation is allowed; each subsequent ON/OFF
  click must **not** launch a helper EXE, ask for UAC, change SCM startup type,
  or stop/restart the ARFastSNTP service.
- Source OFF writes the service-owned HKLM preference, prevents any further
  `SetSystemTime` after acknowledgement, and pauses SNTP polling in the worker.
  While OFF, PC Sync says NTP off, not falsely No reply / Synced.
- Source ON wakes that SAME running worker, clears pre-toggle sample trust,
  and resumes unicast NTP polling. Fresh verified samples are required before
  claiming synchronized.
- ON/OFF preference remains after reboot and worker restart, including when
  Studio is closed. Full uninstall cleans the app-owned preference key.
- Control uses a fixed-size two-byte request/three-byte response protocol and
  an explicit Windows pipe DACL for LocalSystem, Administrators and INTERACTIVE
  Windows logon sessions. Never expose a TCP/HTTP endpoint or generic command.
  Interaction by signed-in users is only appropriate on an authorized isolated
  certification host. Verify the DACL on physical Windows as part of acceptance.
- Two Studio instances remain consistent within the periodic state refresh;
  lost/old worker control pipe disables toggle and produces a clear tooltip.
- DPI 100/125/150/200%, summary 66 DIP tall, switch 34x19 DIP, grid focus
  remains visible. No overflow or unnecessary top-card text.
- SmartScreen installer reputation is solved separately by publisher code
  signing; no attempt to bypass Windows protection is permitted.

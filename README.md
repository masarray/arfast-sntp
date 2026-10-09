# AR Fast SNTP — Windows SNTP Monitor & Lab Sync

Lightweight C# .NET 8 SNTP worker + graphical dashboard. A dedicated, explicitly approved
**lab mode** can correct Windows time. Normal GUI use is safe **MONITOR ONLY**.

## Quick start (no installer, PowerShell, or Administrator required)

1. Open the latest successful [Windows CI artifact](https://github.com/masarray/arintp/actions)
   named **ARFastSNTP-Windows-x64** and **Extract All**.
2. Run **`dashboard/ARFastSNTP.Dashboard.exe`** (not the console worker).
3. Enter **SNTP Server IP** (default `192.16.1.175`), select **YEAR** (NTP-era reference,
   default 2026), then click **CONNECT**.
4. The dashboard automatically starts a background worker in **Monitor Only** mode.
   No Windows clock settings are changed. Green "VALID REPLY" means UDP/123
   NTP transactions passed validation. "SYNCHRONIZED" means PC offset is within
   100 ms *at the last validated sample* — it does not mean a correction occurred.
5. Click **STOP** to stop this temporary monitor, or close the GUI.
   The GUI owns only its child monitor process and does not stop Windows Services.

**Important:** CONNECT tests reachability and offset but does **not** correct the Windows date.
If the PC is set to 2023 while GPS time is 2026, a large valid offset is expected
until an authorized isolated-lab Windows Service is installed in certification mode.
The graphical UI does not require installing a service simply to test the server.

The standalone `worker/ARFastSNTP.exe` needs `--server` arguments; when double-clicked
without arguments, it now opens the dashboard. The user's latest server IP/year
selection is stored under LocalAppData (configuration is **never** authorization
to step Windows time).

## Install a persistent Windows Service (optional)

Click **INSTALL SERVICE** in the GUI, or use `install-monitor.cmd` from
the extracted ZIP. This installs an automatic-start monitor-only service and
desktop shortcut. Windows UAC Administrator approval is required to register it.
The installer now uses the absolute Windows PowerShell executable path instead
of assuming `powershell.exe` is on PATH.

If Windows PowerShell is unavailable or blocked, **CONNECT** still works for
temporary monitoring. The dashboard is independent of the Windows Service.

### Enable time correction: isolated certification PC ONLY

Never do this on production SCADA, SOE, historian or a live workstation.
A separate **LAB CLOCK STEP** button and `install-certification-lab.cmd` require
explicit risk acknowledgement. Check that other time-setting services are stopped
under an authorized maintenance procedure. The application does not disable
Windows Time, Meinberg or Siemens NTP for you.

The service with clock correction operates even while the dashboard is closed.
The temporary CONNECT mode **never** changes system time.

Existing service installations are not silently overwritten. To switch server
IP or mode, uninstall the prior service before installing another one.

## Why this matters

- The GUI and Windows Service are separate processes; only the worker can
  correct the system clock and only with explicit certification flags.
- SNTP uses UDP port 123; ICMP ping alone does not imply an NTP transaction.
- The estimated server clock is derived from the last valid NTP measurement.
- Broadcast indicator stays **UNKNOWN** because unicast SNTP is not broadcast.
- The worker uses bounded 3-sample validation for suspicious offsets, adaptive
  polling when stable, RFC Kiss-o'-Death handling, and a 16-event correction ring.
- The status file is replaced atomically; UI reads a single snapshot rather
  than accumulating an unbounded stream of events.

## Important release limitations

Basic SNTP is unauthenticated, and source spoofing is possible on an untrusted network.
No signed MSI, physical GPS validation, or production SCADA acceptance has been completed.
Scripts have PowerShell syntax checks in CI, not a full service install/uninstall trial.
Use the [isolated lab acceptance plan](docs/LAB-ACCEPTANCE.md) before enabling stepping.

## Troubleshooting status exceptions

Older builds could crash with **"The given key was not present in the dictionary"**
when a status file from an older worker omitted `StepEnabled` or other optional
JSON properties. The current dashboard reads status through a version-tolerant DTO,
rejects corrupt files safely, and shows offline diagnostics instead of crashing.
The regression suite checks missing fields, corrupted JSON, stale responses and
multi-year clock offset decoding. The worker now logs bounded successful transaction
summaries to the console.

If the worker says "Application started", it **is running**; absence of one-second
console output was intentional in prior releases. Use the dashboard for live lamps
and request/reply counters. The monitor displays synchronization as a measurement,
not proof of an adjustment.

If `install-monitor.cmd` used to fail with "powershell.exe is not recognized",
the new launcher resolves the full built-in Windows PowerShell executable path.
This may still fail on PCs where Windows administrators explicitly block PowerShell;
CONNECT needs no PowerShell.

Always remove/close **old dashboard builds** before launching the new artifact.
The dashboard and worker must both come from the **same ZIP**.

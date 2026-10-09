# AR Fast SNTP — Windows SNTP Monitor and Lab Clock Service

A lightweight **C# .NET 8** SNTP worker + live dashboard + GUI Windows Service installer.

**OT safety:** forced clock steps are for approved, **isolated certification PCs only**.
Never change the clock of a production SCADA/SOE/historian computer with this test tool.
Unicast NTP is unauthenticated; use a trusted isolated source/network.

## 1. Test SNTP (no installer needed)

1. Download the latest **successful** [Windows Actions artifact](https://github.com/masarray/arintp/actions), `ARFastSNTP-Windows-x64`. Extract the **whole ZIP**.
2. Launch `dashboard/ARFastSNTP.Dashboard.exe` (not the worker executable).
3. Type **SERVER IP** (default `192.16.1.175`), choose **YEAR** (NTP-era reference, e.g. 2026), click **CONNECT**.
4. Watch **SNTP SERVER ONLINE**, **VALID REPLY**, server **UTC time + date**, PC **UTC time + date**, offset, RTT, counts and last correction event.
5. STOP stops only the temporary child monitor worker. The portable monitor **does not change Windows time**.

A valid SNTP reply does not imply the PC is synchronized. A huge offset such as
`+1,018,249,956,697 ms` means **about 32 years** difference between PC and NTP server.
The dashboard will show `OFFSET` (amber) instead of misleading `OFFLINE`.

**Broadcast** is marked UNKNOWN: ordinary SNTP request/reply does not verify
server broadcast/multicast operation.

## 2. Install a persistent Windows Service without PowerShell

The newest ZIP contains **`ARFastSNTP.Setup.exe`** at the root.
This is the canonical Windows graphical setup; the legacy PowerShell scripts
are **not included in the release bundle**.

Either click **INSTALL SERVICE** from the dashboard, or double-click
`install-monitor.cmd`. The setup shows the server IP, reference year, install
mode, existing service status, **INSTALL SERVICE / REMOVE SERVICE**, and a
visible error log. Approve the Windows UAC Administrator prompt.

- **Monitor only** (default): Windows Service runs at startup and continues with
  the dashboard closed; **never** sets the Windows clock.
- **Isolated Lab**: explicit acknowledgement, UAC Administrator permission,
  and a final confirmation are required. With this mode enabled, only the
  service (not the dashboard) can correct Windows time after 3 coherent samples.
  It refuses to enable correction if known competing time-setting services such
  as W32Time or Meinberg NTP are running.
- Existing service installation is never silently overwritten. Use the setup
  **REMOVE SERVICE** button before switching server IP, year or correction mode.
- Service executable is copied to `%ProgramFiles%\ARFastSNTP\worker\` and
  dashboard to `%ProgramFiles%\ARFastSNTP\dashboard\`.
- The installer registers the automatic-start service via Windows **sc.exe**
  (Windows SCM), not via PowerShell. Installer errors and commands are shown
  in the setup window and saved in `%ProgramData%\ARFastSNTP\setup.log`.
  Correction-event and status history are preserved when uninstalling.

**PowerShell scripts from older artifact ZIPs will still fail in locked-down
environments. Use the latest artifact and the new `ARFastSNTP.Setup.exe`.**

## 3. Lab test steps

On a disposable, physically isolated Windows PC:
1. Verify NTP/GPS server time and UTC independently.
2. Confirm and resolve other clock-setting services **manually** (the installer
   never shuts down Windows Time or Meinberg for you).
3. Open **LAB CLOCK STEP** in the dashboard or `install-certification-lab.cmd`,
   explicitly confirm isolation and approve elevation.
4. In setup, select lab mode, check the lab confirmation, click INSTALL SERVICE.
5. Confirm the setup reports **RUNNING**, close setup and reopen the dashboard.
   Its status comes from the service when not connected in portable mode.
6. Move test PC clock to a different date; verify the SNTP-offset changes,
   correction event, and then post-correction valid reply and synchronized lamp.
7. Restore approved time policy before connecting any test equipment to SCADA.

Detailed lab acceptance procedure: [LAB-ACCEPTANCE.md](docs/LAB-ACCEPTANCE.md).

## Engineering and limits

- One serial async worker per active instance, bounded correction history (16)
  and atomic status snapshots. No background polling from the GUI.
- Stable polling ~3 seconds; suspicious offsets checked with 3 independent
  samples; adaptive retry/backoff for NTP KoD and failures.
- NTP epoch handling references an **operator-configured server year**, not
  the potentially incorrect local PC clock. Not a substitute for authenticated NTP.
- Synchronization and huge jumps remain **unverified on real GPS hardware**.
- The installer is an inspectable C# executable, not a signed MSI. No automatic
  production deployment or merge to `main` without acceptance testing.

### Developer verification

```text
dotnet build tests/ARFastSNTP.Tests/ARFastSNTP.Tests.csproj -c Release
dotnet build src/ARFastSNTP.Dashboard/ARFastSNTP.Dashboard.csproj -c Release
dotnet build src/ARFastSNTP.Setup/ARFastSNTP.Setup.csproj -c Release
dotnet run --project tests/ARFastSNTP.Tests/ARFastSNTP.Tests.csproj -c Release
```

GitHub CI also runs a no-network/no-clock-change worker lifecycle check,
a native setup argument self-test, and a packaged ZIP layout check.
**No CI test installs a service or changes the GitHub runner system clock.**

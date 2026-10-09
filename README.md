# AR Fast SNTP — Windows certification prototype

A lightweight .NET 8 Windows Service SNTP worker and separate read-only WinForms dashboard.

> **OT safety:** Do NOT enable forced time stepping on live SCADA/SOE/historian
> hosts. Use only on an approved isolated certification PC. Basic NTPv4 is
> unauthenticated; never trust a public or untrusted NTP source for clock stepping.

## Fast Windows setup from GitHub Actions

1. Open the latest green [Windows CI](https://github.com/masarray/arintp/actions).
2. Download the `ARFastSNTP-Windows-x64` artifact, **extract the ZIP**.
3. Double-click **`install-monitor.cmd`** for a safe, monitor-only service.
   The default server is **192.16.1.175** (UDP 123), server-era reference 2026.
4. On an **isolated lab PC only**, resolve competing Windows Time/Meinberg/Siemens
   time-setting services, then use **`install-certification-lab.cmd`** and
   explicitly confirm the years-scale clock-step warning.
5. Approve the Windows UAC prompt. The scripts register an automatic-start
   service `ARFastSNTP` and create a desktop shortcut to the dashboard.
6. Run **`uninstall.cmd`** to stop and remove the service and executables.
   It leaves the last status/event evidence in ProgramData by default.

**Do not run both installers.** Existing service or installation directory
causes a safe abort. For a new configuration uninstall and reinstall.

To use another peer or year reference, run from the extracted artifact folder:

```powershell
.\install.ps1 -Server 192.16.1.175 -ServerYear 2026
# ISOLATED LAB PC ONLY (no other active time-setting service):
.\install.ps1 -Server 192.16.1.175 -ServerYear 2026 -CertificationStep -AcceptLabRisk
```

Scripts use Windows PowerShell/UAC and have no external installer dependencies.
This bundle is **not an MSI or digitally signed installer**. Do not assume
installation is validated merely because PowerShell scripts parse in CI.

## SNTP worker design

- Cold start or suspicious drift: three sequential SNTP transactions must
  agree within 250ms. Only this verified median can authorize a clock step.
- Stable synchronization: one request every ~3s instead of three requests
  every ~1s. Every sample is validated.
- Monitor-only with a large offset: verified burst ~every 5s.
- After step: verification on next ~1s cycle with a monotonic cooldown
  to avoid a rapid correction loop.
- UDP receive timeout: 2s; failed polling uses bounded exponential backoff.
  `RATE` KoD backs off one minute. `DENY`/`RSTR` suppress further polling
  until service restart.
- Serial BackgroundService worker; no parallel clock-set operations.
  Only 16 correction history messages are kept in memory.
- Atomic replace of `%ProgramData%\ARFastSNTP\status.json`, with event
  counters and estimated server UTC. GUI refresh coalesces to ~0.5Hz.
- Broadcast lamp is explicitly **unmeasured**, since UDP request/reply does
  not prove broadcast service.

### Development commands

```powershell
dotnet build src/ARFastSNTP/ARFastSNTP.csproj -c Release
dotnet run --project tests/ARFastSNTP.Tests -c Release
ARFastSNTP.exe --server 192.16.1.175 --server-year 2026 --check-config
```

See [lab acceptance procedure](docs/LAB-ACCEPTANCE.md) before enabling clock stepping.

## Not yet certified

No automated Windows Service integration acceptance test, signed MSI, physical
GPS verification, active NTP broadcast receiver or authenticated NTP.
The time source must reside on a trusted, isolated network. Time discipline
and SOE/historian behavior require site-specific approval before production use.

# AR Fast SNTP v0.10.3 — Windows x64

A performance and network-recovery refinement built directly on the tested **v0.10.2** SNTP Windows Studio. The public download remains one Inno Setup executable: `ARFastSNTP-Setup-Windows-x64.exe`, plus a separate tiny `.sha256` integrity file. No second installer, portable ZIP, separate service package or .NET runtime is required.

## Changes from v0.10.2

- **Fast validated confirmation:** After an initial coherent burst of three independently originated SNTP exchanges, the next confirmation probe is due after 750 ms rather than a generic three-second stable polling interval. A new valid Mode 4 response must still agree within the existing 250 ms spread threshold.
- **Deadline-aware supervisor:** If the next source probe is already due after other slow/unreachable peers complete, the worker no longer inserts another unconditional one-second sleep.
- **Network-notification resilience:** Irrelevant Windows network notifications no longer invalidate healthy source measurements when the observed IPv4 adapter/address/gateway fingerprint has not materially changed.
- **No protocol protection removed:** Originate binding, server mode/stratum/leap, RTT limits, election consensus, clock-epoch and stale sample safeguards, NTP RATE/DENY/RSTR limits and the single Windows clock writer remain enforced.
- All existing v0.10.2 functionality remains: bounded polling (up to 4 concurrent probes), up to 16 IPv4 servers, fast OFF→ON, Ethernet recovery, persistent Windows Service, compact Avalonia Studio, RTT tooltips, status history and JSON diagnostics.

## Real Windows feedback and CI evidence

User-reported acceptance of the tested source commit `103dc9af8dfd4aed687aac1c23b994b5c91c702a`: roughly **one second** for clock correction and roughly **six seconds** to recover and start syncing after the LAN cable was reconnected. These are **observations on that test PC**, not a service-level guarantee; actual performance depends on interface/route readiness, UDP replies and source consensus.

The tested engine commit passed [Windows CI #38093326129](https://github.com/masarray/arfast-sntp/actions/runs/38093326129). The final public v0.10.3 installer is built and verified again by the **post-merge `main`** Windows CI; inspect its workflow and source SHA in this release before deployment.

## Install / upgrade

Download only the official `ARFastSNTP-Setup-Windows-x64.exe` asset and its companion `.sha256` file from this GitHub Release. Verify checksum:

```powershell
Get-FileHash .\ARFastSNTP-Setup-Windows-x64.exe -Algorithm SHA256
```

Run the Inno installer as administrator on an **authorized test/engineering PC**. It uses the existing application identity so it can upgrade a previous installation; an existing `%ProgramData%\ARFastSNTP\peers.json` peer catalog is preserved on normal upgrades. Subsequent Add/Delete/Enable/Disable in Studio does not reinstall the Windows Service or prompt for UAC.

Standard installation permits Windows time correction after source validation. For **monitor-only** lab testing, run the installer with `/LABSYNC=0`. Uninstall using Windows Settings → Apps → Installed apps → AR Fast SNTP.

## Safety and limits

The installer is **unsigned**; Windows SmartScreen may warn. Confirm official repository/release origin and SHA-256. Do not disable antivirus, firewall, or Windows policies to bypass warnings.

**Not certified for production SCADA, SOE, historian, GPS reference or other critical timing systems.** Large-step, independent GPS failover, 24-hour soak, code signing and upgrade-rollback certification remain documented under [post-release hardening Issue #2](https://github.com/masarray/arfast-sntp/issues/2). This is an IPv4 unicast SNTP client, not a full RFC 5905 NTP discipline implementation; ARSAS same-host Npcap RAW delivery is not claimed resolved.

**Source:** https://github.com/masarray/arfast-sntp · **License:** GPL-2.0-or-later (LICENSE/NOTICE included in the installer).

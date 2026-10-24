# AR Fast SNTP v0.10.2 — Windows x64

Official public Windows installer, built from the merged multi-source SNTP Windows Studio and network-recovery baseline. **One installer executable**: `ARFastSNTP-Setup-Windows-x64.exe`, plus a small `.sha256` text checksum file.

## Highlights

- Canonical Avalonia Studio with compact NTP Server Explorer, live status, stratum, time offset, server time, RTT with tooltip, subtle zebra rows, and bounded Status History/diagnostics.
- Up to 16 IPv4 unicast NTP sources with live Add/Delete/Enable/Disable without reinstalling or relaunching the Windows Service.
- Single background service with bounded polling and one clock-correction authority.
- All sources OFF: service remains responsive, but no UDP NTP queries or system clock correction; status heartbeat stays alive.
- ON/reconnected network: coalesced NIC events, adapter fingerprint fallback, bounded retry/recovery. Stale samples are discarded before clock selection.
- Clock-epoch and NTP KoD safeguards; post-step verification requires a fresh NTP response.
- Consolidated canonical icon resources for Windows EXEs, Studio, installer, README, and website.

## Install or upgrade

Download the installer and checksum under **Assets**. Compare the checksum in Windows PowerShell:

```powershell
Get-FileHash .\ARFastSNTP-Setup-Windows-x64.exe -Algorithm SHA256
```

Run installer as administrator on an **authorized test/engineering PC**. It detects the app's existing service, preserves the saved peer catalog during upgrade, and configures one permanent Windows Service. A new installation asks for a unicast IPv4 NTP server.

The default installer permits Windows clock correction when the source is verified. To run **monitor-only** on a lab PC, start the installer with `/LABSYNC=0`. Manage peers afterward from Studio without repeated elevation. Uninstall through Windows Settings → Apps → Installed apps → AR Fast SNTP.

## Known limits and release safety

This build is **not Authenticode-signed**; SmartScreen reputation may be limited. Verify the GitHub release source and checksum. Do not disable antivirus or Group Policy protections merely to install it.

**Not certified for operational SCADA, SOE, historian, or GPS-grade reference timing.** This release is not evidence of 24-hour soak, installer rollback on every older version, deterministic response SLA across every NIC/topology, or full RFC 5905 clock discipline. The ARSAS same-host raw link-layer→Windows UDP delivery issue is not claimed resolved.

Source: [repository](https://github.com/masarray/arfast-sntp) · [Release acceptance / future industrial certification](https://github.com/masarray/arfast-sntp/issues/2) · [Multi-source architecture](https://github.com/masarray/arfast-sntp/blob/main/docs/MULTI-NTP-ARCHITECTURE.md).

**License:** GPL-2.0-or-later; LICENSE and NOTICE are included within the installer.

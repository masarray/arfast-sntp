<img src="assets/favicon/android-chrome-192x192.png" alt="AR Fast SNTP" width="64" height="64">

# AR Fast SNTP

**Windows x64 · Avalonia Studio · Windows Service · GPL-2.0-or-later**

AR Fast SNTP is a compact Windows SNTP (Simple Network Time Protocol) client for monitoring and, when explicitly configured, synchronizing the clock on **authorized test and commissioning PCs**. It supports up to **16 IPv4 NTP sources**, bounded probing, conservative source selection, and one background Windows Service that continues when Studio closes.

> **Pre-release:** Real tests validated public NTP replies and a small Windows clock correction. Independent-source failover, large-step recovery, unattended upgrade/rollback and visual DPI acceptance are **not yet certified**. Do not use on production SCADA/SOE/historian machines.

[Product landing page](index.html) · [Windows CI test builds](https://github.com/masarray/arintp/actions) · [Multi-NTP architecture](docs/MULTI-NTP-ARCHITECTURE.md) · [Acceptance gates](docs/LAB-ACCEPTANCE.md)

## Windows installation

From the most recent successful Windows CI run, download **`ARFastSNTP-Inno-Windows-x64`**, unzip it, and run **`ARFastSNTP-Setup-Windows-x64.exe`** on an isolated test PC. The Inno test installer is currently version **0.10.1** and is **not code-signed**. No public stable release has been tagged.

First install asks for a unicast IPv4 NTP server. The Studio **Add / Delete / Enable / Disable** actions update a persistent peer catalog through ACL-protected local IPC without reinstalling the service, showing UAC every time, or restarting SCM. On upgrade, the existing `peers.json` catalog should be preserved; verify rollback on an isolated PC before relying on it.

Studio displays **status, stratum, offset, server time and RTT** (round-trip delay) with a compact **Status History** and JSON diagnostic export. A source being enabled or reachable does not alone authorize correction. The service elects one time reference; conflicting measurements block changes.

## Diagnostics

| Item | Location |
|---|---|
| Windows Service | `ARFastSNTP` |
| Configured NTP sources | `%ProgramData%\ARFastSNTP\peers.json` |
| Live status | `%ProgramData%\ARFastSNTP\status.json` |
| Bounded decision journal | `%ProgramData%\ARFastSNTP\status.diagnostics.json` |
| Installer log | `%ProgramData%\ARFastSNTP\setup.log` |

Export logs using the download icon beside Status History. Check exported data for site-specific IP addresses before sharing.

### Protocol scope and safety

- SNTPv4 unicast over IPv4; UDP port 123. No NTS, DNS server pools, IPv6, broadcast reception or full RFC 5905 discipline.
- Up to 16 sources with a bounded number of concurrent requests and source-election hysteresis. Two IPs belonging to one provider are **not independent time references**.
- Use an isolated test workstation for large clock corrections. Never disturb live SCADA/SOE time or assume a Npcap RAW Ethernet injection was delivered to a Windows UDP socket.
- Follow [ARSAS interop diagnostics](docs/ARSAS-NTP-INTEROP.md), [installer tests](docs/INSTALLER-UX-ACCEPTANCE.md), and [release readiness](docs/RELEASE-CANDIDATE-NOTES.md).

## Product identity

The **canonical artwork is [`assets/favicon/`](assets/favicon)**. The checked-in multi-resolution `favicon.ico` is the single source for the Windows executables, Avalonia window/taskbar, and Inno Setup icon. README and the lightweight web landing page use the matching PNGs and a product-specific web manifest. Desktop styles follow the [Arssyut token provenance](docs/DESIGN-TOKEN-PROVENANCE.md).

License: [GPL-2.0-or-later](LICENSE) · [Notices](NOTICE).

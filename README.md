<p align="center">
  <img src="assets/favicon/android-chrome-192x192.png" width="74" height="74" alt="AR Fast SNTP">
</p>

<h1 align="center">AR Fast SNTP</h1>
<p align="center"><strong>Know your time sources. Keep your Windows clock in view.</strong></p>
<p align="center">Windows x64 · Avalonia Studio · Windows Service · SNTPv4 · GPL-2.0-or-later</p>
<p align="center">
  <a href="https://masarray.github.io/arfast-sntp/">Website</a> ·
  <a href="https://masarray.github.io/arfast-sntp/download.html">Download</a> ·
  <a href="https://github.com/masarray/arfast-sntp/releases">Releases</a> ·
  <a href="docs/MULTI-NTP-ARCHITECTURE.md">Architecture</a>
</p>

> **Public preview v0.10.1 — not production certified.** This is an unsigned Windows build for trusted, isolated commissioning/test PCs. Do **not** use it to discipline time on live SCADA, SOE, historian, or other critical systems. Independent-source failover, large time jumps, upgrade/rollback and DPI acceptance remain under test ([tracking issue](https://github.com/masarray/arfast-sntp/issues/2)).

## What is AR Fast SNTP?

AR Fast SNTP combines a calm, compact **Avalonia Studio** with **one persistent Windows Service**. It can monitor up to **16 IPv4 unicast NTP servers**, show server health in one explorer, and elect a conservative source for Windows clock correction on **authorized lab PCs**.

| Windows Studio | Time-source engine |
|---|---|
| Add, remove, enable or disable source IPs | Bounded concurrent UDP probing |
| Per-server status, stratum, offset and **RTT** | One elected clock source, no competing time setters |
| Live server time and PC sync indicator | Outlier/conflict checks and failover policies |
| Bounded Status History and diagnostic export | Config persisted without service reinstall/restart |
| Responsive compact dark UI and branded Windows installer | Worker stays active after closing Studio |

## Download and install

**[Download v0.10.1 Preview for Windows x64](https://masarray.github.io/arfast-sntp/download.html)**

The installer is published as **ARFastSNTP-Setup-Windows-x64.exe** in the [v0.10.1 Preview GitHub Release](https://github.com/masarray/arfast-sntp/releases/tag/v0.10.1-preview.1). A matching **.sha256** file is provided to verify integrity. Do not download unofficial mirrors.

1. On a **test/commissioning PC**, download the installer and compare its SHA-256 against the published checksum.
2. Run the Inno installer as administrator. Enter a reachable **unicast IPv4 NTP server** (UDP port 123).
3. Open **AR Fast SNTP Studio** from Start. Manage subsequent server IPs using the built-in Add/Delete/Use controls.
4. For **monitor-only** lab installation (no PC time correction), run the installer with **`/LABSYNC=0`**. **Normal installer behavior enables clock correction** after a suitable NTP reference is accepted, so only use the default on a test PC where time changes are permitted.

The installer is **unsigned**. Windows SmartScreen or antivirus may warn about an unfamiliar executable. Verify the official release source and SHA-256; do not disable protection or create broad exclusions.

### Uninstall

Use **Windows Settings → Apps → Installed apps → AR Fast SNTP → Uninstall**. The uninstall process stops/removes the managed service and removes the server catalog; diagnostic logs may remain under ProgramData. Back up diagnostic evidence before uninstall if needed.

## Architecture

```text
Avalonia Studio (monitor + controls)
             │ ACL-protected local IPC
             ▼
ARFastSNTP Windows Service
  ├─ Persistent peer catalog: up to 16 unicast IPv4 servers
  ├─ Bounded poll workers / validated NTP responses
  ├─ Source consensus, outlier filtering, hysteresis, failover
  ├─ One authorized Windows clock-correction path
  └─ Live status + bounded decision history
```

For source-election policy, see [Multi-NTP architecture](docs/MULTI-NTP-ARCHITECTURE.md). The client is **SNTPv4 over IPv4**, not a full RFC 5905 implementation. No NTS, IPv6, DNS pools, or broadcast-mode reception. Two IPs from one upstream provider do **not** guarantee independent time consensus.

### Troubleshooting / diagnostics

| Item | Location |
|---|---|
| Windows service | `ARFastSNTP` |
| Saved source IPs | `%ProgramData%\ARFastSNTP\peers.json` |
| Latest status | `%ProgramData%\ARFastSNTP\status.json` |
| Decision history | `%ProgramData%\ARFastSNTP\status.diagnostics.json` |
| Installation/recovery log | `%ProgramData%\ARFastSNTP\setup.log` |

A **No reply** status means Windows did not receive an accepted response for that poll. An ARSAS/Npcap log saying a Mode 4 packet was transmitted is not proof of reception by a Windows UDP socket; see the [interop investigation](docs/ARSAS-NTP-INTEROP.md).

## Building from source

This repository targets **.NET 8** and Windows x64 for Studio/installer. Windows CI builds the worker, GUI and setup with warnings-as-errors, runs SNTP regression and no-network worker smoke tests, publishes self-contained Windows binaries and packages Inno Setup.

```powershell
dotnet build src/ARFastSNTP.Studio/ARFastSNTP.Studio.csproj -c Release
dotnet run --project tests/ARFastSNTP.Tests/ARFastSNTP.Tests.csproj -c Release
```

CI does **not** adjust a runner's actual system clock; physical correction tests must run on a disposable lab PC. See [release acceptance](docs/RELEASE-CANDIDATE-NOTES.md) and [installer verification](docs/INSTALLER-UX-ACCEPTANCE.md).

## Brand, license and contributions

All executable, Inno, web and README icons come from [assets/favicon](assets/favicon). Desktop styling follows [Arssyut design-token provenance](docs/DESIGN-TOKEN-PROVENANCE.md).

Released under [GPL-2.0-or-later](LICENSE). See [NOTICE](NOTICE) for credits and notices. Contributions and reproducible issues are welcome; please redact private network/site information from diagnostic exports.

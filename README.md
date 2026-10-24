<p align="center">
  <img src="assets/favicon/android-chrome-192x192.png" width="72" alt="AR Fast SNTP app icon">
</p>

<h1 align="center">AR Fast SNTP</h1>
<p align="center"><strong>Time synchronization should be simple.</strong></p>
<p align="center">Install the app, enter your SNTP server IP address, and let it synchronize your computer clock.</p>

<p align="center">
  <a href="https://github.com/masarray/arfast-sntp/releases/latest/download/ARFastSNTP-Setup-Windows-x64.exe"><strong>Download for Windows</strong></a> ·
  <a href="https://github.com/masarray/arfast-sntp/releases/tag/linux-v0.1.0-beta.1">Linux beta</a> ·
  <a href="https://masarray.github.io/arfast-sntp/">Website</a> ·
  <a href="docs/QUICK-START.md">Quick start</a>
</p>

<p align="center">
  <a href="https://github.com/masarray/arfast-sntp/actions/workflows/windows-ci.yml"><img alt="Windows CI" src="https://github.com/masarray/arfast-sntp/actions/workflows/windows-ci.yml/badge.svg?branch=main"></a>
  <a href="https://github.com/masarray/arfast-sntp/actions/workflows/linux-release.yml"><img alt="Linux CI" src="https://github.com/masarray/arfast-sntp/actions/workflows/linux-release.yml/badge.svg?branch=main"></a>
  <a href="https://github.com/masarray/arfast-sntp/releases/latest"><img alt="Latest Windows release" src="https://img.shields.io/github/v/release/masarray/arfast-sntp?label=Windows%20stable"></a>
</p>

AR Fast SNTP is a **free, open-source time synchronization app** for Windows engineering PCs, industrial HMIs, workstations, and anyone learning how SNTP works. A Linux desktop beta is also available.

**No editing `ntp.conf`. No repeated service restarts. Just your server IP address.**

## See the app

<p align="center">
  <a href="assets/screenshot/Screenshot%202026-10-11%20073259.webp">
    <img src="assets/screenshot/Screenshot%202026-10-11%20073259.webp" width="860" alt="AR Fast SNTP Windows Studio showing selected NTP time sources, status, offsets and connection history">
  </a>
</p>
<p align="center"><sub>Actual Windows Studio screenshot from this repository.</sub></p>

| Add a time server | Check synchronization status |
|---|---|
| <a href="assets/screenshot/Screenshot%202026-10-11%20072931.webp"><img src="assets/screenshot/Screenshot%202026-10-11%20072931.webp" width="420" alt="Add NTP Server dialog with an IP address field"></a> | <a href="assets/screenshot/Screenshot%202026-10-11%20072958.webp"><img src="assets/screenshot/Screenshot%202026-10-11%20072958.webp" width="420" alt="AR Fast SNTP displaying time server health"></a> |

[See how to remove a server](assets/screenshot/Screenshot%202026-10-11%20073230.webp) · [View all original screenshots](assets/screenshot)

## Get started in three steps

1. **Install:** [Download the Windows x64 installer](https://github.com/masarray/arfast-sntp/releases/latest/download/ARFastSNTP-Setup-Windows-x64.exe) and run setup.
2. **Enter your server IP:** Use the IPv4 address of your GPS-based NTP server or another trusted NTP/SNTP server.
3. **Done:** Open **AR Fast SNTP** and see the selected server, synchronization status, offset, and response time.

The synchronization service keeps running even when the app window is closed. You can add, enable, disable, or remove servers directly in Studio.

**New to SNTP?** The [beginner's quick-start guide](docs/QUICK-START.md) explains what server IP to enter, how to check the result, and a few easy troubleshooting tips.

### Prefer a command line?

On Windows, this optional PowerShell helper downloads the official installer and checks its SHA-256 before starting setup:

```powershell
Invoke-WebRequest https://raw.githubusercontent.com/masarray/arfast-sntp/main/tools/install-windows.ps1 -OutFile install-arfast.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\install-arfast.ps1
```

### Linux desktop beta

[Download AR Fast SNTP for Linux x64](https://github.com/masarray/arfast-sntp/releases/tag/linux-v0.1.0-beta.1). It includes a desktop UI and a background SNTP service. Extract the Linux package and follow the [Linux setup guide](docs/LINUX-INSTALL.md).

## What can you do with AR Fast SNTP?

| Feature | What it means for you |
|---|---|
| **Simple server setup** | Enter an IPv4 SNTP server address instead of editing NTP configuration files |
| **Automatic synchronization** | Let the background service handle time updates |
| **Network recovery** | The Windows service reconnects and verifies sources after network changes |
| **Clear status** | See the selected server, offset, response delay (RTT), and events |
| **Multiple time servers** | Add and manage up to 16 IPv4 servers in Windows Studio |
| **Free and open source** | Use it, learn from it, and contribute on GitHub |

### Built from real engineering experience

I originally built AR Fast SNTP after spending too much time troubleshooting slow and inconsistent Windows time synchronization on industrial HMI computers.

In tests with **four Windows HMI computers (two servers and two workstations)**, clock correction was observed in around **one second**. After reconnecting the LAN, synchronization recovered in approximately **six seconds**. These are results from those tests, rather than a promised time for every network.

The goal is still the same: **let the application handle the complexity, not the engineer.**

## Help and development

- [Quick start & common questions](docs/QUICK-START.md) · [Support](SUPPORT.md)
- [Windows stable release](https://github.com/masarray/arfast-sntp/releases/latest) · [Linux beta](https://github.com/masarray/arfast-sntp/releases/tag/linux-v0.1.0-beta.1)
- [Windows SNTP architecture](docs/MULTI-NTP-ARCHITECTURE.md) · [Linux service & setup](docs/LINUX-INSTALL.md)
- [Release history](CHANGELOG.md) · [Contributing](CONTRIBUTING.md) · [Security](SECURITY.md)

Built with C#/.NET 8 and Avalonia. Use AR Fast SNTP on a computer where you are allowed to adjust the clock. Download the installer from the official [GitHub Releases](https://github.com/masarray/arfast-sntp/releases) page; a checksum is provided for verification.

Licensed under [GPL-2.0-or-later](LICENSE). Third-party notices are in [NOTICE](NOTICE).

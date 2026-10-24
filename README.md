<p align="center">
  <img src="assets/favicon/android-chrome-192x192.png" width="72" alt="AR Fast SNTP">
</p>

<h1 align="center">AR Fast SNTP</h1>
<p align="center"><strong>Time synchronization should be simple.</strong></p>
<p align="center">Enter your NTP/SNTP server IP address. Let the application handle the rest.</p>

<p align="center">
  <a href="https://masarray.github.io/arfast-sntp/">Website</a> ·
  <a href="https://masarray.github.io/arfast-sntp/download.html">Download</a> ·
  <a href="https://github.com/masarray/arfast-sntp/releases">All releases</a> ·
  <a href="SUPPORT.md">Help</a>
</p>

<p align="center">
  <a href="https://github.com/masarray/arfast-sntp/actions/workflows/windows-ci.yml"><img alt="Windows CI" src="https://github.com/masarray/arfast-sntp/actions/workflows/windows-ci.yml/badge.svg?branch=main"></a>
  <a href="https://github.com/masarray/arfast-sntp/actions/workflows/linux-release.yml"><img alt="Linux CI" src="https://github.com/masarray/arfast-sntp/actions/workflows/linux-release.yml/badge.svg?branch=main"></a>
  <a href="https://github.com/masarray/arfast-sntp/releases/latest"><img alt="Latest Windows release" src="https://img.shields.io/github/v/release/masarray/arfast-sntp?label=Windows"></a>
</p>

AR Fast SNTP is a free, open-source desktop tool for engineers who want an easier way to synchronize a computer clock with an SNTP server. It was built around real HMI server and workstation testing, but it is also useful for learning and troubleshooting time synchronization on a regular PC.

**Built for electrical engineers, automation engineers, SCADA/HMI engineers, students, and curious PC users.**

## Get started

### Windows x64

**[Download Windows installer](https://github.com/masarray/arfast-sntp/releases/latest/download/ARFastSNTP-Setup-Windows-x64.exe)**

1. Run the installer. Enter the IP address of your NTP/SNTP server.
2. Open **AR Fast SNTP** from the Start menu.
3. Watch the server connection and time synchronization status. You can add more servers whenever needed.

No editing of `ntp.conf` or manual service restart for each server change.

**Prefer the command line?** You can download the official checksum-verifying installer helper and run it from PowerShell:

```powershell
Invoke-WebRequest https://raw.githubusercontent.com/masarray/arfast-sntp/main/tools/install-windows.ps1 -OutFile install-arfast.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\install-arfast.ps1
```

The helper checks the Windows installer SHA-256 against the published release before opening the usual setup wizard. You can read the script first: [tools/install-windows.ps1](tools/install-windows.ps1).

### Linux x64

**[Download Linux beta](https://github.com/masarray/arfast-sntp/releases/tag/linux-v0.1.0-beta.1)**

Linux includes a native SNTP background service and a small Avalonia GUI. On a supported Linux desktop with systemd and polkit, download and extract the release, then run:

```bash
sudo sh ./install.sh
```

Open **AR Fast SNTP** from the Applications menu and enter your server IP. Follow the [Linux installation guide](docs/LINUX-INSTALL.md) for the exact package and setup steps.

## See it in action

<p align="center">
  <a href="assets/screenshot/Screenshot%202026-10-11%20073259.webp"><img src="assets/screenshot/Screenshot%202026-10-11%20073259.webp" width="880" alt="Actual AR Fast SNTP Windows Studio showing server status, round-trip time and history"></a>
</p>

| Add a server | Check the result |
|---|---|
| <a href="assets/screenshot/Screenshot%202026-10-11%20072931.webp"><img src="assets/screenshot/Screenshot%202026-10-11%20072931.webp" width="430" alt="Enter an NTP server IP address"></a> | <a href="assets/screenshot/Screenshot%202026-10-11%20072958.webp"><img src="assets/screenshot/Screenshot%202026-10-11%20072958.webp" width="430" alt="Monitor multiple NTP servers and their connection status"></a> |

[Remove a time source](assets/screenshot/Screenshot%202026-10-11%20073230.webp) · [All original screenshots](assets/screenshot)

## Why AR Fast SNTP?

- **IP address first.** Choose a GPS-referenced local NTP server or a public NTP server reachable on UDP port 123.
- **Always-on service.** Synchronization runs in the background after the UI is closed.
- **Automatic recovery.** The Windows app reacts to network interruptions and new connections.
- **Clear status.** See which server is selected, offset, round-trip time, and recent events.
- **Multiple sources.** Windows Studio supports up to 16 IPv4 server addresses, with individual enable/disable controls.
- **Free and open source.** Learn how it works, inspect the code, and contribute.

During engineering tests on four Windows HMI computers, initial correction was observed in around one second and recovery after LAN reconnection in around six seconds. These are results from that test environment; timings vary with server and network conditions.

## What server IP should I enter?

Use the IPv4 address of an NTP/SNTP server reachable from your PC.

- **Industrial lab:** Enter the IP address of your GPS-referenced NTP server.
- **Home/office experiment:** Try a public NTP server IPv4 address published by a trusted time-service provider.

The client *requests* the time from the server; the server does not have to broadcast anything. Only use sources you trust and are permitted to access.

## For developers and advanced users

This project uses **C# / .NET 8**, **Avalonia** and SNTPv4 over IPv4.

- [Windows architecture and source selection](docs/MULTI-NTP-ARCHITECTURE.md)
- [Linux install and service reference](docs/LINUX-INSTALL.md)
- [WinGet manifest and publishing workflow](docs/WINGET.md)
- [Release notes and changelog](CHANGELOG.md)
- [Help and troubleshooting](SUPPORT.md)
- [Security and responsible usage](SECURITY.md)
- [Contributing](CONTRIBUTING.md)

Use a PC where you are authorized to adjust the system clock; time changes can affect other applications. For production-critical time systems, follow your site's tested time synchronization design. Installers and checksums are available in [GitHub Releases](https://github.com/masarray/arfast-sntp/releases). The Windows installer is currently unsigned; the GitHub release includes a checksum.

Licensed under [GPL-2.0-or-later](LICENSE). See [NOTICE](NOTICE).

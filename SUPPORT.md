# Help and troubleshooting

**Need a hand getting your computer synchronized?** Start with the [quick-start guide](docs/QUICK-START.md), which uses actual screenshots from AR Fast SNTP.

## Quick checks

| What you see | What to try |
|---|---|
| **No reply** | Confirm the server IP is correct, reachable, and responds to NTP requests on UDP port 123 |
| **Server not selected** | Check whether the server is enabled and returning valid NTP time |
| **Clock not updating** | Check that AR Fast SNTP was installed with synchronization enabled and the PC allows time adjustments |
| **Network reconnected** | Check the selected source and Status History after the adapter reconnects |
| **Adding a new server** | Use Add in Studio; changing servers does not require reinstalling the app |

## Found a bug or have an idea?

[Open a GitHub issue](https://github.com/masarray/arfast-sntp/issues/new/choose) with the release version, operating system, what you tried, and what happened. A screenshot is often enough to explain the problem.

Please hide private IP addresses, hostnames, and workplace identifiers in public reports. For confidential security reports, follow [SECURITY.md](SECURITY.md).

## Downloads and updates

Get the [latest Windows installer](https://github.com/masarray/arfast-sntp/releases/latest) or the [Linux x64 beta](https://github.com/masarray/arfast-sntp/releases/tag/linux-v0.1.0-beta.1). Official Windows downloads include a SHA-256 file if you want to verify the installer.

## More detail

- [Beginner quick start](docs/QUICK-START.md)
- [Linux setup and service checks](docs/LINUX-INSTALL.md)
- [Windows architecture](docs/MULTI-NTP-ARCHITECTURE.md)
- [Engineering roadmap](docs/ENGINEERING-ROADMAP.md)

AR Fast SNTP adjusts the computer clock when synchronization is enabled. Try clock-changing experiments on a computer you control, rather than a system that depends on uninterrupted timekeeping.

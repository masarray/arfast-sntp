# Support and troubleshooting

**Use [GitHub Issues](https://github.com/masarray/arfast-sntp/issues) for reproducible bugs and feature requests**, and [GitHub Releases](https://github.com/masarray/arfast-sntp/releases/latest) to obtain the official installer. See [SECURITY.md](SECURITY.md) for private vulnerability disclosure instead of publicly reporting an exploitable security issue.

## Before filing a bug

1. State the exact GitHub Release tag, Windows build and whether you installed in normal synchronization or `/LABSYNC=0` monitor-only mode.
2. Identify the relevant scenario: startup, all sources OFF, source ON, DNS/UDP failure, cable LAN unplug/replug, sleep/resume, upgrade or uninstall.
3. Report the expected behavior, the observed behavior, and approximate elapsed time. If possible, include a redacted screenshot of the actual Avalonia Studio.
4. Export the bounded decision/status diagnostics and attach only after removing private addresses, hostnames, site identifiers and sensitive timestamps.
5. If an installer issue, include only **redacted** entries from `%ProgramData%\ARFastSNTP\setup.log`; never post complete system, site or security event logs to a public issue.

The service listens for replies to IPv4 UDP port 123 and validates NTP packets. A “No reply” from a source is not necessarily an application bug—check network routes, firewall rules and whether the remote endpoint replies to the Windows UDP socket.

## Scope and safety

Use a disposable or authorized **test/commissioning Windows PC** for intentional clock corrections. Changing the date can disturb certificates, authentication, logging and running applications. Do not test clock stepping on active SCADA, SOE, historian or other production timing systems.

No guaranteed response SLA, commercial support plan, 24/7 incident coverage or industrial time accuracy certification is offered. Extended technical constraints are recorded in [PROJECT-STATUS.md](docs/PROJECT-STATUS.md) and [Issue #2](https://github.com/masarray/arfast-sntp/issues/2).

## Integrity

The installer is unsigned. Download only from GitHub Releases and compare its SHA-256 to the accompanying checksum. That checksum detects corruption or substitution relative to the published file but is **not proof of software publisher identity**.

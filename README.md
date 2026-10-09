# AR Fast SNTP

**Windows x64 · GPL-2.0-or-later · Avalonia Studio**

Use the latest successful GitHub Actions artifact `ARFastSNTP-Inno-Windows-x64`.
Extract the artifact ZIP and launch `ARFastSNTP-Setup-Windows-x64.exe`.

The Inno wizard asks for the NTP server IPv4 address, upgrades an existing
ARFastSNTP service in place, starts it, and opens Studio. The Windows Service
continues running when Studio closes. The reference year is retained from
existing configuration and is hidden from the normal setup flow.

The installer recognizes historical PowerShell/WinForms/Inno service layouts
under known Program Files paths. It captures the previous executable, SCM
command, startup mode, and running state for rollback. It never adopts an
unrecognized service. Legacy desktop shortcuts are cleaned only when their
targets are verified.

**Studio** displays PC UTC/date, synchronization state, one measured server
row with health/stratum/offset/estimated server UTC, and a collapsed event
history. The visual system reuses graphite tokens from `masarray/arssyut`.
The monitor no longer contains installer actions or a year field.

The current worker uses a single peer. Multi-server management will be added
only when the canonical engine supports genuine per-peer data and selection.

Expert unattended installation on an authorized lab PC:
`ARFastSNTP-Setup-Windows-x64.exe /VERYSILENT /SERVER=192.16.1.175`.
Optional `/YEAR=2026` adjusts the engine NTP-era reference; `/LABSYNC=0`
requests monitoring rather than clock correction.

**Safety:** clock stepping is intended for authorized isolated testing, not
a live operational control computer. NTP server input should be trusted.

Technical references:
- [Installer and UI acceptance matrix](docs/INSTALLER-UX-ACCEPTANCE.md)
- [Release candidate notes](docs/RELEASE-CANDIDATE-NOTES.md)
- [Isolated lab acceptance](docs/LAB-ACCEPTANCE.md)
- [Arssyut token provenance](docs/DESIGN-TOKEN-PROVENANCE.md)
- [Windows product roadmap](docs/WINDOWS-PRODUCT-PLAN.md)

GitHub CI verifies builds, offline migration logic, worker monitor lifecycle,
and Inno compilation. Real Windows service upgrades and DPI acceptance require
field testing; green CI alone is not production acceptance. Source from the
existing WinForms dashboard remains buildable as a fallback but is not bundled
into the primary Inno installer.

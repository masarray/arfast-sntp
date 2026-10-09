# AR Fast SNTP

**Windows x64 · GPL-2.0-or-later · Avalonia Studio**

Use the latest successful GitHub Actions artifact `ARFastSNTP-Inno-Windows-x64`.
Extract the artifact ZIP and launch `ARFastSNTP-Setup-Windows-x64.exe`.

The v0.9.2 Inno wizard simply says **Enter NTP Server IP Address**, upgrades an existing
ARFastSNTP service in place, starts it, and opens Studio. The Windows Service
continues running when Studio closes. The reference year is retained from
existing configuration and is hidden from the normal setup flow.

The final wizard distinguishes **service running and PC time synchronized**,\n**service running but waiting for an NTP reply**, and **failed service startup**.\nThe installer checks recent worker measurements for up to eight seconds and\nnever equates a running service with a verified synchronized clock.\n\nThe installer recognizes historical PowerShell/WinForms/Inno service layouts
under known Program Files paths. It captures the previous executable, SCM
command, startup mode, and running state for rollback. It never adopts an
unrecognized service. Legacy desktop shortcuts are cleaned only when their
targets are verified.

**Studio** now uses a premium, low-border graphite layout: PC UTC/date,
synchronization state, one measured server row with offset/stratum/server
time, and a collapsible history with Material circle-chevron icons. The
server and switch icons follow Material vector paths provided in the
design review. The **Use** switch genuinely starts or stops the currently
configured single-source Windows Service with one Administrator approval;
it is not a decorative toggle. The service stays running if Studio closes
while the switch is enabled.
The monitor no longer contains installer actions or a year field.

The current worker uses a **single peer**. Add/Delete and independent
enable switches for additional NTP servers are deliberately not shown yet.
They require a canonical multi-peer worker, source registry and real
per-peer polling/selection. We will not ship fake active controls.

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

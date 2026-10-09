# AR Fast SNTP

**Windows x64 · GPL-2.0-or-later · Avalonia Studio**

Use the latest successful GitHub Actions artifact `ARFastSNTP-Inno-Windows-x64`.
Extract the artifact ZIP and launch `ARFastSNTP-Setup-Windows-x64.exe`.

The v0.9.3 Inno wizard simply says **Enter NTP Server IP Address**, upgrades an existing
ARFastSNTP service in place, starts it, and opens Studio. The Windows Service
continues running when Studio closes. The reference year is retained from
existing configuration and is hidden from the normal setup flow.

The final wizard distinguishes **service running and PC time synchronized**,\n**service running but waiting for an NTP reply**, and **failed service startup**.\nThe installer checks recent worker measurements for up to eight seconds and\nnever equates a running service with a verified synchronized clock.\n\nThe installer recognizes historical PowerShell/WinForms/Inno service layouts
under known Program Files paths. It captures the previous executable, SCM
command, startup mode, and running state for rollback. It never adopts an
unrecognized service. Legacy desktop shortcuts are cleaned only when their
targets are verified.

**Studio** now uses a compact, low-border graphite layout: PC **local** time
and date (matching Windows), independently verified synchronization state,
one measured server row with offset/stratum/**local** server time, and
collapsible history with Material circle-chevron icons. NTP calculations
remain UTC internally. Source status only says *NTP enabled* when service
startup is enabled; that alone is **not** proof of a valid NTP reply. The
server and switch icons follow Material vector paths provided in the
design review. The **Use** switch now pauses/resumes only the configured NTP source,
without stopping the Windows Service. It sends a tiny, fixed-format command
over a local Windows named pipe to the worker; there is **no second UAC
approval, service reinstall, or helper executable** for each click. The
service remains running when Studio closes, including while NTP is OFF.
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

## Compact monitor acceptance

A single-source window must be content-sized, not stretch one row into a
mostly empty table. The top bar has only the application title and refresh
control; it does not repeat NTP status. PC Sync displays only the verified
time status. The table displays source health, offset and estimated time.
A short actionable footer appears only when there is an issue; request
counters, RTT jitter and rapidly changing "updated at" text are no longer
repainted in the always-visible monitor. Status History remains the place
for diagnostic events.

The 34×19 DIP switch is deliberately smaller than its 40×30 DIP click target.
The PC Time/Sync summary is now 66 DIP high; the NTP Explorer is the primary
visual area with consistent 9/10/14 DIP spacing.
The adjacent Material time-server icon becomes green when the NTP source
is enabled, gray when disabled. This is NOT a claim the remote NTP server
is reachable: the independent Status column determines that. The source
switch uses a protected, local-only pipe handled by the existing Windows
Service; Studio never changes system time or modifies service startup mode.
ON/OFF survives reboot in a service-owned HKLM registry setting.
A 1 Hz coalesced status reader updates unchanged labels zero times, and a
monotonic estimated server clock is only rebased on actual discontinuities.
Stale status still triggers future polls so the app recovers when service
replies resume.

The pure `StudioStatusPresenter` is included in the .NET regression suite,
which covers verified sync, timeout guidance, large drift, and rejecting a
false sync when the timestamp is absent. No new CI lane is required.

## Source pause/resume (single trusted NTP peer)

- The installer still requires a **one-time Windows Administrator approval**.
  The existing worker then runs as the canonical Windows Service.
- A source **OFF** command is acknowledged after the worker has persisted it
  and established a synchronized barrier against further SetSystemTime calls.
  The worker does not poll UDP/123 while disabled; the service stays RUNNING.
- Source **ON** wakes the same worker without stopping/starting SCM. It
  discards pre-toggle burst validity and waits for newly verified NTP samples
  before attempting a clock correction.
- The 2-byte protocol accepts only OFF, ON, or QUERY and replies with a
  bounded 3-byte acknowledgment; no server IP, arbitrary path, command line,
  or executable can be supplied. The pipe's protected DACL permits LocalSystem,
  Administrators and local interactive sessions; non-interactive network logins
  do not receive a blanket Allow rule. Installation is restricted to an
  authorized isolated lab machine, where locally logged-in operators may
  deliberately pause/resume the pre-approved source.
- The flag is stored under a protected HKLM setting, not in a file writable
  by ordinary users. Disabling source never changes Windows Service startup
  type; service health and source preference remain separate concepts.
- On an older worker with no control pipe, Studio disables the switch and
  asks for an upgrade instead of launching an elevated Setup executable.

**Windows Defender SmartScreen / UAC:** removing the child process launch
eliminates the repeated elevation workflow when toggling a source. The
downloaded, currently unsigned installer may still display a SmartScreen
reputation warning when first launched. Production distribution should use
consistent Authenticode signing; no attempt to disable/bypass SmartScreen.

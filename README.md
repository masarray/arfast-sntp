# AR Fast SNTP

**Windows x64 · GPL-2.0-or-later · Avalonia Studio**

AR Fast SNTP is a Windows NTP client and monitor for **authorized isolated
certification/test PCs**. It runs one Windows Service that can monitor up to
16 NTP IPv4 servers, select a coherent time reference, and (when explicitly
installed in synchronization mode) correct the Windows clock.

**This is not yet suitable for live SCADA/SOE/historian hosts.** SNTP is
unauthenticated; use a trusted isolated GPS/NTP network. Real-device
failover, accuracy and installer acceptance are still required.

## One install, then manage servers from Studio

Download a successful `ARFastSNTP-Inno-Windows-x64` artifact from
[GitHub Actions](https://github.com/masarray/arintp/actions), extract it
and run `ARFastSNTP-Setup-Windows-x64.exe`.

**Clean installation:** Enter the first NTP Server IP Address. Inno Setup
installs one automatically started Windows Service and offers to open Studio.
**Upgrade:** Existing PowerShell/WinForms/Inno service variants are migrated
using an allowlist. When the canonical `peers.json` already exists, the IP
page is skipped: the full server list is preserved. A previous executable
and SCM settings are captured for rollback if activation fails.

The Inno wizard is set to its **normal 100% size** rather than the oversized
modern-style default. Installation progress distinguishes the service
starting from a verified NTP reply. Closing Studio does not stop syncing.

**Studio** has no duplicate application name in the body. The compact PC
Time/Sync strip is secondary, followed by the principal **NTP Servers**
explorer: row number, per-source switch, Material server icon, IPv4, reply
health, stratum, offset, local server time, broadcast field and Delete.
Add opens a short IP dialog. Status History uses a Material circle-chevron
and a bounded scrolling list. Window height follows the content instead
of reserving empty space for hypothetical extra servers.

Add/Delete/ON/OFF changes are sent to an ACL-protected, local Windows
named pipe. The already-running worker writes the configuration atomically,
wakes itself, and refreshes telemetry. **No UAC/SmartScreen prompt, service
installation, SCM stop/start, or background process per server** is needed
for ordinary source management.

## Multiple servers: who sets the clock?

Only **one** canonical worker is permitted to call the Windows clock API.
It probes configured enabled servers (up to four concurrent UDP requests),
tracks stratum/RTT/offset, applies bounded backoff to failures, and elects
one source. Its conservative election requires a strict majority of
currently valid measurements inside a 250 ms offset interval if two or
more servers respond. A stable previous source is retained to reduce
flip-flopping. After correction, prior measurement trust is discarded.

A single responding server can be selected after repeated coherent
measurements, **marked as degraded operation**. This is a deliberate
trusted-lab fallback and not a proof of correctness; two valid but
conflicting servers stop the clock correction. A former selected server
that becomes unreachable can be replaced by a healthy eligible source.

See [MULTI-NTP-ARCHITECTURE.md](docs/MULTI-NTP-ARCHITECTURE.md) for
selection examples, limitations and the remaining acceptance matrix.
This is **not** a complete RFC 5905 implementation and it does not
yet support NTS, broadcast reception, DNS source pools or IPv6.
A server being enabled is not evidence it is reachable/synchronized.

## Windows operation and diagnostics

- NTP worker status: `%ProgramData%\ARFastSNTP\status.json`
- Canonical server list: `%ProgramData%\ARFastSNTP\peers.json`
- Service setup log: `%ProgramData%\ARFastSNTP\setup.log`
- Installer recovery: `%ProgramData%\ARFastSNTP\installer-recovery`
- Service name: `ARFastSNTP` (Automatic, continues while Studio is closed)

Advanced unattended *isolated lab* install:
`ARFastSNTP-Setup-Windows-x64.exe /VERYSILENT /SERVER=192.16.1.175`.
Optional `/YEAR=2026` sets NTP era reference and `/LABSYNC=0`
requests monitor-only mode. Updating NTP servers thereafter requires
**no installer**. A full uninstall removes the catalog/owned registry
setting; logs are retained for diagnosis.

Builds, regression checks and Inno packaging run in one Windows CI job.
**CI never changes its own system clock or installs the Windows Service.**
Physical Windows acceptance (upgrade, named pipe ACL, 1/2/3 sources,
failover, event records and GUI DPI) is mandatory before promotion.
The PR remains Draft until those gates are met.

License: [GPL-2.0-or-later](LICENSE), with Arssyut token provenance in
[docs/DESIGN-TOKEN-PROVENANCE.md](docs/DESIGN-TOKEN-PROVENANCE.md).

## v0.10.1 — calm compact UX and RAW-mode diagnostics

- The title bar owns the application name. A single short PC Time/Sync
  strip remains above the NTP Explorer.
- The server table has one shared numeric layout, consistent column/header
  alignment, 13-DIP readable value typography, bounded height, and subtle
  hover only. Event History shows local `HH:mm:ss` plus the event; complete
  timestamp/error text remains in tooltips.
- Add and Remove windows use a shared dialog chrome and `SizeToContent`:
  no fixed-height blank space. The Add dialog rejects empty/invalid and
  duplicate IP addresses without closing and supports Enter/Escape.
- A Windows UDP timeout now retains the **local UDP IP:ephemeral port**
  and expected remote server `IP:123` in peer status diagnostics. It does
  not count a Npcap RAW injection by ARSAS as an accepted NTP reply.
- For ARSAS same-host/interoperability diagnosis, follow
  [ARSAS-NTP-INTEROP.md](docs/ARSAS-NTP-INTEROP.md). In particular,
  `192.16.1.33` is the configured ARSAS server in the supplied log,
  while `192.16.1.255` is a broadcast target, not a unicast NTP server.
  Source IP/port + NTP originate must match the exact AR Fast SNTP request.
- **No changes** to the server election, clock-step thresholds, service
  installation or source IPC commands in this visual/diagnostic milestone.

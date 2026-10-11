# Changelog

All notable **public Windows releases** are listed here. The single Inno Setup installer and its SHA-256 file are published on the [GitHub Releases page](https://github.com/masarray/arfast-sntp/releases). Historical binary assets are never overwritten to incorporate later commits.

## Unreleased — repository maintenance

- The GitHub Pages website and README now feature the four **original Windows Studio screenshots** in [`assets/screenshot`](assets/screenshot) rather than an illustrative UI.
- Added public governance and maintenance material: security policy, contribution guide, Dependabot grouping, site validation, accessibility and discoverability metadata.
- Updated the matched Avalonia Desktop, Fluent, and Inter package references from `12.1.3` to `12.1.4` in [PR #16](https://github.com/masarray/arfast-sntp/pull/16). This **does not** modify or replace the published v0.10.3 installer. New GUI/runtime builds require normal Windows and visual acceptance before another release.

## [v0.10.3](https://github.com/masarray/arfast-sntp/releases/tag/v0.10.3) — 2026-10-11

- Faster safe confirmation: 750 ms follow-up NTP verification with independently validated Mode 4 packets after an initial coherent burst.
- Corrected supervisor polling deadlines and prevented spurious NIC notifications from discarding valid SNTP observations.
- Preserved OFF-idle behavior, link restoration, monotonic clock epoch safeguards, bounded probes, and RATE/DENY source restrictions.
- Windows lab feedback: approximately **1 second clock correction** and **6 seconds to resume after LAN reconnection** on the tested configuration; not a cross-network performance guarantee.
- [PR #6](https://github.com/masarray/arfast-sntp/pull/6) · [Windows CI](https://github.com/masarray/arfast-sntp/actions/runs/38096460110) · [Release notes](docs/RELEASE-NOTES-0.10.3.md)

## [v0.10.2](https://github.com/masarray/arfast-sntp/releases/tag/v0.10.2) — 2026-10-10

- Released network-aware recovery, OFF-idle heartbeat, clock-epoch protection, and KoD backoff handling.
- Aligned Windows and Inno Setup versions, direct latest-release download URLs, and GitHub Pages documentation.
- [PR #4](https://github.com/masarray/arfast-sntp/pull/4) · [PR #5](https://github.com/masarray/arfast-sntp/pull/5) · [Release notes](docs/RELEASE-NOTES-0.10.2.md)

## [v0.10.1-preview.1](https://github.com/masarray/arfast-sntp/releases/tag/v0.10.1-preview.1) — 2026-10-10

- First public Windows x64 **preview**, with Avalonia Studio, background SNTP Windows Service, an editable IPv4 source catalog, live RTT/offset and diagnostic status history.
- Preview was unsigned and intended for isolated engineering/test PCs.
- [Release notes](docs/RELEASE-NOTES-0.10.1-preview.1.md)

## Scope and verification

AR Fast SNTP is an IPv4 unicast SNTP client, **not** a complete RFC 5905 reference clock implementation or a certified industrial timing system. Builds are compiled on Windows CI without stepping the CI host clock. See [project status](docs/PROJECT-STATUS.md), [lab acceptance](docs/LAB-ACCEPTANCE.md), and [open industrial certification work](https://github.com/masarray/arfast-sntp/issues/2).

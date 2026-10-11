# Project status — 2026-10-11

This is a **release and repository handoff**, not a declaration of industrial certification. It distinguishes the public installer from future `main` commits.

## Published product

| Item | Canonical source |
|---|---|
| Latest public release | [v0.10.3](https://github.com/masarray/arfast-sntp/releases/tag/v0.10.3) |
| Release source tag | `v0.10.3`, commit `d20920b2b0e91369cdb858edda88c822446fde8b` |
| Verified Windows release CI | [#38096460110](https://github.com/masarray/arfast-sntp/actions/runs/38096460110) |
| Single Inno installer | [Latest Windows x64 EXE](https://github.com/masarray/arfast-sntp/releases/latest/download/ARFastSNTP-Setup-Windows-x64.exe) |
| Integrity checksum | [Latest SHA-256 file](https://github.com/masarray/arfast-sntp/releases/latest/download/ARFastSNTP-Setup-Windows-x64.exe.sha256) |
| Official website | [Landing page](https://masarray.github.io/arfast-sntp/) and [Download page](https://masarray.github.io/arfast-sntp/download.html) |
| Original screenshots | [`assets/screenshot`](../assets/screenshot) and the [README gallery](../README.md#real-windows-studio-screenshots) |

**Do not retag, republish or overwrite v0.10.3 assets** to incorporate unrelated site, docs, or dependency changes. The versioned installer must stay reproducibly attributable to the above source commit.

## Working engineering baseline

The accepted v0.10.3 SNTP engine retains the user's observed ~1-second clock correction and ~6-second post-LAN-reconnection recovery on their Windows lab configuration. These are observed times, not guarantees. Keep the following invariant for future changes:

- One Windows Service and one clock-step authority, bounded NTP probe concurrency, conservative source consensus and KoD RATE/DENY/RSTR handling.
- No UDP probes or Windows clock writes when every source is OFF; do not confuse idle service with an offline service.
- On NIC reconnect/clock-epoch change, disregard old measurements, measure fresh NTP replies, and verify actual corrections from fresh post-step samples.
- Never build public releases from old `main` or a detached, unverified preview; require successful Windows CI from the intended release source SHA.
- Do not modify this tested engine just to make screenshot/website/README changes.

## Source vs released binary

The repository can contain maintenance changes **after the stable release tag**. For example, the grouped Avalonia `12.1.4` patch-only dependencies were merged via [PR #16](https://github.com/masarray/arfast-sntp/pull/16) after [successful Windows CI](https://github.com/masarray/arfast-sntp/actions/runs/38101009556), but they are **not included** in the immutable v0.10.3 release assets. Treat those commits as unreleased source; check window appearance/resize/DPI before promotion to a future installer.

## Verified repository governance

- Public [README](../README.md), [contributions](../CONTRIBUTING.md), [security policy](../SECURITY.md), [support](../SUPPORT.md), [changelog](../CHANGELOG.md), and original screenshots.
- [Windows CI](https://github.com/masarray/arfast-sntp/actions/workflows/windows-ci.yml), [site checks](https://github.com/masarray/arfast-sntp/actions/workflows/site-ci.yml), signed-release **provenance constraints** and SHA-256 checks. **Note: executable itself is unsigned.**
- Versioned Inno Setup and .NET file metadata; GitHub Latest Release downloads on README and the website.

## Intentionally unresolved, not a blocker to closing this chat

[Issue #2](https://github.com/masarray/arfast-sntp/issues/2) remains an open **future industrial hardening scope**, not a broken implementation claimed fixed: Authenticode publisher identity and SmartScreen reputation, full upgrade/rollback matrix, long-running soak, Windows DPI/accessibility coverage and GPS/NTP/ARSAS interoperability certification. Production SCADA/SOE/historian clock use is **out of the accepted release scope**.

### GitHub owner-only configuration

Repository metadata and branch governance are **administrative account settings**, not source files. Confirm in GitHub UI:

- **About**: describe this as an IPv4 SNTP Windows Studio + Windows Service, not merely “Fast SNTP Daemon”; set homepage to the official Pages URL; add accurate repository topics.
- **Settings → Branches / Rules → Rulesets**: require PR review and passing **Windows CI** and **Public site checks** when applicable for `main`, disallow force-push and deletion. Configure workflows so a skipped path-filtered check cannot block every documentation-only PR.
- **Settings → Security**: enable private vulnerability reporting and dependency alerts if your plan/repository configuration supports them.

No repository automation should claim these owner-only settings are applied until GitHub confirms them.

# AR Fast SNTP — release status and engineering baseline

AR Fast SNTP is a free, open-source SNTP client for engineering workstations, laboratory computers, and learning.

## Releases

- **Windows stable:** [Latest verified Windows x64 installer](https://github.com/masarray/arfast-sntp/releases/latest/download/ARFastSNTP-Setup-Windows-x64.exe), with a companion [SHA-256](https://github.com/masarray/arfast-sntp/releases/latest/download/ARFastSNTP-Setup-Windows-x64.exe.sha256).
- **Linux beta:** [Linux x64 prerelease](https://github.com/masarray/arfast-sntp/releases/tag/linux-v0.1.0-beta.1). Windows and Linux are released separately.
- **Windows v0.10.4 change summary:** [Release notes](RELEASE-NOTES-0.10.4.md).
- **CI:** [Windows](https://github.com/masarray/arfast-sntp/actions/workflows/windows-ci.yml), [Linux](https://github.com/masarray/arfast-sntp/actions/workflows/linux-release.yml), [public website](https://github.com/masarray/arfast-sntp/actions/workflows/site-ci.yml).

## Working synchronization baseline

The previously accepted Windows SNTP engine demonstrated approximately one-second time correction and approximately six-second recovery after reconnecting Ethernet on the test PCs. These are observed results, not fixed time limits for every network.

- One Windows Service controls local clock adjustment.
- Server replies are validated before accepting a time source.
- Reconnection and clock discontinuity invalidate stale measurements.
- Maximum 16 IPv4 unicast sources; synchronized state and diagnostics are visible in Studio.
- The public installer uses the same application identity across upgrades.

## Maintenance and release hygiene

- Use descriptive **vendor-neutral** wording in website, docs, diagnostics and comments.
- Keep third-party licenses and provenance notices intact.
- Require clean Windows CI, site checks, and repository terminology checks before a new stable release.
- Do not mutate existing release assets or replace their tags.
- Source in `main` may include an independent experimental Linux beta; its prerelease does not replace the latest Windows stable release.

For optional longer-running validation, publisher identity and advanced certification tasks, see the [engineering roadmap](ENGINEERING-ROADMAP.md).

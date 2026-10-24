# AR Fast SNTP v0.10.4 — Windows x64 stable

**Simple time synchronization for engineering PCs.** Install AR Fast SNTP, enter a trusted SNTP server IPv4 address, and watch the connection and clock synchronization status in the desktop application.

## What's improved

- Clearer, vendor-neutral information across the website, README, historical documentation and status messages.
- A streamlined quick-start guide for Windows and a separate link to the Linux beta.
- A PowerShell helper that verifies the SHA-256 hash of the official Windows installer before opening setup.
- Automated repository terminology checks to keep public copy independent of third-party relay, device or clock-service brands.
- Prior repository maintenance consolidated into this installer: updated desktop UI package versions and clearer GitHub Pages presentation.

## Reliability and compatibility

The Windows SNTP protocol and synchronization policy are **not changed** by this release. Source reply validation, bounded probing, monotonic clock-change protection, persisted server IP settings, automatic network recovery and one service-owned clock correction path remain in place.

The Windows installer keeps the same application identity so existing users can install the new version over an earlier release. The public binary is self-contained; no separate .NET runtime is required.

## Install

Download the official `ARFastSNTP-Setup-Windows-x64.exe` and matching `.sha256` from this GitHub release. Run setup, choose the SNTP server IP address, and open AR Fast SNTP from Start.

**Platform:** Windows x64. The [Linux x64 beta](https://github.com/masarray/arfast-sntp/releases/tag/linux-v0.1.0-beta.1) is distributed separately.

## Testing and scope

The build and regression suite run in GitHub Actions on the exact merged source. The Windows clock is not changed during CI. The original four-PC engineering experience measured rapid correction and network recovery; results can vary with network and time-server conditions.

Use a computer where you have permission to adjust the clock. For publisher signing, extended soak testing and critical infrastructure deployment qualification, see the [engineering roadmap](ENGINEERING-ROADMAP.md).

**Source:** https://github.com/masarray/arfast-sntp · **License:** GPL-2.0-or-later.

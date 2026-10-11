# Security policy

## Supported releases

The [latest GitHub Release](https://github.com/masarray/arfast-sntp/releases/latest) is the current supported public baseline. Older release/preview branches may not receive security backports.

## Reporting

Do not post exploitable findings or sensitive machine/network data in a public issue. Prefer GitHub's private **Report a vulnerability** / Security Advisory channel **if enabled** for this repository. Otherwise, establish private contact through the maintainer's GitHub profile before sharing sensitive technical details. Provide a redacted reproducer, affected tag, Windows version, and estimated impact. No specific response-time or bug-bounty SLA is promised.

## Clock and installation safety

AR Fast SNTP runs a Windows Service and can request privileged local clock adjustments when installed in normal synchronization mode. For monitor-only testing use the installer option `/LABSYNC=0`. Do not run system time-stepping tests on production SCADA/SOE/historian hosts.

The [official installer](https://github.com/masarray/arfast-sntp/releases/latest) is **unsigned**, with a SHA-256 checksum companion. A checksum helps detect byte changes but does not authenticate the publisher by itself. Do not disable Windows Defender, SmartScreen or organizational security policy just to install this project. See [Issue #2](https://github.com/masarray/arfast-sntp/issues/2) for industrial hardening and signing work.

# Historical engineering draft — GUI and background service plan

This is an archived planning document from an earlier project milestone. It is preserved for contributors, not intended as a current setup guide.

For current use: [quick-start guide](../QUICK-START.md) · [project README](../../README.md) · [official releases](https://github.com/masarray/arfast-sntp/releases)

---

# Lightweight Windows SNTP dashboard and daemon architecture

The Windows daemon and GUI must be separate processes: synchronization must continue when the dashboard is closed.

- Canonical SNTP worker: one poll loop per configured server, bounded samples, no unbounded queues, monotonic discontinuity detection, cancellation-aware UDP requests.
- Single-flight/coalescing: collapse duplicate refresh signals into one pending poll; never spawn parallel time setters.
- State: Offline / Reachable / Responding / Validated / Synchronized / Correcting / Rejected. These are distinct states, not just one green lamp.
- Transaction indicators: request count, valid response count, rejected count, last response time, stratum, RTT, offset, last correction UTC, and correction outcome.
- Broadcast indicator: OFF/UNKNOWN unless a separate broadcast/multicast listener is implemented. Ordinary SNTP client-server UDP request/reply does not prove NTP broadcast availability.
- Read-only GUI: WinForms or WPF, status refresh 2–4 Hz from one atomic snapshot, server clock display as estimated server UTC with uncertainty, bounded correction event ring.
- Single writer: Windows Service owns clock setting. GUI must never directly set Windows time.
- Time stepping policy: disabled by default, opt-in certification-only, enforce privilege and explicit allowlist of server IP, protect production SCADA SOE/historian.
- On server KoD RATE: exponential backoff; DENY/RSTR: disable peer. No retry storms.
- Security: unauthenticated SNTP is susceptible to spoofing; isolated trusted LAN and server IP allowlist are mandatory for forced clock steps.
- No service or GUI is claimed implemented by this plan; milestones require compile, deterministic simulation, installer, and real GPS acceptance tests.

References: RFC 5905, NTPsec polling design, Microsoft .NET Windows BackgroundService documentation.

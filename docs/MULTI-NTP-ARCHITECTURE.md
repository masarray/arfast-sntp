# AR Fast SNTP — multi-source engine architecture

> Current public baseline: v0.10.3. This document describes engineering behavior, **not** industrial/SCADA timing certification.

This milestone implements a **bounded NTP client for a trusted isolated
Windows lab**, not a general Internet NTP daemon or RFC-5905-complete clock
discipline. Do not use time stepping on a production SCADA/SOE host.

## Canonical components

- `PeerCatalog`: atomic, service-owned `ProgramData/ARFastSNTP/peers.json`;
  maximum 16 normalized IPv4 addresses; exact identity, no duplicates;
  persistent Enabled per server. On upgrade, seeds the legacy `--server`
  only if no catalog exists. It never overwrites an existing list.
- `PeerControlService`: one local Windows named pipe, ACL-guarded to
  LocalSystem/Administrators/INTERACTIVE; fixed, length-bounded JSON
  operations LIST, ADD, DELETE, ENABLE, DISABLE. GUI never modifies SCM.
  Non-Windows and network access are not enabled.
- `MultiPeerWorker`: one clock authority, one bounded polling supervisor,
  at most 4 concurrent requests, per-peer exponential backoff, no
  long-lived thread per server. Source updates wake the worker without
  service restart or installation.
- `PeerElection`: pure selection function, independently regression-tested.
  For >=2 valid responders, require a strict majority agreeing in an
  overall 250 ms interval. With exactly 2 valid responders that disagree:
  hold correction. For a sole responder: require repeated samples; mark
  degraded (the authorized isolated LAN is the trust boundary).
  Previous agreeing source remains selected to avoid flip-flopping.
- Only the peer elected in the current generation can request
  `PeerCatalog.TryStep`. Removal/disable/edits and time correction share
  a lock. Previous batch results are invalidated on a successful time step.
- `StatusSnapshot.Peers`: independent enabled, status, offset, stratum,
  RTT, local-time-convertible server UTC, selected marker and error.
  The legacy single-peer status fields remain for old readers.

## Operator workflow

Install the service **once**. On a clean install, enter the first NTP
Server IP. On an upgrade, the installer skips IP setup if the canonical
server list already exists. Studio supports Add/Delete and per-row
Enable/Disable without UAC, SmartScreen or service restart. Changes are
stored before acknowledgement; a full installer upgrade preserves the
catalog and service identity.

Studio visual order: single compact PC Time / Sync Status strip, NTP table
and expandable event history. The resizable window uses bounded list/history
scrolling without changing native window bounds each time the peer catalog
refreshes. Source enable and reply-status colors differ.

## Election examples

- One trusted GPS server: repeated coherent samples allow correction;
  service reports degraded single-source operation.
- 3 servers at +2ms, +4ms and +8000ms: agree on first 2; third Outlier.
- 2 servers at +2ms and +8000ms: no consensus, no step.
- 1 of 3 reachable after outage: after repeated coherent samples can be
  used as degraded failover in this **authorized isolated lab mode**.
- Source A disabled or deleted while a probe is outstanding: generation
  changes, stale measurement ignored and no step from A can start.
- All sources OFF: no NTP UDP requests and no Windows time correction.

## Limitations and acceptance requirements

The selection policy is deliberately conservative but is **not** a full
RFC 5905 intersection/cluster/combine implementation, nor does it support
NTS (RFC 8915), authenticated packets, frequency discipline/slew, root
dispersion/uncertainty ranking, broadcast packet capture, IPv6 or DNS.
RTT/stratum are telemetry and quality tie-breakers, not proofs of accuracy.
A sole unauthenticated source cannot establish absolute correct time.
These limitations must not be hidden by the GUI.

Before claiming industrial production certification: verify 1/2/3/many-server cases against real GPS/NTP
devices, simulated conflicting clocks and KoD rate limits, reload after
reboot, add/remove during active requests, rollback/upgrades from 0.9.x,
Windows Service/pipe ACLs, visual DPI 100–200%, and resource stability
during a long soak test. CI regression tests never alter runner clock.

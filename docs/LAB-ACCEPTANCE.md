# AR Fast SNTP - Isolated Windows Lab Acceptance

Use a disposable certification Windows test PC, **not** any active SCADA server,
SICAM SCC primary, historian, engineering workstation attached to operations,
or production network. Photograph/capture results for each test.

## Preconditions

- GPS/SNTP server is reachable at 192.16.1.175, UDP 123 on isolated LAN.
- Its UTC time, time zone, stratum and health are independently confirmed.
- Conflicting clock-setting services are stopped or disabled explicitly by
  the authorized tester, **not** by the application installer.
- Installer and ZIP are reviewed and obtained from a successful CI run.
- Administrator authorization for service installation and SetSystemTime.

## Acceptance sequence

1. Install **monitor-only**, open dashboard, confirm 3 distinct indicators:
   peer live; valid SNTP transactions; PC actually within 100ms of peer.
   Confirm broadcast reads **UNKNOWN / not measured**.
2. Disconnect Ethernet: transactions should time out and counters advance;
   dashboard should show offline/stale rather than green.
3. Reconnect: watch requests, replies, state recovery and estimated server clock.
4. Uninstall monitor-only; confirm service removed but status evidence retained.
5. Ensure isolated PC, confirm no competing clock setter, install lab correction
   using the **separate warning-gated** installer.
6. In an isolated window, change PC date a few minutes then by a few years
   (test each direction) and measure time-to-recovery. Confirm worker runs with
   GUI closed and reconnect recovers without restarting the service.
7. Observe exactly one or appropriately spaced corrections; the following
   SNTP probes must verify the adjustment, not merely report successful API call.
8. Test Windows Service restart and automatic startup after reboot.
9. Uninstall and ensure lab correction is no longer active. Restore approved
   Windows Time policy and the correct PC date before reconnecting to SCADA.

## Failure gates

- Failed/invalid originate echo, LI=3, KoD, or suspicious RTT must not
  authorize a correction.
- No protocol packets or correction when installer is run in check-config mode.
- An existing ARFastSNTP service must never be overwritten silently.
- Unsigned unauthenticated SNTP has a spoofing risk: physical isolation remains
  a prerequisite for forced clock adjustments.

## Evidence to capture

- Before/after PC UTC and independently verified GPS UTC.
- Wall-clock recovery duration and correction event details from dashboard.
- Request/accepted/rejected counts, RTT, offset, server stratum.
- Service startup/restart, network loss/recovery and resource use.
- Whether any time-setting service conflict occurred.

**CI passing is NOT an acceptance of physical or production SCADA behavior.**

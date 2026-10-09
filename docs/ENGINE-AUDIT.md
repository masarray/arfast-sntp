# Engine audit / milestone 2

## Verified
Initial Windows CI run 37883442675 completed successfully. This confirms build/publish, not protocol or time-stepping correctness.

## Blocking issue before real-world certification
The initial NTP response decoder chooses an era using the **local Windows clock**. When an operator manually sets Windows time to 2023 and the GPS server is in 2026, this is normally within the same NTP era, but around the 2036 rollover and for large historical jumps it can choose the wrong era. A hard-coded reference year is not a production-quality remedy either.

**Required design:** keep a trusted server-era anchor from a previous validated sync, persisted with integrity controls and independent of Windows wall-clock; for cold start without anchor, require an operator-configured plausible server year range (e.g. 2025–2035) and reject ambiguous eras. Do not silently trust Windows time to resolve server era.

## Other required engineering work
- Replace wall-clock-plus-Stopwatch receive timestamp with monotonic interpolation anchored to a high-resolution clock sample at send and receive, with explicit uncertainty bounds.
- Add clock discontinuity detection (wall time versus Stopwatch elapsed) and discard in-flight samples across jumps.
- Do not reuse old offset measurements after a clock change.
- Implement Windows Service lifecycle and structured Event Log/JSON logs; avoid tight restart loops.
- Add deterministic NTP simulator tests: originate mismatch, LI=3, stratum 0/16, replay, malformed packets, time jumps, 2036 era rollover, packet delay, and KoD.
- Implement configurable conservative operational policy; never step the clock on a live SCADA computer without approval.
- Prevent conflicts with Siemens/Meinberg/W32Time clock discipline.
- Test against the actual GPS server on an isolated certification PC.

## CI strategy
One Windows build per milestone PR update, with a short compile + deterministic test stage. Publish artifact only after tests pass. Avoid CI loops from documentation-only changes.

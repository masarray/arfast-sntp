# AR Fast SNTP — Windows-first product migration plan

**Decision:** Windows x64 only until a complete Install -> Sync -> Monitor
acceptance run passes. Avalonia is only the desktop presentation shell; this
decision does **not** schedule Linux or macOS work.

## Verified baseline (must not regress)

Existing .NET 8 worker polls 192.16.1.175 with validated SNTP transactions,
can correct large Windows time offsets when the approved service is configured,
and keeps running independently of the GUI. The previous WinForms dashboard
and C# service setup are retained as fallbacks during the migration.

## P8 — Windows Avalonia Studio using Arssyut tokens (this milestone)

- Pin and reuse the upstream Arssyut colors/spacing/type resources.
- Present live worker status read-only from the canonical ProgramData snapshot.
- Show full server/PC UTC date and clock, valid reply, offset, RTT, stratum,
  request counters, last event and service correction mode.
- Reuse the current INSTALL & SYNC setup executable, never run clock setters
  from the GUI.
- Keep existing WinForms dashboard buildable and artifact-bundled until Studio
  passes real visual acceptance on the user's PC.
- **Single peer only:** the Explorer shows exactly what the existing worker
  supports. Add Server is disabled, with an explicit reason, until real
  multi-peer scheduling and config are implemented.

## P9 — Canonical multi-peer worker and configuration

- Define bounded canonical config file under ProgramData (atomic rewrite;
  versioned schema, validated IPv4 + uniqueness, enable/priority).
- A single clock-setting authority evaluates all enabled peers.
- Independent bounded per-peer health; coalesce refresh requests; limit
  concurrent UDP requests and discard stale replies.
- Selection uses health, stratum, RTT, jitter, consistency and priority;
  reject false consensus and failed/denied servers.
- Toggles and Add Server in Studio become functional only after service IPC
  and config apply/reload tests are green.
- Explicitly distinguish unicast server timestamps from broadcast traffic.
  Broadcast remains "not measured" until a dedicated listener exists.

## P10 — Inno Setup smart Windows installer

- Replace multiple setup windows and PowerShell/CMD UX with one Inno
  `ARFastSNTP-Setup.exe`, using Administrator approval once.
- Detect existing owned ARFastSNTP SCM registration, preserve validated
  config on upgrade; coordinate stop/replace/start and rollback on failure.
- On success start service automatically and launch Studio in user's session.
- Avoid silently changing non-owned time services.
- Support unattended deployment with explicit server/mode config,
  installer logging, and uninstall that preserves diagnostic evidence.
- Test clean install, install over monitor-only, in-place upgrade, reboot,
  service reconnect and controlled offline failure on disposable VMs.

## CI efficiency and release gates

- One Windows CI lane; reuse .NET restore caches when available.
- Test engine, schema and UI build before self-contained publish.
- No GitHub runner service install and never set CI runner's clock.
- Publish artifact only after build, deterministic regressions and packaging
  validation pass.
- Lock source/commit + license provenance for reused design resources.
- Perform visual acceptance at 100/125/150/200% Windows DPI and resource
  measurements (idle CPU/RAM/socket count) before replacing WinForms.

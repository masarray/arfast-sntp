# AR Fast SNTP — Windows x64

License: GPL-2.0-or-later.

Installer v0.9.1: one Inno wizard, server IP entry, automatic upgrade of
the canonical ARFastSNTP Windows Service, then open Avalonia Studio.
Existing PowerShell/WinForms/Inno layouts are recognized by an allowlist.
The installer backs up the old executable, service command, startup mode,
and running state. On failure, rollback is attempted and diagnostics remain
in ProgramData.

The Avalonia monitor now shows PC UTC, sync status, a compact NTP table,
and a collapsible 16-event history. It no longer displays a user-facing
reference-year field or an Install & Sync button. Server time is inferred
from validated unicast replies; broadcast is not claimed without measurement.

Only one server is supported by the current worker. Multi-server toggle,
selection, and automatic failover remain a separate engine milestone.
An unowned service is never silently modified.

See docs/INSTALLER-UX-ACCEPTANCE.md for regression and field test cases.
Windows CI never changes its own clock. Physical install/upgrade and
visual DPI tests are still required before release promotion.

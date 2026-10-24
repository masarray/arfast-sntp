# AR Fast SNTP — Pre-release acceptance

**Public Windows installer:** v0.10.3, Avalonia Studio + one Windows Service, up to 16 unicast IPv4 NTP sources. Public availability does **not** imply industrial/SCADA production certification; the following extended acceptance gates remain open.

## Evidence collected
- Actual Windows test screenshots show two public NTP IPv4 addresses returning validated stratum-3 samples. A small (+179.4 ms) Windows correction was logged in an earlier test.
- Diagnostic JSON verified coherent sources and source-election decisions. Windows CI checks .NET builds, non-stepping protocol tests, service startup smoke, and Inno compilation.
- A physical ARSAS raw link-layer reply to the Windows UDP socket has **not** been proven. See [ARSAS-NTP-INTEROP.md](ARSAS-NTP-INTEROP.md).

## Extended certification gates — open
1. Visual interaction on real Windows: history collapse/expand, resize/maximize/restore, 1/6/16 rows, Add/Delete, RTT tooltip, 100/125/150/200% DPI.
2. Independent-reference NTP consensus, true conflict/outlier handling, failover, stale-sample rejection, and corrected-clock re-verification.
3. Windows certification PC: large clock jump, TLS/endpoint protection interactions, competing Windows Time updates, upgrade, rollback and clean uninstall.
4. Long-running resource stability, code-signing strategy and installer reputation. Production SCADA or SOE is not an acceptable clock-step test host.
5. Verify executable/taskbar/installer icons, brand mark, README, landing page and manifest all use [`assets/favicon`](../assets/favicon).

CI green is not real UI proof or an approval to deploy on production timing systems.

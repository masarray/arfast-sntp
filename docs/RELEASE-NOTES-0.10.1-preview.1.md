# AR Fast SNTP v0.10.1 Preview 1

**First public testing preview · Windows x64 · unsigned Inno installer**

This release packages the **Windows CI #49 SUCCESS** executable from Git commit `ef23e4efa0905334c0a7f58c1b03a6573802ce3f`, validated in the original project before it was renamed to `masarray/arfast-sntp`.

## Included
- A compact, branded Avalonia Windows Studio and one permanent Windows Service.
- Up to 16 configured IPv4 NTP servers; Add/Delete/Enable/Disable without reinstalling the service.
- Source health, selected source, stratum, offset, server time, **RTT with tooltip** and status history.
- Bounded SNTPv4 probing, source election/consensus checks, outlier and failover logic.
- Inno Setup x64 installer and diagnostic export.

## Download and verify
Download `ARFastSNTP-Setup-Windows-x64.exe` and `ARFastSNTP-Setup-Windows-x64.exe.sha256` from this release's **Assets**. In PowerShell, run:

```powershell
Get-FileHash .\ARFastSNTP-Setup-Windows-x64.exe -Algorithm SHA256
```

Compare the reported hexadecimal SHA-256 digest to the `.sha256` file. This is a **code-unsigned** preview; verification of the publisher identity is not available.

## Important safety information

**For trusted isolated lab/test PCs only. Not for production SCADA, SOE, historian or any critical system.** The default installer activates clock correction when it has an eligible reference. To test monitor-only operation, run the installer with `/LABSYNC=0`. Administrator approval is required at install time.

Reported real tests accepted two public public provider NTP IPv4 replies and a small clock correction. They do **not** establish GPS-grade precision, independent sources, robust recovery from a very large time jump, or production acceptance.

## Outstanding validation

Windows DPI 100–200% and live GUI behavior; real 1/2/3-source failover and conflicting independent servers; long-running resource soak; upgraded service rollback/uninstall; large clock-step barrier and antivirus/TLS effects. ARSAS/raw link-layer same-host Mode 4 delivery remains unverified. Track these in [Issue #2](https://github.com/masarray/arfast-sntp/issues/2).

No public **stable** release claim is made for this version. Feedback, redacted diagnostics and screenshots are welcome.

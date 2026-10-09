# AR Fast SNTP — Windows certification prototype

Lightweight .NET 8 Windows SNTP worker and **read-only** WinForms monitor.

> **OT safety:** Do NOT run forced stepping on production SCADA/SOE/historian hosts.
> Only use --allow-step with --certification-mode on an isolated and approved test PC.
> Disable conflicting Windows Time / Meinberg / Siemens NTP clock setters first.
> NTPv4 client-server traffic is unauthenticated; use a trusted isolated LAN.

## Run / evaluate
- Build: `dotnet build src/ARFastSNTP/ARFastSNTP.csproj -c Release`
- Monitor only: `ARFastSNTP.exe --server 192.16.1.175 --server-year 2026`
- Approved isolated certification only:
  `ARFastSNTP.exe --server 192.16.1.175 --server-year 2026 --certification-mode --allow-step`
- Launch `ARFastSNTP.Dashboard.exe` to view live status. Dashboard is view-only.
- Status file: `%ProgramData%\ARFastSNTP\status.json`.
- Win x64 binaries: download Actions artifact `ARFastSNTP-Windows-x64`.
- Tests: `dotnet run --project tests/ARFastSNTP.Tests -c Release`.

The worker can be registered through the Windows Service Control Manager only after
inspecting the artifact and permissions on a dedicated test PC. Example (elevated,
with EXE installed in C:\Program Files\ARFastSNTP):

```cmd
sc.exe create ARFastSNTP binPath= "\"C:\Program Files\ARFastSNTP\ARFastSNTP.exe\" --server 192.16.1.175 --server-year 2026" start= auto
sc.exe start ARFastSNTP
```

Windows Service startup uses the standard Microsoft.Extensions.Hosting.WindowsServices
lifetime. **Do not enable clock stepping by default.** The dashboard remains separate
so stopping the GUI does not stop synchronization.

## Monitoring semantics
- **Server online** requires a recently validated server response, not an ICMP ping.
- **Valid transaction** requires a recent, validated NTP round-trip.
- **Synchronized** requires measured offset within 100ms and fresh, coherent samples.
- **Broadcast** is `Not measured` (client/server polling is NOT broadcast proof).
- Server clock is an **estimate** from last sample, not a GPS reference display.
- Accepted/rejected counters are process-local, with bounded correction history (16).
- The server era reference defaults to 2026 and validates responses inside a ±15-year
  plausible range; set --server-year to match your deployment era.

## Known limits before release
No installer, no service-integration or physical GPS acceptance tests yet.
Clock setting requires Windows privilege and can fail if permission is absent.
No native NTP authentication; robust against malformed packets, **not** MITM spoofing.
Clock changes while responses are in flight are rejected; end-to-end recovery still
needs on-device testing. Treat this artifact as a lab prototype.

# Contributing to AR Fast SNTP

Contributions are welcome. The current repository is [masarray/arfast-sntp](https://github.com/masarray/arfast-sntp); old `arintp` artifacts are archival history.

## A focused, safe workflow

1. Open a reproducible issue before broad behavioral changes. Include the application version, Windows edition, source configuration and **redacted** diagnostics. Do not disclose private IP topology, secrets or other site information.
2. Use a separate feature branch and open a pull request. Keep website/documentation changes separate from clock, service and NTP engine changes.
3. With .NET 8 on Windows, run:

```powershell
dotnet build src/ARFastSNTP.Studio/ARFastSNTP.Studio.csproj -c Release --warnaserror
dotnet run --project tests/ARFastSNTP.Tests/ARFastSNTP.Tests.csproj -c Release
python tools/check_site.py
```

4. Describe the functional delta, tests and Windows Service/installer impact. Green CI is not physical NIC or Windows clock acceptance; use a disposable authorized lab PC for real clock correction tests.
5. Never mutate the files of an existing GitHub release. Bump the version and validate the source and CI artifact before creating another stable release.

Do **not** test clock steps on operational SCADA, SOE, historian or other critical time domains. The Windows installer is unsigned: do not bypass protection warnings. Vulnerabilities should use [SECURITY.md](SECURITY.md), not a public issue.

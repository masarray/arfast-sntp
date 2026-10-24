# Install AR Fast SNTP using WinGet

WinGet is the Windows Package Manager. It makes it easy to install and update applications from a terminal.

## Windows installation today

[Download the official Windows installer](https://github.com/masarray/arfast-sntp/releases/latest/download/ARFastSNTP-Setup-Windows-x64.exe), run it, and enter your SNTP server IPv4 address during setup.

## One-command installation (once Microsoft accepts the package)

The AR Fast SNTP WinGet manifest is prepared in this repository and must first be accepted into the Microsoft [winget-pkgs](https://github.com/microsoft/winget-pkgs) community catalog. Until that approval is completed, the following command is **planned**, not yet available from the catalog:

\`\`\`powershell
winget install --id masarray.ARFastSNTP --exact --interactive
\`\`\`

The \`--interactive\` flag lets you enter your server IP address in the existing Windows setup wizard. After installation, start **AR Fast SNTP** from the Start menu.

Once indexed, updates will use:

\`\`\`powershell
winget upgrade --id masarray.ARFastSNTP --exact --interactive
\`\`\`

## How maintainers publish to WinGet

This repository includes the multi-file community manifest under \`packaging/winget/manifests/m/masarray/ARFastSNTP/0.10.3/\`. The installer is the unchanged, versioned Windows v0.10.3 Inno Setup asset. Its SHA-256 is verified from the public GitHub release.

1. Run the **WinGet package validation** GitHub Actions workflow manually to download the current official Windows stable installer, verify the SHA-256, and generate a submission-ready three-file manifest artifact. It also runs automatically on new GitHub Releases, skipping Linux beta and other prereleases.
2. Download the workflow artifact \`ARFastSNTP-WinGet-manifests\`, and submit the three YAML files to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) according to [Microsoft's submission guide](https://learn.microsoft.com/en-us/windows/package-manager/package/manifest).
3. For the first submission, use Microsoft's **WinGetCreate** to create a package PR. WinGetCreate can be installed using \`winget install wingetcreate\`, then \`wingetcreate new https://github.com/masarray/arfast-sntp/releases/download/v0.10.3/ARFastSNTP-Setup-Windows-x64.exe\`. Review its detected metadata, package ID and manifest files before submitting. The checked-in manifest is the source of the intended metadata.
4. After the initial manifest is **merged and indexed** in Microsoft's repository, future versions can be updated using \`wingetcreate update masarray.ARFastSNTP --urls <new-immutable-release-url> --version <version> --submit\`. Microsoft requires a GitHub-authenticated upstream PR. Do not overwrite released assets.
5. Confirm catalog availability with \`winget show --id masarray.ARFastSNTP --exact\` on Windows before documenting that the install command is live.

WinGet validation does not imply Microsoft's approval or code signing. Keep the end-user README focused on using the application; this page contains packaging details.

## Verify locally

\`\`\`bash
python3 tools/winget_manifest.py --check packaging/winget/manifests/m/masarray/ARFastSNTP/0.10.3
\`\`\`

The CI validates these files and verifies the Windows executable's SHA-256 before preparing new manifests. GitHub Actions has read-only permissions for package preparation; no publisher credential is exposed by this workflow.

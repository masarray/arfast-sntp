#!/usr/bin/env python3
"""Create/check deterministic community WinGet manifests for published Inno releases.

No third-party Python packages or mutable latest-release URLs required.
Checks are not a substitute for upstream WinGet validation/review.
"""
from __future__ import annotations

import argparse
import pathlib
import re
import sys

ID = "masarray.ARFastSNTP"
REPO = "https://github.com/masarray/arfast-sntp"
MANIFEST_VERSION = "1.10.0"


def render(version: str, digest: str) -> dict[str, str]:
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version):
        raise ValueError("Expected a stable Windows X.Y.Z release")
    if not re.fullmatch(r"[0-9a-fA-F]{64}", digest):
        raise ValueError("Expected a real 64-digit SHA-256 digest")
    url = f"{REPO}/releases/download/v{version}/ARFastSNTP-Setup-Windows-x64.exe"
    return {
        f"{ID}.yaml": f"""# yaml-language-server: $schema=https://aka.ms/winget-manifest.version.1.10.0.schema.json
PackageIdentifier: {ID}
PackageVersion: {version}
DefaultLocale: en-US
ManifestType: version
ManifestVersion: {MANIFEST_VERSION}
""",
        f"{ID}.installer.yaml": f"""# yaml-language-server: $schema=https://aka.ms/winget-manifest.installer.1.10.0.schema.json
PackageIdentifier: {ID}
PackageVersion: {version}
InstallerType: inno
Installers:
  - Architecture: x64
    Scope: machine
    InstallerUrl: {url}
    InstallerSha256: {digest.upper()}
ManifestType: installer
ManifestVersion: {MANIFEST_VERSION}
""",
        f"{ID}.locale.en-US.yaml": f"""# yaml-language-server: $schema=https://aka.ms/winget-manifest.defaultLocale.1.10.0.schema.json
PackageIdentifier: {ID}
PackageVersion: {version}
PackageLocale: en-US
Publisher: masarray
PublisherUrl: https://github.com/masarray
PublisherSupportUrl: {REPO}/issues
PackageName: AR Fast SNTP
PackageUrl: {REPO}
License: GPL-2.0-or-later
LicenseUrl: {REPO}/blob/main/LICENSE
Copyright: Copyright (c) AR Fast SNTP contributors
ShortDescription: Easily synchronize and monitor your Windows PC clock with an SNTP server IP address.
Description: >-
  An easy-to-use SNTP time synchronization tool for engineering PCs,
  industrial HMI workstations, lab computers and everyday Windows testing.
  Add a trusted NTP server IP address and monitor connection and sync status
  from the Avalonia desktop application.
Moniker: arfast-sntp
Tags:
  - sntp
  - ntp
  - time
  - synchronization
  - engineering
  - automation
  - scada
  - hmi
ManifestType: defaultLocale
ManifestVersion: {MANIFEST_VERSION}
""",
    }


def check(directory: pathlib.Path) -> None:
    expected = {f"{ID}.yaml", f"{ID}.installer.yaml", f"{ID}.locale.en-US.yaml"}
    present = {p.name for p in directory.glob("*.yaml")}
    if present != expected:
        raise ValueError(f"Expected {sorted(expected)}; found {sorted(present)}")
    installer = (directory / f"{ID}.installer.yaml").read_text(encoding="utf-8")
    version = re.search(r"^PackageVersion: ([0-9]+\.[0-9]+\.[0-9]+)$",
                        installer, re.MULTILINE)
    sha = re.search(r"^    InstallerSha256: ([0-9a-fA-F]{64})$",
                    installer, re.MULTILINE)
    if not version or not sha:
        raise ValueError("Malformed installer version or SHA-256")
    outputs = render(version.group(1), sha.group(1))
    for name, content in outputs.items():
        existing = (directory / name).read_text(encoding="utf-8")
        if existing != content:
            raise ValueError(f"Manifest differs from validated template: {name}")
    print(f"PASS: WinGet {ID} v{version.group(1)}: 3 manifests and immutable URL")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", type=pathlib.Path, help="Validate existing files")
    parser.add_argument("--version", help="Stable Windows release version")
    parser.add_argument("--sha256", help="SHA256 of the original Windows EXE")
    parser.add_argument("--out-root", type=pathlib.Path, help="Manifest root folder")
    args = parser.parse_args()
    if args.check:
        check(args.check)
        return
    if not (args.version and args.sha256 and args.out_root):
        parser.error("Specify --version, --sha256 and --out-root to generate")
    target = args.out_root / "m" / "masarray" / "ARFastSNTP" / args.version
    target.mkdir(parents=True, exist_ok=True)
    for name, content in render(args.version, args.sha256).items():
        (target / name).write_text(content, encoding="utf-8")
    check(target)
    print(f"Ready for community submission: {target}")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        sys.exit(1)

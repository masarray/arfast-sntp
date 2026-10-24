#!/usr/bin/env python3
"""Check public-facing repository text for unrelated vendor or device branding.

Keep platform names, dependency identifiers, GitHub URLs and legally required
license/provenance information intact. This is a terminology check, not a
third-party attribution remover.
"""
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
RAW_TERMS = (
    "IED" + "Scout",
    "Omi" + "cron",
    "Sipro" + "tec",
    "Sie" + "mens",
    "Mein" + "berg",
    "Schnei" + "der",
    "Cloud" + "flare",
    "Goo" + "gle",
    "Npc" + "ap",
    "Wire" + "shark",
    "Kas" + "persky",
    "Hita" + "chi",
    "Mits" + "ubishi",
    "Tos" + "hiba",
    "Em" + "erson",
    "Honey" + "well",
    "Rock" + "well",
    "Beck" + "hoff",
    "Mo" + "xa",
    "Wa" + "go",
    "Cis" + "co",
    "Al" + "stom",
    "Nari",
    "GE Vern" + "ova",
    "General Elec" + "tric",
)
PATTERN = re.compile(r"(?i)\b(?:" + "|".join(map(re.escape, RAW_TERMS)) + r")\b")
EXTENSIONS = {".md", ".txt", ".html", ".css", ".js", ".json", ".xml",
              ".cs", ".csproj", ".axaml", ".yml", ".yaml", ".py", ".ps1",
              ".cmd", ".iss", ".sh", ".props"}
SKIP_PATHS = {
    "tools/check_vendor_neutrality.py",
    "third_party/ARSSYUT-GPL-2.0-OR-LATER.txt",  # Licensed upstream text
    "LICENSE",
    "NOTICE",  # Required third-party licensing provenance
    "docs/DESIGN-TOKEN-PROVENANCE.md",  # Required provenance
}


def tracked_files() -> list[str]:
    result = subprocess.run(["git", "ls-files", "-z"], cwd=ROOT,
                            capture_output=True, check=True)
    return [f.decode("utf-8") for f in result.stdout.split(b"\0") if f]


def main() -> int:
    problems = []
    for path in tracked_files():
        if path in SKIP_PATHS or Path(path).suffix.lower() not in EXTENSIONS:
            continue
        source = (ROOT / path).read_text(encoding="utf-8-sig", errors="replace")
        for line_number, line in enumerate(source.splitlines(), 1):
            matches = [match.group(0) for match in PATTERN.finditer(line)]
            if matches:
                problems.append(f"{path}:{line_number}: {', '.join(matches)}")
    if problems:
        print("Vendor-neutral wording check failed:")
        print("\n".join(problems))
        return 1
    print("PASS: repository public text uses vendor-neutral engineering terms.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Dependency-free public-site and README regression checks.

Purposefully never invokes the Windows clock or starts the SNTP service.
"""
from html.parser import HTMLParser
import re
from pathlib import Path
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
PAGES = ("index.html", "download.html")
SHOTS = (
    "Screenshot 2026-10-11 072931.webp",
    "Screenshot 2026-10-11 072958.webp",
    "Screenshot 2026-10-11 073230.webp",
    "Screenshot 2026-10-11 073259.webp",
)
RELEASE = "releases/latest/download/ARFastSNTP-Setup-Windows-x64.exe"
failures = []


class Links(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.urls = []
        self.meta = {}
        self.img = []

    def handle_starttag(self, tag, attrs):
        values = dict(attrs)
        for key in ("href", "src"):
            if values.get(key):
                self.urls.append(values[key])
        if tag == "img":
            self.img.append(values)
        if tag == "meta":
            self.meta[values.get("property") or values.get("name")] = values.get("content")


for page in PAGES:
    text = (ROOT / page).read_text(encoding="utf-8")
    parser = Links()
    parser.feed(text)
    if parser.meta.get("og:image") is None or parser.meta.get("description") is None:
        failures.append(f"{page}: missing Open Graph preview or description")
    if '<link rel="canonical"' not in text:
        failures.append(f"{page}: missing canonical link")
    for img in parser.img:
        if "alt" not in img:
            failures.append(f"{page}: missing image alt attribute")
    for href in parser.urls:
        url = urlsplit(href)
        if url.scheme or url.netloc or href.startswith("#"):
            continue  # external refs cannot be checked offline
        if not url.path or url.path in (".", "./"):
            continue
        target = (ROOT / unquote(url.path)).resolve()
        if target != ROOT and ROOT not in target.parents:
            failures.append(f"{page}: URL escapes repository root: {href}")
        elif not target.is_file():
            failures.append(f"{page}: broken internal asset link: {href}")

readme = (ROOT / "README.md").read_text(encoding="utf-8")
index = (ROOT / "index.html").read_text(encoding="utf-8")
download = (ROOT / "download.html").read_text(encoding="utf-8")
if RELEASE not in readme or RELEASE not in download:
    failures.append("README and download page must link to GitHub Latest EXE")
if RELEASE + ".sha256" not in download:
    failures.append("download page must link to GitHub Latest SHA-256")
if "simulated dashboard" in index.lower() and "not a simulated dashboard" not in index.lower():
    failures.append("landing must not present simulated data as a real screenshot")
if "assets/screenshot/" not in readme or "assets/screenshot/" not in index:
    failures.append("real screenshots must be visible in README and landing")
# Keep beginner and support documentation free of broken local links.
# Check Markdown links and inline HTML assets without depending on GitHub APIs.
PUBLIC_DOCS = (
    "README.md", "SUPPORT.md", "SECURITY.md", "CONTRIBUTING.md",
    "docs/QUICK-START.md", "docs/LINUX-INSTALL.md",
    "docs/PROJECT-STATUS.md", "docs/ENGINEERING-ROADMAP.md",
    "docs/RELEASE-NOTES-0.10.4.md",
    "docs/archive/HANDOFF-2026-10-11.md",
)
MARKDOWN_LINK = re.compile(r'!?\[[^\r\n\]]*\]\(([^)\r\n]+)\)')
INLINE_HTML_LINK = re.compile(r"""(?:href|src)=["']([^"']+)["']""")
for filename in PUBLIC_DOCS:
    document = ROOT / filename
    if not document.is_file():
        failures.append(f"missing user documentation: {filename}")
        continue
    body = document.read_text(encoding="utf-8")
    refs = [m.group(1) for m in MARKDOWN_LINK.finditer(body)]
    refs.extend(m.group(1) for m in INLINE_HTML_LINK.finditer(body))
    for ref in refs:
        parsed = urlsplit(ref)
        if parsed.scheme or parsed.netloc or not parsed.path:
            continue
        target = (document.parent / unquote(parsed.path)).resolve()
        if target != ROOT and ROOT not in target.parents:
            failures.append(f"{filename}: local link escapes repository: {ref}")
        elif not target.exists():
            failures.append(f"{filename}: broken local link: {ref}")

for name in SHOTS:
    file = ROOT / "assets/screenshot" / name
    if not file.is_file():
        failures.append(f"missing canonical screenshot: {name}")
        continue
    data = file.read_bytes()
    if not (data[:4] == b"RIFF" and data[8:12] == b"WEBP"):
        failures.append(f"not a WebP screenshot: {name}")
    if len(data) > 500_000:
        failures.append(f"oversized screenshot (>500kB): {name}")
    url = "assets/screenshot/" + name.replace(" ", "%20")
    if url not in index or url not in readme:
        failures.append(f"not referenced in landing and README: {name}")

if not (ROOT / "LICENSE").read_text(encoding="utf-8").strip().startswith(
    "GNU GENERAL PUBLIC LICENSE"
):
    failures.append("LICENSE must begin with the standard GPL license text")
for needed in ("SECURITY.md", "CONTRIBUTING.md", "sitemap.xml", "robots.txt", ".nojekyll"):
    if not (ROOT / needed).is_file():
        failures.append(f"missing public governance/site file: {needed}")
if (ROOT / "HANDOFF.md").exists():
    failures.append("historical maintainer handoff should not be in the repository root")
for needed in ("docs/QUICK-START.md", "docs/LINUX-INSTALL.md"):
    if not (ROOT / needed).is_file():
        failures.append(f"missing user setup guide: {needed}")

if failures:
    for error in failures:
        print("FAIL:", error)
    raise SystemExit(1)
print(f"PASS: {len(PAGES)} HTML pages, {len(SHOTS)} WebP screenshots, release links, "
      "license, user guides and governance.")

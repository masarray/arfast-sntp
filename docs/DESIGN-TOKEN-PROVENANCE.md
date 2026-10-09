# AR Fast SNTP Studio — Arssyut design system provenance

Copied at pinned GitHub commit `b9bfa882e784560051e8753e691d6662909dd5ad`
from `masarray/arssyut` for the Windows Avalonia Studio shell:

- `src/ui/Arssyut.UI/Design/ArColors.axaml` -> `src/ARFastSNTP.Studio/Design/ArColors.axaml`
- `src/ui/Arssyut.UI/Design/ArTokens.axaml` -> `src/ARFastSNTP.Studio/Design/ArTokens.axaml`
- `src/ui/Arssyut.UI/Design/ArTypography.axaml` -> `src/ARFastSNTP.Studio/Design/ArTypography.axaml`

The three upstream resource files are retained byte-for-byte. This avoids
creating inconsistent secondary palettes and spacing systems. Studio-specific
`SntpControls.axaml` adapts the compact control vocabulary to SNTP status,
without copying recorder-only components or reusing "record red" as a
synchronization-success accent.

Upstream arssyut LICENSE: **GPL-2.0-or-later**. A copy is stored in
`third_party/ARSSYUT-GPL-2.0-OR-LATER.txt`, and included in the Windows CI
artifact. Reuse/distribution must satisfy the applicable upstream license
obligations or be separately authorized by the relevant copyright holder(s).
This document does not claim that the arintp project has received an
alternative license. Public release licensing must be reviewed first.

Upstream UI package baseline from Arssyut:
- Avalonia.Desktop 12.1.3 (MIT)
- Avalonia.Themes.Fluent 12.1.3 (MIT)
- Avalonia.Fonts.Inter 12.1.3 (contains Inter under SIL OFL 1.1)

No loose font files are included.

## Design constraints carried over

- Graphite canvas, three semantic surface layers, neutral white text.
- Spacing scale 4 / 8 / 12 / 16 / 20 / 24 DIP.
- Control heights 28 / 32 / 36 / 40 DIP.
- Radius 6 / 8 / 10 / 12 DIP.
- Type scale 11 / 12 / 13 / 16 DIP.
- Success green = validated/synchronized; warning amber = offset or stale;
  recorder red is not used as a primary SNTP color.
- Prefer quiet compact tables/explorer navigation rather than giant cards.
- Real DPI acceptance at 100%, 125%, 150% and 200%.

# Calm spatial map, nebula.5

## What this run was meant to do

Implement the owner's approved quieter map: smaller folders and labels, thin straight connections, local hover emphasis, and direct folder navigation without the synthetic Open folder node. Produce a Windows installer only after the exact build passes its lifecycle gate.

## What actually changed

Folder glyphs, root scale and typography are smaller. Connections use a constant muted stroke and are clipped around glyphs and final label bounds. Hover draws only a soft local glow and label background. Moving/enlarging hover lenses, floating positions, path glows, dust and their unused code/tests are removed.

Search hover validates the result and marks its nearest visible ancestor under the same chosen root. It adds no scene nodes or connections. Activating the result still navigates to it. Overview roots open directly; previous/next paging still reaches every admitted folder entry. Saved roots, pins, schema and path permissions are unchanged.

## Important technical decisions

- Keep one displayed scene rather than a temporary expanded search scene. Density changes reproject it and remap the validated marker.
- Preserve 44-DIP pointer/automation targets while reducing the decorative glyph size.
- Subtract glyph/label intervals from a straight connection rather than reroute it or draw a hover layer. Work remains bounded by the existing scene cap.
- Announce a marked ancestor as a search-result location, not an exact search match.

## Validation and confidence

**Verified locally:** analyzer-enabled Release build with zero warnings/errors; 203 ordinary tests; 37 UI/renderer/monitoring cases using the actual Skia software renderer. Dense single-folder, compact search, eight-root and enlarged-text captures were inspected; the test suite also captured hidden-control layouts. A 1,400 × 900 rest/hover image comparison changed only the 51 × 55 pixel region around HOA (1,904 pixels), with no changed connectors or other folders. Independent static review accepted the corrected code. Whitespace formatting and diff checks passed.

**Delivery gate:** the standard Windows/Ubuntu CI must run full solution formatting, Release builds/tests, dependency audit and engineering-doc checks. Windows also packages nebula.5 and exercises its exact EXE through installation, saved-state readiness, close/relaunch, retired-file cleanup, settings/unknown-content preservation and uninstall. The build's manifest and `OmniBrille-release-validation.json` are the authoritative artifact-bound record. This source report does not preclaim their outcome.

**Not verified locally:** full solution formatter (the local MSBuild formatting host cannot open its pipe), native Windows installation, physical DPI/GPU, screen readers, actual OneDrive hydration, or real prior-version upgrade. Native/software evidence and synthetic installer fixtures do not establish those manual/hardware claims.

## Problems found and lessons

Independent review caught an obsolete preview guard that could retain a large scene during resize, a misleading ancestor-as-match accessibility announcement, and an asynchronous test assertion. All were corrected; overview/focused-density and root-removal regressions cover the state cases. Initial render helpers also waited for a newly added exact-match node; they now wait for the validated visible ancestor.

The earlier assumption that brighter paths and more hover structure improve location feedback failed this visual review. Retained lesson: prefer stationary local emphasis when the map itself supplies navigation. Evidence is the owner's feedback, pixel comparison and stable-geometry tests; promoted only into the interaction contract and focused regressions, with independent review. No new global engineering rule is needed.

## Documentation and repository

Updated the product overview, architecture, interactions, glossary, testing, packaging/version guidance and ADR 0006 refinement. The component-flow Mermaid remains accurate. Earlier run reports retain their historical claims.

Repository: `Dr-Amit-Lal/OmniBrille`, branch `feature/nebulux-my-files`, starting HEAD `c2a43535b7dc37959c1c7b30e5c339a3d6bc605e`; initial worktree clean. No persistence or native dependency change. The final commit and installer source identity are recorded by Git and the build manifest.

Status: Complete with follow-up. Implementation is locally validated and independently reviewed; deliver the installer only after the exact hosted build and lifecycle checks pass. Physical/native accessibility and real upgrade/cloud checks remain separate evidence.

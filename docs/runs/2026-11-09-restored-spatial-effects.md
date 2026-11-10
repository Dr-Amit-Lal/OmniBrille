# Restored spatial effects, nebula.6

## What this run was meant to do

Correct the nebula.5 visual overcorrection. The owner asked for a cleaner layout and non-overlapping straight connections, and clarified that the original floating, glow and hover effects should remain. Prepare a matching Windows installer after the exact build and lifecycle gates pass.

## What actually changed

Restore the original analytic `GraphMotionPolicy`: gentle bounded floating, a smooth hover lens up to 1.16× and subtle local neighbor response. Restore folder glow/shine and local hover/selection rings. Scene changes animate navigation; hover or selection updates do not restart that transition.

Keep the compact nebula.5 base layout, glyphs and labels. Connections remain thin, neutral and straight, with no added hover routes. Glyph/label clipping now also reserves the local ring envelope, including at rest. Search still marks only a visible item or its nearest visible ancestor; it adds no branches. The synthetic Open folder node remains removed. Sessions, saved roots/pins, schema, path authority and explicit file activation are unchanged. Current package metadata advances to `1.2.0-nebula.6`.

## Important technical decisions

- Restore effects independently of layout/topology. A quieter graph does not require removing the original visual feedback.
- Compute motion from the stable base layout, rather than accumulating offsets between frames or navigation transitions. Root glyphs have no ambient drift.
- Keep connector stroke constant and reserve local ring space for labels/clipping. Hover feedback stays around the item instead of drawing another path across the graph.
- Retain Reduced motion and Reduced effects. Reduced motion disables floating, hover enlargement and navigation animation; Reduced effects lowers glow.

## Validation and confidence

**Verified at documentation handoff:** current prose and version literals were inspected against the changed rendering source and motion policy. The component/state-flow diagram remains accurate. A Python equivalent of the repository link/fence check inspected 63 Markdown files, 198 relative links and one Mermaid diagram with no errors; `pwsh` was unavailable locally, so the canonical PowerShell check remains a hosted gate. `git diff --check` passed. Historical nebula.5 evidence was preserved, including its now-superseded stationary-hover decision.

**Verified locally:** analyzer-enabled Release build completed with zero warnings/errors. All 207 ordinary tests and 41 headless tests passed using the Skia software renderer. Tests cover ambient floating, full-motion hover and refreshed-scene transitions in both themes, a dense 25-node scene sampled across five frames, unchanged membership, a maximum 1.16× lens, non-overlapping labels and usable hit targets. Reduced motion and inactive-window behavior also passed. Whitespace formatting and `git diff --check` passed. Full solution formatting, dependency audit and canonical PowerShell documentation validation remain hosted gates because the local MSBuild formatter cannot open its pipe and PowerShell is unavailable.

**Independent review:** completed with no remaining blocker. Review found that transition snapshots could bake in the active lens and apply it twice; snapshots now contain base geometry only, with a refresh regression. Dark/light hover and dense software captures were inspected: rings and captions clear straight connectors, with no added hover route. The normal tooltip can cover its own caption temporarily. Software captures establish only the tested samples, not physical-display acceptance or a performance benchmark.

**Delivery gate:** run the standard Windows/Ubuntu checks against the final source commit. The Windows job must package nebula.6 and exercise those exact installer bytes through installation, saved-state readiness, close/relaunch, retired-file cleanup, settings/unknown-content preservation and uninstall. The matching manifest and `OmniBrille-release-validation.json` bind artifact identity to results; workflow existence is not evidence of success.

**Not verified here:** native Windows visual acceptance, physical DPI/GPU and motion comfort, real screen readers, OneDrive hydration, a full predecessor upgrade, or interactive Linux/macOS behavior. Software captures and synthetic installer fixtures do not establish those claims. Full-motion performance still needs the final build's measured evidence.

## Problems found and lessons

Nebula.5 treated the owner's rejection of overlapping hover lines as authorization to remove the original motion and enlargement. Its current documentation then made that unintended behavior a contract. This run corrects the current contract while leaving the historical report intact.

Scoped lesson: preserve independently requested visual behavior when changing layout. Evidence is the owner's explicit correction, the earlier motion implementation and independent review. Motion and topology are now checked separately by regressions; no new global rule is promoted. Renderer/test and documentation ownership remained separate to avoid conflicting edits and unsupported validation claims.

## Documentation and diagrams

Updated README, architecture, interaction contract, glossary, testing, release notes/checklist, compatibility, packaging/security version guidance, engineering entry point and ADR 0006. Added this report and its index entry. The component-flow Mermaid needs no topology change. The intent to preserve original effects while cleaning layout is now recorded in the current contract.

## Repository state

Repository: `Dr-Amit-Lal/OmniBrille`; branch `feature/nebulux-my-files`; starting HEAD `87e9e6d7db88eeea2e0d1e662e92c5c950a9355b`. Renderer/policy and test edits were already in progress when documentation work began and were preserved. Documentation handoff does not claim a commit, push or published artifact. Git and the final build manifest must identify the delivered source. No persistence schema, protocol, native dependency or saved-data compatibility change is intended.

Status: Implementation and local validation complete. Exact-build hosted installer qualification is pending at this source commit and must pass before delivery; the final PR evidence and artifact-bound validation record identify that result. Manual platform limits remain separate.

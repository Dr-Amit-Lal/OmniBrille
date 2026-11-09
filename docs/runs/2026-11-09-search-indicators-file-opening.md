# Search indicators and direct file opening, nebula.7

## What this run was meant to do

Show where search matches lie along the existing folder connections, with stronger emphasis for a previewed result. Let the owner directly double-click files in the graph or search results to open them.

## What changed

Search draws small moving points on the existing clipped straight segments that lead toward matches. Previewing a result emphasizes its visible ancestor path. Scene membership, roots, pins and committed navigation remain unchanged by preview. Reduced motion makes the points stationary; Reduced effects suppresses their outer glow.

Graph files already had a double-click activation path; headless pointer coverage now exercises it. Double-click or Enter on a file search result now calls the same guarded activation service. Single selection and hover remain non-opening. **Show in map** preserves the previous ability to navigate to a result's location without opening it. The search-row double-tap handler is attached to the row, preventing a double-click on blank list space from opening a stale selected result. Current-result membership, root authority and file path are rechecked before activation; high-risk file types retain the existing restrictions.

## Decisions and validation

- Reuse visible edge segments instead of introducing extra lines: the owner requested cleaner straight connections and then clarified the indicators should follow them. The closest visible ancestor is used when a deeper result is not in the current scene; an overview with no visible edge can show only its root match badge.
- Keep file activation explicit and share the existing shell/path guard. Search-result membership is rechecked after asynchronous validation to avoid opening a stale result. Show in map remains an independent navigation action.
- The analyzer-enabled Release build passed with zero warnings/errors. Local tests passed: 207 ordinary tests and 45 headless tests, including actual double-clicks in map and search, keyboard activation, stale/deleted search hits, both appearance themes, matching-edge indicators, preview emphasis and reduced motion. Dark/light software captures and a compact search panel were inspected. Whitespace formatting and `git diff --check` passed.
- The first full run exposed a short-window list-height regression from stacking the new action buttons. Placing them side by side restored the existing compact layout test; the full suite passed afterward.
- Full solution formatting, dependency audit, canonical PowerShell documentation checks, Windows packaging and exact installer lifecycle remain hosted gates for this source commit. Native physical DPI/GPU, screen readers, motion comfort, OneDrive hydration and real predecessor upgrades were not verified locally. Independent human review was unavailable; the implementation was self-reviewed and exercised by automated tests.

## Lessons and documentation

The earlier interpretation of clean connections removed the route indicator rather than adapting it. Retain as a candidate lesson: check whether the requested visual cue can use existing geometry before removing it. The owner correction and the new rendering test provide evidence; this observation is not promoted to a global rule.

Updated current README, architecture, interaction contract, glossary, security, testing, release guidance, packaging/version references and ADR 0006. Existing historical reports retain their original evidence and scope. No component-flow change requires a diagram update.

## Repository state

Repository `Dr-Amit-Lal/OmniBrille`, branch `feature/nebulux-my-files`, starting HEAD `9870235f19cdcfdcdb83a8e540b4fd34fe92b45f`; worktree was clean at start. This changes presentation and explicit file-result activation but not persistence schema, search authority or bundled native assets. The final PR/build record must identify and qualify the delivered tree and installer bytes.

Status: Complete with follow-up. Local implementation and tests are complete; the exact hosted installer qualification must pass before delivery. Manual platform and independent-review limits remain.

# Pinned folders and Nebula folder refresh

## What this run was meant to do

Add an elegant left pinned-folder panel. Clicking a saved pin opens that folder directly in Spatial Explorer. Prepare the updated Windows installer on the existing fork branch.

## What actually changed

The sidebar saves up to 12 explicitly selected local folders, supports drag and Alt+Up/Down reordering, unpinning, collapse, and Ctrl+Shift+P to pin the current Standalone folder. Clicking a pin revalidates the local path, selects it as the root, and displays its children in the graph. Saved pins never open automatically. The main graph reserves space for the panel. Empty-folder guidance stays in the graph area at compact sizes.

This continuation also delivers the previously prepared matching Nebula folder glyphs, removed sensory sound, and Search-only interface toggle (Ctrl+H). Light, reduced effects, keyboard/automation controls, and independent Spatial Explorer startup remain supported. Version is `1.2.0-nebula.3`.

## Important technical decisions

- Save only explicit local pins in a separate bounded, schema-versioned JSON file. Do not import the dormant My Files workspace or persist connected identities. User-selected pin activation from Connected mode explicitly switches to Standalone.
- Reuse cached folder geometry and path-derived accents between graph and sidebar. Reserve the graph viewport without rebuilding the scene or resetting user zoom during panel changes.
- Bound the panel itself and scroll only entries. Keep actions/status visible. Bind glyph colors to theme and propagate reduced-effects preferences.
- Compare provider plus load/search request generations around deferred checks and root completion. Older pin/root work cannot announce status, reset view, or steal focus after newer navigation.

## Validation and confidence

**Verified locally:** Release solution build with zero warnings/errors; all 116 headless UI tests passed. All 13 new pin UI cases passed, including actual pointer/automation activation, drag and keyboard reorder, persistence, duplicate/unavailable paths, failed saves, collapse/Ctrl+H, themes/effects and compact overflow. Ordinary tests: 331 passed; seven named-pipe cases are blocked by this environment denying Unix socket creation. New model/store tests all passed. Changed-file whitespace and `git diff --check` passed.

Actual Skia software renders at 1280x800 and 820x520 were inspected in Nebula and Light, including 12 long labels, wrapped failure status and empty-folder guidance. Captures use temporary fixture directories, not owner data. Independent source/visual review found no remaining blocker after corrections.

**Hosted qualification:** Full Windows/Ubuntu formatting, documentation, tests, dependency audit and Windows installer results are tracked in [PR 1](https://github.com/Dr-Amit-Lal/OmniBrille/pull/1). This report records the source checkpoint before hosted execution; the final artifact must match the branch commit.

**Not verified:** native Windows installation/upgrade on the owner's computer, native DPI/screen-reader or hardware behavior, GPU timing, live companion host. A controlled delayed availability-probe regression remains useful follow-up; generation guards were source-reviewed.

## Problems and learning

Independent review found stale pin completion effects, fixed dark-only unpin color, effects/theme redraw omissions, and compact overflow. Actual software capture caught empty-folder guidance covering the sidebar; a bounds regression now prevents that overlap. Two older pointer tests encoded fixed graph margins; they now calculate their target from the public scene padding and pass with the sidebar. No global engineering rule was added. Existing rendered evidence and focused state assertions were the appropriate durable checks.

## Documentation and repository state

README, architecture, interaction contract, privacy, testing, packaging and ADR 0005 now describe the feature and changed persistence boundary. Diagrams remain accurate. The decisions index includes the current startup and pin decisions. The implementation stays in `Dr-Amit-Lal/OmniBrille`, branch `feature/nebulux-my-files`, PR 1; main remains unmerged. Remote baseline was `e7d75e56e1893c3b395b92d8d72c1add4eb4870b`; local prepared folder/sound/toggle changes were preserved and included. No protocol schema changed.

Status: Complete with follow-up. Source and local validation are ready; hosted package qualification and native owner acceptance are distinct boundaries.

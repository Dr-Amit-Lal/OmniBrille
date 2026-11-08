# Nebula Spatial Explorer: sole application interface

## What this run was meant to do

Remove the requirement to keep My Files open and apply the owner's constellation reference to Spatial Explorer. The owner then explicitly chose to remove My Files from the application experience entirely.

## What actually changed

Ordinary launch now opens Spatial Explorer directly. The installer replaces the obsolete My Files shortcut with one Spatial Explorer launcher. No saved workspace data is deleted or imported. Dark mode renders small blue, violet, teal and amber points, subtle real branch lines, and cached navy/violet washes. Rounded, quieter HUD surfaces keep their original control positions and minimum interaction sizes. Light remains available.

## Important technical decisions

- Sole MainWindow startup removes the unwanted second-window dependency. `--root`, `--theme`, `--advanced` compatibility and connected handoffs remain. No root means the existing choose-folder welcome.
- Dark circle radii drive the glyph, connector endpoint and label obstacle. Existing effective hit rectangles remain at least 44 DIP. All admitted names remain; color indicates existing containment branches only.
- Cache accents and preview names at scene preparation; reuse gradients and bounded text/brush/pen caches. No new animation loop or inferred relationship is introduced.
- Use packaged Inter throughout graph, HUD, diagnostics and decorative loading text. Software rendering exposed an unavailable system-font dependency in the latter two surfaces.
- Keep the prior My Files source/tests dormant and retain existing workspace data. No new persistence, protocol or provider-authority contract is introduced.

## Validation and confidence

**Verified locally:** Release build, zero warnings/errors;318 ordinary tests passed, seven named-pipe tests fail because this execution environment denies Unix socket creation; all 99 headless tests passed. Eight actual Skia software render fixtures passed, covering Nebula, Light, dense 48-node scenes, zoom-out, minimum 820×520 client size, selected/search cues and 2× graph text. Contrast regressions cover the scoped HUD and branch accents over conservative maximum washes. Existing hit-target, all-name, automation, motion/effects and cache tests remain passing. Changed C# whitespace verification and `git diff --check` passed.

**Independent review:** UX/performance and adversarial review covered source, startup/shortcut behavior, effective hit targets, label/edge geometry and actual software renders. Findings corrected: HUD font overrides blocked text scaling and shrank icons; new aggregate glyph initially lost its navigable cue; old rectangular endpoints did not suit circular points; disabled buttons needed quieter surfaces. Source and screenshot review found no remaining blocker.

**Hosted qualification:** Windows/Ubuntu CI, full formatting/docs checks, package audit and exact Windows installer build are tracked on [PR#1](https://github.com/Dr-Amit-Lal/OmniBrille/pull/1). This report records the source checkpoint before that run; final run/artifact evidence belongs to the PR.

**Not verified:** interactive installation/upgrade on the owner's Windows computer, native DPI/snap/screen-reader behavior, GPU frame time, physical sound/microphone, or a live companion host. The private-preview installed-lifecycle workflow now checks the new shortcut target, absent legacy shortcut and uninstall cleanup, but was not executed locally.

## Problems and learning

The first hosted run passed all 325 ordinary and 98 headless tests on Ubuntu. Windows first hit a 250ms successful-handoff fixture deadline; its same-source retry passed all 321 ordinary tests (four platform skips) but exposed an unreliable single-frame edge-duration inequality. The successful handoff now uses the production deadline while the unavailable replay still must time out with a separate 250ms receiver. A real EdgeGlowDrawCalls diagnostic replaces the timing inequality in a Dark/Light theory: full effects must draw glows, reduced effects must draw none, and nodes/edges/labels must match. The counter increments at actual draw execution, resets per frame and adds no rendering behavior. Independent review accepted both corrections; local Release and 99 headless tests passed afterward. Final hosted qualification is tracked in the PR.

The original owned-window launch explained the reported close coupling. The final product choice eliminated that route. A separate lifecycle regression fixture was discarded after the sole-interface decision made it irrelevant. Deterministic scene placement and semantic limits were retained rather than introducing a new graph model for a styling request. Candidate lesson: visual changes need actual rendered evidence; non-rendering headless checks alone did not reveal the missing font. Existing render-evidence fixtures are the durable check; no global agent rule was added.

## Documentation and repository state

Updated README, current architecture/diagram, packaging boundaries and dormant My Files status. ADR 0004 supersedes ADR 0002 for startup. Previous historical run reports remain unchanged. Source baseline was `b4b345e7bda231005843338095c0fbaaace9ec23` on `feature/nebulux-my-files`; the worktree was clean before this task. Changes stay on that branch/PR; main is not updated. Version is `1.2.0-nebula.1`.

Status: Complete with follow-up. Source implementation and local checks are complete; hosted artifact qualification and native owner acceptance remain distinct evidence boundaries.

# Saved local Spatial Explorer — 2026-11-09

## What this run was meant to do

Repair the owner's folder workflow and bring the running interface closer to the approved straight-line Nebula concept. Remove unused capabilities completely, including voice, OmniSorSe, Context and Hybrid.

## What actually changed

The sole window now has one saved workspace, up to eight monitored roots, independent pins, real parent navigation and a full-height search sidebar. Adding roots shows their separate clusters. Opening a pin leaves those roots intact. Hover previews real containment paths without saving or navigating; keyboard activation and explicit file opening share the same session actions. Roots, pins/order and current location survive restart.

Straight containment lines, matching folder colors, search badges, gold paths and target pulses replace the former mode-heavy shell. Compact or enlarged-text scenes use cached paging instead of overlapping labels. Folder notifications and a coalesced/periodic refresh update the view while the window is open. Verified Windows Cloud Files tags are supported; symbolic links, junctions and unknown reparse tags remain blocked.

The source, dependencies, runtime assets and tests for the retired UI/session, voice/audio and connected modes were deleted. Migration retains only explicit legacy root/pin records. Package version is `1.2.0-nebula.4`; manifest schema is 6. Installer cleanup uses an exact retired-file allowlist and preserves unknown files and settings.

## Important technical decisions

- Separate roots from pins and navigation: shortcuts cannot silently redefine monitoring authority. A pin outside the roots is a temporary exact-folder scope.
- Merge only navigation fields after asynchronous reads: pin edits and root removals cannot be overwritten by captured state. Distinct generations protect additions, navigation, search and hover.
- Keep filesystem probes and native watch setup off the UI thread. Track accepted watchers before starting subscriptions, revalidate paths and coalesce events before dispatch.
- Bound scenes to 64 nodes and search to 80 results. Reproject cached children for responsive paging; retain actual path edges and stable positions during previews.
- Keep explicit search submission stable against deferred text-change echoes. Keyboard events are marked handled before awaiting navigation.

## Validation and confidence

**Verified at the source checkpoint:** Release compilation with zero warnings/errors; 200 ordinary tests; all 36 headless cases with actual Skia software rendering. These include saved-state restart, unavailable roots, independent pins, late-result races, keyboard/hover activation, real filesystem notifications and window closure. Nine full-window captures cover 1600×1000 and 820×520, hidden Search, eight roots and graph text at 1.5×/2×. Label tests also exercise all 24 compact folder orderings, cache bounds, stable previews and folder color parity. Independent review inspected source, failure paths, screenshots and installer checks. Whitespace/diff, Markdown links/fences and workflow YAML checks passed.

The CI workflow runs full formatting, Windows/Ubuntu builds, the software-rendered test suite, dependency audit and Windows packaging. Its new native lifecycle gate verifies exact installer/manifest hashes, notices and signatures; install/launch/normal close/relaunch; actual saved-root readiness; removal of known retired assets; and preservation of settings and unknown fixture files through uninstall. The artifact's release-validation JSON and [PR checks](https://github.com/Dr-Amit-Lal/OmniBrille/pull/1/checks) record hosted results separately from this source checkpoint.

The first hosted run passed Ubuntu validation and Windows formatting/build, 195 ordinary cases (five Unix link fixtures skipped), all 36 rendered cases, dependency audit and packaging. Native install, close/relaunch, saved roots, retired-file cleanup, unknown-file preservation and uninstall file checks also passed before the test harness rejected a remaining empty pre-existing fixture directory. The harness now removes only empty non-reparse fixture directories deepest-first after its strict remaining-file checks; installer behavior is unchanged. Delivery still requires the complete rerun to pass.

**Not verified locally:** native Windows installer execution, actual OneDrive synchronization/hydration, physical DPI/GPU behavior, screen-reader software and a real prior-version in-place upgrade. Synthetic retired-asset fixtures do not prove that last case. The local full formatter cannot create its MSBuild named pipe; CI runs that check. Software captures use sample folders, not the owner's filesystem.

## Problems found and lessons

Review caught stale navigation overwriting pins, root buttons depending on the visible scene, UI-thread filesystem probes, watcher event flooding, unavailable-pin graph replacement, corrupt-store overwrite and native-path/ordinal-ID mismatches. These now have clearer ownership and focused regressions. The explicit-search race was reproduced with a failing delayed-row-stability test before correction. Compact typography required actual pixels: a sampled label search missed narrow gaps, so a bounded exact-boundary fallback repairs residual collisions.

Independent reviewer evidence supported promoting these lessons into code and tests. No new global process rule was added. Initial assumptions that old connected contracts and the former fixed 60-child projection were reusable were rejected for this product. Dedicated state, renderer, filesystem, UI-test, release/documentation and adversarial owners reduced overlapping edits; final integration remained with the lead.

## Documentation and repository state

Current overview, architecture/diagrams, terminology, interaction, packaging, privacy, testing and engineering routes were updated. ADR 0006 records the local-only decision; old guides and reports remain clearly historical, with removed-source links anchored to the baseline commit.

Repository: `Dr-Amit-Lal/OmniBrille`, branch `feature/nebulux-my-files`, baseline `f38811d7729ec573bce264d9bfd6e1f9e0a62797`. The worktree was clean at discovery. This checkpoint changes intentional product behavior, persistence and package schema; it does not merge the branch or publish a stable release. Exact submitted commits and hosted evidence are recorded by the PR and generated manifests.

Status: Complete with follow-up. Source validation is suitable for the installer gate; native cloud, physical display and assistive-technology acceptance remain environment-specific follow-up checks.

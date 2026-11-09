# Left-edge workspace tab, nebula.8

## What this run was meant to do

Let the owner bring the running Spatial Explorer back from a small tab on the left side of the screen, without leaving the full workspace in front of other applications.

## What changed

When the workspace is minimized or deactivated, a narrow topmost tab appears at the left of its display's working area. Clicking it restores the same main window and its prior normal/maximized state. The tab is hidden while the workspace is active and closes with the main window. The app still exits on normal close and does not start automatically with Windows. Saved roots, pins, navigation and existing visual effects are unchanged.

## Decisions and local validation

- Use a separate non-taskbar window rather than reserving desktop space or changing the main window's size. It can remain accessible above other windows while the workspace is inactive. Track the main window's display and reposition after display changes; preserve ordinary app close semantics.
- The Release build passed with zero warnings/errors. All 207 ordinary tests and 47 headless tests passed. Headless pointer input verified minimize, edge-tab activation, restoration of a maximized workspace, retained roots/pins/location and cleanup on close. A separate test invokes the deactivation handler directly because the headless backend does not transfer real OS window activation. An actual software-rendered tab was inspected.
- The Windows installer lifecycle script now contains an installed-app Win32 minimize, left-work-area placement and tab-click restoration probe. Exact hosted execution, full solution formatting, dependency audit and canonical PowerShell documentation checks remain pending for this source commit.
- The first hosted Windows run exposed an existing animation test's exact-equality assumption: the final easing frame had a scale of 1.0000118 after pointer exit. The assertion now permits at most 0.1% residual scale, retaining the effective-rest check without depending on dispatcher timing. A fresh exact-commit run is required.
- Physical multi-monitor/DPI behavior, foreground interaction with other desktop apps, fullscreen programs, native screen readers, OneDrive hydration and a real predecessor upgrade remain manual acceptance limits. Independent human review was unavailable; the owner performed a source review and ran the listed tests.

## Problems, learning and documentation

Headless activation does not model another real OS window taking focus. Retain as a candidate lesson: desktop-shell features need an installed native-window probe in addition to headless control tests. The new lifecycle probe is scoped to this behavior and must pass against the exact artifact; no global rule is promoted.

Updated README, architecture and its component-flow diagram, interaction contract, glossary, testing, release and packaging guidance, version references and ADR 0006. Historical reports retain their original evidence. No persistence schema, filesystem authority, protocol or bundled native dependency changed.

## Repository state

Repository `Dr-Amit-Lal/OmniBrille`, branch `feature/nebulux-my-files`, starting HEAD `17c647116fe21b6c74e679561374e23f7bbb111c`; worktree was clean at start. The final PR/build record must identify the delivered tree and installer bytes.

Status: Complete with follow-up. Implementation and local tests are complete; the exact hosted Windows installer probe must pass before delivery. Physical display and independent-review limits remain.

# Interaction-state contract

This contract keeps OmniBrille's spatial presentation, keyboard/UIA behavior, visual feedback, and provider authority aligned. [`architecture.md`](architecture.md) remains the ownership authority; this document is the concise behavior matrix for the graph-first shell.

## Independent state planes

| State | Owner | Meaning | Must not be inferred from |
| --- | --- | --- | --- |
| Graph focus | `ExplorerSession.Neighborhood.FocusNodeId` | Center of the acquired bounded scene | Selection, keyboard focus, or density |
| Selected node | `ExplorerSession.SelectedNode` | Item whose Details/actions are active | Graph focus or pointer hover |
| Keyboard focus | Avalonia focus manager | Control receiving keys | Graph focus/selection |
| Scene relation | `ExplorerSceneSemantics` | Current focus, direct child, descendant preview, previous focus, Context, both, or aggregate | `PresentationBand`, radius, opacity, or size |
| Presentation band | layout/presentation policy | Bounded visual density/label priority | Filesystem depth or provider authority |
| Provider authority | active `IExplorerProvider` | Standalone selected-root paths or Connected opaque IDs | Display path, Details text, or motion |
| Pinned folders | `PinnedFolders` / `JsonPinnedFoldersStore` | Up to 12 explicit local folders, display names and order | Current graph membership, Connected IDs or projected paths |

The graph, synchronized list, Details, and automation tree project the same bounded scene and selection. No projection may acquire, crawl, persist, or authorize data independently.

A newly acquired or navigated scene begins without a selected node; centering graph focus never opens Details. Pointer, keyboard, list, UIA, or Voice selection is the only way to create selection. Search, Trail, Connection, and Details share one overlay authority so opening one cannot leave another actionable surface hidden behind it.

## Navigation and activation

| User intent | Pointer/control | Keyboard | Voice | Session/result contract |
| --- | --- | --- | --- | --- |
| Select | single click on file or Context/Hybrid node | geometric arrows, list selection | `focus <visible name>` | Changes selection and Details only |
| Enter Structure folder or aggregate | first click on navigable folder, previous-focus node, or aggregate | select then `Enter` | `open selected`, `enter selected` | Navigates through the same session action; centered focus does not navigate to itself |
| Activate selected | double click / Open | `Enter` | `open selected`, `enter selected` | Context/Hybrid explicitly refocus; ordinary Standalone file uses safe OS activation. Pointer double-click requires the same node and unchanged scene across both presses |
| Back | `BACK` or graph right-click | `Backspace`, `Alt+Left` | `back`, `previous` | Unwinds chronological mode/refocus/aggregate/folder history |
| Up | `UP` | `Alt+Up` | `up`, `parent` | Uses the provider-authored parent target; never display-path inference |
| Root | `ROOT` | `Alt+Home` | `root`, `home` | Returns to the active Standalone path root or Connected opaque root |
| Jump to recent history | `TRAIL`, then named destination | normal Tab activation | no separate voice intent | Up to six named, mode-aware destinations in a scrolling surface. Targets remain private; stale or loading actions are rejected. Crossed history is removed only after successful acquisition |
| Reopen Details | selection or Details action | `Ctrl+I` | `show details` | Reopens the same selected-node projection; an explicit Details command restores hidden controls |
| Hide/show controls | **Hide controls** / **Show controls** | `Ctrl+H` | no separate voice intent | Changes shell visibility only; Search and restore remain reachable, with session state and pan/zoom retained |
| Pin a local folder | **+ Pin folder**; **Pin current folder** in Standalone | `Ctrl+Shift+P` pins the current Standalone folder | no separate voice intent | Saves only an explicitly chosen ordinary local folder, name and order; at most 12 |
| Open a pinned folder | saved folder button | Tab then Enter | no separate voice intent | Revalidates and explicitly switches to that Standalone access root/focus; resets prior scope history/Search |
| Reorder pins | drag or context-menu Move up/down | `Alt+Up` / `Alt+Down` on the focused pin | no separate voice intent | Changes saved display order, without opening a folder |
| Unpin / collapse | pin removal control or context menu; sidebar header | normal Tab activation | no separate voice intent | Unpin changes the saved list, never files; collapse is temporary visibility state |

Standalone file activation requires an explicit user action, an ordinary non-reparse file, a path inside the selected root, no reparse-point ancestor below that root, and a non-executable/script/shortcut/URL-like extension. Connected files cannot use projected paths; until the protocol provides an authorized host operation, the UI states that opening is unavailable.

Pins use their own bounded local store and never import the former My Files workspace. Opening a pin from Connected mode is an explicit switch to the saved local root, not a filesystem fallback for a connected node. Folders rejected by the availability precheck retain their saved pin and the active graph; the status offers retry/unpin guidance. A later directory-enumeration failure can occur after the root switch and is shown as a read failure. Newer navigation, Search, or provider changes supersede delayed pin work. Loading the list at startup does not activate a root or enumerate its contents. Failed saves retain the changed list for the current session and report that persistence failed.

## Visual and timing behavior

| Event/state | Visual | Announcement/UIA | Reduced motion |
| --- | --- | --- | --- |
| Keyboard enters graph | distinct canvas/selected target cue | selected `TreeItem` remains queryable | same static cue |
| Hover node | eased local magnification across target and nearby nodes; central focus remains steady | no focus/selection change | no displacement |
| Select node | selection halo and Details update when controls are shown | `SelectionItem.IsSelected`; full Details semantics atomic | no Details typing |
| Navigate | destination moves to focus; prior scene recedes | one polite completion status | immediate base layout |
| Loading/Search/status | visible local surface as applicable | one independent polite status authority | unchanged |
| Voice listening | text state plus input-level meter | state-specific button/help and polite status | static high-contrast state |
| File opened | status confirms Windows handoff when controls are shown | polite status | unchanged |

Graph motion is analytic and bounded around immutable deterministic coordinates. It never changes membership, edge topology, semantic relation, selection, or provider work. One approximately 24-fps foreground timer serves transition/float/lens invalidation and stops for Reduced motion, hidden/minimized windows, teardown, or no scene. There is no background simulation or application sound output.

Details assigns complete text and automation names to the actual Type-through-Index, summary, and relationship fields before starting an optional sequential visual clip reveal. A new selection or metadata replacement cancels the prior reveal; Reduced motion immediately removes the clips. The projection never substitutes a duplicate command header or emits per-character live announcements.

Every admitted node whose center is on the graph retains its name at every zoom/text scale. Labels use measured widths and ellipsis for long text, move around glyphs and prior labels, and receive peripheral leader lines when displaced and unobstructed. The top/bottom HUD reserves graph-coordinate and label-placement space. Exceptional space pressure may still cause overlapping names; the synchronized list provides full-size reading without changing scene membership.

Descendant previews are actual acquired subfolders, visibly marked with `↳`, connected to their direct-folder parent, and announced as two levels below focus. Their subdued size does not redefine the depth of nearby direct children. Like-type placement and deterministic asymmetry convey organization without adding a relationship.

## Hidden controls

The hide-controls flag belongs to `MainWindow` and lasts only for that window. **Hide controls** or `Ctrl+H` hides normal chrome, navigation, the pinned-folder sidebar, modes, utility controls, Details, zoom, idle Voice, visible status and diagnostics. Search stays open alongside a 44-DIP **Show controls** button. Its automation name and tooltip describe the current action. Search results, loading cancellation and active microphone feedback remain available when needed; the independent polite status authority continues to announce state.

Toggling changes visibility without cancelling pending provider/Search/Voice work, clearing queries or selection, changing provider authority, or resetting pan/zoom. The pin sidebar reserves left-side graph space while expanded and visible; hiding or collapsing it releases that space without changing logical layout or pan/zoom values. Entering the mode moves keyboard focus into Search; restoring through the toggle focuses the restore control. Escape clears results without closing the only search entry. Explicit accessible-list, Details, Context-filter and Voice actions restore the interface before opening their controls.

## Accessibility and target contract

- Primary shell actions and every visible graph node have at least a 44 by 44 DIP effective target. Dense visual glyphs may be smaller only inside that target rectangle.
- Keyboard focus, graph focus, selection, Search match, relationship kind, and semantic scene relation each have a non-color text/shape/automation channel.
- The graph exposes one bounded `Tree` with single-selection semantics; each visible node exposes `TreeItem`, `SelectionItem`, and `Invoke`. Search match and semantic relation are present in queryable state.
- One independent polite status authority announces accepted current work only; its queryable automation name changes with the message. Visible status text is not a second live region. Stale provider, Search, Details, and Voice results are rejected before visual or assistive output.
- Contrast gates parse both application resources and the renderer's actual `ScenePalette`, alpha-composite effective interactive glyph colors, and enforce 4.5:1 text plus 3:1 non-text floors. Release inspection still covers both themes, high contrast, keyboard focus, and actual Windows scaling.

## Voice input and sound-output boundary

Voice is a click-to-toggle convenience over existing actions: first activation enables Voice and listens without an enablement submenu; second activation stops and transcribes locally. The microphone becomes Stop while active, and the same control cancels during initialization/transcription. Two seconds of quiet after detected input also stops capture; initial silence does not submit an utterance. Quiet/duration callbacks and cancellation completion remain bound to their original operation, so older work cannot stop or reset a newer capture. Grammar has no destructive action, arbitrary shell/file command, or client-created Context. Missing microphone/runtime/model, recognition failure, or cancellation cannot affect typed/pointer navigation or replace provider state.

The installed voice runtime/model are pinned, installer-owned, hash-bound, local-only assets whose hashes are rechecked whenever the bundle is resolved. The installed application does not download or update them. The process receives a minimal environment and bounded arguments/output/time. Its GUID workspace must be local and non-reparse; termination is awaited before zero/retry cleanup, and a cleanup failure is surfaced rather than silently ignored. No transcript or audio enters preferences, diagnostics, release metadata, or logs.

Interaction sound output is removed from Spatial Explorer. `MainWindow` does not construct a sound service or dispatch hover, selection, navigation, folder-entry or file-open cues. Sound controls are absent. Older `SoundEnabled=true` preferences normalize to false. Retained legacy sound source/tests do not create a runtime route. Voice capture/transcription remains separate input functionality.

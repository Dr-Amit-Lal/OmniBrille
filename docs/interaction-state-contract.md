# Spatial workspace interaction contract

Current product: `1.2.0-nebula.8`, local-only Spatial Explorer. All navigation, search and file actions use `SpatialWorkspaceSession`. The shell and graph must not maintain competing location state.

| Action | Required outcome |
| --- | --- |
| Choose folders | Add explicit roots, at most 8, and show all roots together; retain existing roots and pins; duplicate-only choices keep the current view; never move content |
| Open folder glyph / page | Navigate within its authorized root; keep real parent-child edges |
| Back / Up / graph right-click / Alt+Left | Move to the containing folder, then overview at the root boundary |
| Workspace / Alt+Home | Show all chosen roots |
| Select file | Select only; show file details/Open file when the appropriate panel is visible |
| Open file / file graph activation | Revalidate path and blocked-file policy before asking the OS to open it |
| Pin folder / Ctrl+Shift+P | Save an explicit folder shortcut, at most 12, independently of chosen roots |
| Open pin | Use deepest containing chosen root, or the pin itself as a temporary scope; preserve root set |
| Missing root/pin | Retain saved entry, explain unavailability, never silently replace authorization |
| Remove root / pin | Change workspace metadata only; invalidate work tied to removed authority |
| Reorder pin | Same saved order via drag, Alt+Up/Down or context-menu equivalents |
| Ctrl+F | Open Search and focus its editor; normal controls stay hidden if already hidden |
| Type search / Enter | Search chosen roots after debounce / immediately; show bounded partial-result status |
| Hover/select result | Highlight the item or nearest visible parent inside the same chosen root; emphasize its existing straight connection path without adding nodes/edges or committing navigation |
| Hover graph item | Smooth local enlargement up to 16%, glow/shine and a local ring; gentle floating continues while the compact base layout and muted straight connectors remain, without extra routes |
| Leave result / close Search | Clear the local marker; closing Search clears query/results |
| Double-click result / Enter | Enter a folder or explicitly open a file through the guarded shell activation service |
| Show in map | Enter the selected folder result, or enter the containing folder and select the file without opening it |
| Minimize or switch away | Keep the running workspace available from a small tab at the left edge of its display |
| Click the edge tab | Restore/activate the existing workspace and hide the tab; preserve the previous normal/maximized state |
| Close window | Exit normally and remove the edge tab |
| Ctrl+R | Request bounded refresh of current scene and active query |
| Ctrl+H | Hide/restore controls without changing workspace authority or location |

Search row keyboard selection has the same preview semantics as pointer hover. A later preview, query, navigation or root edit cancels/invalidates older asynchronous work. Refresh must not resurrect a closed search or an obsolete hover path.

Pins and Search share one collapsible sidebar. Hidden controls leave a minimal Search field and Show controls button; submitting it shows Search results while normal controls stay hidden. Hide/collapse changes graph padding while preserving session state and pan/zoom. Theme/motion/effects persist; temporary interface state does not.

Graph keyboard selection/invoke and automation operate on the visible bounded scene. Full result names and root-relative paths are exposed in Search automation labels. Reduced motion stops floating, hover enlargement and navigation transitions; Reduced effects lowers glow. Navigation transitions run only for a changed displayed scene. Labels and connectors clear the reserved local ring area. Neither setting changes content or actions. Native screen-reader behavior and display scaling require manual verification.

Voice, playback, Connected/Context/Hybrid mode switches, chronological Trail, the previous accessible-list panel and diagnostics controls are removed. Historical contracts remain in Git history, not as current behavior promises.

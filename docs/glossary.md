# Spatial workspace glossary

Current meanings for `1.2.0-nebula.6`; [architecture](architecture.md) owns behavior.

| Term | Meaning |
| --- | --- |
| Workspace root | One of at most 8 explicitly chosen local folders; authorizes browsing, search and live monitoring below it |
| Workspace overview | Separate graph clusters for all chosen roots and bounded immediate contents |
| Pin | One of at most 12 independent saved folder shortcuts; does not itself add search/watch scope |
| Pin excursion | Browsing an outside pin with that exact folder as temporary access root, returning to the workspace at its boundary |
| Current root/path | Authorized browsing scope and displayed containing folder, saved in workspace JSON |
| Selected node | Visible node chosen for an action; selection alone does not launch files |
| Preview | Temporary local emphasis on a validated result or its nearest visible ancestor; scene membership and current location stay unchanged |
| Hover lens | Smooth temporary enlargement up to 16% with subtle local neighbor response; does not change the base layout, scene membership or saved state |
| Back / Up | One containing-folder step toward the workspace; no chronological-history semantics |
| Aggregate | Bounded previous/next-page control, not a semantic grouping inferred from document contents |
| Operation generation | Identity used with cancellation to prevent old navigation/search/preview work replacing newer state |
| Refresh | Bounded re-read of the visible scene and active query, triggered by user, watcher events or periodic recovery |
| Presentation band | Visual placement/scale, never filesystem depth or authority |
| Graph automation | Bounded visible-node selection/invocation projection over session state |

Connected authority, Explorer Protocol, semantic Context/Hybrid, Voice and the old chronological Trail are historical concepts. The previous-folder glyph kind in shared `ExplorerModels` remains only for structural orientation; removed mode/provider/relationship contracts do not remain. Historical guides are labeled accordingly.

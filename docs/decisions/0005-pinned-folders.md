# ADR 0005: Explicit local folder pins

Status: Accepted for the custom Nebula.3 preview.

## Context

The owner wants quick access to frequently used folders inside Spatial Explorer. The sole-interface decision remains in force; the former My Files workspace is dormant and must not become a second navigation authority or an implicit source of access.

## Decision

Persist only explicitly pinned local folder paths, display names and order in a separate schema-versioned `pinned-folders.json`, limited to 12 pins. Normal startup loads the list without opening or indexing its folders. Do not import My Files data or persist Connected IDs, projected paths, searches, content or navigation history.

The sidebar uses an explicit local folder picker and a current-folder action available only for a Standalone folder. Activation revalidates the saved local path, explicitly selects Standalone, and calls the existing root-opening route. The pin becomes the access root and graph focus; the prior scope's history and Search are cleared by the same session transition. A missing folder retains its pin and the active graph.

Pins support removal, drag reorder and keyboard/context-menu equivalents. Collapse and hide-controls state stay in memory. The graph reserves left-side space while the sidebar is expanded and visible, releasing it when hidden or collapsed without resetting pan/zoom or session state.

## Consequences

The persistent-data boundary now includes the small user-chosen local pin list. It survives upgrade/uninstall beside safe visual preferences. Pin load/save does not acquire filesystem content; activation remains a user action with path revalidation. A failed save is reported while the current in-memory list stays usable. The model/store enforce native-path deduplication, bounded labels, schema checks and bounded serialized input.

[ADR 0004](0004-spatial-only-nebula.md) remains authoritative for startup and the sole interface. Its Nebula.2 fixed-padding description predates the sidebar; Nebula.3 changes only the viewport reservation needed by this explicit feature. [Current architecture](../architecture.md#explicit-pinned-folders) describes ownership and behavior.

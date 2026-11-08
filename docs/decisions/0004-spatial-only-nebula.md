# ADR 0004: Spatial Explorer as the sole interface

Status: Accepted for the custom Nebula preview.

## Decision

Always launch MainWindow/ExplorerSession. Expose one Start Menu launcher named OmniBrille Spatial Explorer and remove the known obsolete OmniBrille.lnk on upgrade. Preserve command-line roots, themes and connected handoffs. Retain --advanced as a harmless compatibility argument.

## Reason

The owner chose the spatial graph and explicitly removed My Files from the product experience. Making the graph the default removes the unwanted second window and any dependency on keeping it open.

## Consequences

Normal launch without a root opens the existing folder picker welcome. No saved-root import or new persistence schema is introduced. My Files source and tests remain dormant, and its data is preserved. The Nebula dark theme changes rendered glyphs and visual styling while keeping the existing topology, scene limits, names, keyboard navigation and effective hit targets.

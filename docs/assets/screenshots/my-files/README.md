# My Files interface evidence

These PNGs are software-rendered captures of the real Avalonia MyFilesWindow, using synthetic folders from MyFilesWindowHeadlessTests. They are not image-generation mockups or screenshots of an installed Windows release.

- `my-files-first-run.png`: empty first-use screen, with explicit Documents/Desktop/folder selection.
- `my-files-home.png`: configured six-folder overview using example labels.
- `my-files-expanded.png`: Teaching and Database Management expanded into example file nodes. Other home folders remain lower in the scrollable map.

Captured at 1400 × 900 with the bundled Inter font and Light appearance. File opening, settings, and map controls are real code paths. No user documents or user-specific filesystem paths appear in the captures. The desktop OS title bar, Windows scaling, Narrator, and native installed behavior are not covered by these images.

## Nebulux custom preview, 2026-11-07

`nebulux-*.png` captures the actual My Files Avalonia window with software rendering and packaged Inter. The home fixtures use six synthetic selected roots. First-run, home, expanded folders, opaque Settings, extra-large text, and Light appearance were inspected. The earlier `my-files-*.png` files are preserved as historical pre-Nebulux evidence.

These are interface composition checks, not screenshots from a native installed Windows session. The long-label tests separately check that three-line 26px file names fit within their buttons.

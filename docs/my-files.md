# My Files custom interface (historical)

**Superseded in 1.2.0-nebula.4.** This retained guide describes removed earlier functionality and is not a current product contract. See [the local workspace architecture](architecture.md) and [ADR 0006](decisions/0006-local-spatial-workspace.md).
My Files is retired from the application entry points in `1.2.0-nebula.1`. Normal launch, the single **OmniBrille Spatial Explorer** Start Menu shortcut, `--advanced`, and OmniSorSe handoffs all use Spatial Explorer. The former My Files implementation and regression fixtures remain in source for now; its saved workspace is preserved but not read into Spatial Explorer. The following details describe that dormant implementation.

## Interaction

- Choose locations explicitly. Documents/Desktop buttons select the Windows known folder; Add folder uses the operating system picker. Optional labels change the map only.
- Home shows the connected folders around a central My Files label. Expanding a folder switches to connected branching cards. Positions are deterministic; manual node placement is not implemented.
- Folder cards expand/collapse. File cards select only. Open file uses the existing ordinary-file activation service.
- Current Work stores a bounded list of file references, not copies. Selecting a pin reveals its location; unpinning changes only the map.
- Search scans filenames within selected roots on request, shows paths, and reveals a result's ancestry. It is bounded and is not a whole-computer index.
- Native buttons support Tab, Enter and Space. Ctrl+F opens Search; Alt+Home returns Home; Escape closes secondary panels. Full filenames and paths are available in details/accessible labels even when a long map label is shortened.
- The map is scrollable and retains readable text instead of shrinking all nodes to fit. Settings provides three text sizes, Light/Nebulux (dark) appearance, explicit Refresh files, and Remove from map.

## Nebulux appearance

The retained My Files implementation uses Nebulux (dark) by default. An existing Light preference and an explicit startup theme are respected. The Appearance picker retains the existing `Dark`/`Light` storage values, so there is no preference or workspace migration.

The My Files window owns its dark navy gradients, glass-like cards, colored folder/file outlines, soft static glows and branch lines. Six accent colors repeat in saved root order; descendants use their root's color. Labels remain packaged Inter with a distinct keyboard-focus ring. Spatial Explorer has its own Nebula palette: small branch-colored points, thin structural connectors, quiet static navy/violet washes and rounded controls. Its Dark preference is displayed as Nebula, while Light remains available. Settings, Search and file details use opaque panels for readable text.

The six names in the screenshots are examples. First launch asks you to connect actual folders; it does not create or reorganize folders on your PC.

## State and boundaries

The custom map uses its own `MyFilesSession`, leaving the advanced/connected `ExplorerSession` unchanged. Workspace metadata lives in `%LOCALAPPDATA%\OmniBrille\file-map-workspace.json`. Its schema admits local selected paths only, validates bounds and ownership, and replaces the file atomically. Invalid or oversized workspace data falls back safely; failed saves are reported in the UI.

Roots, expanded folders and pinned paths are revalidated before use. Reparse-point targets and ancestors including the selected root are rejected. A generation counter rejects late results after a later action, root removal, or disposal. No file operation uses a connected display path.

The scene is capped at 48 nodes. Folder pages, roots, restored expansion, and search all have explicit limits in `MyFilesSession` and `FileMapWorkspace`. Source enumeration retains at most 5,000 entries per directory. Large or incomplete results are reported; the app does not imply that every item on a drive is loaded.

This first version does not add semantic relationships, file previews, automatic organization, drag placement, background indexing or a filesystem watcher. Refresh is explicit. Deep paths remain accessible through expansion/search; existing folder hierarchies are not flattened.

## Build and run

Use the SDK pinned in `global.json`:

```powershell
dotnet restore .\OmniBrille.sln
dotnet build .\OmniBrille.sln --configuration Release --no-restore
dotnet test .\OmniBrille.sln --configuration Release --no-build --no-restore
dotnet run --project .\src\OmniBrille.Desktop --configuration Release --no-build
```

For a portable Windows source build:

```powershell
dotnet publish .\src\OmniBrille.Desktop -c Release -r win-x64 --self-contained true -o .\artifacts\my-files-windows
```

This builds an application folder containing `OmniBrille.exe`; it does not create a signed installer or bundle the optional advanced voice assets. The upstream installer does not include these custom changes. Existing installer tooling remains available for a separately qualified release.

## Verification scope

Automated tests exercise workspace state, stale requests, path boundaries, button invocation, selection/open separation and layout bounds. Software-rendered screenshots use synthetic folders and the actual Avalonia interface. They are implementation previews, not captures from an installed Windows release. Native Windows high DPI, file associations, Narrator and installation lifecycle require separate testing.

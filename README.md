# OmniBrille · Spatial Explorer

A local folder workspace shown as connected, glowing folder and document icons. **Custom preview: `1.2.0-nebula.6`.** Spatial Explorer is the only interface.

Choose up to **8 workspace folders** and see their immediate contents together. Open a folder to explore its children; **Back** and **Up** move one containing folder toward the workspace overview. **Workspace** returns to all chosen roots. Your files stay where they are.

The sidebar has **Pinned** and **Search** views. Save up to **12 independent folder pins**, reorder them by dragging or with `Alt+Up` / `Alt+Down`, and remove shortcuts without changing files. A pin inside a workspace root uses that root; a pin elsewhere opens a temporary browsing scope and leaves your chosen roots unchanged.

Search looks for names and paths across your chosen workspace folders. Hover or select a result to softly highlight its nearest visible folder or file without adding branches or extra connections. Results outside the displayed tree retain their path in the sidebar; activate one to navigate there. Double-click a result or press `Enter` to navigate there. Selecting a file reveals an explicit **Open file** action.

Compact folders retain their original gentle floating motion, smooth hover enlargement, glow and shine. A local ring emphasizes the hovered or selected item. Thin, muted straight connections stay clear of icons and labels; hover adds no crossing routes. Navigation transitions run when the displayed scene changes.

Workspace roots, pins and the current location are saved locally and restored on launch. Folder-change notifications and periodic bounded refresh keep the visible view current while the app is open. There is no whole-computer index, document-content analysis, microphone, sound output, OmniSorSe connection, Context or Hybrid mode.

## Install on Windows

The [custom Windows build workflow](https://github.com/Dr-Amit-Lal/OmniBrille/actions/workflows/ci.yml) produces the installer ZIP when its checks succeed. Use the artifact for the intended commit and version; a workflow link is not evidence that a particular build passed.

1. Download the matching `OmniBrille-1.2.0-nebula.6-win-x64-setup.exe`, checksum and manifest from that build.
2. Compare its SHA-256 with the sidecar and manifest, then run the installer as your normal Windows user.
3. Open **OmniBrille Spatial Explorer** from the Start Menu. Choose folders on first use; previously saved explicit roots and pins migrate automatically.

The self-contained installer needs no separate .NET runtime or administrator access. It installs under `%LOCALAPPDATA%\Programs\OmniBrille` and provides an Installed apps uninstall entry. Preview packages are **unsigned**; Windows may show Unknown Publisher or SmartScreen. A checksum verifies bytes, not publisher identity. Native Windows acceptance for this version must be recorded separately.

Upgrade keeps your local workspace settings, removes known obsolete Voice assets and the old My Files launcher, and leaves user content alone. See [packaging](docs/PACKAGING.md) and [compatibility](COMPATIBILITY.md).

## Controls

| Action | Control |
| --- | --- |
| Search chosen folders | `Ctrl+F` |
| Return to workspace | `Alt+Home` |
| Step toward workspace | Back / Up, `Alt+Left`, graph right-click |
| Pin current folder | `Ctrl+Shift+P` |
| Refresh | `Ctrl+R` |
| Hide / restore controls | `Ctrl+H` |
| Pan / zoom / fit | Drag empty graph space; zoom controls |

Nebula and Light themes are available. Reduced motion stops floating, hover enlargement and navigation animation; Reduced effects lowers glow. Hide controls leaves a Search field and restore button; submitting that search shows results while normal controls stay hidden. The temporary interface setting does not change saved roots, pins or location.

## Build from source

Use the SDK pinned in [global.json](global.json):

```powershell
dotnet restore .\OmniBrille.sln
dotnet build .\OmniBrille.sln --configuration Release --no-restore
dotnet test .\OmniBrille.sln --configuration Release --no-build --no-restore
dotnet run --project .\src\OmniBrille.Desktop\OmniBrille.Desktop.csproj
.\build\Package-Windows.ps1 -BootstrapInnoSetup
```

`--root <folder>` adds an explicit workspace root; `--theme Light` or `--theme Dark` selects startup appearance. See [architecture](docs/architecture.md), [testing](docs/testing.md), [security and privacy](docs/SECURITY-PRIVACY.md), and the [engineering guide](docs/engineering/README.md).

## Attribution and history

This fork derives from OmniBrille. The [upstream v1.2.0 release](https://github.com/nishdel/OmniBrille/releases/tag/v1.2.0) and [its original README](https://github.com/nishdel/OmniBrille/blob/v1.2.0/README.md) describe the earlier product, including features removed from this custom workspace. Its installer does not contain this design. Earlier screenshots and run reports retain their original scope and do not qualify nebula.6.

Project code uses the **MIT License**: see [LICENSE](LICENSE), [third-party notices](THIRD-PARTY-NOTICES.txt), and [component licenses](THIRD-PARTY-LICENSES). The Windows renderer retains the reviewed DNG-free SkiaSharp native asset and [its build provenance](docs/native-skia.md).

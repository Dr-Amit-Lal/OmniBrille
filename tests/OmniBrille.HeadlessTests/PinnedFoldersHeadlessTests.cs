using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using OmniBrille.Core;
using OmniBrille.Desktop;
using OmniBrille.Desktop.Presentation;
using OmniBrille.Desktop.Rendering;
using OmniBrille.Infrastructure;

namespace OmniBrille.HeadlessTests;

public sealed class PinnedFoldersHeadlessTests
{
    [AvaloniaFact]
    public async Task PinCurrentFolder_AddsOnceAndRestoresAfterWindowRecreation()
    {
        using var directories = new TemporaryFolders();
        var root = directories.Create("Teaching");
        var store = new MemoryPinsStore();
        using (var window = CreateWindow(store, out var session))
        {
            window.Show();
            await OpenRootAsync(session, root);
            Click(window.FindControl<Button>("PinCurrentFolderButton")!);
            await WaitUntilAsync(() => store.Saves == 1);
            Assert.Equal(root, Assert.Single(store.Saved.Folders).Path);
            Assert.False(window.FindControl<TextBlock>("PinsEmptyText")!.IsVisible);
            Click(window.FindControl<Button>("PinCurrentFolderButton")!);
            await WaitUntilAsync(() => Status(window).Contains("already pinned", StringComparison.Ordinal));
            Assert.Single(Rows(window));
            Assert.Equal(1, store.Saves);
        }

        using var restored = CreateWindow(store, out _);
        restored.Show();
        Assert.Equal(root, Assert.Single(Rows(restored)).Tag);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActivatePin_OpensSavedRootAndItsChildrenInSpatialGraph(bool throughAutomation)
    {
        using var directories = new TemporaryFolders();
        var original = directories.Create("Original");
        var target = directories.Create("Teaching");
        var child = Directory.CreateDirectory(Path.Combine(target, "Courses")).FullName;
        using var window = CreateWindow(Pins(target), out var session);
        window.Show();
        await OpenRootAsync(session, original);
        var button = OpenButton(Rows(window)[0]);
        if (throughAutomation)
        {
            var peer = ControlAutomationPeer.CreatePeerForElement(button);
            Assert.Equal("Open pinned folder Teaching", peer.GetName());
            Assert.IsAssignableFrom<IInvokeProvider>(peer.GetProvider<IInvokeProvider>()).Invoke();
        }
        else
        {
            using (window.CaptureRenderedFrame()) { }
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
        }

        await WaitUntilAsync(() => session.CurrentPath == target && !session.IsLoading);
        Assert.Equal(target, session.AccessRoot);
        Assert.Equal(ExplorerProviderMode.Standalone, session.ProviderMode);
        Assert.Contains(session.Neighborhood!.Nodes, node => node.Path == child);
        Assert.False(session.CanGoBack);
        using (window.CaptureRenderedFrame()) { }
        Assert.True(window.FindControl<GraphSceneControl>("GraphScene")!.Diagnostics.Nodes >= 2);
        Assert.Contains("current-pin", OpenButton(Rows(window)[0]).Classes);
    }

    [AvaloniaFact]
    public async Task KeyboardReorderAndUnpin_PersistWithoutNavigating()
    {
        using var directories = new TemporaryFolders();
        var first = directories.Create("Teaching");
        var second = directories.Create("Projects");
        var third = directories.Create("Archive");
        var store = Pins(first, second, third);
        using (var window = CreateWindow(store, out var session))
        {
            window.Show();
            await OpenRootAsync(session, first);
            OpenButton(Rows(window)[1]).Focus();
            window.KeyPress(Key.Up, RawInputModifiers.Alt, PhysicalKey.ArrowUp, null);
            Assert.Equal(new[] { second, first, third }, store.Saved.Folders.Select(folder => folder.Path));
            Assert.True(OpenButton(Rows(window)[0]).IsFocused);
            window.KeyPress(Key.Down, RawInputModifiers.Alt, PhysicalKey.ArrowDown, null);
            Assert.Equal(new[] { first, second, third }, store.Saved.Folders.Select(folder => folder.Path));
            Click(Rows(window)[2].Children.OfType<Button>().Single(button => Grid.GetColumn(button) == 1));
            Assert.Equal(new[] { first, second }, store.Saved.Folders.Select(folder => folder.Path));
            Assert.Equal(first, session.CurrentPath);
            Assert.Equal(first, session.AccessRoot);
        }

        using var restored = CreateWindow(store, out _);
        restored.Show();
        Assert.Equal(new[] { first, second }, Rows(restored).Select(row => row.Tag));
    }

    [AvaloniaFact]
    public async Task PointerDrag_ReordersWithoutActivatingTheDraggedFolder()
    {
        using var directories = new TemporaryFolders();
        var active = directories.Create("Active");
        var first = directories.Create("Teaching");
        var second = directories.Create("Projects");
        var third = directories.Create("Archive");
        var store = Pins(first, second, third);
        using var window = CreateWindow(store, out var session);
        window.Show();
        await OpenRootAsync(session, active);
        using (window.CaptureRenderedFrame()) { }
        var rows = Rows(window);
        var button = OpenButton(rows[0]);
        var start = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        var end = rows[2].TranslatePoint(new Point(40, rows[2].Bounds.Height / 2), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(start.X, start.Y + 12), RawInputModifiers.LeftMouseButton);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left);
        Assert.Equal(new[] { second, third, first }, store.Saved.Folders.Select(folder => folder.Path));
        await Task.Delay(50);
        Assert.Equal(active, session.CurrentPath);
        Assert.Equal(active, session.AccessRoot);
    }

    [AvaloniaFact]
    public void CollapseAndHideControls_PreservePinsAndRestoreTheSidebar()
    {
        using var directories = new TemporaryFolders();
        var store = Pins(directories.Create("Teaching"));
        using var window = CreateWindow(store, out _);
        window.Show();
        var panel = window.FindControl<Border>("PinnedFoldersPanel")!;
        var body = window.FindControl<Control>("PinnedFoldersBody")!;
        var graph = window.FindControl<GraphSceneControl>("GraphScene")!;
        Assert.True(panel.IsVisible);
        Assert.True(body.IsVisible);
        Assert.True(graph.ScenePadding.Left > 250);
        Click(window.FindControl<Button>("PinsCollapseButton")!);
        Assert.True(panel.IsVisible);
        Assert.False(body.IsVisible);
        Assert.Equal(20, graph.ScenePadding.Left);
        window.KeyPress(Key.H, RawInputModifiers.Control, PhysicalKey.H, "h");
        Assert.False(panel.IsVisible);
        window.KeyPress(Key.H, RawInputModifiers.Control, PhysicalKey.H, "h");
        Assert.True(panel.IsVisible);
        Assert.False(body.IsVisible);
        Click(window.FindControl<Button>("PinsCollapseButton")!);
        Assert.True(body.IsVisible);
        Assert.Single(Rows(window));
        Assert.Equal(0, store.Saves);
    }

    [AvaloniaFact]
    public async Task MissingPin_ReportsUnavailableWithoutReplacingActiveRoot()
    {
        using var directories = new TemporaryFolders();
        var active = directories.Create("Active");
        var missing = Path.Combine(directories.Root, "Disconnected drive");
        var store = Pins(missing);
        using var window = CreateWindow(store, out var session);
        window.Show();
        await OpenRootAsync(session, active);
        var scene = session.Neighborhood;
        Click(OpenButton(Rows(window)[0]));
        await WaitUntilAsync(() => Status(window).Contains("unavailable", StringComparison.Ordinal));
        Assert.Equal(active, session.CurrentPath);
        Assert.Equal(active, session.AccessRoot);
        Assert.Same(scene, session.Neighborhood);
        Assert.Single(store.Saved.Folders);
        Assert.Equal(0, store.Saves);
    }

    [AvaloniaFact]
    public async Task SaveFailure_KeepsSessionPinAndExplainsThatItWasNotPersisted()
    {
        using var directories = new TemporaryFolders();
        var active = directories.Create("Teaching");
        var store = new MemoryPinsStore { CanSave = false };
        using var window = CreateWindow(store, out var session);
        window.Show();
        await OpenRootAsync(session, active);
        Click(window.FindControl<Button>("PinCurrentFolderButton")!);
        await WaitUntilAsync(() => Status(window).Contains("Could not save", StringComparison.Ordinal));
        Assert.Equal(active, Assert.Single(Rows(window)).Tag);
        Assert.Empty(store.Saved.Folders);
        Assert.Equal(active, session.CurrentPath);
    }

    [AvaloniaTheory]
    [InlineData("pinned-sidebar", 1280, 800, false)]
    [InlineData("pinned-sidebar-compact", 820, 520, false)]
    [InlineData("pinned-sidebar-light", 1280, 800, true)]
    public async Task RenderPinnedSidebarEvidence(string name, int width, int height, bool light)
    {
        using var directories = new TemporaryFolders();
        var root = directories.Create("Workspace");
        string[] names = ["Teaching", "Research", "Projects", "Presentations", "Photography", "Writing", "Resources", "Archive"];
        foreach (var folder in names)
        {
            Directory.CreateDirectory(Path.Combine(root, folder, "2026"));
            Directory.CreateDirectory(Path.Combine(root, folder, "Shared"));
        }
        await File.WriteAllTextAsync(Path.Combine(root, "Notes.md"), "Synthetic rendering evidence.");
        var store = Pins(names.Take(4).Select(folder => Path.Combine(root, folder)).ToArray());
        using var window = CreateWindow(store, out var session);
        window.Width = width;
        window.Height = height;
        window.Show();
        if (light) { window.FindControl<ComboBox>("ThemePicker")!.SelectedIndex = 1; }
        await OpenRootAsync(session, root);
        using var frame = window.CaptureRenderedFrame();
        var panel = window.FindControl<Border>("PinnedFoldersPanel")!;
        var button = window.FindControl<Button>("PinFolderButton")!;
        var buttonOrigin = button.TranslatePoint(default, window)!.Value;
        Assert.True(panel.IsVisible);
        Assert.True(button.IsEffectivelyVisible);
        Assert.InRange(buttonOrigin.Y + button.Bounds.Height, 0, window.ClientSize.Height);
        Assert.Equal(4, Rows(window).Count);
        var output = Environment.GetEnvironmentVariable("OMNIBRILLE_RENDER_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            Assert.NotNull(frame);
            frame.Save(Path.Combine(output, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaFact]
    public async Task CompactSidebar_TwelveLongNamesAndStatusKeepActionsAboveZoomControls()
    {
        using var directories = new TemporaryFolders();
        var active = directories.Create("Workspace");
        var paths = Enumerable.Range(1, 12)
            .Select(index => Path.Combine(active, $"Very long saved folder name for semester teaching resources {index:D2}"))
            .ToArray();
        using var window = CreateWindow(Pins(paths), out var session);
        window.Width = 820;
        window.Height = 520;
        window.Show();
        await OpenRootAsync(session, active);
        Click(OpenButton(Rows(window)[0]));
        await WaitUntilAsync(() => Status(window).Contains("unavailable", StringComparison.Ordinal));
        using var frame = window.CaptureRenderedFrame();
        var panel = window.FindControl<Border>("PinnedFoldersPanel")!;
        var zoom = window.FindControl<Border>("ZoomHud")!;
        var panelOrigin = panel.TranslatePoint(default, window)!.Value;
        var zoomOrigin = zoom.TranslatePoint(default, window)!.Value;
        Assert.True(panelOrigin.Y + panel.Bounds.Height <= zoomOrigin.Y,
            "All pin actions and wrapped status must stay above the zoom controls at 820×520.");
        var emptyHint = window.FindControl<Border>("StructureEmptyPanel")!;
        if (emptyHint.IsEffectivelyVisible)
        {
            var hintOrigin = emptyHint.TranslatePoint(default, window)!.Value;
            Assert.False(new Rect(panelOrigin, panel.Bounds.Size).Intersects(new Rect(hintOrigin, emptyHint.Bounds.Size)),
                "The empty-folder guidance must not cover pinned folders or their actions.");
        }
        var pinAction = window.FindControl<Button>("PinFolderButton")!;
        var actionOrigin = pinAction.TranslatePoint(default, window)!.Value;
        Assert.True(actionOrigin.Y + pinAction.Bounds.Height <= zoomOrigin.Y);
        var scroll = window.FindControl<ScrollViewer>("PinnedFoldersScroll")!;
        Assert.True(scroll.Bounds.Height >= 44, "At least one complete pin action remains reachable.");
        Assert.Equal(12, Rows(window).Count);
        var first = OpenButton(Rows(window)[0]);
        var label = Assert.Single(first.GetVisualDescendants().OfType<TextBlock>());
        var labelOrigin = label.TranslatePoint(default, first)!.Value;
        Assert.True(labelOrigin.X + label.Bounds.Width <= first.Bounds.Width,
            "Long folder labels must stay within their open button and leave Unpin reachable.");
        var output = Environment.GetEnvironmentVariable("OMNIBRILLE_RENDER_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            Assert.NotNull(frame);
            frame.Save(Path.Combine(output, "pinned-sidebar-compact-status.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    [AvaloniaFact]
    public void PinnedIcons_FollowThemeAndReducedEffectsSettings()
    {
        using var directories = new TemporaryFolders();
        using var window = CreateWindow(Pins(directories.Create("Teaching")), out _);
        window.Show();
        var icon = Assert.Single(OpenButton(Rows(window)[0]).GetVisualDescendants().OfType<PinnedFolderIcon>());
        Assert.Equal(ThemeVariant.Dark, icon.ActualThemeVariant);
        Assert.False(icon.ReducedEffects);
        window.FindControl<ComboBox>("ThemePicker")!.SelectedIndex = 1;
        window.FindControl<CheckBox>("ReducedEffectsToggle")!.IsChecked = true;
        using (window.CaptureRenderedFrame()) { }
        Assert.Equal(ThemeVariant.Light, icon.ActualThemeVariant);
        Assert.True(icon.ReducedEffects);
        window.FindControl<ComboBox>("ThemePicker")!.SelectedIndex = 0;
        window.FindControl<CheckBox>("ReducedEffectsToggle")!.IsChecked = false;
        Assert.Equal(ThemeVariant.Dark, icon.ActualThemeVariant);
        Assert.False(icon.ReducedEffects);
    }

    private static MainWindow CreateWindow(MemoryPinsStore store, out ExplorerSession session)
    {
        session = new ExplorerSession();
        return new MainWindow(session, new MemoryPreferencesStore(), pinnedFoldersStore: store);
    }

    private static MemoryPinsStore Pins(params string[] paths) => new()
    {
        Saved = new PinnedFolders(paths.Select(path => new PinnedFolder(path, Path.GetFileName(path))).ToArray()),
    };

    private static List<Grid> Rows(MainWindow window) => window.FindControl<StackPanel>("PinnedEntries")!.Children.OfType<Grid>().ToList();

    private static Button OpenButton(Grid row) => row.Children.OfType<Button>().Single(button => Grid.GetColumn(button) == 0);

    private static string Status(MainWindow window) => window.FindControl<TextBlock>("PinnedStatusText")!.Text ?? string.Empty;

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Task OpenRootAsync(ExplorerSession session, string path)
    {
        var provider = new FileSystemExplorerProvider(path);
        return session.OpenRootAsync(provider, provider);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        Assert.True(condition(), "The expected pinned-folder state was not reached before the test deadline.");
    }

    private sealed class MemoryPinsStore : IPinnedFoldersStore
    {
        public PinnedFolders Saved { get; set; } = PinnedFolders.Empty;
        public int Saves { get; private set; }
        public bool CanSave { get; init; } = true;
        public PinnedFolders Load() => Saved;
        public bool TrySave(PinnedFolders folders)
        {
            Saves++;
            if (!CanSave) { return false; }
            Saved = folders;
            return true;
        }
    }

    private sealed class MemoryPreferencesStore : IVisualPreferencesStore
    {
        private VisualPreferences _saved = new(ReducedMotion: true, VoiceEnabled: false);
        public VisualPreferences Load() => _saved;
        public void Save(VisualPreferences preferences) => _saved = preferences;
    }

    private sealed class TemporaryFolders : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "OmniBrillePins-" + Guid.NewGuid().ToString("N"));
        public string Create(string name) => Directory.CreateDirectory(Path.Combine(Root, name)).FullName;
        public void Dispose()
        {
            if (Directory.Exists(Root)) { Directory.Delete(Root, recursive: true); }
        }
    }
}

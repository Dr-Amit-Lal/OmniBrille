using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using OmniBrille.Core;
using OmniBrille.Desktop;
using OmniBrille.Desktop.Presentation;
using OmniBrille.Desktop.Rendering;
using OmniBrille.Infrastructure;

namespace OmniBrille.HeadlessTests;

public sealed class WorkspaceWindowTests
{
    [AvaloniaFact]
    public async Task Restart_RestoresThreeRootsPinsAndCurrentFolderFromJson()
    {
        using var fixture = new WorkspaceFixture();
        fixture.Save();
        using (var window = fixture.CreateWindow())
        {
            await ShowAsync(window);
            Assert.True(window.Session.IsOverview);
            Assert.Equal(3, window.Session.State.Roots.Count);
            Assert.Equal(3, window.FindControl<StackPanel>("RootsEntries")!.Children.Count);
            Click(PinButton(window, fixture.Letters));
            await WaitUntilAsync(() => !window.Session.IsBusy && window.Session.CurrentPath == fixture.Letters);
            Assert.Equal(fixture.Documents, window.Session.CurrentRoot);
            Assert.Equal(fixture.Letters, fixture.Store.Load().CurrentPath);
        }

        using var restarted = fixture.CreateWindow();
        await ShowAsync(restarted);
        Assert.Equal(new[] { fixture.Documents, fixture.Projects, fixture.Pictures }, restarted.Session.State.Roots.Select(root => root.Path));
        Assert.Equal(new[] { fixture.Letters, fixture.ProjectNotes }, restarted.Session.State.Pins.Select(pin => pin.Path));
        Assert.Equal(fixture.Letters, restarted.Session.CurrentPath);
        Assert.Equal(fixture.Documents, restarted.Session.CurrentRoot);
        Assert.Equal(2, PinRows(restarted).Count);
    }

    [AvaloniaFact]
    public async Task PinnedFolder_RightClickAscendsItsRealParentChainThenWorkspace()
    {
        using var fixture = new WorkspaceFixture();
        fixture.Save();
        using var window = fixture.CreateWindow();
        await ShowAsync(window);
        Click(PinButton(window, fixture.Letters));
        await WaitUntilAsync(() => window.Session.CurrentPath == fixture.Letters && !window.Session.IsBusy);

        foreach (var parent in new[] { fixture.Teaching, fixture.Documents })
        {
            RightClickGraph(window);
            await WaitUntilAsync(() => window.Session.CurrentPath == parent && !window.Session.IsBusy);
            Assert.Equal(fixture.Documents, window.Session.CurrentRoot);
            Assert.False(window.Session.IsOverview);
        }
        RightClickGraph(window);
        await WaitUntilAsync(() => window.Session.IsOverview && !window.Session.IsBusy);
        Assert.Null(window.Session.CurrentRoot);
        Assert.Equal(3, window.Session.State.Roots.Count);
        Assert.DoesNotContain(window.Session.Scene!.Nodes, node => node.Path == fixture.Root);
    }

    [AvaloniaFact]
    public async Task WorkspaceFolderButtons_SwitchSavedRootsFromANestedPinnedFolder()
    {
        using var fixture = new WorkspaceFixture();
        fixture.Save();
        using var window = fixture.CreateWindow();
        await ShowAsync(window);
        Click(PinButton(window, fixture.Letters));
        await WaitUntilAsync(() => window.Session.CurrentPath == fixture.Letters && !window.Session.IsBusy);
        Click(RootButton(window, fixture.Projects));
        await WaitUntilAsync(() => window.Session.CurrentPath == fixture.Projects && !window.Session.IsBusy);
        Assert.Equal(fixture.Projects, window.Session.CurrentRoot);
        Click(RootButton(window, fixture.Documents));
        await WaitUntilAsync(() => window.Session.CurrentPath == fixture.Documents && !window.Session.IsBusy);
        Assert.Equal(fixture.Documents, window.Session.CurrentRoot);
    }

    [AvaloniaFact]
    public async Task GraphAutomationSelectionAndKeyboardActivationUseTheSameSession()
    {
        using var fixture = new WorkspaceFixture();
        fixture.Save();
        using var window = fixture.CreateWindow();
        await ShowAsync(window);
        using (window.CaptureRenderedFrame()) { }
        var graph = window.FindControl<GraphSceneControl>("GraphScene")!;
        var peer = ControlAutomationPeer.CreatePeerForElement(graph);
        var documents = Assert.Single(peer.GetChildren(), item => item.GetName().StartsWith("Documents, ", StringComparison.Ordinal));
        documents.SetFocus();
        Assert.True(window.Session.IsOverview);
        Assert.Equal(fixture.Documents, window.Session.NodeById(window.Session.SelectedNodeId)!.Path);
        graph.Focus();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await WaitUntilAsync(() => window.Session.CurrentPath == fixture.Documents && !window.Session.IsBusy);
        using (window.CaptureRenderedFrame()) { }
        var teaching = Assert.Single(peer.GetChildren(), item => item.GetName().StartsWith("Teaching, ", StringComparison.Ordinal));
        Assert.IsAssignableFrom<IInvokeProvider>(teaching.GetProvider<IInvokeProvider>()).Invoke();
        await WaitUntilAsync(() => window.Session.CurrentPath == fixture.Teaching && !window.Session.IsBusy);
        Assert.Equal(fixture.Documents, window.Session.CurrentRoot);
        Click(window.FindControl<Button>("UpButton")!);
        await WaitUntilAsync(() => window.Session.CurrentPath == fixture.Documents && !window.Session.IsBusy);
    }

    [AvaloniaFact]
    public async Task SearchResultHoverPreviewsWithoutNavigationAndEnterOpensItsFolder()
    {
        using var fixture = new WorkspaceFixture();
        fixture.Save();
        using var window = fixture.CreateWindow();
        window.Width = 1600;
        window.Height = 1000;
        await ShowAsync(window);
        await SearchAsync(window, "Letters");
        Assert.True(window.Session.IsOverview);
        var list = window.FindControl<ListBox>("SearchResultsList")!;
        var hit = Assert.Single(window.Session.SearchResults, item => item.Path == fixture.Letters);
        var index = window.Session.SearchResults.ToList().IndexOf(hit);
        using (window.CaptureRenderedFrame()) { }
        var row = Assert.IsAssignableFrom<Control>(list.ContainerFromIndex(index));
        var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        var original = window.Session.CurrentPath;
        window.MouseMove(point);
        await WaitUntilAsync(() => window.Session.PreviewNodeId is not null);
        Assert.Equal(original, window.Session.CurrentPath);
        Assert.True(window.Session.IsOverview);
        Assert.NotEmpty(window.Session.PreviewPathNodeIds);
        Assert.Null(window.Session.SelectedNodeId);

        list.SelectedIndex = index;
        Assert.True(row.Focus());
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await WaitUntilAsync(() => window.Session.CurrentPath == fixture.Letters && !window.Session.IsBusy);
        Assert.Equal(fixture.Documents, window.Session.CurrentRoot);
    }

    [AvaloniaFact]
    public async Task ExplicitSearch_DeferredTextEventsKeepTheFocusedResultStable()
    {
        using var fixture = new WorkspaceFixture();
        fixture.Save();
        using var window = fixture.CreateWindow();
        await ShowAsync(window);
        await SearchAsync(window, "Letters");
        var results = window.Session.SearchResults;
        var list = window.FindControl<ListBox>("SearchResultsList")!;
        using (window.CaptureRenderedFrame()) { }
        var row = Assert.IsAssignableFrom<Control>(list.ContainerFromIndex(0));
        list.SelectedIndex = 0;
        Assert.True(row.Focus());
        await Task.Delay(400);
        Assert.Same(results, window.Session.SearchResults);
        Assert.Same(row, list.ContainerFromIndex(0));
        Assert.True(row.IsKeyboardFocusWithin);
        Assert.Equal(0, list.SelectedIndex);
    }

    [AvaloniaFact]
    public async Task HideControlsAndRestore_PreserveSearchRootsAndPins()
    {
        using var fixture = new WorkspaceFixture();
        fixture.Save();
        using var window = fixture.CreateWindow();
        await ShowAsync(window);
        await SearchAsync(window, "Letters");
        var graph = window.FindControl<GraphSceneControl>("GraphScene")!;
        using (window.CaptureRenderedFrame()) { }
        var shownWidth = GraphContentBounds(graph, window).Width;
        window.KeyPress(Key.H, RawInputModifiers.Control, PhysicalKey.H, "h");
        using (window.CaptureRenderedFrame()) { }
        Assert.False(window.FindControl<Border>("Toolbar")!.IsVisible);
        Assert.False(window.FindControl<Control>("PinnedPanel")!.IsEffectivelyVisible);
        Assert.True(GraphContentBounds(graph, window).Width >= shownWidth);
        Assert.True(window.FindControl<Border>("MinimalSearchPanel")!.IsEffectivelyVisible);
        Assert.Equal("Letters", window.Session.SearchQuery);
        Assert.Equal(3, window.Session.State.Roots.Count);
        Assert.Equal(2, window.Session.State.Pins.Count);
        window.KeyPress(Key.H, RawInputModifiers.Control, PhysicalKey.H, "h");
        Assert.True(window.FindControl<Border>("SidePanel")!.IsVisible);
        Assert.Equal("Letters", window.Session.SearchQuery);
        Assert.True(window.Session.IsOverview);
    }

    [AvaloniaFact]
    public async Task HiddenSearch_EnterShowsResultsWhileKeepingChromeHiddenUntilRestore()
    {
        using var fixture = new WorkspaceFixture();
        fixture.Save();
        using var window = fixture.CreateWindow();
        await ShowAsync(window);
        window.KeyPress(Key.H, RawInputModifiers.Control, PhysicalKey.H, "h");
        var search = window.FindControl<TextBox>("MinimalSearchBox")!;
        search.Text = "Letters";
        Assert.True(search.Focus());
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await WaitUntilAsync(() => !window.Session.IsSearching && window.Session.SearchQuery == "Letters" && window.Session.SearchResults.Count > 0);
        Assert.False(window.FindControl<Border>("Toolbar")!.IsVisible);
        Assert.False(window.FindControl<Control>("PinnedPanel")!.IsEffectivelyVisible);
        Assert.True(window.FindControl<Control>("SearchPanel")!.IsEffectivelyVisible);
        var minimal = window.FindControl<Border>("MinimalSearchPanel")!;
        Assert.True(minimal.IsEffectivelyVisible);
        Click(Assert.IsType<Grid>(minimal.Child).Children.OfType<Button>().Single());
        Assert.True(window.FindControl<Border>("Toolbar")!.IsVisible);
        Assert.Equal("Letters", window.Session.SearchQuery);
        Assert.True(window.Session.IsOverview);
    }

    [AvaloniaFact]
    public async Task FileSearchResult_SelectsItsContainingFolderAndOpensOnlyOnExplicitAction()
    {
        using var fixture = new WorkspaceFixture();
        fixture.Save();
        var activation = new RecordingFileActivation();
        using var window = fixture.CreateWindow(activation);
        await ShowAsync(window);
        await SearchAsync(window, "Document 01");
        var list = window.FindControl<ListBox>("SearchResultsList")!;
        var hit = Assert.Single(window.Session.SearchResults);
        using (window.CaptureRenderedFrame()) { }
        Assert.True(window.FindControl<TextBox>("SearchBox")!.Focus());
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Assert.Equal(0, list.SelectedIndex);
        Assert.True(Assert.IsAssignableFrom<Control>(list.ContainerFromIndex(0)).IsKeyboardFocusWithin);
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await WaitUntilAsync(() => window.Session.CurrentPath == fixture.Documents && window.Session.SelectedNodeId is not null && !window.Session.IsBusy);
        Assert.Equal(hit.Path, window.Session.NodeById(window.Session.SelectedNodeId)!.Path);
        Assert.Equal(0, activation.Calls);
        Click(window.FindControl<Button>("CloseSearchButton")!);
        Assert.True(window.FindControl<Border>("FileDetailsPanel")!.IsEffectivelyVisible);
        Click(window.FindControl<Button>("OpenFileButton")!);
        await WaitUntilAsync(() => activation.Calls == 1);
        Assert.Equal(fixture.Documents, activation.Root);
        Assert.Equal(hit.Path, activation.Path);
    }

    [AvaloniaFact]
    public async Task PinCurrentFolder_AddsOnceAndUnpinPersistsWithoutChangingTheFolder()
    {
        using var fixture = new WorkspaceFixture();
        fixture.Save();
        using var window = fixture.CreateWindow();
        await ShowAsync(window);
        await window.Session.NavigateAsync(SpatialWorkspaceSession.Key(fixture.Documents, fixture.Documents));
        await window.Session.NavigateAsync(SpatialWorkspaceSession.Key(fixture.Documents, fixture.Teaching));
        Click(window.FindControl<Button>("PinCurrentFolderButton")!);
        await WaitUntilAsync(() => window.Session.State.Pins.Count == 3);
        Click(window.FindControl<Button>("PinCurrentFolderButton")!);
        await WaitUntilAsync(() => window.Session.Status.Contains("already pinned", StringComparison.Ordinal));
        Assert.Equal(3, window.Session.State.Pins.Count);
        Assert.Contains(fixture.Store.Load().Pins, pin => pin.Path == fixture.Teaching);
        var row = PinRows(window).Single(item => item.Children.OfType<Button>().Any(button => Equals(button.Tag, fixture.Teaching)));
        Click(row.Children.OfType<Button>().Single(button => Grid.GetColumn(button) == 1));
        Assert.Equal(2, window.Session.State.Pins.Count);
        Assert.DoesNotContain(fixture.Store.Load().Pins, pin => pin.Path == fixture.Teaching);
        Assert.Equal(fixture.Teaching, window.Session.CurrentPath);
    }

    [AvaloniaFact]
    public async Task PinDragAndKeyboardReorder_PersistWithoutOpeningEitherPin()
    {
        using var fixture = new WorkspaceFixture();
        fixture.Save();
        Assert.True(fixture.Store.TrySave(fixture.Store.Load() with
        {
            Pins = [new(fixture.Letters, "Letters"), new(fixture.ProjectNotes, "Project notes"),
                new(fixture.Teaching, "Teaching"), new(fixture.Projects, "Projects")],
        }));
        using var window = fixture.CreateWindow();
        await ShowAsync(window);
        await window.Session.NavigateAsync(SpatialWorkspaceSession.Key(fixture.Documents, fixture.Documents));
        using (window.CaptureRenderedFrame()) { }
        var first = PinButton(window, fixture.Letters);
        var last = PinButton(window, fixture.Projects);
        var start = first.TranslatePoint(new Point(first.Bounds.Width / 2, first.Bounds.Height / 2), window)!.Value;
        var end = last.TranslatePoint(new Point(last.Bounds.Width / 2, last.Bounds.Height / 2), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(start.X, start.Y + 12), RawInputModifiers.LeftMouseButton);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left);
        Assert.Equal(new[] { fixture.ProjectNotes, fixture.Teaching, fixture.Projects, fixture.Letters }, fixture.Store.Load().Pins.Select(pin => pin.Path));
        Assert.Equal(fixture.Documents, window.Session.CurrentPath);
        Assert.True(PinButton(window, fixture.Letters).IsFocused);
        PinButton(window, fixture.Letters).Focus();
        window.KeyPress(Key.Up, RawInputModifiers.Alt, PhysicalKey.ArrowUp, null);
        Assert.Equal(new[] { fixture.ProjectNotes, fixture.Teaching, fixture.Letters, fixture.Projects }, fixture.Store.Load().Pins.Select(pin => pin.Path));
        Assert.Equal(fixture.Documents, window.Session.CurrentPath);
    }

    [AvaloniaFact]
    public async Task MissingRoot_RemainsSavedWithoutReplacingTheAvailableScene()
    {
        using var fixture = new WorkspaceFixture();
        var missing = Path.Combine(fixture.Root, "Disconnected drive");
        fixture.Save(extraRoot: missing);
        using var window = fixture.CreateWindow();
        await ShowAsync(window);
        Assert.Contains(window.Session.State.Roots, root => root.Path == missing);
        Assert.Contains(window.Session.Scene!.Nodes, node => node.Path == fixture.Documents);
        Assert.Contains(fixture.Store.Load().Roots, root => root.Path == missing);
        Click(RootButton(window, missing));
        await WaitUntilAsync(() => !window.Session.IsBusy && window.Session.Status.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
        Assert.True(window.Session.IsOverview);
        Assert.Contains(fixture.Store.Load().Roots, root => root.Path == missing);
        Assert.Contains(window.Session.Scene!.Nodes, node => node.Path == fixture.Documents);
    }

    [AvaloniaTheory]
    [InlineData(1600, 1000)]
    [InlineData(820, 520)]
    public async Task SearchPanel_UsesAvailableHeightWithoutCoveringTheGraph(int width, int height)
    {
        using var fixture = new WorkspaceFixture(dense: true);
        fixture.Save();
        using var window = fixture.CreateWindow();
        window.Width = width;
        window.Height = height;
        await ShowAsync(window);
        await SearchAsync(window, "Document");
        using (window.CaptureRenderedFrame()) { }
        var side = window.FindControl<Border>("SidePanel")!;
        var graph = window.FindControl<GraphSceneControl>("GraphScene")!;
        var search = window.FindControl<Control>("SearchPanel")!;
        var results = window.FindControl<ListBox>("SearchResultsList")!;
        Assert.True(search.IsEffectivelyVisible);
        Assert.True(side.Bounds.Height >= window.ClientSize.Height - 240);
        Assert.True(results.Bounds.Height >= (height > 600 ? window.ClientSize.Height / 3 : 68));
        Assert.False(BoundsInWindow(side, window).Intersects(GraphContentBounds(graph, window)));
        Assert.True(BoundsInWindow(results, window).Bottom <= window.ClientSize.Height);
    }

    [AvaloniaTheory]
    [InlineData("workspace-overview", 1600, 1000, false, false, 1.0)]
    [InlineData("workspace-search", 1600, 1000, true, true, 1.0)]
    [InlineData("workspace-search-compact", 820, 520, true, true, 1.0)]
    [InlineData("workspace-compact", 820, 520, false, false, 1.0)]
    [InlineData("workspace-dense", 1600, 1000, true, false, 1.0)]
    [InlineData("workspace-text-150", 1600, 1000, true, false, 1.5)]
    [InlineData("workspace-text-200", 820, 520, true, false, 2.0)]
    public async Task RenderWorkspaceEvidence(string name, int width, int height, bool dense, bool search, double textScale)
    {
        using var fixture = new WorkspaceFixture(dense);
        fixture.Save();
        using var window = fixture.CreateWindow();
        window.Width = width;
        window.Height = height;
        await ShowAsync(window);
        if (dense && !search)
        {
            await window.Session.NavigateAsync(SpatialWorkspaceSession.Key(fixture.Documents, fixture.Documents));
        }
        if (search)
        {
            await SearchAsync(window, "Letters");
            await HoverSearchResultAsync(window, fixture.Letters);
        }
        var graph = window.FindControl<GraphSceneControl>("GraphScene")!;
        graph.TextScale = textScale;
        using var frame = window.CaptureRenderedFrame();
        Assert.True(graph.Diagnostics.Nodes > 0);
        Assert.Equal(graph.Diagnostics.Nodes, graph.Diagnostics.Labels);
        Assert.False(BoundsInWindow(window.FindControl<Border>("SidePanel")!, window).Intersects(GraphContentBounds(graph, window)));
        var output = Environment.GetEnvironmentVariable("OMNIBRILLE_RENDER_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            Assert.NotNull(frame);
            frame.Save(Path.Combine(output, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        Assert.Equal(0, graph.Diagnostics.OverlappingLabelPairs);
    }

    [AvaloniaTheory]
    [InlineData("workspace-eight-roots", false)]
    [InlineData("workspace-hidden-search", true)]
    public async Task RenderCompactWorkspaceEdgeEvidence(string name, bool hiddenSearch)
    {
        using var fixture = new WorkspaceFixture();
        fixture.Save();
        if (!hiddenSearch)
        {
            string[] names = ["Archive", "Research", "Resources", "Teaching", "Travel"];
            var added = names.Select(folder => new FileMapRoot(
                Directory.CreateDirectory(Path.Combine(fixture.Root, folder)).FullName, folder));
            Assert.True(fixture.Store.TrySave(fixture.Store.Load() with { Roots = fixture.Store.Load().Roots.Concat(added).ToArray() }));
        }
        using var window = fixture.CreateWindow();
        window.Width = 820;
        window.Height = 520;
        await ShowAsync(window);
        if (hiddenSearch)
        {
            window.KeyPress(Key.H, RawInputModifiers.Control, PhysicalKey.H, "h");
            window.FindControl<TextBox>("MinimalSearchBox")!.Text = "Letters";
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await WaitUntilAsync(() => !window.Session.IsSearching && window.Session.SearchResults.Count > 0);
            await HoverSearchResultAsync(window, fixture.Letters);
            Assert.False(window.FindControl<Border>("Toolbar")!.IsVisible);
            Assert.True(window.FindControl<Control>("SearchPanel")!.IsEffectivelyVisible);
        }
        else { Assert.Equal(8, window.Session.State.Roots.Count); }
        var graph = window.FindControl<GraphSceneControl>("GraphScene")!;
        using var frame = window.CaptureRenderedFrame();
        var output = Environment.GetEnvironmentVariable("OMNIBRILLE_RENDER_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            Assert.NotNull(frame);
            frame.Save(Path.Combine(output, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        Assert.Equal(graph.Diagnostics.Nodes, graph.Diagnostics.Labels);
        Assert.Equal(0, graph.Diagnostics.OverlappingLabelPairs);
        Assert.False(BoundsInWindow(window.FindControl<Border>("SidePanel")!, window).Intersects(GraphContentBounds(graph, window)));
    }

    private static async Task ShowAsync(MainWindow window)
    {
        window.Show();
        await window.Initialization;
        await WaitUntilAsync(() => !window.Session.IsBusy);
    }

    private static async Task SearchAsync(MainWindow window, string query)
    {
        Click(window.FindControl<Button>("SearchToggleButton")!);
        window.FindControl<TextBox>("SearchBox")!.Text = query;
        Click(window.FindControl<Button>("SearchButton")!);
        await WaitUntilAsync(() => !window.Session.IsSearching && window.Session.SearchQuery == query && window.Session.SearchResults.Count > 0);
    }

    private static async Task HoverSearchResultAsync(MainWindow window, string path)
    {
        var index = window.Session.SearchResults.ToList().FindIndex(hit => hit.Path == path);
        Assert.True(index >= 0);
        using (window.CaptureRenderedFrame()) { }
        var row = Assert.IsAssignableFrom<Control>(window.FindControl<ListBox>("SearchResultsList")!.ContainerFromIndex(index));
        var point = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        var hit = window.Session.SearchResults[index];
        await WaitUntilAsync(() => window.Session.PreviewNodeId == SpatialWorkspaceSession.Key(hit.RootPath, hit.Path));
    }

    private static List<Grid> PinRows(MainWindow window) => window.FindControl<StackPanel>("PinnedEntries")!.Children.OfType<Grid>().ToList();

    private static Button PinButton(MainWindow window, string path) => PinRows(window).SelectMany(row => row.Children.OfType<Button>()).Single(button => Equals(button.Tag, path));

    private static Button RootButton(MainWindow window, string path) => window.FindControl<StackPanel>("RootsEntries")!.Children.OfType<Grid>()
        .SelectMany(row => row.Children.OfType<Button>()).Single(button => Equals(button.Tag, path));

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Rect BoundsInWindow(Control control, MainWindow window) => new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    private static Rect GraphContentBounds(GraphSceneControl graph, MainWindow window)
    {
        var origin = graph.TranslatePoint(default, window)!.Value;
        var padding = graph.ScenePadding;
        return new Rect(origin.X + padding.Left, origin.Y + padding.Top,
            Math.Max(0, graph.Bounds.Width - padding.Left - padding.Right),
            Math.Max(0, graph.Bounds.Height - padding.Top - padding.Bottom));
    }

    private static void RightClickGraph(MainWindow window)
    {
        using (window.CaptureRenderedFrame()) { }
        var graph = window.FindControl<GraphSceneControl>("GraphScene")!;
        var point = graph.TranslatePoint(new Point(graph.Bounds.Width / 2, graph.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) { await Task.Delay(10); }
        Assert.True(condition(), "The expected workspace UI state was not reached before the test deadline.");
    }

    private sealed class MemoryPreferencesStore : IVisualPreferencesStore
    {
        private VisualPreferences _saved = new(ReducedMotion: true);
        public VisualPreferences Load() => _saved;
        public void Save(VisualPreferences preferences) => _saved = preferences;
    }

    private sealed class RecordingFileActivation : IFileActivationService
    {
        public int Calls { get; private set; }
        public string? Root { get; private set; }
        public string? Path { get; private set; }
        public Task<FileActivationResult> OpenAsync(string accessRoot, string path, CancellationToken cancellationToken = default)
        {
            Calls++;
            Root = accessRoot;
            Path = path;
            return Task.FromResult(FileActivationResult.Opened);
        }
    }

    private sealed class WorkspaceFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "SpatialWorkspaceUI-" + Guid.NewGuid().ToString("N"));
        public string Documents { get; }
        public string Teaching { get; }
        public string Letters { get; }
        public string Projects { get; }
        public string ProjectNotes { get; }
        public string Pictures { get; }
        public JsonSpatialWorkspaceStore Store { get; }

        public WorkspaceFixture(bool dense = false)
        {
            Documents = Directory.CreateDirectory(Path.Combine(Root, "Documents")).FullName;
            Teaching = Directory.CreateDirectory(Path.Combine(Documents, "Teaching")).FullName;
            Letters = Directory.CreateDirectory(Path.Combine(Teaching, "Letters")).FullName;
            Projects = Directory.CreateDirectory(Path.Combine(Root, "Projects")).FullName;
            ProjectNotes = Directory.CreateDirectory(Path.Combine(Projects, "Notes")).FullName;
            Pictures = Directory.CreateDirectory(Path.Combine(Root, "Pictures")).FullName;
            Directory.CreateDirectory(Path.Combine(Documents, "Research"));
            Directory.CreateDirectory(Path.Combine(Documents, "Presentations"));
            Directory.CreateDirectory(Path.Combine(Documents, "Archive"));
            Directory.CreateDirectory(Path.Combine(Projects, "Spatial Explorer"));
            Directory.CreateDirectory(Path.Combine(Pictures, "Travel"));
            Directory.CreateDirectory(Path.Combine(Pictures, "Family"));
            File.WriteAllText(Path.Combine(Letters, "Welcome.md"), "Synthetic folder-navigation fixture.");
            File.WriteAllText(Path.Combine(Teaching, "Course overview.txt"), "Synthetic teaching fixture.");
            File.WriteAllText(Path.Combine(ProjectNotes, "Roadmap.md"), "Synthetic project fixture.");
            File.WriteAllText(Path.Combine(Pictures, "Gallery.txt"), "Synthetic gallery fixture.");
            for (var index = 1; index <= (dense ? 48 : 3); index++)
            {
                File.WriteAllText(Path.Combine(Documents, $"Document {index:D2}.md"), "Synthetic rendering fixture.");
            }
            Store = new JsonSpatialWorkspaceStore(Path.Combine(Root, "settings"));
        }

        public void Save(string? extraRoot = null)
        {
            var roots = new List<FileMapRoot> { new(Documents, "Documents"), new(Projects, "Projects"), new(Pictures, "Pictures") };
            if (extraRoot is not null) { roots.Add(new(extraRoot, "Disconnected drive")); }
            Assert.True(Store.TrySave(new SpatialWorkspace(roots, [new(Letters, "Letters"), new(ProjectNotes, "Project notes")])));
        }

        public MainWindow CreateWindow(IFileActivationService? activation = null) => new(new SpatialWorkspaceSession(Store, activation: activation), new MemoryPreferencesStore(), monitorChanges: false);

        public void Dispose()
        {
            if (Directory.Exists(Root)) { Directory.Delete(Root, recursive: true); }
        }
    }
}

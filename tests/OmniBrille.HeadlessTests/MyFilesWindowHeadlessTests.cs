using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using OmniBrille.Core;
using OmniBrille.Desktop;
using OmniBrille.Desktop.Presentation;

namespace OmniBrille.HeadlessTests;

public sealed class MyFilesWindowHeadlessTests
{
    [AvaloniaFact]
    public async Task AutomationInvoke_ExpandsRealFolderAndExposesCompleteFileName()
    {
        using var fixture = new MapFixture();
        using var window = fixture.CreateWindow();
        window.Show();
        await ReadyAsync(window, fixture.Store.Workspace.Roots.Count);

        var teaching = NodeButton(window, "Teaching");
        Assert.Contains("collapsed", ControlAutomationPeer.CreatePeerForElement(teaching).GetName(), StringComparison.Ordinal);
        teaching.Focus();
        Invoke(teaching);
        await WaitUntilAsync(() => window.Session.VisibleNodes.Any(node => node.Entry.Name == "Database Management"));
        await WaitUntilAsync(() => NodeButton(window, "Teaching").IsFocused);
        NodeButton(window, "Database Management").Focus();
        Invoke(NodeButton(window, "Database Management"));
        await WaitUntilAsync(() => window.Session.VisibleNodes.Any(node => node.Entry.Path == fixture.Syllabus));
        await WaitUntilAsync(() => NodeButton(window, "Database Management").IsFocused);

        var file = NodeButton(window, Path.GetFileName(fixture.Syllabus));
        Assert.Contains(Path.GetFileName(fixture.Syllabus), ControlAutomationPeer.CreatePeerForElement(file).GetName(), StringComparison.Ordinal);
        Assert.Equal(fixture.Syllabus, AutomationProperties.GetHelpText(file));
        window.UpdateLayout();
        Assert.True(file.Bounds.Width >= 44);
        Assert.True(file.Bounds.Height >= 44);
        Assert.Equal(0, fixture.Activation.OpenCount);
    }

    [AvaloniaFact]
    public async Task FileSelection_OpensOnlyThroughExplicitOpenAndCanPinWithoutMovingFile()
    {
        using var fixture = new MapFixture(expanded: true);
        using var window = fixture.CreateWindow();
        window.Show();
        await ReadyAsync(window, 6);
        await WaitUntilAsync(() => window.Session.VisibleNodes.Any(node => node.Entry.Path == fixture.Syllabus));

        Invoke(NodeButton(window, Path.GetFileName(fixture.Syllabus)));
        await WaitUntilAsync(() => window.Session.SelectedNode?.Entry.Path == fixture.Syllabus);
        Assert.True(window.FindControl<Border>("DetailsPanel")!.IsVisible);
        Assert.Equal(fixture.Syllabus, window.FindControl<SelectableTextBlock>("FilePathText")!.Text);
        Assert.Equal(0, fixture.Activation.OpenCount);

        Invoke(window.FindControl<Button>("PinFileButton")!);
        await WaitUntilAsync(() => window.Session.State.Pins.Count == 1);
        Assert.Single(window.Session.State.Pins);
        Assert.Single(fixture.Store.Workspace.Pins);
        Assert.True(window.FindControl<StackPanel>("PinsSection")!.IsVisible);
        Assert.True(File.Exists(fixture.Syllabus));

        Invoke(window.FindControl<Button>("OpenFileButton")!);
        await WaitUntilAsync(() => fixture.Activation.OpenCount == 1);
        Assert.Equal(fixture.Teaching, fixture.Activation.AccessRoot);
        Assert.Equal(fixture.Syllabus, fixture.Activation.Path);
    }

    [AvaloniaFact]
    public async Task CollapsingBranch_RemovesHiddenButtonsAndRestoresParentFocus()
    {
        using var fixture = new MapFixture(expanded: true);
        using var window = fixture.CreateWindow();
        window.Show();
        await ReadyAsync(window, 6);
        await WaitUntilAsync(() => window.Session.VisibleNodes.Any(node => node.Entry.Path == fixture.Syllabus));

        var parent = NodeButton(window, "Database Management");
        parent.Focus();
        Invoke(parent);
        await WaitUntilAsync(() => !window.Session.VisibleNodes.Any(node => node.Entry.Path == fixture.Syllabus));
        await WaitUntilAsync(() => NodeButton(window, "Database Management").IsFocused);

        Assert.DoesNotContain(MapButtons(window), button => AutomationProperties.GetHelpText(button) == fixture.Syllabus);
        Assert.Contains("collapsed", ControlAutomationPeer.CreatePeerForElement(NodeButton(window, "Database Management")).GetName(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task MinimumWindow_LargeTextKeepsPrimaryActionsReachableAndNamesReadable()
    {
        using var fixture = new MapFixture(expanded: true, textSize: 26);
        using var window = fixture.CreateWindow();
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        window.Show();
        await ReadyAsync(window, 6);
        await WaitUntilAsync(() => window.Session.VisibleNodes.Any(node => node.Entry.Path == fixture.Syllabus));
        window.UpdateLayout();

        foreach (var name in new[] { "HomeButton", "BackButton", "SearchToggle", "SettingsButton", "AddFolderButton" })
        {
            var button = window.FindControl<Button>(name)!;
            Assert.True(button.IsEffectivelyVisible, name);
            Assert.True(button.Bounds.Width >= 44, name);
            Assert.True(button.Bounds.Height >= 44, name);
            var top = button.TranslatePoint(default, window)!.Value;
            Assert.InRange(top.X, 0, window.ClientSize.Width - button.Bounds.Width);
            Assert.InRange(top.Y, 0, window.ClientSize.Height - button.Bounds.Height);
        }

        var file = NodeButton(window, Path.GetFileName(fixture.Syllabus));
        var label = file.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == Path.GetFileName(fixture.Syllabus));
        Assert.Equal(26, label.FontSize);
        Assert.Equal(TextWrapping.Wrap, label.TextWrapping);
        Assert.Contains(Path.GetFileName(fixture.Syllabus), AutomationProperties.GetName(file), StringComparison.Ordinal);
        Assert.InRange(MapButtons(window).Count(), 1, MyFilesSession.MaximumVisibleNodes);
    }

    [AvaloniaFact]
    public async Task Home_CollapsesBranchesAndKeepsConnectedFolders()
    {
        using var fixture = new MapFixture(expanded: true);
        using var window = fixture.CreateWindow();
        window.Show();
        await ReadyAsync(window, 6);
        await WaitUntilAsync(() => window.Session.VisibleNodes.Any(node => node.Entry.Path == fixture.Syllabus));
        Invoke(window.FindControl<Button>("HomeButton")!);
        await WaitUntilAsync(() => window.Session.VisibleNodes.Count == 6);

        Assert.Equal(6, window.Session.VisibleNodes.Count);
        Assert.Empty(window.Session.State.ExpandedFolders);
        Assert.False(window.FindControl<Border>("DetailsPanel")!.IsVisible);
        Assert.True(window.FindControl<Button>("HomeButton")!.IsFocused);
        Assert.True(File.Exists(fixture.Syllabus));
    }

    [AvaloniaTheory]
    [InlineData("my-files-first-run", false, false)]
    [InlineData("my-files-home", true, false)]
    [InlineData("my-files-expanded", true, true)]
    public async Task RenderedMyFilesEvidence(string name, bool configured, bool expanded)
    {
        using var fixture = new MapFixture(expanded);
        if (!configured)
        {
            fixture.Store.Workspace = FileMapWorkspace.Empty;
        }

        using var window = fixture.CreateWindow();
        window.Width = 1400;
        window.Height = 900;
        window.Show();
        await ReadyAsync(window, configured ? 6 : 0);
        if (expanded)
        {
            await WaitUntilAsync(() => window.Session.VisibleNodes.Any(node => node.Entry.Path == fixture.Syllabus));
        }

        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        var output = Environment.GetEnvironmentVariable("OMNIBRILLE_RENDER_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            Assert.NotNull(frame);
            frame.Save(Path.Combine(output, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        Assert.Equal(!configured, window.FindControl<StackPanel>("Welcome")!.IsVisible);
    }

    private static IEnumerable<Button> MapButtons(MyFilesWindow window) =>
        window.FindControl<Canvas>("MapCanvas")!.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("mapNode"));

    private static Button NodeButton(MyFilesWindow window, string name) =>
        Assert.Single(MapButtons(window), button => AutomationProperties.GetName(button)?.StartsWith(name + ",", StringComparison.Ordinal) == true);

    private static void Invoke(Button button) =>
        Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(button).GetProvider<IInvokeProvider>()).Invoke();

    private static Task ReadyAsync(MyFilesWindow window, int rootCount) =>
        WaitUntilAsync(() => !window.Session.IsBusy && window.Session.State.Roots.Count == rootCount &&
            (rootCount > 0 || window.Session.Status.StartsWith("Add Documents", StringComparison.Ordinal)));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The expected My Files UI state was not reached before the test deadline.");
    }

    private sealed class MemoryWorkspaceStore : IFileMapWorkspaceStore
    {
        public FileMapWorkspace Workspace { get; set; } = FileMapWorkspace.Empty;

        public FileMapWorkspace Load() => Workspace;

        public bool TrySave(FileMapWorkspace workspace)
        {
            Workspace = workspace;
            return true;
        }
    }

    private sealed class MemoryPreferencesStore : IVisualPreferencesStore
    {
        private VisualPreferences _preferences = new(Theme: "Light");

        public VisualPreferences Load() => _preferences;

        public void Save(VisualPreferences preferences) => _preferences = preferences;
    }

    private sealed class RecordingFileActivationService : IFileActivationService
    {
        public int OpenCount { get; private set; }
        public string? AccessRoot { get; private set; }
        public string? Path { get; private set; }

        public Task<FileActivationResult> OpenAsync(string accessRoot, string path, CancellationToken cancellationToken = default)
        {
            OpenCount++;
            AccessRoot = accessRoot;
            Path = path;
            return Task.FromResult(FileActivationResult.Opened);
        }
    }

    private sealed class MapFixture : IDisposable
    {
        private static readonly string[] HomeLabels = ["Teaching", "Research", "PivotPath", "Personal", "HOA", "To Sort"];
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "OmniBrille-Map-" + Guid.NewGuid().ToString("N"));

        public MapFixture(bool expanded = false, double textSize = 18)
        {
            var roots = HomeLabels.Select(label => new FileMapRoot(Path.Combine(_folder, label), label)).ToArray();
            foreach (var root in roots)
            {
                Directory.CreateDirectory(root.Path);
            }

            Teaching = roots[0].Path;
            var course = Path.Combine(Teaching, "Database Management");
            Directory.CreateDirectory(course);
            Directory.CreateDirectory(Path.Combine(Teaching, "Introduction to Programming"));
            Syllabus = Path.Combine(course, "Database Management - Syllabus.docx");
            File.WriteAllText(Syllabus, "Synthetic UI fixture. Not an Office document.");
            File.WriteAllText(Path.Combine(course, "Table Joins - Practice.pdf"), "Synthetic UI fixture. Not a PDF document.");
            File.WriteAllText(Path.Combine(course, "Normalization - Exam.docx"), "Synthetic UI fixture. Not an Office document.");
            Store.Workspace = new FileMapWorkspace(roots,
                expanded ? [new FileMapLocation(Teaching, Teaching), new FileMapLocation(Teaching, course)] : [],
                [], textSize);
        }

        public string Teaching { get; }
        public string Syllabus { get; }
        public MemoryWorkspaceStore Store { get; } = new();
        public RecordingFileActivationService Activation { get; } = new();

        public MyFilesWindow CreateWindow() => new(startupTheme: "Light",
            session: new MyFilesSession(Store, Activation), preferencesStore: new MemoryPreferencesStore());

        public void Dispose() => Directory.Delete(_folder, recursive: true);
    }
}

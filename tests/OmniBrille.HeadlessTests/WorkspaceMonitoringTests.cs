using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using OmniBrille.Core;
using OmniBrille.Desktop;
using OmniBrille.Desktop.Presentation;

namespace OmniBrille.HeadlessTests;

public sealed class WorkspaceMonitoringTests
{
    [AvaloniaFact]
    public async Task FilesystemChangesUpdateTheWorkspaceAndStopAtRootRemovalOrWindowClose()
    {
        using var files = new TemporaryFolders();
        var store = new MemoryWorkspaceStore(new SpatialWorkspace(
            [new(files.Documents, "Documents"), new(files.Projects, "Projects")], [],
            CurrentRoot: files.Documents, CurrentPath: files.Documents));
        using var session = new SpatialWorkspaceSession(store);
        using var window = new MainWindow(session, new MemoryPreferencesStore(), monitorChanges: true)
        {
            Width = 1600,
            Height = 1000,
        };
        var changes = 0;
        session.Changed += () => changes++;
        try
        {
            window.Show();
            await window.Initialization;
            await WaitUntilAsync(() => !session.IsBusy && session.CurrentPath == files.Documents,
                "The chosen folder did not finish loading.");
            await WaitForMonitoringAsync(session, Path.Combine(files.Documents, "Monitor ready.txt"));

            var created = Path.Combine(files.Documents, "Created while monitoring.txt");
            await File.WriteAllTextAsync(created, "Created after the workspace was opened.");
            await WaitUntilAsync(() => !session.IsBusy && ContainsPath(session, created),
                "A newly created file did not appear automatically in the selected folder.");

            // Remove a root while its filesystem activity may still be queued. Remaining
            // roots must keep monitoring, and late events must not restore the removed root.
            await File.WriteAllTextAsync(Path.Combine(files.Documents, "Pending before removal.txt"), "pending");
            var roots = window.FindControl<StackPanel>("RootsEntries")!;
            var removeDocuments = Assert.Single(roots.Children.OfType<Grid>().SelectMany(row => row.Children.OfType<Button>()),
                button => AutomationProperties.GetName(button) == "Remove Documents from workspace");
            removeDocuments.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => !session.IsBusy && session.IsOverview && session.State.Roots.Count == 1,
                "Removing a watched folder did not return to the remaining workspace.");

            var removedRootFile = Path.Combine(files.Documents, "Created after removal.txt");
            var remainingRootFile = Path.Combine(files.Projects, "Still monitored.txt");
            await File.WriteAllTextAsync(removedRootFile, "This root is no longer monitored.");
            await WaitForMonitoringAsync(session, Path.Combine(files.Projects, "Monitor ready.txt"));
            await File.WriteAllTextAsync(remainingRootFile, "The remaining root still updates.");
            await WaitUntilAsync(() => !session.IsBusy && ContainsPath(session, remainingRootFile),
                "The remaining root stopped monitoring after another root was removed.");
            Assert.Equal(files.Projects, Assert.Single(session.State.Roots).Path);
            Assert.Equal(files.Projects, Assert.Single(store.Load().Roots).Path);
            Assert.DoesNotContain(session.Scene!.Nodes, node =>
                FileMapWorkspace.IsWithin(files.Documents, node.Path));

            await File.WriteAllTextAsync(Path.Combine(files.Projects, "Pending before close.txt"), "pending");
            window.Close();
            var closedScene = session.Scene;
            var changesAtClose = changes;
            await File.WriteAllTextAsync(Path.Combine(files.Projects, "Created after close.txt"), "closed");
            await File.WriteAllTextAsync(Path.Combine(files.Documents, "Removed and closed.txt"), "closed");

            // Let an already posted change and a refresh interval elapse. Disposal must
            // prevent that work from publishing into a closed window's session.
            await Task.Delay(TimeSpan.FromMilliseconds(1200));
            Assert.Same(closedScene, session.Scene);
            Assert.Equal(changesAtClose, changes);
            Assert.Equal(files.Projects, Assert.Single(session.State.Roots).Path);
        }
        finally
        {
            if (window.IsVisible) { window.Close(); }
        }
    }

    private static bool ContainsPath(SpatialWorkspaceSession session, string path) =>
        session.Scene?.Nodes.Any(node => PathBoundary.Comparer.Equals(node.Path, path)) == true;

    private static async Task WaitForMonitoringAsync(SpatialWorkspaceSession session, string probe)
    {
        // Native subscription is asynchronous. Bound the readiness probe and exercise
        // it through real changes instead of depending on private watcher state.
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!ContainsPath(session, probe) && DateTime.UtcNow < deadline)
        {
            await File.WriteAllTextAsync(probe, "Monitor readiness probe.");
            await Task.Delay(100);
        }
        Assert.True(ContainsPath(session, probe), "The selected folder did not begin monitoring filesystem changes.");
        await WaitUntilAsync(() => !session.IsBusy, "The monitoring readiness refresh did not finish.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string failureMessage)
    {
        // This deadline is shorter than the periodic recovery scan, so success requires
        // live filesystem monitoring rather than a manual refresh or periodic fallback.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline) { await Task.Delay(25); }
        Assert.True(condition(), failureMessage);
    }

    private sealed class MemoryWorkspaceStore(SpatialWorkspace state) : ISpatialWorkspaceStore
    {
        private SpatialWorkspace _saved = state;
        public SpatialWorkspace Load() => _saved;
        public bool TrySave(SpatialWorkspace workspace) { _saved = workspace; return true; }
    }

    private sealed class MemoryPreferencesStore : IVisualPreferencesStore
    {
        private VisualPreferences _saved = new(ReducedMotion: true);
        public VisualPreferences Load() => _saved;
        public void Save(VisualPreferences preferences) => _saved = preferences;
    }

    private sealed class TemporaryFolders : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "SpatialMonitoring-" + Guid.NewGuid().ToString("N"));
        public TemporaryFolders()
        {
            Documents = Directory.CreateDirectory(Path.Combine(_root, "Documents")).FullName;
            Projects = Directory.CreateDirectory(Path.Combine(_root, "Projects")).FullName;
        }
        public string Documents { get; }
        public string Projects { get; }
        public void Dispose()
        {
            if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
        }
    }
}

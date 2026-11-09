using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using OmniBrille.Core;
using OmniBrille.Desktop.Rendering;

namespace OmniBrille.HeadlessTests;

public sealed class WorkspaceRendererTests
{
    [Fact]
    public void IndependentTreesRemainSeparateAndPreviewPreservesExistingPositions()
    {
        var scene = CreateScene();
        var original = WorkspaceGraphLayout.Layout(scene, Roots);
        Assert.Equal(scene.Nodes.Count, original.Count);
        Assert.True(original["documents"].X < original["projects"].X);
        Assert.True(original["projects"].Y < original["pictures"].Y);
        foreach (var root in Roots)
        {
            Assert.True(original[root].Scale > original["courses"].Scale);
            Assert.DoesNotContain(scene.Edges, edge => edge.TargetId == root);
        }

        var extended = WithPreview(scene);
        var preview = WorkspaceGraphLayout.Layout(extended, Roots, original);
        foreach (var pair in original)
        {
            Assert.Equal(pair.Value.X, preview[pair.Key].X);
            Assert.Equal(pair.Value.Y, preview[pair.Key].Y);
        }
        Assert.True(preview.ContainsKey("letter"));
        Assert.Equal(extended.Nodes.Count, preview.Count);
    }

    [Fact]
    public void LayoutIsDeterministicBoundedAndKeepsAllEightRootTrees()
    {
        var roots = Enumerable.Range(0, 8).Select(index => $"root{index}").ToArray();
        var nodes = roots.SelectMany(root => Enumerable.Range(0, 8).Select(index => Folder(index == 0 ? root : $"{root}/{index}"))).ToArray();
        var edges = roots.SelectMany(root => Enumerable.Range(1, 7).Select(index => new ExplorerEdge(root, $"{root}/{index}"))).ToArray();
        var scene = new ExplorerNeighborhood(roots[0], nodes, edges, 56, 0);
        var layout = WorkspaceGraphLayout.Layout(scene, roots);
        var repeated = WorkspaceGraphLayout.Layout(scene with { Nodes = nodes.Reverse().ToArray(), Edges = edges.Reverse().ToArray() }, roots);
        Assert.Equal(64, layout.Count);
        foreach (var pair in layout)
        {
            Assert.Equal(pair.Value, repeated[pair.Key]);
            Assert.InRange(pair.Value.X, -0.48, 0.48);
            Assert.InRange(pair.Value.Y, -0.48, 0.48);
        }
    }

    [AvaloniaFact]
    public void PreviewAddsOnlyRealPathEdgesAndBadgesWithoutSelectionOrZoomChanges()
    {
        var scene = CreateScene();
        var graph = new GraphSceneControl { ReducedMotion = true, ScenePadding = new Thickness(40) };
        var window = new Window { Content = graph, Width = 1280, Height = 800, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        try
        {
            var selectedEvents = 0;
            var activatedEvents = 0;
            graph.NodeSelected += (_, _) => selectedEvents++;
            graph.NodeActivated += (_, _) => activatedEvents++;
            graph.SetWorkspaceScene(scene, Roots, "courses", null, null, null);
            graph.ZoomIn();
            var zoom = graph.Diagnostics.Zoom;
            using (window.CaptureRenderedFrame()) { }
            var graphPeer = ControlAutomationPeer.CreatePeerForElement(graph);
            var before = graphPeer.GetChildren().ToDictionary(peer => peer.GetAutomationId()!, peer => peer.GetBoundingRectangle());
            var expanded = WithPreview(scene);
            graph.SetWorkspaceScene(expanded, Roots, "courses", PathIds,
                new Dictionary<string, int> { ["letters"] = 3, ["projects"] = 2 }, "letter");
            using (window.CaptureRenderedFrame()) { }
            Assert.Equal(3, graph.Diagnostics.PreviewEdges);
            Assert.Equal(2, graph.Diagnostics.MatchBadges);
            Assert.Equal(0, graph.Diagnostics.PreviewParticles);
            Assert.Equal(zoom, graph.Diagnostics.Zoom);
            Assert.Equal(0, selectedEvents);
            Assert.Equal(0, activatedEvents);
            var peers = graphPeer.GetChildren();
            Assert.All(peers, peer =>
            {
                var bounds = peer.GetBoundingRectangle();
                Assert.True(bounds.Width >= 44 && bounds.Height >= 44);
                if (before.TryGetValue(peer.GetAutomationId()!, out var previous))
                {
                    Assert.Equal(previous.Center, bounds.Center);
                }
            });
            Assert.Equal(3, peers.Count(peer => peer.GetName()!.Contains("workspace folder", StringComparison.Ordinal)));
            Assert.Contains(peers, peer => peer.GetItemStatus()!.Contains("Search result preview", StringComparison.Ordinal));
            Assert.Contains(peers, peer => peer.GetItemStatus()!.Contains("2 search matches inside", StringComparison.Ordinal));

            graph.SetWorkspaceScene(scene, Roots, "courses", null, null, null);
            using (window.CaptureRenderedFrame()) { }
            Assert.Equal(0, graph.Diagnostics.PreviewEdges);
            Assert.Equal(0, graph.Diagnostics.MatchBadges);
            Assert.Equal(zoom, graph.Diagnostics.Zoom);
            graph.ResetView();
            Assert.Equal(1, graph.Diagnostics.Zoom);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviewDustStopsWhenInactiveAndCachesStayBounded(bool light)
    {
        var graph = new GraphSceneControl { ScenePadding = new Thickness(40) };
        var window = new Window
        {
            Content = graph,
            Width = 1280,
            Height = 800,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        window.Show();
        try
        {
            graph.SetWorkspaceScene(WithPreview(CreateScene()), Roots, null, PathIds,
                new Dictionary<string, int> { ["letters"] = 3 }, "letter");
            using (window.CaptureRenderedFrame()) { }
            Assert.Equal(3, graph.Diagnostics.PreviewEdges);
            Assert.Equal(9, graph.Diagnostics.PreviewParticles);
            for (var index = 0; index < 24; index++) { using (window.CaptureRenderedFrame()) { } }
            Assert.InRange(graph.Diagnostics.TextCacheEntries, 0, 256);
            Assert.InRange(graph.Diagnostics.ResourceCacheEntries, 0, 576);
            graph.SetMotionActivity(false);
            using (window.CaptureRenderedFrame()) { }
            Assert.Equal(0, graph.Diagnostics.PreviewParticles);
            Assert.Equal(3, graph.Diagnostics.PreviewEdges);
            Assert.False(graph.Diagnostics.MotionActive);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PreviewFromAnotherRootKeepsCurrentTreeAnchoredAndRestoresIt()
    {
        var graph = new GraphSceneControl { ReducedMotion = true, ScenePadding = new Thickness(40) };
        var window = new Window { Content = graph, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            var nodes = new[] { Folder("documents"), Folder("teaching"), Folder("letters") };
            ExplorerEdge[] edges = [new("documents", "teaching"), new("teaching", "letters")];
            var scene = new ExplorerNeighborhood("documents", nodes, edges, 2, 0);
            graph.SetWorkspaceScene(scene, ["documents"], null, null, null, null);
            using (window.CaptureRenderedFrame()) { }
            var peer = ControlAutomationPeer.CreatePeerForElement(graph);
            var before = peer.GetChildren().ToDictionary(node => node.GetAutomationId()!, node => node.GetBoundingRectangle().Center);
            foreach (var otherRoot in new[] { "projects", "pictures" })
            {
                var overlay = scene with
                {
                    Nodes = [.. nodes, Folder(otherRoot), Folder(otherRoot + "/match")],
                    Edges = [.. edges, new(otherRoot, otherRoot + "/match")],
                };
                graph.SetWorkspaceScene(overlay, ["documents", otherRoot], null,
                    new HashSet<string>([otherRoot, otherRoot + "/match"], ExplorerIdentity.Comparer), null, otherRoot + "/match");
                using (window.CaptureRenderedFrame()) { }
                foreach (var node in peer.GetChildren())
                {
                    if (before.TryGetValue(node.GetAutomationId()!, out var center)) { Assert.Equal(center, node.GetBoundingRectangle().Center); }
                }
                Assert.Equal(1, graph.Diagnostics.PreviewEdges);
            }
            graph.SetWorkspaceScene(scene, ["documents"], null, null, null, null);
            using (window.CaptureRenderedFrame()) { }
            Assert.All(peer.GetChildren(), node => Assert.Equal(before[node.GetAutomationId()!], node.GetBoundingRectangle().Center));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RenderWorkspaceEvidence(bool preview)
    {
        var graph = new GraphSceneControl { ReducedMotion = true, ScenePadding = new Thickness(40, 70, 40, 60) };
        var window = new Window { Content = graph, Width = 1280, Height = 800, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        try
        {
            var scene = CreateScene();
            graph.SetWorkspaceScene(preview ? WithPreview(scene) : scene, Roots, null,
                preview ? PathIds : null,
                preview ? new Dictionary<string, int> { ["letters"] = 3, ["research"] = 1, ["projects"] = 2 } : null,
                preview ? "letter" : null);
            using var frame = window.CaptureRenderedFrame();
            Assert.Equal(graph.Diagnostics.Nodes, graph.Diagnostics.Labels);
            var output = Environment.GetEnvironmentVariable("OMNIBRILLE_RENDER_EVIDENCE");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                Assert.NotNull(frame);
                frame.Save(Path.Combine(output, preview ? "workspace-preview-graph.png" : "workspace-overview-graph.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public void CompactEightRootOverviewKeepsSeparateNamesAndUsableTargets(double textScale)
    {
        var roots = new[] { "Documents", "Projects", "Pictures", "Teaching", "Research", "Personal", "Business", "Archive" };
        var scene = new ExplorerNeighborhood(roots[0], roots.Select(Folder).ToArray(), [], 0, 0);
        var graph = new GraphSceneControl { ReducedMotion = true, TextScale = textScale };
        var window = new Window { Content = graph, Width = 500, Height = 270, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        try
        {
            graph.SetWorkspaceScene(scene, roots, null, null, null, null);
            using var frame = window.CaptureRenderedFrame();
            Assert.Equal(8, graph.Diagnostics.Labels);
            Assert.Equal(0, graph.Diagnostics.OverlappingLabelPairs);
            var peers = ControlAutomationPeer.CreatePeerForElement(graph).GetChildren();
            Assert.All(peers, peer =>
            {
                var target = peer.GetBoundingRectangle();
                Assert.True(target.Width >= 44 && target.Height >= 44);
                Assert.Contains("workspace folder", peer.GetName(), StringComparison.Ordinal);
            });
            var output = Environment.GetEnvironmentVariable("OMNIBRILLE_RENDER_EVIDENCE");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                Assert.NotNull(frame);
                frame.Save(Path.Combine(output, $"workspace-eight-roots-{textScale}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    private static readonly string[] Roots = ["documents", "projects", "pictures"];
    private static readonly HashSet<string> PathIds = new(["documents", "teaching", "letters", "letter"], ExplorerIdentity.Comparer);

    private static ExplorerNeighborhood CreateScene()
    {
        var names = new[] { "documents", "teaching", "research", "personal", "archive", "resources", "letters", "courses",
            "papers", "data", "projects", "website", "software", "applications", "planning", "pictures", "travel", "photography", "family" };
        ExplorerEdge[] edges = [new("documents", "teaching"), new("documents", "research"), new("documents", "personal"),
            new("documents", "archive"), new("documents", "resources"), new("teaching", "letters"), new("teaching", "courses"),
            new("research", "papers"), new("research", "data"), new("projects", "website"), new("projects", "software"),
            new("projects", "applications"), new("projects", "planning"), new("pictures", "travel"),
            new("pictures", "photography"), new("pictures", "family")];
        return new("documents", names.Select(Folder).ToArray(), edges, 16, 0);
    }

    private static ExplorerNeighborhood WithPreview(ExplorerNeighborhood scene) => scene with
    {
        Nodes = [.. scene.Nodes, new ExplorerNode("letter", "Faculty LOR.docx", "/Documents/Teaching/Letters/Faculty LOR.docx",
            ExplorerNodeKind.File, 2400, null, false, Roles: ExplorerNodeRole.Structural)],
        Edges = [.. scene.Edges, new("letters", "letter")],
    };

    private static ExplorerNode Folder(string id) => new(id, char.ToUpperInvariant(id[0]) + id[1..], "/" + id,
        ExplorerNodeKind.Folder, null, null, true, Roles: ExplorerNodeRole.Structural);

    [AvaloniaFact]
    public void CompactLargeLabelsStaySeparateAcrossEveryFolderIdentityOrdering()
    {
        var graph = new GraphSceneControl { ReducedMotion = true, TextScale = 2, ScenePadding = new Thickness(304, 196, 36, 96) };
        var window = new Window { Content = graph, Width = 820, Height = 520, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        try
        {
            for (var seed = 0; seed < 24; seed++)
            {
                var root = $"scope-{seed}/Documents";
                var nodes = new List<ExplorerNode> { Folder(root) with { Name = "Documents" } };
                var edges = new List<ExplorerEdge>();
                var available = new List<int> { 0, 1, 2, 3 };
                var order = seed;
                foreach (var name in new[] { "Archive", "Presentations", "Research", "Teaching" })
                {
                    var slot = order % available.Count;
                    order /= available.Count;
                    var id = $"scope-{seed}/node-{available[slot]}";
                    available.RemoveAt(slot);
                    nodes.Add(Folder(id) with { Name = name });
                    edges.Add(new(root, id));
                }
                var next = root + "|page:4";
                nodes.Add(new(next, "Next items", root, ExplorerNodeKind.Aggregate, null, null, true));
                edges.Add(new(root, next));
                graph.SetWorkspaceScene(new(root, nodes, edges, 48, 44), [root], null, null, null, null);
                using (window.CaptureRenderedFrame()) { }
                Assert.True(graph.Diagnostics.OverlappingLabelPairs == 0, $"Identity ordering {seed} produced overlapping labels.");
                Assert.Equal(6, graph.Diagnostics.Labels);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SameNativeFolderKeepsItsAppearanceAcrossWorkspaceAuthorities()
    {
        var graph = new GraphSceneControl { ReducedMotion = true, ScenePadding = new Thickness(40) };
        var window = new Window { Content = graph, Width = 800, Height = 600, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        try
        {
            (byte[]? Pixels, string? Name, Rect Bounds) Capture(string id, string path)
            {
                var node = Folder(id) with { Name = "Documents", Path = path };
                graph.SetWorkspaceScene(new(id, [node], [], 0, 0), [id], null, null, null, null);
                using var frame = window.CaptureRenderedFrame();
                Assert.Equal(1, graph.Diagnostics.Nodes);
                Assert.Equal(1, graph.Diagnostics.Labels);
                Assert.Equal(0, graph.Diagnostics.OverlappingLabelPairs);
                var peer = Assert.Single(ControlAutomationPeer.CreatePeerForElement(graph).GetChildren());
                var bounds = peer.GetBoundingRectangle();
                Assert.True(bounds.Width >= 44 && bounds.Height >= 44);
                // The default headless drawing backend has no pixel surface.
                // Keep its state checks, while requiring pixels in Skia evidence runs.
                if (frame is null)
                {
                    Assert.True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OMNIBRILLE_RENDER_EVIDENCE")));
                    return (null, peer.GetName(), bounds);
                }
                using var stream = new MemoryStream();
                frame.Save(stream, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                return (stream.ToArray(), peer.GetName(), bounds);
            }
            var path = Path.Combine(Path.GetTempPath(), "SpatialAppearance", "Documents");
            var parent = Capture("parent-authority/documents", path);
            var pinned = Capture("pinned-authority/documents", path + Path.DirectorySeparatorChar);
            Assert.Equal(parent.Name, pinned.Name);
            Assert.Equal(parent.Bounds, pinned.Bounds);
            if (parent.Pixels is not null || pinned.Pixels is not null)
            {
                Assert.NotNull(parent.Pixels);
                Assert.NotNull(pinned.Pixels);
                Assert.Equal(parent.Pixels, pinned.Pixels);
            }
        }
        finally { window.Close(); }
    }
}

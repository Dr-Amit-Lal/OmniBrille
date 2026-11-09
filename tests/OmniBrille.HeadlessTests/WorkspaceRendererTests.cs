using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ReducedMotionKeepsGlyphsLabelsAndConnectionsStillAndOnlyMarksOneFolder(bool light, bool reducedEffects)
    {
        var scene = CreateScene();
        var graph = new GraphSceneControl { ScenePadding = new Thickness(40), ReducedEffects = reducedEffects, ReducedMotion = true };
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
            var selectedEvents = 0;
            var activatedEvents = 0;
            graph.NodeSelected += (_, _) => selectedEvents++;
            graph.NodeActivated += (_, _) => activatedEvents++;
            var counts = new Dictionary<string, int> { ["letters"] = 3, ["projects"] = 2 };
            graph.SetWorkspaceScene(scene, Roots, "courses", counts, null);
            using (window.CaptureRenderedFrame()) { }
            var peer = ControlAutomationPeer.CreatePeerForElement(graph);
            var before = peer.GetChildren().ToDictionary(node => node.GetAutomationId()!, node => node.GetBoundingRectangle());
            var segments = graph.Diagnostics.ConnectionSegments;
            var zoom = graph.Diagnostics.Zoom;
            var target = peer.GetChildren().Single(node => node.GetName()!.StartsWith("Letters", StringComparison.Ordinal));
            var point = target.GetBoundingRectangle().Center;
            window.MouseMove(window.PointToClient(new PixelPoint((int)point.X, (int)point.Y)));
            await Task.Delay(220);
            using (window.CaptureRenderedFrame()) { }
            Assert.Equal(1, graph.Diagnostics.HoverHighlights);
            Assert.Equal(segments, graph.Diagnostics.ConnectionSegments);
            Assert.All(peer.GetChildren(), node => Assert.Equal(before[node.GetAutomationId()!], node.GetBoundingRectangle()));
            window.MouseMove(new Point(2, 2));
            graph.SetWorkspaceScene(scene, Roots, "courses", counts, "letters");
            using (window.CaptureRenderedFrame()) { }
            Assert.Equal(1, graph.Diagnostics.HoverHighlights);
            Assert.Equal(2, graph.Diagnostics.MatchBadges);
            Assert.Equal(segments, graph.Diagnostics.ConnectionSegments);
            Assert.Equal(zoom, graph.Diagnostics.Zoom);
            Assert.Equal(0, selectedEvents);
            Assert.Equal(0, activatedEvents);
            Assert.All(peer.GetChildren(), node => Assert.Equal(before[node.GetAutomationId()!], node.GetBoundingRectangle()));
            Assert.Contains(peer.GetChildren(), node => node.GetItemStatus()!.Contains("Search result location", StringComparison.Ordinal));
            Assert.DoesNotContain(peer.GetChildren(), node => node.GetItemStatus()!.Contains("Search match", StringComparison.Ordinal));
            for (var index = 0; index < 24; index++) { using (window.CaptureRenderedFrame()) { } }
            Assert.InRange(graph.Diagnostics.TextCacheEntries, 0, 256);
            Assert.InRange(graph.Diagnostics.ResourceCacheEntries, 0, 576);
            graph.SetMotionActivity(false);
            using (window.CaptureRenderedFrame()) { }
            Assert.False(graph.Diagnostics.MotionActive);
            Assert.Equal(1, graph.Diagnostics.HoverHighlights);
            graph.SetWorkspaceScene(scene, Roots, "courses", counts, null);
            using (window.CaptureRenderedFrame()) { }
            Assert.Equal(0, graph.Diagnostics.HoverHighlights);
            Assert.Equal(segments, graph.Diagnostics.ConnectionSegments);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullMotionRestoresHoverMagnificationWithoutAddingBranchesAndStopsWhenDisabled(bool light)
    {
        var scene = CreateScene();
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
            graph.SetWorkspaceScene(scene, Roots, null, null, null, animate: false);
            using (window.CaptureRenderedFrame()) { }
            var peer = ControlAutomationPeer.CreatePeerForElement(graph);
            var identities = peer.GetChildren().Select(node => node.GetAutomationId()).ToArray();
            var zoom = graph.Diagnostics.Zoom;
            Assert.True(graph.Diagnostics.MotionActive);
            Assert.Equal(1, graph.Diagnostics.MaximumHoverScale);
            var target = peer.GetChildren().Single(node => node.GetName()!.StartsWith("Letters", StringComparison.Ordinal));
            var point = target.GetBoundingRectangle().Center;
            window.MouseMove(window.PointToClient(new PixelPoint((int)point.X, (int)point.Y)));
            await Task.Delay(240);
            using (var frame = window.CaptureRenderedFrame())
            {
                var output = Environment.GetEnvironmentVariable("OMNIBRILLE_RENDER_EVIDENCE");
                if (!string.IsNullOrEmpty(output))
                {
                    Directory.CreateDirectory(output);
                    Assert.NotNull(frame);
                    frame.Save(Path.Combine(output, light ? "effects-hover-light.png" : "effects-hover-dark.png"),
                        Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
            }
            Assert.InRange(graph.Diagnostics.MaximumHoverScale, 1.01, GraphMotionPolicy.MaximumLensScale + 0.000001);
            Assert.Equal(1, graph.Diagnostics.HoverHighlights);
            // A directory refresh is a new scene instance. Animated snapshots
            // must not reapply the already active hover lens a second time.
            graph.SetWorkspaceScene(scene with { Nodes = scene.Nodes.ToArray() }, Roots, null, null, null);
            for (var frame = 0; frame < 6; frame++)
            {
                await Task.Delay(50);
                using (window.CaptureRenderedFrame()) { }
                Assert.InRange(graph.Diagnostics.MaximumHoverScale, 1.01, GraphMotionPolicy.MaximumLensScale + 0.000001);
                Assert.Equal(scene.Nodes.Count, graph.Diagnostics.Nodes);
                Assert.Equal(scene.Edges.Count, graph.Diagnostics.Edges);
                Assert.Equal(scene.Nodes.Count, graph.Diagnostics.Labels);
                Assert.Equal(0, graph.Diagnostics.OverlappingLabelPairs);
                Assert.Equal(identities, peer.GetChildren().Select(node => node.GetAutomationId()).ToArray());
                Assert.All(peer.GetChildren(), node =>
                {
                    var bounds = node.GetBoundingRectangle();
                    Assert.True(bounds.Width >= 44 && bounds.Height >= 44);
                });
            }
            Assert.Equal(zoom, graph.Diagnostics.Zoom);
            graph.ReducedMotion = true;
            using (window.CaptureRenderedFrame()) { }
            Assert.False(graph.Diagnostics.MotionActive);
            Assert.Equal(1, graph.Diagnostics.MaximumHoverScale);
            var frozen = peer.GetChildren().ToDictionary(node => node.GetAutomationId()!, node => node.GetBoundingRectangle());
            await Task.Delay(100);
            using (window.CaptureRenderedFrame()) { }
            Assert.All(peer.GetChildren(), node => Assert.Equal(frozen[node.GetAutomationId()!], node.GetBoundingRectangle()));
            graph.ReducedMotion = false;
            window.MouseMove(new Point(2, 2));
            await Task.Delay(220);
            using (window.CaptureRenderedFrame()) { }
            Assert.True(graph.Diagnostics.MotionActive);
            Assert.Equal(0, graph.Diagnostics.HoverHighlights);
            Assert.Equal(1, graph.Diagnostics.MaximumHoverScale);
            graph.SetMotionActivity(false);
            using (window.CaptureRenderedFrame()) { }
            Assert.False(graph.Diagnostics.MotionActive);
            Assert.Equal(1, graph.Diagnostics.MaximumHoverScale);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DenseFullMotionHoverAndRefreshKeepEveryLabelAndTargetUsable(bool light)
    {
        var children = Enumerable.Range(1, 24).Select(index => $"Folder {index:D2}").ToArray();
        var scene = new ExplorerNeighborhood("Documents", children.Prepend("Documents").Select(Folder).ToArray(),
            children.Select(id => new ExplorerEdge("Documents", id)).ToArray(), children.Length, 0);
        var graph = new GraphSceneControl { ScenePadding = new Thickness(64, 80, 64, 64) };
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
            graph.SetWorkspaceScene(scene, ["Documents"], null, null, null, animate: false);
            using (window.CaptureRenderedFrame()) { }
            var peer = ControlAutomationPeer.CreatePeerForElement(graph);
            var identities = peer.GetChildren().Select(node => node.GetAutomationId()).ToArray();
            var target = peer.GetChildren().Single(node => node.GetName()!.StartsWith("Folder 12", StringComparison.Ordinal));
            var point = target.GetBoundingRectangle().Center;
            window.MouseMove(window.PointToClient(new PixelPoint((int)point.X, (int)point.Y)));
            await Task.Delay(240);
            graph.SetWorkspaceScene(scene with { Nodes = scene.Nodes.ToArray() }, ["Documents"], null, null, null);
            for (var index = 0; index < 5; index++)
            {
                await Task.Delay(90);
                using var frame = window.CaptureRenderedFrame();
                Assert.True(graph.Diagnostics.MotionActive);
                Assert.Equal(1, graph.Diagnostics.HoverHighlights);
                Assert.InRange(graph.Diagnostics.MaximumHoverScale, 1.01, GraphMotionPolicy.MaximumLensScale + 0.000001);
                Assert.Equal(scene.Nodes.Count, graph.Diagnostics.Nodes);
                Assert.Equal(scene.Edges.Count, graph.Diagnostics.Edges);
                Assert.Equal(scene.Nodes.Count, graph.Diagnostics.Labels);
                Assert.Equal(0, graph.Diagnostics.OverlappingLabelPairs);
                Assert.Equal(identities, peer.GetChildren().Select(node => node.GetAutomationId()).ToArray());
                Assert.All(peer.GetChildren(), node =>
                {
                    var bounds = node.GetBoundingRectangle();
                    Assert.True(bounds.Width >= 44 && bounds.Height >= 44);
                });
                var output = Environment.GetEnvironmentVariable("OMNIBRILLE_RENDER_EVIDENCE");
                if (index == 4 && !string.IsNullOrEmpty(output))
                {
                    Directory.CreateDirectory(output);
                    Assert.NotNull(frame);
                    frame.Save(Path.Combine(output, light ? "effects-dense-light.png" : "effects-dense-dark.png"),
                        Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
            }
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
            graph.SetWorkspaceScene(scene, Roots, null,
                preview ? new Dictionary<string, int> { ["letters"] = 3, ["research"] = 1, ["projects"] = 2 } : null,
                preview ? "letters" : null);
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
    [InlineData(false)]
    [InlineData(true)]
    public void RenderCalmSingleFolderWithEveryItemAndLocalHover(bool hover)
    {
        string[] names = ["Adobe", "Amit Lal Workspace", "Archive", "Camtasia", "ChatGPT", "Custom Office Templates",
            "Fall 2026", "GitHub", "HOA", "OneNote Notebooks", "Organization Records", "Personal", "Pivot Path Website",
            "PivotPath", "PowerShell", "PowerToys", "Research", "Snagit", "Software Projects", "Teaching", "To Sort", "Visual Studio 2022", "Zoom"];
        var scene = new ExplorerNeighborhood("Documents", names.Prepend("Documents").Select(Folder).ToArray(),
            names.Select(name => new ExplorerEdge("Documents", name)).ToArray(), names.Length, 0);
        var graph = new GraphSceneControl { ReducedMotion = true, ScenePadding = new Thickness(50) };
        var window = new Window { Content = graph, Width = 1400, Height = 900, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        try
        {
            graph.SetWorkspaceScene(scene, ["Documents"], null, null, hover ? "HOA" : null);
            using var frame = window.CaptureRenderedFrame();
            Assert.Equal(24, graph.Diagnostics.Nodes);
            Assert.Equal(24, graph.Diagnostics.Labels);
            Assert.Equal(0, graph.Diagnostics.OverlappingLabelPairs);
            Assert.Equal(hover ? 1 : 0, graph.Diagnostics.HoverHighlights);
            var output = Environment.GetEnvironmentVariable("OMNIBRILLE_RENDER_EVIDENCE");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                Assert.NotNull(frame);
                frame.Save(Path.Combine(output, hover ? "calm-folder-hover.png" : "calm-folder-rest.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
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
            graph.SetWorkspaceScene(scene, roots, null, null, null);
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
                graph.SetWorkspaceScene(new(root, nodes, edges, 48, 44), [root], null, null, null);
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
                graph.SetWorkspaceScene(new(id, [node], [], 0, 0), [id], null, null, null);
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

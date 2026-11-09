namespace OmniBrille.Core;

/// <summary>Places independent, real folder trees without inventing a workspace parent.</summary>
public static class WorkspaceGraphLayout
{
    public const int MaximumRoots = 8;
    public const int MaximumNodes = 64;

    public static IReadOnlyDictionary<string, GraphLayoutNode> Layout(
        ExplorerNeighborhood scene,
        IReadOnlyList<string> clusterRootIds,
        IReadOnlyDictionary<string, GraphLayoutNode>? previousLayout = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(clusterRootIds);
        if (scene.Nodes.Count > MaximumNodes || clusterRootIds.Count > MaximumRoots)
        {
            throw new ArgumentException("Workspace scenes must contain at most eight roots and 64 nodes.");
        }

        var nodes = scene.Nodes.ToDictionary(node => node.Id, ExplorerIdentity.Comparer);
        var roots = clusterRootIds.Where(nodes.ContainsKey).Distinct(ExplorerIdentity.Comparer).ToArray();
        var rootSet = roots.ToHashSet(ExplorerIdentity.Comparer);
        var children = scene.Edges.Where(edge => edge.Kind == ExplorerGraphEdgeKind.Structural &&
                nodes.ContainsKey(edge.SourceId) && nodes.ContainsKey(edge.TargetId) && !rootSet.Contains(edge.TargetId))
            .GroupBy(edge => edge.SourceId, ExplorerIdentity.Comparer)
            .ToDictionary(group => group.Key, group => group.Select(edge => nodes[edge.TargetId])
                .DistinctBy(node => node.Id, ExplorerIdentity.Comparer)
                .OrderBy(node => node.Kind == ExplorerNodeKind.Folder ? 0 : 1)
                .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(node => node.Id, ExplorerIdentity.Comparer).ToArray(), ExplorerIdentity.Comparer);
        var result = new Dictionary<string, GraphLayoutNode>(ExplorerIdentity.Comparer);
        for (var index = 0; index < roots.Length; index++)
        {
            var root = roots[index];
            var cell = CellFor(index, roots.Length);
            if (previousLayout is not null && previousLayout.TryGetValue(root, out var rootAnchor)) { cell = cell with { X = rootAnchor.X, Y = rootAnchor.Y }; }
            else if (previousLayout is { Count: > 0 }) { cell = PreviewCell(previousLayout.Values); }
            var rootScale = roots.Length <= 3 ? 1.12 : 1.0;
            result[root] = new(root, cell.X, cell.Y, rootScale, 1, 0, 0);
            var queue = new Queue<(string Id, int Depth, double Angle)>();
            queue.Enqueue((root, 0, -Math.PI / 2));
            while (queue.TryDequeue(out var parent))
            {
                if (!children.TryGetValue(parent.Id, out var descendants)) { continue; }
                var parentPosition = result[parent.Id];
                for (var childIndex = 0; childIndex < descendants.Length; childIndex++)
                {
                    var child = descendants[childIndex];
                    if (result.ContainsKey(child.Id) || rootSet.Contains(child.Id)) { continue; }
                    var depth = parent.Depth + 1;
                    var angle = depth == 1
                        ? -Math.PI * 0.75 + Math.PI * 2 * childIndex / Math.Max(1, descendants.Length)
                        : parent.Angle + (childIndex - (descendants.Length - 1) / 2d) * Math.Min(0.8, 1.5 / Math.Max(1, descendants.Length - 1));
                    var radiusX = cell.Width * (depth == 1 ? 0.32 : 0.19 / Math.Sqrt(depth - 1));
                    var radiusY = cell.Height * (depth == 1 ? 0.28 : 0.18 / Math.Sqrt(depth - 1));
                    var x = Math.Clamp(parentPosition.X + Math.Cos(angle) * radiusX, cell.Left + 0.025, cell.Right - 0.025);
                    var y = Math.Clamp(parentPosition.Y + Math.Sin(angle) * radiusY, cell.Top + 0.025, cell.Bottom - 0.045);
                    if (previousLayout?.TryGetValue(child.Id, out var previous) == true)
                    {
                        // Adding a hover preview must not move the already visible tree.
                        x = previous.X;
                        y = previous.Y;
                    }
                    else
                    {
                        (x, y) = FindClearPosition(x, y, cell, result.Values, childIndex);
                    }
                    result[child.Id] = new(child.Id, x, y,
                        depth == 1 ? roots.Length <= 3 ? 1.02 : 0.85 : 0.78, 1, depth, Math.Min(3, depth));
                    queue.Enqueue((child.Id, depth, angle));
                }
            }
        }

        // Malformed/disconnected input stays visible and edge-free; no parent is inferred.
        var unassigned = nodes.Values.Where(node => !result.ContainsKey(node.Id))
            .OrderBy(node => node.Id, ExplorerIdentity.Comparer).ToArray();
        for (var index = 0; index < unassigned.Length; index++)
        {
            var node = unassigned[index];
            result[node.Id] = new(node.Id, -0.42 + index % 8 * 0.12, 0.42 - index / 8 * 0.12, 0.8, 1, 1, 1);
        }
        return result;
    }

    private static (double X, double Y) FindClearPosition(
        double x, double y, ClusterCell cell, IEnumerable<GraphLayoutNode> placed, int seed)
    {
        var positions = placed.ToArray();
        var best = (X: x, Y: y);
        var bestDistance = Clearance(x, y);
        if (bestDistance >= 1) { return best; }
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var angle = (attempt + seed * 3) * 2.399963229728653;
            var distance = 0.018 * Math.Sqrt(attempt + 1);
            var candidateX = Math.Clamp(x + Math.Cos(angle) * distance, cell.Left + 0.025, cell.Right - 0.025);
            var candidateY = Math.Clamp(y + Math.Sin(angle) * distance, cell.Top + 0.025, cell.Bottom - 0.045);
            var clearance = Clearance(candidateX, candidateY);
            if (clearance <= bestDistance) { continue; }
            best = (candidateX, candidateY);
            bestDistance = clearance;
            if (clearance >= 1) { break; }
        }
        return best;

        double Clearance(double candidateX, double candidateY) => positions.Length == 0 ? 2 : positions.Min(item =>
            Math.Pow((item.X - candidateX) / 0.075, 2) + Math.Pow((item.Y - candidateY) / 0.095, 2));
    }

    private static ClusterCell PreviewCell(IEnumerable<GraphLayoutNode> previous)
    {
        var positions = previous.ToArray();
        ClusterCell[] corners = [new(0.34, -0.30, 0.28, 0.34), new(-0.34, -0.30, 0.28, 0.34),
            new(0.34, 0.30, 0.28, 0.34), new(-0.34, 0.30, 0.28, 0.34)];
        return corners.OrderByDescending(cell => positions.Min(node =>
            Math.Pow(cell.X - node.X, 2) + Math.Pow(cell.Y - node.Y, 2))).First();
    }

    private static ClusterCell CellFor(int index, int count)
    {
        if (count == 1) { return new(0, 0.02, 0.96, 0.90); }
        if (count == 2) { return new(index == 0 ? -0.25 : 0.25, 0, 0.46, 0.88); }
        if (count == 3)
        {
            return index == 0 ? new(-0.20, 0.04, 0.55, 0.90)
                : new(0.30, index == 1 ? -0.24 : 0.26, 0.35, 0.40);
        }
        var columns = count <= 4 ? 2 : count <= 6 ? 3 : 4;
        var width = 0.96 / columns;
        return new(-0.48 + width * (index % columns + 0.5), index < columns ? -0.24 : 0.24, width - 0.025, 0.42);
    }

    private readonly record struct ClusterCell(double X, double Y, double Width, double Height)
    {
        public double Left => X - Width / 2;
        public double Right => X + Width / 2;
        public double Top => Y - Height / 2;
        public double Bottom => Y + Height / 2;
    }
}

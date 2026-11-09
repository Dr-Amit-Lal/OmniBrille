namespace OmniBrille.Core;

public static class ExplorerIdentity
{
    public static StringComparer Comparer { get; } = StringComparer.Ordinal;

    public static bool Equals(string? left, string? right) => Comparer.Equals(left, right);
}

public enum ExplorerNodeKind
{
    Folder,
    File,
    Aggregate,
    Context,
}

[Flags]
public enum ExplorerNodeRole
{
    None = 0,
    Structural = 1,
    DescendantPreview = 8,
}

/// <summary>
/// The user-facing relationship of a node to the current scene focus. This is semantic
/// navigation state and must never be inferred from a renderer density band.
/// </summary>
public enum ExplorerSceneRelation
{
    CurrentFocus,
    DirectChild,
    PreviousFocus,
    Aggregate,
    Visible,
    DescendantPreview,
}

public static class ExplorerSceneSemantics
{
    public static ExplorerSceneRelation RelationOf(ExplorerNeighborhood neighborhood, ExplorerNode node)
    {
        ArgumentNullException.ThrowIfNull(neighborhood);
        ArgumentNullException.ThrowIfNull(node);

        if (ExplorerIdentity.Equals(node.Id, neighborhood.FocusNodeId))
        {
            return ExplorerSceneRelation.CurrentFocus;
        }

        if (node.Kind == ExplorerNodeKind.Context)
        {
            return ExplorerSceneRelation.PreviousFocus;
        }

        if (node.Kind == ExplorerNodeKind.Aggregate)
        {
            return ExplorerSceneRelation.Aggregate;
        }

        if ((node.Roles & ExplorerNodeRole.DescendantPreview) != 0)
        {
            return ExplorerSceneRelation.DescendantPreview;
        }

        return (node.Roles & ExplorerNodeRole.Structural) != 0
            ? ExplorerSceneRelation.DirectChild
            : ExplorerSceneRelation.Visible;
    }

    public static string Describe(ExplorerNeighborhood neighborhood, ExplorerNode node) =>
        RelationOf(neighborhood, node) switch
        {
            ExplorerSceneRelation.CurrentFocus => "current focus",
            ExplorerSceneRelation.DirectChild => $"direct child of {neighborhood.Focus.Name}",
            ExplorerSceneRelation.PreviousFocus => "previous focus in navigation history",
            ExplorerSceneRelation.Aggregate => "bounded overflow portal",
            ExplorerSceneRelation.DescendantPreview => DescribePreview(neighborhood, node),
            _ => "visible graph item",
        };

    private static string DescribePreview(ExplorerNeighborhood neighborhood, ExplorerNode node)
    {
        var parentId = neighborhood.Edges.FirstOrDefault(edge =>
            edge.Kind == ExplorerGraphEdgeKind.Structural && ExplorerIdentity.Equals(edge.TargetId, node.Id))?.SourceId;
        var parent = neighborhood.Nodes.FirstOrDefault(candidate => ExplorerIdentity.Equals(candidate.Id, parentId));
        return parent is null
            ? "subfolder preview; two levels below current focus"
            : $"subfolder of {parent.Name}; two levels below current focus";
    }
}

public enum ExplorerGraphEdgeKind
{
    Structural,
}

public enum ExplorerFailureKind
{
    None,
    NotFound,
    AccessDenied,
    EnumerationFailed,
}

public enum AggregateActionKind
{
    OpenPage,
    PreviousPage,
    NextPage,
    Overview,
}

public sealed record AggregateAction(
    AggregateActionKind Kind,
    int? TargetOffset = null,
    string? Description = null);

public sealed record AggregatePage(int Offset, int PageSize);

public sealed record ExplorerEntry(
    string Id,
    string Name,
    string Path,
    ExplorerNodeKind Kind,
    long? SizeBytes = null,
    DateTimeOffset? LastModified = null,
    bool IsReparsePoint = false,
    bool IsNavigable = true,
    string? NavigationTarget = null,
    string? ParentNavigationTarget = null)
{
    public string Target => NavigationTarget ?? Path;
}

public sealed record ExplorerDirectoryPreview(string ParentNodeId, IReadOnlyList<ExplorerEntry> Children);

public sealed record ExplorerDirectorySnapshot(
    ExplorerEntry Focus,
    IReadOnlyList<ExplorerEntry> Children,
    ExplorerFailureKind Failure = ExplorerFailureKind.None,
    string? Warning = null,
    int? TotalChildCount = null,
    bool WasTruncated = false,
    IReadOnlyList<ExplorerDirectoryPreview>? Previews = null);

public sealed record ExplorerNode(
    string Id,
    string Name,
    string Path,
    ExplorerNodeKind Kind,
    long? SizeBytes,
    DateTimeOffset? LastModified,
    bool IsNavigable,
    int AggregatedItemCount = 0,
    AggregateAction? AggregateAction = null,
    string? NavigationTarget = null,
    string? ParentNavigationTarget = null,
    ExplorerNodeRole Roles = ExplorerNodeRole.None)
{
    public string Target => NavigationTarget ?? Path;

    public static ExplorerNode FromEntry(
        ExplorerEntry entry,
        ExplorerNodeRole roles = ExplorerNodeRole.Structural) => new(
        entry.Id,
        entry.Name,
        entry.Path,
        entry.Kind,
        entry.SizeBytes,
        entry.LastModified,
        entry.IsNavigable,
        NavigationTarget: entry.NavigationTarget,
        ParentNavigationTarget: entry.ParentNavigationTarget,
        Roles: roles);
}

public sealed record ExplorerEdge(
    string SourceId,
    string TargetId,
    ExplorerGraphEdgeKind Kind = ExplorerGraphEdgeKind.Structural);

public sealed record ExplorerNeighborhood(
    string FocusNodeId,
    IReadOnlyList<ExplorerNode> Nodes,
    IReadOnlyList<ExplorerEdge> Edges,
    int TotalChildCount,
    int HiddenChildCount,
    string? Warning = null,
    bool SourceWasTruncated = false,
    AggregatePage? AggregatePage = null)
{
    public ExplorerNode Focus => Nodes.First(node => node.Id == FocusNodeId);
}

public sealed record ExplorerDirectoryBatch(
    ExplorerEntry Focus,
    IReadOnlyList<ExplorerEntry> AddedChildren,
    int ItemsObserved,
    bool IsComplete,
    ExplorerFailureKind Failure = ExplorerFailureKind.None,
    string? Warning = null,
    int? TotalChildCount = null,
    bool WasTruncated = false);

public sealed record SearchRequest(
    string RootPath,
    string Query,
    int MaxResults = 80,
    int MaxDirectories = 500);

public sealed record ExplorerSearchHit(
    string Id,
    string Name,
    string Path,
    ExplorerNodeKind Kind,
    string? NavigationTarget = null,
    string? ParentNavigationTarget = null)
{
    public string Target => NavigationTarget ?? Path;

    public override string ToString() => $"{Name}, {Kind}, {Path}";
}

public sealed record ExplorerSearchResult(
    IReadOnlyList<ExplorerSearchHit> Hits,
    bool WasTruncated,
    int DirectoriesVisited,
    string? Warning = null);

public interface IExplorerProvider
{
    public string AccessRoot { get; }

    public string DisplayRoot => AccessRoot;

    public Task<ExplorerDirectorySnapshot> GetDirectoryAsync(
        string path,
        CancellationToken cancellationToken);
}

public interface IExplorerSearchProvider
{
    public Task<ExplorerSearchResult> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken);
}

public interface IProgressiveExplorerProvider
{
    public IAsyncEnumerable<ExplorerDirectoryBatch> GetDirectoryBatchesAsync(
        string path,
        int batchSize,
        CancellationToken cancellationToken);
}

/// <summary>
/// Optional shallow preview acquisition. Implementations cap work at 64 inspected entries,
/// return at most three actual folders, and never recursively acquire their contents.
/// </summary>
public interface IExplorerDirectoryPreviewProvider
{
    public Task<ExplorerDirectorySnapshot> GetDirectoryPreviewAsync(
        string target,
        CancellationToken cancellationToken = default);
}

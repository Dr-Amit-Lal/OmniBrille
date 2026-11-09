using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Avalonia.Threading;
using OmniBrille.Core;

namespace OmniBrille.Desktop.Rendering;

public sealed record SceneDiagnostics(
    int Nodes,
    int Edges,
    int Labels,
    int Budget,
    double Zoom,
    double TextScale,
    bool AnimationActive,
    bool MotionActive,
    TimeSpan LayoutDuration,
    TimeSpan ScenePreparationDuration,
    TimeSpan LastRenderDuration,
    TimeSpan BackgroundDuration,
    TimeSpan EdgeDuration,
    TimeSpan GlyphDuration,
    TimeSpan LabelPreparationDuration,
    TimeSpan LabelCollisionDuration,
    TimeSpan LabelDrawDuration,
    long RenderAllocatedBytes,
    int TextCacheEntries,
    int ResourceCacheEntries)
{
    public int ConnectionSegments { get; init; }
    public int HoverHighlights { get; init; }
    public double MaximumHoverScale { get; init; }
    public int MatchBadges { get; init; }
    public int OverlappingLabelPairs { get; init; }
}

public sealed class GraphSceneControl : Control
{
    private const double AnimationDurationMilliseconds = 440;
    public const double MinimumInteractiveOpacity = 0.85;
    private static readonly Geometry FolderOutline = Geometry.Parse("M -25,-20 L -8,-20 L -3,-14 L 22,-14 L 22,-10 L 26,-10 L 21,18 L -22,18 Z M -22,18 L -25,-10 L 26,-10");
    private static readonly Geometry NebulaDocument = Geometry.Parse("M -12,-18 L 4,-18 L 13,-9 L 13,18 L -12,18 Z M 4,-18 L 4,-9 L 13,-9 M -7,-3 L 7,-3 M -7,3 L 7,3 M -7,9 L 4,9");
    private static readonly IBrush NebulaAtmosphere = new RadialGradientBrush
    {
        Center = new RelativePoint(0.48, 0.46, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.48, 0.46, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.64, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.72, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new(ScenePalette.NebulaWash, 0),
            new(ScenePalette.Dark.Background, 1),
        },
    };
    private static readonly IBrush NebulaVioletAtmosphere = new RadialGradientBrush
    {
        Center = new RelativePoint(0.7, 0.34, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.7, 0.34, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.48, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.56, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new(WithAlpha(ScenePalette.NebulaVioletWash, 68), 0),
            new(WithAlpha(ScenePalette.NebulaVioletWash, 0), 1),
        },
    };

    private readonly RadialGraphLayout _structureLayoutEngine = new();
    private static readonly Color PreviewGold = Color.Parse("#FFD36A");
    private static readonly Color BadgeText = Color.Parse("#101725");
    private bool _workspaceMode;
    private string[] _clusterRootIds = [];
    private readonly HashSet<string> _clusterRoots = new(ExplorerIdentity.Comparer);
    private IReadOnlyDictionary<string, int> _matchCounts = new Dictionary<string, int>(ExplorerIdentity.Comparer);
    private string? _previewNodeId;
    private int _matchBadges;
    private int _connectionSegments;
    private int _hoverHighlights;
    private readonly List<LabelBox> _edgeExclusions = new(128);
    private readonly List<(double Start, double End)> _edgeOcclusions = new(128);
    private readonly DispatcherTimer _animationTimer;
    private readonly Stopwatch _animationClock = new();
    private readonly Stopwatch _motionClock = Stopwatch.StartNew();
    private readonly Dictionary<string, Rect> _hitTargets = new(ExplorerIdentity.Comparer);
    private readonly List<LabelBox> _glyphBoxes = new(GraphNeighborhoodBuilder.DefaultNodeBudget);
    private readonly Dictionary<string, GraphLayoutNode> _renderLayout = new(ExplorerIdentity.Comparer);
    private readonly Dictionary<string, GraphNodePresentation> _presentations = new(ExplorerIdentity.Comparer);
    private readonly Dictionary<string, double> _lensInfluences = new(ExplorerIdentity.Comparer);
    private readonly Dictionary<string, double> _lensFrom = new(ExplorerIdentity.Comparer);
    private readonly Stopwatch _lensClock = new();
    private double _maximumHoverScale = 1;
    private readonly List<PreparedLabel> _preparedLabels = new(GraphNeighborhoodBuilder.DefaultNodeBudget);
    private readonly HashSet<string> _visibleLabelIds = new(ExplorerIdentity.Comparer);
    private readonly List<ExplorerNode> _drawOrder = new(GraphNeighborhoodBuilder.DefaultNodeBudget);
    private readonly Dictionary<string, Color> _branchAccents = new(ExplorerIdentity.Comparer);
    private readonly Dictionary<string, string> _labelNames = new(ExplorerIdentity.Comparer);
    private readonly Point[] _backgroundPoints = new Point[42];
    private readonly BoundedLruCache<LabelTextKey, TextLayout> _textCache = new(256);
    private readonly BoundedLruCache<Color, SolidColorBrush> _brushCache = new(176);
    private readonly BoundedLruCache<Color, IBrush> _folderFillCache = new(8);
    private readonly BoundedLruCache<Color, IBrush> _folderGlowCache = new(8);
    private readonly BoundedLruCache<PenKey, Pen> _penCache = new(384);
    private ExplorerNeighborhood? _neighborhood;
    private IReadOnlyDictionary<string, GraphLayoutNode> _targetLayout =
        new Dictionary<string, GraphLayoutNode>();
    private Dictionary<string, GraphLayoutNode> _animationFrom = new();
    private IReadOnlySet<string> _highlights = new HashSet<string>();
    private string? _selectedNodeId;
    private string? _hoveredNodeId;
    private string? _lastLeftClickNodeId;
    private ExplorerNeighborhood? _lastLeftClickScene;
    private Vector _pan;
    private Point _lastPointer;
    private bool _isPanning;
    private bool _reducedMotion;
    private bool _motionAllowed = true;
    private double _zoom = 1;
    private double _textScale = 1;
    private TimeSpan _layoutDuration;
    private TimeSpan _scenePreparationDuration;
    private TimeSpan _lastRenderDuration;
    private TimeSpan _backgroundDuration;
    private TimeSpan _edgeDuration;
    private TimeSpan _glyphDuration;
    private TimeSpan _labelPreparationDuration;
    private TimeSpan _labelCollisionDuration;
    private TimeSpan _labelDrawDuration;
    private long _renderAllocatedBytes;
    private int _renderedLabelCount;
    private ScenePalette? _cachedPalette;
    private double _cachedRenderScaling;

    public GraphSceneControl()
    {
        Focusable = true;
        ClipToBounds = true;
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(42) };
        _animationTimer.Tick += (_, _) =>
        {
            if (_animationClock.Elapsed.TotalMilliseconds >= AnimationDurationMilliseconds)
            {
                _animationClock.Stop();
            }

            if (!ShouldTick)
            {
                _animationTimer.Stop();
            }

            InvalidateVisual();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _motionAllowed = false;
            UpdateTicker();
        };
        AttachedToVisualTree += (_, _) =>
        {
            _motionAllowed = true;
            UpdateTicker();
        };
        GotFocus += (_, _) => NotifyAutomationInteractionChanged();
        LostFocus += (_, _) => NotifyAutomationInteractionChanged();
    }

    public event EventHandler<string>? NodeSelected;

    public event EventHandler<string>? NodeActivated;

    public event EventHandler<string?>? NodeHovered;

    public event EventHandler? BackRequested;

    public event EventHandler? DismissRequested;

    public event EventHandler? TextScaleChanged;

    public bool ReducedMotion
    {
        get => _reducedMotion;
        set
        {
            if (_reducedMotion == value)
            {
                return;
            }

            _reducedMotion = value;
            if (value)
            {
                _animationClock.Reset();
                _animationFrom.Clear();
            }
            UpdateTicker();
            InvalidateVisual();
        }
    }

    public bool ReducedEffects { get; set; }

    public bool SearchActive { get; set; }

    private Thickness _scenePadding;
    public Thickness ScenePadding
    {
        get => _scenePadding;
        set
        {
            if (_scenePadding == value) { return; }
            _scenePadding = value;
            InvalidateVisual();
        }
    }

    private Rect SceneViewport => new(ScenePadding.Left, ScenePadding.Top,
        Math.Max(1, Bounds.Width - ScenePadding.Left - ScenePadding.Right),
        Math.Max(1, Bounds.Height - ScenePadding.Top - ScenePadding.Bottom));

    public double TextScale
    {
        get => _textScale;
        set
        {
            var normalized = Math.Clamp(value, 1, 2);
            if (Math.Abs(_textScale - normalized) < 0.001)
            {
                return;
            }

            _textScale = normalized;
            _textCache.Clear();
            InvalidateVisual();
            TextScaleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public SceneDiagnostics Diagnostics => new(
        _neighborhood?.Nodes.Count ?? 0,
        _neighborhood?.Edges.Count ?? 0,
        _renderedLabelCount,
        _workspaceMode ? WorkspaceGraphLayout.MaximumNodes : GraphNeighborhoodBuilder.DefaultNodeBudget,
        _zoom,
        _textScale,
        _animationClock.IsRunning,
        ShouldAnimateMotion,
        _layoutDuration,
        _scenePreparationDuration,
        _lastRenderDuration,
        _backgroundDuration,
        _edgeDuration,
        _glyphDuration,
        _labelPreparationDuration,
        _labelCollisionDuration,
        _labelDrawDuration,
        _renderAllocatedBytes,
        _textCache.Count,
        _brushCache.Count + _penCache.Count + _folderFillCache.Count + _folderGlowCache.Count)
    {
        ConnectionSegments = _connectionSegments,
        HoverHighlights = _hoverHighlights,
        MaximumHoverScale = _maximumHoverScale,
        MatchBadges = _matchBadges,
        OverlappingLabelPairs = CountOverlappingLabels(),
    };

    internal bool ShouldAnimateMotion => !_reducedMotion && _motionAllowed && _neighborhood is not null;

    private bool ShouldTick => _animationClock.IsRunning || ShouldAnimateMotion;

    public void SetMotionActivity(bool active)
    {
        _motionAllowed = active;
        if (!active)
        {
            _animationClock.Reset();
            _animationFrom.Clear();
        }
        UpdateTicker();
        InvalidateVisual();
    }

    public void SetScene(
        ExplorerNeighborhood? neighborhood,
        string? selectedNodeId,
        IReadOnlySet<string>? highlights,
        bool animate = true)
    {
        _workspaceMode = false;
        _clusterRoots.Clear();
        _clusterRootIds = [];
        _matchCounts = new Dictionary<string, int>(ExplorerIdentity.Comparer);
        _previewNodeId = null;
        SetSceneCore(neighborhood, selectedNodeId, highlights, animate, null);
    }

    public void SetWorkspaceScene(
        ExplorerNeighborhood? scene,
        IReadOnlyList<string> clusterRootIds,
        string? selectedNodeId,
        IReadOnlyDictionary<string, int>? matchCounts,
        string? previewNodeId,
        bool animate = true)
    {
        ArgumentNullException.ThrowIfNull(clusterRootIds);
        var sameRoots = _workspaceMode && _clusterRootIds.SequenceEqual(clusterRootIds, ExplorerIdentity.Comparer);
        var stableLayout = sameRoots ? _targetLayout : null;
        _workspaceMode = true;
        _clusterRootIds = clusterRootIds.ToArray();
        _clusterRoots.Clear();
        foreach (var id in _clusterRootIds) { _clusterRoots.Add(id); }
        _matchCounts = matchCounts ?? new Dictionary<string, int>(ExplorerIdentity.Comparer);
        _previewNodeId = previewNodeId;
        SearchActive = _matchCounts.Count > 0 || previewNodeId is not null;
        var highlights = new HashSet<string>(ExplorerIdentity.Comparer);
        if (previewNodeId is not null) { highlights.Add(previewNodeId); }
        SetSceneCore(scene, selectedNodeId, highlights, animate && !ReferenceEquals(_neighborhood, scene), stableLayout);
    }

    private void SetSceneCore(
        ExplorerNeighborhood? neighborhood, string? selectedNodeId,
        IReadOnlySet<string>? highlights, bool animate, IReadOnlyDictionary<string, GraphLayoutNode>? stableWorkspaceLayout)
    {
        var preparationClock = Stopwatch.StartNew();
        // Navigation interpolates base geometry. Apply the live hover/float
        // once when rendering, never bake it into the next transition.
        var previousLayout = CurrentLayout(includeMotion: false).ToDictionary(pair => pair.Key, pair => pair.Value, ExplorerIdentity.Comparer);
        if (stableWorkspaceLayout is null && !ReferenceEquals(_neighborhood, neighborhood)) { _hitTargets.Clear(); }
        _neighborhood = neighborhood;
        var admittedIds = neighborhood?.Nodes.Select(node => node.Id).ToHashSet(ExplorerIdentity.Comparer);
        foreach (var stale in _hitTargets.Keys.Where(id => admittedIds?.Contains(id) != true).ToArray()) { _hitTargets.Remove(stale); }
        if (_hoveredNodeId is not null &&
            (neighborhood is null || !neighborhood.Nodes.Any(node => ExplorerIdentity.Equals(node.Id, _hoveredNodeId))))
        {
            _hoveredNodeId = null;
            ToolTip.SetTip(this, null);
        }

        var selectionChanged = !ExplorerIdentity.Equals(_selectedNodeId, selectedNodeId);
        _selectedNodeId = selectedNodeId;
        _highlights = highlights ?? new HashSet<string>();
        _drawOrder.Clear();
        if (neighborhood is not null)
        {
            _drawOrder.AddRange(neighborhood.Nodes
                .OrderBy(node => IsClusterRoot(node.Id) ? 1 : 0)
                .ThenBy(node => node.Id, ExplorerIdentity.Comparer));
        }
        PrepareScenePresentation();

        var layoutClock = Stopwatch.StartNew();
        _targetLayout = neighborhood is null ? new Dictionary<string, GraphLayoutNode>()
            : _workspaceMode ? WorkspaceGraphLayout.Layout(neighborhood, _clusterRootIds,
                stableWorkspaceLayout)
            : _structureLayoutEngine.Layout(neighborhood, previousLayout);
        layoutClock.Stop();
        _layoutDuration = layoutClock.Elapsed;
        UpdateLensInfluences();

        _animationFrom = _targetLayout.ToDictionary(
            pair => pair.Key,
            pair => previousLayout.TryGetValue(pair.Key, out var previous)
                ? previous
                : pair.Value with
                {
                    X = 0,
                    Y = 0,
                    Scale = Math.Min(0.28, pair.Value.Scale),
                    Opacity = 0,
                });

        if (animate && !ReducedMotion && neighborhood is not null)
        {
            _animationClock.Restart();
            UpdateTicker();
        }
        else
        {
            _animationClock.Reset();
            UpdateTicker();
        }

        UpdateAutomationDescription();
        NotifyAutomationSceneChanged();
        if (selectionChanged)
        {
            NotifyAutomationInteractionChanged();
        }
        preparationClock.Stop();
        _scenePreparationDuration = preparationClock.Elapsed;
        InvalidateVisual();
    }

    public void SetInteractionState(
        string? selectedNodeId,
        IReadOnlySet<string>? highlights,
        bool searchActive)
    {
        var selectionChanged = !ExplorerIdentity.Equals(_selectedNodeId, selectedNodeId);
        _selectedNodeId = selectedNodeId;
        _highlights = highlights ?? new HashSet<string>();
        SearchActive = searchActive;
        UpdateAutomationDescription();
        NotifyAutomationInteractionChanged();

        InvalidateVisual();
    }

    public void ZoomIn() => SetZoom(_zoom * 1.15);

    public void ZoomOut() => SetZoom(_zoom / 1.15);

    public void ResetView()
    {
        _zoom = 1;
        _pan = default;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var renderClock = Stopwatch.StartNew();
        var allocatedBytesBefore = GC.GetAllocatedBytesForCurrentThread();
        _labelPreparationDuration = TimeSpan.Zero;
        _matchBadges = 0;
        _connectionSegments = 0;
        _hoverHighlights = 0;
        base.Render(context);
        var palette = ActualThemeVariant == ThemeVariant.Light ? ScenePalette.Light : ScenePalette.Dark;
        EnsureCacheContext(palette);
        var phaseClock = Stopwatch.StartNew();
        context.DrawRectangle(Brush(palette.Background), null, Bounds);
        DrawBackgroundNetwork(context, palette);
        phaseClock.Stop();
        _backgroundDuration = phaseClock.Elapsed;

        if (_neighborhood is null)
        {
            _preparedLabels.Clear();
            _visibleLabelIds.Clear();
            _renderedLabelCount = 0;
            renderClock.Stop();
            _lastRenderDuration = renderClock.Elapsed;
            _renderAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBytesBefore;
            return;
        }

        var layout = CurrentLayout();
        var presentationContext = new GraphPresentationContext(
            _zoom,
            _neighborhood.Nodes.Count,
            _neighborhood.FocusNodeId,
            _selectedNodeId,
            _hoveredNodeId,
            _highlights,
            SearchActive,
            ReducedEffects,
            _textScale);
        _presentations.Clear();
        foreach (var node in _neighborhood.Nodes)
        {
            _presentations[node.Id] = GraphPresentationPolicy.Evaluate(node, layout[node.Id], presentationContext);
        }

        _hitTargets.Clear();
        _glyphBoxes.Clear();
        _preparedLabels.Clear();
        phaseClock.Restart();
        foreach (var node in _drawOrder)
        {
            if (!layout.TryGetValue(node.Id, out var position))
            {
                continue;
            }

            var preparedLabel = DrawNodeGlyph(context, palette, node, position, _presentations[node.Id]);
            if (preparedLabel.HasValue)
            {
                _preparedLabels.Add(preparedLabel.Value);
            }
        }
        phaseClock.Stop();
        _glyphDuration = phaseClock.Elapsed - _labelPreparationDuration;

        phaseClock.Restart();
        ResolvePreparedLabels();
        phaseClock.Stop();
        _labelCollisionDuration = phaseClock.Elapsed;
        _edgeExclusions.Clear();
        _edgeExclusions.AddRange(_glyphBoxes);
        foreach (var label in _preparedLabels)
        {
            var box = label.Candidate.Bounds;
            _edgeExclusions.Add(new LabelBox(box.X - 5, box.Y - 3, box.Width + 10, box.Height + 6));
        }
        phaseClock.Restart();
        DrawEdges(context, palette, layout);
        phaseClock.Stop();
        _edgeDuration = phaseClock.Elapsed;
        phaseClock.Restart();
        foreach (var label in _preparedLabels)
        {
            if (!_visibleLabelIds.Contains(label.Candidate.NodeId))
            {
                continue;
            }

            using (context.PushOpacity(label.Opacity))
            {
                if (IsHoverTarget(label.Candidate.NodeId))
                {
                    var box = label.Candidate.Bounds;
                    var accent = NodeAccent(label.Candidate.NodeId, palette);
                    context.DrawRectangle(Brush(accent, 38), null,
                        new Rect(box.X - 5, box.Y - 3, box.Width + 10, box.Height + 6), 5, 5);
                }
                label.Text.Draw(context, label.Origin);
            }
        }
        phaseClock.Stop();
        _labelDrawDuration = phaseClock.Elapsed;

        _renderedLabelCount = _visibleLabelIds.Count;
        renderClock.Stop();
        _lastRenderDuration = renderClock.Elapsed;
        _renderAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBytesBefore;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            _lastLeftClickNodeId = null;
            _lastLeftClickScene = null;
            BackRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }
        if (!properties.IsLeftButtonPressed)
        {
            return;
        }
        var nodeId = HitTest(point);
        var sameClickTarget = ReferenceEquals(_lastLeftClickScene, _neighborhood) &&
            ExplorerIdentity.Equals(_lastLeftClickNodeId, nodeId);
        _lastLeftClickNodeId = nodeId;
        _lastLeftClickScene = _neighborhood;
        if (nodeId is not null)
        {
            _selectedNodeId = nodeId;
            NodeSelected?.Invoke(this, nodeId);
            var node = GetAutomationNode(nodeId);
            var enterOnClick = node?.IsNavigable == true && node.Kind is ExplorerNodeKind.Folder or ExplorerNodeKind.Context or ExplorerNodeKind.Aggregate &&
                (_workspaceMode || !ExplorerIdentity.Equals(nodeId, _neighborhood!.FocusNodeId));
            if ((enterOnClick && e.ClickCount == 1) || (!enterOnClick && e.ClickCount == 2 && sameClickTarget))
            {
                NodeActivated?.Invoke(this, nodeId);
            }

            UpdateAutomationDescription();
            NotifyAutomationInteractionChanged();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isPanning = true;
            _lastPointer = point;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new GraphSceneAutomationPeer(this);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        if (_isPanning)
        {
            _pan += point - _lastPointer;
            _lastPointer = point;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var hovered = HitTest(point);
        if (!ExplorerIdentity.Equals(_hoveredNodeId, hovered))
        {
            _hoveredNodeId = hovered;
            ToolTip.SetTip(this, hovered is null ? null : GetAutomationNode(hovered)?.Name);
            UpdateLensInfluences();
            NodeHovered?.Invoke(this, hovered);
            UpdateTicker();
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoveredNodeId is not null)
        {
            _hoveredNodeId = null;
            ToolTip.SetTip(this, null);
            UpdateLensInfluences();
            NodeHovered?.Invoke(this, null);
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isPanning)
        {
            _isPanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        SetZoom(_zoom * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            DismissRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.OemPlus || e.Key == Key.Add)
        {
            ZoomIn();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
        {
            ZoomOut();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.D0 || e.Key == Key.NumPad0)
        {
            ResetView();
            e.Handled = true;
            return;
        }

        if (_neighborhood is null)
        {
            return;
        }

        if (e.Key == Key.Back)
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && _selectedNodeId is not null)
        {
            NodeActivated?.Invoke(this, _selectedNodeId);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var selected = FindDirectionalNode(e.Key);
            if (selected is null)
            {
                return;
            }

            _selectedNodeId = selected.Id;
            NodeSelected?.Invoke(this, _selectedNodeId);
            UpdateAutomationDescription();
            NotifyAutomationInteractionChanged();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    internal IReadOnlyList<ExplorerNode> GetAutomationNodes() => _neighborhood?.Nodes ?? [];

    internal ExplorerNode? GetAutomationNode(string nodeId) => _neighborhood?.Nodes.FirstOrDefault(node =>
        ExplorerIdentity.Equals(node.Id, nodeId));

    internal bool IsAutomationNodeSelected(string nodeId) =>
        ExplorerIdentity.Equals(nodeId, _selectedNodeId);

    internal bool IsAutomationNodeFocused(string nodeId) =>
        (!_workspaceMode || _clusterRootIds.Length == 1) && ExplorerIdentity.Equals(nodeId, _neighborhood?.FocusNodeId);

    internal bool IsAutomationNodeHighlighted(string nodeId) => !_workspaceMode && _highlights.Contains(nodeId);

    internal string GetAutomationNodeRelation(string nodeId)
    {
        var node = GetAutomationNode(nodeId);
        if (node is null || _neighborhood is null) { return "visible graph item"; }
        if (_workspaceMode && _clusterRoots.Contains(nodeId)) { return "workspace folder"; }
        var parentEdge = _neighborhood.Edges.FirstOrDefault(edge => edge.Kind == ExplorerGraphEdgeKind.Structural &&
            ExplorerIdentity.Equals(edge.TargetId, nodeId));
        var parent = parentEdge is null ? null : GetAutomationNode(parentEdge.SourceId);
        return parent is not null ? $"inside {parent.Name}"
            : ExplorerIdentity.Equals(nodeId, _neighborhood.FocusNodeId) ? "current folder" : "visible folder item";
    }

    internal string? GetAutomationSearchStatus(string nodeId)
    {
        var count = _matchCounts.GetValueOrDefault(nodeId);
        if (ExplorerIdentity.Equals(nodeId, _previewNodeId)) { return "Search result location"; }
        return count > 0 ? $"{count} search matches inside" : null;
    }

    private bool IsClusterRoot(string nodeId) => _workspaceMode
        ? _clusterRoots.Contains(nodeId) : ExplorerIdentity.Equals(nodeId, _neighborhood?.FocusNodeId);

    internal Rect GetAutomationNodeBounds(string nodeId) =>
        _hitTargets.TryGetValue(nodeId, out var bounds) ? bounds : default;

    internal void SelectAutomationNode(string nodeId)
    {
        if (GetAutomationNode(nodeId) is null)
        {
            return;
        }

        Focus();
        _selectedNodeId = nodeId;
        NodeSelected?.Invoke(this, nodeId);
        UpdateAutomationDescription();
        NotifyAutomationInteractionChanged();
        InvalidateVisual();
    }

    internal void ActivateAutomationNode(string nodeId)
    {
        SelectAutomationNode(nodeId);
        NodeActivated?.Invoke(this, nodeId);
    }

    private void PrepareScenePresentation()
    {
        _branchAccents.Clear();
        _labelNames.Clear();
        if (_neighborhood is null)
        {
            return;
        }

        foreach (var node in _neighborhood.Nodes)
        {
            // Appearance follows the native folder path so a pin and its graph
            // glyph keep the same color. Only admitted edges define containment.
            _branchAccents[node.Id] = NebulaFolderGeometry.AccentFor(node.Path);
            _labelNames[node.Id] = !_workspaceMode && HasRole(node, ExplorerNodeRole.DescendantPreview) ? $"↳ {node.Name}" : node.Name;
        }

        // Only a real containment edge gives a preview its parent's accent.
        foreach (var edge in _neighborhood.Edges)
        {
            if (edge.Kind == ExplorerGraphEdgeKind.Structural &&
                GetAutomationNode(edge.TargetId) is { } target &&
                HasRole(target, ExplorerNodeRole.DescendantPreview) &&
                _branchAccents.TryGetValue(edge.SourceId, out var parentAccent))
            {
                _branchAccents[edge.TargetId] = parentAccent;
            }
        }
    }

    private void DrawBackgroundNetwork(DrawingContext context, ScenePalette palette)
    {
        if (ReferenceEquals(palette, ScenePalette.Dark))
        {
            // Two reusable washes create the vignette; no simulated particles,
            // background connectors, new animation work, or per-frame brushes.
            if (!ReducedEffects)
            {
                context.DrawRectangle(NebulaAtmosphere, null, Bounds);
                context.DrawRectangle(NebulaVioletAtmosphere, null, Bounds);
                var starBrush = Brush(palette.BackgroundPoint, 46);
                for (var index = 0; index < 18; index++)
                {
                    var star = new Point(((index * 127) % 997) / 997d * Bounds.Width,
                        ((index * 283 + 71) % 991) / 991d * Bounds.Height);
                    var radius = index % 7 == 0 ? 1.05 : 0.6;
                    context.DrawEllipse(starBrush, null, star, radius, radius);
                }
            }
            return;
        }

        var density = ReducedEffects ? 16 : 42;
        var pointBrush = Brush(palette.BackgroundPoint, ReducedEffects ? (byte)18 : (byte)38);
        for (var index = 0; index < density; index++)
        {
            var x = ((index * 127) % 997) / 997d * Bounds.Width;
            var y = ((index * 283 + 71) % 991) / 991d * Bounds.Height;
            _backgroundPoints[index] = new Point(x, y);
            context.DrawEllipse(pointBrush, null, _backgroundPoints[index], index % 7 == 0 ? 1.8 : 1.05, index % 7 == 0 ? 1.8 : 1.05);

        }
    }

    private void DrawEdges(
        DrawingContext context,
        ScenePalette palette,
        IReadOnlyDictionary<string, GraphLayoutNode> layout)
    {
        foreach (var edge in _neighborhood!.Edges)
        {
            if (edge.Kind != ExplorerGraphEdgeKind.Structural ||
                (_workspaceMode && _clusterRoots.Contains(edge.TargetId)) ||
                !layout.TryGetValue(edge.SourceId, out var source) ||
                !layout.TryGetValue(edge.TargetId, out var target)) { continue; }
            var sourceCenter = ToCanvas(source);
            var targetCenter = ToCanvas(target);
            var start = PeripheralPoint(source, sourceCenter, targetCenter);
            var end = PeripheralPoint(target, targetCenter, sourceCenter);
            var direction = new Vector(end.X - start.X, end.Y - start.Y);
            if (Vector.Dot(direction, targetCenter - sourceCenter) <= 0) { continue; }
            // Hover and selection never change a connection. Clip straight
            // segments around every glyph and label, without introducing bends.
            _edgeOcclusions.Clear();
            foreach (var box in _edgeExclusions)
            {
                if (GraphEdgeGeometry.TryIntersectBox(start.X, start.Y, end.X, end.Y, box, out var from, out var to))
                {
                    _edgeOcclusions.Add((from, to));
                }
            }
            _edgeOcclusions.Sort(static (left, right) => left.Start.CompareTo(right.Start));
            var cursor = 0d;
            var pen = Pen(palette.Edge, ReferenceEquals(palette, ScenePalette.Dark) ? (byte)82 : (byte)120, 0.8);
            foreach (var interval in _edgeOcclusions)
            {
                if (interval.Start > cursor) { DrawSegment(cursor, interval.Start); }
                cursor = Math.Max(cursor, interval.End);
            }
            if (cursor < 1) { DrawSegment(cursor, 1); }

            void DrawSegment(double from, double to)
            {
                if ((to - from) * direction.Length < 0.5) { return; }
                context.DrawLine(pen, start + direction * from, start + direction * to);
                _connectionSegments++;
            }
        }
    }

    private bool IsHoverTarget(string nodeId) => ExplorerIdentity.Equals(nodeId, _previewNodeId ?? _hoveredNodeId);

    private Color NodeAccent(string nodeId, ScenePalette palette) => ReferenceEquals(palette, ScenePalette.Dark)
        ? _branchAccents.GetValueOrDefault(nodeId, palette.Node) : palette.Selection;

    private PreparedLabel? DrawNodeGlyph(
        DrawingContext context,
        ScenePalette palette,
        ExplorerNode node,
        GraphLayoutNode layout,
        GraphNodePresentation presentation)
    {
        var center = ToCanvas(layout);
        var scale = layout.Scale * Math.Clamp(_zoom, 0.68, 1.65);
        var isFocus = IsClusterRoot(node.Id);
        var isSelected = ExplorerIdentity.Equals(node.Id, _selectedNodeId);
        var isHovered = ExplorerIdentity.Equals(node.Id, _hoveredNodeId);
        var isHighlighted = _highlights.Contains(node.Id);
        var isNebula = ReferenceEquals(palette, ScenePalette.Dark);
        var opacity = Math.Max(
            MinimumInteractiveOpacity,
            Math.Clamp(layout.Opacity * (_workspaceMode ? 1 : presentation.OpacityMultiplier), 0, 1));
        var color = !_workspaceMode && isHighlighted ? palette.Search
            : isNebula ? _branchAccents.GetValueOrDefault(node.Id, palette.Node)
            : isFocus ? palette.Focus : palette.Node;

        if (presentation.LevelOfDetail == GraphLevelOfDetail.Point)
        {
            context.DrawEllipse(Brush(color, ToByte(220 * opacity)), null, center, 2.2, 2.2);
            _hitTargets[node.Id] = new Rect(center.X - 22, center.Y - 22, 44, 44);
            return null;
        }

        var halfWidth = 25 * scale;
        var halfHeight = 19 * scale;
        var nebulaSize = NebulaGlyphSize(node, scale, isFocus);
        var glyphHalfWidth = GlyphHalfWidth(node.Kind) * scale;
        // Reserve the local hover ring even at rest so captions and straight
        // connections stay outside it when the original effects are active.
        var decorationWidth = nebulaSize.Width * 1.42 + 6;
        var decorationHeight = nebulaSize.Height * 1.42 + 6;
        _glyphBoxes.Add(isNebula
            ? new LabelBox(center.X - decorationWidth - 3, center.Y - decorationHeight - 3, decorationWidth * 2 + 6, decorationHeight * 2 + 6)
            : new LabelBox(center.X - glyphHalfWidth - 3, center.Y - halfHeight - 3, glyphHalfWidth * 2 + 6, halfHeight * 2 + 6));
        var hitWidth = Math.Max(44, ((isNebula ? nebulaSize.Width : halfWidth) + 8) * 2);
        var hitHeight = Math.Max(44, ((isNebula ? nebulaSize.Height : halfHeight) + 8) * 2);
        _hitTargets[node.Id] = new Rect(
            center.X - (hitWidth / 2),
            center.Y - (hitHeight / 2),
            hitWidth,
            hitHeight);

        if (IsHoverTarget(node.Id))
        {
            _hoverHighlights++;
            var size = isNebula ? nebulaSize : new Size(halfWidth, halfHeight);
            var pulse = ShouldAnimateMotion ? (Math.Sin(_motionClock.Elapsed.TotalSeconds * 2.6) + 1) / 2 : 0.5;
            if (!ReducedEffects)
            {
                using (context.PushOpacity(0.45 + pulse * 0.25))
                {
                    context.DrawEllipse(FolderGlow(NodeAccent(node.Id, palette)), null, center, size.Width + 13, size.Height + 11);
                }
            }
        }
        if (isNebula)
        {
            DrawNebulaNode(context, palette, node, center, nebulaSize, color, opacity,
                isFocus, isSelected, isHighlighted, IsHoverTarget(node.Id));
        }
        else
        {
            var stroke = Pen(color, ToByte(250 * opacity),
                isFocus ? 2.05 : layout.Depth == 1 ? 1.25 : 0.95);
            if (isSelected && !isHovered)
            {
                var halo = isHighlighted ? palette.Search : palette.Selection;
                var haloAlpha = ReducedEffects ? 38 : isFocus ? 82 : 58;
                context.DrawEllipse(
                    null,
                    Pen(halo, ToByte(haloAlpha * opacity), ReducedEffects ? 4 : isFocus ? 11 : 7),
                    center,
                    halfWidth + 12,
                    halfHeight + 12);
            }

            if (isFocus)
            {
                DrawFocusReticle(
                    context,
                    center,
                    halfWidth + 17,
                    halfHeight + 15,
                    Pen(color, ToByte((ReducedEffects ? 185 : 235) * opacity), 1.15));
            }

            if (IsFocused && isSelected)
            {
                DrawFocusReticle(
                    context,
                    center,
                    halfWidth + 22,
                    halfHeight + 20,
                    Pen(palette.Selection, 255, 2.15));
            }

            switch (node.Kind)
            {
                case ExplorerNodeKind.Folder:
                case ExplorerNodeKind.Context:
                    DrawFolder(context, center, halfWidth, halfHeight, stroke, Brush(palette.Background));
                    break;
                case ExplorerNodeKind.File:
                    DrawFile(context, center, halfWidth * 0.72, halfHeight, stroke);
                    break;
                case ExplorerNodeKind.Aggregate:
                    DrawAggregate(context, center, halfHeight, stroke, Brush(color, 185), node.IsNavigable);
                    break;
            }
        }

        if (_workspaceMode)
        {
            DrawSearchAdornment(context, palette, node, center, isNebula ? nebulaSize : new Size(halfWidth, halfHeight));
        }

        if (presentation.LevelOfDetail < GraphLevelOfDetail.Labeled || !Bounds.Contains(center))
        {
            return null;
        }

        // Typography follows the immutable layout, not every hover/float frame.
        var baseScale = _targetLayout[node.Id].Scale * Math.Clamp(_zoom, 0.68, 1.65);
        var fontSize = Math.Round((isFocus ? 14
            : Math.Clamp(11.2 * baseScale, 10, 12)) * _textScale * 2) / 2;
        var widthScale = Math.Min(1.45, _textScale);
        var maxWidth = (isFocus ? 230 : _neighborhood!.Nodes.Count > 24 ? 105 : 155) * widthScale;
        if (_workspaceMode) { maxWidth = Math.Min(maxWidth, WorkspaceLabelWidth(node.Id)); }
        var labelClock = Stopwatch.StartNew();
        var key = new LabelTextKey(
            _labelNames[node.Id],
            CultureInfo.CurrentCulture.Name,
            fontSize,
            maxWidth,
            isFocus ? FontWeight.SemiBold : FontWeight.Normal,
            palette.Text);
        var text = _textCache.GetOrAdd(key, static item => new TextLayout(
            item.Text,
            new Typeface("avares://Avalonia.Fonts.Inter/Assets#Inter", FontStyle.Normal, item.Weight),
            item.FontSize,
            new SolidColorBrush(item.Color),
            textWrapping: TextWrapping.NoWrap,
            textTrimming: TextTrimming.CharacterEllipsis,
            maxWidth: item.MaxWidth,
            maxLines: 1));
        var width = Math.Min(maxWidth, text.WidthIncludingTrailingWhitespace);
        var labelOffset = isNebula ? decorationHeight : halfHeight;
        var origin = new Point(center.X - (width / 2), center.Y + labelOffset + 7);
        var bounds = new LabelBox(origin.X, origin.Y, width, Math.Max(fontSize + 3, text.Height));
        var prepared = new PreparedLabel(
            new LabelCandidate(node.Id, bounds, isFocus ? 1000 : 0, true),
            text,
            origin,
            1,
            center,
            origin);
        labelClock.Stop();
        _labelPreparationDuration += labelClock.Elapsed;
        return prepared;
    }

    private Size NebulaGlyphSize(ExplorerNode node, double scale, bool isFocus)
    {
        var factor = Math.Clamp(scale, 0.55, 1.6);
        if (_workspaceMode && _clusterRootIds.Length > 1)
        {
            // Root-only compact overviews retain real 44-DIP targets while their
            // decorative silhouettes fit the available cluster cells.
            factor *= Math.Clamp(Math.Min(SceneViewport.Width / 800, SceneViewport.Height / 420), 0.7, 1);
        }
        if (isFocus) { return new Size(26 * factor, 20 * factor); }
        return node.Kind switch
        {
            ExplorerNodeKind.File => new Size(8 * factor, 11 * factor),
            ExplorerNodeKind.Aggregate => new Size(10 * factor, 10 * factor),
            _ => new Size(12.5 * factor, 9.5 * factor),
        };
    }

    private double WorkspaceLabelWidth(string nodeId)
    {
        var count = _clusterRootIds.Length;
        var fraction = count <= 1 ? 0.42 : count == 2 ? 0.46
            : count == 3 ? ExplorerIdentity.Equals(nodeId, _clusterRootIds[0]) ? 0.55 : 0.35
            : count <= 4 ? 0.455 : count <= 6 ? 0.295 : 0.215;
        return Math.Max(56, SceneViewport.Width * 0.94 * fraction - 16);
    }

    private int CountOverlappingLabels()
    {
        var count = 0;
        for (var index = 0; index < _preparedLabels.Count; index++)
        {
            for (var other = index + 1; other < _preparedLabels.Count; other++)
            {
                if (_preparedLabels[index].Candidate.Bounds.Intersects(_preparedLabels[other].Candidate.Bounds, 0)) { count++; }
            }
        }
        return count;
    }

    private void DrawNebulaNode(
        DrawingContext context, ScenePalette palette, ExplorerNode node,
        Point center, Size size, Color color, double opacity,
        bool isFocus, bool isSelected, bool isHighlighted, bool isHovered)
    {
        var width = size.Width;
        var height = size.Height;
        if (node.Kind is ExplorerNodeKind.Folder or ExplorerNodeKind.Context)
        {
            using (context.PushTransform(Matrix.CreateScale(width / 26, height / 19) * Matrix.CreateTranslation(center.X, center.Y)))
            {
                // Static paths and bounded brush/pen caches keep the glow inexpensive.
                if (!ReducedEffects)
                {
                    context.DrawEllipse(FolderGlow(color), null, new Point(0, 0), 42, 35);
                    context.DrawGeometry(null, Pen(color, ToByte(28 * opacity), 4), NebulaFolderGeometry.Front);
                }
                context.DrawGeometry(Brush(palette.Background), Pen(color, ToByte(250 * opacity), 1.7), NebulaFolderGeometry.Back);
                context.DrawGeometry(Brush(color, ToByte(32 * opacity)), null, NebulaFolderGeometry.Back);
                context.DrawGeometry(Brush(palette.Background), null, NebulaFolderGeometry.Front);
                context.DrawGeometry(FolderFill(color), Pen(color, ToByte(250 * opacity), 1.6), NebulaFolderGeometry.Front);
                if (!ReducedEffects)
                {
                    context.DrawGeometry(null, Pen(palette.Text, ToByte(160 * opacity), 0.65), NebulaFolderGeometry.Front);
                }
            }
        }
        else if (node.Kind == ExplorerNodeKind.File)
        {
            using (context.PushTransform(Matrix.CreateScale(width / 13, height / 18) * Matrix.CreateTranslation(center.X, center.Y)))
            {
                context.DrawGeometry(Brush(palette.Background), Pen(color, ToByte(250 * opacity), 1.6), NebulaDocument);
            }
        }
        else
        {
            DrawAggregate(context, center, height, Pen(color, ToByte(250 * opacity), 1.5), Brush(color, 230), node.IsNavigable);
        }

        if (isSelected || isHovered)
        {
            context.DrawEllipse(null,
                Pen(palette.Selection, ToByte((isSelected ? 250 : 180) * opacity), isSelected ? 1.8 : 1),
                center, width * 1.42 + 6, height * 1.42 + 6);
        }
        if (isHighlighted && !_workspaceMode)
        {
            // Search diamond clears the whole folder rectangle, including its corners.
            var extentX = width * 2 + 7;
            var extentY = height * 2 + 7;
            var searchPen = Pen(palette.Search, ToByte(250 * opacity), 1.3);
            var top = new Point(center.X, center.Y - extentY);
            var right = new Point(center.X + extentX, center.Y);
            var bottom = new Point(center.X, center.Y + extentY);
            var left = new Point(center.X - extentX, center.Y);
            context.DrawLine(searchPen, top, right);
            context.DrawLine(searchPen, right, bottom);
            context.DrawLine(searchPen, bottom, left);
            context.DrawLine(searchPen, left, top);
        }
        if (IsFocused && isSelected)
        {
            DrawFocusReticle(context, center, Math.Max(22, width + 11), Math.Max(22, height + 11), Pen(palette.Selection, 255, 2));
        }
    }

    private void DrawSearchAdornment(DrawingContext context, ScenePalette palette, ExplorerNode node, Point center, Size size)
    {
        var count = _matchCounts.GetValueOrDefault(node.Id);
        if (count <= 0 || node.Kind == ExplorerNodeKind.File) { return; }
        var label = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
        var key = new LabelTextKey(label, CultureInfo.InvariantCulture.Name, 9.5, 26, FontWeight.Bold, BadgeText);
        var text = _textCache.GetOrAdd(key, static item => new TextLayout(item.Text,
            new Typeface("avares://Avalonia.Fonts.Inter/Assets#Inter", FontStyle.Normal, item.Weight),
            item.FontSize, new SolidColorBrush(item.Color), maxWidth: item.MaxWidth, maxLines: 1));
        var badge = new Point(center.X + size.Width + 3, center.Y - size.Height + 1);
        var badgeRadius = label.Length > 2 ? 11 : 8;
        context.DrawEllipse(Brush(PreviewGold), Pen(palette.Background, 255, 1.5), badge, badgeRadius, 8);
        text.Draw(context, new Point(badge.X - text.WidthIncludingTrailingWhitespace / 2, badge.Y - text.Height / 2));
        _glyphBoxes.Add(new LabelBox(badge.X - badgeRadius - 2, badge.Y - 10, badgeRadius * 2 + 4, 20));
        _matchBadges++;
    }

    private void ResolvePreparedLabels()
    {
        _visibleLabelIds.Clear();
        var placements = GraphPresentationPolicy.PlaceLabels(
            _preparedLabels.Select(label => label.Candidate),
            new LabelBox(SceneViewport.X, SceneViewport.Y, SceneViewport.Width, SceneViewport.Height), _glyphBoxes);
        for (var index = 0; index < _preparedLabels.Count; index++)
        {
            var label = _preparedLabels[index];
            var placed = placements[label.Candidate.NodeId];
            _preparedLabels[index] = label with { Candidate = label.Candidate with { Bounds = placed }, Origin = new Point(placed.X, placed.Y) };
            _visibleLabelIds.Add(label.Candidate.NodeId);
        }
        if (_workspaceMode && CountOverlappingLabels() > 0) { RepairWorkspaceLabelCollisions(); }
    }

    private void RepairWorkspaceLabelCollisions()
    {
        // The general ring search samples every 18 DIP. At large text sizes a
        // valid narrow gap can fall between samples. Try the exact boundaries
        // of the small bounded scene before accepting a colliding placement.
        var viewport = SceneViewport;
        var remainingChecks = 4096;
        var order = Enumerable.Range(0, _preparedLabels.Count)
            .OrderBy(index => _preparedLabels[index].Candidate.Priority)
            .ThenBy(index => _preparedLabels[index].Candidate.NodeId, ExplorerIdentity.Comparer).ToArray();
        for (var pass = 0; pass < 2 && CountOverlappingLabels() > 0; pass++)
        {
            var changed = false;
            foreach (var index in order)
            {
                if (remainingChecks <= 0) { break; }
                var label = _preparedLabels[index];
                var current = label.Candidate.Bounds;
                if (!_preparedLabels.Where((_, other) => other != index).Any(other => current.Intersects(other.Candidate.Bounds, 0))) { continue; }
                var obstacles = _glyphBoxes.Concat(_preparedLabels.Where((_, other) => other != index)
                    .Select(other => other.Candidate.Bounds)).ToArray();
                const double gap = 3.5;
                var xs = new HashSet<double> { viewport.Left, viewport.Right - current.Width, current.X };
                var ys = new HashSet<double> { viewport.Top, viewport.Bottom - current.Height, current.Y };
                foreach (var obstacle in obstacles)
                {
                    xs.Add(Math.Clamp(obstacle.X - current.Width - gap, viewport.Left, viewport.Right - current.Width));
                    xs.Add(Math.Clamp(obstacle.X + obstacle.Width + gap, viewport.Left, viewport.Right - current.Width));
                    ys.Add(Math.Clamp(obstacle.Y - current.Height - gap, viewport.Top, viewport.Bottom - current.Height));
                    ys.Add(Math.Clamp(obstacle.Y + obstacle.Height + gap, viewport.Top, viewport.Bottom - current.Height));
                }
                var best = current;
                var bestDistance = double.PositiveInfinity;
                var orderedXs = xs.OrderBy(x => Math.Abs(x - current.X)).ToArray();
                var orderedYs = ys.OrderBy(y => Math.Abs(y - current.Y)).ToArray();
                foreach (var x in orderedXs)
                {
                    if (remainingChecks <= 0 || Math.Pow(x - current.X, 2) >= bestDistance) { break; }
                    foreach (var y in orderedYs)
                    {
                        var distance = Math.Pow(x - current.X, 2) + Math.Pow(y - current.Y, 2);
                        if (remainingChecks <= 0 || distance >= bestDistance) { break; }
                        remainingChecks--;
                        var candidate = new LabelBox(x, y, current.Width, current.Height);
                        var free = true;
                        foreach (var obstacle in obstacles)
                        {
                            if (candidate.Intersects(obstacle, 3)) { free = false; break; }
                        }
                        if (!free) { continue; }
                        best = candidate;
                        bestDistance = distance;
                    }
                }
                if (!double.IsFinite(bestDistance)) { continue; }
                _preparedLabels[index] = label with { Candidate = label.Candidate with { Bounds = best }, Origin = new Point(best.X, best.Y) };
                changed = true;
            }
            if (!changed) { break; }
        }
    }

    private static void DrawFolder(
        DrawingContext context,
        Point center,
        double halfWidth,
        double halfHeight,
        Pen stroke,
        IBrush background)
    {
        using (context.PushTransform(Matrix.CreateScale(halfWidth / 25, halfHeight / 19) * Matrix.CreateTranslation(center.X, center.Y)))
        {
            context.DrawGeometry(background, stroke, FolderOutline);
        }
    }

    private static void DrawFile(
        DrawingContext context,
        Point center,
        double halfWidth,
        double halfHeight,
        Pen stroke)
    {
        var body = new Rect(center.X - halfWidth, center.Y - halfHeight, halfWidth * 2, halfHeight * 2);
        context.DrawRectangle(null, stroke, body, 2, 2);
        context.DrawLine(stroke, new Point(center.X - (halfWidth * 0.55), center.Y), new Point(center.X + (halfWidth * 0.55), center.Y));
    }

    private static void DrawAggregate(
        DrawingContext context,
        Point center,
        double radius,
        Pen stroke,
        IBrush markerBrush,
        bool isNavigable)
    {
        context.DrawEllipse(null, stroke, center, radius, radius);
        context.DrawEllipse(markerBrush, null, center, 2.5, 2.5);
        if (isNavigable)
        {
            context.DrawLine(stroke, new Point(center.X - 5, center.Y), new Point(center.X + 5, center.Y));
            context.DrawLine(stroke, new Point(center.X + 2, center.Y - 3), new Point(center.X + 5, center.Y));
            context.DrawLine(stroke, new Point(center.X + 2, center.Y + 3), new Point(center.X + 5, center.Y));
        }
    }

    private static void DrawFocusReticle(
        DrawingContext context,
        Point center,
        double halfWidth,
        double halfHeight,
        Pen pen)
    {
        const double arm = 8;
        context.DrawLine(pen, new Point(center.X - halfWidth, center.Y - halfHeight), new Point(center.X - halfWidth + arm, center.Y - halfHeight));
        context.DrawLine(pen, new Point(center.X - halfWidth, center.Y - halfHeight), new Point(center.X - halfWidth, center.Y - halfHeight + arm));
        context.DrawLine(pen, new Point(center.X + halfWidth, center.Y - halfHeight), new Point(center.X + halfWidth - arm, center.Y - halfHeight));
        context.DrawLine(pen, new Point(center.X + halfWidth, center.Y - halfHeight), new Point(center.X + halfWidth, center.Y - halfHeight + arm));
        context.DrawLine(pen, new Point(center.X - halfWidth, center.Y + halfHeight), new Point(center.X - halfWidth + arm, center.Y + halfHeight));
        context.DrawLine(pen, new Point(center.X - halfWidth, center.Y + halfHeight), new Point(center.X - halfWidth, center.Y + halfHeight - arm));
        context.DrawLine(pen, new Point(center.X + halfWidth, center.Y + halfHeight), new Point(center.X + halfWidth - arm, center.Y + halfHeight));
        context.DrawLine(pen, new Point(center.X + halfWidth, center.Y + halfHeight), new Point(center.X + halfWidth, center.Y + halfHeight - arm));
    }

    private IReadOnlyDictionary<string, GraphLayoutNode> CurrentLayout(bool includeMotion = true)
    {
        _maximumHoverScale = 1;
        if ((!_animationClock.IsRunning || _animationClock.Elapsed.TotalMilliseconds >= AnimationDurationMilliseconds) && (!includeMotion || !ShouldAnimateMotion))
        {
            return _targetLayout;
        }

        var amount = 1d;
        if (_animationClock.IsRunning)
        {
            var raw = Math.Clamp(_animationClock.Elapsed.TotalMilliseconds / AnimationDurationMilliseconds, 0, 1);
            amount = 1 - Math.Pow(1 - raw, 3);
        }

        _renderLayout.Clear();
        foreach (var pair in _targetLayout)
        {
            var target = pair.Value;
            var from = _animationFrom.TryGetValue(pair.Key, out var source) ? source : target;
            var interpolated = new GraphLayoutNode(
                pair.Key,
                Lerp(from.X, target.X, amount),
                Lerp(from.Y, target.Y, amount),
                Lerp(from.Scale, target.Scale, amount),
                Lerp(from.Opacity, target.Opacity, amount),
                target.Depth,
                target.PresentationBand);
            var motion = includeMotion ? GraphMotionPolicy.Evaluate(interpolated, _motionClock.Elapsed.TotalSeconds,
                LensInfluence(pair.Key), !ShouldAnimateMotion, IsClusterRoot(pair.Key)) : interpolated;
            _renderLayout[pair.Key] = motion;
            _maximumHoverScale = Math.Max(_maximumHoverScale, motion.Scale / target.Scale);
        }

        return _renderLayout;
    }

    private Point ToCanvas(GraphLayoutNode node)
    {
        var viewport = SceneViewport;
        var width = viewport.Width * (_workspaceMode ? 0.94 : 0.66) * _zoom;
        var height = viewport.Height * (_workspaceMode ? 0.94 : 0.68) * _zoom;
        return new Point(viewport.Center.X + _pan.X + (node.X * width), viewport.Center.Y + _pan.Y + (node.Y * height));
    }

    private string? HitTest(Point point) => _hitTargets
        .Where(pair => pair.Value.Contains(point))
        .OrderBy(pair => (GetAutomationNode(pair.Key)?.Roles & ExplorerNodeRole.DescendantPreview) != 0 ? 1 : 0)
        .ThenBy(pair => new Vector(point.X - pair.Value.Center.X, point.Y - pair.Value.Center.Y).Length)
        .ThenBy(pair => pair.Value.Width * pair.Value.Height)
        .Select(pair => pair.Key)
        .FirstOrDefault();

    private Point PeripheralPoint(GraphLayoutNode node, Point center, Point toward)
    {
        var scale = node.Scale * Math.Clamp(_zoom, 0.68, 1.65);
        var sceneNode = GetAutomationNode(node.NodeId)!;
        if (ActualThemeVariant != ThemeVariant.Light)
        {
            var size = NebulaGlyphSize(sceneNode, scale, IsClusterRoot(node.NodeId));
            var peripheral = GraphEdgeGeometry.PeripheralPoint(center.X, center.Y, toward.X, toward.Y, size.Width + 2, size.Height + 2);
            return new Point(peripheral.X, peripheral.Y);
        }
        var kind = sceneNode.Kind;
        var point = GraphEdgeGeometry.PeripheralPoint(center.X, center.Y, toward.X, toward.Y,
            GlyphHalfWidth(kind) * scale, 19 * scale + 2);
        return new Point(point.X, point.Y);
    }

    private static double GlyphHalfWidth(ExplorerNodeKind kind) => kind switch
    {
        ExplorerNodeKind.File => 18,
        ExplorerNodeKind.Aggregate => 19,
        _ => 25,
    };

    private double LensInfluence(string nodeId)
    {
        var target = _lensInfluences.GetValueOrDefault(nodeId);
        var from = _lensFrom.GetValueOrDefault(nodeId);
        var t = Math.Clamp(_lensClock.Elapsed.TotalMilliseconds / 180, 0, 1);
        return Lerp(from, target, t * t * (3 - 2 * t));
    }

    private void UpdateLensInfluences()
    {
        var current = _targetLayout.Keys.ToDictionary(id => id, LensInfluence, ExplorerIdentity.Comparer);
        _lensFrom.Clear();
        foreach (var pair in current) { _lensFrom[pair.Key] = pair.Value; }
        _lensInfluences.Clear();
        _lensClock.Restart();
        var active = _previewNodeId ?? _hoveredNodeId;
        if (active is null || _neighborhood is null || !_targetLayout.TryGetValue(active, out var hovered)) { return; }

        foreach (var node in _targetLayout.Values)
        {
            // A star's two-hop neighbors are the whole scene. Use spatial proximity
            // instead, and keep the orientation anchor still.
            if (IsClusterRoot(node.NodeId)) { continue; }
            var distance = Math.Sqrt(Math.Pow(node.X - hovered.X, 2) + Math.Pow(node.Y - hovered.Y, 2));
            _lensInfluences[node.NodeId] = ExplorerIdentity.Equals(node.NodeId, active)
                ? 1 : 0.22 * Math.Max(0, 1 - distance / 0.23);
        }
    }

    private ExplorerNode? FindDirectionalNode(Key key)
    {
        if (_neighborhood is null || !_targetLayout.TryGetValue(_selectedNodeId ?? _neighborhood.FocusNodeId, out var origin))
        {
            return _neighborhood?.Focus;
        }

        var desired = key switch
        {
            Key.Left => new Vector(-1, 0),
            Key.Right => new Vector(1, 0),
            Key.Up => new Vector(0, -1),
            _ => new Vector(0, 1),
        };
        var candidates = _neighborhood.Nodes
            .Where(node => !ExplorerIdentity.Equals(node.Id, origin.NodeId) && _targetLayout.ContainsKey(node.Id))
            .Select(node =>
            {
                var candidate = _targetLayout[node.Id];
                var delta = new Vector(candidate.X - origin.X, candidate.Y - origin.Y);
                var length = Math.Max(0.0001, delta.Length);
                var alignment = ((delta.X * desired.X) + (delta.Y * desired.Y)) / length;
                return new { Node = node, Alignment = alignment, Distance = length };
            })
            .ToArray();
        return candidates
            .Where(item => item.Alignment > 0.15)
            .OrderByDescending(item => item.Alignment)
            .ThenBy(item => item.Distance)
            .ThenBy(item => item.Node.Id, ExplorerIdentity.Comparer)
            .Select(item => item.Node)
            .FirstOrDefault() ?? candidates
                .OrderBy(item => item.Distance)
                .ThenBy(item => item.Node.Id, ExplorerIdentity.Comparer)
                .Select(item => item.Node)
                .FirstOrDefault();
    }

    private void UpdateTicker()
    {
        if (ShouldTick)
        {
            _animationTimer.Start();
        }
        else
        {
            _animationTimer.Stop();
        }
    }

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.5, 2.4);
        InvalidateVisual();
    }

    private void UpdateAutomationDescription()
    {
        var selected = _neighborhood?.Nodes.FirstOrDefault(node =>
            ExplorerIdentity.Equals(node.Id, _selectedNodeId));
        var description = selected is null
            ? "Spatial folder workspace. Use arrows to select folders or files and Enter to open. Right-click steps back."
            : $"Selected {selected.Kind}: {selected.Name}. Use Enter to open.";
        SetValue(AutomationProperties.HelpTextProperty, description);
    }

    private void NotifyAutomationSceneChanged() =>
        (ControlAutomationPeer.FromElement(this) as GraphSceneAutomationPeer)?.NotifySceneChanged();

    private void NotifyAutomationInteractionChanged() =>
        (ControlAutomationPeer.FromElement(this) as GraphSceneAutomationPeer)?.NotifyInteractionChanged();

    private void EnsureCacheContext(ScenePalette palette)
    {
        var renderScaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        if (ReferenceEquals(_cachedPalette, palette) && Math.Abs(_cachedRenderScaling - renderScaling) < 0.001)
        {
            return;
        }

        _cachedPalette = palette;
        _cachedRenderScaling = renderScaling;
        _textCache.Clear();
        _brushCache.Clear();
        _folderFillCache.Clear();
        _folderGlowCache.Clear();
        _penCache.Clear();
    }

    private SolidColorBrush Brush(Color color, byte alpha = byte.MaxValue)
    {
        var resolved = alpha == byte.MaxValue ? color : WithAlpha(color, alpha);
        return _brushCache.GetOrAdd(resolved, static item => new SolidColorBrush(item));
    }

    private IBrush FolderFill(Color color) => _folderFillCache.GetOrAdd(color, static item => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new(WithAlpha(item, 135), 0),
            new(WithAlpha(item, 35), 0.5),
            new(WithAlpha(item, 65), 1),
        },
    });

    private IBrush FolderGlow(Color color) => _folderGlowCache.GetOrAdd(color, static item => new RadialGradientBrush
    {
        GradientStops = new GradientStops
        {
            new(WithAlpha(item, 65), 0),
            new(WithAlpha(item, 30), 0.5),
            new(WithAlpha(item, 0), 1),
        },
    });

    private Pen Pen(Color color, byte alpha, double thickness)
    {
        var resolved = alpha == byte.MaxValue ? color : WithAlpha(color, alpha);
        return _penCache.GetOrAdd(
            new PenKey(resolved, thickness),
            item => new Pen(Brush(item.Color), item.Thickness));
    }

    private static double Lerp(double from, double to, double amount) => from + ((to - from) * amount);

    private static byte ToByte(double value) => (byte)Math.Clamp(value, 0, 255);

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static bool HasRole(ExplorerNode node, ExplorerNodeRole role) => (node.Roles & role) == role;

    private readonly record struct PreparedLabel(LabelCandidate Candidate, TextLayout Text, Point Origin, double Opacity, Point Anchor, Point PreferredOrigin);

    private readonly record struct LabelTextKey(
        string Text,
        string CultureName,
        double FontSize,
        double MaxWidth,
        FontWeight Weight,
        Color Color);

    private readonly record struct PenKey(Color Color, double Thickness);
}

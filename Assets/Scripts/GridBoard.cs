using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class GridBoard : MonoBehaviour
{
    private const string DisplayObjectName = "Display";
    private const string ComponentsObjectName = "Components";
    private const string WireViewsObjectName = "WireViews";
    private const string WirePreviewObjectName = "WirePreview";
    private const string WireViewResourceName = "WireView";

    private static readonly WireConnection[] CardinalWireConnections =
    {
        WireConnection.North,
        WireConnection.East,
        WireConnection.South,
        WireConnection.West
    };

    [Header("Grid")]
    [SerializeField, Min(1)]
    private int width = 8;

    [SerializeField, Min(1)]
    private int height = 6;

    [SerializeField, Min(0.01f)]
    [Tooltip("The length of one cell edge in this object's local space.")]
    private float cellSize = 1f;

    [Header("Scene Debug")]
    [SerializeField]
    [Tooltip("Draw the grid as Scene-view gizmos.")]
    private bool drawGrid = true;

    [SerializeField]
    private Color gridColor = new(0f, 0.8f, 1f, 0.75f);

    [SerializeField]
    private Color hoverColor = new(1f, 0.55f, 0f, 0.3f);

    [Header("Game View Grid")]
    [SerializeField]
    [Tooltip("Draw the logical grid in the Game view and player builds.")]
    private bool drawGridInGameView = true;

    [SerializeField, Range(0f, 0.1f)]
    [Tooltip("Raises runtime grid lines above the board to avoid depth flicker, as a fraction of cell size.")]
    private float gameViewGridHeight = 0.01f;

    [Header("Grid Size Visual")]
    [SerializeField]
    [Tooltip("Optional mesh resized to cover the logical grid. Its position, orientation, and thickness are preserved.")]
    private Transform gridSizeVisual;

    [Header("Component Hierarchy")]
    [SerializeField]
    [Tooltip("Object that owns the Components container. If empty, a nearby Display is found automatically.")]
    private Transform displayRoot;

    [SerializeField]
    [Tooltip("Container used for placed BoardComponent objects. Created automatically as Display/Components.")]
    private Transform componentsRoot;

    [Header("Wire Placement")]
    [SerializeField]
    private bool enableWirePlacement = true;

    [SerializeField]
    [Tooltip("Camera used to project the mouse onto the board. If empty, the Main Camera is used.")]
    private Camera wirePlacementCamera;

    [Header("Wire Visuals")]
    [SerializeField]
    [Tooltip("Prefab used once per occupied wire cell. If empty, Resources/WireView is loaded automatically.")]
    private WireView wireViewPrefab;

    [SerializeField]
    [Tooltip("Container for generated wire views. Created automatically as a child of the GridBoard.")]
    private Transform wireViewsRoot;

    [SerializeField, Range(0f, 0.25f)]
    [Tooltip("Height of wire visuals above the board as a fraction of cell size.")]
    private float wireViewHeight = 0.04f;

    [Header("Wire Cursor Visual")]
    [SerializeField]
    [Tooltip("Tint used for the center or edge that LMB can place.")]
    private Color wireGhostColor = new(1f, 0.65f, 0.12f, 0.55f);

    [SerializeField]
    [Tooltip("Tint used for the exact center or edge that RMB will delete.")]
    private Color wireDeleteHighlightColor = new(1f, 0.12f, 0.04f, 1f);

    [SerializeField, Range(0.1f, 0.45f)]
    [Tooltip("Half-size of the center hover area, as a fraction of cell size. The remaining area targets neighboring edges.")]
    private float wireCenterHoverRadius = 0.25f;

    [SerializeField, Range(0f, 0.1f)]
    [Tooltip("Raises ghost wire parts above live wires, as a fraction of cell size.")]
    private float wireGhostHeightOffset = 0.015f;

    [Header("Wire Debug")]
    [SerializeField]
    private bool drawWireGizmos = true;

    [SerializeField]
    private Color wireColor = new(1f, 0.8f, 0.1f, 1f);

    [SerializeField, Range(0f, 0.25f)]
    [Tooltip("Height above the board as a fraction of cell size.")]
    private float wireGizmoHeight = 0.02f;

    [SerializeField, Range(0.01f, 0.2f)]
    [Tooltip("Center marker radius as a fraction of cell size.")]
    private float wireGizmoNodeSize = 0.06f;

    private Vector2Int[,] cells;
    private BoardComponent[,] occupants;
    private WireCell[,] wires;
    private bool occupancyDirty = true;
    private bool rebuildingOccupancy;
    private bool wireViewsDirty = true;
    private bool gameplayInputEnabled = true;
    private int topologyRevision;
    private WireEditMode activeWireEditMode;
    private bool hasPreviousWireDragCell;
    private Vector2Int previousWireDragCell;
    private readonly Dictionary<Vector2Int, WireView> wireViews = new();
    private readonly HashSet<WireView> interactionHighlightedWireViews = new();
    private Transform wirePreviewRoot;
    private WireView primaryWirePreview;
    private WireView secondaryWirePreview;
    private GameObject runtimeGridObject;
    private Mesh runtimeGridMesh;
    private MeshRenderer runtimeGridRenderer;
    private Material runtimeGridMaterial;

    public int Width => width;
    public int Height => height;
    public float CellSize => cellSize;
    public int CellCount => width * height;
    public bool DrawGrid => drawGrid;
    public Color HoverColor => hoverColor;
    public Transform DisplayRoot => ResolveDisplayRoot();
    public Transform ComponentsRoot => ResolveComponentsRoot();
    public int TopologyRevision => topologyRevision;
    public bool GameplayInputEnabled => gameplayInputEnabled;
    public bool WirePlacementEnabled =>
        enableWirePlacement && gameplayInputEnabled;
    public bool DrawGridInGameView
    {
        get => drawGridInGameView;
        set
        {
            if (drawGridInGameView == value)
                return;

            drawGridInGameView = value;
            RefreshRuntimeGridView();
        }
    }

    public event Action<Vector2Int> WireChanged;
    public event Action TopologyChanged;

    /// <summary>
    /// Enumerates the stored logical coordinates in row-major order.
    /// Valid coordinates run from (0, 0) through (Width - 1, Height - 1).
    /// </summary>
    public IEnumerable<Vector2Int> Cells
    {
        get
        {
            EnsureCells();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    yield return cells[x, y];
            }
        }
    }

    private void Awake()
    {
        EnsureCells();
        EnsureWireStorage();
        EnsureComponentsRoot();
        RebuildOccupancy();
        RefreshWireViews();
        RefreshRuntimeGridView();
        RefreshGridSizeVisual();
    }

    private void OnEnable()
    {
        EnsureCells();
        EnsureWireStorage();
        EnsureComponentsRoot();
        RebuildOccupancy();
        RefreshWireViews();
        RefreshRuntimeGridView();
        RefreshGridSizeVisual();
    }

    private void OnDisable()
    {
        ResetWireDrag();
        ClearWireHoverVisual();
        DestroyWirePreview();
        DestroyRuntimeGridView();
    }

    private void OnDestroy()
    {
        DestroyWirePreview();
        DestroyRuntimeGridView();
    }

    private void OnValidate()
    {
        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);
        cellSize = Mathf.Max(0.01f, cellSize);
        wireViewHeight = Mathf.Clamp(wireViewHeight, 0f, 0.25f);
        wireCenterHoverRadius = Mathf.Clamp(wireCenterHoverRadius, 0.1f, 0.45f);
        wireGhostHeightOffset = Mathf.Clamp(wireGhostHeightOffset, 0f, 0.1f);
        wireGizmoHeight = Mathf.Clamp(wireGizmoHeight, 0f, 0.25f);
        wireGizmoNodeSize = Mathf.Clamp(wireGizmoNodeSize, 0.01f, 0.2f);
        gameViewGridHeight = Mathf.Clamp(gameViewGridHeight, 0f, 0.1f);
        RebuildCells();
        bool topologyResized = EnsureWireStorage();
        occupancyDirty = true;
        wireViewsDirty = true;
        if (runtimeGridObject != null)
            RefreshRuntimeGridView();
        RefreshGridSizeVisual();
        if (topologyResized)
            NotifyTopologyChanged();
    }

    private void Update()
    {
        if (wireViewsDirty)
            RefreshWireViews();

        if (Application.isPlaying &&
            drawGridInGameView &&
            runtimeGridObject == null)
        {
            RefreshRuntimeGridView();
        }
        else
        {
            UpdateRuntimeGridVisibility();
        }

        if (!Application.isPlaying || !WirePlacementEnabled)
        {
            ResetWireDrag();
            ClearWireHoverVisual();
            return;
        }

        Mouse mouse = Mouse.current;
        bool hasHoverTarget = TryGetWireHoverTarget(mouse, out WireHoverTarget hoverTarget);
        HandleWireInput(mouse, hasHoverTarget, hoverTarget);
        UpdateWireHoverVisual(hasHoverTarget, hoverTarget);
    }

    /// <summary>
    /// Enables or blocks player wire editing without changing the board's
    /// inspector-level wire placement configuration.
    /// </summary>
    public void SetGameplayInputEnabled(bool enabled)
    {
        if (gameplayInputEnabled == enabled)
            return;

        gameplayInputEnabled = enabled;
        if (!gameplayInputEnabled)
        {
            ResetWireDrag();
            ClearWireHoverVisual();
        }
    }

    /// <summary>
    /// Converts a world position to a logical grid coordinate. Positions outside
    /// the board return out-of-range coordinates; use IsInside or TryWorldToCell
    /// when bounds checking is required.
    /// </summary>
    public Vector2Int WorldToCell(Vector3 worldPosition)
    {
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        Vector3 bottomLeft = GetLocalBottomLeft();

        return new Vector2Int(
            Mathf.FloorToInt((localPosition.x - bottomLeft.x) / cellSize),
            Mathf.FloorToInt((localPosition.z - bottomLeft.z) / cellSize));
    }

    /// <summary>
    /// Returns the world-space center of a logical cell on this object's local XZ plane.
    /// </summary>
    public Vector3 CellToWorld(Vector2Int cell)
    {
        Vector3 bottomLeft = GetLocalBottomLeft();
        Vector3 localCenter = bottomLeft + new Vector3(
            (cell.x + 0.5f) * cellSize,
            0f,
            (cell.y + 0.5f) * cellSize);

        return transform.TransformPoint(localCenter);
    }

    public bool TryWorldToCell(Vector3 worldPosition, out Vector2Int cell)
    {
        cell = WorldToCell(worldPosition);
        return IsInside(cell);
    }

    public bool IsInside(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < width &&
               cell.y >= 0 && cell.y < height;
    }

    /// <summary>
    /// Resizes the board to a square grid at runtime.
    /// </summary>
    public void SetSize(int size)
    {
        SetSize(size, size);
    }

    /// <summary>
    /// Resizes the logical grid and refreshes its runtime storage and views.
    /// Existing content should be cleared before shrinking the board.
    /// </summary>
    public void SetSize(int newWidth, int newHeight)
    {
        newWidth = Mathf.Max(1, newWidth);
        newHeight = Mathf.Max(1, newHeight);

        if (width == newWidth && height == newHeight)
        {
            RefreshRuntimeGridView();
            RefreshGridSizeVisual();
            return;
        }

        width = newWidth;
        height = newHeight;
        occupancyDirty = true;
        wireViewsDirty = true;

        RebuildCells();
        EnsureWireStorage();
        EnsureOccupancy();
        RefreshWireViews();
        RefreshRuntimeGridView();
        RefreshGridSizeVisual();
        NotifyTopologyChanged();
    }

    /// <summary>
    /// Gets the coordinate stored at a logical grid index.
    /// </summary>
    public Vector2Int GetCell(int x, int y)
    {
        if (x < 0 || x >= width)
            throw new ArgumentOutOfRangeException(nameof(x));

        if (y < 0 || y >= height)
            throw new ArgumentOutOfRangeException(nameof(y));

        EnsureCells();
        return cells[x, y];
    }

    /// <summary>
    /// Returns the component occupying a cell, or null when the cell is empty or outside the board.
    /// </summary>
    public BoardComponent GetOccupant(Vector2Int cell)
    {
        if (!IsInside(cell))
            return null;

        EnsureOccupancy();
        return occupants[cell.x, cell.y];
    }

    public bool TryGetOccupant(Vector2Int cell, out BoardComponent occupant)
    {
        occupant = GetOccupant(cell);
        return occupant != null;
    }

    /// <summary>
    /// Finds a component port on a specific logical cell edge.
    /// </summary>
    public bool TryGetPortAtEdge(
        Vector2Int componentCell,
        BoardPortDirection facingDirection,
        out BoardComponent component,
        out BoardPort port)
    {
        component = GetOccupant(componentCell);
        if (component != null &&
            component.TryGetPortAtGridEdge(componentCell, facingDirection, out port))
        {
            return true;
        }

        port = null;
        return false;
    }

    /// <summary>
    /// Returns the component port reached by one of a wire cell's terminal flags.
    /// </summary>
    public bool TryGetConnectedPort(
        Vector2Int wireCell,
        WireConnection connection,
        out BoardComponent component,
        out BoardPort port)
    {
        WireCell wire = GetWire(wireCell);
        if (wire == null ||
            !wire.HasConnection(connection) ||
            !WireConnectionUtility.TryToPortDirection(
                WireConnectionUtility.GetOpposite(connection),
                out BoardPortDirection portDirection))
        {
            component = null;
            port = null;
            return false;
        }

        Vector2Int componentCell =
            wireCell + WireConnectionUtility.ToCellOffset(connection);
        return TryGetPortAtEdge(componentCell, portDirection, out component, out port);
    }

    /// <summary>
    /// Enumerates cells containing a placed wire center.
    /// </summary>
    public IEnumerable<Vector2Int> WireCells
    {
        get
        {
            EnsureWireStorage();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (HasWireInternal(new Vector2Int(x, y)))
                        yield return new Vector2Int(x, y);
                }
            }
        }
    }

    public WireCell GetWire(Vector2Int cell)
    {
        if (!IsInside(cell))
            return null;

        EnsureWireStorage();
        return wires[cell.x, cell.y];
    }

    public WireConnection GetWireConnections(Vector2Int cell)
    {
        WireCell wire = GetWire(cell);
        return wire != null ? wire.Connections : WireConnection.None;
    }

    public bool HasWire(Vector2Int cell)
    {
        WireCell wire = GetWire(cell);
        return wire != null && wire.HasCenter;
    }

    /// <summary>
    /// Returns the visual representing an occupied wire cell, or null when the
    /// cell has no wire (or no WireView prefab is available).
    /// </summary>
    public WireView GetWireView(Vector2Int cell)
    {
        if (wireViewsDirty)
            RefreshWireViews();

        wireViews.TryGetValue(cell, out WireView view);
        return view;
    }

    public bool CanPlaceWire(Vector2Int cell)
    {
        return IsInside(cell) && GetOccupant(cell) == null;
    }

    public bool TryPlaceWireCenter(Vector2Int cell)
    {
        if (!CanPlaceWire(cell) || HasWire(cell))
            return false;

        EnsureOccupancy();
        EnsureWireStorage();
        wires[cell.x, cell.y] ??= new WireCell();

        bool changed = wires[cell.x, cell.y].AddCenter();
        changed |= AddFacingPortConnections(cell);
        if (changed)
            NotifyWireChanged(cell);

        return changed;
    }

    private bool CanAddWireConnection(Vector2Int from, Vector2Int to)
    {
        if (!WireConnectionUtility.TryGetConnection(
                from,
                to,
                out WireConnection fromConnection,
                out WireConnection toConnection) ||
            !IsInside(from) ||
            !IsInside(to))
        {
            return false;
        }

        EnsureOccupancy();
        EnsureWireStorage();

        BoardComponent fromComponent = occupants[from.x, from.y];
        BoardComponent toComponent = occupants[to.x, to.y];
        if (fromComponent != null && toComponent != null)
            return false;

        if (fromComponent == null && toComponent == null)
            return true;

        bool componentIsFrom = fromComponent != null;
        Vector2Int componentCell = componentIsFrom ? from : to;
        Vector2Int wireCell = componentIsFrom ? to : from;
        WireConnection componentFacingWire = componentIsFrom
            ? fromConnection
            : toConnection;

        return CanPlaceWire(wireCell) &&
               TryGetPortAtEdge(
                   componentCell,
                   componentFacingWire,
                   out _,
                   out _);
    }

    /// <summary>
    /// Adds an edge between orthogonally neighboring cells. Wire-to-wire edges
    /// are reciprocal; a wire-to-port edge stores only the wire-side flag.
    /// Missing endpoint centers are created as part of placing the side. New
    /// centers also connect to any adjacent component ports.
    /// </summary>
    public bool TryAddWireConnection(Vector2Int from, Vector2Int to)
    {
        return TryAddWireConnection(from, to, out _);
    }

    public bool TryAddWireConnection(
        Vector2Int from,
        Vector2Int to,
        out string failureReason)
    {
        if (!WireConnectionUtility.TryGetConnection(
                from,
                to,
                out WireConnection fromConnection,
                out WireConnection toConnection))
        {
            failureReason = "Wire connections require two orthogonally neighboring cells.";
            return false;
        }

        if (!IsInside(from) || !IsInside(to))
        {
            failureReason = "Wire connections must stay inside the board.";
            return false;
        }

        EnsureOccupancy();
        EnsureWireStorage();

        BoardComponent fromComponent = occupants[from.x, from.y];
        BoardComponent toComponent = occupants[to.x, to.y];

        if (fromComponent != null && toComponent != null)
        {
            failureReason = "A wire edge requires at least one non-component cell.";
            return false;
        }

        if (fromComponent != null || toComponent != null)
        {
            bool componentIsFrom = fromComponent != null;
            Vector2Int componentCell = componentIsFrom ? from : to;
            Vector2Int wireCell = componentIsFrom ? to : from;
            WireConnection componentFacingWire = componentIsFrom
                ? fromConnection
                : toConnection;
            WireConnection wireFacingComponent = componentIsFrom
                ? toConnection
                : fromConnection;

            if (!CanPlaceWire(wireCell) ||
                !TryGetPortAtEdge(
                    componentCell,
                    componentFacingWire,
                    out _,
                    out _))
            {
                failureReason =
                    $"The component at {componentCell} has no port facing cell {wireCell}.";
                return false;
            }

            wires[wireCell.x, wireCell.y] ??= new WireCell();
            bool wireChanged = wires[wireCell.x, wireCell.y].AddCenter();
            wireChanged |=
                wires[wireCell.x, wireCell.y].AddConnection(wireFacingComponent);
            wireChanged |= AddFacingPortConnections(wireCell);
            if (wireChanged)
                NotifyWireChanged(wireCell);

            failureReason = string.Empty;
            return true;
        }

        wires[from.x, from.y] ??= new WireCell();
        wires[to.x, to.y] ??= new WireCell();

        bool fromChanged = wires[from.x, from.y].AddCenter();
        bool toChanged = wires[to.x, to.y].AddCenter();
        fromChanged |= wires[from.x, from.y].AddConnection(fromConnection);
        toChanged |= wires[to.x, to.y].AddConnection(toConnection);
        fromChanged |= AddFacingPortConnections(from);
        toChanged |= AddFacingPortConnections(to);

        if (fromChanged)
            NotifyWireChanged(from);

        if (toChanged)
            NotifyWireChanged(to);

        failureReason = string.Empty;
        return true;
    }

    /// <summary>
    /// Removes only the requested shared edge. This handles reciprocal wire
    /// edges and one-sided component-port terminals while preserving other flags.
    /// </summary>
    public bool TryRemoveWireConnection(Vector2Int from, Vector2Int to)
    {
        if (!WireConnectionUtility.TryGetConnection(
                from,
                to,
                out WireConnection fromConnection,
                out WireConnection toConnection) ||
            !IsInside(from) ||
            !IsInside(to))
        {
            return false;
        }

        EnsureWireStorage();
        bool changed = false;

        WireCell fromWire = wires[from.x, from.y];
        if (fromWire != null && fromWire.RemoveConnection(fromConnection))
        {
            changed = true;
            RemoveEmptyWire(from);
            NotifyWireChanged(from);
        }

        WireCell toWire = wires[to.x, to.y];
        if (toWire != null && toWire.RemoveConnection(toConnection))
        {
            changed = true;
            RemoveEmptyWire(to);
            NotifyWireChanged(to);
        }

        return changed;
    }

    public bool ClearWire(Vector2Int cell)
    {
        if (!IsInside(cell))
            return false;

        EnsureWireStorage();
        WireCell wire = wires[cell.x, cell.y];
        if (wire == null)
            return false;

        WireConnection connections = wire.Connections;
        foreach (WireConnection connection in CardinalWireConnections)
        {
            if ((connections & connection) == 0)
                continue;

            Vector2Int neighbor = cell + WireConnectionUtility.ToCellOffset(connection);
            if (!IsInside(neighbor))
                continue;

            WireCell neighborWire = wires[neighbor.x, neighbor.y];
            if (neighborWire == null)
                continue;

            neighborWire.RemoveConnection(WireConnectionUtility.GetOpposite(connection));
            RemoveEmptyWire(neighbor);
            NotifyWireChanged(neighbor);
        }

        wires[cell.x, cell.y] = null;
        NotifyWireChanged(cell);
        return true;
    }

    public void ClearAllWires()
    {
        EnsureWireStorage();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (wires[x, y] == null)
                    continue;

                wires[x, y] = null;
                NotifyWireChanged(new Vector2Int(x, y));
            }
        }
    }

    /// <summary>
    /// Reconciles port-facing terminal flags after component ports or placements
    /// change. This never creates connections between neighboring wire cells.
    /// </summary>
    public void RefreshWireConnections()
    {
        EnsureWireStorage();
        EnsureOccupancy();
        RemoveBrokenWireConnections(true);
        AddMissingPortConnections(true);
        NotifyTopologyChanged();
    }

    public bool CanPlaceComponent(
        BoardComponent component,
        Vector2Int gridPosition,
        int rotationSteps)
    {
        return CanPlaceComponent(component, gridPosition, rotationSteps, out _);
    }

    /// <summary>
    /// Checks whether a component's 2x2 footprint fits without changing current occupancy.
    /// Cells already occupied by the same component are ignored so moves are atomic.
    /// </summary>
    public bool CanPlaceComponent(
        BoardComponent component,
        Vector2Int gridPosition,
        int rotationSteps,
        out string failureReason)
    {
        EnsureOccupancy();
        EnsureWireStorage();
        return CanPlaceComponentInternal(component, gridPosition, out failureReason);
    }

    /// <summary>
    /// Checks component bounds and occupancy while allowing wires in the target
    /// footprint. Use this before TryPlaceComponentClearingWires when a placement
    /// is allowed to replace existing wiring.
    /// </summary>
    public bool CanPlaceComponentAfterClearingWires(
        BoardComponent component,
        Vector2Int gridPosition,
        int rotationSteps,
        out string failureReason)
    {
        EnsureOccupancy();
        EnsureWireStorage();
        return CanPlaceComponentInternal(
            component,
            gridPosition,
            out failureReason,
            allowWiresInFootprint: true);
    }

    /// <summary>
    /// Places or moves a component. Failed moves leave its previous placement unchanged.
    /// </summary>
    public bool TryPlaceComponent(
        BoardComponent component,
        Vector2Int gridPosition,
        int rotationSteps)
    {
        return TryPlaceComponent(component, gridPosition, rotationSteps, out _);
    }

    public bool TryPlaceComponent(
        BoardComponent component,
        Vector2Int gridPosition,
        int rotationSteps,
        out string failureReason)
    {
        if (component == null)
        {
            failureReason = "A BoardComponent reference is required.";
            return false;
        }

        if (component.gameObject.scene != gameObject.scene)
        {
            failureReason =
                "A BoardComponent and GridBoard must belong to the same scene or prefab stage.";
            return false;
        }

        EnsureOccupancy();
        EnsureWireStorage();

        if (!CanPlaceComponentInternal(component, gridPosition, out failureReason))
            return false;

        if (component.Board != null && component.Board != this)
            component.Board.RemoveComponent(component);

        ClearOccupiedCells(component);
        OccupyCells(component, gridPosition);

        int normalizedRotation = NormalizeRotationSteps(rotationSteps);
        Transform targetParent = EnsureComponentsRoot();
        component.ApplyPlacementFromBoard(
            this,
            gridPosition,
            normalizedRotation,
            targetParent);

        RemoveBrokenWireConnections(true);
        AddMissingPortConnections(true);
        NotifyTopologyChanged();

        return true;
    }

    /// <summary>
    /// Places a component after deleting wires in its 2x2 footprint. Bounds,
    /// scene ownership, and component overlap are validated before any wire is
    /// changed, so a rejected placement leaves the board untouched.
    /// </summary>
    public bool TryPlaceComponentClearingWires(
        BoardComponent component,
        Vector2Int gridPosition,
        int rotationSteps,
        out string failureReason)
    {
        if (component == null)
        {
            failureReason = "A BoardComponent reference is required.";
            return false;
        }

        if (component.gameObject.scene != gameObject.scene)
        {
            failureReason =
                "A BoardComponent and GridBoard must belong to the same scene or prefab stage.";
            return false;
        }

        EnsureOccupancy();
        EnsureWireStorage();
        if (!CanPlaceComponentInternal(
                component,
                gridPosition,
                out failureReason,
                allowWiresInFootprint: true))
        {
            return false;
        }

        for (int y = 0; y < BoardComponent.FootprintHeight; y++)
        {
            for (int x = 0; x < BoardComponent.FootprintWidth; x++)
                ClearWire(gridPosition + new Vector2Int(x, y));
        }

        return TryPlaceComponent(
            component,
            gridPosition,
            rotationSteps,
            out failureReason);
    }

    /// <summary>
    /// Clears this component from the occupancy grid without destroying its GameObject.
    /// </summary>
    public void RemoveComponent(BoardComponent component)
    {
        if (component == null)
            return;

        EnsureOccupancy();
        ClearOccupiedCells(component);
        component.SetPlacementValidity(false);
        RemoveBrokenWireConnections(true);
        NotifyTopologyChanged();
    }

    /// <summary>
    /// Rebuilds occupancy from BoardComponent objects below Display/Components.
    /// Useful after changing board dimensions or editing the hierarchy.
    /// </summary>
    public void RefreshPlacements()
    {
        EnsureComponentsRoot();
        occupancyDirty = true;
        EnsureOccupancy();
        RemoveBrokenWireConnections(true);
        AddMissingPortConnections(true);
        NotifyTopologyChanged();
    }

    /// <summary>
    /// Finds or creates the required Display/Components hierarchy container.
    /// </summary>
    public Transform EnsureComponentsRoot()
    {
        Transform display = ResolveDisplayRoot();
        Transform existing = ResolveComponentsRoot();
        if (existing != null)
            return existing;

        GameObject container = new(ComponentsObjectName);
        componentsRoot = container.transform;
        componentsRoot.SetParent(display, false);
        occupancyDirty = true;
        return componentsRoot;
    }

    internal Vector3 GetFootprintCenterWorld(Vector2Int gridPosition, Vector2Int footprintSize)
    {
        Vector2Int oppositeCell = gridPosition + footprintSize - Vector2Int.one;
        return (CellToWorld(gridPosition) + CellToWorld(oppositeCell)) * 0.5f;
    }

    internal static int NormalizeRotationSteps(int rotationSteps)
    {
        int normalized = rotationSteps % 4;
        return normalized < 0 ? normalized + 4 : normalized;
    }

    private void EnsureCells()
    {
        if (cells == null ||
            cells.GetLength(0) != width ||
            cells.GetLength(1) != height)
        {
            RebuildCells();
        }
    }

    private void RebuildCells()
    {
        cells = new Vector2Int[width, height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                cells[x, y] = new Vector2Int(x, y);
        }
    }

    private bool EnsureWireStorage()
    {
        if (wires != null &&
            wires.GetLength(0) == width &&
            wires.GetLength(1) == height)
        {
            return false;
        }

        WireCell[,] previousWires = wires;
        WireCell[,] resizedWires = new WireCell[width, height];

        if (previousWires != null)
        {
            int copyWidth = Mathf.Min(width, previousWires.GetLength(0));
            int copyHeight = Mathf.Min(height, previousWires.GetLength(1));

            for (int y = 0; y < copyHeight; y++)
            {
                for (int x = 0; x < copyWidth; x++)
                    resizedWires[x, y] = previousWires[x, y];
            }
        }

        wires = resizedWires;
        RemoveBrokenWireConnections();
        wireViewsDirty = true;
        return true;
    }

    private void RemoveBrokenWireConnections(bool notifyChanges = false)
    {
        List<Vector2Int> changedCells = notifyChanges
            ? new List<Vector2Int>()
            : null;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2Int cell = new(x, y);
                WireCell wire = wires[x, y];
                if (wire == null)
                    continue;

                bool cellChanged = false;

                foreach (WireConnection connection in CardinalWireConnections)
                {
                    if (!wire.HasConnection(connection))
                        continue;

                    Vector2Int neighbor = cell + WireConnectionUtility.ToCellOffset(connection);
                    WireConnection opposite = WireConnectionUtility.GetOpposite(connection);
                    bool hasReciprocalWire =
                        IsInside(neighbor) &&
                        wires[neighbor.x, neighbor.y] != null &&
                        wires[neighbor.x, neighbor.y].HasConnection(opposite);
                    bool hasFacingPort =
                        IsInside(neighbor) &&
                        TryGetPortAtEdge(neighbor, opposite, out _, out _);

                    if (!hasReciprocalWire && !hasFacingPort)
                        cellChanged |= wire.RemoveConnection(connection);
                }

                RemoveEmptyWire(cell);
                if (cellChanged)
                    changedCells?.Add(cell);
            }
        }

        if (changedCells == null)
            return;

        foreach (Vector2Int changedCell in changedCells)
            NotifyWireChanged(changedCell);
    }

    private void AddMissingPortConnections(bool notifyChanges)
    {
        List<Vector2Int> changedCells = notifyChanges
            ? new List<Vector2Int>()
            : null;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2Int cell = new(x, y);
                if (AddFacingPortConnections(cell))
                    changedCells?.Add(cell);
            }
        }

        if (changedCells == null)
            return;

        foreach (Vector2Int changedCell in changedCells)
            NotifyWireChanged(changedCell);
    }

    private bool AddFacingPortConnections(Vector2Int wireCell)
    {
        if (!IsInside(wireCell) || occupants[wireCell.x, wireCell.y] != null)
            return false;

        WireCell wire = wires[wireCell.x, wireCell.y];
        if (wire == null || !wire.HasCenter)
            return false;

        bool changed = false;
        foreach (WireConnection connection in CardinalWireConnections)
        {
            Vector2Int componentCell =
                wireCell + WireConnectionUtility.ToCellOffset(connection);

            if (IsInside(componentCell) &&
                TryGetPortAtEdge(
                    componentCell,
                    WireConnectionUtility.GetOpposite(connection),
                    out _,
                    out _))
            {
                changed |= wire.AddConnection(connection);
            }
        }

        return changed;
    }

    private bool TryGetPortAtEdge(
        Vector2Int componentCell,
        WireConnection facingConnection,
        out BoardComponent component,
        out BoardPort port)
    {
        if (!WireConnectionUtility.TryToPortDirection(
                facingConnection,
                out BoardPortDirection facingDirection))
        {
            component = null;
            port = null;
            return false;
        }

        return TryGetPortAtEdge(
            componentCell,
            facingDirection,
            out component,
            out port);
    }

    private bool HasWireInternal(Vector2Int cell)
    {
        if (wires == null || !IsInside(cell))
            return false;

        WireCell wire = wires[cell.x, cell.y];
        return wire != null && wire.HasCenter;
    }

    private void RemoveEmptyWire(Vector2Int cell)
    {
        WireCell wire = wires[cell.x, cell.y];
        if (wire != null && !wire.HasAnyPart)
            wires[cell.x, cell.y] = null;
    }

    /// <summary>
    /// Reconciles generated views with the logical wire grid. Existing views are
    /// reused, so changing a junction only toggles its prefab arms.
    /// </summary>
    public void RefreshWireViews()
    {
        EnsureWireStorage();
        wireViewsDirty = false;
        wireViews.Clear();

        bool hasAnyWire = false;
        for (int y = 0; y < height && !hasAnyWire; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!HasWireInternal(new Vector2Int(x, y)))
                    continue;

                hasAnyWire = true;
                break;
            }
        }

        Transform root = ResolveWireViewsRoot(hasAnyWire);
        if (root == null)
            return;

        WireView[] existingViews = root.GetComponentsInChildren<WireView>(true);
        foreach (WireView view in existingViews)
        {
            if (view == null ||
                !HasWireInternal(view.GridCell) ||
                wireViews.ContainsKey(view.GridCell))
            {
                DestroyWireView(view);
                continue;
            }

            wireViews.Add(view.GridCell, view);
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector2Int cell = new(x, y);
                if (HasWireInternal(cell))
                    SyncWireView(cell);
            }
        }
    }

    private void NotifyWireChanged(Vector2Int cell)
    {
        if (wireViewsDirty)
            RefreshWireViews();
        else
            SyncWireView(cell);

        NotifyTopologyChanged();
        WireChanged?.Invoke(cell);
    }

    private void NotifyTopologyChanged()
    {
        unchecked
        {
            topologyRevision++;
        }

        TopologyChanged?.Invoke();
    }

    private void SyncWireView(Vector2Int cell)
    {
        if (!HasWireInternal(cell))
        {
            if (wireViews.Remove(cell, out WireView obsoleteView))
                DestroyWireView(obsoleteView);

            return;
        }

        if (!wireViews.TryGetValue(cell, out WireView view) || view == null)
        {
            WireView prefab = ResolveWireViewPrefab();
            Transform root = ResolveWireViewsRoot(prefab != null);
            if (prefab == null || root == null)
                return;

            view = Instantiate(prefab, root);
            view.name = $"Wire ({cell.x}, {cell.y})";
            wireViews[cell] = view;
        }

        Vector3 localCenter = GetLocalBottomLeft() + new Vector3(
            (cell.x + 0.5f) * cellSize,
            wireViewHeight * cellSize,
            (cell.y + 0.5f) * cellSize);

        Transform viewTransform = view.transform;
        viewTransform.SetParent(ResolveWireViewsRoot(true), false);
        viewTransform.localPosition = localCenter;
        viewTransform.localRotation = Quaternion.identity;
        viewTransform.localScale = Vector3.one * cellSize;
        if (Application.isPlaying)
            view.SetPointerInteractionEnabled(false);
        view.SetState(cell, HasWire(cell), GetWireConnections(cell));
    }

    private WireView ResolveWireViewPrefab()
    {
        return wireViewPrefab != null
            ? wireViewPrefab
            : Resources.Load<WireView>(WireViewResourceName);
    }

    private Transform ResolveWireViewsRoot(bool createIfMissing)
    {
        if (wireViewsRoot != null &&
            wireViewsRoot.parent == transform &&
            wireViewsRoot.name == WireViewsObjectName)
        {
            return wireViewsRoot;
        }

        Transform existing = transform.Find(WireViewsObjectName);
        if (existing != null)
        {
            wireViewsRoot = existing;
            return existing;
        }

        if (!createIfMissing)
            return null;

        GameObject rootObject = new(WireViewsObjectName);
        wireViewsRoot = rootObject.transform;
        wireViewsRoot.SetParent(transform, false);
        return wireViewsRoot;
    }

    private static void DestroyWireView(WireView view)
    {
        if (view == null)
            return;

        view.gameObject.SetActive(false);
        if (Application.isPlaying)
            Destroy(view.gameObject);
        else
            DestroyImmediate(view.gameObject);
    }

    private void EnsureOccupancy()
    {
        EnsureCells();

        if (occupants == null ||
            occupants.GetLength(0) != width ||
            occupants.GetLength(1) != height ||
            occupancyDirty)
        {
            RebuildOccupancy();
        }
    }

    private void RebuildOccupancy()
    {
        if (rebuildingOccupancy)
            return;

        rebuildingOccupancy = true;
        occupants = new BoardComponent[width, height];
        occupancyDirty = false;

        Transform root = ResolveComponentsRoot();
        if (root != null)
        {
            BoardComponent[] components = root.GetComponentsInChildren<BoardComponent>(true);
            foreach (BoardComponent component in components)
            {
                if (component.Board != this)
                    continue;

                component.SetPlacementValidity(false);

                if (!CanPlaceComponentInternal(component, component.GridPosition, out _))
                    continue;

                OccupyCells(component, component.GridPosition);
                component.ApplyPlacementFromBoard(
                    this,
                    component.GridPosition,
                    component.RotationSteps,
                    root);
            }
        }

        rebuildingOccupancy = false;
    }

    private bool CanPlaceComponentInternal(
        BoardComponent component,
        Vector2Int gridPosition,
        out string failureReason,
        bool allowWiresInFootprint = false)
    {
        if (component == null)
        {
            failureReason = "A BoardComponent reference is required.";
            return false;
        }

        for (int y = 0; y < BoardComponent.FootprintHeight; y++)
        {
            for (int x = 0; x < BoardComponent.FootprintWidth; x++)
            {
                Vector2Int cell = gridPosition + new Vector2Int(x, y);
                if (!IsInside(cell))
                {
                    failureReason =
                        $"The 2x2 footprint at {gridPosition} extends outside the board.";
                    return false;
                }

                BoardComponent occupant = occupants[cell.x, cell.y];
                if (occupant != null && occupant != component)
                {
                    failureReason =
                        $"Cell {cell} is already occupied by {occupant.name}.";
                    return false;
                }

                if (!allowWiresInFootprint && HasWireInternal(cell))
                {
                    failureReason = $"Cell {cell} already contains a wire.";
                    return false;
                }
            }
        }

        failureReason = string.Empty;
        return true;
    }

    private void OccupyCells(BoardComponent component, Vector2Int gridPosition)
    {
        for (int y = 0; y < BoardComponent.FootprintHeight; y++)
        {
            for (int x = 0; x < BoardComponent.FootprintWidth; x++)
                occupants[gridPosition.x + x, gridPosition.y + y] = component;
        }
    }

    private void ClearOccupiedCells(BoardComponent component)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (occupants[x, y] == component)
                    occupants[x, y] = null;
            }
        }
    }

    private Transform ResolveDisplayRoot()
    {
        if (displayRoot != null)
            return displayRoot;

        for (Transform current = transform; current != null; current = current.parent)
        {
            if (current.name == DisplayObjectName)
                return current;
        }

        if (transform.parent != null)
        {
            Transform siblingDisplay = transform.parent.Find(DisplayObjectName);
            if (siblingDisplay != null)
                return siblingDisplay;
        }

        return transform;
    }

    private Transform ResolveComponentsRoot()
    {
        Transform display = ResolveDisplayRoot();

        if (componentsRoot != null &&
            componentsRoot.parent == display &&
            componentsRoot.name == ComponentsObjectName)
        {
            return componentsRoot;
        }

        Transform existing = display.Find(ComponentsObjectName);
        if (existing != null)
            componentsRoot = existing;

        return existing;
    }

    private void HandleWireInput(
        Mouse mouse,
        bool hasHoverTarget,
        WireHoverTarget hoverTarget)
    {
        if (mouse == null)
        {
            ResetWireDrag();
            return;
        }

        if (hasHoverTarget)
            ApplyWireClick(mouse, hoverTarget);

        // Removal is driven by the precise center/edge hover target every frame.
        // Do not also run the older cell-transition drag path, which can remove
        // an edge that the cursor did not directly pass over.
        if (mouse.rightButton.isPressed)
        {
            ResetWireDrag();
            return;
        }

        WireEditMode requestedMode = GetRequestedWireEditMode(mouse);

        if (requestedMode == WireEditMode.None)
        {
            ResetWireDrag();
            return;
        }

        if (requestedMode != activeWireEditMode)
        {
            activeWireEditMode = requestedMode;
            hasPreviousWireDragCell = false;
        }

        if (!TryGetWireCellUnderCursor(mouse, out Vector2Int currentCell))
        {
            hasPreviousWireDragCell = false;
            return;
        }

        if (!hasPreviousWireDragCell)
        {
            previousWireDragCell = currentCell;
            hasPreviousWireDragCell = true;
            return;
        }

        if (currentCell == previousWireDragCell)
            return;

        ApplyWireDrag(previousWireDragCell, currentCell);

        // Straight cursor jumps are expanded into neighboring edges. A diagonal
        // jump is ambiguous, so the current cell becomes a fresh anchor.
        previousWireDragCell = currentCell;
    }

    private void ApplyWireClick(Mouse mouse, WireHoverTarget hoverTarget)
    {
        if (mouse.rightButton.isPressed)
        {
            if (hoverTarget.Type == WireHoverTargetType.Center)
                ClearWire(hoverTarget.From);
            else if (hoverTarget.Type == WireHoverTargetType.Edge)
                TryRemoveWireConnection(hoverTarget.From, hoverTarget.To);

            return;
        }

        if (mouse.leftButton.wasPressedThisFrame &&
            hoverTarget.Type == WireHoverTargetType.Center)
        {
            TryPlaceWireCenter(hoverTarget.From);
        }
        else if (mouse.leftButton.wasPressedThisFrame &&
                 hoverTarget.Type == WireHoverTargetType.Edge &&
                 !HasWireEdge(hoverTarget.From, hoverTarget.To) &&
                 CanAddWireConnection(hoverTarget.From, hoverTarget.To))
        {
            TryAddWireConnection(hoverTarget.From, hoverTarget.To);
        }
    }

    private void ApplyWireDrag(Vector2Int from, Vector2Int to)
    {
        Vector2Int difference = to - from;
        if (difference.x != 0 && difference.y != 0)
            return;

        int distance = Mathf.Abs(difference.x) + Mathf.Abs(difference.y);
        if (distance == 0)
            return;

        Vector2Int step = new(
            Math.Sign(difference.x),
            Math.Sign(difference.y));
        Vector2Int segmentStart = from;

        for (int index = 0; index < distance; index++)
        {
            Vector2Int segmentEnd = segmentStart + step;
            if (!TryAddWireConnection(segmentStart, segmentEnd))
                break;

            segmentStart = segmentEnd;
        }
    }

    private WireEditMode GetRequestedWireEditMode(Mouse mouse)
    {
        if (mouse == null)
            return WireEditMode.None;

        if (mouse.leftButton.isPressed)
            return WireEditMode.Add;

        return WireEditMode.None;
    }

    private bool TryGetWireCellUnderCursor(Mouse mouse, out Vector2Int cell)
    {
        if (!TryGetWireWorldPointUnderCursor(mouse, out Vector3 worldPoint))
        {
            cell = default;
            return false;
        }

        return TryWorldToCell(worldPoint, out cell);
    }

    private bool TryGetWireWorldPointUnderCursor(
        Mouse mouse,
        out Vector3 worldPoint)
    {
        Camera cameraToUse = wirePlacementCamera != null
            ? wirePlacementCamera
            : Camera.main;
        if (mouse == null || cameraToUse == null)
        {
            worldPoint = default;
            return false;
        }

        Ray mouseRay = cameraToUse.ScreenPointToRay(mouse.position.ReadValue());
        Plane boardPlane = new(transform.up, transform.position);
        if (!boardPlane.Raycast(mouseRay, out float distance))
        {
            worldPoint = default;
            return false;
        }

        worldPoint = mouseRay.GetPoint(distance);
        return true;
    }

    private bool TryGetWireHoverTarget(
        Mouse mouse,
        out WireHoverTarget hoverTarget)
    {
        hoverTarget = default;
        if (!TryGetWireWorldPointUnderCursor(mouse, out Vector3 worldPoint))
            return false;

        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        Vector3 bottomLeft = GetLocalBottomLeft();
        float gridX = (localPoint.x - bottomLeft.x) / cellSize;
        float gridY = (localPoint.z - bottomLeft.z) / cellSize;
        if (gridX < 0f || gridX >= width || gridY < 0f || gridY >= height)
            return false;

        Vector2Int cell = new(
            Mathf.FloorToInt(gridX),
            Mathf.FloorToInt(gridY));
        float offsetX = gridX - (cell.x + 0.5f);
        float offsetY = gridY - (cell.y + 0.5f);
        float absoluteX = Mathf.Abs(offsetX);
        float absoluteY = Mathf.Abs(offsetY);

        if (absoluteX <= wireCenterHoverRadius &&
            absoluteY <= wireCenterHoverRadius)
        {
            hoverTarget = WireHoverTarget.Center(cell);
            return true;
        }

        Vector2Int direction = absoluteX >= absoluteY
            ? new Vector2Int(offsetX >= 0f ? 1 : -1, 0)
            : new Vector2Int(0, offsetY >= 0f ? 1 : -1);
        Vector2Int neighbor = cell + direction;
        if (IsInside(neighbor))
        {
            hoverTarget = WireHoverTarget.Edge(cell, neighbor);
            return true;
        }

        // The outer half of a boundary cell has no edge between two centers.
        hoverTarget = WireHoverTarget.Center(cell);
        return true;
    }

    private bool HasWireEdge(Vector2Int from, Vector2Int to)
    {
        if (!WireConnectionUtility.TryGetConnection(
                from,
                to,
                out WireConnection fromConnection,
                out WireConnection toConnection))
        {
            return false;
        }

        WireCell fromWire = GetWire(from);
        WireCell toWire = GetWire(to);
        return (fromWire != null && fromWire.HasConnection(fromConnection)) ||
               (toWire != null && toWire.HasConnection(toConnection));
    }

    private void UpdateWireHoverVisual(
        bool hasHoverTarget,
        WireHoverTarget hoverTarget)
    {
        ClearInteractionHighlightVisuals();

        if (!hasHoverTarget)
        {
            HideWirePreviews();
            return;
        }

        if (hoverTarget.Type == WireHoverTargetType.Center)
        {
            if (HasWire(hoverTarget.From))
            {
                HideWirePreviews();
                HighlightWirePart(
                    hoverTarget.From,
                    true,
                    WireConnection.None);
            }
            else if (CanPlaceWire(hoverTarget.From))
            {
                ShowCenterWirePreview(hoverTarget.From);
            }
            else
            {
                HideWirePreviews();
            }

            return;
        }

        if (hoverTarget.Type != WireHoverTargetType.Edge)
        {
            HideWirePreviews();
            return;
        }

        if (HasWireEdge(hoverTarget.From, hoverTarget.To))
        {
            HideWirePreviews();
            HighlightWireEdge(hoverTarget.From, hoverTarget.To);
        }
        else if (CanAddWireConnection(hoverTarget.From, hoverTarget.To))
        {
            ShowWireEdgePreview(hoverTarget.From, hoverTarget.To);
        }
        else
        {
            HideWirePreviews();
        }
    }

    private void HighlightWireEdge(Vector2Int from, Vector2Int to)
    {
        if (!WireConnectionUtility.TryGetConnection(
                from,
                to,
                out WireConnection fromConnection,
                out WireConnection toConnection))
        {
            return;
        }

        WireCell fromWire = GetWire(from);
        if (fromWire != null && fromWire.HasConnection(fromConnection))
            HighlightWirePart(from, false, fromConnection);

        WireCell toWire = GetWire(to);
        if (toWire != null && toWire.HasConnection(toConnection))
            HighlightWirePart(to, false, toConnection);
    }

    private void HighlightWirePart(
        Vector2Int cell,
        bool highlightCenter,
        WireConnection highlightedArms)
    {
        WireView view = GetWireView(cell);
        if (view == null)
            return;

        view.SetPointerInteractionEnabled(false);
        view.SetInteractionHighlight(
            highlightCenter,
            highlightedArms,
            wireDeleteHighlightColor);
        interactionHighlightedWireViews.Add(view);
    }

    private void ClearInteractionHighlightVisuals()
    {
        foreach (WireView view in interactionHighlightedWireViews)
        {
            if (view != null)
                view.ClearInteractionHighlight();
        }

        interactionHighlightedWireViews.Clear();
    }

    private void ShowCenterWirePreview(Vector2Int cell)
    {
        if (!EnsureWirePreviews())
            return;

        ConfigureWirePreview(
            primaryWirePreview,
            cell,
            true,
            WireConnection.None);
        SetWirePreviewVisible(primaryWirePreview, true);
        SetWirePreviewVisible(secondaryWirePreview, false);
    }

    private void ShowWireEdgePreview(Vector2Int from, Vector2Int to)
    {
        if (!WireConnectionUtility.TryGetConnection(
                from,
                to,
                out WireConnection fromConnection,
                out WireConnection toConnection) ||
            !EnsureWirePreviews())
        {
            HideWirePreviews();
            return;
        }

        bool showFromArm = GetOccupant(from) == null;
        bool showToArm = GetOccupant(to) == null;

        if (showFromArm)
        {
            ConfigureWirePreview(
                primaryWirePreview,
                from,
                false,
                fromConnection);
        }

        if (showToArm)
        {
            ConfigureWirePreview(
                secondaryWirePreview,
                to,
                false,
                toConnection);
        }

        SetWirePreviewVisible(primaryWirePreview, showFromArm);
        SetWirePreviewVisible(secondaryWirePreview, showToArm);
    }

    private bool EnsureWirePreviews()
    {
        WireView prefab = ResolveWireViewPrefab();
        if (prefab == null)
            return false;

        if (wirePreviewRoot == null)
        {
            Transform existing = transform.Find(WirePreviewObjectName);
            if (existing != null)
            {
                DestroyRuntimeObject(existing.gameObject);
            }

            GameObject rootObject = new(WirePreviewObjectName)
            {
                hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave,
                layer = gameObject.layer
            };
            wirePreviewRoot = rootObject.transform;
            wirePreviewRoot.SetParent(transform, false);
        }

        if (primaryWirePreview == null)
            primaryWirePreview = CreateWirePreview(prefab, "Primary Wire Preview");

        if (secondaryWirePreview == null)
        {
            secondaryWirePreview = CreateWirePreview(
                prefab,
                "Secondary Wire Preview");
        }

        return primaryWirePreview != null && secondaryWirePreview != null;
    }

    private WireView CreateWirePreview(WireView prefab, string objectName)
    {
        WireView preview = Instantiate(prefab, wirePreviewRoot);
        preview.name = objectName;
        preview.gameObject.SetActive(false);
        return preview;
    }

    private void ConfigureWirePreview(
        WireView preview,
        Vector2Int cell,
        bool showCenter,
        WireConnection visibleArms)
    {
        if (preview == null)
            return;

        Vector3 localCenter = GetLocalBottomLeft() + new Vector3(
            (cell.x + 0.5f) * cellSize,
            (wireViewHeight + wireGhostHeightOffset) * cellSize,
            (cell.y + 0.5f) * cellSize);
        Transform previewTransform = preview.transform;
        previewTransform.SetParent(wirePreviewRoot, false);
        previewTransform.localPosition = localCenter;
        previewTransform.localRotation = Quaternion.identity;
        previewTransform.localScale = Vector3.one * cellSize;
        preview.SetPreviewState(showCenter, visibleArms, wireGhostColor);
    }

    private static void SetWirePreviewVisible(WireView preview, bool visible)
    {
        if (preview != null && preview.gameObject.activeSelf != visible)
            preview.gameObject.SetActive(visible);
    }

    private void HideWirePreviews()
    {
        SetWirePreviewVisible(primaryWirePreview, false);
        SetWirePreviewVisible(secondaryWirePreview, false);
    }

    private void ClearWireHoverVisual()
    {
        ClearInteractionHighlightVisuals();
        HideWirePreviews();
    }

    private void DestroyWirePreview()
    {
        DestroyRuntimeObject(wirePreviewRoot != null
            ? wirePreviewRoot.gameObject
            : null);
        wirePreviewRoot = null;
        primaryWirePreview = null;
        secondaryWirePreview = null;
    }

    private void ResetWireDrag()
    {
        activeWireEditMode = WireEditMode.None;
        hasPreviousWireDragCell = false;
    }

    private void RefreshRuntimeGridView()
    {
        if (!Application.isPlaying)
            return;

        if (!drawGridInGameView)
        {
            UpdateRuntimeGridVisibility();
            return;
        }

        EnsureRuntimeGridView();
        if (runtimeGridMesh == null || runtimeGridObject == null)
            return;

        int lineCount = width + height + 2;
        Vector3[] vertices = new Vector3[lineCount * 2];
        int[] indices = new int[vertices.Length];
        Vector3 bottomLeft = GetLocalBottomLeft();
        float boardWidth = width * cellSize;
        float boardHeight = height * cellSize;
        int vertexIndex = 0;

        for (int x = 0; x <= width; x++)
        {
            float localX = bottomLeft.x + x * cellSize;
            vertices[vertexIndex] = new Vector3(localX, 0f, bottomLeft.z);
            indices[vertexIndex] = vertexIndex;
            vertexIndex++;
            vertices[vertexIndex] = new Vector3(
                localX,
                0f,
                bottomLeft.z + boardHeight);
            indices[vertexIndex] = vertexIndex;
            vertexIndex++;
        }

        for (int y = 0; y <= height; y++)
        {
            float localZ = bottomLeft.z + y * cellSize;
            vertices[vertexIndex] = new Vector3(bottomLeft.x, 0f, localZ);
            indices[vertexIndex] = vertexIndex;
            vertexIndex++;
            vertices[vertexIndex] = new Vector3(
                bottomLeft.x + boardWidth,
                0f,
                localZ);
            indices[vertexIndex] = vertexIndex;
            vertexIndex++;
        }

        runtimeGridMesh.Clear();
        runtimeGridMesh.vertices = vertices;
        runtimeGridMesh.SetIndices(indices, MeshTopology.Lines, 0);
        runtimeGridMesh.RecalculateBounds();
        runtimeGridObject.transform.localPosition =
            Vector3.up * (gameViewGridHeight * cellSize);
        UpdateRuntimeGridMaterial();
        UpdateRuntimeGridVisibility();
    }

    /// <summary>
    /// Resizes an optional backing/display mesh to the exact world-space size
    /// of the logical grid without changing its normal-axis thickness.
    /// </summary>
    public void RefreshGridSizeVisual()
    {
        if (gridSizeVisual == null || gridSizeVisual == transform)
            return;

        Vector3 widthDirection =
            transform.TransformVector(Vector3.right).normalized;
        Vector3 heightDirection =
            transform.TransformVector(Vector3.forward).normalized;
        int widthAxis = FindBestAlignedLocalAxis(
            gridSizeVisual,
            widthDirection,
            -1);
        int heightAxis = FindBestAlignedLocalAxis(
            gridSizeVisual,
            heightDirection,
            widthAxis);

        MeshFilter meshFilter = gridSizeVisual.GetComponent<MeshFilter>();
        Vector3 meshSize = meshFilter != null && meshFilter.sharedMesh != null
            ? meshFilter.sharedMesh.bounds.size
            : Vector3.one;
        float targetWidth = transform.TransformVector(
            Vector3.right * (width * cellSize)).magnitude;
        float targetHeight = transform.TransformVector(
            Vector3.forward * (height * cellSize)).magnitude;

        Vector3 nextScale = gridSizeVisual.localScale;
        SetVisualAxisWorldSize(
            gridSizeVisual,
            ref nextScale,
            widthAxis,
            GetAxis(meshSize, widthAxis),
            targetWidth);
        SetVisualAxisWorldSize(
            gridSizeVisual,
            ref nextScale,
            heightAxis,
            GetAxis(meshSize, heightAxis),
            targetHeight);
        gridSizeVisual.localScale = nextScale;
    }

    private static int FindBestAlignedLocalAxis(
        Transform target,
        Vector3 desiredWorldDirection,
        int excludedAxis)
    {
        int bestAxis = 0;
        float bestAlignment = -1f;

        for (int axis = 0; axis < 3; axis++)
        {
            if (axis == excludedAxis)
                continue;

            Vector3 worldAxis = target.TransformVector(GetAxisVector(axis));
            float alignment = worldAxis.sqrMagnitude > Mathf.Epsilon
                ? Mathf.Abs(Vector3.Dot(
                    worldAxis.normalized,
                    desiredWorldDirection))
                : 0f;
            if (alignment <= bestAlignment)
                continue;

            bestAlignment = alignment;
            bestAxis = axis;
        }

        return bestAxis;
    }

    private static void SetVisualAxisWorldSize(
        Transform target,
        ref Vector3 scale,
        int axis,
        float meshAxisSize,
        float targetWorldSize)
    {
        meshAxisSize = Mathf.Max(Mathf.Abs(meshAxisSize), 0.0001f);
        float currentScale = GetAxis(scale, axis);
        float scaleSign = currentScale < 0f ? -1f : 1f;
        Vector3 localAxis = GetAxisVector(axis);
        float worldUnitsPerScaleUnit;

        if (Mathf.Abs(currentScale) > 0.0001f)
        {
            worldUnitsPerScaleUnit =
                target.TransformVector(localAxis).magnitude /
                Mathf.Abs(currentScale);
        }
        else
        {
            Vector3 parentSpaceAxis = target.localRotation * localAxis;
            worldUnitsPerScaleUnit = target.parent != null
                ? target.parent.TransformVector(parentSpaceAxis).magnitude
                : parentSpaceAxis.magnitude;
        }

        float nextAxisScale = targetWorldSize /
                              Mathf.Max(
                                  meshAxisSize * worldUnitsPerScaleUnit,
                                  0.0001f);
        SetAxis(ref scale, axis, nextAxisScale * scaleSign);
    }

    private static Vector3 GetAxisVector(int axis)
    {
        return axis switch
        {
            0 => Vector3.right,
            1 => Vector3.up,
            _ => Vector3.forward
        };
    }

    private static float GetAxis(Vector3 value, int axis)
    {
        return axis switch
        {
            0 => value.x,
            1 => value.y,
            _ => value.z
        };
    }

    private static void SetAxis(ref Vector3 value, int axis, float axisValue)
    {
        switch (axis)
        {
            case 0:
                value.x = axisValue;
                break;
            case 1:
                value.y = axisValue;
                break;
            default:
                value.z = axisValue;
                break;
        }
    }

    private void EnsureRuntimeGridView()
    {
        if (runtimeGridObject != null &&
            runtimeGridMesh != null &&
            runtimeGridRenderer != null)
        {
            return;
        }

        Shader gridShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (gridShader == null)
            gridShader = Shader.Find("Unlit/Color");

        if (gridShader == null)
        {
            Debug.LogWarning(
                "Runtime grid could not find an unlit shader.",
                this);
            return;
        }

        runtimeGridObject = new GameObject("Runtime Grid");
        runtimeGridObject.hideFlags = HideFlags.HideInHierarchy |
                                      HideFlags.DontSave;
        runtimeGridObject.layer = gameObject.layer;
        runtimeGridObject.transform.SetParent(transform, false);

        MeshFilter meshFilter = runtimeGridObject.AddComponent<MeshFilter>();
        runtimeGridRenderer = runtimeGridObject.AddComponent<MeshRenderer>();
        runtimeGridRenderer.shadowCastingMode = ShadowCastingMode.Off;
        runtimeGridRenderer.receiveShadows = false;
        runtimeGridRenderer.motionVectorGenerationMode =
            MotionVectorGenerationMode.ForceNoMotion;

        runtimeGridMesh = new Mesh
        {
            name = "Runtime Grid Mesh",
            hideFlags = HideFlags.HideAndDontSave
        };
        meshFilter.sharedMesh = runtimeGridMesh;

        runtimeGridMaterial = new Material(gridShader)
        {
            name = "Runtime Grid Material",
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = (int)RenderQueue.Transparent
        };
        runtimeGridMaterial.SetOverrideTag("RenderType", "Transparent");
        SetMaterialFloat("_Surface", 1f);
        SetMaterialFloat("_Blend", 0f);
        SetMaterialFloat("_AlphaClip", 0f);
        SetMaterialFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        SetMaterialFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        SetMaterialFloat("_ZWrite", 0f);
        runtimeGridMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        runtimeGridMaterial.DisableKeyword("_ALPHATEST_ON");
        runtimeGridRenderer.sharedMaterial = runtimeGridMaterial;
    }

    private void UpdateRuntimeGridMaterial()
    {
        if (runtimeGridMaterial == null)
            return;

        if (runtimeGridMaterial.HasProperty("_BaseColor"))
            runtimeGridMaterial.SetColor("_BaseColor", gridColor);

        if (runtimeGridMaterial.HasProperty("_Color"))
            runtimeGridMaterial.SetColor("_Color", gridColor);
    }

    private void UpdateRuntimeGridVisibility()
    {
        if (runtimeGridRenderer != null)
            runtimeGridRenderer.enabled = Application.isPlaying &&
                                          drawGridInGameView;
    }

    private void SetMaterialFloat(string propertyName, float value)
    {
        if (runtimeGridMaterial != null &&
            runtimeGridMaterial.HasProperty(propertyName))
        {
            runtimeGridMaterial.SetFloat(propertyName, value);
        }
    }

    private void DestroyRuntimeGridView()
    {
        DestroyRuntimeObject(runtimeGridObject);
        DestroyRuntimeObject(runtimeGridMesh);
        DestroyRuntimeObject(runtimeGridMaterial);
        runtimeGridObject = null;
        runtimeGridMesh = null;
        runtimeGridRenderer = null;
        runtimeGridMaterial = null;
    }

    private static void DestroyRuntimeObject(UnityEngine.Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    private Vector3 GetLocalBottomLeft()
    {
        return new Vector3(
            -width * cellSize * 0.5f,
            0f,
            -height * cellSize * 0.5f);
    }

    private enum WireEditMode
    {
        None,
        Add
    }

    private enum WireHoverTargetType
    {
        None,
        Center,
        Edge
    }

    private readonly struct WireHoverTarget
    {
        public WireHoverTargetType Type { get; }
        public Vector2Int From { get; }
        public Vector2Int To { get; }

        private WireHoverTarget(
            WireHoverTargetType type,
            Vector2Int from,
            Vector2Int to)
        {
            Type = type;
            From = from;
            To = to;
        }

        public static WireHoverTarget Center(Vector2Int cell)
        {
            return new WireHoverTarget(
                WireHoverTargetType.Center,
                cell,
                cell);
        }

        public static WireHoverTarget Edge(Vector2Int from, Vector2Int to)
        {
            return new WireHoverTarget(
                WireHoverTargetType.Edge,
                from,
                to);
        }
    }

    private void OnDrawGizmos()
    {
        if (!drawGrid && !drawWireGizmos)
            return;

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        Gizmos.matrix = transform.localToWorldMatrix;

        if (drawGrid)
        {
            Gizmos.color = gridColor;

            Vector3 bottomLeft = GetLocalBottomLeft();
            float boardWidth = width * cellSize;
            float boardHeight = height * cellSize;

            for (int x = 0; x <= width; x++)
            {
                float localX = bottomLeft.x + x * cellSize;
                Gizmos.DrawLine(
                    new Vector3(localX, 0f, bottomLeft.z),
                    new Vector3(localX, 0f, bottomLeft.z + boardHeight));
            }

            for (int y = 0; y <= height; y++)
            {
                float localZ = bottomLeft.z + y * cellSize;
                Gizmos.DrawLine(
                    new Vector3(bottomLeft.x, 0f, localZ),
                    new Vector3(bottomLeft.x + boardWidth, 0f, localZ));
            }
        }

        if (drawWireGizmos)
            DrawWireGizmos();

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }

    private void DrawWireGizmos()
    {
        EnsureWireStorage();
        Gizmos.color = wireColor;

        float localHeight = wireGizmoHeight * cellSize;
        float nodeRadius = wireGizmoNodeSize * cellSize;
        float halfCell = cellSize * 0.5f;
        Vector3 bottomLeft = GetLocalBottomLeft();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                WireCell wire = wires[x, y];
                if (wire == null || !wire.HasCenter)
                    continue;

                Vector3 center = bottomLeft + new Vector3(
                    (x + 0.5f) * cellSize,
                    localHeight,
                    (y + 0.5f) * cellSize);
                Gizmos.DrawSphere(center, nodeRadius);

                foreach (WireConnection connection in CardinalWireConnections)
                {
                    if (!wire.HasConnection(connection))
                        continue;

                    Vector2Int direction = WireConnectionUtility.ToCellOffset(connection);
                    Vector3 localDirection = new(direction.x, 0f, direction.y);
                    Gizmos.DrawLine(center, center + localDirection * halfCell);
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class GridBoard : MonoBehaviour
{
    private const string DisplayObjectName = "Display";
    private const string ComponentsObjectName = "Components";

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
    private bool drawGrid = true;

    [SerializeField]
    private Color gridColor = new(0f, 0.8f, 1f, 0.75f);

    [SerializeField]
    private Color hoverColor = new(1f, 0.55f, 0f, 0.3f);

    [Header("Component Hierarchy")]
    [SerializeField]
    [Tooltip("Object that owns the Components container. If empty, a nearby Display is found automatically.")]
    private Transform displayRoot;

    [SerializeField]
    [Tooltip("Container used for placed BoardComponent objects. Created automatically as Display/Components.")]
    private Transform componentsRoot;

    private Vector2Int[,] cells;
    private BoardComponent[,] occupants;
    private bool occupancyDirty = true;
    private bool rebuildingOccupancy;

    public int Width => width;
    public int Height => height;
    public float CellSize => cellSize;
    public int CellCount => width * height;
    public bool DrawGrid => drawGrid;
    public Color HoverColor => hoverColor;
    public Transform DisplayRoot => ResolveDisplayRoot();
    public Transform ComponentsRoot => ResolveComponentsRoot();

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
        EnsureComponentsRoot();
        RebuildOccupancy();
    }

    private void OnEnable()
    {
        EnsureCells();
        EnsureComponentsRoot();
        RebuildOccupancy();
    }

    private void OnValidate()
    {
        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);
        cellSize = Mathf.Max(0.01f, cellSize);
        RebuildCells();
        occupancyDirty = true;
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
        return CanPlaceComponentInternal(component, gridPosition, out failureReason);
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
        EnsureOccupancy();

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

        return true;
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
        out string failureReason)
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

    private Vector3 GetLocalBottomLeft()
    {
        return new Vector3(
            -width * cellSize * 0.5f,
            0f,
            -height * cellSize * 0.5f);
    }

    private void OnDrawGizmos()
    {
        if (!drawGrid)
            return;

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        Gizmos.matrix = transform.localToWorldMatrix;
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

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}

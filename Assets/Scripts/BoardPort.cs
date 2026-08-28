using System;
using UnityEngine;

public enum BoardPortDirection
{
    North = 0,
    East = 1,
    South = 2,
    West = 3
}

public enum BoardPortType
{
    Input = 0,
    Output = 1
}

[Serializable]
public sealed class BoardPort
{
    [SerializeField]
    [Tooltip("Cell inside the component's unrotated 2x2 footprint.")]
    private Vector2Int localCellOffset;

    [SerializeField]
    [Tooltip("Direction the port faces before the component is rotated.")]
    private BoardPortDirection direction = BoardPortDirection.North;

    [SerializeField]
    private BoardPortType type;

    public Vector2Int LocalCellOffset => localCellOffset;
    public BoardPortDirection Direction => direction;
    public BoardPortType Type => type;

    public BoardPort()
    {
    }

    public BoardPort(
        Vector2Int localCellOffset,
        BoardPortDirection direction,
        BoardPortType type)
    {
        this.localCellOffset = localCellOffset;
        this.direction = direction;
        this.type = type;
    }

    /// <summary>
    /// Returns the cell offset after rotating clockwise in board space.
    /// Positive Y rotation maps North to East, matching the component transform.
    /// </summary>
    public Vector2Int GetRotatedCellOffset(int rotationSteps, Vector2Int footprintSize)
    {
        int steps = GridBoard.NormalizeRotationSteps(rotationSteps);
        int maximumX = footprintSize.x - 1;
        int maximumY = footprintSize.y - 1;

        return steps switch
        {
            1 => new Vector2Int(localCellOffset.y, maximumX - localCellOffset.x),
            2 => new Vector2Int(maximumX - localCellOffset.x, maximumY - localCellOffset.y),
            3 => new Vector2Int(maximumY - localCellOffset.y, localCellOffset.x),
            _ => localCellOffset
        };
    }

    public BoardPortDirection GetRotatedDirection(int rotationSteps)
    {
        int directionIndex = (int)direction + GridBoard.NormalizeRotationSteps(rotationSteps);
        return (BoardPortDirection)(directionIndex % 4);
    }

    public static Vector2Int DirectionToCellOffset(BoardPortDirection portDirection)
    {
        return portDirection switch
        {
            BoardPortDirection.North => Vector2Int.up,
            BoardPortDirection.East => Vector2Int.right,
            BoardPortDirection.South => Vector2Int.down,
            BoardPortDirection.West => Vector2Int.left,
            _ => Vector2Int.zero
        };
    }

    internal void Validate(Vector2Int footprintSize)
    {
        localCellOffset = new Vector2Int(
            Mathf.Clamp(localCellOffset.x, 0, Mathf.Max(0, footprintSize.x - 1)),
            Mathf.Clamp(localCellOffset.y, 0, Mathf.Max(0, footprintSize.y - 1)));
        direction = (BoardPortDirection)GridBoard.NormalizeRotationSteps((int)direction);
        type = type == BoardPortType.Output ? BoardPortType.Output : BoardPortType.Input;
    }
}

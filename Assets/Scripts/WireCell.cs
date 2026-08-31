using System;
using UnityEngine;

[Flags]
public enum WireConnection
{
    None = 0,
    North = 1 << 0,
    East = 1 << 1,
    South = 1 << 2,
    West = 1 << 3
}

[Serializable]
public sealed class WireCell
{
    [SerializeField]
    private bool hasCenter;

    [SerializeField]
    private WireConnection connections;

    public bool HasCenter => hasCenter;
    public WireConnection Connections => connections;
    public bool HasConnections => connections != WireConnection.None;
    public bool HasAnyPart => hasCenter || HasConnections;

    public bool HasConnection(WireConnection connection)
    {
        return (connections & connection) != 0;
    }

    internal bool AddCenter()
    {
        if (hasCenter)
            return false;

        hasCenter = true;
        return true;
    }

    internal bool AddConnection(WireConnection connection)
    {
        if (!hasCenter)
            return false;

        WireConnection previous = connections;
        connections |= connection;
        return connections != previous;
    }

    internal bool RemoveConnection(WireConnection connection)
    {
        WireConnection previous = connections;
        connections &= ~connection;
        return connections != previous;
    }
}

public static class WireConnectionUtility
{
    public static bool TryGetConnection(
        Vector2Int from,
        Vector2Int to,
        out WireConnection connection,
        out WireConnection opposite)
    {
        Vector2Int offset = to - from;

        if (offset == Vector2Int.up)
        {
            connection = WireConnection.North;
            opposite = WireConnection.South;
            return true;
        }

        if (offset == Vector2Int.right)
        {
            connection = WireConnection.East;
            opposite = WireConnection.West;
            return true;
        }

        if (offset == Vector2Int.down)
        {
            connection = WireConnection.South;
            opposite = WireConnection.North;
            return true;
        }

        if (offset == Vector2Int.left)
        {
            connection = WireConnection.West;
            opposite = WireConnection.East;
            return true;
        }

        connection = WireConnection.None;
        opposite = WireConnection.None;
        return false;
    }

    public static WireConnection GetOpposite(WireConnection connection)
    {
        return connection switch
        {
            WireConnection.North => WireConnection.South,
            WireConnection.East => WireConnection.West,
            WireConnection.South => WireConnection.North,
            WireConnection.West => WireConnection.East,
            _ => WireConnection.None
        };
    }

    public static Vector2Int ToCellOffset(WireConnection connection)
    {
        return connection switch
        {
            WireConnection.North => Vector2Int.up,
            WireConnection.East => Vector2Int.right,
            WireConnection.South => Vector2Int.down,
            WireConnection.West => Vector2Int.left,
            _ => Vector2Int.zero
        };
    }

    public static bool TryToPortDirection(
        WireConnection connection,
        out BoardPortDirection direction)
    {
        switch (connection)
        {
            case WireConnection.North:
                direction = BoardPortDirection.North;
                return true;
            case WireConnection.East:
                direction = BoardPortDirection.East;
                return true;
            case WireConnection.South:
                direction = BoardPortDirection.South;
                return true;
            case WireConnection.West:
                direction = BoardPortDirection.West;
                return true;
            default:
                direction = default;
                return false;
        }
    }
}

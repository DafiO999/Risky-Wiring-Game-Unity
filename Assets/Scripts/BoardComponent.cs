using System.Collections.Generic;
using UnityEngine;

public enum BoardRotation
{
    Degrees0 = 0,
    Degrees90 = 1,
    Degrees180 = 2,
    Degrees270 = 3
}

[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class BoardComponent : MonoBehaviour
{
    public const int FootprintWidth = 2;
    public const int FootprintHeight = 2;

    public static readonly Vector2Int FootprintSize = new(FootprintWidth, FootprintHeight);

    [SerializeField, HideInInspector]
    private GridBoard board;

    [SerializeField, HideInInspector]
    private Vector2Int gridPosition;

    [SerializeField, HideInInspector]
    private BoardRotation rotation;

    private bool isPlaced;
    private bool isApplyingPlacement;

    public GridBoard Board => board;
    public Vector2Int GridPosition => gridPosition;
    public BoardRotation Rotation => rotation;
    public int RotationSteps => (int)rotation;
    public int RotationDegrees => RotationSteps * 90;
    public bool IsPlaced => isPlaced;

    public IEnumerable<Vector2Int> OccupiedCells
    {
        get
        {
            for (int y = 0; y < FootprintHeight; y++)
            {
                for (int x = 0; x < FootprintWidth; x++)
                    yield return gridPosition + new Vector2Int(x, y);
            }
        }
    }

    private void Reset()
    {
        board = FindBoardForTransform();
        rotation = BoardRotation.Degrees0;

        if (board == null)
            return;

        gridPosition = board.WorldToCell(transform.position);
        board.TryPlaceComponent(this, gridPosition, RotationSteps);
    }

    private void OnEnable()
    {
        if (isApplyingPlacement)
            return;

        if (board == null)
            board = FindBoardForTransform();

        if (board != null)
            board.TryPlaceComponent(this, gridPosition, RotationSteps);
    }

    private void OnValidate()
    {
        rotation = (BoardRotation)GridBoard.NormalizeRotationSteps((int)rotation);
    }

    private void OnTransformParentChanged()
    {
        if (!isApplyingPlacement && isActiveAndEnabled && board != null)
            board.TryPlaceComponent(this, gridPosition, RotationSteps);
    }

    private void OnDestroy()
    {
        if (board != null)
            board.RemoveComponent(this);
    }

    public bool TrySetPlacement(
        GridBoard targetBoard,
        Vector2Int targetGridPosition,
        BoardRotation targetRotation)
    {
        return TrySetPlacement(
            targetBoard,
            targetGridPosition,
            (int)targetRotation,
            out _);
    }

    public bool TrySetPlacement(
        GridBoard targetBoard,
        Vector2Int targetGridPosition,
        int targetRotationSteps,
        out string failureReason)
    {
        if (targetBoard == null)
        {
            failureReason = "A GridBoard reference is required.";
            return false;
        }

        return targetBoard.TryPlaceComponent(
            this,
            targetGridPosition,
            targetRotationSteps,
            out failureReason);
    }

    public void ClearPlacement()
    {
        GridBoard previousBoard = board;
        if (previousBoard != null)
            previousBoard.RemoveComponent(this);

        board = null;
        isPlaced = false;
    }

    internal void ApplyPlacementFromBoard(
        GridBoard owner,
        Vector2Int targetGridPosition,
        int targetRotationSteps,
        Transform targetParent)
    {
        isApplyingPlacement = true;

        board = owner;
        gridPosition = targetGridPosition;
        rotation = (BoardRotation)GridBoard.NormalizeRotationSteps(targetRotationSteps);

        if (targetParent != null && transform.parent != targetParent)
            transform.SetParent(targetParent, true);

        Vector3 worldPosition = owner.GetFootprintCenterWorld(gridPosition, FootprintSize);
        Quaternion worldRotation = owner.transform.rotation *
                                   Quaternion.Euler(0f, RotationDegrees, 0f);
        transform.SetPositionAndRotation(worldPosition, worldRotation);

        isPlaced = true;
        isApplyingPlacement = false;
    }

    internal void SetPlacementValidity(bool value)
    {
        isPlaced = value;
    }

    private GridBoard FindBoardForTransform()
    {
        GridBoard parentBoard = GetComponentInParent<GridBoard>();
        if (parentBoard != null)
            return parentBoard;

        GridBoard[] boards = FindObjectsByType<GridBoard>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (GridBoard candidate in boards)
        {
            Transform candidateComponents = candidate.ComponentsRoot;
            if (candidateComponents != null && transform.IsChildOf(candidateComponents))
                return candidate;

            Transform candidateDisplay = candidate.DisplayRoot;
            if (candidateDisplay != null && transform.IsChildOf(candidateDisplay))
                return candidate;
        }

        return boards.Length == 1 ? boards[0] : null;
    }
}

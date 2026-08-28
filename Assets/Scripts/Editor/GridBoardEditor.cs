using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[CustomEditor(typeof(GridBoard))]
public sealed class GridBoardEditor : Editor
{
    private static readonly Color HoverOutlineColor = new(1f, 0.65f, 0.1f, 1f);

    public override void OnInspectorGUI()
    {
        GridBoard board = (GridBoard)target;

        if (DrawDefaultInspector())
        {
            board.RefreshPlacements();
            EditorUtility.SetDirty(board);
            SceneView.RepaintAll();
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Refresh Component Placements"))
        {
            board.RefreshPlacements();
            EditorUtility.SetDirty(board);
            SceneView.RepaintAll();
        }
    }

    private void OnSceneGUI()
    {
        GridBoard board = (GridBoard)target;
        Event currentEvent = Event.current;

        if (currentEvent.type == EventType.MouseMove)
            SceneView.RepaintAll();

        if (!board.DrawGrid || !TryGetHoveredCell(board, currentEvent.mousePosition, out Vector2Int cell))
            return;

        DrawCellHighlight(board, cell);
    }

    private static bool TryGetHoveredCell(
        GridBoard board,
        Vector2 mousePosition,
        out Vector2Int cell)
    {
        Ray mouseRay = HandleUtility.GUIPointToWorldRay(mousePosition);
        Plane boardPlane = new(board.transform.up, board.transform.position);

        if (!boardPlane.Raycast(mouseRay, out float distance))
        {
            cell = default;
            return false;
        }

        Vector3 worldPosition = mouseRay.GetPoint(distance);
        return board.TryWorldToCell(worldPosition, out cell);
    }

    private static void DrawCellHighlight(GridBoard board, Vector2Int cell)
    {
        Vector3 center = board.CellToWorld(cell);
        Vector3 halfRight = board.transform.TransformVector(
            Vector3.right * board.CellSize * 0.5f);
        Vector3 halfForward = board.transform.TransformVector(
            Vector3.forward * board.CellSize * 0.5f);
        Vector3 offset = board.transform.up * Mathf.Max(0.001f, board.CellSize * 0.002f);

        Vector3[] corners =
        {
            center - halfRight - halfForward + offset,
            center - halfRight + halfForward + offset,
            center + halfRight + halfForward + offset,
            center + halfRight - halfForward + offset
        };

        CompareFunction previousZTest = Handles.zTest;
        Handles.zTest = CompareFunction.LessEqual;
        Handles.DrawSolidRectangleWithOutline(corners, board.HoverColor, HoverOutlineColor);
        Handles.zTest = previousZTest;
    }
}

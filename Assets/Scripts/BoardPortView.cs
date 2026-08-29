using UnityEngine;

[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class BoardPortView : MonoBehaviour
{
    [SerializeField]
    private BoardComponent component;

    [SerializeField, Min(0)]
    private int portIndex;

    [SerializeField, Min(0f)]
    [Tooltip("Height above the grid plane as a fraction of the board cell size.")]
    private float heightAboveBoard = 0.38f;

    public BoardComponent Component => component;
    public int PortIndex => portIndex;

    private void OnEnable()
    {
        RefreshPose();
    }

    private void OnValidate()
    {
        portIndex = Mathf.Max(0, portIndex);
        heightAboveBoard = Mathf.Max(0f, heightAboveBoard);
        RefreshPose();
    }

    public void RefreshPose(BoardComponent owner = null)
    {
        if (owner != null)
            component = owner;

        if (component == null)
            component = GetComponentInParent<BoardComponent>();

        if (component == null ||
            component.Board == null ||
            !component.TryGetPortWorldPose(
                portIndex,
                out Vector3 portPosition,
                out _))
        {
            return;
        }

        GridBoard board = component.Board;
        Vector3 boardNormal = board.transform.up.normalized;
        transform.position = portPosition +
                             boardNormal * board.CellSize * heightAboveBoard;
    }
}

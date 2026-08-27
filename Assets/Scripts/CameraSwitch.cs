using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

public class CameraSwitch : MonoBehaviour
{

    [SerializeField]
    private CinemachineCamera gambaCamera;
    [SerializeField]
    private CinemachineCamera shopCamera;
    [SerializeField]
    private CinemachineCamera gameCamera;

    public void SwitchToShop()
    {
        gambaCamera.Priority = 0;
        shopCamera.Priority = 1;
        gameCamera.Priority = 0;
    }
    public void SwitchToGamba()
    {
        gambaCamera.Priority = 1;
        shopCamera.Priority = 0;
        gameCamera.Priority = 0;
    }
    public void SwitchToGame()
    {
        StartCoroutine(SwitchToGameRoutine());
    }

    private IEnumerator SwitchToGameRoutine()
    {
        yield return new WaitForSeconds(5f);

        gambaCamera.Priority = 0;
        shopCamera.Priority = 0;
        gameCamera.Priority = 1;
    }
}

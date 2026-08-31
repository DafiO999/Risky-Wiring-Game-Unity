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
    [SerializeField]
    private float waitFor;

    private void Start()
    {
        SwitchToGamba(0);
    }

    public void SwitchToShop()
    {
        gambaCamera.Priority = 0;
        shopCamera.Priority = 1;
        gameCamera.Priority = 0;
    }
    public void SwitchToGamba(float waitFor)
    {
        StartCoroutine(SwitchToGambaRoutine(waitFor));
    }
    public void SwitchToGame()
    {
        StartCoroutine(SwitchToGameRoutine());
    }

    private IEnumerator SwitchToGameRoutine()
    {
        yield return new WaitForSeconds(waitFor);

        gambaCamera.Priority = 0;
        shopCamera.Priority = 0;
        gameCamera.Priority = 1;
    }
    private IEnumerator SwitchToGambaRoutine(float waitFor)
    {
        yield return new WaitForSeconds(waitFor);

        gambaCamera.Priority = 1;
        shopCamera.Priority = 0;
        gameCamera.Priority = 0;
    }
}

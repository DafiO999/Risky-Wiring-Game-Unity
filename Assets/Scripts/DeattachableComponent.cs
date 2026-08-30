using UnityEngine;
[RequireComponent(typeof(Rigidbody))]

[RequireComponent(typeof(Collider))]
public class DeattachableComponent : MonoBehaviour
{
    [SerializeField]
    private Rigidbody rb;

    [SerializeField]
    private Collider coll;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        coll = GetComponent<Collider>();
        coll.isTrigger = false;
        rb.isKinematic = true;
    }

    public void Detach()
    {
        rb.isKinematic = false;

        coll.isTrigger = true;

        rb.AddForce(transform.up * -2.5f, ForceMode.Impulse);
    }

    public void ResetState()
    {
        rb.isKinematic = true;
        coll.isTrigger = false;
    }
}

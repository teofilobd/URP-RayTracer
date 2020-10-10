using UnityEngine;

[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
public class RayTracingSphereObject : MonoBehaviour
{
    // Start is called before the first frame update
    void OnBecameVisible()
    {
        RayTracingMaster.RegisterSphereObject(this);
    }

    void OnBecameInvisible()
    {
        RayTracingMaster.UnregisterSphereObject(this);
    }
}

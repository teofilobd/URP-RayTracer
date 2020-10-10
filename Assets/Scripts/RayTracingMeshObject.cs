using UnityEngine;

[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
public class RayTracingMeshObject : MonoBehaviour
{
    void OnBecameVisible()
    {
        RayTracingMaster.RegisterObject(this);        
    }

    void OnBecameInvisible()
    {
        RayTracingMaster.UnregisterObject(this);
    }
}
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

[RequireComponent(typeof(Camera))]
public class RayTracingMaster : MonoBehaviour
{
    public ComputeShader m_RayTracingShader;
    public Texture m_SkyboxTexture;
    public Light m_DirectionalLight;

    [Header("Spheres")]
    public int m_SphereSeed;
    public Vector2 m_SphereRadius = new Vector2(3.0f, 8.0f);
    public uint m_SpheresMax = 100;
    public float m_SpherePlacementRadius = 100.0f;

    int _KiMain = -1;
    int _KiFrustumCulling = -1;
    int _ScreenWidth;
    int _ScreenHeight;

    private Camera _Camera;
    private float _LastFieldOfView;
    RenderTexture _Target;
    private uint _CurrentSample = 0;
    private ComputeBuffer _SphereBuffer;
    private List<Transform> _TransformsToWatch = new List<Transform>();
    private static bool _MeshObjectsNeedRebuilding = false;
    private static bool _SphereObjectsNeedRebuilding = false;
    private static List<RayTracingMeshObject> _RayTracingObjects = new List<RayTracingMeshObject>();
    private static List<RayTracingSphereObject> _RayTracingSphereObjects = new List<RayTracingSphereObject>();
    private static List<MeshObject> _MeshObjects = new List<MeshObject>();
    private static List<MeshMaterial> _MeshMaterials = new List<MeshMaterial>();
    private static List<VertexInfo> _Vertices = new List<VertexInfo>();
    private static List<int> _Indices = new List<int>();
    private static List<Texture2D> _Textures = new List<Texture2D>();
    List<Triangle> _Triangles = new List<Triangle>();

    private Texture2DArray _TextureBuffer;
    private ComputeBuffer _MeshObjectBuffer;
    private ComputeBuffer _MeshMaterialBuffer;
    private ComputeBuffer _VertexBuffer;
    private ComputeBuffer _IndexBuffer;
    private ComputeBuffer _TriangleBuffer;
    private ComputeBuffer _FrustumBuffer;

    private Plane[] _FrustumPlanes;

    struct MeshObject
    {
        public Matrix4x4 localToWorldMatrix;
        public int indices_offset;
        public int indices_count;
    }

    struct MeshMaterial
    {
        public Vector3 albedo;
        public Vector3 specular;
        public Vector3 emission;
        public float smoothness;
        public int textureID;
    }

    struct VertexInfo
    {
        public Vector3 position;
        public Vector3 normal;
        public Vector2 uv;
    }

    struct Sphere
    {
        public Vector3 position;
        public float radius;
        public Vector3 albedo;
        public Vector3 specular;
        public float smoothness;
        public Vector3 emission;
    };

    struct Triangle
    {
        public int id0;
        public int id1; 
        public int id2;
    };

    private void Awake()
    {
        _KiMain = m_RayTracingShader.FindKernel("CSMain");
        _KiFrustumCulling = m_RayTracingShader.FindKernel("CSFrustumCulling");

        _Camera = GetComponent<Camera>();
        _ScreenWidth = Screen.width;
        _ScreenHeight = Screen.height;

        _TransformsToWatch.Add(transform);
        _TransformsToWatch.Add(m_DirectionalLight.transform);

        _FrustumPlanes = new Plane[6];
    }

    private void OnEnable()
    {
        _CurrentSample = 0;
        SetupSpheres();
        //SetUpScene();
    }

    private void OnDisable()
    {
        _SphereBuffer?.Release();
        _MeshObjectBuffer?.Release();
        _MeshMaterialBuffer?.Release();
        _VertexBuffer?.Release();
        _IndexBuffer?.Release();
        _FrustumBuffer?.Release();
        _TriangleBuffer?.Release();
         if(_TextureBuffer != null)
        {
            Destroy(_TextureBuffer);
        }
    }

    public static void RegisterObject(RayTracingMeshObject obj)
    {
        _RayTracingObjects.Add(obj);
        _MeshObjectsNeedRebuilding = true;
    }

    public static void UnregisterObject(RayTracingMeshObject obj)
    {
        _RayTracingObjects.Remove(obj);
        _MeshObjectsNeedRebuilding = true;
    }

    public static void RegisterSphereObject(RayTracingSphereObject obj)
    {
        _RayTracingSphereObjects.Add(obj);
        _SphereObjectsNeedRebuilding = true;
    }

    public static void UnregisterSphereObject(RayTracingSphereObject obj)
    {
        _RayTracingSphereObjects.Remove(obj);
        _SphereObjectsNeedRebuilding = true;
    }

    private static void CreateComputeBuffer<T>(ref ComputeBuffer buffer, List<T> data, int stride)
    where T : struct
    {
        // Do we already have a compute buffer?
        if (buffer != null)
        {
            // If no data or buffer doesn't match the given criteria, release it
            if (data.Count == 0 || buffer.count != data.Count || buffer.stride != stride)
            {
                buffer.Release();
                buffer = null;
            }
        }
        if (data.Count != 0)
        {
            // If the buffer has been released or wasn't there to
            // begin with, create it
            if (buffer == null)
            {
                buffer = new ComputeBuffer(data.Count, stride);
            }
            // Set data on the buffer
            buffer.SetData(data);
        }
    }
    private void SetComputeBuffer(int kernelIndex, string name, ComputeBuffer buffer)
    {
        if (buffer != null)
        {
            m_RayTracingShader.SetBuffer(kernelIndex, name, buffer);
        }
    }

    // reference: https://catlikecoding.com/unity/tutorials/hex-map/part-14/
    Texture2DArray CreateTextureArray(Texture2D[] textures)
    {
        if(textures == null || textures.Length == 0)
        {
            return null;
        }

        Texture2D firstTex = textures[0];
        Texture2DArray texArray = new Texture2DArray(firstTex.width,
                                                    firstTex.height,
                                                    textures.Length,
                                                    firstTex.format,
                                                    firstTex.mipmapCount > 0);
        texArray.anisoLevel = firstTex.anisoLevel;
        texArray.filterMode = firstTex.filterMode;
        texArray.wrapMode = firstTex.wrapMode;

        for (int i = 0; i < textures.Length; i++)
        {
            for (int m = 0; m < firstTex.mipmapCount; m++)
            {
                Graphics.CopyTexture(textures[i], 0, m, texArray, i, m);
            }
        }

        texArray.Apply();

        return texArray;
    }

    private void RebuildMeshObjectBuffers()
    {
        if (!_MeshObjectsNeedRebuilding)
        {
            return;
        }

        GeometryUtility.CalculateFrustumPlanes(_Camera, _FrustumPlanes);

        _MeshObjectsNeedRebuilding = false;
        _CurrentSample = 0;
        
        // Clear all lists
        _MeshObjects.Clear();
        _MeshMaterials.Clear();
        _Vertices.Clear();
        _Indices.Clear();
        _Textures.Clear();
        _Triangles.Clear();

        int texId = -1;
        // Loop over all objects and gather their data
        foreach (RayTracingMeshObject obj in _RayTracingObjects)
        {
            MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
            Mesh mesh = meshFilter.sharedMesh;
        
            int firstVertex = _Vertices.Count;

            for(int vertexId = 0; vertexId < mesh.vertexCount; ++vertexId)
            {
                VertexInfo vertexInfo = new VertexInfo()
                {
                    // position = mesh.vertices[vertexId],
                    position = meshFilter.transform.TransformPoint(mesh.vertices[vertexId]),
                    normal = mesh.normals[vertexId],
                    uv = mesh.uv[vertexId]
                };
                _Vertices.Add(vertexInfo);
            }
            
            // Add index data - if the vertex buffer wasn't empty before, the
            // indices need to be offset
            int firstIndex = _Indices.Count;
            var indices = mesh.GetIndices(0);
            _Indices.AddRange(indices.Select(index => index + firstVertex));
            
            // Add the object itself
            _MeshObjects.Add(new MeshObject()
            {
                localToWorldMatrix = obj.transform.localToWorldMatrix,
                indices_offset = firstIndex/3,
                indices_count = indices.Length/3
            });

            Material material = obj.GetComponent<MeshRenderer>().sharedMaterial;

            Texture2D baseTexture = material.GetTexture("_BaseMap") as Texture2D;

            if(baseTexture)
            {
                _Textures.Add(baseTexture);
            }

            MeshMaterial meshMaterial = new MeshMaterial()
            {
                smoothness = material.GetFloat("_Smoothness"),
                specular = material.GetVector("_SpecColor"),
                albedo = material.GetVector("_BaseColor"),
                emission = material.GetVector("_EmissionColor"),
                textureID = baseTexture ? ++texId : -1
            };
            _MeshMaterials.Add(meshMaterial);
        }

        if (_TextureBuffer != null)
        {
            Destroy(_TextureBuffer);
        }
        _TextureBuffer = CreateTextureArray(_Textures.ToArray());

        CreateComputeBuffer(ref _MeshObjectBuffer, _MeshObjects, 72);
        CreateComputeBuffer(ref _MeshMaterialBuffer, _MeshMaterials, 44);
        CreateComputeBuffer(ref _VertexBuffer, _Vertices, 32);
        CreateComputeBuffer(ref _IndexBuffer, _Indices, 12);
        
        for(int id = 0; id < _Indices.Count; id+=3)
        {
            Triangle tri = new Triangle()
            {
                id0 = _Indices[id],
                id1 = _Indices[id + 1],
                id2 = _Indices[id + 2]
            };
            _Triangles.Add(tri);
        }

        CreateComputeBuffer(ref _FrustumBuffer, _FrustumPlanes.ToList(), 16);
        CreateComputeBuffer(ref _TriangleBuffer, _Triangles, 12);


        SetComputeBuffer(_KiFrustumCulling, "_FrustumPlanes", _FrustumBuffer);
        SetComputeBuffer(_KiFrustumCulling, "_Vertices", _VertexBuffer);
        SetComputeBuffer(_KiFrustumCulling, "_Indices", _IndexBuffer);

        m_RayTracingShader.Dispatch(_KiFrustumCulling, Mathf.CeilToInt(_Triangles.Count/32.0f), 1, 1);
    }

    private void SetupSpheres()
    {
        if(!_SphereObjectsNeedRebuilding)
        {
            return;
        }

        _SphereObjectsNeedRebuilding = false;

        List<Sphere> spheres = new List<Sphere>();

        // Add a number of random spheres
        foreach (var sphereObj in _RayTracingSphereObjects)
        {
            Sphere sphere = new Sphere();

            // Radius and radius
            sphere.radius = sphereObj.transform.lossyScale.x * 0.5f;
            sphere.position = sphereObj.transform.position;

            Color color = Random.ColorHSV();
            float chance = Random.value;
            if (chance < 0.8f)
            {
                bool metal = chance < 0.4f;
                sphere.albedo = metal ? Vector4.zero : new Vector4(color.r, color.g, color.b);
                sphere.specular = metal ? new Vector4(color.r, color.g, color.b) : new Vector4(0.04f, 0.04f, 0.04f);
                sphere.smoothness = Random.value;
            }
            else
            {
                Color emission = Random.ColorHSV(0, 1, 0, 1, 3.0f, 8.0f);
                sphere.emission = new Vector3(emission.r, emission.g, emission.b);
                sphere.specular = Vector3.one;
                sphere.albedo = Vector3.one;
                sphere.smoothness = 1;
            }

            // Add the sphere to the list
            spheres.Add(sphere);
        }

        // Assign to compute buffer
        CreateComputeBuffer(ref _SphereBuffer, spheres, 56);
    }

    private void SetUpScene()
    {
        Random.InitState(m_SphereSeed);
        List<Sphere> spheres = new List<Sphere>();

        // Add a number of random spheres
        for (int i = 0; i < m_SpheresMax; i++)
        {
            Sphere sphere = new Sphere();

            // Radius and radius
            sphere.radius = m_SphereRadius.x + Random.value * (m_SphereRadius.y - m_SphereRadius.x);
            Vector2 randomPos = Random.insideUnitCircle * m_SpherePlacementRadius;
            sphere.position = new Vector3(randomPos.x, sphere.radius, randomPos.y);
            
            // Reject spheres that are intersecting others
            foreach (Sphere other in spheres)
            {
                float minDist = sphere.radius + other.radius;
                if (Vector3.SqrMagnitude(sphere.position - other.position) < minDist * minDist)
                    goto SkipSphere;
            }

            Color color = Random.ColorHSV();
            float chance = Random.value;
            if (chance < 0.8f)
            {
                bool metal = chance < 0.4f;
                sphere.albedo = metal ? Vector4.zero : new Vector4(color.r, color.g, color.b);
                sphere.specular = metal ? new Vector4(color.r, color.g, color.b) : new Vector4(0.04f, 0.04f, 0.04f);
                sphere.smoothness = Random.value;
            }
            else
            {
                Color emission = Random.ColorHSV(0, 1, 0, 1, 3.0f, 8.0f);
                sphere.emission = new Vector3(emission.r, emission.g, emission.b);
                sphere.specular = Vector3.one;
                sphere.albedo = Vector3.one;
                sphere.smoothness = 1;
            }

            // Add the sphere to the list
            spheres.Add(sphere);

        SkipSphere:
            continue;
        }

        // Assign to compute buffer
        _SphereBuffer?.Release();
        if (spheres.Count > 0)
        {
            _SphereBuffer = new ComputeBuffer(spheres.Count, 56);
            _SphereBuffer.SetData(spheres);
        }
    }

    private void SetShaderParameters()
    {
        m_RayTracingShader.SetTexture(_KiMain, "_SkyboxTexture", m_SkyboxTexture);
        m_RayTracingShader.SetMatrix("_CameraToWorld", _Camera.cameraToWorldMatrix);
        m_RayTracingShader.SetMatrix("_CameraInverseProjection", _Camera.projectionMatrix.inverse);
        m_RayTracingShader.SetVector("_PixelOffset", new Vector2(Random.value, Random.value));
        m_RayTracingShader.SetFloat("_Seed", Random.value);

        Vector3 l = m_DirectionalLight.transform.forward;
        m_RayTracingShader.SetVector("_DirectionalLight", new Vector4(l.x, l.y, l.z, m_DirectionalLight.intensity));

        if (_TextureBuffer != null)
        {
            m_RayTracingShader.SetTexture(_KiMain, "_TextureBuffer", _TextureBuffer);
        }
        SetComputeBuffer(_KiMain, "_Spheres", _SphereBuffer);
        SetComputeBuffer(_KiMain, "_MeshObjects", _MeshObjectBuffer);
        SetComputeBuffer(_KiMain, "_MeshMaterials", _MeshMaterialBuffer);
        SetComputeBuffer(_KiMain, "_Vertices", _VertexBuffer);
        SetComputeBuffer(_KiMain, "_Indices", _IndexBuffer);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F12))
        {
            ScreenCapture.CaptureScreenshot(Time.time + "-" + _CurrentSample + ".png");
        }

        if (_ScreenWidth != Screen.width || _ScreenHeight != Screen.height)
        {
            _CurrentSample = 0;
            _ScreenWidth = Screen.width;
            _ScreenHeight = Screen.height;
        }

        if (_Camera.fieldOfView != _LastFieldOfView)
        {
            _CurrentSample = 0;
            _LastFieldOfView = _Camera.fieldOfView;
        }

        foreach (Transform t in _TransformsToWatch)
        {
            if (t.hasChanged)
            {
                _CurrentSample = 0;
                t.hasChanged = false;
            }
        }
    }

    public void Render(CommandBuffer cmd)
    {
        RebuildMeshObjectBuffers();
        SetupSpheres();
        SetShaderParameters();
        
        // Make sure we have a current render target
        InitRenderTexture();

        m_RayTracingShader.SetTexture(_KiMain, "Result", _Target);
        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);
        m_RayTracingShader.Dispatch(_KiMain, threadGroupsX, threadGroupsY, 1);

        m_RayTracingShader.SetFloat("_Sample", _CurrentSample);
        // Blit the result texture to the screen       
        cmd.Blit(_Target, BuiltinRenderTextureType.CurrentActive);
        _CurrentSample++;
    }

    private void InitRenderTexture()
    {
        if (_Target == null || _Target.width != Screen.width || _Target.height != Screen.height)
        {
            // Release render texture if we already have one
            if (_Target != null)
            {
                _Target.Release();
            }
            
            // Get a render target for Ray Tracing
            _Target = new RenderTexture(Screen.width, Screen.height, 24,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _Target.enableRandomWrite = true;
            _Target.Create();

            // Reset sampling
            _CurrentSample = 0;
        }
    }

    private void OnValidate()
    {
        if(m_DirectionalLight == null)
        {
            var lights = FindObjectsOfType<Light>();
            foreach(var light in lights)
            {
                if(light.type == LightType.Directional)
                {
                    m_DirectionalLight = light;
                    break;
                }
            }
        }
    }
}
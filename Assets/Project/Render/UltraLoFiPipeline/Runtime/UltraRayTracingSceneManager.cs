using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace UltraLoFiPipeline
{
    [StructLayout(LayoutKind.Sequential)]
    public struct UltraRTTriangle
    {
        public Vector3 n0; public float u0;
        public Vector3 n1; public float v0;
        public Vector3 n2; public float u1;
        public float v1;   public float u2; public float v2; public float pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UltraRTInstanceData
    {
        public Matrix4x4 normalMatrix;       // Inverse-transpose (worldToLocal.transpose) for exact normals under non-uniform scale
        public Vector4 baseColorAndMetallic; // rgb: albedo, a: metallic
        public Vector4 surfaceParams;        // x: smoothness, y: reflectance, z: clearCoatMask, w: clearCoatSmoothness
        public Vector4 sssColorAndStrength;  // rgb: sssColor, a: sssStrength
        public Vector4 translucencyParams;   // x: translucencyStrength, y: thickness, z: sssWrap, w: patternType (0=smooth,1=stone,2=marble)
        public Vector4 emissionAndTexScale;  // rgb: emission, a: texScale
        public uint triangleOffset;
        public uint triangleCount;
        public uint flags;
        public uint pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UltraRTPointLight
    {
        public Vector4 positionAndRange;     // xyz: posWS, w: range
        public Vector4 colorAndIntensity;    // rgb: linear color * intensity, a: unused
    }

    /// <summary>
    /// Zero-allocation Hardware DXR Acceleration Structure (BVH) and GPU Geometry/Material Cache.
    /// Packs unique meshes once into GPU memory and performs dirty-only BVH builds and transform updates.
    /// </summary>
    public sealed class UltraRayTracingSceneManager : IDisposable
    {
        private struct CachedMeshSlice
        {
            public uint triangleOffset;
            public uint triangleCount;
        }

        private struct TrackedInstance
        {
            public MeshRenderer renderer;
            public Transform transform;
            public Mesh mesh;
            public Material material;
            public int accelHandle;
            public uint instanceIndex;
            public CachedMeshSlice meshSlice;
        }

        private RayTracingAccelerationStructure m_AccelStruct;
        private GraphicsBuffer m_TriangleBuffer;
        private GraphicsBuffer m_InstanceBuffer;
        private GraphicsBuffer m_LightBuffer;

        private readonly Dictionary<Mesh, CachedMeshSlice> m_MeshCache = new Dictionary<Mesh, CachedMeshSlice>();
        private readonly List<UltraRTTriangle> m_AllTriangles = new List<UltraRTTriangle>(16384);
        private readonly List<TrackedInstance> m_TrackedInstances = new List<TrackedInstance>(128);
        private readonly List<Light> m_TrackedLights = new List<Light>(32);

        private UltraRTInstanceData[] m_InstanceDataArray = Array.Empty<UltraRTInstanceData>();
        private readonly UltraRTPointLight[] m_LightDataArray = new UltraRTPointLight[32];

        private bool m_TriangleBufferDirty = true;
        private bool m_BvhNeedsRebuild = true;
        private string m_LastScenePath = string.Empty;
        private int m_LastHierarchyCheckFrame = -1000;
        private int m_ActiveLightCount;

        // Cached Shader Property IDs
        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_ColorId = Shader.PropertyToID("_Color");
        private static readonly int s_BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int s_MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int s_SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int s_ReflectanceId = Shader.PropertyToID("_Reflectance");
        private static readonly int s_ClearCoatMaskId = Shader.PropertyToID("_ClearCoatMask");
        private static readonly int s_ClearCoatSmoothnessId = Shader.PropertyToID("_ClearCoatSmoothness");
        private static readonly int s_SubsurfaceColorId = Shader.PropertyToID("_SubsurfaceColor");
        private static readonly int s_SubsurfaceStrengthId = Shader.PropertyToID("_SubsurfaceStrength");
        private static readonly int s_SubsurfaceWrapId = Shader.PropertyToID("_SubsurfaceWrap");
        private static readonly int s_TranslucencyStrengthId = Shader.PropertyToID("_TranslucencyStrength");
        private static readonly int s_ThicknessId = Shader.PropertyToID("_Thickness");
        private static readonly int s_EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public RayTracingAccelerationStructure AccelStruct => m_AccelStruct;
        public GraphicsBuffer TriangleBuffer => m_TriangleBuffer;
        public GraphicsBuffer InstanceBuffer => m_InstanceBuffer;
        public GraphicsBuffer LightBuffer => m_LightBuffer;
        public int ActiveInstanceCount => m_TrackedInstances.Count;
        public int ActiveLightCount => m_ActiveLightCount;

        public static bool IsHardwareRayTracingSupported()
        {
            return SystemInfo.supportsRayTracing;
        }

        public void MarkSceneDirty()
        {
            m_LastScenePath = string.Empty;
            m_BvhNeedsRebuild = true;
        }

        public bool SyncScene(CommandBuffer cmd)
        {
            if (!IsHardwareRayTracingSupported())
                return false;

            EnsureInitialized();

            string currentScenePath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            int frame = Time.frameCount;

            bool needTopologyCheck = (currentScenePath != m_LastScenePath) ||
                                     (m_TrackedInstances.Count == 0) ||
                                     (!Application.isPlaying && (frame - m_LastHierarchyCheckFrame > 30)) ||
                                     HasInvalidTrackedRenderers();

            if (needTopologyCheck)
            {
                m_LastHierarchyCheckFrame = frame;
                var renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
                if (currentScenePath != m_LastScenePath || renderers.Length != m_TrackedInstances.Count || HasInvalidTrackedRenderers())
                {
                    RebuildTopology(renderers, currentScenePath);
                }
            }

            if (m_TrackedInstances.Count == 0)
                return false;

            UpdateDynamicInstances(!Application.isPlaying);
            UpdatePunctualLights();

            if (m_BvhNeedsRebuild)
            {
                var buildSettings = new RayTracingAccelerationStructure.BuildSettings
                {
                    buildFlags = RayTracingAccelerationStructureBuildFlags.PreferFastTrace,
                    relativeOrigin = Vector3.zero
                };
                cmd.BuildRayTracingAccelerationStructure(m_AccelStruct, buildSettings);
                m_BvhNeedsRebuild = false;
            }

            return true;
        }

        private void EnsureInitialized()
        {
            if (m_AccelStruct == null)
            {
                var settings = new RayTracingAccelerationStructure.Settings
                {
                    managementMode = RayTracingAccelerationStructure.ManagementMode.Manual,
                    rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                    layerMask = ~0
                };
                m_AccelStruct = new RayTracingAccelerationStructure(settings);
                m_BvhNeedsRebuild = true;
            }

            if (m_LightBuffer == null)
            {
                m_LightBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_LightDataArray.Length, Marshal.SizeOf<UltraRTPointLight>());
            }
        }

        private bool HasInvalidTrackedRenderers()
        {
            for (int i = 0; i < m_TrackedInstances.Count; i++)
            {
                var inst = m_TrackedInstances[i];
                if (inst.renderer == null || !inst.renderer.enabled || !inst.renderer.gameObject.activeInHierarchy)
                    return true;
            }
            return false;
        }

        private void RebuildTopology(MeshRenderer[] renderers, string scenePath)
        {
            m_LastScenePath = scenePath;
            m_AccelStruct.ClearInstances();
            m_TrackedInstances.Clear();

            uint instanceIdx = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer mr = renderers[i];
                if (mr == null || !mr.enabled || !mr.gameObject.activeInHierarchy)
                    continue;

                MeshFilter mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null)
                    continue;

                Mesh mesh = mf.sharedMesh;
                if (mesh.subMeshCount == 0)
                    continue;

                CachedMeshSlice slice = GetOrCacheMeshTriangles(mesh);
                if (slice.triangleCount == 0)
                    continue;

                var config = new RayTracingMeshInstanceConfig(mesh, 0, null)
                {
                    mask = 0xFF,
                    enableTriangleCulling = false,
                    frontTriangleCounterClockwise = false,
                    subMeshFlags = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly
                };

                Matrix4x4 l2w = mr.transform.localToWorldMatrix;
                int handle = m_AccelStruct.AddInstance(config, l2w, null, instanceIdx);

                m_TrackedInstances.Add(new TrackedInstance
                {
                    renderer = mr,
                    transform = mr.transform,
                    mesh = mesh,
                    material = mr.sharedMaterial,
                    accelHandle = handle,
                    instanceIndex = instanceIdx,
                    meshSlice = slice
                });

                mr.transform.hasChanged = false;
                instanceIdx++;
            }

            if (m_TriangleBufferDirty && m_AllTriangles.Count > 0)
            {
                if (m_TriangleBuffer == null || m_TriangleBuffer.count < m_AllTriangles.Count)
                {
                    m_TriangleBuffer?.Release();
                    int cap = Mathf.NextPowerOfTwo(Mathf.Max(256, m_AllTriangles.Count));
                    m_TriangleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, Marshal.SizeOf<UltraRTTriangle>());
                }
                m_TriangleBuffer.SetData(m_AllTriangles);
                m_TriangleBufferDirty = false;
            }

            int instCount = Mathf.Max(1, m_TrackedInstances.Count);
            if (m_InstanceDataArray.Length < instCount)
                m_InstanceDataArray = new UltraRTInstanceData[Mathf.NextPowerOfTwo(instCount)];

            if (m_InstanceBuffer == null || m_InstanceBuffer.count < instCount)
            {
                m_InstanceBuffer?.Release();
                m_InstanceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_InstanceDataArray.Length, Marshal.SizeOf<UltraRTInstanceData>());
            }

            for (int i = 0; i < m_TrackedInstances.Count; i++)
            {
                PopulateInstanceData(m_TrackedInstances[i], ref m_InstanceDataArray[i]);
            }

            if (m_TrackedInstances.Count > 0)
                m_InstanceBuffer.SetData(m_InstanceDataArray, 0, 0, m_TrackedInstances.Count);

            // Cache scene lights once on topology rebuild
            m_TrackedLights.Clear();
            var allLights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < allLights.Length; i++)
            {
                if (allLights[i] != null && allLights[i].type != LightType.Directional)
                    m_TrackedLights.Add(allLights[i]);
            }

            m_BvhNeedsRebuild = true;
        }

        private void UpdateDynamicInstances(bool syncMaterialsInEditor)
        {
            bool anyTransformMoved = false;
            bool anyDataChanged = false;

            for (int i = 0; i < m_TrackedInstances.Count; i++)
            {
                TrackedInstance tracked = m_TrackedInstances[i];
                if (tracked.renderer == null)
                    continue;

                bool trChanged = tracked.transform.hasChanged;
                if (trChanged)
                {
                    Matrix4x4 l2w = tracked.transform.localToWorldMatrix;
                    m_AccelStruct.UpdateInstanceTransform(tracked.accelHandle, l2w);
                    tracked.transform.hasChanged = false;
                    anyTransformMoved = true;
                }

                if (trChanged || syncMaterialsInEditor)
                {
                    PopulateInstanceData(tracked, ref m_InstanceDataArray[i]);
                    anyDataChanged = true;
                }
            }

            if (anyTransformMoved)
                m_BvhNeedsRebuild = true;

            if (anyDataChanged)
                m_InstanceBuffer.SetData(m_InstanceDataArray, 0, 0, m_TrackedInstances.Count);
        }

        private void PopulateInstanceData(in TrackedInstance tracked, ref UltraRTInstanceData data)
        {
            // Inverse-transpose matrix transforms object-space normals accurately even under non-uniform scale
            data.normalMatrix = tracked.transform.worldToLocalMatrix.transpose;
            data.triangleOffset = tracked.meshSlice.triangleOffset;
            data.triangleCount = tracked.meshSlice.triangleCount;
            data.flags = 1u;

            Material mat = tracked.renderer.sharedMaterial;
            if (mat == null)
            {
                data.baseColorAndMetallic = new Vector4(0.8f, 0.8f, 0.8f, 0.0f);
                data.surfaceParams = new Vector4(0.5f, 0.5f, 0.0f, 0.0f);
                data.sssColorAndStrength = Vector4.zero;
                data.translucencyParams = Vector4.zero;
                data.emissionAndTexScale = new Vector4(0f, 0f, 0f, 1f);
                return;
            }

            Color baseCol = mat.HasProperty(s_BaseColorId) ? mat.GetColor(s_BaseColorId)
                          : (mat.HasProperty(s_ColorId) ? mat.GetColor(s_ColorId) : Color.white);
            float metallic = mat.HasProperty(s_MetallicId) ? mat.GetFloat(s_MetallicId) : 0.0f;
            float smoothness = mat.HasProperty(s_SmoothnessId) ? mat.GetFloat(s_SmoothnessId) : 0.5f;
            float reflectance = mat.HasProperty(s_ReflectanceId) ? mat.GetFloat(s_ReflectanceId) : 0.5f;
            float ccMask = mat.HasProperty(s_ClearCoatMaskId) ? mat.GetFloat(s_ClearCoatMaskId) : 0.0f;
            float ccSmooth = mat.HasProperty(s_ClearCoatSmoothnessId) ? mat.GetFloat(s_ClearCoatSmoothnessId) : 0.9f;

            Color sssCol = mat.HasProperty(s_SubsurfaceColorId) ? mat.GetColor(s_SubsurfaceColorId) : new Color(1f, 0.3f, 0.1f);
            float sssStr = mat.HasProperty(s_SubsurfaceStrengthId) ? mat.GetFloat(s_SubsurfaceStrengthId) : 0.0f;
            float sssWrap = mat.HasProperty(s_SubsurfaceWrapId) ? mat.GetFloat(s_SubsurfaceWrapId) : 0.45f;
            float transStr = mat.HasProperty(s_TranslucencyStrengthId) ? mat.GetFloat(s_TranslucencyStrengthId) : 0.0f;
            float thickness = mat.HasProperty(s_ThicknessId) ? mat.GetFloat(s_ThicknessId) : 0.25f;
            Color emission = mat.HasProperty(s_EmissionColorId) ? mat.GetColor(s_EmissionColorId) : Color.black;

            float texScale = 1.0f;
            float patternType = 0.0f;
            if (mat.HasProperty(s_BaseMapId))
            {
                Vector2 scale = mat.GetTextureScale(s_BaseMapId);
                texScale = Mathf.Max(1.0f, (scale.x + scale.y) * 0.5f);
                Texture tex = mat.GetTexture(s_BaseMapId);
                if (tex != null)
                {
                    string texName = tex.name;
                    if (texName.Contains("Stone")) patternType = 1.0f;
                    else if (texName.Contains("Marble")) patternType = 2.0f;
                }
            }

            data.baseColorAndMetallic = new Vector4(baseCol.r, baseCol.g, baseCol.b, metallic);
            data.surfaceParams = new Vector4(smoothness, reflectance, ccMask, ccSmooth);
            data.sssColorAndStrength = new Vector4(sssCol.r, sssCol.g, sssCol.b, sssStr);
            data.translucencyParams = new Vector4(transStr, thickness, sssWrap, patternType);
            data.emissionAndTexScale = new Vector4(emission.r, emission.g, emission.b, texScale);
        }

        private CachedMeshSlice GetOrCacheMeshTriangles(Mesh mesh)
        {
            if (m_MeshCache.TryGetValue(mesh, out CachedMeshSlice cached))
                return cached;

            Vector3[] verts = mesh.vertices;
            Vector3[] norms = mesh.normals;
            Vector2[] uvs = mesh.uv;
            int[] tris = mesh.GetTriangles(0);

            bool hasNorms = norms != null && norms.Length == verts.Length;
            bool hasUVs = uvs != null && uvs.Length == verts.Length;

            uint startOffset = (uint)m_AllTriangles.Count;
            uint triCount = (uint)(tris.Length / 3);

            for (int t = 0; t < tris.Length; t += 3)
            {
                int i0 = tris[t];
                int i1 = tris[t + 1];
                int i2 = tris[t + 2];

                Vector3 n0, n1, n2;
                if (hasNorms)
                {
                    n0 = norms[i0];
                    n1 = norms[i1];
                    n2 = norms[i2];
                }
                else
                {
                    Vector3 faceN = Vector3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]).normalized;
                    n0 = n1 = n2 = faceN;
                }

                Vector2 uv0 = hasUVs ? uvs[i0] : Vector2.zero;
                Vector2 uv1 = hasUVs ? uvs[i1] : Vector2.zero;
                Vector2 uv2 = hasUVs ? uvs[i2] : Vector2.zero;

                m_AllTriangles.Add(new UltraRTTriangle
                {
                    n0 = n0, u0 = uv0.x,
                    n1 = n1, v0 = uv0.y,
                    n2 = n2, u1 = uv1.x,
                    v1 = uv1.y, u2 = uv2.x, v2 = uv2.y, pad = 0f
                });
            }

            var slice = new CachedMeshSlice
            {
                triangleOffset = startOffset,
                triangleCount = triCount
            };
            m_MeshCache[mesh] = slice;
            m_TriangleBufferDirty = true;
            return slice;
        }

        private void UpdatePunctualLights()
        {
            int count = 0;
            for (int i = 0; i < m_TrackedLights.Count && count < m_LightDataArray.Length; i++)
            {
                Light l = m_TrackedLights[i];
                if (l == null || !l.isActiveAndEnabled)
                    continue;

                Vector3 pos = l.transform.position;
                Color linearCol = l.color.linear * l.intensity;

                m_LightDataArray[count] = new UltraRTPointLight
                {
                    positionAndRange = new Vector4(pos.x, pos.y, pos.z, Mathf.Max(0.1f, l.range)),
                    colorAndIntensity = new Vector4(linearCol.r, linearCol.g, linearCol.b, 1.0f)
                };
                count++;
            }

            m_ActiveLightCount = count;
            if (count > 0)
                m_LightBuffer.SetData(m_LightDataArray, 0, 0, count);
        }

        public void Dispose()
        {
            m_AccelStruct?.Dispose();
            m_AccelStruct = null;

            m_TriangleBuffer?.Release();
            m_TriangleBuffer = null;

            m_InstanceBuffer?.Release();
            m_InstanceBuffer = null;

            m_LightBuffer?.Release();
            m_LightBuffer = null;

            m_MeshCache.Clear();
            m_AllTriangles.Clear();
            m_TrackedInstances.Clear();
            m_TrackedLights.Clear();
        }
    }
}

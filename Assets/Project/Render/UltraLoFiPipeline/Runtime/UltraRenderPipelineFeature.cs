using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace UltraLoFiPipeline
{
    /// <summary>
    /// Modular, portable URP ScriptableRendererFeature implementing the Ultra Lo-Fi Realistic Render Pipeline:
    /// - Single-Pass Hardware DXR Ray Tracing (RTR 2-Bounce Reflections, RTGI, RTAO, Ray-Traced SSS Thickness, Shadows & Volumetrics)
    ///   with automatic fallback to screen-space passes if Hardware Ray Tracing is disabled or unsupported
    /// - Low-resolution RenderTexture virtual framebuffer rendering & parameterized fullscreen stretch
    /// - Multi-mip Dual-Kawase HDR Bloom, Spectral Chromatic Aberration, Bokeh DOF, AgX/ACES Tone Mapping, and Bayer Dithering
    /// </summary>
    [DisallowMultipleRendererFeature("Ultra Lo-Fi Realistic Pipeline")]
    public class UltraRenderPipelineFeature : ScriptableRendererFeature
    {
        [Tooltip("Active pipeline profile containing all lighting, low-res render texture, aliasing, ray tracing, and post-processing parameters.")]
        public UltraPipelineProfile profile;

        [Tooltip("Hardware DXR RayTracingShader asset (UltraHardwareRayTracing.raytrace).")]
        public RayTracingShader hardwareRayTracingShader;

        [Tooltip("Automatically find or create a default profile if none is assigned.")]
        public bool autoAssignFallbackProfile = true;

        [NonSerialized] private Shader m_LightingShader;
        [NonSerialized] private Shader m_PostShader;
        [NonSerialized] private Material m_LightingMaterial;
        [NonSerialized] private Material m_PostMaterial;
        [NonSerialized] private UltraRenderPipelinePass m_PipelinePass;
        [NonSerialized] private UltraPipelineProfile m_RuntimeFallbackProfile;
        [NonSerialized] private UltraRayTracingSceneManager m_RTSceneManager;

        private RTHandle m_PersistentLowResHandle;
        private int m_LastExportFrame = -1;

        /// <summary>
        /// Live low-resolution RenderTexture produced each frame before stretching to the full screen.
        /// </summary>
        public static RenderTexture CurrentLowResRenderTexture { get; private set; }

        /// <summary>
        /// Current active low-resolution dimensions (Width x Height).
        /// </summary>
        public static Vector2Int CurrentLowResDimensions { get; private set; }

        /// <summary>
        /// True when Hardware DXR Ray Tracing is active this frame.
        /// </summary>
        public static bool IsHardwareRayTracingActive { get; private set; }

        public override void Create()
        {
            m_LightingShader = Shader.Find("Hidden/UltraLoFiPipeline/UltraLightingEffects");
            m_PostShader = Shader.Find("Hidden/UltraLoFiPipeline/UltraPostProcessAndUpscale");

            if (m_LightingShader != null && m_LightingMaterial == null)
                m_LightingMaterial = CoreUtils.CreateEngineMaterial(m_LightingShader);

            if (m_PostShader != null && m_PostMaterial == null)
                m_PostMaterial = CoreUtils.CreateEngineMaterial(m_PostShader);

#if UNITY_EDITOR
            if (hardwareRayTracingShader == null)
            {
                hardwareRayTracingShader = UnityEditor.AssetDatabase.LoadAssetAtPath<RayTracingShader>(
                    "Assets/UltraLoFiPipeline/Shaders/UltraHardwareRayTracing.raytrace");

                if (hardwareRayTracingShader == null)
                {
                    string[] rtGuids = UnityEditor.AssetDatabase.FindAssets("UltraHardwareRayTracing t:RayTracingShader");
                    if (rtGuids != null && rtGuids.Length > 0)
                    {
                        string rtPath = UnityEditor.AssetDatabase.GUIDToAssetPath(rtGuids[0]);
                        hardwareRayTracingShader = UnityEditor.AssetDatabase.LoadAssetAtPath<RayTracingShader>(rtPath);
                    }
                }
            }
#endif

            if (m_RTSceneManager == null && UltraRayTracingSceneManager.IsHardwareRayTracingSupported())
                m_RTSceneManager = new UltraRayTracingSceneManager();

            if (m_PipelinePass == null)
                m_PipelinePass = new UltraRenderPipelinePass("Ultra Lo-Fi Realistic Pipeline");
        }

        private UltraPipelineProfile GetEffectiveProfile()
        {
            if (profile != null)
                return profile;

            if (UltraLowResPipelineManager.ActiveInstance != null && UltraLowResPipelineManager.ActiveInstance.profile != null)
            {
                profile = UltraLowResPipelineManager.ActiveInstance.profile;
                return profile;
            }

            if (autoAssignFallbackProfile)
            {
                if (m_RuntimeFallbackProfile == null)
                {
                    m_RuntimeFallbackProfile = ScriptableObject.CreateInstance<UltraPipelineProfile>();
                    m_RuntimeFallbackProfile.name = "RuntimeFallback_UltraPipelineProfile";
                    m_RuntimeFallbackProfile.hideFlags = HideFlags.HideAndDontSave;
                }
                return m_RuntimeFallbackProfile;
            }

            return null;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType camType = renderingData.cameraData.cameraType;
            if (camType == CameraType.Preview || camType == CameraType.Reflection)
                return;

            UltraPipelineProfile activeProfile = GetEffectiveProfile();
            if (activeProfile == null)
                return;

            if (m_LightingMaterial == null || m_PostMaterial == null)
            {
                Create();
                if (m_LightingMaterial == null || m_PostMaterial == null)
                    return;
            }

            // Guard against missing/uncompiled shaders so the camera never turns solid magenta
            if (m_LightingShader == null || !m_LightingShader.isSupported || m_LightingMaterial.passCount < 6 ||
                m_PostShader == null || !m_PostShader.isSupported || m_PostMaterial.passCount < 6)
            {
                return;
            }

            if (activeProfile.forceDisableAntiAliasing)
            {
                renderingData.cameraData.antialiasing = AntialiasingMode.None;
            }

            int fullW = Mathf.Max(64, renderingData.cameraData.camera.pixelWidth);
            int fullH = Mathf.Max(64, renderingData.cameraData.camera.pixelHeight);
            Vector2Int lowResSize = activeProfile.ComputeLowResolution(fullW, fullH);
            CurrentLowResDimensions = lowResSize;

            RTHandle exportHandle = null;
            if (activeProfile.exposeLowResRenderTexture && camType == CameraType.Game && m_LastExportFrame != Time.frameCount)
            {
                EnsurePersistentLowResTexture(lowResSize.x, lowResSize.y, activeProfile.upscaleFilter);
                exportHandle = m_PersistentLowResHandle;
                m_LastExportFrame = Time.frameCount;
            }

            bool canUseRT = activeProfile.useHardwareRayTracing &&
                            UltraRayTracingSceneManager.IsHardwareRayTracingSupported() &&
                            hardwareRayTracingShader != null &&
                            m_RTSceneManager != null;
            IsHardwareRayTracingActive = canUseRT;

            m_PipelinePass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            m_PipelinePass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Color);
            m_PipelinePass.requiresIntermediateTexture = true;
            m_PipelinePass.Setup(activeProfile, m_LightingMaterial, m_PostMaterial, hardwareRayTracingShader, m_RTSceneManager, exportHandle, canUseRT);

            renderer.EnqueuePass(m_PipelinePass);
        }

        private void EnsurePersistentLowResTexture(int width, int height, UpscaleFilterMode filter)
        {
            FilterMode targetFilter = (filter == UpscaleFilterMode.Bilinear) ? FilterMode.Bilinear : FilterMode.Point;

            if (m_PersistentLowResHandle == null ||
                m_PersistentLowResHandle.rt == null ||
                m_PersistentLowResHandle.rt.width != width ||
                m_PersistentLowResHandle.rt.height != height)
            {
                m_PersistentLowResHandle?.Release();
                var desc = new RenderTextureDescriptor(width, height, GraphicsFormat.R16G16B16A16_SFloat, 0)
                {
                    msaaSamples = 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                    sRGB = false
                };
                m_PersistentLowResHandle = RTHandles.Alloc(desc, targetFilter, TextureWrapMode.Clamp, name: "_UltraLowResFrameBuffer");
                CurrentLowResRenderTexture = m_PersistentLowResHandle.rt;
            }
            else if (m_PersistentLowResHandle.rt.filterMode != targetFilter)
            {
                m_PersistentLowResHandle.rt.filterMode = targetFilter;
            }
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_LightingMaterial);
            CoreUtils.Destroy(m_PostMaterial);
            m_LightingMaterial = null;
            m_PostMaterial = null;

            if (m_RuntimeFallbackProfile != null)
            {
                CoreUtils.Destroy(m_RuntimeFallbackProfile);
                m_RuntimeFallbackProfile = null;
            }

            m_RTSceneManager?.Dispose();
            m_RTSceneManager = null;

            m_PersistentLowResHandle?.Release();
            m_PersistentLowResHandle = null;
            CurrentLowResRenderTexture = null;
        }

        // ==================================================================
        // RenderGraph Execution Pass (URP 17 / Unity 6+)
        // ==================================================================
        private sealed class UltraRenderPipelinePass : ScriptableRenderPass
        {
            private UltraPipelineProfile m_Profile;
            private Material m_LightingMat;
            private Material m_PostMat;
            private RayTracingShader m_RTShader;
            private UltraRayTracingSceneManager m_RTSceneMgr;
            private RTHandle m_ExportLowResHandle;
            private bool m_UseHardwareRT;

            // Shader Property IDs
            private static readonly int s_SourceColorTexId = Shader.PropertyToID("_SourceColorTex");
            private static readonly int s_GITexId = Shader.PropertyToID("_GITex");
            private static readonly int s_AOTexId = Shader.PropertyToID("_AOTex");
            private static readonly int s_SSSTexId = Shader.PropertyToID("_SSSTex");
            private static readonly int s_SSRVolumetricTexId = Shader.PropertyToID("_SSRVolumetricTex");

            private static readonly int s_UltraAccelStructId = Shader.PropertyToID("_UltraAccelStruct");
            private static readonly int s_UltraRTTrianglesId = Shader.PropertyToID("_UltraRTTriangles");
            private static readonly int s_UltraRTInstancesId = Shader.PropertyToID("_UltraRTInstances");
            private static readonly int s_UltraRTLightsId = Shader.PropertyToID("_UltraRTLights");
            private static readonly int s_CameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");
            private static readonly int s_CameraNormalsTextureId = Shader.PropertyToID("_CameraNormalsTexture");
            private static readonly int s_OutRTAOId = Shader.PropertyToID("_OutRTAO");
            private static readonly int s_OutRTGIAndShadowsId = Shader.PropertyToID("_OutRTGIAndShadows");
            private static readonly int s_OutRTReflectionsAndVolumetricsId = Shader.PropertyToID("_OutRTReflectionsAndVolumetrics");
            private static readonly int s_OutRTSubsurfaceId = Shader.PropertyToID("_OutRTSubsurface");

            private static readonly int s_MainSourceTexId = Shader.PropertyToID("_MainSourceTex");
            private static readonly int s_BloomMipTexId = Shader.PropertyToID("_BloomMipTex");
            private static readonly int s_BloomLowMipTexId = Shader.PropertyToID("_BloomLowMipTex");
            private static readonly int s_LowResFinalTexId = Shader.PropertyToID("_LowResFinalTex");

            private static readonly int s_UltraCamPosWSId = Shader.PropertyToID("_UltraCamPosWS");
            private static readonly int s_UltraCamForwardId = Shader.PropertyToID("_UltraCamForward");
            private static readonly int s_UltraCamRightId = Shader.PropertyToID("_UltraCamRight");
            private static readonly int s_UltraCamUpId = Shader.PropertyToID("_UltraCamUp");
            private static readonly int s_UltraCamProjParamsId = Shader.PropertyToID("_UltraCamProjParams");
            private static readonly int s_UltraLowResSizeId = Shader.PropertyToID("_UltraLowResSize");
            private static readonly int s_UltraFullScreenSizeId = Shader.PropertyToID("_UltraFullScreenSize");
            private static readonly int s_UltraMipTexelSizeId = Shader.PropertyToID("_UltraMipTexelSize");
            private static readonly int s_UltraFrameParamsId = Shader.PropertyToID("_UltraFrameParams");
            private static readonly int s_UltraZBufferParamsId = Shader.PropertyToID("_UltraZBufferParams");
            private static readonly int s_UltraMainLightDirId = Shader.PropertyToID("_UltraMainLightDir");
            private static readonly int s_UltraMainLightColorId = Shader.PropertyToID("_UltraMainLightColor");
            private static readonly int s_UltraRTParamsId = Shader.PropertyToID("_UltraRTParams");

            private static readonly int s_UltraAOParams1Id = Shader.PropertyToID("_UltraAOParams1");
            private static readonly int s_UltraAOParams2Id = Shader.PropertyToID("_UltraAOParams2");

            private static readonly int s_UltraGIParams1Id = Shader.PropertyToID("_UltraGIParams1");
            private static readonly int s_UltraGIParams2Id = Shader.PropertyToID("_UltraGIParams2");
            private static readonly int s_UltraGISkyColorId = Shader.PropertyToID("_UltraGISkyColor");
            private static readonly int s_UltraGIGroundColorId = Shader.PropertyToID("_UltraGIGroundColor");
            private static readonly int s_UltraGIDenoiseParamsId = Shader.PropertyToID("_UltraGIDenoiseParams");
            private static readonly int s_UltraContactShadowParamsId = Shader.PropertyToID("_UltraContactShadowParams");

            private static readonly int s_UltraSSSParamsId = Shader.PropertyToID("_UltraSSSParams");
            private static readonly int s_UltraSSSScatterColorId = Shader.PropertyToID("_UltraSSSScatterColor");

            private static readonly int s_UltraSSRParamsId = Shader.PropertyToID("_UltraSSRParams");
            private static readonly int s_UltraSSRParams2Id = Shader.PropertyToID("_UltraSSRParams2");
            private static readonly int s_UltraVolumetricParamsId = Shader.PropertyToID("_UltraVolumetricParams");
            private static readonly int s_UltraVolumetricParams2Id = Shader.PropertyToID("_UltraVolumetricParams2");
            private static readonly int s_UltraVolumetricColorId = Shader.PropertyToID("_UltraVolumetricColor");

            private static readonly int s_UltraBloomParamsId = Shader.PropertyToID("_UltraBloomParams");
            private static readonly int s_UltraBloomTintId = Shader.PropertyToID("_UltraBloomTint");
            private static readonly int s_UltraCAParamsId = Shader.PropertyToID("_UltraCAParams");
            private static readonly int s_UltraDOFParamsId = Shader.PropertyToID("_UltraDOFParams");
            private static readonly int s_UltraVignetteParamsId = Shader.PropertyToID("_UltraVignetteParams");
            private static readonly int s_UltraVignetteColorId = Shader.PropertyToID("_UltraVignetteColor");
            private static readonly int s_UltraGrainParamsId = Shader.PropertyToID("_UltraGrainParams");
            private static readonly int s_UltraGradingParams1Id = Shader.PropertyToID("_UltraGradingParams1");
            private static readonly int s_UltraGradingParams2Id = Shader.PropertyToID("_UltraGradingParams2");
            private static readonly int s_UltraShadowsTintId = Shader.PropertyToID("_UltraShadowsTint");
            private static readonly int s_UltraMidtonesTintId = Shader.PropertyToID("_UltraMidtonesTint");
            private static readonly int s_UltraHighlightsTintId = Shader.PropertyToID("_UltraHighlightsTint");
            private static readonly int s_UltraAliasingParamsId = Shader.PropertyToID("_UltraAliasingParams");
            private static readonly int s_UltraUpscaleParamsId = Shader.PropertyToID("_UltraUpscaleParams");

            public UltraRenderPipelinePass(string name)
            {
                profilingSampler = new ProfilingSampler(name);
            }

            public void Setup(
                UltraPipelineProfile profile,
                Material lightingMat,
                Material postMat,
                RayTracingShader rtShader,
                UltraRayTracingSceneManager rtSceneMgr,
                RTHandle exportLowResHandle,
                bool useHardwareRT)
            {
                m_Profile = profile;
                m_LightingMat = lightingMat;
                m_PostMat = postMat;
                m_RTShader = rtShader;
                m_RTSceneMgr = rtSceneMgr;
                m_ExportLowResHandle = exportLowResHandle;
                m_UseHardwareRT = useHardwareRT;
            }

            private class PassData
            {
                public Material material;
                public int passIndex;
                public TextureHandle texA;
                public TextureHandle texB;
                public TextureHandle texC;
                public TextureHandle texD;
                public TextureHandle texE;
                public Vector4 extraVec;
            }

            private class RTPassData
            {
                public RayTracingShader rtShader;
                public UltraRayTracingSceneManager rtSceneMgr;
                public UltraPipelineProfile profile;
                public UltraPipelineVolumeComponent vol;
                public Camera camera;
                public int lowW;
                public int lowH;
                public int fullW;
                public int fullH;
                public TextureHandle sourceColor;
                public TextureHandle depthTex;
                public TextureHandle normalsTex;
                public TextureHandle outAO;
                public TextureHandle outGI;
                public TextureHandle outSSRVol;
                public TextureHandle outSSS;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (m_Profile == null || m_LightingMat == null || m_PostMat == null)
                    return;

                UniversalResourceData resourcesData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                if (!resourcesData.cameraColor.IsValid())
                    return;

                cameraData.postProcessEnabled = false;
                if (m_Profile.forceDisableAntiAliasing)
                    cameraData.antialiasing = AntialiasingMode.None;

                var volumeStack = VolumeManager.instance.stack;
                var vol = volumeStack != null ? volumeStack.GetComponent<UltraPipelineVolumeComponent>() : null;

                int fullW = Mathf.Max(64, cameraData.camera.pixelWidth);
                int fullH = Mathf.Max(64, cameraData.camera.pixelHeight);

                Vector2Int lowResSize = m_Profile.ComputeLowResolution(fullW, fullH);
                if (vol != null && vol.enableLowResOverride.overrideState && vol.enableLowResOverride.value)
                {
                    if (vol.targetVerticalHeight.overrideState)
                    {
                        int h = Mathf.Clamp(vol.targetVerticalHeight.value, 32, fullH);
                        int w = Mathf.Max(32, Mathf.RoundToInt(h * ((float)fullW / fullH)));
                        lowResSize = new Vector2Int((w / 2) * 2, (h / 2) * 2);
                    }
                    else if (vol.downscaleFactor.overrideState)
                    {
                        float f = Mathf.Max(1.0f, vol.downscaleFactor.value);
                        int w = Mathf.Max(32, Mathf.RoundToInt(fullW / f));
                        int h = Mathf.Max(32, Mathf.RoundToInt(fullH / f));
                        lowResSize = new Vector2Int((w / 2) * 2, (h / 2) * 2);
                    }
                }

                int lowW = lowResSize.x;
                int lowH = lowResSize.y;

                bool executeHardwareRT = m_UseHardwareRT &&
                                         resourcesData.cameraDepthTexture.IsValid() &&
                                         resourcesData.cameraNormalsTexture.IsValid();

                UpdateMaterialUniforms(cameraData.camera, m_Profile, vol, lowW, lowH, fullW, fullH, executeHardwareRT);

                TextureDesc cameraColorDesc = renderGraph.GetTextureDesc(resourcesData.cameraColor);

                TextureDesc lowResDesc = cameraColorDesc;
                lowResDesc.width = lowW;
                lowResDesc.height = lowH;
                lowResDesc.format = GraphicsFormat.R16G16B16A16_SFloat;
                lowResDesc.filterMode = FilterMode.Point;
                lowResDesc.clearBuffer = false;
                lowResDesc.msaaSamples = MSAASamples.None;
                lowResDesc.enableRandomWrite = executeHardwareRT;

                // 1. Capture scene color into the low-resolution HDR RenderTexture
                lowResDesc.name = "_UltraLowResSceneColor";
                TextureHandle lowResSceneColor = renderGraph.CreateTexture(lowResDesc);
                RecordSimpleCopyPass(renderGraph, "Ultra Capture Low-Res Color", resourcesData.cameraColor, lowResSceneColor);

                lowResDesc.name = "_UltraLowResGTAO";
                TextureHandle aoTexture = renderGraph.CreateTexture(lowResDesc);

                lowResDesc.name = "_UltraLowResSSGI_Raw";
                TextureHandle giCurrent = renderGraph.CreateTexture(lowResDesc);

                lowResDesc.name = "_UltraLowResSSSS";
                TextureHandle sssTexture = renderGraph.CreateTexture(lowResDesc);

                lowResDesc.name = "_UltraLowResSSR_Volumetrics";
                TextureHandle ssrVolTexture = renderGraph.CreateTexture(lowResDesc);

                if (executeHardwareRT)
                {
                    // ==============================================================
                    // SINGLE-PASS HARDWARE DXR RAY TRACING (RTAO + RTGI + 2-Bounce RTR + SSS Thickness + Shadows + Volumetrics)
                    // ==============================================================
                    RecordHardwareRayTracingPass(
                        renderGraph,
                        cameraData.camera,
                        m_Profile,
                        vol,
                        lowW, lowH, fullW, fullH,
                        lowResSceneColor,
                        resourcesData.cameraDepthTexture,
                        resourcesData.cameraNormalsTexture,
                        aoTexture,
                        giCurrent,
                        ssrVolTexture,
                        sssTexture);

                    // Fast Edge-Preserving À-Trous Spatial Denoiser on the Ray-Traced GI buffer
                    lowResDesc.enableRandomWrite = false;
                    int denoiseIterations = m_Profile.enableSSGI ? Mathf.Clamp(m_Profile.giDenoiseIterations, 0, 4) : 0;
                    for (int i = 0; i < denoiseIterations; i++)
                    {
                        lowResDesc.name = (i % 2 == 0) ? "_UltraLowResRTGI_DenoiseA" : "_UltraLowResRTGI_DenoiseB";
                        TextureHandle giNext = renderGraph.CreateTexture(lowResDesc);
                        float stepWidth = 1 << i;
                        Vector4 denoiseVec = new Vector4(stepWidth, m_Profile.giDenoiseDepthWeight, m_Profile.giDenoiseNormalWeight, 1.0f);

                        RecordLightingPass(renderGraph, "Ultra RTGI A-Trous Denoise", m_LightingMat, 2,
                            resourcesData, lowResSceneColor, giCurrent, TextureHandle.nullHandle,
                            TextureHandle.nullHandle, TextureHandle.nullHandle, giNext, denoiseVec);

                        giCurrent = giNext;
                    }
                }
                else
                {
                    // ==============================================================
                    // FALLBACK SCREEN-SPACE LIGHTING PASSES
                    // ==============================================================
                    lowResDesc.enableRandomWrite = false;

                    if (m_Profile.enableAO)
                    {
                        RecordLightingPass(renderGraph, "Ultra GTAO Pass", m_LightingMat, 0,
                            resourcesData, lowResSceneColor, TextureHandle.nullHandle, TextureHandle.nullHandle,
                            TextureHandle.nullHandle, TextureHandle.nullHandle, aoTexture, Vector4.zero);
                    }
                    else
                    {
                        RecordClearWhitePass(renderGraph, "Ultra AO Clear", aoTexture);
                    }

                    if (m_Profile.enableSSGI || m_Profile.enableContactShadows)
                    {
                        RecordLightingPass(renderGraph, "Ultra SSGI & Contact Shadows", m_LightingMat, 1,
                            resourcesData, lowResSceneColor, TextureHandle.nullHandle, TextureHandle.nullHandle,
                            TextureHandle.nullHandle, TextureHandle.nullHandle, giCurrent, Vector4.zero);

                        int denoiseIterations = m_Profile.enableSSGI ? Mathf.Clamp(m_Profile.giDenoiseIterations, 0, 4) : 0;
                        for (int i = 0; i < denoiseIterations; i++)
                        {
                            lowResDesc.name = (i % 2 == 0) ? "_UltraLowResSSGI_DenoiseA" : "_UltraLowResSSGI_DenoiseB";
                            TextureHandle giNext = renderGraph.CreateTexture(lowResDesc);
                            float stepWidth = 1 << i;
                            Vector4 denoiseVec = new Vector4(stepWidth, m_Profile.giDenoiseDepthWeight, m_Profile.giDenoiseNormalWeight, 1.0f);

                            RecordLightingPass(renderGraph, "Ultra SSGI A-Trous Denoise", m_LightingMat, 2,
                                resourcesData, lowResSceneColor, giCurrent, TextureHandle.nullHandle,
                                TextureHandle.nullHandle, TextureHandle.nullHandle, giNext, denoiseVec);

                            giCurrent = giNext;
                        }
                    }
                    else
                    {
                        RecordClearWhiteAlphaPass(renderGraph, "Ultra GI Clear", giCurrent);
                    }

                    if (m_Profile.enableSubsurfaceScattering)
                    {
                        RecordLightingPass(renderGraph, "Ultra Screen-Space SSS", m_LightingMat, 3,
                            resourcesData, lowResSceneColor, TextureHandle.nullHandle, TextureHandle.nullHandle,
                            TextureHandle.nullHandle, TextureHandle.nullHandle, sssTexture, Vector4.zero);
                    }
                    else
                    {
                        RecordSimpleCopyPass(renderGraph, "Ultra SSS Bypass", lowResSceneColor, sssTexture);
                    }

                    if (m_Profile.enableSSR || m_Profile.enableVolumetricLighting)
                    {
                        RecordLightingPass(renderGraph, "Ultra SSR & Volumetric Shafts", m_LightingMat, 4,
                            resourcesData, lowResSceneColor, TextureHandle.nullHandle, TextureHandle.nullHandle,
                            TextureHandle.nullHandle, TextureHandle.nullHandle, ssrVolTexture, Vector4.zero, useMainShadows: true);
                    }
                    else
                    {
                        RecordClearBlackPass(renderGraph, "Ultra SSR/Vol Clear", ssrVolTexture);
                    }
                }

                // 7. Pass 5: Physical HDR Lighting Composite
                lowResDesc.enableRandomWrite = false;
                lowResDesc.name = "_UltraLowResCompositedHDR";
                TextureHandle compositedHDR = renderGraph.CreateTexture(lowResDesc);
                RecordLightingPass(renderGraph, "Ultra HDR Lighting Composite", m_LightingMat, 5,
                    resourcesData, lowResSceneColor, giCurrent, aoTexture,
                    sssTexture, ssrVolTexture, compositedHDR, Vector4.zero);

                // 8. Multi-Mip Dual-Kawase HDR Bloom Pyramid (in low-res space)
                float effectiveBloomIntensity = (vol != null && vol.bloomIntensity.overrideState) ? vol.bloomIntensity.value : m_Profile.bloomIntensity;
                TextureHandle finalBloomTex = compositedHDR;

                if (m_Profile.enableBloom && effectiveBloomIntensity > 0.001f && m_Profile.debugView == PipelineDebugView.FinalOutput)
                {
                    int iterations = Mathf.Clamp(m_Profile.bloomIterations, 2, 6);
                    TextureHandle[] mipDown = new TextureHandle[iterations];

                    int mipW = Mathf.Max(16, lowW / 2);
                    int mipH = Mathf.Max(16, lowH / 2);

                    for (int i = 0; i < iterations; i++)
                    {
                        TextureDesc mipDesc = lowResDesc;
                        mipDesc.width = mipW;
                        mipDesc.height = mipH;
                        mipDesc.filterMode = FilterMode.Bilinear;
                        mipDesc.name = $"_UltraBloomMipDown_{i}";
                        mipDown[i] = renderGraph.CreateTexture(mipDesc);

                        Vector4 mipSizeVec = new Vector4(mipW, mipH, 1.0f / mipW, 1.0f / mipH);
                        if (i == 0)
                        {
                            RecordPostPass(renderGraph, "Ultra Bloom Prefilter", m_PostMat, 0,
                                resourcesData, compositedHDR, TextureHandle.nullHandle, TextureHandle.nullHandle,
                                mipDown[0], mipSizeVec, useDepth: false);
                        }
                        else
                        {
                            RecordPostPass(renderGraph, "Ultra Bloom Downsample", m_PostMat, 1,
                                resourcesData, TextureHandle.nullHandle, mipDown[i - 1], TextureHandle.nullHandle,
                                mipDown[i], mipSizeVec, useDepth: false);
                        }

                        mipW = Mathf.Max(8, mipW / 2);
                        mipH = Mathf.Max(8, mipH / 2);
                    }

                    TextureHandle currentUpsample = mipDown[iterations - 1];
                    for (int i = iterations - 2; i >= 0; i--)
                    {
                        TextureDesc upDesc = renderGraph.GetTextureDesc(mipDown[i]);
                        upDesc.name = $"_UltraBloomMipUp_{i}";
                        TextureHandle upTex = renderGraph.CreateTexture(upDesc);
                        Vector4 mipSizeVec = new Vector4(upDesc.width, upDesc.height, 1.0f / upDesc.width, 1.0f / upDesc.height);

                        RecordPostPass(renderGraph, "Ultra Bloom Upsample", m_PostMat, 2,
                            resourcesData, TextureHandle.nullHandle, mipDown[i], currentUpsample,
                            upTex, mipSizeVec, useDepth: false);

                        currentUpsample = upTex;
                    }

                    finalBloomTex = currentUpsample;
                }

                // 9. Low-Res Uber Post-Processing (Chromatic Aberration, DOF, Edge Aliasing Sharpen, AgX/ACES, Bayer Dithering)
                lowResDesc.name = "_UltraLowResFinalPost";
                TextureHandle lowResFinalPost = renderGraph.CreateTexture(lowResDesc);
                RecordPostPass(renderGraph, "Ultra Low-Res Uber PostProcess", m_PostMat, 3,
                    resourcesData, compositedHDR, finalBloomTex, TextureHandle.nullHandle,
                    lowResFinalPost, new Vector4(lowW, lowH, 1.0f / lowW, 1.0f / lowH), useDepth: true);

                // 10. Optional export to persistent low-res RenderTexture asset/instance for inspection
                if (m_ExportLowResHandle != null && m_ExportLowResHandle.rt != null)
                {
                    TextureHandle importedPersistentLowRes = renderGraph.ImportTexture(m_ExportLowResHandle);
                    RecordSimpleCopyPass(renderGraph, "Ultra Export Low-Res RenderTexture", lowResFinalPost, importedPersistentLowRes);
                }

                // 11. Final Fullscreen Stretch / Upscale Pass (Stretches low-res texture across full screen target)
                RecordPostPass(renderGraph, "Ultra Fullscreen Stretch Upscale", m_PostMat, 4,
                    resourcesData, TextureHandle.nullHandle, TextureHandle.nullHandle, lowResFinalPost,
                    resourcesData.activeColorTexture, new Vector4(fullW, fullH, 1.0f / fullW, 1.0f / fullH), useDepth: false);
            }

            private void RecordHardwareRayTracingPass(
                RenderGraph renderGraph,
                Camera cam,
                UltraPipelineProfile p,
                UltraPipelineVolumeComponent vol,
                int lowW, int lowH, int fullW, int fullH,
                TextureHandle sourceColor,
                TextureHandle depthTex,
                TextureHandle normalsTex,
                TextureHandle outAO,
                TextureHandle outGI,
                TextureHandle outSSRVol,
                TextureHandle outSSS)
            {
                using (var builder = renderGraph.AddUnsafePass<RTPassData>("Ultra Hardware DXR Ray Tracing (Unified Low-Res)", out var passData, profilingSampler))
                {
                    passData.rtShader = m_RTShader;
                    passData.rtSceneMgr = m_RTSceneMgr;
                    passData.profile = p;
                    passData.vol = vol;
                    passData.camera = cam;
                    passData.lowW = lowW;
                    passData.lowH = lowH;
                    passData.fullW = fullW;
                    passData.fullH = fullH;
                    passData.sourceColor = sourceColor;
                    passData.depthTex = depthTex;
                    passData.normalsTex = normalsTex;
                    passData.outAO = outAO;
                    passData.outGI = outGI;
                    passData.outSSRVol = outSSRVol;
                    passData.outSSS = outSSS;

                    builder.UseTexture(sourceColor, AccessFlags.Read);
                    builder.UseTexture(depthTex, AccessFlags.Read);
                    builder.UseTexture(normalsTex, AccessFlags.Read);

                    builder.UseTexture(outAO, AccessFlags.Write);
                    builder.UseTexture(outGI, AccessFlags.Write);
                    builder.UseTexture(outSSRVol, AccessFlags.Write);
                    builder.UseTexture(outSSS, AccessFlags.Write);

                    builder.SetRenderFunc(static (RTPassData data, UnsafeGraphContext ctx) =>
                    {
                        CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                        if (!data.rtSceneMgr.SyncScene(cmd))
                            return;

                        RayTracingShader rts = data.rtShader;
                        UltraPipelineProfile p = data.profile;
                        UltraPipelineVolumeComponent vol = data.vol;
                        Camera cam = data.camera;

                        Transform camTr = cam.transform;
                        Vector3 camPos = camTr.position;
                        Vector3 camFwd = camTr.forward;
                        Vector3 camRight = camTr.right;
                        Vector3 camUp = camTr.up;

                        float fovRad = cam.fieldOfView * Mathf.Deg2Rad;
                        float tanHalfFov = Mathf.Tan(fovRad * 0.5f);
                        float aspect = (float)data.fullW / Mathf.Max(1, data.fullH);
                        float near = cam.nearClipPlane;
                        float far = cam.farClipPlane;

                        Vector4 zBufParams;
                        if (SystemInfo.usesReversedZBuffer)
                        {
                            zBufParams = new Vector4(-1.0f + far / near, 1.0f, (-1.0f + far / near) / far, 1.0f / far);
                        }
                        else
                        {
                            zBufParams = new Vector4(1.0f - far / near, far / near, (1.0f - far / near) / far, (far / near) / far);
                        }

                        // Find main directional light
                        Vector3 sunDir = new Vector3(0.3f, 0.8f, -0.5f).normalized;
                        Color sunColor = new Color(1.5f, 1.4f, 1.2f);
                        Light sun = RenderSettings.sun;
                        if (sun != null && sun.isActiveAndEnabled)
                        {
                            sunDir = -sun.transform.forward;
                            sunColor = sun.color.linear * sun.intensity;
                        }
                        else
                        {
                            var allLights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
                            for (int i = 0; i < allLights.Length; i++)
                            {
                                if (allLights[i] != null && allLights[i].isActiveAndEnabled && allLights[i].type == LightType.Directional)
                                {
                                    sunDir = -allLights[i].transform.forward;
                                    sunColor = allLights[i].color.linear * allLights[i].intensity;
                                    break;
                                }
                            }
                        }

                        float giIntensity = (vol != null && vol.giIntensity.overrideState) ? vol.giIntensity.value : p.giIntensity;
                        float giRadius = (vol != null && vol.giRayRadius.overrideState) ? vol.giRayRadius.value : p.giRayRadius;
                        float aoIntensity = (vol != null && vol.aoIntensity.overrideState) ? vol.aoIntensity.value : p.aoIntensity;
                        float aoRadius = (vol != null && vol.aoRadius.overrideState) ? vol.aoRadius.value : p.aoRadius;
                        float sssIntensity = (vol != null && vol.sssIntensity.overrideState) ? vol.sssIntensity.value : p.sssIntensity;
                        float sssRadius = (vol != null && vol.sssRadius.overrideState) ? vol.sssRadius.value : p.sssRadius;
                        Color sssColor = (vol != null && vol.sssScatterColor.overrideState) ? vol.sssScatterColor.value : p.sssScatterColor;
                        float volIntensity = (vol != null && vol.volumetricIntensity.overrideState) ? vol.volumetricIntensity.value : p.volumetricIntensity;
                        float ssrIntensity = (vol != null && vol.ssrIntensity.overrideState) ? vol.ssrIntensity.value : p.ssrIntensity;

                        cmd.SetRayTracingShaderPass(rts, "UltraRayTracing");
                        cmd.SetRayTracingAccelerationStructure(rts, s_UltraAccelStructId, data.rtSceneMgr.AccelStruct);
                        cmd.SetRayTracingBufferParam(rts, s_UltraRTTrianglesId, data.rtSceneMgr.TriangleBuffer);
                        cmd.SetRayTracingBufferParam(rts, s_UltraRTInstancesId, data.rtSceneMgr.InstanceBuffer);
                        cmd.SetRayTracingBufferParam(rts, s_UltraRTLightsId, data.rtSceneMgr.LightBuffer);

                        cmd.SetRayTracingTextureParam(rts, s_SourceColorTexId, data.sourceColor);
                        cmd.SetRayTracingTextureParam(rts, s_CameraDepthTextureId, data.depthTex);
                        cmd.SetRayTracingTextureParam(rts, s_CameraNormalsTextureId, data.normalsTex);

                        cmd.SetRayTracingTextureParam(rts, s_OutRTAOId, data.outAO);
                        cmd.SetRayTracingTextureParam(rts, s_OutRTGIAndShadowsId, data.outGI);
                        cmd.SetRayTracingTextureParam(rts, s_OutRTReflectionsAndVolumetricsId, data.outSSRVol);
                        cmd.SetRayTracingTextureParam(rts, s_OutRTSubsurfaceId, data.outSSS);

                        cmd.SetRayTracingVectorParam(rts, s_UltraCamPosWSId, new Vector4(camPos.x, camPos.y, camPos.z, 1.0f));
                        cmd.SetRayTracingVectorParam(rts, s_UltraCamForwardId, new Vector4(camFwd.x, camFwd.y, camFwd.z, 0.0f));
                        cmd.SetRayTracingVectorParam(rts, s_UltraCamRightId, new Vector4(camRight.x, camRight.y, camRight.z, 0.0f));
                        cmd.SetRayTracingVectorParam(rts, s_UltraCamUpId, new Vector4(camUp.x, camUp.y, camUp.z, 0.0f));
                        cmd.SetRayTracingVectorParam(rts, s_UltraCamProjParamsId, new Vector4(aspect, tanHalfFov, near, far));
                        cmd.SetRayTracingVectorParam(rts, s_UltraLowResSizeId, new Vector4(data.lowW, data.lowH, 1.0f / data.lowW, 1.0f / data.lowH));
                        cmd.SetRayTracingVectorParam(rts, s_UltraFullScreenSizeId, new Vector4(data.fullW, data.fullH, 1.0f / data.fullW, 1.0f / data.fullH));
                        cmd.SetRayTracingVectorParam(rts, s_UltraZBufferParamsId, zBufParams);
                        cmd.SetRayTracingVectorParam(rts, s_UltraMainLightDirId, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0.0f));
                        cmd.SetRayTracingVectorParam(rts, s_UltraMainLightColorId, new Vector4(sunColor.r, sunColor.g, sunColor.b, 1.0f));

                        cmd.SetRayTracingVectorParam(rts, s_UltraRTParamsId, new Vector4(
                            data.rtSceneMgr.ActiveLightCount,
                            p.rtReflectionBounces,
                            p.rtGIRaysPerPixel,
                            p.rtHardwareShadows ? 1.0f : 0.0f));

                        cmd.SetRayTracingVectorParam(rts, s_UltraAOParams1Id, new Vector4(p.enableAO ? aoIntensity : 0.0f, aoRadius, p.aoPower, p.enableAO ? p.aoDirectLightOcclusion : 0.0f));
                        cmd.SetRayTracingVectorParam(rts, s_UltraAOParams2Id, new Vector4(p.aoDirectionCount, p.aoStepsPerDirection, p.aoMultiBounceTint, p.aoMaxDistance));

                        cmd.SetRayTracingVectorParam(rts, s_UltraGIParams1Id, new Vector4(p.enableSSGI ? giIntensity : 0.0f, p.rtGIRaysPerPixel, p.giStepsPerRay, giRadius));
                        cmd.SetRayTracingVectorParam(rts, s_UltraGIParams2Id, new Vector4(p.giThickness, p.giEmissiveBoost, p.giBounceSaturation, p.giSkyContribution));
                        cmd.SetRayTracingVectorParam(rts, s_UltraGISkyColorId, p.giSkyAmbientTint);
                        cmd.SetRayTracingVectorParam(rts, s_UltraGIGroundColorId, p.giGroundAmbientTint);
                        cmd.SetRayTracingVectorParam(rts, s_UltraContactShadowParamsId, new Vector4(p.enableContactShadows ? 1.0f : 0.0f, p.contactShadowLength, p.contactShadowIntensity, p.contactShadowSteps));

                        cmd.SetRayTracingVectorParam(rts, s_UltraSSSParamsId, new Vector4((p.enableSubsurfaceScattering && p.rtTrueSubsurfaceThickness) ? 1.0f : 0.0f, sssIntensity, sssRadius, p.sssDepthFalloff));
                        cmd.SetRayTracingVectorParam(rts, s_UltraSSSScatterColorId, sssColor);

                        cmd.SetRayTracingVectorParam(rts, s_UltraSSRParamsId, new Vector4(p.enableSSR ? 1.0f : 0.0f, ssrIntensity, p.ssrMaxSteps, p.ssrMaxDistance));
                        cmd.SetRayTracingVectorParam(rts, s_UltraSSRParams2Id, new Vector4(p.ssrThickness, p.ssrFresnelPower, 0.0f, 0.0f));
                        cmd.SetRayTracingVectorParam(rts, s_UltraVolumetricParamsId, new Vector4(p.enableVolumetricLighting ? 1.0f : 0.0f, volIntensity, p.volumetricSteps, p.volumetricMaxDistance));
                        cmd.SetRayTracingVectorParam(rts, s_UltraVolumetricParams2Id, new Vector4(p.volumetricMieAnisotropy, p.volumetricHeightFalloff, 0.0f, 0.0f));

                        cmd.DispatchRays(rts, "UltraRayGen", (uint)data.lowW, (uint)data.lowH, 1, null);
                    });
                }
            }

            private void UpdateMaterialUniforms(Camera cam, UltraPipelineProfile p, UltraPipelineVolumeComponent vol, int lowW, int lowH, int fullW, int fullH, bool isHardwareRTActive)
            {
                Transform camTr = cam.transform;
                Vector3 camPos = camTr.position;
                Vector3 camFwd = camTr.forward;
                Vector3 camRight = camTr.right;
                Vector3 camUp = camTr.up;

                float fovRad = cam.fieldOfView * Mathf.Deg2Rad;
                float tanHalfFov = Mathf.Tan(fovRad * 0.5f);
                float aspect = (float)fullW / Mathf.Max(1, fullH);

                Vector4 camPosVec = new Vector4(camPos.x, camPos.y, camPos.z, 1.0f);
                Vector4 camFwdVec = new Vector4(camFwd.x, camFwd.y, camFwd.z, 0.0f);
                Vector4 camRightVec = new Vector4(camRight.x, camRight.y, camRight.z, 0.0f);
                Vector4 camUpVec = new Vector4(camUp.x, camUp.y, camUp.z, 0.0f);
                Vector4 projParams = new Vector4(aspect, tanHalfFov, cam.nearClipPlane, cam.farClipPlane);
                Vector4 lowResVec = new Vector4(lowW, lowH, 1.0f / lowW, 1.0f / lowH);
                Vector4 fullResVec = new Vector4(fullW, fullH, 1.0f / fullW, 1.0f / fullH);

                float giIntensity = (vol != null && vol.giIntensity.overrideState) ? vol.giIntensity.value : p.giIntensity;
                float giRadius = (vol != null && vol.giRayRadius.overrideState) ? vol.giRayRadius.value : p.giRayRadius;
                float aoIntensity = (vol != null && vol.aoIntensity.overrideState) ? vol.aoIntensity.value : p.aoIntensity;
                float aoRadius = (vol != null && vol.aoRadius.overrideState) ? vol.aoRadius.value : p.aoRadius;
                float sssIntensity = (vol != null && vol.sssIntensity.overrideState) ? vol.sssIntensity.value : p.sssIntensity;
                float sssRadius = (vol != null && vol.sssRadius.overrideState) ? vol.sssRadius.value : p.sssRadius;
                Color sssColor = (vol != null && vol.sssScatterColor.overrideState) ? vol.sssScatterColor.value : p.sssScatterColor;
                float volIntensity = (vol != null && vol.volumetricIntensity.overrideState) ? vol.volumetricIntensity.value : p.volumetricIntensity;
                float ssrIntensity = (vol != null && vol.ssrIntensity.overrideState) ? vol.ssrIntensity.value : p.ssrIntensity;

                float bloomIntensity = (vol != null && vol.bloomIntensity.overrideState) ? vol.bloomIntensity.value : p.bloomIntensity;
                float bloomThreshold = (vol != null && vol.bloomThreshold.overrideState) ? vol.bloomThreshold.value : p.bloomThreshold;
                float caIntensity = (vol != null && vol.chromaticAberrationIntensity.overrideState) ? vol.chromaticAberrationIntensity.value : p.chromaticAberrationIntensity;
                float vignetteIntensity = (vol != null && vol.vignetteIntensity.overrideState) ? vol.vignetteIntensity.value : p.vignetteIntensity;
                float grainIntensity = (vol != null && vol.filmGrainIntensity.overrideState) ? vol.filmGrainIntensity.value : p.filmGrainIntensity;
                float exposureEV = (vol != null && vol.postExposureEV.overrideState) ? vol.postExposureEV.value : p.postExposureEV;
                float contrast = (vol != null && vol.contrast.overrideState) ? vol.contrast.value : p.contrast;
                float saturation = (vol != null && vol.saturation.overrideState) ? vol.saturation.value : p.saturation;
                float edgeSharpen = (vol != null && vol.edgeAliasingSharpen.overrideState) ? vol.edgeAliasingSharpen.value : p.edgeAliasingSharpen;
                float ditherStrength = (vol != null && vol.ditherStrength.overrideState) ? vol.ditherStrength.value : p.ditherStrength;
                int colorSteps = (vol != null && vol.colorStepsPerChannel.overrideState) ? vol.colorStepsPerChannel.value : p.colorStepsPerChannel;

                m_LightingMat.SetVector(s_UltraCamPosWSId, camPosVec);
                m_LightingMat.SetVector(s_UltraCamForwardId, camFwdVec);
                m_LightingMat.SetVector(s_UltraCamRightId, camRightVec);
                m_LightingMat.SetVector(s_UltraCamUpId, camUpVec);
                m_LightingMat.SetVector(s_UltraCamProjParamsId, projParams);
                m_LightingMat.SetVector(s_UltraLowResSizeId, lowResVec);
                m_LightingMat.SetVector(s_UltraFrameParamsId, new Vector4(Time.frameCount % 1024, (int)p.debugView, p.specularAliasingBoost, isHardwareRTActive ? 1.0f : 0.0f));

                m_LightingMat.SetVector(s_UltraAOParams1Id, new Vector4(aoIntensity, aoRadius, p.aoPower, p.enableAO ? p.aoDirectLightOcclusion : 0.0f));
                m_LightingMat.SetVector(s_UltraAOParams2Id, new Vector4(p.aoDirectionCount, p.aoStepsPerDirection, p.aoMultiBounceTint, p.aoMaxDistance));

                m_LightingMat.SetVector(s_UltraGIParams1Id, new Vector4(p.enableSSGI ? giIntensity : 0.0f, p.giRayCount, p.giStepsPerRay, giRadius));
                m_LightingMat.SetVector(s_UltraGIParams2Id, new Vector4(p.giThickness, p.giEmissiveBoost, p.giBounceSaturation, p.giSkyContribution));
                m_LightingMat.SetColor(s_UltraGISkyColorId, p.giSkyAmbientTint);
                m_LightingMat.SetColor(s_UltraGIGroundColorId, p.giGroundAmbientTint);
                m_LightingMat.SetVector(s_UltraContactShadowParamsId, new Vector4(p.enableContactShadows ? 1.0f : 0.0f, p.contactShadowLength, p.contactShadowIntensity, p.contactShadowSteps));

                m_LightingMat.SetVector(s_UltraSSSParamsId, new Vector4(p.enableSubsurfaceScattering ? 1.0f : 0.0f, sssIntensity, sssRadius, p.sssDepthFalloff));
                m_LightingMat.SetColor(s_UltraSSSScatterColorId, sssColor);

                m_LightingMat.SetVector(s_UltraSSRParamsId, new Vector4(p.enableSSR ? 1.0f : 0.0f, ssrIntensity, p.ssrMaxSteps, p.ssrMaxDistance));
                m_LightingMat.SetVector(s_UltraSSRParams2Id, new Vector4(p.ssrThickness, p.ssrFresnelPower, 0.0f, 0.0f));
                m_LightingMat.SetVector(s_UltraVolumetricParamsId, new Vector4(p.enableVolumetricLighting ? 1.0f : 0.0f, volIntensity, p.volumetricSteps, p.volumetricMaxDistance));
                m_LightingMat.SetVector(s_UltraVolumetricParams2Id, new Vector4(p.volumetricMieAnisotropy, p.volumetricHeightFalloff, 0.0f, 0.0f));
                m_LightingMat.SetColor(s_UltraVolumetricColorId, p.volumetricFogColor);

                m_PostMat.SetVector(s_UltraLowResSizeId, lowResVec);
                m_PostMat.SetVector(s_UltraFullScreenSizeId, fullResVec);

                float activeBloomInt = p.enableBloom ? bloomIntensity : 0.0f;
                m_PostMat.SetVector(s_UltraBloomParamsId, new Vector4(bloomThreshold, p.bloomSoftKnee, activeBloomInt, p.bloomScatter));
                m_PostMat.SetVector(s_UltraBloomTintId, new Vector4(p.bloomTint.r, p.bloomTint.g, p.bloomTint.b, p.bloomAnamorphicRatio));

                m_PostMat.SetVector(s_UltraCAParamsId, new Vector4(p.enableChromaticAberration ? 1.0f : 0.0f, caIntensity, p.chromaticAberrationRadialPower, p.chromaticAberrationSamples));
                m_PostMat.SetVector(s_UltraDOFParamsId, new Vector4(p.enableDepthOfField ? 1.0f : 0.0f, p.dofFocusDistance, p.dofFocusRange, p.dofBokehRadius));

                m_PostMat.SetVector(s_UltraVignetteParamsId, new Vector4(p.enableVignette ? 1.0f : 0.0f, vignetteIntensity, p.vignetteSmoothness, p.lensDistortion));
                m_PostMat.SetColor(s_UltraVignetteColorId, p.vignetteColor);
                m_PostMat.SetVector(s_UltraGrainParamsId, new Vector4(p.enableFilmGrain ? 1.0f : 0.0f, grainIntensity, p.filmGrainResponse, (Time.frameCount % 256) * 0.1f));

                float linearExposure = Mathf.Pow(2.0f, exposureEV);
                m_PostMat.SetVector(s_UltraGradingParams1Id, new Vector4((int)p.toneMapping, linearExposure, contrast, saturation));
                m_PostMat.SetVector(s_UltraGradingParams2Id, new Vector4(p.whiteBalanceTemperature, p.whiteBalanceTint, p.gamma, p.debugView != PipelineDebugView.FinalOutput ? 1.0f : 0.0f));
                m_PostMat.SetColor(s_UltraShadowsTintId, p.shadowsTint);
                m_PostMat.SetColor(s_UltraMidtonesTintId, p.midtonesTint);
                m_PostMat.SetColor(s_UltraHighlightsTintId, p.highlightsTint);

                int ditherModeInt = (p.enableLowResRendering && p.enableDithering) ? (int)p.ditherMode : 0;
                float effectiveEdgeSharpen = p.enableLowResRendering ? edgeSharpen : 0.0f;
                int effectiveColorSteps = p.enableLowResRendering ? colorSteps : 256;
                m_PostMat.SetVector(s_UltraAliasingParamsId, new Vector4(effectiveEdgeSharpen, ditherModeInt, ditherStrength, effectiveColorSteps));
                m_PostMat.SetVector(s_UltraUpscaleParamsId, new Vector4((int)p.upscaleFilter, (int)p.aspectRatioMode, p.pixelGridOverlay, p.enableLowResRendering ? 1.0f : 0.0f));
            }

            private void RecordSimpleCopyPass(RenderGraph renderGraph, string passName, TextureHandle source, TextureHandle destination)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData, profilingSampler))
                {
                    passData.material = m_PostMat;
                    passData.passIndex = 5; // Pure Point/Nearest low-res capture
                    passData.texA = source;

                    builder.UseTexture(source, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    {
                        var mpb = ctx.renderGraphPool.GetTempMaterialPropertyBlock();
                        mpb.SetTexture(s_LowResFinalTexId, data.texA);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, data.passIndex, MeshTopology.Triangles, 3, 1, mpb);
                    });
                }
            }

            private static void RecordClearWhitePass(RenderGraph renderGraph, string passName, TextureHandle destination)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out _, new ProfilingSampler(passName)))
                {
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (PassData _, RasterGraphContext ctx) =>
                    {
                        ctx.cmd.ClearRenderTarget(false, true, Color.white);
                    });
                }
            }

            private static void RecordClearWhiteAlphaPass(RenderGraph renderGraph, string passName, TextureHandle destination)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out _, new ProfilingSampler(passName)))
                {
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (PassData _, RasterGraphContext ctx) =>
                    {
                        ctx.cmd.ClearRenderTarget(false, true, new Color(0f, 0f, 0f, 1f));
                    });
                }
            }

            private static void RecordClearBlackPass(RenderGraph renderGraph, string passName, TextureHandle destination)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out _, new ProfilingSampler(passName)))
                {
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (PassData _, RasterGraphContext ctx) =>
                    {
                        ctx.cmd.ClearRenderTarget(false, true, Color.clear);
                    });
                }
            }

            private void RecordLightingPass(
                RenderGraph renderGraph,
                string passName,
                Material mat,
                int passIndex,
                UniversalResourceData resourcesData,
                TextureHandle sourceColor,
                TextureHandle giTex,
                TextureHandle aoTex,
                TextureHandle sssTex,
                TextureHandle ssrVolTex,
                TextureHandle destination,
                Vector4 extraVec,
                bool useMainShadows = false)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData, profilingSampler))
                {
                    passData.material = mat;
                    passData.passIndex = passIndex;
                    passData.texA = sourceColor;
                    passData.texB = giTex;
                    passData.texC = aoTex;
                    passData.texD = sssTex;
                    passData.texE = ssrVolTex;
                    passData.extraVec = extraVec;

                    if (sourceColor.IsValid()) builder.UseTexture(sourceColor, AccessFlags.Read);
                    if (giTex.IsValid()) builder.UseTexture(giTex, AccessFlags.Read);
                    if (aoTex.IsValid()) builder.UseTexture(aoTex, AccessFlags.Read);
                    if (sssTex.IsValid()) builder.UseTexture(sssTex, AccessFlags.Read);
                    if (ssrVolTex.IsValid()) builder.UseTexture(ssrVolTex, AccessFlags.Read);

                    if (resourcesData.cameraDepthTexture.IsValid())
                        builder.UseTexture(resourcesData.cameraDepthTexture, AccessFlags.Read);

                    if (resourcesData.cameraNormalsTexture.IsValid())
                        builder.UseTexture(resourcesData.cameraNormalsTexture, AccessFlags.Read);

                    if (useMainShadows && resourcesData.mainShadowsTexture.IsValid())
                        builder.UseTexture(resourcesData.mainShadowsTexture, AccessFlags.Read);

                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    {
                        var mpb = ctx.renderGraphPool.GetTempMaterialPropertyBlock();
                        if (data.texA.IsValid()) mpb.SetTexture(s_SourceColorTexId, data.texA);
                        if (data.texB.IsValid()) mpb.SetTexture(s_GITexId, data.texB);
                        if (data.texC.IsValid()) mpb.SetTexture(s_AOTexId, data.texC);
                        if (data.texD.IsValid()) mpb.SetTexture(s_SSSTexId, data.texD);
                        if (data.texE.IsValid()) mpb.SetTexture(s_SSRVolumetricTexId, data.texE);

                        if (data.passIndex == 2)
                            mpb.SetVector(s_UltraGIDenoiseParamsId, data.extraVec);

                        ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, data.passIndex, MeshTopology.Triangles, 3, 1, mpb);
                    });
                }
            }

            private void RecordPostPass(
                RenderGraph renderGraph,
                string passName,
                Material mat,
                int passIndex,
                UniversalResourceData resourcesData,
                TextureHandle mainSource,
                TextureHandle bloomMip,
                TextureHandle lowMipOrFinal,
                TextureHandle destination,
                Vector4 mipSizeVec,
                bool useDepth)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData, profilingSampler))
                {
                    passData.material = mat;
                    passData.passIndex = passIndex;
                    passData.texA = mainSource;
                    passData.texB = bloomMip;
                    passData.texC = lowMipOrFinal;
                    passData.extraVec = mipSizeVec;

                    if (mainSource.IsValid()) builder.UseTexture(mainSource, AccessFlags.Read);
                    if (bloomMip.IsValid()) builder.UseTexture(bloomMip, AccessFlags.Read);
                    if (lowMipOrFinal.IsValid()) builder.UseTexture(lowMipOrFinal, AccessFlags.Read);

                    if (useDepth && resourcesData.cameraDepthTexture.IsValid())
                        builder.UseTexture(resourcesData.cameraDepthTexture, AccessFlags.Read);

                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    {
                        var mpb = ctx.renderGraphPool.GetTempMaterialPropertyBlock();
                        if (data.texA.IsValid()) mpb.SetTexture(s_MainSourceTexId, data.texA);
                        if (data.texB.IsValid()) mpb.SetTexture(s_BloomMipTexId, data.texB);
                        if (data.texC.IsValid())
                        {
                            if (data.passIndex == 4)
                                mpb.SetTexture(s_LowResFinalTexId, data.texC);
                            else
                                mpb.SetTexture(s_BloomLowMipTexId, data.texC);
                        }

                        mpb.SetVector(s_UltraMipTexelSizeId, data.extraVec);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, data.passIndex, MeshTopology.Triangles, 3, 1, mpb);
                    });
                }
            }
        }
    }
}

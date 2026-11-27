using UnityEditor;
using UnityEngine;

namespace UltraLoFiPipeline.Editor
{
    [CustomEditor(typeof(UltraPipelineProfile))]
    public class UltraPipelineProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var profile = (UltraPipelineProfile)target;

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("ULTRA LO-FI REALISTIC RENDER PIPELINE (DXR RAY TRACING)", EditorStyles.boldLabel);

            Vector2Int previewRes = profile.ComputeLowResolution(1920, 1080);
            string modeLabel = profile.enableLowResRendering
                ? $"Low-Res RT: {previewRes.x} x {previewRes.y} -> Stretched 1080p ({profile.upscaleFilter})"
                : "Native Full Resolution (Low-Res Stretch Disabled)";
            EditorGUILayout.LabelField(modeLabel, EditorStyles.miniLabel);

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("240p Aliased (Default)"))
            {
                Undo.RecordObject(profile, "Apply 240p Settings");
                ApplyDefaultRayTracingSettings(profile);
                profile.enableLowResRendering = true;
                profile.targetVerticalHeight = 240;
                EditorUtility.SetDirty(profile);
            }
            if (GUILayout.Button("360p Detailed Lo-Fi"))
            {
                Undo.RecordObject(profile, "Apply 360p Settings");
                ApplyDefaultRayTracingSettings(profile);
                profile.enableLowResRendering = true;
                profile.targetVerticalHeight = 360;
                profile.edgeAliasingSharpen = 0.22f;
                profile.ditherStrength = 0.06f;
                profile.colorStepsPerChannel = 160;
                EditorUtility.SetDirty(profile);
            }
            if (GUILayout.Button("Full-Res Native (No Low-Res)"))
            {
                Undo.RecordObject(profile, "Apply Full-Res Settings");
                ApplyDefaultRayTracingSettings(profile);
                profile.enableLowResRendering = false;
                EditorUtility.SetDirty(profile);
            }
            EditorGUILayout.EndHorizontal();

            if (UltraRenderPipelineFeature.CurrentLowResRenderTexture != null)
            {
                var rt = UltraRenderPipelineFeature.CurrentLowResRenderTexture;
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField($"Live Internal RenderTexture ({rt.width}x{rt.height}):", EditorStyles.miniBoldLabel);
                Rect previewRect = GUILayoutUtility.GetAspectRect((float)rt.width / Mathf.Max(1, rt.height), GUILayout.MaxHeight(140f));
                EditorGUI.DrawPreviewTexture(previewRect, rt, null, ScaleMode.ScaleToFit);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6f);

            DrawDefaultInspector();
        }

        public static void ApplyDefaultRayTracingSettings(UltraPipelineProfile p)
        {
            p.useHardwareRayTracing = true;
            p.rtReflectionBounces = 2;
            p.rtGIRaysPerPixel = 6;
            p.rtHardwareShadows = true;
            p.rtTrueSubsurfaceThickness = true;

            p.enableLowResRendering = true;
            p.sizingMode = ResolutionSizingMode.FixedVerticalHeight;
            p.targetVerticalHeight = 240;
            p.upscaleFilter = UpscaleFilterMode.PointNearest;
            p.aspectRatioMode = AspectRatioMode.StretchFullScreen;
            p.forceDisableAntiAliasing = true;
            p.edgeAliasingSharpen = 0.32f;
            p.specularAliasingBoost = 0.38f;
            p.enableDithering = true;
            p.ditherMode = DitherMode.Bayer4x4;
            p.ditherStrength = 0.10f;
            p.colorStepsPerChannel = 128;
            p.pixelGridOverlay = 0.03f;

            p.enableSSGI = true;
            p.giIntensity = 1.50f;
            p.giRayRadius = 6.0f;
            p.giEmissiveBoost = 2.4f;
            p.giBounceSaturation = 1.25f;
            p.giDenoiseIterations = 2;
            p.giDenoiseDepthWeight = 28.0f;
            p.giDenoiseNormalWeight = 18.0f;

            p.enableAO = true;
            p.aoIntensity = 1.30f;
            p.aoRadius = 0.75f;
            p.aoPower = 1.50f;
            p.aoDirectLightOcclusion = 0.35f;
            p.aoMultiBounceTint = 0.70f;

            p.enableSubsurfaceScattering = true;
            p.sssIntensity = 0.95f;
            p.sssRadius = 1.60f;
            p.enableContactShadows = true;
            p.contactShadowLength = 0.40f;
            p.contactShadowIntensity = 0.75f;

            p.enableSSR = true;
            p.ssrIntensity = 0.88f;
            p.ssrMaxDistance = 25.0f;
            p.ssrFresnelPower = 4.0f;

            p.enableVolumetricLighting = true;
            p.volumetricIntensity = 0.65f;
            p.volumetricSteps = 12;
            p.volumetricMaxDistance = 35.0f;
            p.volumetricMieAnisotropy = 0.65f;

            p.enableBloom = true;
            p.bloomThreshold = 0.90f;
            p.bloomSoftKnee = 0.55f;
            p.bloomIntensity = 0.72f;
            p.bloomScatter = 0.70f;

            p.enableChromaticAberration = true;
            p.chromaticAberrationIntensity = 0.28f;
            p.enableVignette = true;
            p.vignetteIntensity = 0.28f;
            p.enableFilmGrain = true;
            p.filmGrainIntensity = 0.04f;
            p.toneMapping = ToneMappingMode.AgX;
            p.postExposureEV = 0.25f;
            p.contrast = 1.12f;
            p.saturation = 1.08f;
        }
    }
}

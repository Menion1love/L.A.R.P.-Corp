using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UltraLoFiPipeline
{
    /// <summary>
    /// Scene & Camera controller for the Ultra Lo-Fi Realistic Render Pipeline.
    /// Synchronizes the single master UltraPipelineProfile and exposes the live low-res RenderTexture.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Ultra Lo-Fi Pipeline Manager")]
    public class UltraLowResPipelineManager : MonoBehaviour
    {
        public static UltraLowResPipelineManager ActiveInstance { get; private set; }

        [Header("Master Pipeline Profile")]
        [Tooltip("Single master profile containing all Ray Tracing, Low-Res RenderTexture, Aliasing, Lighting, and Post-Processing settings.")]
        public UltraPipelineProfile profile;

        [Header("Live RenderTexture Telemetry (Read-Only)")]
        [Tooltip("Live reference to the low-resolution RenderTexture stretched across the screen.")]
        public RenderTexture activeLowResRenderTexture;

        [Tooltip("Current internal RenderTexture dimensions in pixels.")]
        public Vector2Int activeLowResResolution;

        [Tooltip("Current full-screen target dimensions in pixels.")]
        public Vector2Int fullScreenResolution;

        private void OnEnable()
        {
            ActiveInstance = this;
            SyncPipelineSettings();
        }

        private void OnDisable()
        {
            if (ActiveInstance == this)
                ActiveInstance = null;
        }

        private void Update()
        {
            if (ActiveInstance == null)
                ActiveInstance = this;

            SyncPipelineSettings();
        }

        public void SyncPipelineSettings()
        {
            if (profile == null)
                return;

            int screenW = Mathf.Max(64, Screen.width);
            int screenH = Mathf.Max(64, Screen.height);
            fullScreenResolution = new Vector2Int(screenW, screenH);
            activeLowResResolution = profile.ComputeLowResolution(screenW, screenH);
            activeLowResRenderTexture = UltraRenderPipelineFeature.CurrentLowResRenderTexture;

            var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset == null)
                urpAsset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;

            if (urpAsset != null)
            {
                urpAsset.supportsHDR = true;
                urpAsset.upscalingFilter = (!profile.enableLowResRendering || profile.upscaleFilter == UpscaleFilterMode.Bilinear)
                    ? UpscalingFilterSelection.Linear
                    : UpscalingFilterSelection.Point;

                if (profile.forceDisableAntiAliasing && profile.enableLowResRendering)
                    urpAsset.msaaSampleCount = 1;

                if (Mathf.Abs(urpAsset.renderScale - 1.0f) > 0.005f)
                    urpAsset.renderScale = 1.0f;
            }
        }
    }
}

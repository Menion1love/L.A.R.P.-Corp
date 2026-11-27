using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UltraLoFiPipeline
{
    /// <summary>
    /// Custom VolumeComponent allowing per-scene or spatial trigger Volume overrides
    /// of any parameter in the Ultra Lo-Fi Realistic Render Pipeline.
    /// </summary>
    [Serializable]
    [VolumeComponentMenu("Ultra Lo-Fi Pipeline/Ultra Lo-Fi Lighting & PostFX")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class UltraPipelineVolumeComponent : VolumeComponent, IPostProcessComponent
    {
        [Header("Low-Res Virtual Framebuffer")]
        public BoolParameter enableLowResOverride = new BoolParameter(false);
        public ClampedIntParameter targetVerticalHeight = new ClampedIntParameter(240, 64, 1080);
        public ClampedFloatParameter downscaleFactor = new ClampedFloatParameter(4.0f, 1.0f, 16.0f);
        public ClampedFloatParameter edgeAliasingSharpen = new ClampedFloatParameter(0.35f, 0.0f, 2.0f);
        public ClampedFloatParameter ditherStrength = new ClampedFloatParameter(0.14f, 0.0f, 1.0f);
        public ClampedIntParameter colorStepsPerChannel = new ClampedIntParameter(96, 8, 256);

        [Header("Global Illumination & Ambient Occlusion")]
        public ClampedFloatParameter giIntensity = new ClampedFloatParameter(1.45f, 0.0f, 5.0f);
        public ClampedFloatParameter giRayRadius = new ClampedFloatParameter(4.5f, 0.2f, 20.0f);
        public ClampedFloatParameter aoIntensity = new ClampedFloatParameter(1.30f, 0.0f, 4.0f);
        public ClampedFloatParameter aoRadius = new ClampedFloatParameter(0.65f, 0.05f, 4.0f);

        [Header("Subsurface Scattering & Volumetrics")]
        public ClampedFloatParameter sssIntensity = new ClampedFloatParameter(0.85f, 0.0f, 3.0f);
        public ClampedFloatParameter sssRadius = new ClampedFloatParameter(1.65f, 0.1f, 5.0f);
        public ColorParameter sssScatterColor = new ColorParameter(new Color(1.0f, 0.34f, 0.18f, 1.0f), true, false, true);
        public ClampedFloatParameter volumetricIntensity = new ClampedFloatParameter(0.68f, 0.0f, 3.0f);
        public ClampedFloatParameter ssrIntensity = new ClampedFloatParameter(0.55f, 0.0f, 2.0f);

        [Header("Bloom, Chromatic Aberration & Grading")]
        public ClampedFloatParameter bloomIntensity = new ClampedFloatParameter(0.75f, 0.0f, 5.0f);
        public ClampedFloatParameter bloomThreshold = new ClampedFloatParameter(0.85f, 0.0f, 5.0f);
        public ClampedFloatParameter chromaticAberrationIntensity = new ClampedFloatParameter(0.35f, 0.0f, 3.0f);
        public ClampedFloatParameter vignetteIntensity = new ClampedFloatParameter(0.32f, 0.0f, 1.0f);
        public ClampedFloatParameter filmGrainIntensity = new ClampedFloatParameter(0.06f, 0.0f, 0.5f);
        public ClampedFloatParameter postExposureEV = new ClampedFloatParameter(0.25f, -4.0f, 4.0f);
        public ClampedFloatParameter contrast = new ClampedFloatParameter(1.12f, 0.5f, 2.0f);
        public ClampedFloatParameter saturation = new ClampedFloatParameter(1.08f, 0.0f, 2.0f);

        public bool IsActive() => active;

        public bool IsTileCompatible() => false;
    }
}

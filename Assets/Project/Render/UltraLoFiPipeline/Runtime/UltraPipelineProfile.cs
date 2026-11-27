using System;
using UnityEngine;

namespace UltraLoFiPipeline
{
    public enum ResolutionSizingMode
    {
        FixedVerticalHeight = 0,
        DownscaleFactor = 1,
        CustomDimensions = 2
    }

    public enum UpscaleFilterMode
    {
        PointNearest = 0,
        CrispPixelArt = 1,
        Bilinear = 2
    }

    public enum AspectRatioMode
    {
        StretchFullScreen = 0,
        PreserveAspectLetterbox = 1,
        IntegerPixelScale = 2
    }

    public enum DitherMode
    {
        None = 0,
        Bayer4x4 = 1,
        Bayer8x8 = 2,
        InterleavedGradientNoise = 3
    }

    public enum ToneMappingMode
    {
        None = 0,
        Reinhard = 1,
        Neutral = 2,
        ACESFilmic = 3,
        AgX = 4
    }

    public enum PipelineDebugView
    {
        FinalOutput = 0,
        RawDirectLighting = 1,
        SSGIOnly = 2,
        AmbientOcclusionOnly = 3,
        SubsurfaceScatteringOnly = 4,
        VolumetricLightOnly = 5,
        SSROnly = 6,
        WorldNormals = 7,
        LinearDepth = 8
    }

    /// <summary>
    /// Portable, fully parameterized configuration profile for the Ultra Lo-Fi Realistic Render Pipeline.
    /// Can be shared across scenes and exported to any Unity project.
    /// </summary>
    [CreateAssetMenu(fileName = "UltraPipelineProfile", menuName = "Rendering/Ultra Lo-Fi Pipeline/Pipeline Profile", order = 100)]
    public class UltraPipelineProfile : ScriptableObject
    {
        [Header("0. Hardware Ray Tracing (DXR Acceleration & Optimization)")]
        [Tooltip("Use Unity Hardware DXR Ray Tracing (BVH Acceleration Structure on RT Cores) for Reflections, GI, AO, SSS Thickness, Shadows, and Volumetrics.")]
        public bool useHardwareRayTracing = true;

        [Tooltip("Maximum hardware reflection bounces (2 = spheres reflect neighboring spheres that also reflect the room).")]
        [Range(1, 3)]
        public int rtReflectionBounces = 2;

        [Tooltip("Number of hardware BVH rays per low-res pixel for Ray-Traced GI & RTAO (denoised via edge-preserving À-Trous filter).")]
        [Range(1, 12)]
        public int rtGIRaysPerPixel = 4;

        [Tooltip("Trace hardware shadow rays for contact shadows, reflected object shadows, and volumetric light shafts.")]
        public bool rtHardwareShadows = true;

        [Tooltip("Trace internal hardware transmission rays to measure true 3D object thickness for Subsurface Scattering & Translucency.")]
        public bool rtTrueSubsurfaceThickness = true;

        [Header("1. Low-Res Render Texture & Fullscreen Stretch")]
        [Tooltip("Enable rendering the entire pipeline into a low-resolution RenderTexture and stretching it to full screen.")]
        public bool enableLowResRendering = true;

        [Tooltip("How the low-resolution RenderTexture dimensions are calculated.")]
        public ResolutionSizingMode sizingMode = ResolutionSizingMode.FixedVerticalHeight;

        [Tooltip("Target vertical height in pixels (width is computed automatically from screen aspect ratio).")]
        [Range(64, 1080)]
        public int targetVerticalHeight = 240;

        [Tooltip("Screen resolution divisor when Sizing Mode is set to DownscaleFactor.")]
        [Range(1.0f, 16.0f)]
        public float downscaleFactor = 4.0f;

        [Tooltip("Explicit low-res RenderTexture dimensions when Sizing Mode is CustomDimensions.")]
        public Vector2Int customResolution = new Vector2Int(426, 240);

        [Tooltip("Filtering method when stretching the low-res texture to the full screen. PointNearest preserves maximum aliasing.")]
        public UpscaleFilterMode upscaleFilter = UpscaleFilterMode.PointNearest;

        [Tooltip("How the low-res texture fills the screen viewport.")]
        public AspectRatioMode aspectRatioMode = AspectRatioMode.StretchFullScreen;

        [Tooltip("Synchronizes URP hardware rasterizer scale and disables MSAA so 3D geometry is hardware-rasterized at the low resolution.")]
        public bool syncHardwareRenderScale = true;

        [Tooltip("Exposes a persistent low-res RenderTexture asset/instance that external scripts or UI can inspect.")]
        public bool exposeLowResRenderTexture = true;

        [Header("2. Aliasing, Edge Crunch & Dithering")]
        [Tooltip("Forces camera anti-aliasing (MSAA, FXAA, TAA, SMAA) off to keep hard pixel edges.")]
        public bool forceDisableAntiAliasing = true;

        [Tooltip("High-frequency edge contrast boost that accentuates crisp aliased geometry and lighting boundaries.")]
        [Range(0.0f, 2.0f)]
        public float edgeAliasingSharpen = 0.35f;

        [Tooltip("Boosts crisp 1-pixel specular micro-glints on PBR surfaces.")]
        [Range(0.0f, 2.0f)]
        public float specularAliasingBoost = 0.40f;

        [Tooltip("Enable ordered Bayer or IGN dithering before color quantization.")]
        public bool enableDithering = true;

        [Tooltip("Dithering pattern applied in low-resolution pixel space.")]
        public DitherMode ditherMode = DitherMode.Bayer4x4;

        [Tooltip("Intensity of the dithering matrix pattern.")]
        [Range(0.0f, 1.0f)]
        public float ditherStrength = 0.14f;

        [Tooltip("Color quantization steps per channel (256 = full smooth color, lower values = crunchy retro quantization).")]
        [Range(8, 256)]
        public int colorStepsPerChannel = 96;

        [Tooltip("Subtle pixel grid boundary definition after stretching to full screen (0 = off).")]
        [Range(0.0f, 0.5f)]
        public float pixelGridOverlay = 0.04f;

        [Header("3. Global Illumination (Screen-Space GI & Indirect Bounce)")]
        [Tooltip("Enables real-time raymarched Screen-Space Global Illumination (indirect diffuse light bounce + emissive bleeding).")]
        public bool enableSSGI = true;

        [Tooltip("Overall intensity of indirect global illumination bounce.")]
        [Range(0.0f, 5.0f)]
        public float giIntensity = 1.45f;

        [Tooltip("Number of hemispherical rays traced per pixel.")]
        [Range(2, 16)]
        public int giRayCount = 6;

        [Tooltip("Number of raymarching steps along each GI ray.")]
        [Range(4, 32)]
        public int giStepsPerRay = 10;

        [Tooltip("Maximum world-space radius for indirect light bounce gathering.")]
        [Range(0.2f, 20.0f)]
        public float giRayRadius = 4.5f;

        [Tooltip("Assumed depth thickness of surfaces in screen space during ray intersection checks.")]
        [Range(0.05f, 3.0f)]
        public float giThickness = 0.45f;

        [Tooltip("Extra multiplier for bright & emissive surfaces contributing to indirect GI light bleeding.")]
        [Range(0.5f, 10.0f)]
        public float giEmissiveBoost = 2.4f;

        [Tooltip("Saturation boost for indirect color bleeding between colored surfaces.")]
        [Range(0.0f, 2.5f)]
        public float giBounceSaturation = 1.3f;

        [Tooltip("Sky hemisphere irradiance tint for unoccluded upward-facing normals.")]
        [ColorUsage(false, true)]
        public Color giSkyAmbientTint = new Color(0.56f, 0.68f, 0.92f, 1.0f);

        [Tooltip("Ground hemisphere bounce tint for downward-facing normals.")]
        [ColorUsage(false, true)]
        public Color giGroundAmbientTint = new Color(0.26f, 0.21f, 0.17f, 1.0f);

        [Tooltip("Sky/environment indirect fill contribution.")]
        [Range(0.0f, 2.0f)]
        public float giSkyContribution = 0.35f;

        [Tooltip("Number of edge-preserving À-Trous spatial denoiser iterations for GI.")]
        [Range(0, 4)]
        public int giDenoiseIterations = 2;

        [Tooltip("Depth weight for GI spatial denoiser (higher = sharper depth edges).")]
        [Range(1.0f, 100.0f)]
        public float giDenoiseDepthWeight = 28.0f;

        [Tooltip("Normal weight for GI spatial denoiser (higher = sharper normal creases).")]
        [Range(1.0f, 64.0f)]
        public float giDenoiseNormalWeight = 18.0f;

        [Header("4. Ambient Occlusion (Multi-Scale GTAO & Multi-Bounce)")]
        [Tooltip("Enables horizon-based Ground-Truth Ambient Occlusion with multi-bounce albedo color bleeding.")]
        public bool enableAO = true;

        [Tooltip("Strength of ambient occlusion shadowing.")]
        [Range(0.0f, 4.0f)]
        public float aoIntensity = 1.30f;

        [Tooltip("World-space sampling radius for ambient occlusion.")]
        [Range(0.05f, 4.0f)]
        public float aoRadius = 0.65f;

        [Tooltip("Exponent curve controlling AO contrast falloff.")]
        [Range(0.5f, 4.0f)]
        public float aoPower = 1.55f;

        [Tooltip("Azimuthal directions sampled per pixel for GTAO.")]
        [Range(2, 12)]
        public int aoDirectionCount = 6;

        [Tooltip("Horizon steps marched per direction.")]
        [Range(2, 12)]
        public int aoStepsPerDirection = 6;

        [Tooltip("How strongly AO also occludes direct lighting in deep crevices.")]
        [Range(0.0f, 1.0f)]
        public float aoDirectLightOcclusion = 0.35f;

        [Tooltip("Multi-bounce AO approximation that tints occlusion by surface albedo instead of pure black.")]
        [Range(0.0f, 1.0f)]
        public float aoMultiBounceTint = 0.65f;

        [Tooltip("Distance at which AO smoothly fades out.")]
        [Range(5.0f, 300.0f)]
        public float aoMaxDistance = 75.0f;

        [Header("5. Subsurface Scattering & Contact Shadows")]
        [Tooltip("Enables screen-space Subsurface Scattering diffusion (RGB mean-free-path scatter for skin, wax, jade, marble, foliage).")]
        public bool enableSubsurfaceScattering = true;

        [Tooltip("Overall Subsurface Scattering blend intensity.")]
        [Range(0.0f, 3.0f)]
        public float sssIntensity = 0.85f;

        [Tooltip("Screen-space diffusion radius weighted by depth.")]
        [Range(0.1f, 5.0f)]
        public float sssRadius = 1.65f;

        [Tooltip("RGB subsurface scatter falloff color (Red scatters widest, Green medium, Blue shortest).")]
        [ColorUsage(false, true)]
        public Color sssScatterColor = new Color(1.0f, 0.34f, 0.18f, 1.0f);

        [Tooltip("Depth gradient sensitivity preventing SSS from bleeding across disconnected object silhouettes.")]
        [Range(1.0f, 100.0f)]
        public float sssDepthFalloff = 32.0f;

        [Tooltip("Enables screen-space raymarched Contact Shadows toward the main directional light.")]
        public bool enableContactShadows = true;

        [Tooltip("World-space raymarch length for micro contact shadows.")]
        [Range(0.02f, 2.0f)]
        public float contactShadowLength = 0.38f;

        [Tooltip("Opacity of raymarched contact shadows.")]
        [Range(0.0f, 1.0f)]
        public float contactShadowIntensity = 0.78f;

        [Tooltip("Raymarching steps for contact shadows.")]
        [Range(4, 32)]
        public int contactShadowSteps = 12;

        [Header("6. Screen-Space Reflections (SSR) & Volumetric Lighting")]
        [Tooltip("Enables raymarched Screen-Space Reflections weighted by Fresnel and surface smoothness.")]
        public bool enableSSR = true;

        [Tooltip("Reflection intensity multiplier.")]
        [Range(0.0f, 2.0f)]
        public float ssrIntensity = 0.55f;

        [Tooltip("Maximum raymarching steps per reflection ray.")]
        [Range(8, 64)]
        public int ssrMaxSteps = 24;

        [Tooltip("Maximum world-space distance for reflection rays.")]
        [Range(1.0f, 100.0f)]
        public float ssrMaxDistance = 18.0f;

        [Tooltip("Surface thickness threshold for SSR ray hits.")]
        [Range(0.05f, 2.0f)]
        public float ssrThickness = 0.30f;

        [Tooltip("Fresnel exponent controlling grazing-angle reflection boost.")]
        [Range(1.0f, 10.0f)]
        public float ssrFresnelPower = 4.0f;

        [Tooltip("Enables physically based raymarched Volumetric Light Shafts (God Rays) & Mie scattering fog using the main shadow map.")]
        public bool enableVolumetricLighting = true;

        [Tooltip("Intensity of volumetric light shafts and atmospheric scattering.")]
        [Range(0.0f, 3.0f)]
        public float volumetricIntensity = 0.68f;

        [Tooltip("Number of raymarch steps through the shadow volume per pixel.")]
        [Range(4, 48)]
        public int volumetricSteps = 16;

        [Tooltip("Maximum distance in meters for volumetric shadow raymarching.")]
        [Range(5.0f, 150.0f)]
        public float volumetricMaxDistance = 35.0f;

        [Tooltip("Henyey-Greenstein Mie forward-scattering anisotropy g (0 = isotropic, 0.75 = strong sun shafts).")]
        [Range(0.0f, 0.95f)]
        public float volumetricMieAnisotropy = 0.65f;

        [Tooltip("Tint of the volumetric scattering medium.")]
        [ColorUsage(false, true)]
        public Color volumetricFogColor = new Color(1.0f, 0.92f, 0.78f, 1.0f);

        [Tooltip("Vertical height attenuation of volumetric fog.")]
        [Range(0.0f, 2.0f)]
        public float volumetricHeightFalloff = 0.14f;

        [Header("7. HDR Bloom")]
        [Tooltip("Enables multi-mip Dual-Kawase HDR Bloom.")]
        public bool enableBloom = true;

        [Tooltip("Luminance threshold above which pixels contribute to bloom.")]
        [Range(0.0f, 5.0f)]
        public float bloomThreshold = 0.85f;

        [Tooltip("Soft knee transition around the bloom threshold.")]
        [Range(0.0f, 1.0f)]
        public float bloomSoftKnee = 0.55f;

        [Tooltip("Final additive intensity of HDR bloom.")]
        [Range(0.0f, 5.0f)]
        public float bloomIntensity = 0.75f;

        [Tooltip("How widely light scatters across bloom mip levels.")]
        [Range(0.1f, 1.0f)]
        public float bloomScatter = 0.72f;

        [Tooltip("Color tint applied to bloom highlights.")]
        [ColorUsage(false, true)]
        public Color bloomTint = new Color(1.0f, 0.96f, 0.90f, 1.0f);

        [Tooltip("Anamorphic horizontal/vertical stretch ratio (-1 = vertical, 0 = round, +1 = horizontal flare).")]
        [Range(-1.0f, 1.0f)]
        public float bloomAnamorphicRatio = 0.0f;

        [Tooltip("Number of downsample/upsample pyramid levels.")]
        [Range(2, 6)]
        public int bloomIterations = 4;

        [Header("8. Chromatic Aberration & Camera Lens Effects")]
        [Tooltip("Enables multi-tap spectral radial Chromatic Aberration.")]
        public bool enableChromaticAberration = true;

        [Tooltip("Radial dispersion strength at screen corners.")]
        [Range(0.0f, 3.0f)]
        public float chromaticAberrationIntensity = 0.35f;

        [Tooltip("Radial falloff power from screen center to edges.")]
        [Range(0.5f, 4.0f)]
        public float chromaticAberrationRadialPower = 1.6f;

        [Tooltip("Spectral samples along the dispersion vector.")]
        [Range(3, 9)]
        public int chromaticAberrationSamples = 5;

        [Tooltip("Enables Circle-of-Confusion Depth of Field blur.")]
        public bool enableDepthOfField = false;

        [Tooltip("Focus distance in meters from the camera.")]
        [Range(0.5f, 50.0f)]
        public float dofFocusDistance = 6.5f;

        [Tooltip("Depth range around Focus Distance that stays sharp.")]
        [Range(0.5f, 30.0f)]
        public float dofFocusRange = 4.0f;

        [Tooltip("Maximum disc blur radius in low-res pixels.")]
        [Range(0.0f, 6.0f)]
        public float dofBokehRadius = 2.0f;

        [Tooltip("Enables natural lens Vignette darkening.")]
        public bool enableVignette = true;

        [Tooltip("Vignette corner darkening strength.")]
        [Range(0.0f, 1.0f)]
        public float vignetteIntensity = 0.32f;

        [Tooltip("Vignette falloff smoothness.")]
        [Range(0.05f, 1.0f)]
        public float vignetteSmoothness = 0.45f;

        [Tooltip("Vignette border tint color.")]
        public Color vignetteColor = new Color(0.03f, 0.02f, 0.04f, 1.0f);

        [Tooltip("Enables organic Film Grain.")]
        public bool enableFilmGrain = true;

        [Tooltip("Film grain amplitude.")]
        [Range(0.0f, 0.5f)]
        public float filmGrainIntensity = 0.06f;

        [Tooltip("Luminance response (higher = more grain in shadows/midtones, less in blown highlights).")]
        [Range(0.0f, 1.0f)]
        public float filmGrainResponse = 0.8f;

        [Tooltip("Barrel / Pincushion lens distortion (-0.5 to +0.5).")]
        [Range(-0.5f, 0.5f)]
        public float lensDistortion = 0.0f;

        [Header("9. Tone Mapping & Custom Color Grading")]
        [Tooltip("HDR to SDR tone mapping operator (AgX and ACES Filmic recommended for ultra-realistic lighting).")]
        public ToneMappingMode toneMapping = ToneMappingMode.AgX;

        [Tooltip("Post-exposure adjustment in EV stops.")]
        [Range(-4.0f, 4.0f)]
        public float postExposureEV = 0.25f;

        [Tooltip("White balance color temperature (-1 cool blue to +1 warm amber).")]
        [Range(-1.0f, 1.0f)]
        public float whiteBalanceTemperature = 0.06f;

        [Tooltip("White balance green/magenta tint.")]
        [Range(-1.0f, 1.0f)]
        public float whiteBalanceTint = 0.0f;

        [Tooltip("Log-space contrast around middle grey.")]
        [Range(0.5f, 2.0f)]
        public float contrast = 1.12f;

        [Tooltip("Global color saturation.")]
        [Range(0.0f, 2.0f)]
        public float saturation = 1.08f;

        [Tooltip("Shadow range color multiplier (Lift).")]
        public Color shadowsTint = new Color(0.95f, 0.97f, 1.04f, 1.0f);

        [Tooltip("Midtone range color multiplier (Gamma).")]
        public Color midtonesTint = Color.white;

        [Tooltip("Highlight range color multiplier (Gain).")]
        public Color highlightsTint = new Color(1.03f, 1.0f, 0.96f, 1.0f);

        [Tooltip("Display gamma correction factor.")]
        [Range(0.5f, 2.0f)]
        public float gamma = 1.0f;

        [Header("10. Pipeline Diagnostics")]
        [Tooltip("Inspect individual lighting and buffer passes in real time.")]
        public PipelineDebugView debugView = PipelineDebugView.FinalOutput;

        /// <summary>
        /// Computes the target low-resolution RenderTexture dimensions for a given full-screen viewport size.
        /// </summary>
        public Vector2Int ComputeLowResolution(int screenWidth, int screenHeight)
        {
            screenWidth = Mathf.Max(16, screenWidth);
            screenHeight = Mathf.Max(16, screenHeight);

            if (!enableLowResRendering)
                return new Vector2Int(screenWidth, screenHeight);

            int w, h;
            float aspect = (float)screenWidth / Mathf.Max(1, screenHeight);

            switch (sizingMode)
            {
                case ResolutionSizingMode.FixedVerticalHeight:
                    h = Mathf.Clamp(targetVerticalHeight, 32, screenHeight);
                    w = Mathf.Max(32, Mathf.RoundToInt(h * aspect));
                    break;

                case ResolutionSizingMode.DownscaleFactor:
                    float factor = Mathf.Max(1.0f, downscaleFactor);
                    w = Mathf.Max(32, Mathf.RoundToInt(screenWidth / factor));
                    h = Mathf.Max(32, Mathf.RoundToInt(screenHeight / factor));
                    break;

                case ResolutionSizingMode.CustomDimensions:
                default:
                    w = Mathf.Clamp(customResolution.x, 32, 4096);
                    h = Mathf.Clamp(customResolution.y, 32, 4096);
                    break;
            }

            // Keep dimensions even for clean mip/half-res alignment
            w = Mathf.Max(32, (w / 2) * 2);
            h = Mathf.Max(32, (h / 2) * 2);
            return new Vector2Int(w, h);
        }
    }
}

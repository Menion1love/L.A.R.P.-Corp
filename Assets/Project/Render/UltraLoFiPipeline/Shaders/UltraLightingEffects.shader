Shader "Hidden/UltraLoFiPipeline/UltraLightingEffects"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        ZTest Always
        ZWrite Off
        Cull Off

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

        TEXTURE2D_X(_SourceColorTex);
        TEXTURE2D_X(_GITex);
        TEXTURE2D_X(_AOTex);
        TEXTURE2D_X(_SSSTex);
        TEXTURE2D_X(_SSRVolumetricTex);

        TEXTURE2D_X_FLOAT(_CameraDepthTexture);
        TEXTURE2D_X_FLOAT(_CameraNormalsTexture);

        // Camera basis & projection parameters set from C#
        float4 _UltraCamPosWS;
        float4 _UltraCamForward;
        float4 _UltraCamRight;
        float4 _UltraCamUp;
        float4 _UltraCamProjParams; // x: aspect, y: tanHalfFov, z: near, w: far
        float4 _UltraLowResSize;    // x: width, y: height, z: 1/width, w: 1/height
        float4 _UltraFrameParams;   // x: frameIndex, y: debugViewMode, z: specAliasingBoost, w: unused

        // GTAO parameters
        float4 _UltraAOParams1;     // x: intensity, y: radius, z: power, w: directOcclusion
        float4 _UltraAOParams2;     // x: dirCount, y: stepsPerDir, z: multiBounceTint, w: maxDistance

        // SSGI & Contact Shadows parameters
        float4 _UltraGIParams1;     // x: intensity, y: rayCount, z: stepsPerRay, w: rayRadius
        float4 _UltraGIParams2;     // x: thickness, y: emissiveBoost, z: bounceSaturation, w: skyContrib
        float4 _UltraGISkyColor;
        float4 _UltraGIGroundColor;
        float4 _UltraGIDenoiseParams; // x: stepWidth, y: depthWeight, z: normalWeight, w: enabled
        float4 _UltraContactShadowParams; // x: enabled, y: length, z: intensity, w: steps

        // Subsurface Scattering parameters
        float4 _UltraSSSParams;     // x: enabled, y: intensity, z: radius, w: depthFalloff
        float4 _UltraSSSScatterColor;

        // SSR & Volumetric parameters
        float4 _UltraSSRParams;     // x: enabled, y: intensity, z: maxSteps, w: maxDistance
        float4 _UltraSSRParams2;    // x: thickness, y: fresnelPower, z: unused, w: unused
        float4 _UltraVolumetricParams; // x: enabled, y: intensity, z: steps, w: maxDistance
        float4 _UltraVolumetricParams2; // x: mieAnisotropyG, y: heightFalloff, z: unused, w: unused
        float4 _UltraVolumetricColor;

        struct Attributes
        {
            uint vertexID : SV_VertexID;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv         : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings VertFullScreen(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
            output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
            return output;
        }

        float SampleRawDepth(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, saturate(uv), 0).r;
        }

        bool IsSkyDepth(float rawDepth)
        {
            #if UNITY_REVERSED_Z
            return rawDepth <= 0.00001;
            #else
            return rawDepth >= 0.99999;
            #endif
        }

        float SampleLinearEyeDepth(float2 uv, out float rawDepth)
        {
            rawDepth = SampleRawDepth(uv);
            return LinearEyeDepth(rawDepth, _ZBufferParams);
        }

        float4 SampleNormalAndSmoothness(float2 uv)
        {
            float4 nSample = SAMPLE_TEXTURE2D_X_LOD(_CameraNormalsTexture, sampler_PointClamp, saturate(uv), 0);
            float3 N = nSample.xyz;
            float lenSq = dot(N, N);
            if (lenSq < 0.001)
                N = float3(0, 1, 0);
            else
                N = N * rsqrt(lenSq);
            return float4(N, saturate(nSample.w));
        }

        float3 ReconstructWorldPos(float2 uv, float eyeDepth)
        {
            #if UNITY_UV_STARTS_AT_TOP
            float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
            #else
            float2 ndc = uv * 2.0 - 1.0;
            #endif
            float aspect = _UltraCamProjParams.x;
            float tanHalfFov = _UltraCamProjParams.y;

            float3 viewRayWS = _UltraCamForward.xyz
                             + (ndc.x * aspect * tanHalfFov) * _UltraCamRight.xyz
                             + (ndc.y * tanHalfFov) * _UltraCamUp.xyz;
            return _UltraCamPosWS.xyz + viewRayWS * eyeDepth;
        }

        bool ProjectWorldToScreenUV(float3 posWS, out float2 screenUV, out float eyeDepth)
        {
            float3 rel = posWS - _UltraCamPosWS.xyz;
            eyeDepth = dot(rel, _UltraCamForward.xyz);
            if (eyeDepth <= 0.05)
            {
                screenUV = 0.0;
                return false;
            }
            float aspect = _UltraCamProjParams.x;
            float tanHalfFov = _UltraCamProjParams.y;
            float xNdc = dot(rel, _UltraCamRight.xyz) / (eyeDepth * aspect * tanHalfFov);
            float yNdc = dot(rel, _UltraCamUp.xyz) / (eyeDepth * tanHalfFov);
            #if UNITY_UV_STARTS_AT_TOP
            screenUV = float2(xNdc * 0.5 + 0.5, 0.5 - yNdc * 0.5);
            #else
            screenUV = float2(xNdc, yNdc) * 0.5 + 0.5;
            #endif
            return (screenUV.x >= 0.0 && screenUV.x <= 1.0 && screenUV.y >= 0.0 && screenUV.y <= 1.0);
        }

        // Spatial Interleaved Gradient Noise (Jimenez 2014)
        float InterleavedGradientNoise(float2 pixelCoord)
        {
            float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
            return frac(magic.z * frac(dot(pixelCoord, magic.xy)));
        }

        float2 Hash22(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.xx + p3.yz) * p3.zy);
        }

        // Orthonormal basis from world normal N
        void BuildOrthonormalBasis(float3 N, out float3 T, out float3 B)
        {
            float3 up = abs(N.y) < 0.999 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
            T = normalize(cross(up, N));
            B = cross(N, T);
        }
        ENDHLSL

        // ==================================================================
        // Pass 0: Multi-Scale GTAO (Ground-Truth Ambient Occlusion + Multi-Bounce Albedo)
        // ==================================================================
        Pass
        {
            Name "UltraGTAO"
            HLSLPROGRAM
            #pragma vertex VertFullScreen
            #pragma fragment FragGTAO

            half4 FragGTAO(Varyings input) : SV_Target
            {
                float rawDepth;
                float centerDepth = SampleLinearEyeDepth(input.uv, rawDepth);
                if (IsSkyDepth(rawDepth) || centerDepth > _UltraAOParams2.w)
                    return half4(1.0, 1.0, 1.0, 1.0);

                float3 posWS = ReconstructWorldPos(input.uv, centerDepth);
                float3 N = SampleNormalAndSmoothness(input.uv).xyz;

                float2 pixelCoord = floor(input.uv * _UltraLowResSize.xy);
                float noise = InterleavedGradientNoise(pixelCoord);

                int dirCount = clamp((int)_UltraAOParams2.x, 2, 12);
                int stepsPerDir = clamp((int)_UltraAOParams2.y, 2, 12);
                float worldRadius = _UltraAOParams1.y;

                float3 T, B;
                BuildOrthonormalBasis(N, T, B);

                float occlusion = 0.0;
                float totalWeight = 0.0;

                [loop]
                for (int d = 0; d < dirCount; d++)
                {
                    float angle = ((float)d + noise) * (TWO_PI / (float)dirCount);
                    float2 dir2D = float2(cos(angle), sin(angle));
                    float3 tangentDirWS = T * dir2D.x + B * dir2D.y;

                    float maxHorizon = 0.0;

                    [loop]
                    for (int s = 1; s <= stepsPerDir; s++)
                    {
                        float stepFrac = ((float)s - 0.35 * noise) / (float)stepsPerDir;
                        float marchDist = stepFrac * worldRadius;
                        float3 sampleTargetWS = posWS + tangentDirWS * marchDist + N * (0.02 * marchDist);

                        float2 sampleUV;
                        float expectedDepth;
                        if (!ProjectWorldToScreenUV(sampleTargetWS, sampleUV, expectedDepth))
                            continue;

                        float sampleRaw;
                        float actualDepth = SampleLinearEyeDepth(sampleUV, sampleRaw);
                        if (IsSkyDepth(sampleRaw))
                            continue;

                        float3 sampleActualWS = ReconstructWorldPos(sampleUV, actualDepth);
                        float3 deltaWS = sampleActualWS - posWS;
                        float dist = length(deltaWS);

                        if (dist > 0.005 && dist < worldRadius * 1.35)
                        {
                            float3 dirToSample = deltaWS / dist;
                            float elevation = saturate(dot(N, dirToSample) - 0.08);
                            float falloff = saturate(1.0 - (dist / (worldRadius * 1.25)));
                            maxHorizon = max(maxHorizon, elevation * falloff);
                        }
                    }

                    occlusion += maxHorizon;
                    totalWeight += 1.0;
                }

                occlusion = (totalWeight > 0.0) ? (occlusion / totalWeight) : 0.0;
                float distanceFade = saturate(1.0 - centerDepth / max(1.0, _UltraAOParams2.w));
                float aoScalar = pow(saturate(1.0 - occlusion * _UltraAOParams1.x * distanceFade), _UltraAOParams1.z);

                // Multi-Bounce Colored AO Approximation (Jimenez et al.)
                float3 sceneColor = SAMPLE_TEXTURE2D_X_LOD(_SourceColorTex, sampler_PointClamp, input.uv, 0).rgb;
                float3 approxAlbedo = saturate(sceneColor / (1.0 + Luminance(sceneColor)));
                float3 multiBounceAO = aoScalar / max(float3(0.001, 0.001, 0.001), aoScalar * (1.0 - approxAlbedo) + approxAlbedo);
                float3 finalAOColor = lerp(float3(aoScalar, aoScalar, aoScalar), multiBounceAO, _UltraAOParams2.z);

                return half4(finalAOColor, aoScalar);
            }
            ENDHLSL
        }

        // ==================================================================
        // Pass 1: Screen-Space Global Illumination (SSGI) + Contact Shadows
        // ==================================================================
        Pass
        {
            Name "UltraSSGIAndContactShadows"
            HLSLPROGRAM
            #pragma vertex VertFullScreen
            #pragma fragment FragSSGI

            half4 FragSSGI(Varyings input) : SV_Target
            {
                float rawDepth;
                float centerDepth = SampleLinearEyeDepth(input.uv, rawDepth);
                if (IsSkyDepth(rawDepth))
                    return half4(0.0, 0.0, 0.0, 1.0);

                float3 posWS = ReconstructWorldPos(input.uv, centerDepth);
                float3 N = SampleNormalAndSmoothness(input.uv).xyz;

                float2 pixelCoord = floor(input.uv * _UltraLowResSize.xy);
                float ign = InterleavedGradientNoise(pixelCoord);
                float2 hash = Hash22(pixelCoord);

                float3 T, B;
                BuildOrthonormalBasis(N, T, B);

                float3 indirectLight = 0.0;
                int rayCount = clamp((int)_UltraGIParams1.y, 2, 16);
                int stepsPerRay = clamp((int)_UltraGIParams1.z, 4, 32);
                float rayRadius = _UltraGIParams1.w;
                float thickness = _UltraGIParams2.x;
                float emissiveBoost = _UltraGIParams2.y;

                // 1. Raymarched Hemispherical SSGI
                if (_UltraGIParams1.x > 0.001)
                {
                    [loop]
                    for (int r = 0; r < rayCount; r++)
                    {
                        // Golden-ratio Fibonacci hemisphere distribution rotated per-pixel
                        float fi = ((float)r + 0.5) / (float)rayCount;
                        float phi = TWO_PI * frac((float)r * 0.61803398875 + ign);
                        float cosTheta = sqrt(1.0 - fi * 0.85);
                        float sinTheta = sqrt(1.0 - cosTheta * cosTheta);

                        float3 localDir = float3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);
                        float3 rayDirWS = normalize(T * localDir.x + B * localDir.y + N * localDir.z);

                        bool hit = false;
                        float3 rayRadiance = 0.0;

                        [loop]
                        for (int s = 1; s <= stepsPerRay; s++)
                        {
                            float t = ((float)s - 0.3 * hash.x) / (float)stepsPerRay;
                            // Quadratic step distribution for near-field detail + far-field reach
                            float marchDist = (t * t * 0.75 + t * 0.25) * rayRadius;
                            float3 marchPosWS = posWS + N * 0.03 + rayDirWS * marchDist;

                            float2 sampleUV;
                            float rayEyeDepth;
                            if (!ProjectWorldToScreenUV(marchPosWS, sampleUV, rayEyeDepth))
                                break;

                            float sampleRaw;
                            float surfaceDepth = SampleLinearEyeDepth(sampleUV, sampleRaw);
                            if (IsSkyDepth(sampleRaw))
                                continue;

                            float depthDiff = rayEyeDepth - surfaceDepth;
                            if (depthDiff > 0.015 && depthDiff < thickness * (1.0 + marchDist * 0.25))
                            {
                                float3 hitNormal = SampleNormalAndSmoothness(sampleUV).xyz;
                                float3 hitColor = SAMPLE_TEXTURE2D_X_LOD(_SourceColorTex, sampler_PointClamp, sampleUV, 0).rgb;

                                // Boost bright/emissive surfaces for realistic indirect light bleeding
                                float luma = Luminance(hitColor);
                                float boost = 1.0 + saturate(luma - 0.75) * (emissiveBoost - 1.0);
                                float facingWeight = saturate(dot(hitNormal, -rayDirWS) * 0.7 + 0.4);
                                float distAtten = 1.0 / (1.0 + marchDist * 0.35);

                                rayRadiance = min(hitColor * boost * facingWeight * distAtten, 12.0);
                                hit = true;
                                break;
                            }
                        }

                        if (!hit)
                        {
                            // Unoccluded sky/ground hemisphere bounce
                            float skyFactor = saturate(rayDirWS.y * 0.5 + 0.5);
                            rayRadiance = lerp(_UltraGIGroundColor.rgb, _UltraGISkyColor.rgb, skyFactor) * _UltraGIParams2.w;
                        }

                        float lambert = saturate(dot(N, rayDirWS));
                        indirectLight += rayRadiance * lambert * 2.0;
                    }

                    indirectLight /= (float)rayCount;

                    // Saturation adjustment for bounce light bleeding
                    float giLuma = Luminance(indirectLight);
                    indirectLight = max(0.0, lerp(float3(giLuma, giLuma, giLuma), indirectLight, _UltraGIParams2.z));
                    indirectLight *= _UltraGIParams1.x;
                }

                // 2. Raymarched Screen-Space Contact Shadows toward Main Directional Light
                float contactShadow = 1.0;
                if (_UltraContactShadowParams.x > 0.5)
                {
                    float3 lightDirWS = normalize(_MainLightPosition.xyz);
                    float NoL = dot(N, lightDirWS);
                    if (NoL > 0.02)
                    {
                        int csSteps = clamp((int)_UltraContactShadowParams.w, 4, 32);
                        float csLength = _UltraContactShadowParams.y;
                        float csThickness = csLength * 0.35;

                        [loop]
                        for (int s = 1; s <= csSteps; s++)
                        {
                            float t = ((float)s - 0.25 * ign) / (float)csSteps;
                            float3 csPosWS = posWS + N * 0.015 + lightDirWS * (t * csLength);

                            float2 csUV;
                            float csRayDepth;
                            if (!ProjectWorldToScreenUV(csPosWS, csUV, csRayDepth))
                                break;

                            float csRaw;
                            float csSurfDepth = SampleLinearEyeDepth(csUV, csRaw);
                            if (IsSkyDepth(csRaw))
                                continue;

                            float diff = csRayDepth - csSurfDepth;
                            if (diff > 0.008 && diff < csThickness)
                            {
                                float fade = 1.0 - t * t;
                                contactShadow = min(contactShadow, 1.0 - saturate(fade * _UltraContactShadowParams.z));
                            }
                        }
                    }
                }

                return half4(indirectLight, contactShadow);
            }
            ENDHLSL
        }

        // ==================================================================
        // Pass 2: Edge-Preserving À-Trous Bilateral Spatial Denoiser for SSGI
        // ==================================================================
        Pass
        {
            Name "UltraGIDenoiser"
            HLSLPROGRAM
            #pragma vertex VertFullScreen
            #pragma fragment FragGIDenoise

            half4 FragGIDenoise(Varyings input) : SV_Target
            {
                float4 centerGI = SAMPLE_TEXTURE2D_X_LOD(_GITex, sampler_PointClamp, input.uv, 0);
                if (_UltraGIDenoiseParams.w < 0.5)
                    return centerGI;

                float rawDepth;
                float centerDepth = SampleLinearEyeDepth(input.uv, rawDepth);
                if (IsSkyDepth(rawDepth))
                    return centerGI;

                float3 centerN = SampleNormalAndSmoothness(input.uv).xyz;

                float stepWidth = _UltraGIDenoiseParams.x;
                float depthWeightParam = _UltraGIDenoiseParams.y;
                float normalWeightParam = _UltraGIDenoiseParams.z;
                float2 texelSize = _UltraLowResSize.zw;

                static const float kernelWeights[3] = { 1.0, 0.6667, 0.25 };

                float3 accumGI = 0.0;
                float accumCS = 0.0;
                float totalWeight = 0.0;

                [unroll]
                for (int y = -2; y <= 2; y++)
                {
                    [unroll]
                    for (int x = -2; x <= 2; x++)
                    {
                        float2 offset = float2(x, y) * stepWidth * texelSize;
                        float2 sampleUV = saturate(input.uv + offset);

                        float sampleRaw;
                        float sampleDepth = SampleLinearEyeDepth(sampleUV, sampleRaw);
                        if (IsSkyDepth(sampleRaw))
                            continue;

                        float3 sampleN = SampleNormalAndSmoothness(sampleUV).xyz;
                        float4 sampleGI = SAMPLE_TEXTURE2D_X_LOD(_GITex, sampler_PointClamp, sampleUV, 0);

                        float kWeight = kernelWeights[abs(x)] * kernelWeights[abs(y)];
                        float relDepthDiff = abs(centerDepth - sampleDepth) / max(0.1, centerDepth * 0.05);
                        float dWeight = exp(-relDepthDiff * depthWeightParam * 0.1);
                        float nWeight = pow(saturate(dot(centerN, sampleN)), normalWeightParam);

                        float w = max(1e-4, kWeight * dWeight * nWeight);
                        accumGI += sampleGI.rgb * w;
                        accumCS += sampleGI.a * w;
                        totalWeight += w;
                    }
                }

                if (totalWeight > 0.0001)
                    return half4(accumGI / totalWeight, accumCS / totalWeight);

                return centerGI;
            }
            ENDHLSL
        }

        // ==================================================================
        // Pass 3: Screen-Space Subsurface Scattering (RGB Mean-Free-Path Diffusion)
        // ==================================================================
        Pass
        {
            Name "UltraScreenSpaceSSS"
            HLSLPROGRAM
            #pragma vertex VertFullScreen
            #pragma fragment FragSSSS

            half4 FragSSSS(Varyings input) : SV_Target
            {
                float3 centerColor = SAMPLE_TEXTURE2D_X_LOD(_SourceColorTex, sampler_PointClamp, input.uv, 0).rgb;
                if (_UltraSSSParams.x < 0.5 || _UltraSSSParams.y <= 0.001)
                    return half4(centerColor, 0.0);

                float rawDepth;
                float centerDepth = SampleLinearEyeDepth(input.uv, rawDepth);
                if (IsSkyDepth(rawDepth))
                    return half4(centerColor, 0.0);

                float3 centerN = SampleNormalAndSmoothness(input.uv).xyz;

                // Depth-scaled screen-space kernel radius
                float2 texelSize = _UltraLowResSize.zw;
                float radiusScale = _UltraSSSParams.z / max(0.6, sqrt(centerDepth));
                float depthFalloff = _UltraSSSParams.w;

                // Dual-lobe RGB diffusion weights (Red scatters farthest, Green mid, Blue narrowest)
                float3 mfpColor = max(float3(0.05, 0.05, 0.05), _UltraSSSScatterColor.rgb);

                static const float2 offsets[12] =
                {
                    float2( 1.0,  0.0), float2(-1.0,  0.0),
                    float2( 0.0,  1.0), float2( 0.0, -1.0),
                    float2( 0.85,  0.85), float2(-0.85,  0.85),
                    float2( 0.85, -0.85), float2(-0.85, -0.85),
                    float2( 2.2,  0.0), float2(-2.2,  0.0),
                    float2( 0.0,  2.2), float2( 0.0, -2.2)
                };

                float3 accumColor = centerColor;
                float3 totalWeight = float3(1.0, 1.0, 1.0);

                [unroll]
                for (int i = 0; i < 12; i++)
                {
                    float2 sampleUV = saturate(input.uv + offsets[i] * texelSize * radiusScale);
                    float sampleRaw;
                    float sampleDepth = SampleLinearEyeDepth(sampleUV, sampleRaw);
                    if (IsSkyDepth(sampleRaw))
                        continue;

                    float3 sampleN = SampleNormalAndSmoothness(sampleUV).xyz;
                    float3 sampleColor = SAMPLE_TEXTURE2D_X_LOD(_SourceColorTex, sampler_PointClamp, sampleUV, 0).rgb;

                    float dist = length(offsets[i]);
                    float depthDiff = abs(centerDepth - sampleDepth);
                    // Prevent bleeding across sharp geometric silhouettes
                    float depthGate = exp(-depthDiff * depthFalloff);
                    // Curvature boost where normals bend smoothly across organic geometry
                    float normalCurvature = saturate(dot(centerN, sampleN) * 0.7 + 0.3);

                    float3 rgbProfile = exp(-dist / (mfpColor * 2.2)) * depthGate * normalCurvature;
                    accumColor += sampleColor * rgbProfile;
                    totalWeight += rgbProfile;
                }

                float3 diffusedColor = accumColor / max(float3(0.001, 0.001, 0.001), totalWeight);
                return half4(diffusedColor, 1.0);
            }
            ENDHLSL
        }

        // ==================================================================
        // Pass 4: Screen-Space Reflections (SSR) & Volumetric Mie Light Shafts
        // ==================================================================
        Pass
        {
            Name "UltraSSRAndVolumetrics"
            HLSLPROGRAM
            #pragma vertex VertFullScreen
            #pragma fragment FragSSRAndVolumetrics
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            // Henyey-Greenstein phase function for Mie atmospheric forward scattering
            float EvaluateHenyeyGreenstein(float cosTheta, float g)
            {
                float g2 = g * g;
                float denom = max(0.001, 1.0 + g2 - 2.0 * g * cosTheta);
                return (1.0 - g2) / (4.0 * PI * denom * sqrt(denom));
            }

            half4 FragSSRAndVolumetrics(Varyings input) : SV_Target
            {
                float rawDepth;
                float centerDepth = SampleLinearEyeDepth(input.uv, rawDepth);
                bool isSky = IsSkyDepth(rawDepth);

                float3 posWS = isSky
                    ? ReconstructWorldPos(input.uv, _UltraVolumetricParams.w)
                    : ReconstructWorldPos(input.uv, centerDepth);

                float2 pixelCoord = floor(input.uv * _UltraLowResSize.xy);
                float ign = InterleavedGradientNoise(pixelCoord);

                float3 ssrColor = 0.0;
                float ssrWeight = 0.0;

                // 1. Raymarched Screen-Space Reflections (SSR) with Near-Field Binary Refinement & Metallic F0
                if (!isSky && _UltraSSRParams.x > 0.5 && _UltraSSRParams.y > 0.001)
                {
                    float4 normalAndSmooth = SampleNormalAndSmoothness(input.uv);
                    float3 N = normalAndSmooth.xyz;
                    float reflectivity = normalAndSmooth.w;

                    if (reflectivity > 0.15)
                    {
                        float3 V = normalize(_UltraCamPosWS.xyz - posWS);
                        float3 R = reflect(-V, N);

                        float NoV = saturate(dot(N, V));
                        // Metallic & high-reflectivity surfaces reflect strongly even at normal incidence (center of spheres)
                        float f0 = max(0.06, reflectivity * reflectivity);
                        float fresnel = f0 + (1.0 - f0) * pow(1.0 - NoV, _UltraSSRParams2.y);

                        int maxSteps = clamp((int)_UltraSSRParams.z, 12, 64);
                        float maxDist = _UltraSSRParams.w;
                        float thickness = _UltraSSRParams2.x;

                        float prevMarchDist = 0.02;

                        [loop]
                        for (int s = 1; s <= maxSteps; s++)
                        {
                            float t = ((float)s - 0.2 * ign) / (float)maxSteps;
                            // Dense near-field steps (0.04m..2.5m) to catch adjacent spheres, plinths, and pedestals
                            float marchDist = 0.03 + (t * t * 0.78 + t * 0.22) * maxDist;
                            float3 rayPosWS = posWS + N * 0.025 + R * marchDist;

                            float2 hitUV;
                            float rayEyeDepth;
                            if (!ProjectWorldToScreenUV(rayPosWS, hitUV, rayEyeDepth))
                                break;

                            float hitRaw;
                            float surfDepth = SampleLinearEyeDepth(hitUV, hitRaw);
                            if (IsSkyDepth(hitRaw))
                            {
                                prevMarchDist = marchDist;
                                continue;
                            }

                            float diff = rayEyeDepth - surfDepth;
                            float allowedThickness = max(0.25, thickness * (1.0 + marchDist * 0.35));

                            if (diff > 0.005 && diff < allowedThickness)
                            {
                                // 3-step binary search refinement between prevMarchDist and marchDist for accurate curved surface hits
                                float loDist = prevMarchDist;
                                float hiDist = marchDist;
                                float2 refinedUV = hitUV;

                                [unroll]
                                for (int b = 0; b < 3; b++)
                                {
                                    float midDist = 0.5 * (loDist + hiDist);
                                    float3 midPosWS = posWS + N * 0.025 + R * midDist;
                                    float2 midUV;
                                    float midEyeDepth;
                                    if (ProjectWorldToScreenUV(midPosWS, midUV, midEyeDepth))
                                    {
                                        float midRaw;
                                        float midSurfDepth = SampleLinearEyeDepth(midUV, midRaw);
                                        if (!IsSkyDepth(midRaw) && (midEyeDepth > midSurfDepth))
                                        {
                                            hiDist = midDist;
                                            refinedUV = midUV;
                                        }
                                        else
                                        {
                                            loDist = midDist;
                                        }
                                    }
                                }

                                float2 edgeDist = abs(refinedUV * 2.0 - 1.0);
                                float edgeFade = saturate(1.0 - max(edgeDist.x, edgeDist.y));
                                edgeFade = smoothstep(0.0, 0.18, edgeFade);

                                float distFade = saturate(1.15 - t);
                                ssrColor = SAMPLE_TEXTURE2D_X_LOD(_SourceColorTex, sampler_PointClamp, refinedUV, 0).rgb;
                                ssrWeight = saturate(fresnel * reflectivity * edgeFade * distFade * _UltraSSRParams.y);
                                break;
                            }

                            prevMarchDist = marchDist;
                        }
                    }
                }

                // 2. Raymarched Volumetric Light Shafts (God Rays) & Mie Fog via Main Shadow Map
                float volumetricAccum = 0.0;
                if (_UltraVolumetricParams.x > 0.5 && _UltraVolumetricParams.y > 0.001)
                {
                    int volSteps = clamp((int)_UltraVolumetricParams.z, 4, 48);
                    float maxVolDist = _UltraVolumetricParams.w;
                    float targetDist = isSky ? maxVolDist : min(centerDepth, maxVolDist);

                    float3 camToPixel = posWS - _UltraCamPosWS.xyz;
                    float3 rayDir = normalize(camToPixel);
                    float3 lightDirWS = normalize(_MainLightPosition.xyz);

                    float cosTheta = dot(rayDir, lightDirWS);
                    float miePhase = EvaluateHenyeyGreenstein(cosTheta, _UltraVolumetricParams2.x);
                    // Physically bounded Mie phase so god rays have strong contrast without washing out foreground
                    miePhase = clamp(miePhase * 0.42, 0.04, 0.55);

                    float stepLen = targetDist / (float)volSteps;
                    float heightFalloff = _UltraVolumetricParams2.y;

                    float accumScattering = 0.0;
                    [loop]
                    for (int v = 0; v < volSteps; v++)
                    {
                        float marchDist = ((float)v + ign) * stepLen;
                        float3 samplePosWS = _UltraCamPosWS.xyz + rayDir * marchDist;

                        float4 shadowCoord = TransformWorldToShadowCoord(samplePosWS);
                        float shadowAtten = MainLightRealtimeShadow(shadowCoord);

                        float heightDensity = exp(-max(0.0, samplePosWS.y) * heightFalloff);
                        float transmittance = exp(-marchDist * 0.045);

                        accumScattering += shadowAtten * heightDensity * transmittance;
                    }

                    float opticalDepth = 1.0 - exp(-targetDist * 0.038);
                    volumetricAccum = (accumScattering / (float)volSteps) * miePhase * opticalDepth * _UltraVolumetricParams.y;
                }

                // Pack SSR RGB weighted color + Volumetric scattering intensity in Alpha
                return half4(ssrColor * ssrWeight, volumetricAccum);
            }
            ENDHLSL
        }

        // ==================================================================
        // Pass 5: Physical HDR Lighting Composite & Diagnostic Debug Views
        // ==================================================================
        Pass
        {
            Name "UltraLightingComposite"
            HLSLPROGRAM
            #pragma vertex VertFullScreen
            #pragma fragment FragLightingComposite

            half4 FragLightingComposite(Varyings input) : SV_Target
            {
                float3 directColor = SAMPLE_TEXTURE2D_X_LOD(_SourceColorTex, sampler_PointClamp, input.uv, 0).rgb;
                float4 aoSample = SAMPLE_TEXTURE2D_X_LOD(_AOTex, sampler_PointClamp, input.uv, 0);
                float4 giSample = SAMPLE_TEXTURE2D_X_LOD(_GITex, sampler_PointClamp, input.uv, 0);
                float4 sssSample = SAMPLE_TEXTURE2D_X_LOD(_SSSTex, sampler_PointClamp, input.uv, 0);
                float4 ssrVolSample = SAMPLE_TEXTURE2D_X_LOD(_SSRVolumetricTex, sampler_PointClamp, input.uv, 0);

                float rawDepth;
                float eyeDepth = SampleLinearEyeDepth(input.uv, rawDepth);
                bool isSky = IsSkyDepth(rawDepth);

                float3 multiBounceAO = aoSample.rgb;
                float scalarAO = aoSample.a;
                float3 indirectGI = giSample.rgb;
                float contactShadow = giSample.a;
                float3 sssColor = sssSample.rgb;
                float3 ssrColor = ssrVolSample.rgb;
                float volumetricShafts = ssrVolSample.a;

                int debugView = (int)round(_UltraFrameParams.y);

                // Diagnostic Debug Views
                if (debugView == 1) // RawDirectLighting
                    return half4(directColor, 1.0);
                if (debugView == 2) // SSGIOnly
                    return half4(indirectGI, 1.0);
                if (debugView == 3) // AmbientOcclusionOnly
                    return half4(multiBounceAO * contactShadow, 1.0);
                if (debugView == 4) // SubsurfaceScatteringOnly
                    return half4(sssColor, 1.0);
                if (debugView == 5) // VolumetricLightOnly
                    return half4(volumetricShafts * _UltraVolumetricColor.rgb * _MainLightColor.rgb, 1.0);
                if (debugView == 6) // SSROnly
                    return half4(ssrColor, 1.0);
                if (debugView == 7) // WorldNormals
                {
                    float3 n = SampleNormalAndSmoothness(input.uv).xyz;
                    return half4(n * 0.5 + 0.5, 1.0);
                }
                if (debugView == 8) // LinearDepth
                {
                    float dVis = saturate(eyeDepth / 40.0);
                    return half4(dVis, dVis, dVis, 1.0);
                }

                float3 finalHDR = directColor;

                if (!isSky)
                {
                    // 1. Blend Screen-Space Subsurface Scattering diffusion into base lit color
                    if (_UltraSSSParams.x > 0.5 && _UltraSSSParams.y > 0.001)
                    {
                        // Preserve sharp specular highlights while diffusing mid-range subsurface luminance
                        float lumaDiff = max(0.0, Luminance(directColor) - Luminance(sssColor));
                        float3 specPreserve = directColor * saturate(lumaDiff * 1.5);
                        finalHDR = lerp(directColor, sssColor + specPreserve * 0.5, saturate(_UltraSSSParams.y * 0.65));
                    }

                    // 2. Apply Contact Shadows and Direct AO micro-occlusion
                    float directOcclusion = lerp(1.0, scalarAO, _UltraAOParams1.w) * contactShadow;
                    finalHDR *= directOcclusion;

                    // 3. Add Indirect SSGI/RTGI modulated by Multi-Bounce Colored AO and surface Albedo estimation
                    float3 surfaceTint = lerp(float3(0.45, 0.45, 0.45), saturate(directColor / (0.25 + Luminance(directColor))), 0.65);
                    finalHDR += indirectGI * surfaceTint * multiBounceAO;

                    // 4. Add Ray-Traced (DXR) or Screen-Space Reflections
                    if (_UltraFrameParams.w > 0.5)
                    {
                        float reflReplaceWeight = saturate(sssSample.a);
                        float3 directSpecGlint = max(0.0, directColor - 1.15);
                        // Energy-conserving composite: attenuate underlying diffuse by metallic/Fresnel replacement weight,
                        // then add Fresnel-weighted Ray-Traced reflection once (avoiding double Fresnel attenuation)
                        finalHDR = finalHDR * (1.0 - reflReplaceWeight * 0.88) + (ssrColor + directSpecGlint * reflReplaceWeight) * lerp(0.7, 1.0, scalarAO);
                    }
                    else
                    {
                        finalHDR += ssrColor * lerp(0.5, 1.0, scalarAO);
                    }

                    // 5. Crisp 1-Pixel Specular Aliasing Glint Boost (accentuates lo-fi realistic highlights)
                    float specBoost = _UltraFrameParams.z;
                    if (specBoost > 0.001)
                    {
                        float2 texel = _UltraLowResSize.zw;
                        float3 nL = SAMPLE_TEXTURE2D_X_LOD(_SourceColorTex, sampler_PointClamp, saturate(input.uv - float2(texel.x, 0)), 0).rgb;
                        float3 nR = SAMPLE_TEXTURE2D_X_LOD(_SourceColorTex, sampler_PointClamp, saturate(input.uv + float2(texel.x, 0)), 0).rgb;
                        float3 nU = SAMPLE_TEXTURE2D_X_LOD(_SourceColorTex, sampler_PointClamp, saturate(input.uv + float2(0, texel.y)), 0).rgb;
                        float3 nD = SAMPLE_TEXTURE2D_X_LOD(_SourceColorTex, sampler_PointClamp, saturate(input.uv - float2(0, texel.y)), 0).rgb;
                        float avgNeighborLuma = 0.25 * (Luminance(nL) + Luminance(nR) + Luminance(nU) + Luminance(nD));
                        float centerLuma = Luminance(directColor);
                        float glint = max(0.0, centerLuma - max(0.85, avgNeighborLuma * 1.25));
                        finalHDR += directColor * (glint * specBoost * 0.6);
                    }
                }

                // 6. Add Volumetric Mie Light Shafts (applies to both scene geometry and sky background)
                float3 lightTint = max(float3(0.25, 0.22, 0.18), _MainLightColor.rgb);
                finalHDR += volumetricShafts * _UltraVolumetricColor.rgb * lightTint;

                return half4(max(0.0, finalHDR), 1.0);
            }
            ENDHLSL
        }
    }
}

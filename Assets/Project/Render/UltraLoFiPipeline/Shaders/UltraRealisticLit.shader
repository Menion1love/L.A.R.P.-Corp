Shader "UltraLoFiPipeline/UltraRealisticLit"
{
    Properties
    {
        [MainColor] _BaseColor("Albedo Color", Color) = (0.85, 0.85, 0.85, 1.0)
        [MainTexture] _BaseMap("Albedo Map", 2D) = "white" {}

        [Header(PBR Surface)]
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.65
        _Reflectance("Dielectric F0 Reflectance", Range(0.0, 1.0)) = 0.5
        _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Range(0.0, 2.0)) = 1.0
        _OcclusionStrength("Micro Occlusion", Range(0.0, 1.0)) = 1.0

        [Header(Subsurface Scattering and Translucency)]
        _SubsurfaceStrength("Subsurface Scattering Strength", Range(0.0, 2.0)) = 0.0
        [HDR] _SubsurfaceColor("Subsurface Scatter Color (RGB MFP)", Color) = (1.0, 0.28, 0.12, 1.0)
        _SubsurfaceWrap("Subsurface Wrap Curvature", Range(0.0, 1.0)) = 0.45
        _TranslucencyStrength("Back-Lit Translucency", Range(0.0, 4.0)) = 0.0
        _TranslucencyPower("Translucency Directivity", Range(1.0, 16.0)) = 4.0
        _TranslucencyDistortion("Normal Distortion", Range(0.0, 1.0)) = 0.25
        _Thickness("Local Thickness (0 = Thin, 1 = Thick)", Range(0.0, 1.0)) = 0.25

        [Header(Clearcoat and Anisotropic Microfacet)]
        _ClearCoatMask("Clearcoat Weight", Range(0.0, 1.0)) = 0.0
        _ClearCoatSmoothness("Clearcoat Smoothness", Range(0.0, 1.0)) = 0.9

        [Header(Emission)]
        [HDR] _EmissionColor("Emission Color", Color) = (0.0, 0.0, 0.0, 1.0)
        _EmissionMap("Emission Map", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry"
        }
        LOD 300

        // ------------------------------------------------------------------
        // Pass 0: Forward Lit (PBR Cook-Torrance GGX + Burley + Dual-Lobe SSS + Translucency + Clearcoat)
        // ------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _Metallic;
                float _Smoothness;
                float _Reflectance;
                float _BumpScale;
                float _OcclusionStrength;
                float _SubsurfaceStrength;
                float4 _SubsurfaceColor;
                float _SubsurfaceWrap;
                float _TranslucencyStrength;
                float _TranslucencyPower;
                float _TranslucencyDistortion;
                float _Thickness;
                float _ClearCoatMask;
                float _ClearCoatSmoothness;
                float4 _EmissionColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float4 tangentOS    : TANGENT;
                float2 texcoord     : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float4 tangentWS    : TEXCOORD2;
                float2 uv           : TEXCOORD3;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 4);
                float4 shadowCoord  : TEXCOORD5;
                float  fogFactor    : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings LitPassVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                output.tangentWS = float4(normalInput.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);

                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
                OUTPUT_SH(output.normalWS.xyz, output.vertexSH);

                output.shadowCoord = GetShadowCoord(vertexInput);
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                return output;
            }

            // Physically-based Burley Diffuse + Cook-Torrance GGX + Dual-Lobe Subsurface Scattering & Translucency
            float3 EvaluateUltraRealisticLight(
                Light light,
                float3 N,
                float3 V,
                float3 albedo,
                float3 f0,
                float metallic,
                float perceptualRoughness,
                float roughness,
                float clearCoatMask,
                float clearCoatRoughness)
            {
                float3 L = normalize(light.direction);
                float3 H = SafeNormalize(V + L);

                float NoV = saturate(abs(dot(N, V)) + 1e-5);
                float NoL_raw = dot(N, L);
                float NoL = saturate(NoL_raw);
                float NoH = saturate(dot(N, H));
                float LoH = saturate(dot(L, H));

                float3 lightAtten = light.color * (light.distanceAttenuation * light.shadowAttenuation);

                // 1. Disney / Burley Diffuse BRDF
                float fd90 = 0.5 + 2.0 * LoH * LoH * perceptualRoughness;
                float lightScatter = 1.0 + (fd90 - 1.0) * pow(1.0 - NoL, 5.0);
                float viewScatter  = 1.0 + (fd90 - 1.0) * pow(1.0 - NoV, 5.0);
                float burleyDiffuse = (lightScatter * viewScatter) * INV_PI;

                float3 diffuseColor = albedo * (1.0 - metallic);
                float3 directDiffuse = diffuseColor * burleyDiffuse * NoL * lightAtten;

                // 2. Dual-Lobe Subsurface Wrap Scattering (RGB Wavelength-Dependent Wrap)
                if (_SubsurfaceStrength > 0.001)
                {
                    float wrap = _SubsurfaceWrap;
                    float3 rgbWrap = float3(
                        wrap * 1.15,
                        wrap * 0.70,
                        wrap * 0.40
                    );
                    float3 wrappedNoL = saturate((NoL_raw + rgbWrap) / ((1.0 + rgbWrap) * (1.0 + rgbWrap)));
                    // Smooth shadow penumbra bleed for subsurface materials
                    float sssShadow = lerp(pow(saturate(light.shadowAttenuation), 0.55), light.shadowAttenuation, 0.35);
                    float3 sssLight = light.color * (light.distanceAttenuation * sssShadow);
                    float3 sssContrib = diffuseColor * _SubsurfaceColor.rgb * wrappedNoL * _SubsurfaceStrength * INV_PI;
                    directDiffuse = lerp(directDiffuse, directDiffuse + sssContrib * sssLight, saturate(_SubsurfaceStrength));
                }

                // 3. Back-Lit Transmission / Translucency through thin geometry
                float3 translucencyContrib = 0.0;
                if (_TranslucencyStrength > 0.001)
                {
                    float3 transLightDir = normalize(L + N * _TranslucencyDistortion);
                    float transDot = pow(saturate(dot(V, -transLightDir)), _TranslucencyPower);
                    float thicknessAtten = exp(-_Thickness * 3.5);
                    float transShadow = lerp(0.18, 1.0, light.shadowAttenuation);
                    translucencyContrib = diffuseColor * _SubsurfaceColor.rgb * (transDot * thicknessAtten * _TranslucencyStrength)
                                        * light.color * (light.distanceAttenuation * transShadow);
                }

                // 4. Cook-Torrance GGX Specular BRDF
                float a2 = max(0.0004, roughness * roughness);
                float dDenom = (NoH * a2 - NoH) * NoH + 1.0;
                float D = a2 / (PI * dDenom * dDenom + 1e-7);

                // Height-correlated Smith-GGX Visibility
                float gv = NoL * sqrt(NoV * (NoV - NoV * a2) + a2);
                float gl = NoV * sqrt(NoL * (NoL - NoL * a2) + a2);
                float V_vis = 0.5 / max(gv + gl, 1e-5);

                // Schlick Fresnel
                float3 F = f0 + (1.0 - f0) * pow(1.0 - LoH, 5.0);

                float3 specular = (D * V_vis) * F * NoL * lightAtten;

                // 5. Optional Dielectric Clearcoat Lobe
                if (clearCoatMask > 0.001)
                {
                    float ccA2 = max(0.0004, clearCoatRoughness * clearCoatRoughness);
                    float ccDenom = (NoH * ccA2 - NoH) * NoH + 1.0;
                    float ccD = ccA2 / (PI * ccDenom * ccDenom + 1e-7);
                    float ccV = 0.25 / max(LoH * LoH, 1e-4);
                    float ccF = (0.04 + 0.96 * pow(1.0 - LoH, 5.0)) * clearCoatMask;
                    float3 ccSpec = (ccD * ccV * ccF) * NoL * lightAtten;
                    specular = specular * (1.0 - ccF) + ccSpec;
                    directDiffuse *= (1.0 - ccF * 0.5);
                }

                return directDiffuse + specular + translucencyContrib;
            }

            half4 LitPassFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                float3 albedo = baseSample.rgb;

                // Tangent-space normal mapping
                float3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                float3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                float3x3 TBN = float3x3(input.tangentWS.xyz, bitangentWS, input.normalWS);
                float3 N = NormalizeNormalPerPixel(mul(normalTS, TBN));
                float3 V = GetWorldSpaceNormalizeViewDir(input.positionWS);

                float metallic = saturate(_Metallic);
                float smoothness = saturate(_Smoothness);
                float perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(smoothness);
                float roughness = max(PerceptualRoughnessToRoughness(perceptualRoughness), 0.02);

                float dielectricF0 = 0.16 * _Reflectance * _Reflectance;
                float3 f0 = lerp(float3(dielectricF0, dielectricF0, dielectricF0), albedo, metallic);

                float clearCoatPerceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(_ClearCoatSmoothness);
                float clearCoatRoughness = max(PerceptualRoughnessToRoughness(clearCoatPerceptualRoughness), 0.02);

                // Indirect GI (SH / Lightmap + Reflection Probes)
                float3 bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, N);
                float3 diffuseColor = albedo * (1.0 - metallic);

                float2 normalizedScreenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float NoV = saturate(abs(dot(N, V)) + 1e-5);
                float3 reflectVector = reflect(-V, N);
                float3 indirectSpecular = GlossyEnvironmentReflection(reflectVector, perceptualRoughness, half(1.0));

                // Roughness-aware Fresnel for indirect environment reflections
                float3 maxF = max(float3(smoothness, smoothness, smoothness), f0);
                float3 envFresnel = f0 + (maxF - f0) * pow(1.0 - NoV, 5.0);
                float surfaceReduction = 1.0 / (roughness * roughness + 1.0);

                float3 color = (bakedGI * diffuseColor + indirectSpecular * surfaceReduction * envFresnel) * _OcclusionStrength;

                // Clearcoat indirect environment reflection lobe
                if (_ClearCoatMask > 0.001)
                {
                    float3 coatIndirectSpec = GlossyEnvironmentReflection(reflectVector, clearCoatPerceptualRoughness, half(1.0));
                    float coatFresnel = (0.04 + 0.96 * pow(1.0 - NoV, 5.0)) * _ClearCoatMask;
                    float coatReduction = 1.0 / (clearCoatRoughness * clearCoatRoughness + 1.0);
                    color = color * (1.0 - coatFresnel * 0.5) + coatIndirectSpec * coatReduction * coatFresnel;
                }

                // Main Directional Light
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord, input.positionWS, half4(1, 1, 1, 1));
                color += EvaluateUltraRealisticLight(
                    mainLight, N, V, albedo, f0, metallic,
                    perceptualRoughness, roughness, _ClearCoatMask, clearCoatRoughness);

                // Additional Lights (Forward and Forward+ Cluster loops via LIGHT_LOOP_BEGIN / LIGHT_LOOP_END)
                #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = N;
                inputData.viewDirectionWS = V;
                inputData.normalizedScreenSpaceUV = normalizedScreenUV;

                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light addLight = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                    color += EvaluateUltraRealisticLight(
                        addLight, N, V, albedo, f0, metallic,
                        perceptualRoughness, roughness, _ClearCoatMask, clearCoatRoughness);
                LIGHT_LOOP_END
                #endif

                // Emission
                float3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb;
                color += emission;

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Pass 1: ShadowCaster
        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Pass 2: DepthOnly
        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthOnlyFragment(Varyings input) : SV_Target
            {
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Pass 3: DepthNormals (Populates _CameraDepthTexture & _CameraNormalsTexture with World Normal + Smoothness/SSS weight)
        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _Metallic;
                float _Smoothness;
                float _Reflectance;
                float _BumpScale;
                float _OcclusionStrength;
                float _SubsurfaceStrength;
                float4 _SubsurfaceColor;
                float _SubsurfaceWrap;
                float _TranslucencyStrength;
                float _TranslucencyPower;
                float _TranslucencyDistortion;
                float _Thickness;
                float _ClearCoatMask;
                float _ClearCoatSmoothness;
                float4 _EmissionColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 texcoord   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float4 tangentWS  : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = vertexInput.positionCS;
                output.normalWS = normalInput.normalWS;
                output.tangentWS = float4(normalInput.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                float3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                float3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                float3x3 TBN = float3x3(input.tangentWS.xyz, bitangentWS, input.normalWS);
                float3 normalWS = NormalizeNormalPerPixel(mul(normalTS, TBN));
                // Pack combined surface reflectivity (smoothness weighted by metallic & clearcoat) in alpha for SSR
                float reflectivity = saturate(_Smoothness * lerp(0.45, 1.0, max(_Metallic, _ClearCoatMask)));
                return half4(normalWS, reflectivity);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}

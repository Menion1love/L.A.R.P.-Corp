Shader "Hidden/UltraLoFiPipeline/UltraPostProcessAndUpscale"
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
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        TEXTURE2D_X(_MainSourceTex);
        TEXTURE2D_X(_BloomMipTex);
        TEXTURE2D_X(_BloomLowMipTex);
        TEXTURE2D_X(_LowResFinalTex);
        TEXTURE2D_X_FLOAT(_CameraDepthTexture);

        float4 _UltraLowResSize;       // x: lowW, y: lowH, z: 1/lowW, w: 1/lowH
        float4 _UltraFullScreenSize;   // x: fullW, y: fullH, z: 1/fullW, w: 1/fullH
        float4 _UltraMipTexelSize;     // x: mipW, y: mipH, z: 1/mipW, w: 1/mipH

        // Bloom parameters
        float4 _UltraBloomParams;      // x: threshold, y: softKnee, z: intensity, w: scatter
        float4 _UltraBloomTint;        // rgb: tint, a: anamorphicRatio

        // Chromatic Aberration & DOF
        float4 _UltraCAParams;         // x: enabled, y: intensity, z: radialPower, w: samples
        float4 _UltraDOFParams;        // x: enabled, y: focusDist, z: focusRange, w: bokehRadius

        // Lens, Vignette, Film Grain
        float4 _UltraVignetteParams;   // x: enabled, y: intensity, z: smoothness, w: lensDistortion
        float4 _UltraVignetteColor;
        float4 _UltraGrainParams;      // x: enabled, y: intensity, z: response, w: timeSeed

        // Tone Mapping & Color Grading
        float4 _UltraGradingParams1;   // x: toneMapMode, y: exposureLinear, z: contrast, w: saturation
        float4 _UltraGradingParams2;   // x: wbTemp, y: wbTint, z: gamma, w: debugBypassPost
        float4 _UltraShadowsTint;
        float4 _UltraMidtonesTint;
        float4 _UltraHighlightsTint;

        // Aliasing, Dithering & Upscale parameters
        float4 _UltraAliasingParams;   // x: edgeSharpen, y: ditherMode, z: ditherStrength, w: colorSteps
        float4 _UltraUpscaleParams;    // x: filterMode (0=Point,1=Crisp,2=Bilinear), y: aspectMode, z: pixelGridOverlay, w: unused

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

        // Spectral weight helper for multi-tap Chromatic Aberration
        float3 SpectralWeight(float t)
        {
            float3 w = float3(
                saturate(1.5 - abs(t * 3.0 - 0.5) * 1.6),
                saturate(1.5 - abs(t * 3.0 - 1.5) * 1.6),
                saturate(1.5 - abs(t * 3.0 - 2.5) * 1.6)
            );
            return max(w, 0.001);
        }

        // Ordered Bayer 4x4 matrix in [-0.5, +0.5]
        float Bayer4x4(uint2 p)
        {
            static const float bayer[16] =
            {
                 0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
                12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
                 3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
                15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
            };
            uint idx = (p.y & 3u) * 4u + (p.x & 3u);
            return bayer[idx] - 0.5;
        }

        // Ordered Bayer 8x8 matrix derived recursively from 4x4
        float Bayer8x8(uint2 p)
        {
            float b4 = (Bayer4x4(p >> 1u) + 0.5) * 0.25;
            uint2 sub = p & 1u;
            float b2 = (sub.x ^ sub.y) * 0.5 + sub.x * 0.25;
            return (b2 + b4) - 0.5;
        }

        float InterleavedGradientNoisePost(float2 pixelCoord)
        {
            float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
            return frac(magic.z * frac(dot(pixelCoord, magic.xy))) - 0.5;
        }

        // ACES Filmic Tone Mapping (Stephen Hill RRT+ODT fit)
        float3 ToneMapACESFilmic(float3 x)
        {
            static const float3x3 ACESInputMat =
            {
                {0.59719, 0.35458, 0.04823},
                {0.07600, 0.90834, 0.01566},
                {0.02840, 0.13383, 0.83777}
            };
            static const float3x3 ACESOutputMat =
            {
                { 1.60475, -0.53108, -0.07367},
                {-0.10208,  1.10813, -0.00605},
                {-0.00327, -0.07276,  1.07602}
            };

            x = mul(ACESInputMat, x);
            float3 a = x * (x + 0.0245786) - 0.000090537;
            float3 b = x * (0.983729 * x + 0.4329510) + 0.238081;
            x = a / max(b, 1e-5);
            return saturate(mul(ACESOutputMat, x));
        }

        // AgX Tone Mapping (Troy Sobotka analytical polynomial approximation)
        float3 AgXDefaultContrastApprox(float3 x)
        {
            float3 x2 = x * x;
            float3 x4 = x2 * x2;
            return + 15.5     * x4 * x2
                   - 40.14    * x4 * x
                   + 31.96    * x4
                   - 6.868    * x2 * x
                   + 0.4298   * x2
                   + 0.1191   * x
                   - 0.00232;
        }

        float3 ToneMapAgX(float3 val)
        {
            static const float3x3 agxMat =
            {
                {0.842479062253094,  0.0784335999999992, 0.0792237451477643},
                {0.0423282422610123, 0.878468636469772,  0.0791661274605434},
                {0.0423756549057051, 0.0784336,          0.879142973793104}
            };
            static const float3x3 agxMatInv =
            {
                { 1.19687900512017,   -0.0980208811401368, -0.0990297440797205},
                {-0.0528968517574562,  1.15190312990417,   -0.0989611768448433},
                {-0.0529716355144438, -0.0980434501171241,  1.15107367264116}
            };

            const float minEv = -12.47393f;
            const float maxEv = 4.026069f;

            val = max(val, 1e-6);
            val = mul(agxMat, val);
            val = clamp(log2(val), minEv, maxEv);
            val = (val - minEv) / (maxEv - minEv);
            val = AgXDefaultContrastApprox(val);
            val = mul(agxMatInv, val);
            return saturate(pow(max(0.0, val), 2.2));
        }

        float3 ToneMapNeutral(float3 x)
        {
            const float a = 0.2;
            const float b = 0.29;
            const float c = 0.24;
            const float d = 0.272;
            const float e = 0.02;
            const float f = 0.3;
            return saturate(((x * (a * x + c * b) + d * e) / (x * (a * x + b) + d * f)) - e / f);
        }
        ENDHLSL

        // ==================================================================
        // Pass 0: HDR Bloom Prefilter (Soft-Knee Threshold + Firefly Suppression)
        // ==================================================================
        Pass
        {
            Name "UltraBloomPrefilter"
            HLSLPROGRAM
            #pragma vertex VertFullScreen
            #pragma fragment FragBloomPrefilter

            half4 FragBloomPrefilter(Varyings input) : SV_Target
            {
                float2 texel = _UltraMipTexelSize.zw;
                float3 c0 = SAMPLE_TEXTURE2D_X_LOD(_MainSourceTex, sampler_LinearClamp, input.uv, 0).rgb;
                float3 c1 = SAMPLE_TEXTURE2D_X_LOD(_MainSourceTex, sampler_LinearClamp, input.uv + float2(-texel.x, -texel.y), 0).rgb;
                float3 c2 = SAMPLE_TEXTURE2D_X_LOD(_MainSourceTex, sampler_LinearClamp, input.uv + float2( texel.x, -texel.y), 0).rgb;
                float3 c3 = SAMPLE_TEXTURE2D_X_LOD(_MainSourceTex, sampler_LinearClamp, input.uv + float2(-texel.x,  texel.y), 0).rgb;
                float3 c4 = SAMPLE_TEXTURE2D_X_LOD(_MainSourceTex, sampler_LinearClamp, input.uv + float2( texel.x,  texel.y), 0).rgb;

                // Karis average to prevent sub-pixel fireflies
                float w0 = 1.0 / (1.0 + Luminance(c0));
                float w1 = 1.0 / (1.0 + Luminance(c1));
                float w2 = 1.0 / (1.0 + Luminance(c2));
                float w3 = 1.0 / (1.0 + Luminance(c3));
                float w4 = 1.0 / (1.0 + Luminance(c4));
                float3 color = (c0 * w0 + c1 * w1 + c2 * w2 + c3 * w3 + c4 * w4) / max(1e-4, w0 + w1 + w2 + w3 + w4);
                color = min(color, 16.0);

                float brightness = max(color.r, max(color.g, color.b));
                float threshold = _UltraBloomParams.x;
                float knee = threshold * _UltraBloomParams.y + 1e-4;
                float soft = clamp(brightness - threshold + knee, 0.0, 2.0 * knee);
                soft = (soft * soft) / (4.0 * knee + 1e-4);
                float contribution = max(soft, brightness - threshold) / max(brightness, 1e-4);

                return half4(color * contribution, 1.0);
            }
            ENDHLSL
        }

        // ==================================================================
        // Pass 1: Dual-Kawase Anamorphic Bloom Downsample
        // ==================================================================
        Pass
        {
            Name "UltraBloomDownsample"
            HLSLPROGRAM
            #pragma vertex VertFullScreen
            #pragma fragment FragBloomDownsample

            half4 FragBloomDownsample(Varyings input) : SV_Target
            {
                float anamorphic = _UltraBloomTint.a;
                float2 anamorphicScale = float2(1.0 + max(0.0, anamorphic) * 1.5, 1.0 + max(0.0, -anamorphic) * 1.5);
                float2 texel = _UltraMipTexelSize.zw * anamorphicScale;

                float3 center = SAMPLE_TEXTURE2D_X_LOD(_BloomMipTex, sampler_LinearClamp, input.uv, 0).rgb * 4.0;
                center += SAMPLE_TEXTURE2D_X_LOD(_BloomMipTex, sampler_LinearClamp, input.uv - texel, 0).rgb;
                center += SAMPLE_TEXTURE2D_X_LOD(_BloomMipTex, sampler_LinearClamp, input.uv + texel, 0).rgb;
                center += SAMPLE_TEXTURE2D_X_LOD(_BloomMipTex, sampler_LinearClamp, input.uv + float2(texel.x, -texel.y), 0).rgb;
                center += SAMPLE_TEXTURE2D_X_LOD(_BloomMipTex, sampler_LinearClamp, input.uv + float2(-texel.x, texel.y), 0).rgb;

                return half4(center * 0.125, 1.0);
            }
            ENDHLSL
        }

        // ==================================================================
        // Pass 2: Tent-Filter Bloom Upsample & Scatter Accumulate
        // ==================================================================
        Pass
        {
            Name "UltraBloomUpsample"
            HLSLPROGRAM
            #pragma vertex VertFullScreen
            #pragma fragment FragBloomUpsample

            half4 FragBloomUpsample(Varyings input) : SV_Target
            {
                float2 texel = _UltraMipTexelSize.zw;

                float3 low = SAMPLE_TEXTURE2D_X_LOD(_BloomLowMipTex, sampler_LinearClamp, input.uv, 0).rgb * 4.0;
                low += SAMPLE_TEXTURE2D_X_LOD(_BloomLowMipTex, sampler_LinearClamp, input.uv + float2(-texel.x, 0), 0).rgb * 2.0;
                low += SAMPLE_TEXTURE2D_X_LOD(_BloomLowMipTex, sampler_LinearClamp, input.uv + float2( texel.x, 0), 0).rgb * 2.0;
                low += SAMPLE_TEXTURE2D_X_LOD(_BloomLowMipTex, sampler_LinearClamp, input.uv + float2(0, -texel.y), 0).rgb * 2.0;
                low += SAMPLE_TEXTURE2D_X_LOD(_BloomLowMipTex, sampler_LinearClamp, input.uv + float2(0,  texel.y), 0).rgb * 2.0;
                low += SAMPLE_TEXTURE2D_X_LOD(_BloomLowMipTex, sampler_LinearClamp, input.uv + float2(-texel.x, -texel.y), 0).rgb;
                low += SAMPLE_TEXTURE2D_X_LOD(_BloomLowMipTex, sampler_LinearClamp, input.uv + float2( texel.x, -texel.y), 0).rgb;
                low += SAMPLE_TEXTURE2D_X_LOD(_BloomLowMipTex, sampler_LinearClamp, input.uv + float2(-texel.x,  texel.y), 0).rgb;
                low += SAMPLE_TEXTURE2D_X_LOD(_BloomLowMipTex, sampler_LinearClamp, input.uv + float2( texel.x,  texel.y), 0).rgb;
                low *= (1.0 / 16.0);

                float3 high = SAMPLE_TEXTURE2D_X_LOD(_BloomMipTex, sampler_LinearClamp, input.uv, 0).rgb;
                float scatter = _UltraBloomParams.w;
                return half4(lerp(high, high + low, scatter), 1.0);
            }
            ENDHLSL
        }

        // ==================================================================
        // Pass 3: Low-Res Uber Post-Processing
        // (Chromatic Aberration, DOF, Bloom, Edge Aliasing Sharpen, Color Grading, AgX/ACES, Dithering)
        // ==================================================================
        Pass
        {
            Name "UltraUberPostProcessLowRes"
            HLSLPROGRAM
            #pragma vertex VertFullScreen
            #pragma fragment FragUberPostLowRes

            float2 ApplyLensDistortion(float2 uv, float distortion)
            {
                if (abs(distortion) < 0.001)
                    return uv;
                float2 centered = uv * 2.0 - 1.0;
                float r2 = dot(centered, centered);
                centered *= (1.0 + distortion * r2);
                return saturate(centered * 0.5 + 0.5);
            }

            half4 FragUberPostLowRes(Varyings input) : SV_Target
            {
                // Diagnostic debug views bypass post-processing
                if (_UltraGradingParams2.w > 0.5)
                    return SAMPLE_TEXTURE2D_X_LOD(_MainSourceTex, sampler_PointClamp, input.uv, 0);

                float2 uv = ApplyLensDistortion(input.uv, _UltraVignetteParams.w);
                float2 texel = _UltraLowResSize.zw;

                // 1. Multi-Tap Spectral Chromatic Aberration
                float3 color = 0.0;
                if (_UltraCAParams.x > 0.5 && _UltraCAParams.y > 0.001)
                {
                    float2 centered = uv * 2.0 - 1.0;
                    float radialDist = length(centered);
                    float2 caDir = centered * pow(max(0.001, radialDist), _UltraCAParams.z) * (_UltraCAParams.y * 0.035);

                    int samples = clamp((int)_UltraCAParams.w, 3, 9);
                    float3 weightSum = 0.0;

                    [loop]
                    for (int i = 0; i < samples; i++)
                    {
                        float t = (float)i / (float)(samples - 1);
                        float2 sampleUV = saturate(uv - caDir * (t - 0.5));
                        float3 sCol = (_UltraUpscaleParams.w < 0.5)
                            ? SAMPLE_TEXTURE2D_X_LOD(_MainSourceTex, sampler_LinearClamp, sampleUV, 0).rgb
                            : SAMPLE_TEXTURE2D_X_LOD(_MainSourceTex, sampler_PointClamp, sampleUV, 0).rgb;
                        float3 specW = SpectralWeight(t);
                        color += sCol * specW;
                        weightSum += specW;
                    }
                    color /= max(float3(1e-4, 1e-4, 1e-4), weightSum);
                }
                else
                {
                    color = (_UltraUpscaleParams.w < 0.5)
                        ? SAMPLE_TEXTURE2D_X_LOD(_MainSourceTex, sampler_LinearClamp, uv, 0).rgb
                        : SAMPLE_TEXTURE2D_X_LOD(_MainSourceTex, sampler_PointClamp, uv, 0).rgb;
                }

                // 2. Optional Circle-of-Confusion Depth of Field (Low-Res Bokeh Disc)
                if (_UltraDOFParams.x > 0.5 && _UltraDOFParams.w > 0.01)
                {
                    float rawDepth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, uv, 0).r;
                    float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                    float coc = saturate((abs(eyeDepth - _UltraDOFParams.y) - _UltraDOFParams.z * 0.5) / max(0.1, _UltraDOFParams.z));
                    float radius = coc * _UltraDOFParams.w;

                    if (radius > 0.15)
                    {
                        static const float2 bokehTaps[8] =
                        {
                            float2( 1.0,  0.0), float2(-1.0,  0.0),
                            float2( 0.0,  1.0), float2( 0.0, -1.0),
                            float2( 0.7,  0.7), float2(-0.7,  0.7),
                            float2( 0.7, -0.7), float2(-0.7, -0.7)
                        };
                        float3 dofAccum = color;
                        float dofW = 1.0;
                        [unroll]
                        for (int k = 0; k < 8; k++)
                        {
                            float2 tapUV = saturate(uv + bokehTaps[k] * texel * radius);
                            dofAccum += SAMPLE_TEXTURE2D_X_LOD(_MainSourceTex, sampler_PointClamp, tapUV, 0).rgb;
                            dofW += 1.0;
                        }
                        color = dofAccum / dofW;
                    }
                }

                // 3. Edge Aliasing Sharpening (Unsharp Laplacian in Low-Res Texel Space)
                float edgeSharpen = _UltraAliasingParams.x;
                if (edgeSharpen > 0.001)
                {
                    float3 cL = SAMPLE_TEXTURE2D_X_LOD(_MainSourceTex, sampler_PointClamp, saturate(uv - float2(texel.x, 0)), 0).rgb;
                    float3 cR = SAMPLE_TEXTURE2D_X_LOD(_MainSourceTex, sampler_PointClamp, saturate(uv + float2(texel.x, 0)), 0).rgb;
                    float3 cU = SAMPLE_TEXTURE2D_X_LOD(_MainSourceTex, sampler_PointClamp, saturate(uv + float2(0, texel.y)), 0).rgb;
                    float3 cD = SAMPLE_TEXTURE2D_X_LOD(_MainSourceTex, sampler_PointClamp, saturate(uv - float2(0, texel.y)), 0).rgb;
                    float3 neighbors = 0.25 * (cL + cR + cU + cD);
                    float3 detail = color - neighbors;
                    color = max(0.0, color + detail * edgeSharpen);
                }

                // 4. Additive HDR Bloom Composite
                if (_UltraBloomParams.z > 0.001)
                {
                    float3 bloom = SAMPLE_TEXTURE2D_X_LOD(_BloomMipTex, sampler_LinearClamp, uv, 0).rgb;
                    color += bloom * _UltraBloomTint.rgb * _UltraBloomParams.z;
                }

                // 5. Post-Exposure (EV100) & White Balance (Temp / Tint)
                color *= _UltraGradingParams1.y;

                float wbTemp = _UltraGradingParams2.x;
                float wbTint = _UltraGradingParams2.y;
                float3 wbScale = float3(
                    1.0 + wbTemp * 0.22 + wbTint * 0.08,
                    1.0 - wbTint * 0.14,
                    1.0 - wbTemp * 0.22 + wbTint * 0.08
                );
                color = max(0.0, color * wbScale);

                // 6. Shadows / Midtones / Highlights (Lift / Gamma / Gain) + Contrast & Saturation
                float luma = Luminance(color);
                float shadowWeight = 1.0 - smoothstep(0.0, 0.5, luma);
                float highlightWeight = smoothstep(0.35, 1.2, luma);
                float midtoneWeight = saturate(1.0 - shadowWeight - highlightWeight);

                float3 tintMultiplier = _UltraShadowsTint.rgb * shadowWeight
                                      + _UltraMidtonesTint.rgb * midtoneWeight
                                      + _UltraHighlightsTint.rgb * highlightWeight;
                color *= tintMultiplier;

                // Log-space contrast around 18% middle grey
                float contrast = _UltraGradingParams1.z;
                const float midGrey = 0.18;
                color = max(0.0, midGrey * pow(max(1e-5, color / midGrey), contrast));

                float saturation = _UltraGradingParams1.w;
                float postLuma = Luminance(color);
                color = max(0.0, lerp(float3(postLuma, postLuma, postLuma), color, saturation));

                // 7. Physically-Based Tone Mapping (HDR -> SDR [0, 1])
                int toneMapMode = (int)round(_UltraGradingParams1.x);
                if (toneMapMode == 1) // Reinhard Extended
                {
                    color = color / (1.0 + color);
                }
                else if (toneMapMode == 2) // Neutral
                {
                    color = ToneMapNeutral(color);
                }
                else if (toneMapMode == 3) // ACES Filmic
                {
                    color = ToneMapACESFilmic(color);
                }
                else if (toneMapMode == 4) // AgX
                {
                    color = ToneMapAgX(color);
                }
                else
                {
                    color = saturate(color);
                }

                // Display Gamma adjustment
                float invGamma = 1.0 / max(0.2, _UltraGradingParams2.z);
                if (abs(invGamma - 1.0) > 0.005)
                    color = pow(max(0.0, color), invGamma);

                // 8. Ordered Bayer / IGN Dithering & Parameterized Color Quantization in Low-Res Pixel Space
                uint2 lowPixelCoord = uint2(floor(uv * _UltraLowResSize.xy));
                int ditherMode = (int)round(_UltraAliasingParams.y);
                float ditherStrength = _UltraAliasingParams.z;
                float colorSteps = _UltraAliasingParams.w;

                if (ditherMode > 0 && ditherStrength > 0.001)
                {
                    float ditherVal = 0.0;
                    if (ditherMode == 1)
                        ditherVal = Bayer4x4(lowPixelCoord);
                    else if (ditherMode == 2)
                        ditherVal = Bayer8x8(lowPixelCoord);
                    else
                        ditherVal = InterleavedGradientNoisePost((float2)lowPixelCoord);

                    float stepSpan = 1.0 / max(8.0, colorSteps);
                    color = saturate(color + ditherVal * stepSpan * ditherStrength * 2.5);
                }

                if (colorSteps < 255.0)
                {
                    color = round(color * colorSteps) / colorSteps;
                }

                // 9. Lens Vignette
                if (_UltraVignetteParams.x > 0.5 && _UltraVignetteParams.y > 0.001)
                {
                    float2 vCentered = (uv - 0.5) * 1.55;
                    float vDist = dot(vCentered, vCentered);
                    float vMask = pow(saturate(1.0 - vDist * _UltraVignetteParams.y), 1.0 / max(0.05, _UltraVignetteParams.z));
                    color = lerp(_UltraVignetteColor.rgb, color, vMask);
                }

                // 10. Organic Film Grain (active in Low-Res mode or when explicitly boosted)
                if (_UltraUpscaleParams.w > 0.5 && _UltraGrainParams.x > 0.5 && _UltraGrainParams.y > 0.001)
                {
                    float2 grainCoord = (float2)lowPixelCoord + float2(_UltraGrainParams.w * 17.13, _UltraGrainParams.w * 31.71);
                    float grain = InterleavedGradientNoisePost(grainCoord);
                    float lumaResp = lerp(1.0, 1.0 - saturate(Luminance(color)), _UltraGrainParams.z);
                    color = saturate(color + grain * _UltraGrainParams.y * lumaResp);
                }

                return half4(saturate(color), 1.0);
            }
            ENDHLSL
        }

        // ==================================================================
        // Pass 4: Parameterized Low-Res to Full-Screen Stretch / Upscale Pass
        // ==================================================================
        Pass
        {
            Name "UltraFullscreenStretchUpscale"
            HLSLPROGRAM
            #pragma vertex VertFullScreen
            #pragma fragment FragFullscreenStretch

            half4 FragFullscreenStretch(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                bool lowResEnabled = _UltraUpscaleParams.w > 0.5;
                if (!lowResEnabled)
                {
                    return SAMPLE_TEXTURE2D_X_LOD(_LowResFinalTex, sampler_LinearClamp, saturate(uv), 0);
                }

                int filterMode = (int)round(_UltraUpscaleParams.x);
                int aspectMode = (int)round(_UltraUpscaleParams.y);
                float pixelGridOverlay = _UltraUpscaleParams.z;

                float2 lowSize = _UltraLowResSize.xy;
                float2 fullSize = _UltraFullScreenSize.xy;

                // Aspect ratio handling (StretchFullScreen, PreserveAspectLetterbox, IntegerPixelScale)
                if (aspectMode == 1) // PreserveAspectLetterbox
                {
                    float targetAspect = lowSize.x / max(1.0, lowSize.y);
                    float screenAspect = fullSize.x / max(1.0, fullSize.y);
                    float2 scale = float2(1.0, 1.0);
                    if (screenAspect > targetAspect)
                        scale.x = screenAspect / targetAspect;
                    else
                        scale.y = targetAspect / screenAspect;

                    uv = (uv - 0.5) * scale + 0.5;
                    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
                        return half4(0.0, 0.0, 0.0, 1.0);
                }
                else if (aspectMode == 2) // IntegerPixelScale
                {
                    float intScale = max(1.0, floor(min(fullSize.x / lowSize.x, fullSize.y / lowSize.y)));
                    float2 occupiedSize = lowSize * intScale;
                    float2 ratio = fullSize / max(float2(1.0, 1.0), occupiedSize);
                    uv = (uv - 0.5) * ratio + 0.5;
                    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
                        return half4(0.0, 0.0, 0.0, 1.0);
                }

                float3 finalColor = 0.0;

                if (filterMode == 0) // PointNearest (Pure crisp aliasing!)
                {
                    float2 nearestUV = (floor(uv * lowSize) + 0.5) / lowSize;
                    finalColor = SAMPLE_TEXTURE2D_X_LOD(_LowResFinalTex, sampler_PointClamp, saturate(nearestUV), 0).rgb;
                }
                else if (filterMode == 1) // CrispPixelArt (Sub-pixel analytical anti-shimmer while keeping flat pixels)
                {
                    float2 texelCoord = uv * lowSize;
                    float2 baseTexel = floor(texelCoord - 0.5) + 0.5;
                    float2 fracTexel = texelCoord - baseTexel;
                    float2 scaleFactor = max(float2(1.0, 1.0), fullSize / lowSize);
                    float2 smoothFrac = saturate((fracTexel - 0.5) * scaleFactor + 0.5);
                    float2 crispUV = (baseTexel + smoothFrac) / lowSize;
                    finalColor = SAMPLE_TEXTURE2D_X_LOD(_LowResFinalTex, sampler_LinearClamp, saturate(crispUV), 0).rgb;
                }
                else // Bilinear
                {
                    finalColor = SAMPLE_TEXTURE2D_X_LOD(_LowResFinalTex, sampler_LinearClamp, saturate(uv), 0).rgb;
                }

                // Optional subtle pixel grid boundary definition after stretching low-res to full screen
                if (pixelGridOverlay > 0.001 && fullSize.y > lowSize.y * 1.5)
                {
                    float2 cellPhase = frac(uv * lowSize);
                    float2 borderDist = min(cellPhase, 1.0 - cellPhase) * (fullSize / lowSize);
                    float gridLine = saturate(min(borderDist.x, borderDist.y));
                    float gridMask = lerp(1.0 - pixelGridOverlay, 1.0, smoothstep(0.0, 0.75, gridLine));
                    finalColor *= gridMask;
                }

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // ==================================================================
        // Pass 5: Pure Point-Sampled Low-Res Buffer Capture / Copy
        // ==================================================================
        Pass
        {
            Name "UltraPointCapture"
            HLSLPROGRAM
            #pragma vertex VertFullScreen
            #pragma fragment FragPointCapture

            half4 FragPointCapture(Varyings input) : SV_Target
            {
                if (_UltraUpscaleParams.w < 0.5)
                    return SAMPLE_TEXTURE2D_X_LOD(_LowResFinalTex, sampler_LinearClamp, saturate(input.uv), 0);

                float2 lowSize = max(float2(16.0, 16.0), _UltraLowResSize.xy);
                float2 nearestUV = (floor(input.uv * lowSize) + 0.5) / lowSize;
                return SAMPLE_TEXTURE2D_X_LOD(_LowResFinalTex, sampler_PointClamp, saturate(nearestUV), 0);
            }
            ENDHLSL
        }
    }
}

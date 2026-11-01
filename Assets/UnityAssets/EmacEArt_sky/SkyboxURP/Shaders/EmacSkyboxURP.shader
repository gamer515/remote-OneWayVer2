Shader "EmacEArt/SkyboxURP"
{
    Properties
    {
        [Header(Zenith and Horizon)]
        _ZenithColor      ("Zenith Color",       Color) = (0.05, 0.08, 0.22, 1)
        _HorizonColor     ("Horizon Color",      Color) = (0.38, 0.55, 0.82, 1)
        _GroundColor      ("Ground Color",       Color) = (0.12, 0.10, 0.08, 1)
        _HorizonSharpness ("Horizon Sharpness",  Range(0.5, 16.0)) = 4.0
        _HorizonOffset    ("Horizon Offset",     Range(-0.5, 0.5)) = 0.0

        [Header(Sun)]
        // _SunFollowLight is read by the editor only - not used as a keyword
        _SunFollowLight   ("Sun Follows Main Light", Float) = 1
        _SunColor         ("Sun Color",          Color) = (1.0, 1.0, 1.0, 1)
        _SunGlowColor     ("Sun Glow Color",     Color) = (1.0, 1.0, 1.0, 1)
        _SunSize          ("Sun Size",           Range(0.0005, 0.05)) = 0.003
        _SunGlowSize      ("Sun Glow Size",      Range(0.01, 1.0)) = 0.132
        _SunGlowFalloff   ("Sun Glow Falloff",   Range(1.0, 16.0)) = 16.0
        _SunDirection     ("Sun Direction (manual)", Vector) = (0.3, 0.6, 0.5, 0)

        [Header(Sky Gradient)]
        _SkyGradColor  ("Gradient Color",   Color)         = (1.0, 0.45, 0.10, 1)
        _SkyGradAngle  ("Direction (deg)",  Range(0, 360)) = 0.0
        _SkyGradSpread ("Spread",           Range(0.01, 1.0)) = 0.5
        _SkyGradStr    ("Strength",         Range(0.0, 1.0)) = 0.0

        [Header(Atmosphere)]
        _AtmosphereColor    ("Atmosphere Tint",    Color) = (0.20, 0.35, 0.60, 1)
        _AtmosphereStrength ("Atmosphere Strength",Range(0.0, 1.5)) = 0.55
        _AtmosphereFalloff  ("Atmosphere Falloff", Range(1.0, 10.0)) = 2.5

        [Header(Color Grading)]
        _Exposure    ("Exposure",         Range(0.1, 4.0)) = 1.0
        _Contrast    ("Contrast",         Range(0.5, 2.0)) = 1.05
        _Saturation  ("Saturation",       Range(0.0, 2.5)) = 1.1
        _Lift        ("Lift (Shadows)",   Color) = (0,0,0,0)
        _Gamma       ("Gamma (Midtones)", Color) = (0.5,0.5,0.5,0)
        _Gain        ("Gain (Highlights)",Color) = (1,1,1,0)

        [Header(Stars)]
        [Toggle] _StarsEnabled  ("Stars Enabled",   Float) = 1
        _StarsTex       ("Stars Cubemap",    Cube) = "black" {}
        _StarsIntensity ("Stars Intensity",  Range(0.0, 4.0)) = 1.0
        _StarsThreshold ("Stars Threshold",  Range(0.0, 1.0)) = 0.72
        _StarsSharpness ("Stars Sharpness",  Range(1.0, 32.0)) = 8.0

        [Header(Clouds)]
        [Toggle] _CloudsEnabled   ("Clouds Enabled",   Float) = 0
        _CloudColor       ("Cloud Color",      Color) = (1.0, 1.0, 1.0, 1)
        _CloudShadowColor ("Cloud Shadow",     Color) = (0.72, 0.78, 0.88, 1)
        _CloudCoverage    ("Coverage",         Range(0.0, 1.0))  = 0.5
        _CloudSoftness    ("Softness",         Range(0.001, 0.5)) = 0.15
        _CloudBands       ("Bands (posterize)",Range(1.0, 8.0))  = 1.0
        _CloudDensity     ("Density",           Range(0.0, 1.0))  = 1.0
        _CloudStretch     ("Stretch",          Range(0.0, 0.97)) = 0.3
        _CloudSwirl       ("Swirl",            Range(0.0, 1.0))  = 0.0
        // 0=Standard 1=Feathered 2=Round 3=Diagonal 4=Cotton - editor shows popup
        _CloudStyle       ("Style",            Range(0.0, 4.0))  = 0.0
        _CloudScale       ("Scale",            Range(0.5, 8.0))  = 3.0
        _CloudHeight      ("Min Elevation",    Range(0.0, 0.5))  = 0.05
        _CloudSpeed       ("Speed",            Range(0.0, 1.0))  = 0.03
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature _STARSENABLED_ON
            #pragma shader_feature _CLOUDSENABLED_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ── Properties ──────────────────────────────────────────────────
            CBUFFER_START(UnityPerMaterial)
                half4  _ZenithColor;
                half4  _HorizonColor;
                half4  _GroundColor;
                half   _HorizonSharpness;
                half   _HorizonOffset;

                half4  _SunColor;
                half4  _SunGlowColor;
                half   _SunSize;
                half   _SunGlowSize;
                half   _SunGlowFalloff;
                float4 _SunDirection;

                half4  _AtmosphereColor;
                half   _AtmosphereStrength;
                half   _AtmosphereFalloff;

                half   _Exposure;
                half   _Contrast;
                half   _Saturation;
                half4  _Lift;
                half4  _Gamma;
                half4  _Gain;

                half   _StarsIntensity;
                half   _StarsThreshold;
                half   _StarsSharpness;

                half4  _CloudColor;
                half4  _CloudShadowColor;
                half   _CloudCoverage;
                half   _CloudSoftness;
                half   _CloudBands;
                half   _CloudDensity;
                half   _CloudStretch;
                half   _CloudSwirl;
                half   _CloudStyle;
                half   _CloudScale;
                half   _CloudHeight;
                half   _CloudSpeed;

                half4  _SkyGradColor;
                half   _SkyGradAngle;
                half   _SkyGradSpread;
                half   _SkyGradStr;
            CBUFFER_END

            #ifdef _STARSENABLED_ON
                TEXTURECUBE(_StarsTex);
                SAMPLER(sampler_StarsTex);
            #endif

            // ── Vertex ──────────────────────────────────────────────────────
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dir        = IN.positionOS.xyz;
                return OUT;
            }

            // ── Helpers ─────────────────────────────────────────────────────
            half3 ApplyContrast(half3 col, half contrast)
            {
                // Clamp: dark colours + high contrast would produce sub-zero → pure black
                return max((col - 0.5h) * contrast + 0.5h, 0.0h);
            }

            half3 ApplySaturation(half3 col, half sat)
            {
                half lum = dot(col, half3(0.2126h, 0.7152h, 0.0722h));
                // Clamp: sat > 1 on near-black channels produces sub-zero values
                return max(lerp(lum.rrr, col, sat), 0.0h);
            }

            // ASC CDL: lift/gamma/gain per channel
            half3 ApplyLiftGammaGain(half3 col, half3 lift, half3 gamma, half3 gain)
            {
                col = col * gain + lift;
                col = sign(col) * pow(abs(col), 1.0h / max(gamma, 0.001h));
                return col;
            }

            // Neutral Gamma = (0.5,0.5,0.5) → remapped to 1.0 (no change)
            half3 GammaRemapped(half4 g) { return max(g.rgb * 2.0h, 0.001h); }

            // ── Cloud noise (value noise + 3-octave FBM) ─────────────────────
            float CloudHash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float CloudNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(CloudHash(i),               CloudHash(i + float2(1,0)), u.x),
                    lerp(CloudHash(i + float2(0,1)), CloudHash(i + float2(1,1)), u.x),
                    u.y);
            }

            float CloudFBM(float2 p)
            {
                float v = CloudNoise(p)                            * 0.5000;
                v      += CloudNoise(p * 2.1 + float2(5.2, 1.3))  * 0.2500;
                v      += CloudNoise(p * 4.3 + float2(9.7, 2.8))  * 0.1250;
                v      += CloudNoise(p * 8.7 + float2(3.1, 7.4))  * 0.0625;
                return v * (1.0 / 0.9375);
            }

            // ── Style 1: Feathered - ridged FBM (thin ridges, like cirrus clouds) ─
            float CloudFBMRidged(float2 p)
            {
                // Inverts noise around 0.5: ridges appear where noise = 0.5
                float v  = (1.0 - abs(CloudNoise(p)                           * 2.0 - 1.0)) * 0.5000;
                v       += (1.0 - abs(CloudNoise(p * 2.1 + float2(5.2, 1.3)) * 2.0 - 1.0)) * 0.2500;
                v       += (1.0 - abs(CloudNoise(p * 4.3 + float2(9.7, 2.8)) * 2.0 - 1.0)) * 0.1250;
                v       += (1.0 - abs(CloudNoise(p * 8.7 + float2(3.1, 7.4)) * 2.0 - 1.0)) * 0.0625;
                return saturate(v * (1.0 / 0.9375));
            }

            // ── Style 2: Voronoi - round, separated cells ──
            float2 VoronoiCenter(float2 cell)
            {
                return cell + float2(
                    CloudHash(cell),
                    CloudHash(cell + float2(5.3, 2.1)));
            }

            float CloudVoronoiEuclid(float2 p)  // round blobs - Euclidean distance
            {
                float2 i = floor(p);
                float minD = 10.0;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    float2 c = VoronoiCenter(i + float2(dx, dy));
                    minD = min(minD, length(p - c));
                }
                return 1.0 - saturate(minD * 1.35);
            }

            // ── Style 4: Cotton - same cell centres as style 2, joined with a smooth
            // minimum instead of a plain one. The cell walls disappear and neighbouring
            // cells grow into one connected, lobed mass, the way heaped cumulus piles up.
            // A slow noise pushes the falloff in and out so the lobes never come out as
            // clean circles - that is what keeps this apart from style 2, where every
            // blob stands on its own with a seam around it.
            float CloudCotton(float2 p)
            {
                float2 i = floor(p);
                const float K = 5.5;   // higher = cells stay apart, lower = everything melts
                float acc = 0.0;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    float2 c = VoronoiCenter(i + float2(dx, dy));
                    acc += exp(-K * length(p - c));
                }
                // Exponential smooth minimum of the cell distances
                float dSmooth = -log(max(acc, 1e-5)) / K;
                // Break the circles: the edge of the mass wanders with a low-frequency noise
                dSmooth -= (CloudNoise(p * 1.6) - 0.5) * 0.42;
                dSmooth -= (CloudNoise(p * 3.7 + float2(6.1, 2.4)) - 0.5) * 0.18;
                return 1.0 - saturate(dSmooth * 1.55);
            }

            // ── Fragment ─────────────────────────────────────────────────────
            half4 Frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dir);
                half   up  = dir.y + _HorizonOffset;

                // ── Sky gradient ─────────────────────────────────────────
                half skyT = saturate(pow(saturate(up), 1.0h / _HorizonSharpness));

                // Ground uses raw dir.y - offset shifts the sky gradient only,
                // not the physical hemisphere boundary. Prevents ground from
                // bleeding into the upper sky at extreme negative offsets.
                half groundT = saturate(pow(saturate(-dir.y), 2.0h));

                half3 skyCol = lerp(_HorizonColor.rgb, _ZenithColor.rgb, skyT);
                skyCol       = lerp(skyCol, _GroundColor.rgb, groundT);

                // ── Azimuthal sky gradient (side-to-side compass tint) ───
                {
                    float  ang   = (float)_SkyGradAngle * 0.01745329252; // degrees to radians
                    float2 gradH = float2(sin(ang), cos(ang));            // compass direction vector
                    float2 dirH  = float2(dir.x, dir.z);
                    float  dLen  = length(dirH);
                    float  azDot = dLen > 0.001 ? dot(dirH / dLen, gradH) : 0.0; // -1..1
                    // Linear ramp across the full sky - always visible when Strength > 0
                    half   tAz  = saturate((half)(azDot * 0.5 + 0.5));
                    // Spread controls falloff sharpness: 1=soft full-sky, 0.1=hard half-sphere
                    tAz = pow(tAz, max(1.0h / _SkyGradSpread, 0.01h));
                    skyCol = lerp(skyCol, _SkyGradColor.rgb, tAz * _SkyGradStr * (1.0h - groundT));
                }

                // ── Aerial perspective - atmospheric haze at horizon ──────
                half horizonFactor = pow(saturate(1.0h - abs(up)), _AtmosphereFalloff);
                skyCol += _AtmosphereColor.rgb * horizonFactor * _AtmosphereStrength * (1.0h - groundT);

                // ── Sun ──────────────────────────────────────────────────
                // _SunDirection is kept updated by the editor when "Sun Follows Main Light" is ON
                float3 sunDir = normalize(_SunDirection.xyz);
                half   cosA   = dot(dir, sunDir);

                // Hard disc
                half sunDisc = smoothstep(_SunSize + 0.0002h, _SunSize - 0.0002h, 1.0h - cosA);

                // Glow corona (additive)
                half glowT   = saturate((cosA - (1.0h - _SunGlowSize)) / _SunGlowSize);
                half sunGlow = pow(glowT, _SunGlowFalloff);

                skyCol += _SunGlowColor.rgb * sunGlow * (1.0h - groundT);
                skyCol  = lerp(skyCol, _SunColor.rgb, sunDisc * (1.0h - groundT));

                // ── Stars ─────────────────────────────────────────────────
                #ifdef _STARSENABLED_ON
                {
                    half4 starSample = SAMPLE_TEXTURECUBE(_StarsTex, sampler_StarsTex, dir);
                    half  starLum    = dot(starSample.rgb, half3(0.299h, 0.587h, 0.114h));
                    half  starMask   = pow(saturate((starLum - _StarsThreshold) / (1.0h - _StarsThreshold)), _StarsSharpness);
                    half  nightFade  = saturate(1.0h - sunGlow * 4.0h) * (1.0h - groundT);
                    skyCol          += starSample.rgb * starMask * _StarsIntensity * nightFade;
                }
                #endif

                // ── Clouds ────────────────────────────────────────────────
                #ifdef _CLOUDSENABLED_ON
                {
                    // Fade in above minimum elevation, disappear toward ground
                    half elevMask = saturate((dir.y - _CloudHeight) / max(_CloudSoftness, 0.01h))
                                  * (1.0h - groundT);

                    // Perspective projection to horizontal cloud plane + time scroll
                    float2 cloudUV = (dir.xz / max(dir.y + 0.1, 0.1)) * (float)_CloudScale;
                    cloudUV.x += _Time.y * (float)_CloudSpeed;

                    // ── Domain warp ─────────────────────────────────────────
                    float stretch = (float)_CloudStretch;
                    float swirl   = (float)_CloudSwirl;

                    if (stretch > 0.01 || swirl > 0.01)
                    {
                        // Stretch - non-linear (squared): slow start, dramatic at high values
                        float sa = stretch * stretch;
                        float wx = CloudNoise(cloudUV * 0.28 + float2(0.0, 0.0));
                        float wy = CloudNoise(cloudUV * 0.28 + float2(4.7, 2.1));
                        cloudUV.x += (wx - 0.5) * sa * 7.0;
                        cloudUV.y += (wy - 0.5) * sa * 1.4;

                        // Swirl - second-level domain warp with a perpendicular component.
                        // float2(-d.y, d.x) = 90-degree rotation of d → organic swirling.
                        if (swirl > 0.01)
                        {
                            float sx = CloudNoise(cloudUV * 0.32 + float2(1.7, 9.2));
                            float sy = CloudNoise(cloudUV * 0.32 + float2(8.3, 2.8));
                            float2 d  = float2(sx - 0.5, sy - 0.5);
                            cloudUV  += d                      * swirl * 3.2   // linear component
                                      + float2(-d.y, d.x)     * swirl * 2.4;  // perpendicular = rotation
                        }
                    }

                    // ── Pattern style selector ───────────────────────────────
                    int cStyle = (int)round((float)_CloudStyle);
                    float n;
                    if      (cStyle == 1) n = CloudFBMRidged(cloudUV);          // feathered / cirrus
                    else if (cStyle == 2) n = CloudVoronoiEuclid(cloudUV);      // round blobs
                    else if (cStyle == 3) {                                      // diagonal 45 deg
                        float2 rUV = float2(cloudUV.x * 0.7071 - cloudUV.y * 0.7071,
                                            cloudUV.x * 0.7071 + cloudUV.y * 0.7071);
                        n = CloudFBM(rUV);
                    }
                    else if (cStyle == 4) n = CloudCotton(cloudUV);              // merged, lobed masses
                    else                  n = CloudFBM(cloudUV);                 // standard

                    // ── Posterization: Bands > 1 → cel-shaded stepped look ──
                    float bands = max((float)_CloudBands, 1.0);
                    float nQ = (bands > 1.5)
                        ? ceil(n * bands) / bands  // snap to N discrete levels
                        : n;                       // bands = 1 → smooth original

                    // How far this pixel is past the coverage threshold. Continuous in
                    // coverage, so dragging the slider creeps the cloud edge outward
                    // instead of making it jump.
                    float d = n - (1.0 - (float)_CloudCoverage);
                    // The steps are put on THIS distance, not on the raw noise. Quantizing
                    // the noise tied the steps to fixed noise levels, so the coverage
                    // slider did nothing until it crossed one and then a whole band
                    // appeared at once. Quantizing the distance keeps the first step glued
                    // to the threshold: the pixel just inside the cloud jumps straight to a
                    // full step - the hard cut-out edge - while the edge itself still moves
                    // smoothly with coverage.
                    float dQ = (bands > 1.5) ? ceil(d * bands) / bands : d;

                    half t = saturate((half)dQ / max(_CloudSoftness, 0.005h));
                    half cloudAlpha = t * t * (3.0h - 2.0h * t) * elevMask * _CloudDensity;

                    // Color driven by band level: low band = shadow, high band = lit
                    half bandLevel = saturate(((half)nQ - (1.0h - _CloudCoverage)) * (half)bands);
                    half3 cloudCol = lerp(_CloudShadowColor.rgb, _CloudColor.rgb, bandLevel);

                    skyCol = lerp(skyCol, cloudCol, cloudAlpha);
                }
                #endif

                // ── Color Grading ──────────────────────────────────────────
                skyCol *= _Exposure;
                skyCol  = ApplyContrast(skyCol, _Contrast);
                skyCol  = ApplySaturation(skyCol, _Saturation);
                skyCol  = ApplyLiftGammaGain(
                    skyCol,
                    _Lift.rgb,
                    GammaRemapped(_Gamma),
                    _Gain.rgb
                );
                skyCol = max(skyCol, 0.0h);

                return half4(skyCol, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
    CustomEditor "EmacEArt.SkyboxURP.EmacSkyboxURPEditor"
}

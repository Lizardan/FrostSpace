// Полярное сияние для вида «каньон» главного меню.
//
// Меш — тонкая плита (cube), натянутая поперёк неба; шейдеру важны только
// локальные координаты грани, поэтому UV не используются: положение внутри
// плиты считается из positionOS. Это делает шейдер нечувствительным к тому,
// как именно нарезан меш и какие у него UV.
//
// Полосы — вертикальные «занавеси»: сумма двух синусов по X с медленным
// сносом по времени плюс фрактальный шум, который рвёт полосы на волокна.
// Ниспадающий край гасится по высоте, поэтому сияние висит в небе, а не
// заливает горизонт.
//
// Аддитивный блендинг и ZWrite Off: сияние светится поверх неба, но
// перекрывается геометрией каньона — стены остаются силуэтом.
// Cull Back: плита тонкая, и без отсечения задней грани она складывалась бы
// сама с собой, давая двойную яркость вместо одной.
Shader "FrostSpace/Menu/Aurora"
{
    Properties
    {
        [HDR] _AuroraColor("Aurora Color", Color) = (0.302, 0.902, 1, 1)
        [HDR] _AuroraDeep("Aurora Deep Color", Color) = (0.09, 0.42, 0.52, 1)
        _Intensity("Intensity", Range(0, 3)) = 0.85
        _Alpha("Alpha", Range(0, 1)) = 0.75
        _Ribbons("Ribbons Across", Float) = 5
        _BandPower("Band Power", Range(0.5, 8)) = 2.4
        _Speed("Drift Speed", Float) = 0.35
        _NoiseScale("Noise Scale", Float) = 5
        _NoiseStrength("Noise Strength", Range(0, 1)) = 0.65
        _Bottom("Curtain Bottom", Range(0, 1)) = 0.22
        _Top("Curtain Top", Range(0, 1)) = 0.85
        _CurtainPower("Curtain Power", Range(0.25, 4)) = 1.4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+40"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Aurora"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _AuroraColor;
                half4 _AuroraDeep;
                half _Intensity;
                half _Alpha;
                half _Ribbons;
                half _BandPower;
                half _Speed;
                half _NoiseScale;
                half _NoiseStrength;
                half _Bottom;
                half _Top;
                half _CurtainPower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 local : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Грань куба лежит в ±0.5 по X и Y — приводим к 0..1.
                output.local = input.positionOS.xy + 0.5;
                return output;
            }

            half Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            half ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                half a = Hash21(cell);
                half b = Hash21(cell + float2(1.0, 0.0));
                half c = Hash21(cell + float2(0.0, 1.0));
                half d = Hash21(cell + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half Fbm(float2 p)
            {
                half sum = 0.0h;
                half amp = 0.5h;
                for (int i = 0; i < 3; i++)
                {
                    sum += ValueNoise(p) * amp;
                    p *= 2.03;
                    amp *= 0.5h;
                }
                return saturate(sum);
            }

            half4 Frag(Varyings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half t = _Time.y * _Speed;
                half x = input.local.x;
                half y = input.local.y;

                // Занавес: две волны разной частоты, снесённые по времени,
                // плюс шум — он и делает волокнистую структуру.
                half wave = sin((x * _Ribbons + t) * 6.28318h) * 0.5h + 0.5h;
                half wave2 = sin((x * _Ribbons * 2.37h - t * 1.6h + y * 1.4h) * 6.28318h) * 0.5h + 0.5h;
                half bands = saturate(pow(wave * 0.65h + wave2 * 0.35h, _BandPower));

                half noise = Fbm(float2(x * _NoiseScale + t * 0.6, y * _NoiseScale * 0.7 - t * 0.2));
                bands *= lerp(1.0h, noise * 1.6h, _NoiseStrength);

                // Высотное окно: полоса висит между _Bottom и _Top,
                // книзу гаснет плавно, кверху обрывается резче — как настоящий занавес.
                half top = saturate((y - _Bottom) / max(_Top - _Bottom, 0.001h));
                half fadeTop = 1.0h - smoothstep(0.55h, 1.0h, top);
                half fadeBottom = smoothstep(0.0h, 0.35h, top);
                half curtain = pow(saturate(top), _CurtainPower) * fadeTop * fadeBottom;

                // Медленное дыхание яркости: сияние не должно быть ровной заливкой.
                half breathe = 0.86h + 0.14h * sin(_Time.y * 0.7h + x * 4.0h);

                half glow = bands * curtain * breathe;
                half3 color = lerp(_AuroraDeep.rgb, _AuroraColor.rgb, saturate(bands * 1.35h));

                return half4(color * glow * _Intensity, saturate(glow) * _Alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

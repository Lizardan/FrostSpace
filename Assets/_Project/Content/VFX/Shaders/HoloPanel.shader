// Голографическая панель: полупрозрачный циановый экран с рамкой, уголками,
// бегущими строками и скан-полосой.
//
// Всё движение процедурное и живёт на _Time: текстур нет, дополнительных
// проходов нет. Фазы (_ScanPhase, _LinePhase) приходят из HoloPanel через
// MaterialPropertyBlock, чтобы один материал обслуживал панель и не ломал
// SRP-батчинг.
//
// Bloom в проекте нет (пост-обработка выключена), поэтому яркость держится
// в пределах 1,2: HDR-значения просто клипнулись бы в белый. Мягкое свечение
// даётся слоями внутри шейдера — рамка и скан ярче заливки.
//
// Очередь Transparent+70: 40/50/60 заняты сканом, силуэтом и вырезом.
Shader "FrostSpace/VFX/HoloPanel"
{
    Properties
    {
        [HDR] _HoloColor("Holo Color", Color) = (0.302, 0.902, 1, 1)
        [HDR] _HoloDeep("Holo Deep", Color) = (0.05, 0.22, 0.30, 1)
        _Alpha("Alpha", Range(0, 1)) = 0.55
        _Intensity("Intensity", Range(0, 2)) = 1.2
        _FrameWidth("Frame Width", Range(0, 0.1)) = 0.012
        _CornerLength("Corner Length", Range(0.01, 0.4)) = 0.14
        _LineCount("Line Count", Float) = 9
        _LineSpeed("Line Speed", Float) = 0.35
        _ScanSpeed("Scan Speed", Float) = 0.22
        _ScanWidth("Scan Width", Range(0.01, 0.5)) = 0.06
        _ScanPhase("Scan Phase", Range(0, 1)) = 0
        _LinePhase("Line Phase", Range(0, 1)) = 0
        _Seed("Seed", Float) = 0
        _Breath("Breath", Range(0, 0.3)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+70"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Holo"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _HoloColor;
                half4 _HoloDeep;
                half _Alpha;
                half _Intensity;
                half _FrameWidth;
                half _CornerLength;
                half _LineCount;
                half _LineSpeed;
                half _ScanSpeed;
                half _ScanWidth;
                half _ScanPhase;
                half _LinePhase;
                half _Seed;
                half _Breath;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Квад в локальных осях от −0,5 до 0,5: UV без развёртки.
                output.uv = input.positionOS.xy + 0.5;
                return output;
            }

            half4 Frag(Varyings input) : SV_TARGET
            {
                float2 uv = saturate(input.uv);
                float2 edgeDistance = min(uv, 1.0 - uv);
                float edge = min(edgeDistance.x, edgeDistance.y);

                // Рамка: мягкая, шире одного пикселя — MSAA альфу не сглаживает.
                half frame = 1.0h - smoothstep(_FrameWidth, _FrameWidth + 0.006, edge);

                // Уголки: короткие L-образные штрихи по четырём углам.
                half corner = step(edgeDistance.x, _CornerLength)
                            * step(edgeDistance.y, _CornerLength)
                            * (step(edgeDistance.x, _FrameWidth * 2.0)
                             + step(edgeDistance.y, _FrameWidth * 2.0));

                // Бегущие строки: у каждой свой старт и длина, движение по X.
                float row = floor(uv.y * _LineCount);
                float rowT = frac(uv.y * _LineCount);
                float rowMask = step(0.35, rowT) * step(rowT, 0.72);
                float rnd = frac(sin(row * 127.1 + _Seed) * 43758.5453);
                float x = frac(uv.x - _LinePhase - rnd);
                float len = lerp(0.25, 0.85,
                                 frac(sin(row * 311.7 + _Seed) * 43758.5453));
                float bar = smoothstep(0.0, 0.02, x) * smoothstep(len, len - 0.02, x)
                          * smoothstep(0.0, 0.08, uv.x)
                          * smoothstep(1.0, 0.92, uv.x);

                // Скан-полоса и дыхание.
                float scan = smoothstep(_ScanWidth, 0.0,
                                        abs(frac(uv.y - _ScanPhase) - 0.5));
                half breath = 1.0h + _Breath * sin(_LinePhase * 6.28318);

                half alpha = saturate(frame * 1.6h + corner * 1.2h
                                    + rowMask * bar * 0.55h + scan * 0.9h)
                           * _Alpha * breath;
                half3 rgb = lerp(_HoloDeep.rgb, _HoloColor.rgb,
                                 saturate(frame + scan + bar * 0.6));
                return half4(rgb * _Intensity, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

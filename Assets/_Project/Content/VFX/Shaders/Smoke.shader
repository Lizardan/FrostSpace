// Дым: мягкий процедурный клубок без единой текстуры.
//
// Нужен именно свой шейдер, а не стандартный партикл-материал: дым обязан быть
// мягким по всей кромке (никаких квадратных краёв на белом снегу), завихряться по
// жизни частицы и оставаться дешёвым — в WebGL это прозрачный слой поверх метели,
// и каждый лишний проход в кадре стоит дорого.
//
// Форма считается из UV квада: радиальная кромка плюс два октавных узла шума,
// которые сдвигают выборку по времени. `_MainTex` и атласов нет.
Shader "FrostSpace/VFX/Smoke"
{
    Properties
    {
        _Color("Цвет дыма", Color) = (0.16, 0.16, 0.17, 1)
        _Opacity("Плотность", Range(0, 1)) = 0.55
        _Softness("Мягкость кромки", Range(0.05, 0.95)) = 0.6
        _Swirl("Завихрение", Range(0, 1)) = 0.28
        _Detail("Крупность клубка", Range(1, 12)) = 4.5
        _Drift("Скорость завихрения", Range(0, 2)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SmokeForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Opacity;
                half _Softness;
                half _Swirl;
                half _Detail;
                half _Drift;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            // Дешёвый value-noise: синус от скалярного произведения. Для дыма
            // важна не статистика шума, а не повторяющийся клубок.
            half Noise(float2 p)
            {
                return frac(sin(dot(p, float2(41.317, 289.131))) * 43758.5453);
            }

            half NoiseSmooth(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(Noise(i), Noise(i + float2(1, 0)), u.x),
                    lerp(Noise(i + float2(0, 1)), Noise(i + float2(1, 1)), u.x),
                    u.y);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // UV квада партиклы: 0..1 по грани билборда. Кромка считается от
                // центра, поэтому край мягкий при любом размере частицы.
                float2 centered = i.uv * 2.0 - 1.0;
                float d = length(centered);

                float2 swirl = centered * _Detail + _Time.y * _Drift;
                half n = NoiseSmooth(swirl) * 0.6 + NoiseSmooth(swirl * 2.1) * 0.4;
                d -= (n - 0.5) * _Swirl;

                half edge = 1.0 - smoothstep(_Softness, 1.0, d);
                half alpha = edge * i.color.a * _Opacity;

                // Цвет частицы умножается, а не заменяется: одна система даёт и
                // чёрный дым из-под крыши, и серый, и пыль — оттенком startColor,
                // без второго материала на каждый оттенок.
                return half4(_Color.rgb * i.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}

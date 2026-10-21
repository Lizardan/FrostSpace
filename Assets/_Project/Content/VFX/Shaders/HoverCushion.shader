// Подушка ховербайка: аддитивные кольца и ядро под корпусом.
//
// Геометрия — плоский Quad под байком, поэтому шейдер работает в UV: центр 0.5,
// край 0 или 1. Кольца бегут от центра к краю, ядро держит свечение у самой
// плоскости. Материал аддитивный и без записи глубины: подушка светится поверх
// снега, но не спорит с ним за пиксели.
//
// Цвет приходит из HoverbikeGlow (циан цифрового слоя #4DE6FF), сила пульсирует
// от оборотов байка: на месте едва тлеет, в движении горит ярче.
Shader "FrostSpace/VFX/HoverCushion"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (0.302, 0.902, 1, 1)
        _Strength("Strength", Range(0, 2)) = 0.6
        _RingScale("Ring Scale", Float) = 4.5
        _PulseSpeed("Pulse Speed", Float) = 2.4
        _CoreSize("Core Size", Range(0, 1)) = 0.55
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
            Name "Cushion"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Strength;
                half _RingScale;
                half _PulseSpeed;
                half _CoreSize;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
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
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Эллипс подушки: Quad растянут по ходу байка, поэтому круг в UV
                // читается как вытянутая подушка под корпусом.
                float2 centered = input.uv * 2.0 - 1.0;
                float radius = length(centered);

                // Кольца идут наружу: фаза бежит по радиусу, а не по времени экрана,
                // поэтому подушка «дышит» от центра.
                half phase = (radius * _RingScale - _Time.y * _PulseSpeed) * 3.14159265h;
                half rings = pow(saturate(sin(phase) * 0.5h + 0.5h), 4.0h);

                half core = pow(saturate(1.0h - radius * (1.0h / max(_CoreSize, 0.05h)) * 0.5h), 2.0h);
                half edge = smoothstep(1.0h, 0.72h, radius);

                half glow = (rings * 0.65h + core * 0.9h) * edge;
                return half4(_Color.rgb * glow * _Strength, glow * _Strength);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

// Подушка ховербайка: два источника и вытянутая капсула по днищу.
//
// Геометрия — плоский Quad под байком, поэтому шейдер работает в UV: центр 0.5,
// край 0 или 1. Квад вытянут вдоль байка, и его локальная Y — длина; _Aspect
// переводит UV в пропорции борта, чтобы кольца и источники не растягивались
// в эллипс. Два ядра расходятся на скорости, капсула между ними держит
// вытянутый след, а руль изгибает всю подушку в сторону поворота.
//
// Материал аддитивный и без записи глубины: подушка светится поверх снега,
// но не спорит с ним за пиксели. Цвет и сила приходят из HoverbikeGlow;
// скорость и руль — тоже, через MaterialPropertyBlock.
Shader "FrostSpace/VFX/HoverCushion"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (0.302, 0.902, 1, 1)
        [HDR] _CoreColor("Core Color", Color) = (0.65, 0.98, 1, 1)
        _Strength("Strength", Range(0, 2)) = 0.6
        _RingScale("Ring Scale", Float) = 9
        _PulseSpeed("Pulse Speed", Float) = 2.4
        _CoreSize("Source Radius", Range(0.05, 1)) = 0.34
        _SourceSpread("Source Spread", Range(0, 1.5)) = 1.0
        _Bend("Steer Bend", Range(0, 0.5)) = 0.22
        _Aspect("Aspect (length / width)", Float) = 2
        _Speed("Speed", Range(0, 1)) = 0
        _Steer("Steer", Range(-1, 1)) = 0
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
                half4 _CoreColor;
                half _Strength;
                half _RingScale;
                half _PulseSpeed;
                half _CoreSize;
                half _SourceSpread;
                half _Bend;
                half _Aspect;
                half _Speed;
                half _Steer;
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

                float t = _Time.y;
                float speed = saturate(_Speed);
                float steer = clamp(_Steer, -1.0, 1.0);

                // UV → координаты борта. По X единица равна половине ширины,
                // по Y — половине длины; так расстояния и кольца круглые.
                float2 uv = input.uv * 2.0 - 1.0;
                float2 q = float2(uv.x, uv.y * max(_Aspect, 0.5));

                // Подушка дышит, покачивается и изгибается в сторону поворота.
                q.x -= steer * _Bend * uv.y;
                q.x += sin(t * 1.9 + uv.y * 3.0) * (0.015 + 0.03 * speed);
                q.y *= 1.0 + 0.04 * sin(t * 2.3);

                // Два источника: на скорости расходятся от центра и качаются
                // в противофазе, поэтому подушка не выглядит застывшей точкой.
                float spread = _SourceSpread * (0.8 + 0.35 * speed);
                float sway = sin(t * 2.1) * 0.025 * (0.4 + speed);
                float2 sourceA = float2(sway, -spread);
                float2 sourceB = float2(-sway, spread);

                // Капсула между источниками: длинный след по днищу.
                float2 toPoint = q - sourceA;
                float2 axis = sourceB - sourceA;
                float along = saturate(dot(toPoint, axis) / max(dot(axis, axis), 1e-4));
                float dist = length(toPoint - axis * along);

                // Радиус сжимается на ходу: на стоянке подушка шире и мягче.
                float radius = _CoreSize * (1.0 - 0.15 * speed) *
                               (1.0 + 0.05 * sin(t * 3.1));
                float body = saturate(1.0 - dist / max(radius, 0.02));
                body = body * body * (3.0 - 2.0 * body);

                // Кольца бегут от капсулы наружу, а не от центра экрана.
                float phase = (dist * _RingScale - t * _PulseSpeed) * 6.2831853;
                float rings = pow(saturate(sin(phase) * 0.5 + 0.5), 5.0) * body;

                // Ядра у источников: два ярких пятна на концах капсулы.
                float2 deltaA = q - sourceA;
                float2 deltaB = q - sourceB;
                float coreRadius = max(radius * radius * 0.10, 1e-4);
                float coreA = exp(-dot(deltaA, deltaA) / coreRadius) *
                              (1.0 + 0.12 * sin(t * 2.4));
                float coreB = exp(-dot(deltaB, deltaB) / coreRadius) *
                              (1.0 + 0.12 * sin(t * 2.4 + 3.14159265));
                float core = saturate(coreA + coreB);

                // Край квада гасится до нуля: прямоугольной границы не видно.
                float edge = smoothstep(1.0, 0.78, max(abs(uv.x), abs(uv.y)));

                float glow = (body * 0.45 + rings * 0.65 + core * 0.95) * edge;
                glow *= 1.0 + 0.06 * sin(t * 2.7);

                half3 color = lerp(_Color.rgb, _CoreColor.rgb, core);
                return half4(color, saturate(glow * _Strength));
            }
            ENDHLSL
        }
    }

    Fallback Off
}

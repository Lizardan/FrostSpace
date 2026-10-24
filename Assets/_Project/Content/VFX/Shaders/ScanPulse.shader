// Кольцо сонара: кромка, расходящаяся от игрока к границе радиуса.
//
// Геометрия — единичный квад в плоскости XZ, повёрнутый на 90° вокруг X, поэтому
// UV совпадают с локальными осями. Радиус точки считается от центра квада, так что
// кольцо остаётся круглым при любом масштабе: меш-аннулус и сегменты не нужны.
//
// Глубина не пишется, но тестируется: кольцо светится поверх снега и не видно
// сквозь стены и склоны. Цвет — тот же циан цифрового слоя, что у контура цели
// и кромки выреза (STYLE.md).
Shader "FrostSpace/VFX/ScanPulse"
{
    Properties
    {
        [HDR] _ScanColor("Scan Color", Color) = (0.302, 0.902, 1, 1)
        _Strength("Strength", Range(0, 1)) = 0
        _Progress("Progress", Range(0, 1)) = 0
        _Thickness("Thickness", Range(0.005, 0.4)) = 0.06
        _Softness("Softness", Range(0.05, 1)) = 0.6
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
            Name "Pulse"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            // Альфа-смешивание, а не аддитивное: снег почти белый, и аддитивный циан
            // на нём выгорает в белую полосу. Смешивание подкрашивает снег в циан,
            // поэтому кольцо читается и на светлом, и на тёмном.
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ScanColor;
                half _Strength;
                half _Progress;
                half _Thickness;
                half _Softness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_TARGET
            {
                // Радиус 0 в центре квада, 1 на вписанной окружности.
                float2 centered = input.uv * 2.0 - 1.0;
                float radius = length(centered);
                if (radius > 1.0)
                    discard;

                float distanceToEdge = abs(radius - _Progress);
                half band = smoothstep(_Thickness, 0.0, distanceToEdge);
                half halo = smoothstep(_Thickness * (1.0 + _Softness), 0.0, distanceToEdge);

                // Внутри кольца — очень слабая заливка: она читается как «область
                // проверена» и не забивает картинку.
                half fill = smoothstep(_Progress, 0.0, radius) * 0.08;

                // Кольцо проявляется и гаснет на концах цикла, иначе пульс «щёлкает».
                half life = smoothstep(0.0, 0.08, _Progress) *
                            (1.0 - smoothstep(0.82, 1.0, _Progress));

                half glow = (band * 1.05 + halo * 0.35 + fill) * life * _Strength;
                return half4(_ScanColor.rgb * 1.15, saturate(glow * 0.85));
            }
            ENDHLSL
        }
    }

    Fallback Off
}

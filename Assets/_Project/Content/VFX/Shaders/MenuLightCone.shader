// Луч прожектора для вида «станция» главного меню.
//
// Меш — конус ProBuilder, вершина смотрит вверх (+Y), основание — вниз.
// Шейдер считает долю длины по positionOS.y и полувысоту берёт из _HalfHeight,
// поэтому не зависит от UV и от нарезки меша.
//
// Яркость: гаснет от лампы к земле по степенному профилю, у кромки
// подсвечивается по френелю — получается объёмный «столб» света, а не
// плоский треугольник. Внутри медленно плывут волокна пыли.
//
// Аддитивный блендинг, Cull Off (камера видит и внутреннюю сторону конуса),
// ZWrite Off: луч не перекрывает снег и постройки.
Shader "FrostSpace/Menu/LightCone"
{
    Properties
    {
        [HDR] _BeamColor("Beam Color", Color) = (1, 0.79, 0.47, 1)
        _Intensity("Intensity", Range(0, 4)) = 0.55
        _Alpha("Alpha", Range(0, 1)) = 0.5
        _HalfHeight("Half Height (mesh units)", Float) = 4
        _Falloff("Falloff Power", Range(0.25, 6)) = 1.6
        _EdgePower("Edge Power", Range(1, 6)) = 2.2
        _EdgeBoost("Edge Boost", Range(0, 2)) = 0.7
        _Streaks("Dust Streaks", Range(0, 1)) = 0.25
        _Scroll("Dust Speed", Float) = 0.8
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+60"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Beam"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
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
                half4 _BeamColor;
                half _Intensity;
                half _Alpha;
                half _HalfHeight;
                half _Falloff;
                half _EdgePower;
                half _EdgeBoost;
                half _Streaks;
                half _Scroll;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positions.positionCS;
                output.positionOS = input.positionOS.xyz;
                output.positionWS = positions.positionWS;
                output.normalWS = normals.normalWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 0 у вершины конуса (лампа), 1 у основания (земля).
                half along = saturate((_HalfHeight - input.positionOS.y) / max(_HalfHeight * 2.0h, 0.001h));
                half falloff = pow(saturate(1.0h - along), _Falloff);

                half3 normal = normalize(input.normalWS);
                half3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half edge = pow(saturate(1.0h - abs(dot(normal, viewDir))), _EdgePower);

                // Волокна пыли, плывущие от лампы к земле.
                half dust = frac(along * 6.0h - _Time.y * _Scroll);
                half streaks = pow(dust, 8.0h) * _Streaks;

                half flicker = 0.94h + 0.06h * sin(_Time.y * 21.0h + along * 9.0h);

                half glow = falloff * (1.0h + edge * _EdgeBoost + streaks) * flicker;

                return half4(_BeamColor.rgb * glow * _Intensity, saturate(glow) * _Alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

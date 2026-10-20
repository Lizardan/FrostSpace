// X-ray силуэт игрока: рисуется только там, где его закрывает геометрия.
//
// Отдельный рендерер-дубликат модели с ZTest Greater: пиксели, которые ближе
// к камере, чем уже нарисованная геометрия, отбрасываются, поэтому силуэт
// «проступает» сквозь дерево или стену, не делая в них дыру.
//
// Материал unlit: эффект не зависит от света и пост-обработки. Альфа ведётся
// из PlayerSilhouette (плавное появление и возврат).
Shader "FrostSpace/VFX/OcclusionSilhouette"
{
    Properties
    {
        _Color("Color", Color) = (0.3, 0.9, 1, 0.55)
        _Intensity("Intensity", Float) = 1.6
        _EdgeBoost("Edge Boost", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+50"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Silhouette"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest Greater
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _EdgeBoost;
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
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
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
                output.positionWS = positions.positionWS;
                output.normalWS = normals.normalWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Кромка силуэта ярче заливки: объём читается даже одной плоскостью.
                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half rim = 1.0h - saturate(dot(normalize(input.normalWS), viewDir));
                half glow = 1.0h + _EdgeBoost * rim;

                return half4(_Color.rgb * (_Intensity * glow), _Color.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

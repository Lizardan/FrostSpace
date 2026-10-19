// Кубы-осколки, которые вылетают из растворяемой поверхности.
// Обычный unlit-материал: эффект не должен зависеть от света и пост-обработки.
Shader "FrostSpace/VFX/OcclusionDebris"
{
    Properties
    {
        _Color("Color", Color) = (0.3, 0.9, 1, 1)
        _Intensity("Intensity", Float) = 2.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
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
                float3 normalWS : TEXCOORD0;
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
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half shade = 0.6 + 0.4 * saturate(dot(normalize(input.normalWS), normalize(float3(0.4, 0.9, 0.2))));
                return half4(_Color.rgb * (_Intensity * shade), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

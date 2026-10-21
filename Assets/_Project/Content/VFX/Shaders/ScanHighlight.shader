// Сканирование объекта: циановый контур, свечение кромки и бегущая полоса.
//
// На цель вешается отдельный дубль геометрии с этим материалом (ObjectScanner):
// сам объект остаётся освещённым и в своей палитре, а сверху ложится цифровой
// слой — так «отсканированный» объект не выпадает из стиля уровня.
//
// Толщина контура задаётся в пикселях экрана, поэтому силуэт читается одинаково
// и вблизи, и на дальней дистанции. Полоса скана идёт по мировому Y от нижней
// границы объекта к верхней; границы и прогресс приходят глобальными параметрами
// из ObjectScanner. Цвет — тот же циан, что у силуэта игрока и кромки выреза
// (STYLE.md, «цифровой слой»).
Shader "FrostSpace/VFX/ScanHighlight"
{
    Properties
    {
        [HDR] _ScanColor("Scan Color", Color) = (0.302, 0.902, 1, 1)
        _Strength("Strength", Range(0, 1)) = 0
        _RimPower("Rim Power", Range(1, 6)) = 2.0
        _OutlineWidth("Outline Width (px)", Float) = 2.6
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

        // Контур: оболочка, вывернутая наизнанку и раздутая по нормали. Даёт
        // читаемый силуэт на любом меше, включая фасетный лоуполе уровня.
        Pass
        {
            Name "Contour"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front
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
                half4 _ScanColor;
                half _Strength;
                half _RimPower;
                half _OutlineWidth;
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

                // Толщина задаётся в пикселях экрана и переводится в мировые единицы по
                // дистанции до камеры: контур одинаково жирный и в метре, и на 30 метрах.
                // UNITY_MATRIX_P[1][1] = 1 / tan(fov/2), _ScreenParams.y — высота экрана.
                float viewDistance = max(distance(positions.positionWS, GetCameraPositionWS()), 0.05);
                float pixelsToWorld = 2.0 * viewDistance /
                                      max(UNITY_MATRIX_P[1][1] * _ScreenParams.y, 0.001);

                float3 extruded = positions.positionWS +
                                  normals.normalWS * (_OutlineWidth * pixelsToWorld);
                output.positionCS = TransformWorldToHClip(extruded);
                return output;
            }

            half4 Frag(Varyings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(_ScanColor.rgb * (2.4h * _Strength), _Strength);
            }
            ENDHLSL
        }

        // Заливка: кромка по френелю и полоса, бегущая снизу вверх по объекту.
        Pass
        {
            Name "Scan"
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
                half4 _ScanColor;
                half _Strength;
                half _RimPower;
                half _OutlineWidth;
            CBUFFER_END

            // Глобальные параметры кадра (Shader.SetGlobalVector из ObjectScanner):
            // x — низ объекта по Y, y — верх, z — прогресс скана 0..1.
            float4 _ScanBand;

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

                float3 normal = normalize(input.normalWS);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // Кромка: тонкая светящаяся рамка по силуэту.
                half rim = pow(saturate(1.0h - dot(normal, viewDir)), _RimPower);

                // Полоса скана: узкая яркая линия, которая проходит объект снизу вверх
                // и затухает, когда скан дошёл до верхней границы.
                half height = max(_ScanBand.y - _ScanBand.x, 0.001h);
                half t = saturate((input.positionWS.y - _ScanBand.x) / height);
                half band = smoothstep(0.05h, 0.0h, abs(t - _ScanBand.z));

                half glow = rim * 1.4h + band * 3.0h + 0.22h;
                return half4(_ScanColor.rgb * glow * _Strength, saturate(glow) * _Strength);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

// Круговой вырез перекрытий вокруг игрока.
//
// Материал используется только на время, пока геометрия закрывает персонажа:
// фейдер подменяет материалы рендереров на копии с этим шейдером. Внутри круга
// на экране пиксели отбрасываются кубическими ячейками, по краю остаётся
// ступенчатая кромка с циановой эмиссией.
//
// Маска живёт в экранном пространстве: крыша дома висит в 4–5 м над игроком и
// в мировую сферу вокруг него не попадёт, а на экране она всегда проецируется
// в круг вокруг персонажа. Пиксели, которые дальше игрока (пол под ногами,
// дальняя стена), не вырезаются.
Shader "FrostSpace/VFX/OcclusionReveal"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)
        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0.3
        _Cull("Cull", Float) = 2

        [Header(Reveal)]
        _RevealStrength("Reveal Strength", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Свойства материала. Всё, что меняется на материал, — в одном cbuffer:
        // это сохраняет SRP Batcher для подменённых копий.
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _EmissionColor;
            half _Metallic;
            half _Smoothness;
            half _RevealStrength;
        CBUFFER_END

        // Параметры эффекта — глобальные, чтобы их можно было крутить в
        // инспекторе и не пересоздавать копии материалов.
        float4 _EdgeColor;
        float _EdgeIntensity;
        float _EdgeSoftness;
        float _CellJitter;

        // Глобальные параметры кадра (Shader.SetGlobal*), общие для всех копий.
        // x,y — центр круга в viewport, z — радиус в долях высоты экрана,
        // w — глубина игрока в пространстве камеры.
        float4 _RevealCenter;
        // x — aspect, y — шаг пикселизации в UV, z — размер ячейки в метрах,
        // w — время (пульсация кромки).
        float4 _RevealSetup;
        // Мировой Y уровня пола под игроком: не даёт вырезать пол и снег.
        float _RevealFloorY;

        float Hash13(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.yzx + 33.33);
            return frac((p.x + p.y) * p.z);
        }

        // Рекурсивный Bayer 4x4/8x8: даёт ступенчатый, но плавный порог
        // без текстуры шума.
        float Bayer2(float2 a)
        {
            a = floor(a);
            return frac(a.x * 0.5 + a.y * a.y * 0.75);
        }

        float Bayer4(float2 a)
        {
            return Bayer2(0.5 * a) * 0.25 + Bayer2(a);
        }

        float Bayer8(float2 a)
        {
            return Bayer4(0.5 * a) * 0.25 + Bayer2(a);
        }

        // amount > 0 — пиксель остаётся, amount < 0 — вырезан.
        // edge — 0..1, яркость кромки в полосе растворения.
        struct RevealSample
        {
            float amount;
            float edge;
        };

        RevealSample SampleReveal(float3 positionWS, float4 screenPos)
        {
            RevealSample sample;
            sample.amount = 1.0;
            sample.edge = 0.0;

            half strength = saturate(_RevealStrength);
            if (strength <= 0.0005)
                return sample;

            float radius = max(_RevealCenter.z, 1e-4);
            float2 uv = screenPos.xy / max(screenPos.w, 1e-5);

            // Пикселизация: круг становится ступенчатым, как в низком разрешении.
            float stepUV = max(_RevealSetup.y, 1e-5);
            float2 uvq = (floor(uv / stepUV) + 0.5) * stepUV;

            float2 delta = (uvq - _RevealCenter.xy) * float2(_RevealSetup.x, 1.0);
            float dist = length(delta) / radius;

            // Вырезаем только то, что ближе к камере, чем игрок, и находится
            // выше уровня пола: иначе круг прорезает пол под ногами и снег.
            float eyeDepth = -TransformWorldToView(positionWS).z;
            float inFront = step(eyeDepth, _RevealCenter.w - 0.03);
            float aboveFloor = step(_RevealFloorY, positionWS.y);
            inFront *= aboveFloor;

            // Кубические ячейки в мире: у каждой свой стабильный порог.
            float3 cell = floor(positionWS / max(_RevealSetup.z, 0.01));
            float cellNoise = Hash13(cell);

            float softness = saturate(_EdgeSoftness);
            float radial = 1.0 - smoothstep(1.0 - softness, 1.0, dist);
            float progress = radial * strength;

            // Дизеринг работает только внутри круга: иначе он вырезает
            // отдельные ячейки по всей поверхности и получается шахматка.
            float dither = (Bayer8(screenPos.xy) - 0.5) * 0.12 * softness
                         * saturate(progress);

            // cut > 0 — пиксель вырезан. В центре progress = 1 и вырезаны все
            // ячейки; к краю cut уходит в минус и ячейки с разным шумом
            // возвращаются вразнобой — получается кубический фронт.
            float cut = progress * (1.0 + _CellJitter)
                      - cellNoise * _CellJitter - 0.002 + dither;
            float keep = lerp(1.0, -cut, inFront);

            sample.amount = keep;
            // Свечение — только у настоящей границы выреза. Вне круга progress = 0,
            // иначе кромка подсвечивала бы случайные ячейки по всей поверхности.
            // Чем ближе ячейка к порогу растворения, тем ярче её кромка.
            float edge = saturate(1.0 - keep * 1.6);
            sample.edge = inFront * saturate(progress * 20.0) * edge * edge;
            return sample;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LitVertex
            #pragma fragment LitFragment

            #pragma shader_feature_local_fragment _EMISSION

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float4 screenPos : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings LitVertex(Attributes input)
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
                output.screenPos = ComputeScreenPos(positions.positionCS);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 LitFragment(Varyings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                RevealSample reveal = SampleReveal(input.positionWS, input.screenPos);
                clip(reveal.amount);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.fogCoord = input.fogFactor;
                inputData.vertexLighting = half3(0, 0, 0);

            #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            #endif

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = _BaseColor.rgb;
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1.0;
                surface.alpha = 1.0;
            #if defined(_EMISSION)
                surface.emission = _EmissionColor.rgb;
            #endif

                half4 color = UniversalFragmentPBR(inputData, surface);

                // Кромка: холодная эмиссия по краю растворившихся ячеек.
                float pulse = 0.85 + 0.15 * sin(_RevealSetup.w * 9.0);
                color.rgb += _EdgeColor.rgb * (_EdgeIntensity * pulse) * reveal.edge;

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        // Нужен, если включена depth-текстура: глубина должна совпадать с цветом,
        // иначе на месте выреза останутся артефакты пост-обработки.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVertex(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.screenPos = ComputeScreenPos(positions.positionCS);
                return output;
            }

            half DepthFragment(DepthVaryings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                clip(SampleReveal(input.positionWS, input.screenPos).amount);
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }

    Fallback Off
}

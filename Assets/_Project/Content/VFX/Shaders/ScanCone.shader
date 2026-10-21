// Луч сканирования: голографический объём квадратного сечения из головы робота к цели.
//
// Меш — квадратная пирамида (вершина в голове, квадрат на дистанции до цели), поэтому
// эффект читается как реальный объём сканирующего луча, а не как плоский спрайт.
// uv.y — доля длины (квадратные сечения), uv.x — доля периметра (рёбра идут по углам).
//
// Рисуется аддитивно и без записи глубины: луч светится, но перекрывается стенами,
// поэтому «сквозь стену» скан не показывает.
//
// Cull Off обязателен: камера в третьем лице смотрит почти вдоль оси луча и видит
// внутреннюю сторону граней — при Cull Back от эффекта остаётся узкая полоса.
//
// Альфа здесь работает как яркость (Blend SrcAlpha One), поэтому профиль вдоль луча
// задаёт видимую яркость: у головы и у цели ноль, в середине _AlphaMid.
//
// Структуру дают квадратные сечения: каждое — яркая рамка с мягким следом назад.
// Рёбра стоят в углах сечения (_Spokes = 4), а чередование сегментов чуть подсвечивает
// каждый второй «дольку», чтобы луч читался набором квадратов, а не стеклянной трубой.
Shader "FrostSpace/VFX/ScanCone"
{
    Properties
    {
        [HDR] _ScanColor("Scan Color", Color) = (0.302, 0.902, 1, 1)
        _Intensity("Intensity", Range(0, 3)) = 1
        _Strength("Strength", Range(0, 1)) = 0
        _Progress("Progress", Range(0, 1)) = 0
        _Rings("Sections Along Axis", Float) = 10
        _Spokes("Edges Around Perimetr", Float) = 4
        _Scroll("Scroll Speed", Float) = 0.45
        _Body("Body Floor", Range(0, 0.2)) = 0.004
        _Segment("Bright Segment Boost", Range(0, 3)) = 0.18
        _AlphaMid("Alpha At Middle", Range(0, 1)) = 0.5
        _AlphaPower("Alpha Profile Power", Range(0.25, 4)) = 1
        _EdgePower("Edge Power", Range(1, 6)) = 2.6
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
                half4 _ScanColor;
                half _Intensity;
                half _Strength;
                half _Progress;
                half _Rings;
                half _Spokes;
                half _Scroll;
                half _Body;
                half _Segment;
                half _AlphaMid;
                half _AlphaPower;
                half _EdgePower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
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
                output.positionWS = positions.positionWS;
                output.normalWS = normals.normalWS;
                // uv.x — доля периметра (рёбра), uv.y — доля длины от головы к цели (сечения).
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half along = saturate(input.uv.y);
                half around = input.uv.x;

                // Профиль альфы вдоль луча: у головы и у цели ноль, в середине _AlphaMid.
                half bell = sin(3.14159265h * along);
                half profile = _AlphaMid * pow(bell, max(_AlphaPower, 0.25h));

                // Квадратные сечения бегут от головы к цели: рамка с мягким следом назад.
                half ringPhase = frac(along * _Rings - _Time.y * _Scroll);
                half rings = pow(ringPhase, 6.0h);

                // Рёбра: при _Spokes = 4 линии стоят ровно в углах квадратного сечения.
                half spokePhase = frac(around * _Spokes);
                half spokes = pow(spokePhase, 8.0h);

                // Каждый второй квадратный сегмент подсвечен — луч читается «дольками».
                half band = floor(along * _Rings * 0.5h);
                half segment = step(0.5h, frac(band * 0.5h));

                // Фронт скана: узкая яркая рамка, идущая к цели за время сканирования.
                half wave = smoothstep(0.72h, 1.0h, 1.0h - abs(along - _Progress) * 6.0h);

                // Кромка грани: при взгляде вдоль оси френель почти постоянен, поэтому вес мал.
                half3 normal = normalize(input.normalWS);
                half3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half edge = pow(saturate(1.0h - abs(dot(normal, viewDir))), _EdgePower);

                // Дыхание яркости: голограмма не должна быть идеально ровной.
                half flicker = 0.94h + 0.06h * sin(_Time.y * 37.0h);

                half glow = _Body + rings * 1.90h + spokes * 0.75h + segment * _Segment +
                            edge * 0.03h + wave * 1.60h;

                half alpha = saturate(_Strength * profile * flicker);
                return half4(_ScanColor.rgb * glow * flicker * _Intensity, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

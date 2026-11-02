// Ночное небо колонии 07: почти чёрное, с редкими особенно яркими звёздами.
//
// Канон — `WORLD.md` § «Остывший космос»: «Небо почти чёрное: звёзд почти нет,
// но несколько особенно ярких остались и видны всегда». До этого шейдера камера
// `Frontier` чистила кадр сплошным цветом мглы (#85A1B8), и небо в игре было
// светло-серым — то есть противоречило собственному документу.
//
// Небо — скайбокс: рисуется на кубе вокруг камеры, `dir` = положение вершины.
// Туман на скайбокс не действует, поэтому гориз отдельно подкрашен цветом
// `RenderSettings.fogColor`: кадр обязан сходиться с мглой без шва.
//
// Звёзды — хеш по ячейкам на сфере, без текстур и без циклов: стоимость одного
// пикселя. Ядро ярчайших выходит за 1.05 — порог Bloom в проекте, — поэтому
// «особо яркие» действительно горят, а не просто светлые точки.
//
// Аномалия (`WORLD.md` § «Остывший космос») — одно тёмное тело со светлым
// кольцом в фиксированном направлении. Скайбокс, а не объект: ноль треугольников
// и ноль источников света, поэтому правило «одна тёплая лампа на кадр» не
// нарушается. Направление задаётся числом из `world_layout.py` (азимут 285°,
// высота 40°), а не на глаз: с площадки «Откоса» оно ложится в кадр левее
// силуэта «Западного», и этот зазор проверяется инвариантом.
Shader "FrostSpace/Sky/SkyNightStars"
{
    Properties
    {
        [HDR] _HorizonColor("Horizon Color", Color) = (0.52, 0.63, 0.72, 1)
        [HDR] _ZenithColor("Zenith Color", Color) = (0.02, 0.03, 0.05, 1)

        [Header(Stars)]
        _StarCells("Star Cells", Range(6, 80)) = 40
        _StarChance("Star Chance", Range(0, 1)) = 0.02
        _StarSize("Star Size", Range(0.02, 1)) = 0.16
        [HDR] _StarColor("Star Color", Color) = (0.86, 0.91, 0.95, 1)
        _StarBrightness("Star Brightness", Range(0, 6)) = 2.2

        [Header(Anomaly)]
        _AnomalyDir("Anomaly Direction (world)", Vector) = (-0.739942, 0.642788, 0.198267, 0)
        _AnomalyRadius("Anomaly Radius (deg)", Range(0.5, 12)) = 3.2
        [HDR] _AnomalyColor("Anomaly Ring Color", Color) = (0.62, 0.70, 0.80, 1)
        _AnomalyRing("Anomaly Ring Intensity", Range(0, 4)) = 1.35
        _AnomalyStrength("Anomaly Strength", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Skybox"
            "PreviewType" = "Skybox"
            "IgnoreProjector" = "True"
        }

        Cull Off
        ZWrite Off
        Lighting Off
        Fog { Mode Off }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 position : POSITION;
            };

            struct v2f
            {
                float4 clip : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            float4 _HorizonColor;
            float4 _ZenithColor;
            float _StarCells;
            float _StarChance;
            float _StarSize;
            float4 _StarColor;
            float _StarBrightness;
            float4 _AnomalyDir;
            float _AnomalyRadius;
            float4 _AnomalyColor;
            float _AnomalyRing;
            float _AnomalyStrength;

            float hash12(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac(p.x * p.y);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.clip = TransformObjectToHClip(v.position.xyz);
                o.dir = v.position.xyz;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float up = saturate(dir.y);

                // Мгла у горизонта, почти чёрное выше. Профиль обязан быть
                // ПОЛОСОЙ, а не плавным разгоном к зениту: первая версия брала
                // `pow(up, 1.5)` и на замере кадра с аномалией (40° над
                // горизонтом) небо оказалось (117,129,139) — серое, то есть
                // ровно тот дефект, из-за которого канон «небо почти чёрное»
                // не читался. Степень монотонно темнеет только к up=1, поэтому
                // вся середина неба оставалась смесью с цветом тумана.
                // Теперь: у самой кромки (up < 2°) значение точно равно цвету
                // тумана — шва между землёй и небом нет при любом профиле;
                // к 16° небо уже чёрное, и выше остаётся чёрным.
                float band = smoothstep(0.03, 0.28, up);
                float3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, band);

                // Звёзды — в той же полосе, где уже темно: у самого горизонта
                // их съедает дымка, и это правильно — внизу мир белый, вверху —
                // пустой. Отдельный порог больше не нужен, один профиль на оба.
                float haze = band;
                if (haze > 0.0)
                {
                    // Сетка обязана быть квадратной ПО УГЛУ, а не в параметрах
                    // сферы. Два прохода были неверными: азимут без поправки
                    // (первая версия) и азимут, ДЕЛЁННЫЙ на cos широты (вторая),
                    // — оба дают ячейку, на экране в разы шире, чем выше, и
                    // точка вытягивается в горизонтальную чёрточку. Замер кадра
                    // 2026-11-02: звёзды 0.42° на 0.18° вместо круглых.
                    // Метрика на сфере: долгота даёт дугу cos(широты)*dλ, широта
                    // — dφ. Значит в сетку азимут входит С МНОЖИТЕЛЕМ cos, и
                    // обе координаты получают одинаковую сторону ячейки:
                    // π/2 / _StarCells radians. У полюса ячейки сливаются в
                    // кольцо, но точка остаётся круглой — это лучший из двух
                    // дефектов, и он не виден: у зенита глаз аномалию ищет.
                    float coslat = sqrt(max(0.02, 1.0 - dir.y * dir.y));
                    float2 sph = float2(atan2(dir.x, dir.z) * 0.63661977 * coslat,
                                        asin(clamp(dir.y, -1.0, 1.0)) * 0.63661977);
                    float2 grid = sph * _StarCells;
                    float2 cell = floor(grid);
                    float2 f = grid - cell;

                    float chance = hash12(cell);
                    if (chance < _StarChance)
                    {
                        // Одна точка на ячейку: «редкие» — это не параметр
                        // красоты, а формулировка канона.
                        float2 center = float2(hash12(cell + float2(1.31, 7.73)),
                                               hash12(cell + float2(3.07, 11.19)));
                        float d = length(f - center);
                        float r = _StarSize * 0.5;
                        float core = saturate(1.0 - d / max(r, 1e-4));
                        // Кубическое затухание: резкая точка с мягким ореолом,
                        // а не мыльное пятно. Фасетный мир просит именно точку.
                        core *= core * core;

                        // Разброс яркости по третьей хеш-координате: из всех
                        // звёзд горят только самые яркие, остальные — тусклые.
                        float magnitude = lerp(0.25, 1.0,
                                               pow(hash12(cell + float2(5.41, 2.17)), 2.0));
                        col += _StarColor.rgb * (_StarBrightness * magnitude * core) * haze;
                    }
                }

                // Аномалия: угол между пикселем и фиксированным направлением.
                // Порядок важен: затемнение диска стоит ПОСЛЕ звёзд, поэтому
                // точки внутри него гаснут сами, без отдельной пробы «не
                // рисовать звезду под диском».
                if (_AnomalyStrength > 0.0)
                {
                    float3 an = normalize(_AnomalyDir.xyz);
                    float ang = acos(clamp(dot(dir, an), -1.0, 1.0));
                    float r0 = radians(_AnomalyRadius);
                    float w = r0 * 0.16;
                    float disc = saturate((r0 - ang) / (r0 * 0.35));
                    col *= 1.0 - 0.9 * disc * _AnomalyStrength;

                    // Кольцо — единственное, что выдаёт диск: он темнее неба,
                    // которое и так почти чёрное. Gaussian по углу держит
                    // резкую нить, а широкий хвост того же профиля — слабое
                    // свечение вокруг, за счёт которого край диска и читается.
                    float d = ang - r0 * 1.06;
                    float ring = exp(-(d * d) / (w * w));
                    float halo = exp(-(d * d) / (9.0 * w * w));
                    // Кольцо бледное и холодное, но НЕ циан: циан в проекте —
                    // только силуэт и UI (`STYLE.md`). Тёплого в нём тоже нет:
                    // единственная тёплая лампа кадра должна остаться одной.
                    col += _AnomalyColor.rgb * _AnomalyRing *
                           (ring + 0.06 * halo) * _AnomalyStrength;
                }

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}

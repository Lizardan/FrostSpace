using System.Reflection;
using FrostSpace.Features.Story;
using FrostSpace.Features.View;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Страховка против расхождения «код говорит одно, сцена — другое».
    ///
    /// Поля прибытия (`_path`, `_seconds`, `_doorAim`, `_aim*`) сериализованы в
    /// `Frontier.unity` и перекрывают дефолты класса. Без этой проверки правка
    /// `DefaultPath()` даёт зелёные тесты и неизменную игру — так уже случилось
    /// один раз, и разница стоила целый смонтированный кадр.
    ///
    /// Вторая проверка — про небо. Оно живёт в двух сериализованных местах
    /// (`RenderSettings.skybox` и `Camera.clearFlags`), и правка из Editor-скрипта
    /// не помечает сцену грязной: `save` пишет старые байты, а `isDirty` при этом
    /// `false`. Так и вышло 2026-11-02: шейдер был готов, кадры сняты, а в файле
    /// остались процедурный скайбокс и `SolidColor` — то есть игрок видел бы
    /// светло-серую заглушку. Числа тумана и дальности — `STYLE.md` §«Туман дали
    /// в `Frontier`».
    ///
    /// Тест открывает сцену только если в редакторе нечего терять: активная
    /// сцена с несохранёнными правками — работа человека, а не входные данные
    /// теста. Тогда он помечается пропущенным, а не переписывает сцену.
    /// </summary>
    public sealed class FrontierSceneSyncTests
    {
        private const string ScenePath = "Assets/Scenes/Frontier.unity";

        /// <summary>Небо колонии: шейдер, который обязан стоять в сцене.</summary>
        private const string SkyShader = "FrostSpace/Sky/SkyNightStars";

        private static readonly BindingFlags Fields =
            BindingFlags.NonPublic | BindingFlags.Instance;

        [Test]
        public void ArrivalPathMatchesCodeDefaults()
        {
            var scene = OpenFrontier(out var original);

            try
            {
                Assert.That(scene.IsValid(), Is.True, "сцена не открылась");

                var director =
                    Object.FindAnyObjectByType<DepotArrivalDirector>();
                Assert.That(director, Is.Not.Null,
                    "в Frontier нет DepotArrivalDirector");

                var type = director.GetType();
                var path = (CameraKey[])Read(type, director, "_path");
                Assert.That(path, Is.Not.Null, "_path не сериализован");

                var expected = DepotArrivalDirector.DefaultPath();
                Assert.That(path.Length, Is.EqualTo(expected.Length),
                    "точек в сцене " + path.Length + ", в коде " + expected.Length);
                for (var i = 0; i < expected.Length; i++)
                {
                    Assert.That(Delta(path[i].Position, expected[i].Position),
                        Is.LessThan(1e-3f),
                        "P" + i + ": позиция в сцене не совпадает с DefaultPath()");
                    Assert.That(path[i].Fov, Is.EqualTo(expected[i].Fov).Within(1e-3f),
                        "P" + i + ": FOV в сцене не совпадает с DefaultPath()");
                }

                Assert.That(Read(type, director, "_seconds"),
                    Is.EqualTo(DepotArrivalDirector.PathSeconds).Within(1e-4f),
                    "_seconds разошёлся с PathSeconds");
                Assert.That(Read(type, director, "_settleSeconds"),
                    Is.EqualTo(DepotArrivalDirector.SettleSeconds).Within(1e-4f),
                    "_settleSeconds разошёлся с SettleSeconds");
                Assert.That(Delta((Vector3)Read(type, director, "_doorAim"),
                                  DepotArrivalDirector.DefaultDoorAim),
                    Is.LessThan(1e-3f), "_doorAim разошёлся с DefaultDoorAim");
                Assert.That(Read(type, director, "_aimHandoffSeconds"),
                    Is.EqualTo(DepotArrivalDirector.AimHandoffSeconds).Within(1e-4f),
                    "_aimHandoffSeconds разошёлся с AimHandoffSeconds");
                Assert.That(Read(type, director, "_aimBlendSeconds"),
                    Is.EqualTo(DepotArrivalDirector.AimBlendSeconds).Within(1e-4f),
                    "_aimBlendSeconds разошёлся с AimBlendSeconds");
            }
            finally
            {
                Restore(original);
            }
        }

        [Test]
        public void SkyIsNightAndCameraDrawsIt()
        {
            var scene = OpenFrontier(out var original);

            try
            {
                Assert.That(RenderSettings.fog, Is.True,
                    "в Frontier выключен туман — дальние слои должны тонуть в мгле");
                Assert.That(RenderSettings.fogMode,
                    Is.EqualTo(FogMode.ExponentialSquared),
                    "туман обязан быть Exp²: профиль 50 % на 139 м и 99 % на 358 м "
                    + "выведен именно из него");
                Assert.That(RenderSettings.fogDensity, Is.EqualTo(0.006f).Within(1e-5f),
                    "плотность тумана разошлась со STYLE.md (0.006)");

                var sky = RenderSettings.skybox;
                Assert.That(sky, Is.Not.Null, "у Frontier нет скайбокса");
                Assert.That(sky.shader.name, Is.EqualTo(SkyShader),
                    "небо сцены — " + sky.shader.name + ", а не ночной шейдер: "
                    + "правка RenderSettings не пометила сцену грязной");

                var camera = Camera.main;
                Assert.That(camera, Is.Not.Null,
                    "в Frontier нет камеры с тегом MainCamera");
                Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.Skybox),
                    "камера чистит кадр " + camera.clearFlags +
                    " — скайбокс с звёздами не будет виден");

                // Гориз неба обязан совпадать с цветом тумана: иначе на линии
                // земли и неба появляется шов, и мир выглядит обрезанным.
                var horizon = sky.GetColor("_HorizonColor");
                var fog = RenderSettings.fogColor;
                Assert.That(Delta(horizon, fog), Is.LessThan(0.01f),
                    "гориз неба " + horizon + " не совпадает с туманом " + fog);

                // Одна аномалия и обязательно в том направлении, которое
                // задало `world_layout.py`: азимут 285°, высота 40°.
                Assert.That(sky.GetFloat("_AnomalyStrength"), Is.GreaterThan(0f),
                    "аномалия выключена, а по канону она есть");
                var raw = sky.GetVector("_AnomalyDir");
                var dir = new Vector3(raw.x, raw.y, raw.z).normalized;
                var want = new Vector3(-0.739942f, 0.642788f, 0.198267f);
                Assert.That(Vector3.Angle(dir, want), Is.LessThan(1f),
                    "аномалия смотрит не туда, где её рисует раскладка");
            }
            finally
            {
                Restore(original);
            }
        }

        /// <summary>
        /// Открывает Frontier или пропускает тест, если активная сцена не
        /// сохранена: правка человека — не входные данные теста.
        /// </summary>
        private static Scene OpenFrontier(out Scene original)
        {
            original = EditorSceneManager.GetActiveScene();
            if (original.isDirty && original.path != ScenePath)
            {
                Assert.Ignore(
                    "активная сцена " + original.name + " не сохранена — "
                    + "открывать Frontier значит затерять правки");
            }

            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        /// <summary>Возвращает редактор в ту сцену, из которой тест ушёл.</summary>
        private static void Restore(Scene original)
        {
            if (original.IsValid() && original.path != ScenePath)
                EditorSceneManager.OpenScene(original.path, OpenSceneMode.Single);
        }

        /// <summary>Наибольшая компонентная разница двух точек.</summary>
        private static float Delta(Vector3 a, Vector3 b)
        {
            return Mathf.Max(Mathf.Abs(a.x - b.x),
                             Mathf.Max(Mathf.Abs(a.y - b.y), Mathf.Abs(a.z - b.z)));
        }

        /// <summary>То же для цвета: максимум по каналам RGB.</summary>
        private static float Delta(Color a, Color b)
        {
            return Mathf.Max(Mathf.Abs(a.r - b.r),
                             Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
        }

        private static object Read(System.Type type, Object target, string name)
        {
            var field = type.GetField(name, Fields);
            Assert.That(field, Is.Not.Null, "нет сериализованного поля " + name);
            return field.GetValue(target);
        }
    }
}

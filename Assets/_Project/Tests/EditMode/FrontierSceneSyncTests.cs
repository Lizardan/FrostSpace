using System.Reflection;
using FrostSpace.Features.Story;
using FrostSpace.Features.View;
using NUnit.Framework;
using UnityEditor;
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

                var insertPath = (CameraKey[])Read(type, director, "_insertPath");
                Assert.That(insertPath, Is.Not.Null, "_insertPath не сериализован");

                var expectedInsert = DepotArrivalDirector.DefaultInsertPath();
                Assert.That(insertPath.Length, Is.EqualTo(expectedInsert.Length),
                    "точек вставки в сцене " + insertPath.Length + ", в коде " + expectedInsert.Length);
                for (var i = 0; i < expectedInsert.Length; i++)
                {
                    Assert.That(Delta(insertPath[i].Position, expectedInsert[i].Position),
                        Is.LessThan(1e-3f),
                        "I" + i + ": позиция вставки не совпадает с DefaultInsertPath()");
                    Assert.That(insertPath[i].Fov, Is.EqualTo(expectedInsert[i].Fov).Within(1e-3f),
                        "I" + i + ": FOV вставки не совпадает с DefaultInsertPath()");
                }

                Assert.That(Read(type, director, "_insertSeconds"),
                    Is.EqualTo(DepotArrivalDirector.InsertSeconds).Within(1e-4f),
                    "_insertSeconds разошёлся с InsertSeconds");

                // Южная стена двора стоит на z = −145: старт раньше неё — единственное,
                // что делает «поезд заезжает в депо» видимым, а не подразумеваемым.
                Assert.That(((Vector3)Read(type, director, "_trainStartOffset")).z,
                    Is.LessThan(-150f),
                    "состав обязан стартовать за южной стеной, иначе въезд через разрыв не виден");
            }
            finally
            {
                Restore(original);
            }
        }

        /// <summary>
        /// Расписание заставки проверяется как единое число: внешний кадр добавлен
        /// перед проездом, и если очередь актёров закончится раньше камеры, игрок
        /// получит управление на середине шага, а посадка в позу рига разойдётся.
        /// </summary>
        [Test]
        public void ArrivalScheduleEndsWithTheCamera()
        {
            var scene = OpenFrontier(out var original);

            try
            {
                Assert.That(scene.IsValid(), Is.True, "сцена не открылась");

                var director = Object.FindAnyObjectByType<DepotArrivalDirector>();
                Assert.That(director, Is.Not.Null, "в Frontier нет DepotArrivalDirector");

                var s = director.Schedule();
                var total = DepotArrivalDirector.InsertSeconds + DepotArrivalDirector.PathSeconds;

                Assert.That(s.CameraEndsAt, Is.EqualTo(total).Within(1e-3f),
                    "весь кадр = вставка плюс проезд");
                Assert.That(s.TrainEndsAt, Is.GreaterThan(DepotArrivalDirector.InsertSeconds),
                    "состав должен доходить до платформы уже внутри главного кадра");
                Assert.That(s.DoorsOpenAt, Is.GreaterThan(s.TrainEndsAt),
                    "створки обязаны открываться после остановки");
                Assert.That(s.R13DoneAt, Is.LessThanOrEqualTo(s.CameraEndsAt + 0.01f),
                    "R13 обязан выйти до конца кадра");
                Assert.That(s.CartDoneAt, Is.LessThanOrEqualTo(s.CameraEndsAt + 0.01f),
                    "тележка обязана выехать до конца кадра");
                Assert.That(s.PlayerDoneAt, Is.EqualTo(s.CameraEndsAt).Within(0.01f),
                    "игрок обязан заканчивать ровно с концом кадра: на этом держится "
                    + "бесшовная передача управления");
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
        /// Клетка — владелец своего содержимого: актёры депо лежат в префабе,
        /// а игрок и режиссёр прибытия остаются сценовыми. Если актёр снова
        /// окажется в корне сцены, резидентность его не погасит, и он будет
        /// висеть в воздухе над выгруженной клеткой.
        /// </summary>
        [Test]
        public void DepotActorsLiveInCellPrefabAndPlayerStaysInScene()
        {
            var scene = OpenFrontier(out var original);

            try
            {
                var cells = GameObject.Find("WorldCells");
                Assert.That(cells, Is.Not.Null, "в Frontier нет корня WorldCells");

                var cell = cells.transform.Find("WorldCell_-2_-2");
                Assert.That(cell, Is.Not.Null, "нет инстанса клетки депо");

                var actors = new[]
                {
                    "DepotTrain", "R7_Drifter", "R16_Warden_A", "R16_Warden_B",
                    "R8_Bulwark", "R9_Tinker", "R20_Cogsmith",
                    "DepotLamp", "DepotDoorSpill", "DepotDoorFill",
                    "DepotArrival", "R13_Prospector", "Prop_GravCart"
                };

                foreach (var name in actors)
                {
                    var actor = FindDeep(cell, name);
                    Assert.That(actor, Is.Not.Null,
                        name + " должен лежать в чанке депо, а не в корне сцены");
                    Assert.That(PrefabUtility.IsPartOfPrefabInstance(actor.gameObject), Is.True,
                        name + " не часть префаба клетки — выгрузка его не погасит");
                }

                var player = GameObject.Find("Player");
                Assert.That(player, Is.Not.Null, "в сцене нет Player");
                Assert.That(player.transform.IsChildOf(cell), Is.False,
                    "игрок — сценовый объект и не должен попадать в префаб клетки");

                var director = Object.FindAnyObjectByType<DepotArrivalDirector>();
                Assert.That(director, Is.Not.Null, "в сцене нет DepotArrivalDirector");
                Assert.That(director.transform.IsChildOf(cell), Is.True,
                    "режиссёр прибытия — содержимое депо и должен лежать в чанке");
                Assert.That(PrefabUtility.IsPartOfPrefabInstance(director.gameObject), Is.True,
                    "режиссёр прибытия не часть префаба клетки — выгрузка его не погасит");
            }
            finally
            {
                Restore(original);
            }
        }

        /// <summary>
        /// Ремонтники двора: R9_Tinker и R20_Cogsmith сканируются и говорят, но
        /// ничего не заводят. Реплика без действия — часть замысла (владелец выбрал
        /// короткие реплики, а не квест), и тест бережёт именно это: если на их
        /// диалог навесить действие, проверка упадёт, а не промолчит.
        /// </summary>
        [Test]
        public void DepotRepairRobotsAreScannedAndSpeakWithoutQuest()
        {
            var scene = OpenFrontier(out var original);

            try
            {
                var cells = GameObject.Find("WorldCells");
                Assert.That(cells, Is.Not.Null, "в Frontier нет корня WorldCells");

                var cell = cells.transform.Find("WorldCell_-2_-2");
                Assert.That(cell, Is.Not.Null, "нет инстанса клетки депо");

                foreach (var name in new[] { "R9_Tinker", "R20_Cogsmith" })
                {
                    var actor = FindDeep(cell, name);
                    Assert.That(actor, Is.Not.Null, name + " должен стоять в чанке депо");

                    var target = actor.GetComponent<FrostSpace.Features.Scan.ScanTarget>();
                    Assert.That(target, Is.Not.Null, name + " не сканируется на Q");

                    var speaker =
                        actor.GetComponent<FrostSpace.Features.Dialogue.DialogueSpeaker>();
                    Assert.That(speaker, Is.Not.Null, name + " не говорит по E");
                    Assert.That(speaker.Dialogue, Is.Not.Null, name + " без ассета диалога");
                    Assert.That(speaker.Dialogue.Nodes.Count, Is.GreaterThan(0),
                        name + ": диалог пуст");

                    foreach (var node in speaker.Dialogue.Nodes)
                    {
                        Assert.That(node.HasAction, Is.False,
                            name + ": узел с действием — ремонтники квест не выдают");
                    }
                }
            }
            finally
            {
                Restore(original);
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t;

            return null;
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

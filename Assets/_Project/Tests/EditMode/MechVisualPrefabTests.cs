using System.Linq;
using FrostSpace.Features.Ride;
using FrostSpace.Features.Scan;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Проверяет контракт ездовых ходунов: префаб <c>ME_*_Model</c> — это и модель,
    /// и транспорт, клипы FBX называются ролями, а якоря наездника стоят там, где
    /// их посчитал генератор.
    ///
    /// Ожидания повторяют <c>Tools/Blender/mech_manifest.json</c> — манифест сборки
    /// <c>Tools/Blender/build_mechs.py</c>. Числа посадки в Unity не выдумываются:
    /// они пришли из манифеста, и тест ловит расхождение, если пересборка сдвинула
    /// сиденье, а префаб не пересобрали. Тесты читают ассеты, ничего не меняя.
    /// </summary>
    public sealed class MechVisualPrefabTests
    {
        private const string ModelFolder = "Assets/_Project/Content/Models/Mechs";
        private const string PrefabFolder = "Assets/_Project/Content/Prefabs/Ride";

        private static readonly string[] States = { "Idle", "Walk", "Run" };

        /// <summary>
        /// Ожидание по ходуну: габарит, точки посадки и длины клипов в секундах.
        /// Точки — в осях модели: (x вбок, y вверх, z вперёд).
        /// </summary>
        private sealed class Expectation
        {
            public string Unit;
            public float Height;
            public Vector3 Seat;
            public Vector3 SeatPoint;
            public Vector3 Grip;
            public Vector3 Foot;
            public float IdleSeconds;
            public float WalkSeconds;
            public float RunSeconds;
        }

        private static Expectation[] Expectations()
        {
            return new[]
            {
                Expect("FrogWalker", 2.389f, 1.219f, 1.309f, 0.24f, 1.549f, 0.50f,
                    0.19f, 0.819f, 0.44f, 3.33f, 1.00f, 0.67f),
                Expect("BeeWalker", 2.412f, 1.230f, 1.320f, 0.24f, 1.560f, 0.50f,
                    0.19f, 0.830f, 0.44f, 3.33f, 1.00f, 0.67f),
                Expect("PandaWalker", 2.356f, 1.191f, 1.281f, 0.24f, 1.521f, 0.50f,
                    0.19f, 0.791f, 0.44f, 3.33f, 1.00f, 0.67f),
                Expect("FlamingoWalker", 2.175f, 1.062f, 1.152f, 0.24f, 1.392f, 0.50f,
                    0.19f, 0.662f, 0.44f, 3.33f, 1.00f, 0.67f),
            };
        }

        private static Expectation Expect(
            string unit, float height,
            float seatY, float seatPointY, float gripX, float gripY, float gripZ,
            float footX, float footY, float footZ,
            float idleSeconds, float walkSeconds, float runSeconds)
        {
            return new Expectation
            {
                Unit = unit,
                Height = height,
                Seat = new Vector3(0f, seatY, 0f),
                SeatPoint = new Vector3(0f, seatPointY, 0f),
                Grip = new Vector3(gripX, gripY, gripZ),
                Foot = new Vector3(footX, footY, footZ),
                IdleSeconds = idleSeconds,
                WalkSeconds = walkSeconds,
                RunSeconds = runSeconds,
            };
        }

        /// <summary>
        /// Список ходунов берётся из папки префабов, а не из константы: партия
        /// расширяется и сокращается без правки теста. Имя префаба
        /// <c>ME_X_Model</c> задаёт ходун <c>X</c>.
        /// </summary>
        private static string[] Mechs()
        {
            return AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith("_Model.prefab"))
                .Select(path => System.IO.Path.GetFileNameWithoutExtension(path))
                .Where(name => name.StartsWith("ME_"))
                .Select(name => name.Replace("ME_", string.Empty).Replace("_Model", string.Empty))
                .OrderBy(name => name)
                .ToArray();
        }

        private static Expectation For(string unit)
        {
            return Expectations().FirstOrDefault(expectation => expectation.Unit == unit);
        }

        private static Transform FindAnchor(GameObject prefab, string anchorName)
        {
            return prefab.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(found => found.name == anchorName);
        }

        [Test]
        public void MechFolder_HasFourWalkers()
        {
            var mechs = Mechs();
            CollectionAssert.IsNotEmpty(mechs, "В папке префабов нет ни одного ходуна");
            Assert.AreEqual(4, mechs.Length, "Ожидались четыре ходуна: " + string.Join(",", mechs));

            foreach (var mech in mechs)
                Assert.IsNotNull(For(mech), "Нет ожиданий для ходуна " + mech);
        }

        /// <summary>
        /// Префаб ходуна — самодостаточный транспорт: модель, Animator, капсула,
        /// логика езды, маркер сканера и настройка посадки на одном объекте.
        /// В отличие от роботов, где визуал отделён от актёра (ADR 0004), у ходуна
        /// визуал и транспорт неразделимы: клип, капсула и сиденье — один объект.
        /// </summary>
        [Test]
        public void Prefab_IsTransportWithVisualAndRideLogic()
        {
            foreach (var unit in Mechs())
            {
                var path = PrefabFolder + "/ME_" + unit + "_Model.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, "Нет префаба: " + path);

                var animator = prefab.GetComponent<Animator>();
                Assert.IsNotNull(animator, path + ": нет Animator");
                Assert.IsNotNull(
                    animator.runtimeAnimatorController, path + ": не назначен controller");
                Assert.IsFalse(animator.applyRootMotion, path + ": включён Root Motion");

                Assert.IsNotNull(prefab.GetComponent<Mech>(), path + ": нет логики Mech");
                Assert.IsNotNull(
                    prefab.GetComponent<CharacterController>(), path + ": нет капсулы");
                Assert.IsNotNull(
                    prefab.GetComponent<RideCameraProfile>(), path + ": нет профиля облёта");
                Assert.IsNotNull(prefab.GetComponent<RiderCockpit>(), path + ": нет кокпита");

                var scan = prefab.GetComponent<ScanTarget>();
                Assert.IsNotNull(scan, path + ": ходун не сканируется");
                Assert.AreEqual("WALKER", scan.Category, path + ": категория сканера");
            }
        }

        /// <summary>
        /// Якоря наездника стоят по числам манифеста. Якоря — дети <c>Seat</c>,
        /// а сам <c>Seat</c> едет за костью корпуса, поэтому его смещение от кости
        /// тоже проверяется: после пересборки FBX кость могла сдвинуться.
        /// </summary>
        [Test]
        public void Prefab_AnchorsMatchManifest()
        {
            foreach (var unit in Mechs())
            {
                var path = PrefabFolder + "/ME_" + unit + "_Model.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var expectation = For(unit);

                var seat = FindAnchor(prefab, "Seat");
                Assert.IsNotNull(seat, path + ": нет якоря Seat");
                Assert.AreEqual(expectation.Seat.y, seat.position.y, 0.01f,
                    path + ": высота сиденья разошлась с манифестом");

                var seatPoint = FindAnchor(prefab, "Seat Point");
                Assert.IsNotNull(seatPoint, path + ": нет якоря Seat Point");
                Assert.AreEqual(expectation.SeatPoint.y, seatPoint.position.y, 0.01f,
                    path + ": точка таза разошлась с манифестом");

                foreach (var (name, expected, sign) in new[]
                         {
                             ("Grip L", expectation.Grip, -1f),
                             ("Grip R", expectation.Grip, 1f),
                         })
                {
                    var anchor = FindAnchor(prefab, name);
                    Assert.IsNotNull(anchor, path + ": нет якоря " + name);
                    Assert.AreEqual(sign * expected.x, anchor.position.x, 0.01f,
                        path + ": " + name + " по X");
                    Assert.AreEqual(expected.y, anchor.position.y, 0.01f,
                        path + ": " + name + " по Y");
                    Assert.AreEqual(expected.z, anchor.position.z, 0.01f,
                        path + ": " + name + " по Z");
                }

                foreach (var (name, sign) in new[] { ("Foot L", -1f), ("Foot R", 1f) })
                {
                    var anchor = FindAnchor(prefab, name);
                    Assert.IsNotNull(anchor, path + ": нет якоря " + name);
                    Assert.AreEqual(sign * expectation.Foot.x, anchor.position.x, 0.01f,
                        path + ": " + name + " по X");
                    Assert.AreEqual(expectation.Foot.y, anchor.position.y, 0.01f,
                        path + ": " + name + " по Y");
                    Assert.AreEqual(expectation.Foot.z, anchor.position.z, 0.01f,
                        path + ": " + name + " по Z");
                }

                Assert.IsNotNull(FindAnchor(prefab, "ExitPoint"), path + ": нет ExitPoint");

                // Смещение сиденья от кости считается в `MechSeat.Awake`, а не
                // лежит в префабе: rest-поза костей в FBX слегка гуляет от
                // пересборки к пересборке, и записанное число устаревало бы
                // после каждого прогона пайплайна. Проверяется то, что должно
                // быть в префабе: ссылка на кость корпуса.
                var seatComp = seat.GetComponent<MechSeat>();
                Assert.IsNotNull(seatComp, path + ": на Seat нет MechSeat");
                Assert.IsNotNull(seatComp.Chest, path + ": MechSeat не знает кость корпуса");
                CollectionAssert.Contains(
                    new[] { "Chest", "Torso" }, seatComp.Chest.name,
                    path + ": MechSeat смотрит не на кость корпуса");
            }
        }

        /// <summary>
        /// Клипы FBX: три роли, зациклены, in-place. Ход корня снят генератором
        /// (`mech_lib.flatten_root`), иначе ходун уезжал бы из-под наездника.
        /// </summary>
        [Test]
        public void Model_HasThreeInPlaceClips()
        {
            foreach (var unit in Mechs())
            {
                var path = ModelFolder + "/ME_" + unit + ".fbx";
                var expectation = For(unit);
                var clips = AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<AnimationClip>()
                    .Where(clip => !clip.name.StartsWith("__preview"))
                    .GroupBy(clip => clip.name)
                    .ToDictionary(group => group.Key, group => group.First());

                foreach (var state in States)
                    Assert.IsTrue(clips.ContainsKey(state), path + ": нет клипа " + state);

                Assert.AreEqual(expectation.IdleSeconds, clips["Idle"].length, 0.02f,
                    path + ": длина Idle");
                Assert.AreEqual(expectation.WalkSeconds, clips["Walk"].length, 0.02f,
                    path + ": длина Walk");
                Assert.AreEqual(expectation.RunSeconds, clips["Run"].length, 0.02f,
                    path + ": длина Run");

                foreach (var state in States)
                {
                    Assert.IsTrue(
                        AnimationUtility.GetAnimationClipSettings(clips[state]).loopTime,
                        path + ": клип " + state + " должен зацикливаться");
                    Assert.IsFalse(
                        clips[state].hasRootCurves,
                        path + ": клип " + state + " должен быть in-place");
                }
            }
        }

        /// <summary>
        /// Контроллер ходуна: три состояния и два параметра. Состояние `Run`
        /// включается по `Running`, а не по скорости, — параметр один на все
        /// ходуны, поэтому переключение одинаковое.
        /// </summary>
        [Test]
        public void Controller_HasWalkStatesAndParameters()
        {
            foreach (var unit in Mechs())
            {
                var path = PrefabFolder + "/ME_" + unit + "_Model.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var animator = prefab.GetComponent<Animator>();
                var controller = animator.runtimeAnimatorController as AnimatorController;
                Assert.IsNotNull(controller, path + ": controller не AnimatorController");

                var stateNames = controller.layers[0].stateMachine.states
                    .Select(state => state.state.name)
                    .ToArray();
                foreach (var state in States)
                    CollectionAssert.Contains(stateNames, state, path + ": нет состояния " + state);

                var parameterNames = controller.parameters
                    .Select(parameter => parameter.name)
                    .ToArray();
                CollectionAssert.Contains(parameterNames, "Moving", path + ": нет Moving");
                CollectionAssert.Contains(parameterNames, "Running", path + ": нет Running");

                Assert.AreEqual("Idle",
                    controller.layers[0].stateMachine.defaultState.name,
                    path + ": стартовое состояние не Idle");
            }
        }
    }
}

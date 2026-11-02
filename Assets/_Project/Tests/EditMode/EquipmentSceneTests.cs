using System.Reflection;
using FrostSpace.Features.Inventory;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Страховка на проводку окна экипировки в сцене.
    ///
    /// Экран строит интерфейс кодом и берёт снаряжение, локомоцию, биндер визуала,
    /// риг и меню паузы из сериализованных ссылок. Если кто-то из них в сцене
    /// обнулится, окно молча не откроется: проверка `TryOpen` на пустой ссылке
    /// просто вернёт false, и в консоли не будет ни ошибки, ни намёка. Так что
    /// расхождение «код готов, сцена разобрана» ловится здесь, а не в Play Mode.
    ///
    /// Тест открывает сцену только если терять нечего — по образцу
    /// <see cref="FrontierSceneSyncTests"/>.
    /// </summary>
    public sealed class EquipmentSceneTests
    {
        private const string ScenePath = "Assets/Scenes/Frontier.unity";

        private static readonly BindingFlags Fields =
            BindingFlags.NonPublic | BindingFlags.Instance;

        [Test]
        public void Frontier_HasEquipmentScreen_Wired()
        {
            var original = EditorSceneManager.GetActiveScene();
            if (original.isDirty && original.path != ScenePath)
                Assert.Ignore(
                    "активная сцена " + original.name + " не сохранена — "
                    + "открывать Frontier значит затерять правки");

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            try
            {
                Assert.That(scene.IsValid(), Is.True, "сцена не открылась");

                var screen = Object.FindAnyObjectByType<EquipmentScreen>();
                Assert.That(screen, Is.Not.Null,
                    "в Frontier нет EquipmentScreen — окно по I не откроется");

                var type = screen.GetType();
                string[] required = { "_equipment", "_locomotion", "_binder", "_rig", "_font" };
                for (var i = 0; i < required.Length; i++)
                {
                    var value = Read(type, screen, required[i]);
                    Assert.That(value, Is.Not.Null,
                        required[i] + " не назначен в сцене");
                }

                var equipment = screen.GetComponent<FrostSpace.Features.Story.PlayerEquipment>();
                Assert.That(equipment, Is.Not.Null,
                    "на актёре с окном нет PlayerEquipment");

                var cells = (int)Read(equipment.GetType(), equipment, "_cellCount");
                Assert.That(cells, Is.GreaterThan(0), "сетка инвентаря пуста");
                Assert.That(cells % 8, Is.EqualTo(0),
                        "ячеек " + cells + " — не укладываются в восемь колонок");
                Assert.That(equipment.Board.CellCount, Is.EqualTo(cells),
                    "доска снаряжения не совпадает с сериализованным числом ячеек");
            }
            finally
            {
                if (original.IsValid() && original.path != ScenePath)
                    EditorSceneManager.OpenScene(original.path, OpenSceneMode.Single);
            }
        }

        private static object Read(System.Type type, Object target, string name)
        {
            var field = type.GetField(name, Fields);
            Assert.That(field, Is.Not.Null, "нет сериализованного поля " + name);
            return field.GetValue(target);
        }
    }
}

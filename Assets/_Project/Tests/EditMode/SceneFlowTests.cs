using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Проверяет сюжетный поток сцен (ADR 0026) по Build Settings: порядок
    /// индексов и наличие сцен, в которые ведут переходы кода.
    /// </summary>
    public sealed class SceneFlowTests
    {
        private static readonly string[] ExpectedScenes =
            { "Boot", "MainMenu", "CharacterSetup", "Train", "Frontier", "Game" };

        private static readonly string[] StoryTargets =
            { "CharacterSetup", "Train", "Frontier" };

        private static string[] EnabledScenePaths() =>
            EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

        [Test]
        public void BuildSettings_FollowStoryFlow()
        {
            var scenes = EnabledScenePaths();

            Assert.AreEqual(ExpectedScenes.Length, scenes.Length,
                "Состав Build Settings разошёлся с потоком ADR 0026");
            for (var i = 0; i < ExpectedScenes.Length; i++)
            {
                StringAssert.EndsWith($"/{ExpectedScenes[i]}.unity", scenes[i],
                    $"Индекс {i} должен быть сценой {ExpectedScenes[i]}");
            }
        }

        [Test]
        public void StoryTargets_AreInBuildSettings()
        {
            var scenes = EnabledScenePaths();

            for (var i = 0; i < StoryTargets.Length; i++)
            {
                var name = StoryTargets[i];
                Assert.IsTrue(
                    scenes.Any(path => path.EndsWith($"/{name}.unity")),
                    $"Сцена {name} недоступна переходу: её нет в Build Settings");
            }
        }

        [Test]
        public void FrontierFollowsTrain()
        {
            var scenes = EnabledScenePaths();
            var train = System.Array.FindIndex(scenes, p => p.EndsWith("/Train.unity"));
            var frontier = System.Array.FindIndex(scenes, p => p.EndsWith("/Frontier.unity"));

            Assert.GreaterOrEqual(train, 0);
            Assert.AreEqual(train + 1, frontier, "Депо должно идти сразу за вагоном");
        }
    }
}

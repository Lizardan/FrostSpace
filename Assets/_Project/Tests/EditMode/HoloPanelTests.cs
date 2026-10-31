using FrostSpace.Features.Interior;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Вёрстка голопанели: крупный кириллический текст, равномерный масштаб
    /// и отсутствие колонок из пробелов. Проверяется префаб, а не сцена.
    /// </summary>
    public sealed class HoloPanelTests
    {
        private const string PrefabPath =
            "Assets/_Project/Content/Prefabs/Props/HoloPanel.prefab";

        private const string FontPath =
            "Assets/_Project/Content/Fonts/LiberationSans Cyrillic SDF.asset";

        [Test]
        public void Prefab_HasUniformScaleAndSeparateScreenQuad()
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(root, "нет префаба HoloPanel");

            Transform screen = root.transform.Find("Screen");
            Assert.IsNotNull(screen, "нет Screen");
            Assert.AreEqual(Vector3.one, screen.localScale);

            Transform quad = screen.Find("ScreenQuad");
            Assert.IsNotNull(quad, "нет ScreenQuad");
            Assert.IsNotNull(quad.GetComponent<MeshRenderer>(), "у ScreenQuad нет рендерера");
            Assert.IsNull(screen.GetComponent<MeshRenderer>(),
                "рендерер остался на Screen — глифы снова растянутся");
        }

        [Test]
        public void Body_UsesCyrillicFontAndReadableSize()
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Transform body = root.transform.Find("Screen/Body");
            Assert.IsNotNull(body, "нет Body");

            TMP_Text tmp = body.GetComponent<TMP_Text>();
            TMP_FontAsset cyr = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            Assert.IsNotNull(cyr, "нет кириллического шрифта");
            Assert.AreSame(cyr, tmp.font);

            Assert.That(tmp.fontSize, Is.GreaterThanOrEqualTo(1.0f));
            Assert.AreEqual(TextAlignmentOptions.MidlineLeft, tmp.alignment);
            Assert.AreEqual(TextOverflowModes.Overflow, tmp.overflowMode);
        }

        [Test]
        public void Messages_HaveFourLinesAndNoSpaceColumns()
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            HoloPanel panel = root.GetComponent<HoloPanel>();
            Assert.IsNotNull(panel, "на корне нет HoloPanel");

            SerializedObject serialized = new SerializedObject(panel);
            SerializedProperty messages = serialized.FindProperty("_messages");
            Assert.IsNotNull(messages, "нет массива _messages");
            Assert.That(messages.arraySize, Is.GreaterThan(0));

            for (int i = 0; i < messages.arraySize; i++)
            {
                string text = messages.GetArrayElementAtIndex(i).stringValue;
                Assert.AreEqual(4, text.Split('\n').Length,
                    "сообщение " + i + " должно быть из четырёх строк");
                Assert.IsFalse(text.Contains("   "),
                    "в сообщении " + i + " колонки набиты пробелами");
            }
        }

        [Test]
        public void ScreenReference_PointsToScreenQuadRenderer()
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            HoloPanel panel = root.GetComponent<HoloPanel>();
            Assert.IsNotNull(panel, "на корне нет HoloPanel");

            Transform quad = root.transform.Find("Screen/ScreenQuad");
            Assert.IsNotNull(quad, "нет ScreenQuad");

            SerializedObject serialized = new SerializedObject(panel);
            SerializedProperty screen = serialized.FindProperty("_screen");
            Assert.IsNotNull(screen, "нет поля _screen");
            Assert.AreSame(quad.GetComponent<MeshRenderer>(), screen.objectReferenceValue);
        }
    }
}

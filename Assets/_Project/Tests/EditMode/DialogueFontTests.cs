using System.Collections.Generic;
using System.Linq;
using FrostSpace.Features.Dialogue;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Охраняет шрифт диалогов: кириллица должна быть в атласе до сборки, иначе в
    /// WebGL получится «тофу». Настройки повторяют ADR 0025: динамический атлас
    /// 2048², SDFAA 86 pt, предзаполнение ASCII + кириллицы и очистка данных при
    /// сборке выключена.
    /// </summary>
    public sealed class DialogueFontTests
    {
        private const string FontPath =
            "Assets/_Project/Content/Fonts/LiberationSans Cyrillic SDF.asset";
        private const string SourceFontPath = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";

        private TMP_FontAsset _font;

        [SetUp]
        public void SetUp()
        {
            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        }

        [Test]
        public void Font_Exists()
        {
            Assert.IsNotNull(_font, $"Нет шрифта диалогов по пути {FontPath}.");
        }

        [Test]
        public void Font_IsBuiltFromProjectTrueType()
        {
            Assert.IsNotNull(_font);
            Assert.IsNotNull(_font.sourceFontFile, "Шрифт должен ссылаться на TTF, а не на другой SDF.");
            Assert.AreEqual("LiberationSans", _font.sourceFontFile.name);
            Assert.IsNotNull(
                AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath),
                "Исходный TTF должен лежать в проекте: по нему пересобирается атлас.");
        }

        [Test]
        public void Font_IsDynamicWithMultiAtlas()
        {
            Assert.IsNotNull(_font);
            Assert.AreEqual(AtlasPopulationMode.Dynamic, _font.atlasPopulationMode);
            Assert.IsTrue(_font.isMultiAtlasTexturesEnabled);
        }

        [Test]
        public void Font_MatchesAtlasSettings()
        {
            Assert.IsNotNull(_font);
            Assert.AreEqual(86, _font.faceInfo.pointSize);
            Assert.AreEqual(9, _font.atlasPadding);
            Assert.IsNotEmpty(_font.atlasTextures);
            Assert.AreEqual(2048, _font.atlasTextures[0].width);
            Assert.AreEqual(2048, _font.atlasTextures[0].height);
        }

        [Test]
        public void Font_KeepsPrefilledGlyphsOnBuild()
        {
            Assert.IsNotNull(_font);

            var serialized = new SerializedObject(_font);
            var clear = serialized.FindProperty("m_ClearDynamicDataOnBuild");
            Assert.IsNotNull(clear, "У TMP-шрифта нет поля m_ClearDynamicDataOnBuild.");
            Assert.IsFalse(clear.boolValue, "Атлас не должен очищаться при сборке WebGL.");
        }

        [Test]
        public void Font_ContainsCyrillicAndPunctuation()
        {
            Assert.IsNotNull(_font);

            foreach (var symbol in "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя«»—–…№")
            {
                Assert.IsTrue(
                    _font.HasCharacter(symbol),
                    $"В атласе нет «{symbol}»: в диалоге будет пустой квадрат.");
            }
        }

        [Test]
        public void Font_CoversEveryCharacterUsedByDialogues()
        {
            Assert.IsNotNull(_font);

            var missing = new HashSet<char>();
            foreach (var guid in AssetDatabase.FindAssets(
                         "t:DialogueAsset", new[] { "Assets/_Project/Content/Data/Dialogues" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<DialogueAsset>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null)
                    continue;

                foreach (var node in asset.Nodes)
                {
                    if (node == null)
                        continue;

                    Collect(node.Line, missing);
                    foreach (var choice in node.Choices)
                        Collect(choice.Text, missing);
                }
            }

            Assert.IsEmpty(
                missing, $"Символов нет в атласе: {string.Join(" ", missing.Select(c => $"«{c}»"))}");
        }

        private void Collect(string text, HashSet<char> missing)
        {
            if (string.IsNullOrEmpty(text))
                return;

            foreach (var symbol in text)
            {
                if (symbol != '\n' && symbol != '\r' && !_font.HasCharacter(symbol))
                    missing.Add(symbol);
            }
        }
    }
}

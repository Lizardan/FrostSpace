using FrostSpace.Features.Character;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Выбор корпуса между сценами: каталог, PlayerPrefs и сброс сессии.
    /// Профили создаются в памяти и заполняются через SerializedObject —
    /// тест не зависит от ассетов, которые появятся вместе со сценой.
    /// </summary>
    public sealed class CharacterSelectionTests
    {
        private const string Key = "FrostSpace.Character";

        private RobotProfile _mechanic;
        private RobotProfile _courier;
        private CharacterCatalog _catalog;
        private bool _hadSelection;
        private string _savedSelection;

        [SetUp]
        public void SetUp()
        {
            _hadSelection = PlayerPrefs.HasKey(Key);
            _savedSelection = PlayerPrefs.GetString(Key);
            PlayerPrefs.DeleteKey(Key);
            CharacterSelection.Clear();

            _mechanic = CreateProfile("R1_PolarMechanic");
            _courier = CreateProfile("R15_Courier");
            _catalog = CreateCatalog(_mechanic, _mechanic, _courier);
        }

        [TearDown]
        public void TearDown()
        {
            CharacterSelection.Clear();
            if (_hadSelection)
                PlayerPrefs.SetString(Key, _savedSelection);
            else
                PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();

            Object.DestroyImmediate(_catalog);
            Object.DestroyImmediate(_mechanic);
            Object.DestroyImmediate(_courier);
        }

        [Test]
        public void Catalog_FindsProfileById()
        {
            Assert.AreSame(_courier, _catalog.Find("R15_Courier"));
            Assert.AreSame(_mechanic, _catalog.Find("R1_PolarMechanic"));
        }

        [Test]
        public void Catalog_UnknownOrEmptyId_ReturnsNull()
        {
            Assert.IsNull(_catalog.Find("R99_Unknown"));
            Assert.IsNull(_catalog.Find(null));
            Assert.IsNull(_catalog.Find(string.Empty));
        }

        [Test]
        public void Catalog_DefaultFallsBackToFirstProfile()
        {
            var fallback = CreateCatalog(null, _courier, _mechanic);

            try
            {
                Assert.AreSame(_courier, fallback.Default);
            }
            finally
            {
                Object.DestroyImmediate(fallback);
            }
        }

        [Test]
        public void Resolve_UsesSavedId()
        {
            PlayerPrefs.SetString(Key, "R15_Courier");

            Assert.AreSame(_courier, CharacterSelection.Resolve(_catalog));
        }

        [Test]
        public void Resolve_FallsBackToDefault_WhenSavedIdIsUnknown()
        {
            PlayerPrefs.SetString(Key, "R99_Unknown");

            Assert.AreSame(_mechanic, CharacterSelection.Resolve(_catalog));
        }

        [Test]
        public void Resolve_CachesUntilCleared()
        {
            PlayerPrefs.SetString(Key, "R1_PolarMechanic");
            Assert.AreSame(_mechanic, CharacterSelection.Resolve(_catalog));

            PlayerPrefs.SetString(Key, "R15_Courier");
            Assert.AreSame(_mechanic, CharacterSelection.Resolve(_catalog));

            CharacterSelection.Clear();
            PlayerPrefs.SetString(Key, "R15_Courier");
            Assert.AreSame(_courier, CharacterSelection.Resolve(_catalog));
        }

        [Test]
        public void Select_PersistsIdAndBecomesCurrent()
        {
            CharacterSelection.Select(_courier);

            Assert.AreSame(_courier, CharacterSelection.Current);
            Assert.AreEqual("R15_Courier", PlayerPrefs.GetString(Key, string.Empty));
            Assert.AreEqual("R15_Courier", CharacterSelection.SavedId);
        }

        [Test]
        public void Clear_RemovesSavedId()
        {
            CharacterSelection.Select(_courier);
            CharacterSelection.Clear();

            Assert.IsNull(CharacterSelection.Current);
            Assert.IsFalse(PlayerPrefs.HasKey(Key));
        }

        [Test]
        public void Profile_MultipliersAreClampedToPositive()
        {
            var profile = CreateProfile("R10_Pipewright");
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("_walkMultiplier").floatValue = -5f;
            serialized.FindProperty("_carryMultiplier").floatValue = 0f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            try
            {
                Assert.Greater(profile.WalkMultiplier, 0f);
                Assert.Greater(profile.CarryMultiplier, 0f);
                Assert.AreEqual(1f, profile.SprintMultiplier);
                Assert.AreEqual(1f, profile.JumpMultiplier);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        private static RobotProfile CreateProfile(string id)
        {
            var profile = ScriptableObject.CreateInstance<RobotProfile>();
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("_id").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }

        private static CharacterCatalog CreateCatalog(
            RobotProfile defaultProfile, params RobotProfile[] profiles)
        {
            var catalog = ScriptableObject.CreateInstance<CharacterCatalog>();
            var serialized = new SerializedObject(catalog);
            serialized.FindProperty("_default").objectReferenceValue = defaultProfile;

            var array = serialized.FindProperty("_profiles");
            array.arraySize = profiles.Length;
            for (var i = 0; i < profiles.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = profiles[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return catalog;
        }
    }
}

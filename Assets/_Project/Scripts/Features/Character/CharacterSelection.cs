using UnityEngine;

namespace FrostSpace.Features.Character
{
    /// <summary>
    /// Выбранный корпус между сценами: <c>CharacterSetup</c> записывает выбор,
    /// <c>Train</c> и <c>Frontier</c> его применяют.
    ///
    /// Хранится и ссылка на профиль (на время сессии), и идентификатор в
    /// <see cref="PlayerPrefs"/> (между запусками). Если сохранённый корпус
    /// исчез из каталога, берётся <see cref="CharacterCatalog.Default"/>.
    /// </summary>
    public static class CharacterSelection
    {
        private const string Key = "FrostSpace.Character";

        private static RobotProfile _current;
        private static bool _resolved;

        /// <summary>Профиль, выбранный в этой сессии; null до <see cref="Resolve"/>.</summary>
        public static RobotProfile Current => _current;

        /// <summary>Сохранённый идентификатор; пустая строка, если выбора ещё не было.</summary>
        public static string SavedId => PlayerPrefs.GetString(Key, string.Empty);

        /// <summary>
        /// Корпус для текущей сцены. Первый вызов в сессии читает PlayerPrefs;
        /// дальше возвращается закэшированный профиль.
        /// </summary>
        public static RobotProfile Resolve(CharacterCatalog catalog)
        {
            if (_resolved)
                return _current;

            _resolved = true;
            _current = catalog != null ? catalog.Find(SavedId) : null;
            if (_current == null && catalog != null)
                _current = catalog.Default;

            return _current;
        }

        /// <summary>Выбор игрока: запоминается и переживает перезапуск.</summary>
        public static void Select(RobotProfile profile)
        {
            if (profile == null)
            {
                Clear();
                return;
            }

            _current = profile;
            _resolved = true;
            PlayerPrefs.SetString(Key, profile.Id);
            PlayerPrefs.Save();
        }

        /// <summary>Сброс выбора: тесты и «создать заново».</summary>
        public static void Clear()
        {
            _current = null;
            _resolved = false;
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Domain reload выключен: без сброса второй запуск Play Mode взял бы
        /// профиль из прошлого прогона. PlayerPrefs при этом не трогаем.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _current = null;
            _resolved = false;
        }
    }
}

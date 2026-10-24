using System;
using UnityEngine;

namespace FrostSpace.Core
{
    /// <summary>
    /// Пользовательские настройки игры: музыка и два канала детекции сканера
    /// (ближайший объект и объект в области прицела).
    ///
    /// Значения лежат в <c>PlayerPrefs</c> и кэшируются в статике: читать диск на каждый
    /// кадр незачем, а меню паузы и сканер должны видеть одно и то же состояние. Запись
    /// идёт сразу при изменении — отдельной кнопки «сохранить» нет, как и в настройках
    /// главного меню, где громкость пишется тем же способом.
    ///
    /// Domain reload выключен, поэтому статика живёт между запусками Play Mode и обязана
    /// сбрасываться в <see cref="ResetStatics"/>; иначе второй запуск начался бы со
    /// значений прошлой сессии, а подписчики <see cref="Changed"/> остались бы от
    /// уничтоженных объектов.
    /// </summary>
    public static class GameSettings
    {
        /// <summary>Музыка включена (1) или выключена (0).</summary>
        public const string MusicKey = "settings.music";

        /// <summary>Детекция ближайшего объекта включена (1) или выключена (0).</summary>
        public const string NearestKey = "settings.detect.nearest";

        /// <summary>Детекция объекта в области прицела включена (1) или выключена (0).</summary>
        public const string AimKey = "settings.detect.aim";

        private static bool _loaded;
        private static bool _music = true;
        private static bool _nearest = true;
        private static bool _aim = true;

        /// <summary>Настройка изменилась: подписчики применяют новое значение сразу.</summary>
        public static event Action Changed;

        /// <summary>
        /// Сброс статики между запусками Play Mode: домен не перезагружается, поэтому
        /// без этого значения и подписчики пережили бы сцену.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _loaded = false;
            _music = true;
            _nearest = true;
            _aim = true;
            Changed = null;
        }

        /// <summary>Музыка: выключение глушит только трек, общая громкость не меняется.</summary>
        public static bool MusicEnabled
        {
            get
            {
                EnsureLoaded();
                return _music;
            }
            set => Write(MusicKey, ref _music, value);
        }

        /// <summary>Подписывать ближайший объект автоматически.</summary>
        public static bool NearestDetection
        {
            get
            {
                EnsureLoaded();
                return _nearest;
            }
            set => Write(NearestKey, ref _nearest, value);
        }

        /// <summary>Подписывать объект в области прицела автоматически.</summary>
        public static bool AimDetection
        {
            get
            {
                EnsureLoaded();
                return _aim;
            }
            set => Write(AimKey, ref _aim, value);
        }

        /// <summary>Сбрасывает все три настройки в состояние «включено».</summary>
        public static void ResetToDefaults()
        {
            MusicEnabled = true;
            NearestDetection = true;
            AimDetection = true;
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _loaded = true;
            _music = PlayerPrefs.GetInt(MusicKey, 1) != 0;
            _nearest = PlayerPrefs.GetInt(NearestKey, 1) != 0;
            _aim = PlayerPrefs.GetInt(AimKey, 1) != 0;
        }

        private static void Write(string key, ref bool field, bool value)
        {
            EnsureLoaded();
            if (field == value)
                return;

            field = value;
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}

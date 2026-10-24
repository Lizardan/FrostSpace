using UnityEngine;

namespace FrostSpace.Core
{
    /// <summary>
    /// Фоновая музыка, живущая вне сцен: объект поднимается сам при запуске любой сцены и
    /// уходит в <c>DontDestroyOnLoad</c>, поэтому трек не обрывается и не начинается заново
    /// при переходах Boot → MainMenu → Game. Клип зациклен, поэтому по окончании он
    /// продолжается с начала без участия сцен.
    ///
    /// Клип берётся из <c>Resources/Music/Music_FrostSpaceTheme</c>: путь, а не ссылка в
    /// сцене, из-за того, что объект создаётся кодом и не принадлежит ни одной сцене.
    /// Общая громкость по-прежнему задаётся <c>AudioListener.volume</c> в настройках меню.
    ///
    /// Выключение музыки — отдельный флаг <see cref="GameSettings.MusicEnabled"/>: он
    /// глушит только этот источник (<c>AudioSource.mute</c>) и не трогает общую громкость,
    /// поэтому эффекты и интерфейс остаются слышны. Менеджер подписан на изменения
    /// настроек и применяет их сразу — без перезапуска сцены.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MusicManager : MonoBehaviour
    {
        /// <summary>Путь клипа внутри папки Resources, без расширения.</summary>
        private const string ClipResourcePath = "Music/Music_FrostSpaceTheme";

        /// <summary>
        /// Ключ PlayerPrefs общей громкости. Пишет меню, а читает менеджер один раз при
        /// старте — до первого звука, чтобы громкость не менялась посреди перехода.
        /// </summary>
        public const string MasterVolumeKey = "audio.master";

        [Header("Настройки")]
        [Tooltip("Громкость музыки 0..1. Общая громкость игры задаётся AudioListener.volume в настройках меню.")]
        [SerializeField, Range(0f, 1f)] private float _volume = 0.5f;

        [Tooltip("Плавное появление трека при старте, секунды. 0 — сразу на полной громкости.")]
        [SerializeField] private float _fadeInSeconds = 2f;

        private static MusicManager _instance;

        /// <summary>Персистентный менеджер музыки; null только до его создания.</summary>
        public static MusicManager Instance => _instance;

        private AudioSource _source;
        private float _fadeTime;

        /// <summary>
        /// Domain reload выключен, поэтому статика живёт между запусками игры и сбрасывается
        /// здесь, а не новой сборкой домена.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
        }

        /// <summary>
        /// Запуск с любой сцены одинаков: если менеджера нет, он создаётся сам, поэтому
        /// музыка есть и при старте с <c>Boot</c>, и при тестовом запуске прямо с <c>Game</c>.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (_instance != null)
                return;

            var go = new GameObject("MusicManager");
            go.AddComponent<MusicManager>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                // Второй менеджер не нужен: трек уже играет из персистентного объекта.
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Подписка до первого звука: тумблер в меню паузы применяется сразу.
            GameSettings.Changed += ApplySettings;

            // Сохранённая общая громкость применяется один раз и до старта трека. Если делать
            // это в меню, как раньше, музыка весь Boot играет на единице и «прыгает» вниз
            // ровно в момент, когда меню применит своё значение — это и слышно на переходе
            // Boot → MainMenu.
            AudioListener.volume = Mathf.Clamp01(
                PlayerPrefs.GetFloat(MasterVolumeKey, AudioListener.volume));

            var clip = Resources.Load<AudioClip>(ClipResourcePath);
            if (clip == null)
            {
                Debug.LogWarning("MusicManager: клип не найден по пути Resources/" + ClipResourcePath, this);
                return;
            }

            _source = gameObject.AddComponent<AudioSource>();
            _source.clip = clip;
            _source.loop = true;
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.volume = _fadeInSeconds > 0f ? 0f : _volume;
            _fadeTime = _fadeInSeconds > 0f ? 0f : 1f;
            ApplySettings();
            _source.Play();
        }

        private void OnDestroy()
        {
            GameSettings.Changed -= ApplySettings;
        }

        /// <summary>
        /// Глушит источник по сохранённой настройке. Отдельно от <see cref="Volume"/>:
        /// mute не меняет громкость трека, поэтому выключение и включение музыки не
        /// сбрасывает ползунок общей громкости.
        /// </summary>
        private void ApplySettings()
        {
            if (_source != null)
                _source.mute = !GameSettings.MusicEnabled;
        }

        /// <summary>Музыка сейчас заглушена настройкой.</summary>
        public bool Muted => _source != null ? _source.mute : !GameSettings.MusicEnabled;

        private void Update()
        {
            if (_source == null || _fadeTime >= 1f)
                return;

            _fadeTime = Mathf.Clamp01(_fadeTime + Time.unscaledDeltaTime / Mathf.Max(_fadeInSeconds, 0.0001f));
            _source.volume = Mathf.Lerp(0f, _volume, _fadeTime);
        }

        /// <summary>Громкость музыки 0..1; установка значения прекращает плавное появление.</summary>
        public float Volume
        {
            get => _source != null ? _source.volume : _volume;
            set
            {
                _volume = Mathf.Clamp01(value);
                _fadeTime = 1f;
                if (_source != null)
                    _source.volume = _volume;
            }
        }
    }
}

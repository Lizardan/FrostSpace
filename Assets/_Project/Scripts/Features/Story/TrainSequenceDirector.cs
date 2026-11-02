using System.Collections;
using FrostSpace.Core;
using FrostSpace.Features.Dialogue;
using FrostSpace.Features.Hud;
using FrostSpace.Features.Locomotion;
using FrostSpace.Features.Quest;
using FrostSpace.Features.View;
using UnityEngine;

namespace FrostSpace.Features.Story
{
    /// <summary>
    /// Финал вагона (TR-09, TR-10): квест сдан, R13 уходит с грузом к боковому проёму
    /// и встаёт лицом к будущему выходу, кабина даёт один провал света, издалека входят
    /// дальние тормоза — звук прибытия начинается ещё до склейки. Камера при этом почти
    /// стоит: только микро-крейп на одной кривой, без «уезда в окно». Дальше кадр уходит
    /// в чёрный — и чёрным владеет один <see cref="SceneController"/>.
    ///
    /// Поезд здесь не останавливается: прибытие — событие `Frontier`, а параллакс
    /// продолжает идти под занавесом, поэтому темнота не выглядит зависанием.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-20)]
    public sealed class TrainSequenceDirector : MonoBehaviour
    {
        [Header("Ссылки")]
        [SerializeField] private QuestDirector _quest;
        [SerializeField] private DialogueController _dialogue;
        [SerializeField] private RobotLocomotion _locomotion;
        [SerializeField] private PlayerCameraRig _rig;
        [SerializeField] private TitleCardScreen _titleCard;
        [Tooltip("Полоса опыта: на время финала интерфейс не висит поверх кадра")]
        [SerializeField] private ExperienceBar _xpBar;
        [Tooltip("R13: после сдачи уходит к проёму — доставка видна, а не рассказана")]
        [SerializeField] private Transform _r13;
        [SerializeField] private Animator _r13Animator;
        [Tooltip("Шаги кабины: один провал света = сменилось состояние машины")]
        [SerializeField] private Light[] _lamps = new Light[0];

        [Header("Финал")]
        [SerializeField] private string _nextScene = "Frontier";
        [Tooltip("Куда уходит R13, мир. Лицом к боковому проёму")]
        [SerializeField] private Vector3 _r13WalkTo = new(2.6f, 0f, 1.4f);
        [Tooltip("Сколько R13 идёт, с")]
        [SerializeField] private float _r13Seconds = 3.2f;
        [Tooltip("Скорость шага клипа Walk, м/с")]
        [SerializeField] private float _r13ClipSpeed = 0.912f;
        [Tooltip("Насколько камера наезжает за финал, м. Микро-крейп, а не маршрут")]
        [SerializeField] private float _creepMeters = 0.6f;
        [Tooltip("Сколько длится наезд, с")]
        [SerializeField] private float _creepSeconds = 2.0f;
        [Tooltip("Длительность провала света кабины, с")]
        [SerializeField] private float _lampDipSeconds = 0.5f;
        [Tooltip("До какой доли яркости гаснут лампы")]
        [SerializeField, Range(0f, 1f)] private float _lampDipLevel = 0.25f;
        [Tooltip("Сколько держим последний кадр до занавеса, с")]
        [SerializeField] private float _holdSeconds = 0.8f;

        [Header("Звук прибытия заранее")]
        [Tooltip("Дальние тормоза: входят до склейки, а не после")]
        [SerializeField] private AudioClip _brakes;
        [SerializeField] private AudioClip _stopThud;
        [SerializeField, Range(0f, 1f)] private float _farVolume = 0.3f;

        [Header("Титул")]
        [Tooltip("За сколько титул проявляется из темноты")]
        [SerializeField] private float _titleFadeIn = 0.35f;
        [Tooltip("За сколько титул гаснет после активации депо. Равен входу")]
        [SerializeField] private float _titleFadeOut = 0.35f;
        [Tooltip("Сколько ждём активацию новой сцены, прежде чем гасить титул принудительно")]
        [SerializeField] private float _sceneWaitTimeout = 6f;

        [Header("Занавес")]
        [Tooltip("Цвет занавеса на переходе в депо: единственный чёрный этого кадра")]
        [SerializeField] private Color _curtainColor = new(0.0196f, 0.0314f, 0.0431f, 1f);

        private bool _running;
        private AudioSource _oneShotSource;

        private void Awake()
        {
            if (_xpBar == null)
                _xpBar = FindAnyObjectByType<ExperienceBar>();
            if (_r13Animator == null && _r13 != null)
                _r13Animator = _r13.GetComponentInChildren<Animator>(true);

            _oneShotSource = gameObject.AddComponent<AudioSource>();
            _oneShotSource.playOnAwake = false;
            _oneShotSource.spatialBlend = 0f;
        }

        private void OnEnable()
        {
            if (_quest != null)
                _quest.QuestCompleted += OnQuestCompleted;
        }

        private void OnDisable()
        {
            if (_quest != null)
                _quest.QuestCompleted -= OnQuestCompleted;
        }

        private void OnQuestCompleted(QuestLog log)
        {
            if (_running)
                return;

            StartCoroutine(PlayFinale());
        }

        private IEnumerator PlayFinale()
        {
            _running = true;

            // Финальный узел диалога ещё открыт в момент CompleteQuest.
            while (_dialogue != null && _dialogue.IsOpen)
                yield return null;

            _locomotion?.SetControlLock(true);
            _rig?.SetCinematic(true);
            _xpBar?.SetVisible(false, 0.4f);

            // Музыка уходит в ноль на всё прибытие: в депо работают ветер и металл,
            // а тема вступает ровно в момент передачи управления.
            MusicManager.Instance?.SetDuck(0f, 1.5f);

            var camera = _rig != null ? _rig.ViewCamera : Camera.main;
            if (camera == null)
            {
                Debug.LogError("TrainSequenceDirector: нет камеры для финала", this);
                _running = false;
                yield break;
            }

            // Движение кадра — в субъекте и в мире: R13 уходит к проёму, кабина
            // гаснет на мгновение, издалека входят тормоза. Камера только наезжает
            // на полметра по одной кривой и больше ничего не делает.
            StartCoroutine(ExitR13());
            StartCoroutine(LampDipRoutine());
            PlayFar(_brakes, 0.8f, 0.72f);

            yield return Creep(camera);

            // Последний кадр держится неподвижно: без паузы занавес закрывается на
            // середине движения и переход читается обрывом, а не концом.
            if (_holdSeconds > 0f)
                yield return new WaitForSecondsRealtime(_holdSeconds);

            PlayFar(_stopThud, 1f, 0.6f);

            var controller = SceneController.Instance;
            if (controller == null)
            {
                Debug.LogError("TrainSequenceDirector: нет SceneController", this);
                _running = false;
                yield break;
            }

            // Один владелец темноты: занавес уходит в чёрный за FadeOutSeconds и ровно
            // за столько же возвращает кадр. Титул — только текст, снег и гул поверх.
            controller.LoadScene(_nextScene, _curtainColor, string.Empty);
            _titleCard?.Play(_titleFadeIn);

            // Гасит титул он сам: дождаться новой сцены здесь нельзя — этот объект
            // уничтожается вместе со старой сценой, и чёрный остался бы на экране.
            _titleCard?.FadeOutWhenScene(_nextScene, _titleFadeOut, _sceneWaitTimeout);
            _running = false;
        }

        /// <summary>
        /// Микро-крейп вдоль текущего взгляда: поворот и поле зрения не меняются,
        /// поэтому наезда хватает, чтобы кадр «дышал», но не хватает на скачок скорости.
        /// </summary>
        private IEnumerator Creep(Camera camera)
        {
            var cameraTransform = camera.transform;
            var start = cameraTransform.position;
            var delta = cameraTransform.forward * _creepMeters;
            var rotation = cameraTransform.rotation;
            var elapsed = 0f;

            while (elapsed < _creepSeconds)
            {
                elapsed += Time.deltaTime;
                cameraTransform.position =
                    start + delta * CameraTrack.Ease(Mathf.Clamp01(elapsed / _creepSeconds));
                cameraTransform.rotation = rotation;
                yield return null;
            }

            cameraTransform.position = start + delta;
        }

        private IEnumerator ExitR13()
        {
            if (_r13 == null)
                yield break;

            yield return CutsceneMover.Walk(_r13,
                new[] { _r13.position, _r13WalkTo },
                _r13Seconds, _r13Animator, _r13ClipSpeed);
        }

        /// <summary>Один провал света кабины туда и обратно, без мигалки.</summary>
        private IEnumerator LampDipRoutine()
        {
            if (_lamps.Length == 0 || _lampDipSeconds <= 0f)
                yield break;

            var from = new float[_lamps.Length];
            for (var i = 0; i < _lamps.Length; i++)
                from[i] = _lamps[i] != null ? _lamps[i].intensity : 0f;

            var elapsed = 0f;
            var half = _lampDipSeconds * 0.5f;

            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                var k = CameraTrack.Ease(Mathf.Clamp01(elapsed / half));
                for (var i = 0; i < _lamps.Length; i++)
                    if (_lamps[i] != null)
                        _lamps[i].intensity = Mathf.LerpUnclamped(
                            from[i], from[i] * _lampDipLevel, k);
                yield return null;
            }

            while (elapsed < _lampDipSeconds)
            {
                elapsed += Time.deltaTime;
                var k = CameraTrack.Ease(Mathf.Clamp01((elapsed - half) / half));
                for (var i = 0; i < _lamps.Length; i++)
                    if (_lamps[i] != null)
                        _lamps[i].intensity = Mathf.LerpUnclamped(
                            from[i] * _lampDipLevel, from[i], k);
                yield return null;
            }

            for (var i = 0; i < _lamps.Length; i++)
                if (_lamps[i] != null)
                    _lamps[i].intensity = from[i];
        }

        /// <summary>Дальний звук прибытия: тихо и пониже — он за бортом, а не в вагоне.</summary>
        private void PlayFar(AudioClip clip, float volumeScale, float pitch)
        {
            if (clip == null || _oneShotSource == null)
                return;

            _oneShotSource.pitch = pitch;
            _oneShotSource.PlayOneShot(clip, _farVolume * volumeScale);
        }
    }
}

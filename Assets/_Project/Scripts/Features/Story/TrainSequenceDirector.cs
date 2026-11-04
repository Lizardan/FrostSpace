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
    /// и успевает увидеть, что тормоза не держат. Дальше кадром владеет авария
    /// (<see cref="TrainCrashRig"/>): мир тормозит, корпус падает и кренится, свет
    /// гаснет с искрой, снег бьёт внутрь, остаётся одна аварийная лампа. Затем кадр
    /// уходит в чёрный — и чёрным владеет один <see cref="SceneController"/>.
    ///
    /// Поезд не встаёт у платформы целым: он дотягивает до депо уже подбитым, и
    /// прибытие — событие `Frontier`.
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


        [Header("Финал")]
        [SerializeField] private string _nextScene = "Frontier";
        [Tooltip("Куда уходит R13, мир. Лицом к боковому проёму")]
        [SerializeField] private Vector3 _r13WalkTo = new(2.6f, 0f, 1.4f);
        [Tooltip("Сколько R13 идёт, с")]
        [SerializeField] private float _r13Seconds = 3.2f;
        [Tooltip("Скорость шага клипа Walk, м/с")]
        [SerializeField] private float _r13ClipSpeed = 0.912f;
        [Tooltip("Авария: торможение, удар, тьма. Пусто — спокойная остановка")]
        [SerializeField] private TrainCrashRig _crash;
        [Tooltip("Сколько держим последний кадр до занавеса, с")]
        [SerializeField] private float _holdSeconds = 0.8f;

        [Header("Звук прибытия заранее")]
        [Tooltip("Дальние тормоза: входят до склейки, а не после")]
        [SerializeField] private AudioClip _brakes;
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

            // R13 успевает дойти до проёма и увидеть, что тормоза не держат.
            // Дальше кадром владеет авария: она же тормозит мир за окнами.
            StartCoroutine(ExitR13());
            PlayFar(_brakes, 0.9f, 0.72f);

            if (_crash != null)
                yield return _crash.Play(camera);
            else
                yield return new WaitForSecondsRealtime(1.5f);

            // Последний кадр держится неподвижно: без паузы занавес закрывается на
            // середине движения и переход читается обрывом, а не концом.
            if (_holdSeconds > 0f)
                yield return new WaitForSecondsRealtime(_holdSeconds);

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

        private IEnumerator ExitR13()
        {
            if (_r13 == null)
                yield break;

            yield return CutsceneMover.Walk(_r13,
                new[] { _r13.position, _r13WalkTo },
                _r13Seconds, _r13Animator, _r13ClipSpeed);
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

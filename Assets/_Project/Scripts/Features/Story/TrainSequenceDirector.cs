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
    /// (<see cref="TrainCrashRig"/>): мир теряет ход, но катится дальше, корпус
    /// проседает, свет гаснет с искрой, снег бьёт внутрь, остаётся одна аварийная
    /// лампа. Затем кадр уходит в чёрный — и чёрным владеет один <see cref="SceneController"/>.
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
        [SerializeField] private float _holdSeconds = 1.4f;

        [Header("Звук прибытия заранее")]
        [Tooltip("Дальние тормоза: входят до склейки, а не после")]
        [SerializeField] private AudioClip _brakes;
        [SerializeField, Range(0f, 1f)] private float _farVolume = 0.3f;

        [Header("Титул в чистом чёрном")]
        [Tooltip("Пауза в чёрном после закрытия занавеса, до титра")]
        [SerializeField] private float _blackLeadSeconds = 0.4f;
        [Tooltip("За сколько титул проявляется из темноты")]
        [SerializeField] private float _titleFadeIn = 0.6f;
        [Tooltip("Сколько титул держится на чёрном")]
        [SerializeField] private float _titleHoldSeconds = 1.8f;
        [Tooltip("За сколько титул гаснет. Весь визуал титра умирает до активации депо")]
        [SerializeField] private float _titleFadeOut = 0.5f;
        [Tooltip("Пауза в чёрном после титра, до проявления депо")]
        [SerializeField] private float _blackTailSeconds = 0.3f;
        [Tooltip("За сколько гул титра гаснет уже в депо: под него набирает громкость ветер")]
        [SerializeField] private float _droneOutSeconds = 1.2f;
        [Tooltip("Сколько ждём активацию новой сцены, прежде чем гасить гул принудительно")]
        [SerializeField] private float _sceneWaitTimeout = 6f;

        [Header("Занавес")]
        [Tooltip("Цвет занавеса на переходе в депо: единственный чёрный этого кадра")]
        [SerializeField] private Color _curtainColor = new(0.0196f, 0.0314f, 0.0431f, 1f);
        [Tooltip("За сколько открывается занавес депо. Длиннее общего проявления: " +
                 "вставка 6 с стартует здесь и смотрится целиком")]
        [SerializeField] private float _revealSeconds = 1.2f;

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
            // Дальше кадром владеет авария: она сбрасывает ход мира за окнами, но
            // не останавливает поезд — он доедет до депо.
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

            // Один владелец темноты: занавес уходит в чёрный, держит в нём титр и
            // только потом проявляет депо. Титр — текст, снег и гул; подложки у него
            // нет, иначе два независимых затемнения наложились бы друг на друга.
            System.Func<IEnumerator> black = null;
            if (_titleCard != null)
            {
                // Гул входит вместе с занавесом: пар вагона под чёрным ещё звучит,
                // поэтому гул нарастает поверх него, а не в тишину.
                _titleCard.StartDrone(controller.FadeOutSeconds);
                black = () => _titleCard.PlayInsideBlack(_nextScene, _titleFadeIn,
                    _titleHoldSeconds, _titleFadeOut, _droneOutSeconds, _sceneWaitTimeout);
            }

            // Гасить титр отсюда нельзя: этот объект уничтожается вместе со старой
            // сценой. Титр доживает гул сам — он на DontDestroyOnLoad.
            controller.LoadScene(_nextScene, _curtainColor, string.Empty,
                new SceneController.Blackout(_blackLeadSeconds, _blackTailSeconds,
                    _revealSeconds, black));
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

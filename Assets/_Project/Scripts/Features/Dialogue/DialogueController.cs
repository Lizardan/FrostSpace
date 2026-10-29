using System;
using FrostSpace.Features.Locomotion;
using FrostSpace.Features.Menu;
using FrostSpace.Features.Scan;
using FrostSpace.Features.View;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FrostSpace.Features.Dialogue
{
    /// <summary>
    /// Открывает и ведёт диалог: E у робота, реплики, варианты, Esc.
    ///
    /// Цель ищется собственным лучом из камеры в перекрестие, а не через
    /// <c>ObjectScanner.AimTarget</c>: тот зависит от тумблера AimDetection, работает
    /// только в третьем лице и с гистерезисом. Дальность луча и радиус разговора
    /// разные: смотреть можно издалека, но подойти надо ближе.
    ///
    /// Пока диалог открыт, игра не встаёт на паузу: мир и анимации идут. Стоят игрок
    /// (<see cref="RobotLocomotion.SetConversationLock"/>), камера
    /// (<see cref="PlayerCameraRig.SetPaused"/>) и ввод карты Player; пауза на Esc
    /// временно выключена, иначе тот же Esc закрыл бы диалог и открыл меню паузы.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-10)]
    public sealed class DialogueController : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Риг камеры: луч прицела, корень игрока и заморозка обзора")]
        [SerializeField] private PlayerCameraRig _rig;
        [Tooltip("Сканер: на время диалога выключается, чтобы Q не пускал волну")]
        [SerializeField] private ObjectScanner _scanner;
        [Tooltip("Меню паузы: временно выключается на время диалога")]
        [SerializeField] private PauseMenuScreen _pause;
        [Tooltip("Экран диалога")]
        [SerializeField] private DialogueScreen _screen;
        [Tooltip("Локомоция игрока: гасит ходьбу и прыжок")]
        [SerializeField] private RobotLocomotion _locomotion;
        [Tooltip("Ассет ввода; пусто — берётся InputSystem.actions")]
        [SerializeField] private InputActionAsset _actions;

        [Header("Взаимодействие")]
        [Tooltip("Слои луча из перекрестия; слой Player исключить")]
        [SerializeField] private LayerMask _blockers = ~0;
        [Tooltip("Максимальная дальность луча, м")]
        [SerializeField] private float _rayDistance = 12f;
        [Tooltip("Радиус, внутри которого с роботом можно заговорить, м")]
        [SerializeField] private float _talkRadius = 4f;

        private InputAction _interact;
        private InputActionMap _playerMap;
        private DialogueSession _session;
        private DialogueSpeaker _speaker;
        private DialogueSpeaker _target;
        private bool _open;
        private bool _pauseSuspended;
        private bool _pauseWasEnabled;
        private bool _scannerWasEnabled;
        private bool _restorePause;

        /// <summary>Диалог сейчас открыт.</summary>
        public bool IsOpen => _open;

        /// <summary>
        /// Показан узел с действием: действие и id квеста. Пустой id означает
        /// квест, назначенный сцене. Событие приходит один раз за показ узла.
        /// </summary>
        public event Action<DialogueAction, string> ActionInvoked;

        private void Awake()
        {
            if (_rig == null)
                _rig = FindAnyObjectByType<PlayerCameraRig>();
            if (_scanner == null)
                _scanner = FindAnyObjectByType<ObjectScanner>();
            if (_pause == null)
                _pause = FindAnyObjectByType<PauseMenuScreen>();
            if (_screen == null)
                _screen = GetComponentInChildren<DialogueScreen>(true);
            if (_locomotion == null && _rig != null && _rig.Target != null)
                _locomotion = _rig.Target.GetComponent<RobotLocomotion>();
            if (_locomotion == null)
                _locomotion = FindAnyObjectByType<RobotLocomotion>();

            var source = _actions != null ? _actions : InputSystem.actions;
            if (source != null)
            {
                _interact = source.FindAction("Player/Interact");
                _playerMap = source.FindActionMap("Player", false);
            }

            if (_screen == null)
            {
                Debug.LogError("DialogueController: не найден DialogueScreen", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (_screen != null)
            {
                _screen.ChoicePressed += OnChoicePressed;
                _screen.AdvancePressed += OnAdvancePressed;
            }

            _interact?.Enable();
        }

        private void OnDisable()
        {
            if (_screen != null)
            {
                _screen.ChoicePressed -= OnChoicePressed;
                _screen.AdvancePressed -= OnAdvancePressed;
            }

            if (_open)
                FinishConversation(false);
            else
                RestorePauseImmediately();

            _interact?.Disable();
        }

        private void Update()
        {
            if (_screen == null)
                return;

            if (_open)
            {
                HandleConversationInput();
                return;
            }

            _target = FindTarget();
            _screen.SetPrompt(_target != null);

            if (_target != null && _interact != null && _interact.WasPressedThisFrame())
                Open(_target);
        }

        private void LateUpdate()
        {
            // Пауза возвращается после кадра закрытия: её Update уже прошёл, поэтому
            // Esc, которым закрыли диалог, не откроет меню паузы тем же нажатием.
            if (!_restorePause)
                return;

            _restorePause = false;
            if (_pause != null)
                _pause.enabled = true;
        }

        /// <summary>Робот в перекрестии, достаточно близко и готов к разговору.</summary>
        private DialogueSpeaker FindTarget()
        {
            if (_rig == null || _rig.IsPaused || _rig.RideTarget != null)
                return null;
            if (_pause != null && _pause.IsPaused)
                return null;

            var camera = _rig.ViewCamera;
            if (camera == null)
                return null;

            var ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(
                    ray, out var hit, _rayDistance, _blockers, QueryTriggerInteraction.Ignore))
            {
                return null;
            }

            var speaker = hit.collider.GetComponentInParent<DialogueSpeaker>();
            if (speaker == null || !speaker.CanTalk)
                return null;

            var player = _rig.Target;
            if (player != null)
            {
                var offset = speaker.transform.position - player.position;
                offset.y = 0f;
                if (offset.magnitude > _talkRadius)
                    return null;
            }

            return speaker;
        }

        private void Open(DialogueSpeaker speaker)
        {
            if (_open || speaker == null || !speaker.CanTalk)
                return;
            if (_rig == null || _rig.Target == null)
                return;

            var session = speaker.Dialogue != null ? speaker.Dialogue.CreateSession() : null;
            if (session == null || session.Current == null)
                return;

            _session = session;
            _speaker = speaker;
            _open = true;
            _target = null;

            _speaker.BeginConversation(_rig.Target);

            _pauseWasEnabled = _pause != null && _pause.enabled;
            if (_pause != null)
            {
                _pause.enabled = false;
                _pauseSuspended = true;
            }

            _scannerWasEnabled = _scanner != null && _scanner.enabled;
            if (_scanner != null)
                _scanner.enabled = false;

            _locomotion?.SetConversationLock(true);
            _rig.SetPaused(true);
            _playerMap?.Disable();

            _screen.SetPrompt(false);
            ShowCurrent();
        }

        private void ShowCurrent()
        {
            var node = _session?.Current;
            if (node == null)
            {
                Close();
                return;
            }

            _screen.Show(node, _speaker.DisplayName, _speaker.Category);

            if (node.HasAction)
                ActionInvoked?.Invoke(node.Action, node.QuestId);
        }

        private void Close()
        {
            FinishConversation(true);
        }

        /// <summary>
        /// Снимает все три блокировки: ввод, камера/курсор и пауза. Вызывается и из
        /// <see cref="OnDisable"/>, чтобы выгрузка сцены не оставила игру замороженной.
        /// </summary>
        private void FinishConversation(bool restorePause)
        {
            if (!_open)
            {
                RestorePauseImmediately();
                return;
            }

            _open = false;
            _session = null;

            _screen?.Hide();
            _speaker?.EndConversation();
            _speaker = null;

            _locomotion?.SetConversationLock(false);
            _rig?.SetPaused(false);
            _playerMap?.Enable();

            if (_scanner != null)
                _scanner.enabled = _scannerWasEnabled;

            if (_pause != null && _pauseSuspended)
            {
                _pauseSuspended = false;
                if (restorePause)
                    _restorePause = true;
                else
                    _pause.enabled = _pauseWasEnabled;
            }
        }

        private void RestorePauseImmediately()
        {
            _restorePause = false;
            if (_pause != null && _pauseSuspended)
            {
                _pauseSuspended = false;
                _pause.enabled = _pauseWasEnabled;
            }
        }

        private void HandleConversationInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            if (keyboard.eKey.wasPressedThisFrame)
            {
                OnAdvancePressed();
                return;
            }

            for (var i = 0; i < DialogueAsset.MaxChoices; i++)
            {
                if (keyboard[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame)
                {
                    OnChoicePressed(i);
                    return;
                }
            }
        }

        private void OnAdvancePressed()
        {
            if (!_open || _screen == null || _session == null)
                return;

            if (_screen.IsTyping)
            {
                _screen.CompleteTyping();
                // Терминальный узел: после раскрытия текста закрываем сразу,
                // иначе управление остаётся заблокированным до второго E.
                if (_session.Current != null && _session.Current.IsTerminal)
                    Close();
                return;
            }

            // С вариантами подтверждает Enter/клик: E не должна случайно выбрать ответ.
            if (_session.HasChoices)
                return;

            if (_session.Advance())
                ShowCurrent();
            else
                Close();
        }

        private void OnChoicePressed(int index)
        {
            if (!_open || _session == null || !_session.HasChoices)
                return;

            if (_session.Choose(index))
                ShowCurrent();
            else
                Close();
        }
    }
}

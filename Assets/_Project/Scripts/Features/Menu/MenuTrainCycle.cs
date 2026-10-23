using UnityEngine;

namespace FrostSpace.Features.Menu
{
    /// <summary>
    /// Цикл поезда в депо меню: состав въезжает из-за края кадра, тормозит,
    /// стоит под навесом, уезжает и ждёт за кадром, затем повторяет.
    ///
    /// Движение задано локальной координатой X относительно остановки: поезд в
    /// сцене стоит там, где должен остановиться, а <c>_farX</c> — сдвиг за кадр.
    /// Отсчёт идёт от <see cref="Time.unscaledTime"/>, поэтому пауза и смена
    /// <c>Time.timeScale</c> не останавливают сюжет.
    ///
    /// Сумма фаз держится около <c>_autoAdvanceSeconds</c> меню (26 с): за один
    /// показ вида зритель должен увидеть весь цикл.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(210)]
    public sealed class MenuTrainCycle : MonoBehaviour
    {
        /// <summary>Фазы цикла: номер нужен только для лога и отладки.</summary>
        private enum Phase
        {
            Approach,
            Dwell,
            Depart,
            Offscreen
        }

        [Header("Точки пути (локальный X)")]
        [Tooltip("Сдвиг состава за кадром — откуда он приезжает")]
        [SerializeField] private float _farX = 34f;

        [Tooltip("Позиция остановки: состав тормозит ровно здесь")]
        [SerializeField] private float _stopX = 0f;

        [Tooltip("Сдвиг за кадром по ходу движения — куда состав уезжает после стоянки")]
        [SerializeField] private float _exitX = -44f;

        [Header("Фазы, с")]
        [Tooltip("Разгон и торможение до остановки")]
        [SerializeField] private float _approachSeconds = 7f;

        [Tooltip("Стоянка под навесом")]
        [SerializeField] private float _dwellSeconds = 6f;

        [Tooltip("Отъезд в другую сторону кадра")]
        [SerializeField] private float _departSeconds = 5f;

        [Tooltip("Пауза, пока состава нет в кадре")]
        [SerializeField] private float _offscreenSeconds = 8f;

        private Vector3 _basePosition;
        private float _elapsed;
        private Phase _phase = Phase.Approach;

        /// <summary>Полная длительность цикла, с.</summary>
        public float CycleSeconds =>
            Mathf.Max(0.1f, _approachSeconds) + Mathf.Max(0f, _dwellSeconds) +
            Mathf.Max(0.1f, _departSeconds) + Mathf.Max(0f, _offscreenSeconds);

        private void OnEnable()
        {
            _basePosition = transform.localPosition;
            _elapsed = 0f;
            _phase = Phase.Approach;
            ApplyAt(_farX);

            Debug.Log(
                $"[MenuTrainCycle] {name}: цикл {CycleSeconds:0.#} с " +
                $"({_approachSeconds:0.#} приезд, {_dwellSeconds:0.#} стоянка, " +
                $"{_departSeconds:0.#} отъезд, {_offscreenSeconds:0.#} пауза)", this);
        }

        private void Update()
        {
            // Оси координат не трогаем: поезд едет по локальному X, а высота и
            // глубина заданы постановкой в сцене.
            _elapsed += Time.unscaledDeltaTime;

            switch (_phase)
            {
                case Phase.Approach:
                {
                    var t = Mathf.Clamp01(_elapsed / Mathf.Max(0.1f, _approachSeconds));
                    ApplyAt(Mathf.Lerp(_farX, _stopX, Mathf.SmoothStep(0f, 1f, t)));
                    if (t >= 1f)
                        Enter(Phase.Dwell);
                    break;
                }

                case Phase.Dwell:
                {
                    ApplyAt(_stopX);
                    if (_elapsed >= Mathf.Max(0f, _dwellSeconds))
                        Enter(Phase.Depart);
                    break;
                }

                case Phase.Depart:
                {
                    var t = Mathf.Clamp01(_elapsed / Mathf.Max(0.1f, _departSeconds));
                    // Отъезд продолжает ход: состав уходит вперёд, а не назад,
                    // откуда приехал, поэтому следующая остановка за кадром — _exitX.
                    ApplyAt(Mathf.Lerp(_stopX, _exitX, t * t));
                    if (t >= 1f)
                        Enter(Phase.Offscreen);

                    break;
                }

                default:
                {
                    ApplyAt(_exitX);
                    if (_elapsed >= Mathf.Max(0f, _offscreenSeconds))
                        Enter(Phase.Approach);
                    break;
                }
            }
        }

        private void Enter(Phase phase)
        {
            _phase = phase;
            _elapsed = 0f;

            Debug.Log($"[MenuTrainCycle] {name}: фаза {phase}", this);
        }

        /// <summary>
        /// Ставит состав в точку локального X. Торможение идёт по SmoothStep на входе,
        /// разгон — по квадрату на отъезде, поэтому подход читается как «накат
        /// и остановка», а отъезд — как «трогание и уход».
        /// </summary>
        private void ApplyAt(float x)
        {
            var position = _basePosition;
            position.x = x;
            transform.localPosition = position;
        }
    }
}

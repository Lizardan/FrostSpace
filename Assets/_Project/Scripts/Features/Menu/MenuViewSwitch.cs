using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace FrostSpace.Features.Menu
{
    /// <summary>
    /// Пять видов главного меню в одной сцене: пустая равнина, открытая
    /// площадка, ремонт, лаборатория и очередь у депо.
    ///
    /// Виды — независимые корни с собственными камерами. В любой момент активен
    /// ровно один корень и одна камера, поэтому второй AudioListener не спорит с
    /// первым, а выключенный вид не тратит кадры на ходящих роботов и частицы.
    /// Переключение идёт через короткое затемнение: в его пике вид подменяется,
    /// и глаз не ловит рывок камеры.
    ///
    /// Дополнительно меню само перелистывает виды: таймер идёт от
    /// <see cref="Time.unscaledDeltaTime"/>, поэтому не зависит от паузы. Ручной
    /// выбор сбрасывает отсчёт, а <see cref="SetAutoAdvancePaused"/> останавливает
    /// его, пока открыты настройки или идёт переход — смотреть меню, которое
    /// переключилось под руками, неприятно.
    ///
    /// UI не принадлежит этому компоненту: ленту видов строит
    /// <see cref="MainMenuScreen"/>, а сюда приходит только привязка.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(140)]
    public sealed class MenuViewSwitch : MonoBehaviour
    {
        /// <summary>Один вид меню: корень, камера и подпись для UI.</summary>
        [Serializable]
        private sealed class View
        {
            [SerializeField] private string _label = "VIEW";
            [SerializeField] private GameObject _root;
            [SerializeField] private GameObject _camera;

            public string Label => _label;
            public GameObject Root => _root;
            public GameObject Camera => _camera;

            /// <summary>Корень без камеры не покажет ничего — такой вид считается сломанным.</summary>
            public bool IsValid => _root != null && _camera != null;
        }

        [Header("Виды (порядок = порядок пунктов в меню)")]
        [SerializeField] private View[] _views = Array.Empty<View>();

        [Header("Переход")]
        [Tooltip("Затемнение до пика, с")]
        [SerializeField] private float _fadeInSeconds = 0.18f;

        [Tooltip("Пауза в тёмном кадре, с")]
        [SerializeField] private float _holdSeconds = 0.08f;

        [Tooltip("Проявление нового вида, с")]
        [SerializeField] private float _fadeOutSeconds = 0.32f;

        [Tooltip("Максимальная прозрачность циановой линии в переходе")]
        [SerializeField, Range(0f, 1f)] private float _lineAlpha = 0.55f;

        [Header("Авто-смена")]
        [Tooltip("Секунд на вид. 0 — авто-смена выключена")]
        [SerializeField] private float _autoAdvanceSeconds = 26f;

        private CanvasGroup _fade;
        private Image _line;
        private int _variant;
        private bool _switching;
        private bool _autoPaused;
        private float _autoElapsed;
        private Coroutine _transition;

        /// <summary>Текущий вид: 0 — равнина, 1 — площадка, 2 — ремонт, 3 — лаборатория, 4 — депо.</summary>
        public int Variant => _variant;

        /// <summary>Сколько видов назначено в инспекторе.</summary>
        public int ViewCount => _views?.Length ?? 0;

        /// <summary>Идёт затемнение; кнопки в это время лучше не нажимать.</summary>
        public bool IsSwitching => _switching;

        /// <summary>Доля времени до авто-смены, 0..1. По ней UI рисует заполнение подчёркивания.</summary>
        public float AutoProgress =>
            _autoAdvanceSeconds <= 0f ? 0f : Mathf.Clamp01(_autoElapsed / _autoAdvanceSeconds);

        /// <summary>Вид уже подменён: UI обновляет активный пункт.</summary>
        public event Action<int> VariantChanged;

        /// <summary>Подпись вида для ленты меню; индекс вне диапазона даёт пустую строку.</summary>
        public string ViewLabel(int index)
        {
            if (_views == null || index < 0 || index >= _views.Length)
                return string.Empty;

            return _views[index].Label;
        }

        private void Start()
        {
            _autoElapsed = 0f;
            Apply(_variant);
            VariantChanged?.Invoke(_variant);
        }

        private void Update()
        {
            if (_views == null || _views.Length < 2 || _autoAdvanceSeconds <= 0f)
                return;

            if (_autoPaused || _switching)
                return;

            _autoElapsed += Time.unscaledDeltaTime;
            if (_autoElapsed < _autoAdvanceSeconds)
                return;

            var next = (_variant + 1) % _views.Length;
            // Отсчёт обнуляется здесь, а не в RequestVariant: при авто-смене
            // таймер уже переполнен, и без сброса следующий вид запрашивался бы
            // сразу после предыдущего перехода.
            _autoElapsed = 0f;
            Debug.Log($"MenuViewSwitch: авто-смена {_variant} → {next}", this);
            RequestVariant(next, true);
        }

        /// <summary>Привязка оверлея, который строит экран меню.</summary>
        public void BindTransition(CanvasGroup fade, Image line)
        {
            _fade = fade;
            _line = line;
            SetOverlay(0f, 0.35f);

            if (_fade != null)
            {
                _fade.alpha = 0f;
                _fade.blocksRaycasts = false;
                _fade.interactable = false;
            }
        }

        /// <summary>Остановить авто-смену, пока открыты настройки или идёт переход сцены.</summary>
        public void SetAutoAdvancePaused(bool paused)
        {
            if (_autoPaused == paused)
                return;

            _autoPaused = paused;
            Debug.Log(paused
                ? "MenuViewSwitch: авто-смена на паузе"
                : "MenuViewSwitch: авто-смена продолжена", this);
        }

        /// <summary>
        /// Запросить вид. Повторный клик по активному пункту и клик во время
        /// перехода игнорируются, чтобы не запускать две корутины на одном оверлее.
        /// Ручной запрос сбрасывает отсчёт авто-смены.
        /// </summary>
        public void RequestVariant(int index)
        {
            RequestVariant(index, false);
        }

        private void RequestVariant(int index, bool fromAuto)
        {
            var count = ViewCount;
            if (count == 0)
                return;

            index = Mathf.Clamp(index, 0, count - 1);
            // Авто-смена идёт по кругу и не должна глохнуть о собственный выбор.
            if (!fromAuto)
                _autoElapsed = 0f;

            if (_switching || index == _variant)
                return;

            if (_fade == null)
            {
                Apply(index);
                VariantChanged?.Invoke(_variant);
                return;
            }

            _transition = StartCoroutine(Switch(index));
        }

        private IEnumerator Switch(int target)
        {
            _switching = true;

            yield return Fade(0f, 1f, Mathf.Max(0.01f, _fadeInSeconds));

            Apply(target);
            VariantChanged?.Invoke(_variant);

            if (_holdSeconds > 0f)
                yield return new WaitForSecondsRealtime(_holdSeconds);

            yield return Fade(1f, 0f, Mathf.Max(0.01f, _fadeOutSeconds));

            SetOverlay(0f, 0.35f);
            _switching = false;
            _transition = null;
        }

        private IEnumerator Fade(float from, float to, float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / seconds);
                var eased = Mathf.SmoothStep(0f, 1f, t);
                var value = Mathf.Lerp(from, to, eased);
                _fade.alpha = value;
                SetOverlay(value, Mathf.Lerp(0.35f, 1f, value));
                yield return null;
            }

            _fade.alpha = to;
            SetOverlay(to, to > 0.5f ? 1f : 0.35f);
        }

        private void SetOverlay(float alpha, float lineScale)
        {
            if (_line == null)
                return;

            var color = _line.color;
            color.a = alpha * _lineAlpha;
            _line.color = color;

            var rect = _line.rectTransform;
            rect.localScale = new Vector3(lineScale, 1f, 1f);
        }

        private void Apply(int index)
        {
            var count = ViewCount;
            if (count == 0)
            {
                Debug.LogError("MenuViewSwitch: не назначено ни одного вида меню", this);
                return;
            }

            _variant = Mathf.Clamp(index, 0, count - 1);

            for (var i = 0; i < count; i++)
            {
                var view = _views[i];
                if (view == null || !view.IsValid)
                {
                    Debug.LogError(
                        $"MenuViewSwitch: вид {i} без корня или камеры — он не будет показан", this);
                    continue;
                }

                var active = i == _variant;
                view.Root.SetActive(active);
                view.Camera.SetActive(active);
            }

            Debug.Log($"MenuViewSwitch: вид {_variant} «{ViewLabel(_variant)}»", this);
        }

        private void OnDisable()
        {
            if (_transition != null)
            {
                StopCoroutine(_transition);
                _transition = null;
            }

            _switching = false;
            _autoPaused = false;
            if (_fade != null)
            {
                _fade.alpha = 0f;
                _fade.blocksRaycasts = false;
            }
        }
    }
}

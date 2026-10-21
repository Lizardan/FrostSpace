using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace FrostSpace.Features.Menu
{
    /// <summary>
    /// Два вида главного меню в одной сцене: диорама и открытая снежная площадка.
    ///
    /// Виды — два независимых корня с собственными камерами. В любой момент
    /// активен ровно один корень и одна камера, поэтому второй AudioListener не
    /// спорит с первым, а выключенный вид не тратит кадры на ходящих роботов и
    /// частицы. Переключение идёт через короткое затемнение: в его пике вид
    /// подменяется, и глаз не ловит рывок камеры.
    ///
    /// UI не принадлежит этому компоненту: оверлей и кнопки строит
    /// <see cref="MainMenuScreen"/>, а сюда приходит только привязка.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(140)]
    public sealed class MenuViewSwitch : MonoBehaviour
    {
        [Header("Вид 1 — диорама")]
        [SerializeField] private GameObject _dioramaRoot;
        [SerializeField] private GameObject _dioramaCamera;

        [Header("Вид 2 — открытая местность")]
        [SerializeField] private GameObject _openRoot;
        [SerializeField] private GameObject _openCamera;

        [Header("Переход")]
        [Tooltip("Затемнение до пика, с")]
        [SerializeField] private float _fadeInSeconds = 0.18f;

        [Tooltip("Пауза в тёмном кадре, с")]
        [SerializeField] private float _holdSeconds = 0.08f;

        [Tooltip("Проявление нового вида, с")]
        [SerializeField] private float _fadeOutSeconds = 0.32f;

        [Tooltip("Максимальная прозрачность циановой линии в переходе")]
        [SerializeField, Range(0f, 1f)] private float _lineAlpha = 0.55f;

        private CanvasGroup _fade;
        private Image _line;
        private int _variant;
        private bool _switching;
        private Coroutine _transition;

        /// <summary>Текущий вид: 0 — диорама, 1 — открытая местность.</summary>
        public int Variant => _variant;

        /// <summary>Идёт затемнение; кнопки в это время лучше не нажимать.</summary>
        public bool IsSwitching => _switching;

        /// <summary>Вид уже подменён: UI обновляет активный пункт.</summary>
        public event Action<int> VariantChanged;

        private void Start()
        {
            Apply(_variant);
            VariantChanged?.Invoke(_variant);
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

        /// <summary>
        /// Запросить вид. Повторный клик по активному пункту и клик во время
        /// перехода игнорируются, чтобы не запускать две корутины на одном оверлее.
        /// </summary>
        public void RequestVariant(int index)
        {
            index = Mathf.Clamp(index, 0, 1);
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
            _variant = Mathf.Clamp(index, 0, 1);
            var showDiorama = _variant == 0;

            if (_dioramaRoot == null || _openRoot == null ||
                _dioramaCamera == null || _openCamera == null)
            {
                Debug.LogError(
                    "MenuViewSwitch: не назначены корни или камеры видов меню", this);
                return;
            }

            _dioramaRoot.SetActive(showDiorama);
            _dioramaCamera.SetActive(showDiorama);
            _openRoot.SetActive(!showDiorama);
            _openCamera.SetActive(!showDiorama);
        }

        private void OnDisable()
        {
            if (_transition != null)
            {
                StopCoroutine(_transition);
                _transition = null;
            }

            _switching = false;
            if (_fade != null)
            {
                _fade.alpha = 0f;
                _fade.blocksRaycasts = false;
            }
        }
    }
}

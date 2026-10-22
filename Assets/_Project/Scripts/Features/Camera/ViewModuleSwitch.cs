using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FrostSpace.Features.View
{
    /// <summary>
    /// Единственный владелец выбранного модуля восприятия. Колёсико мыши проматывает
    /// ленту из двух модулей; HUD рисует её, фейдер перекрытий и сканер читают выбор.
    ///
    /// Ввод берётся напрямую у <see cref="Mouse.current"/>: в карте <c>Player</c> скролла
    /// нет, а добавлять действие ради одного значения — лишняя связность. Тот же приём
    /// использует риг камеры, когда читает позицию мыши.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(140)]
    public sealed class ViewModuleSwitch : MonoBehaviour
    {
        /// <summary>Сколько модулей в ленте: считается по enum, чтобы новый модуль не терялся.</summary>
        private static readonly int ModuleCount = Enum.GetValues(typeof(ViewModule)).Length;

        [Header("Выбор")]
        [Tooltip("Модуль, выбранный на старте")]
        [SerializeField] private ViewModule _start = ViewModule.Aperture;

        [Tooltip("Сколько значений скролла нужно на один шаг: у тачпада шаг мелкий")]
        [SerializeField] private float _scrollPerStep = 0.5f;

        [Tooltip("Пауза между шагами, с: колесо не должно проматывать два модуля за кадр")]
        [SerializeField] private float _stepCooldown = 0.12f;

        private float _pendingScroll;
        private float _nextStepTime;

        /// <summary>Выбранный модуль.</summary>
        public ViewModule Current { get; private set; }

        private void Awake()
        {
            Current = _start;
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return;

            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > ModuleCarousel.ScrollDeadZone)
                _pendingScroll += scroll;

            if (Time.unscaledTime < _nextStepTime)
                return;

            var threshold = Mathf.Max(0.01f, _scrollPerStep);
            if (Mathf.Abs(_pendingScroll) < threshold)
                return;

            var next = ModuleCarousel.Step((int)Current, _pendingScroll, ModuleCount);
            _pendingScroll = 0f;
            _nextStepTime = Time.unscaledTime + _stepCooldown;

            if (next == (int)Current)
                return;

            Current = (ViewModule)next;
            Debug.Log($"[ViewModule] выбран {Current}", this);
        }
    }
}

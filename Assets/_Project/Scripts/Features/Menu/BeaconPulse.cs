using UnityEngine;

namespace FrostSpace.Features.Menu
{
    /// <summary>
    /// Пульс «мёртвой» техники: свет медленно набирает силу и гаснет рывком,
    /// как сигнальный маячок на обломках. Даёт жизнь статичному объекту без
    /// анимаций и частиц.
    /// </summary>
    [RequireComponent(typeof(Light))]
    [DisallowMultipleComponent]
    public sealed class BeaconPulse : MonoBehaviour
    {
        [Tooltip("Минимальная яркость в паузе")]
        [SerializeField] private float _minIntensity = 0.25f;

        [Tooltip("Пиковая яркость вспышки")]
        [SerializeField] private float _maxIntensity = 2.4f;

        [Tooltip("Период между вспышками, с")]
        [SerializeField] private float _period = 3.4f;

        [Tooltip("Доля периода, которую занимает вспышка")]
        [SerializeField, Range(0.02f, 0.9f)] private float _duty = 0.32f;

        private Light _light;

        private void Awake()
        {
            _light = GetComponent<Light>();
        }

        private void Update()
        {
            var phase = Mathf.Repeat(Time.unscaledTime, Mathf.Max(_period, 0.1f)) / Mathf.Max(_period, 0.1f);
            var glow = phase < _duty ? Mathf.Sin(phase / _duty * Mathf.PI) : 0f;
            _light.intensity = Mathf.Lerp(_minIntensity, _maxIntensity, glow * glow);
        }
    }
}

using UnityEngine;

namespace FrostSpace.Features.Menu
{
    /// <summary>
    /// Оживляет мачту станции в главном меню: тарелка радара медленно идёт
    /// по кругу и покачивается по высоте, а конус прожектора качается по
    /// земле, будто обшаривает периметр.
    ///
    /// Углы считаются от <see cref="Time.unscaledTime"/>, а не копятся в поле:
    /// при смене Time.timeScale или после паузы кадр не «уезжает», а вид
    /// продолжает движение с той же фазы. Базовые повороты снимаются в Awake,
    /// поэтому сцена задаёт исходную позу, а скрипт только отклоняет её.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(210)]
    public sealed class MenuRadarSweep : MonoBehaviour
    {
        [Header("Тарелка радара")]
        [Tooltip("Объект тарелки внутри FBX мачты")]
        [SerializeField] private Transform _dish;

        [Tooltip("Скорость вращения тарелки, градусов в секунду")]
        [SerializeField] private float _dishDegreesPerSecond = 11f;

        [Tooltip("Покачивание тарелки по вертикали, градусы")]
        [SerializeField] private float _dishTilt = 4f;

        [Tooltip("Период покачивания тарелки, с")]
        [SerializeField] private float _dishTiltSeconds = 9f;

        [Header("Луч прожектора")]
        [Tooltip("Конус света под мачтой")]
        [SerializeField] private Transform _beam;

        [Tooltip("Размах качания луча по горизонтали, градусы")]
        [SerializeField] private float _beamYaw = 24f;

        [Tooltip("Размах качания луча по вертикали, градусы")]
        [SerializeField] private float _beamPitch = 5f;

        [Tooltip("Период качания луча, с")]
        [SerializeField] private float _beamSeconds = 14f;

        private Quaternion _dishBase;
        private Quaternion _beamBase;

        private void Awake()
        {
            if (_dish != null)
                _dishBase = _dish.localRotation;

            if (_beam != null)
                _beamBase = _beam.localRotation;
        }

        private void LateUpdate()
        {
            var time = Time.unscaledTime;

            if (_dish != null)
            {
                var yaw = time * _dishDegreesPerSecond;
                var tilt = Mathf.Sin(time * Mathf.PI * 2f / Mathf.Max(_dishTiltSeconds, 0.1f)) *
                           _dishTilt;
                _dish.localRotation = _dishBase * Quaternion.Euler(tilt, yaw, 0f);
            }

            if (_beam != null)
            {
                var phase = time * Mathf.PI * 2f / Mathf.Max(_beamSeconds, 0.1f);
                var yaw = Mathf.Sin(phase) * _beamYaw;
                var pitch = Mathf.Cos(phase * 0.7f) * _beamPitch;
                _beam.localRotation = _beamBase * Quaternion.Euler(pitch, yaw, 0f);
            }
        }
    }
}

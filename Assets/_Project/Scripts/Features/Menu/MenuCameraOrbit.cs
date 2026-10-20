using UnityEngine;

namespace FrostSpace.Features.Menu
{
    /// <summary>
    /// Медленный облёт диорамы для главного меню: камера едет по кругу вокруг
    /// центра композиции и чуть покачивается по высоте, поэтому картинка живая
    /// даже без единого клика.
    ///
    /// Угол считается от <see cref="Time.unscaledTime"/>, а не копится в поле:
    /// камера остаётся на своём месте при смене Time.timeScale и не «уезжает»
    /// после паузы.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class MenuCameraOrbit : MonoBehaviour
    {
        [Header("Центр композиции")]
        [Tooltip("Точка, вокруг которой облетает камера (центр пьедестала)")]
        [SerializeField] private Vector3 _center = Vector3.zero;

        [Tooltip("Высота точки, на которую смотрит камера")]
        [SerializeField] private float _lookHeight = 1.2f;

        [Header("Облёт")]
        [Tooltip("Радиус облёта, м")]
        [SerializeField] private float _radius = 7.5f;

        [Tooltip("Высота камеры над центром, м")]
        [SerializeField] private float _height = 2.1f;

        [Tooltip("Скорость облёта, градусов в секунду")]
        [SerializeField] private float _degreesPerSecond = 3.2f;

        [Tooltip("Стартовый угол облёта, градусы")]
        [SerializeField] private float _startAngle = -35f;

        [Header("Дыхание камеры")]
        [Tooltip("Насколько камера покачивается по высоте, м")]
        [SerializeField] private float _bobAmplitude = 0.22f;

        [Tooltip("Период покачивания, с")]
        [SerializeField] private float _bobSeconds = 14f;

        [Tooltip("Насколько камера приближается и отъезжает, м")]
        [SerializeField] private float _pushAmplitude = 0.45f;

        private void LateUpdate()
        {
            var time = Time.unscaledTime;
            var angle = (_startAngle + _degreesPerSecond * time) * Mathf.Deg2Rad;
            var bob = Mathf.Sin(time * Mathf.PI * 2f / Mathf.Max(_bobSeconds, 0.1f)) * _bobAmplitude;
            var push = Mathf.Sin(time * Mathf.PI * 2f / Mathf.Max(_bobSeconds, 0.1f) * 0.62f) * _pushAmplitude;
            var distance = _radius + push;

            var position = new Vector3(
                _center.x + Mathf.Sin(angle) * distance,
                _center.y + _height + bob,
                _center.z + Mathf.Cos(angle) * distance);

            transform.position = position;
            transform.rotation = Quaternion.LookRotation(_center + Vector3.up * _lookHeight - position, Vector3.up);
        }
    }
}

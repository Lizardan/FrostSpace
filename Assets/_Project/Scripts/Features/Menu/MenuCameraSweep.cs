using UnityEngine;

namespace FrostSpace.Features.Menu
{
    /// <summary>
    /// Медленный кинематографичный проезд для открытого вида меню: камера едет
    /// по короткому отрезку и смотрит на обломки корабля, роботов и лампу.
    ///
    /// В отличие от <see cref="MenuCameraOrbit"/> здесь нет облёта диорамы:
    /// движение поступательное, с параллаксом сосен и низкой линией горизонта,
    /// поэтому площадка читается как часть мира, а не как пьедестал.
    /// Позиция считается от <see cref="Time.unscaledTime"/>, поэтому пауза или
    /// смена Time.timeScale не сдвигает кадр.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class MenuCameraSweep : MonoBehaviour
    {
        [Header("Маршрут")]
        [Tooltip("Начало проезда в локальных координатах родителя")]
        [SerializeField] private Vector3 _from = new(-14f, 1.9f, 4f);

        [Tooltip("Конец проезда в локальных координатах родителя")]
        [SerializeField] private Vector3 _to = new(-2f, 2.2f, 10f);

        [Tooltip("Сколько секунд ехать от начала до конца")]
        [SerializeField] private float _travelSeconds = 46f;

        [Header("Взгляд")]
        [Tooltip("Точка, на которую смотрит камера")]
        [SerializeField] private Vector3 _lookAt = new(7f, 1.3f, 22f);

        [Tooltip("Насколько камера покачивается по высоте, м")]
        [SerializeField] private float _bobAmplitude = 0.06f;

        [Tooltip("Период покачивания, с")]
        [SerializeField] private float _bobSeconds = 9f;

        [Tooltip("Насколько камера ведёт точку взгляда вбок, м")]
        [SerializeField] private float _lookDrift = 1.4f;

        private void LateUpdate()
        {
            var time = Time.unscaledTime;
            var cycle = Mathf.PingPong(time / Mathf.Max(_travelSeconds, 0.1f), 1f);
            var t = Mathf.SmoothStep(0f, 1f, cycle);
            var bob = Mathf.Sin(time * Mathf.PI * 2f / Mathf.Max(_bobSeconds, 0.1f)) *
                      _bobAmplitude;
            var drift = Mathf.Sin(time * Mathf.PI * 2f / Mathf.Max(_bobSeconds * 2.7f, 0.1f)) *
                        _lookDrift;

            var position = Vector3.Lerp(_from, _to, t) + Vector3.up * bob;
            var target = _lookAt + Vector3.right * drift;
            transform.localPosition = position;
            transform.localRotation = Quaternion.LookRotation(target - position, Vector3.up);
        }
    }
}

using UnityEngine;

namespace FrostSpace.Features.Ride
{
    /// <summary>
    /// Профиль камеры транспорта: как именно камера смотрит за ним, пока игрок за рулём.
    ///
    /// Байки ездят в виде сверху с фиксированным курсом (<see cref="View.CameraMode.Ride"/>):
    /// верх экрана всегда «север», а корпус разворачивается под ним. Ходун так не
    /// читается — у него две ноги, шаг и корпус в 2,6 м, и сверху видно только шлем.
    /// Поэтому у ходуна своя поза (<see cref="View.CameraMode.RideOrbit"/>): камера
    /// облетает его мышью, как в третьем лице, но орбита крупнее и выше.
    ///
    /// Числа живут на префабе транспорта, а не в риге: риг один на всю игру, а
    /// посадочные габариты у машин разные. Компонент необязателен — без него
    /// транспорт получает вид сверху.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RideCameraProfile : MonoBehaviour
    {
        [Tooltip("Радиус орбиты: на сколько камера отходит от центра, м")]
        [SerializeField] private float _distance = 9f;

        [Tooltip("Высота центра орбиты в транспорте: камера облетает эту точку, м")]
        [SerializeField] private float _lookHeight = 1.9f;

        [Tooltip("Тангаж при посадке: камера смотрит чуть вниз, градусы")]
        [SerializeField] private float _startPitch = 16f;

        [Tooltip("Предел взгляда вверх: ниже камера уходит в землю, градусы")]
        [SerializeField] private float _minPitch = -12f;

        [Tooltip("Предел взгляда вниз: выше камера смотрит на ходуна сверху, градусы")]
        [SerializeField] private float _maxPitch = 62f;

        /// <summary>Радиус орбиты, м.</summary>
        public float Distance => _distance;

        /// <summary>Высота центра орбиты в транспорте, м.</summary>
        public float LookHeight => _lookHeight;

        /// <summary>Тангаж, с которого начинается обзор при посадке, градусы.</summary>
        public float StartPitch => _startPitch;

        /// <summary>Нижний предел тангажа, градусы.</summary>
        public float MinPitch => _minPitch;

        /// <summary>Верхний предел тангажа, градусы.</summary>
        public float MaxPitch => _maxPitch;
    }
}

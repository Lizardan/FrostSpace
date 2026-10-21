namespace FrostSpace.Features.View
{
    /// <summary>Режим камеры игрока.</summary>
    public enum CameraMode
    {
        /// <summary>Из-за плеча — основной вид: мышь ведёт обзор, WASD идёт от камеры.</summary>
        ThirdPerson = 0,

        /// <summary>Из головы робота: включается внутри зданий.</summary>
        FirstPerson = 1,

        /// <summary>
        /// Сверху над транспортом (ховербайк и мотоцикл): включается, пока игрок за рулём.
        /// Камера едет за корпусом по позиции, но курс держит фиксированным.
        /// </summary>
        Ride = 2
    }
}

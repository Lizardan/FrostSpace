using FrostSpace.Features.Ride;
using UnityEngine;

namespace FrostSpace.Features.Menu
{
    /// <summary>
    /// Сажает игрока на байк в витрине меню.
    ///
    /// Поза берётся тем же <see cref="RiderPose"/>, что и в игре: меню не держит
    /// вторую реализацию посадки, поэтому витрина и игровой кадр не расходятся.
    /// Актёр уже стоит ребёнком посадочной точки в сцене, поэтому здесь только
    /// один вызов на включении вида. Повторные включения безопасны: <c>Sit</c>
    /// идемпотентен и не переснимает базу позы.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuRider : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Поза наездника на этом же объекте или на модели")]
        [SerializeField] private RiderPose _pose;

        [Tooltip("Посадочная точка байка, на которой висит наездник")]
        [SerializeField] private Transform _seat;

        private void OnEnable()
        {
            if (_pose == null || _seat == null)
            {
                Debug.LogError(
                    "[MenuRider] не назначены поза или сиденье — наездник останется стоять", this);
                return;
            }

            _pose.Sit(_seat);
        }
    }
}

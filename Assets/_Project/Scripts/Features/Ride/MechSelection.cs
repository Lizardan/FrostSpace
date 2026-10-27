using System.Collections.Generic;
using FrostSpace.Features.Scan;
using UnityEngine;

namespace FrostSpace.Features.Ride
{
    /// <summary>
    /// Адресат клавиши <c>E</c> среди ходунов сцены.
    ///
    /// Раньше каждый <see cref="Mech"/> сам решал, сажать ли игрока, и проверял
    /// только дистанцию. Все четыре подписаны на одно действие <c>Player/Interact</c>,
    /// поэтому при двух ходунах в радиусе побеждал не ближний, а тот, чей
    /// <c>OnEnable</c> отработал раньше: выбор зависел от порядка объектов в сцене.
    /// Здесь выбор один на всю партию:
    ///
    /// 1. **Выделенная цель сканера** — если игрок навёл прицел на ходун
    ///    (<see cref="ObjectScanner.AimTarget"/>), сажает именно его, даже когда
    ///    другой ходун ближе. Намерение игрока важнее геометрии.
    /// 2. Иначе — **ближайший по горизонтали** среди тех, кто реально в своём
    ///    <see cref="Mech.MountRadius"/>. Высота не учитывается: ходуны стоят на
    ///    снегу с разным рельефом, и по 3D-дистанции дальний по склону выигрывал бы
    ///    у стоящего под ногами.
    ///
    /// Реестр статический, а domain reload в проекте выключен, поэтому он
    /// очищается на <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/>:
    /// иначе во втором запуске Play Mode в нём лежали бы уничтоженные объекты.
    /// </summary>
    public static class MechSelection
    {
        private static readonly List<Mech> Mechs = new(8);
        private static ObjectScanner s_scanner;

        /// <summary>
        /// Domain reload выключен: реестр и кэш сканера прошлой сессии сбрасываются
        /// до первого кадра новой.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Mechs.Clear();
            s_scanner = null;
        }

        internal static void Register(Mech mech)
        {
            if (mech == null || Mechs.Contains(mech))
                return;

            Mechs.Add(mech);
        }

        internal static void Unregister(Mech mech)
        {
            if (mech != null)
                Mechs.Remove(mech);
        }

        /// <summary>
        /// Кого сажает <c>E</c> из этой точки: выделенного сканером, иначе
        /// ближайшего в своём радиусе посадки. <c>null</c> — сажать некого.
        /// </summary>
        public static Mech Pick(Vector3 from)
        {
            var aimed = Aimed(from);
            if (aimed != null)
                return aimed;

            Mech best = null;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < Mechs.Count; i++)
            {
                var mech = Mechs[i];
                if (!IsAvailable(mech))
                    continue;

                var distance = Horizontal(from, mech.transform.position);
                if (distance > mech.MountRadius || distance >= bestDistance)
                    continue;

                best = mech;
                bestDistance = distance;
            }

            return best;
        }

        /// <summary>Этот ходун — адресат <c>E</c> из точки; гейт для <see cref="Mech"/>.</summary>
        public static bool IsPicked(Mech mech, Vector3 from)
        {
            return mech != null && Pick(from) == mech;
        }

        /// <summary>Цель сканера, если это ходун и он в своём радиусе посадки.</summary>
        private static Mech Aimed(Vector3 from)
        {
            if (s_scanner == null)
                s_scanner = Object.FindAnyObjectByType<ObjectScanner>();
            if (s_scanner == null)
                return null;

            var target = s_scanner.AimTarget;
            if (target == null)
                return null;

            var mech = target.GetComponentInParent<Mech>();
            if (!IsAvailable(mech))
                return null;

            return Horizontal(from, mech.transform.position) <= mech.MountRadius
                ? mech
                : null;
        }

        /// <summary>Ходун живой, включён и свободен.</summary>
        private static bool IsAvailable(Mech mech)
        {
            return mech != null && mech.isActiveAndEnabled && !mech.IsMounted;
        }

        /// <summary>Расстояние по горизонтали: рельеф под ходунами разный.</summary>
        private static float Horizontal(Vector3 a, Vector3 b)
        {
            var delta = b - a;
            delta.y = 0f;
            return delta.magnitude;
        }
    }
}

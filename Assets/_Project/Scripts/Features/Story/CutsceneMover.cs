using System.Collections;
using FrostSpace.Features.View;
using UnityEngine;

namespace FrostSpace.Features.Story
{
    /// <summary>
    /// Проводка сценового актёра по кривой с равномерной скоростью. Нужна обоим сюжетным
    /// режиссёрам: в вагоне R13 уходит к проёму, в депо через тот же проём идут R13,
    /// тележка и игрок.
    ///
    /// Маршрут — не ломаная, а <see cref="PathCurve"/>: угол в маршруте даёт мгновенную
    /// смену направления движения, камеру наводят на идущего актёра, и она наследует
    /// этот скачок как щелчок рыскания. Кривая делает поворот дугой.
    ///
    /// Если передан <see cref="Animator"/>, корпус доворачивается по касательной, а
    /// скорость клипа подгоняется под фактическую — шаг не скользит. Это не настоящий
    /// root motion (клипы in-place, ADR 0022/0023), но визуально совпадает с ним.
    /// </summary>
    public static class CutsceneMover
    {
        private static readonly int MovingId = Animator.StringToHash("Moving");

        /// <summary>
        /// Доля пройденного пути по доле времени: быстрый старт, ровный ход, мягкая
        /// остановка. Чистый SmoothStep даёт нулевую скорость на старте, и идущий
        /// полсекунды разгоняется из ничего — со стороны это читается как «очень медленно
        /// начинает». Смесь с линейной частью поднимает стартовую скорость до четверти
        /// крейсерской, не ломая плавного финиша.
        /// </summary>
        public static float Profile(float t01)
        {
            var t = Mathf.Clamp01(t01);
            return Mathf.SmoothStep(0f, 1f, t) * 0.75f + t * 0.25f;
        }

        public static IEnumerator Walk(
            Transform target, Vector3[] points, float seconds,
            Animator animator = null, float clipSpeed = 0.93f, float turnSpeed = 240f)
        {
            if (target == null || points == null || points.Length == 0)
                yield break;

            if (points.Length == 1 || seconds <= 0f)
            {
                target.position = points[points.Length - 1];
                if (animator != null)
                    animator.speed = 1f;
                yield break;
            }

            var curve = new PathCurve(points);
            var total = curve.TotalMeters;
            if (total <= 0.0001f)
            {
                target.position = points[points.Length - 1];
                if (animator != null)
                    animator.speed = 1f;
                yield break;
            }

            var elapsed = 0f;
            var speed = 0f;

            // Параметр ходьбы выставляем здесь, а не в режиссёрах: у игрока его обычно
            // ведёт RobotLocomotion, а на время заставки она отключена — без этого
            // персонаж скользил по платформе в Idle.
            if (animator != null)
                animator.SetBool(MovingId, true);

            while (elapsed < seconds)
            {
                var deltaTime = Time.deltaTime;
                elapsed += deltaTime;
                var t = Profile(Mathf.Clamp01(elapsed / seconds));
                var next = curve.PointAtMeters(t * total);

                var delta = next - target.position;
                var planar = new Vector3(delta.x, 0f, delta.z);

                // Доворот по ходу: без него актёр идёт боком и спиной вперёд.
                if (planar.sqrMagnitude > 0.000001f)
                {
                    var rotation = Quaternion.LookRotation(planar.normalized, Vector3.up);
                    target.rotation = Quaternion.RotateTowards(
                        target.rotation, rotation, turnSpeed * deltaTime);
                }

                // Шаг без скольжения: скорость клипа = фактическая / скорость клипа.
                if (animator != null && deltaTime > 0.0001f)
                {
                    var actual = planar.magnitude / deltaTime;
                    speed = Mathf.MoveTowards(speed, actual, 6f * deltaTime);
                    animator.speed = Mathf.Clamp(
                        speed / Mathf.Max(clipSpeed, 0.01f), 0.35f, 1.6f);
                }

                target.position = next;
                yield return null;
            }

            target.position = points[points.Length - 1];
            // Обязательно: иначе Idle останется с поджатой скоростью и шаг продолжится
            // на месте после остановки.
            if (animator != null)
            {
                animator.speed = 1f;
                animator.SetBool(MovingId, false);
            }
        }
    }
}

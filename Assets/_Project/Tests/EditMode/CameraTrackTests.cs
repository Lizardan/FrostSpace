using System.Collections.Generic;
using FrostSpace.Features.Story;
using FrostSpace.Features.View;
using NUnit.Framework;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Математика проезда и объективные метрики плавности. Дёрганье раньше лечили
    /// подбором ключей вслепую; здесь оно запрещено числами: путевая скорость имеет
    /// ровно одну вершину и не скачет на стыках, рыскание ограничено и по скорости,
    /// и по ускорению, поле зрения меняется монотонно.
    ///
    /// Направление кадра даёт субъект, поэтому тесты гоняют составной кадр целиком:
    /// позиция с пути камеры плюс взгляд на игрока, идущего по тому же маршруту, что
    /// в сцене. Маршрут тоже кривая, а не ломаная: угол в маршруте актёра камера
    /// унаследовала бы как щелчок рыскания.
    /// </summary>
    public sealed class CameraTrackTests
    {
        private const float Step = 1f / 60f;
        private const float Seconds = DepotArrivalDirector.PathSeconds;

        // Расписание и маршрут игрока — как в DepotArrivalDirector по умолчанию:
        // очередь стартует в 5.1 с, тележка с запозданием 0.8 с, игрок ещё через 0.9 с,
        // и идёт он по одной прямой из проёма, без поворота в бок.
        private const float PlayerStart = 6.8f;
        private const float PlayerSeconds = 9.8f;

        private static PathCurve CameraCurve()
            => new PathCurve(DepotArrivalDirector.DefaultPath());

        // Маршрут игрока в осях сцены: место в вагоне, горловина проёма, порог на
        // платформе, точка на платформе. Отметки — не ноль сцены: депо стало чанком
        // клетки (−2,−2), и головка главного пути напротив этих точек лежит на
        // −1,06 м (число из `Tools/Blender/world_layout.py`; двор ровный, стол на
        // всём подходе к депо держит ту же отметку). Место в вагоне — корень вагона
        // (+1,00 над головкой) плюс 0,20 локально, порог и точка на платформе — верх
        // настила (+1,04 над головкой: плита 0,90 плюс шапка снежного вала 0,14).
        private const float RailHeadAtDoor = -1.06f;

        private static PathCurve PlayerCurve()
            => new PathCurve(DepotArrivalDirector.BuildRoute(
                new Vector3(-8.6f, RailHeadAtDoor + 1.20f, -1.0f),
                new Vector3(-5.0f, RailHeadAtDoor + 1.00f, -1.0f),
                new Vector3(-2.6f, RailHeadAtDoor + 1.04f, -1.0f),
                new Vector3(0.7f, RailHeadAtDoor + 1.04f, -1.0f)));

        private static Vector3 PlayerAt(PathCurve route, float elapsed)
        {
            var t = Mathf.Clamp01((elapsed - PlayerStart) / PlayerSeconds);
            return route.PointAtFraction(CutsceneMover.Profile(t));
        }

        private static CameraPose PathPose(
            PathCurve curve, PathCurve route, Vector3 aim, float elapsed)
            => DepotArrivalDirector.PoseAt(
                curve, Seconds, elapsed, aim, PlayerAt(route, elapsed),
                DepotArrivalDirector.AimHandoffSeconds, DepotArrivalDirector.AimBlendSeconds);

        private static CameraKey[] Keys(params Vector3[] positions)
        {
            var keys = new CameraKey[positions.Length];
            for (var i = 0; i < positions.Length; i++)
                keys[i] = new CameraKey { Position = positions[i], Fov = 50f - i };
            return keys;
        }

        // ------------------------------------------------------------------ кривая

        [Test]
        public void TotalMeters_MatchesChordLength()
        {
            var curve = new PathCurve(Keys(Vector3.zero, new Vector3(0f, 0f, 10f), new Vector3(0f, 0f, 20f)));
            Assert.AreEqual(20f, curve.TotalMeters, 0.2f);
        }

        [Test]
        public void PointAtMeters_HitsEndpoints()
        {
            var curve = new PathCurve(Keys(Vector3.zero, new Vector3(0f, 0f, 10f), new Vector3(0f, 0f, 20f)));

            Assert.AreEqual(Vector3.zero, curve.PointAtMeters(0f));
            Assert.Less(Vector3.Distance(new Vector3(0f, 0f, 20f), curve.PointAtMeters(99f)), 0.02f);
            Assert.Less(Vector3.Distance(new Vector3(0f, 0f, 20f), curve.PointAtFraction(1f)), 0.02f);
        }

        [Test]
        public void PointAtMeters_AtKeyDistance_IsKeyPosition()
        {
            var curve = new PathCurve(Keys(Vector3.zero, new Vector3(0f, 0f, 10f), new Vector3(0f, 0f, 20f)));
            Assert.Less(Vector3.Distance(new Vector3(0f, 0f, 10f), curve.PointAtMeters(10f)), 0.05f);
        }

        [Test]
        public void PointAtMeters_IsContinuousAcrossKey()
        {
            var curve = new PathCurve(Keys(Vector3.zero, new Vector3(0f, 0f, 10f), new Vector3(0f, 0f, 20f)));
            Assert.Less(Vector3.Distance(
                curve.PointAtMeters(9.95f), curve.PointAtMeters(10.05f)), 0.2f);
        }

        [Test]
        public void PointAtMeters_ClampsBeyondEnds()
        {
            var curve = new PathCurve(Keys(Vector3.zero, new Vector3(0f, 0f, 10f), new Vector3(0f, 0f, 20f)));
            Assert.AreEqual(Vector3.zero, curve.PointAtMeters(-5f));
        }

        [Test]
        public void PointAtMeters_DuplicatePoints_DoesNotProduceNaN()
        {
            var repeated = new Vector3(4f, 0f, 4f);
            var curve = new PathCurve(Keys(Vector3.zero, repeated, repeated, new Vector3(0f, 0f, 12f)));

            for (var i = 0; i <= 10; i++)
            {
                var position = curve.PointAtFraction(i / 10f);
                Assert.IsFalse(float.IsNaN(position.x), "NaN на доле " + i / 10f);
                Assert.IsFalse(float.IsNaN(position.z), "NaN на доле " + i / 10f);
            }
        }

        [Test]
        public void PathCurve_SinglePoint_DoesNotThrow()
        {
            var curve = new PathCurve(Keys(new Vector3(1f, 2f, 3f)));
            Assert.AreEqual(0f, curve.TotalMeters, 1e-4f);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), curve.PointAtMeters(5f));
        }

        // ------------------------------------------------------------------ время кадра

        [Test]
        public void Ease_IsSymmetricAndAnchored()
        {
            Assert.AreEqual(0f, CameraTrack.Ease(0f), 1e-5f);
            Assert.AreEqual(0.5f, CameraTrack.Ease(0.5f), 1e-5f);
            Assert.AreEqual(1f, CameraTrack.Ease(1f), 1e-5f);

            // Зеркальность: разгон и торможение одинаковы, иначе кадр «дёргается» на финише.
            for (var t = 0.05f; t < 0.5f; t += 0.05f)
                Assert.AreEqual(1f, CameraTrack.Ease(t) + CameraTrack.Ease(1f - t), 1e-3f);
        }

        [Test]
        public void FovAlong_IsMonotonicAndSlow()
        {
            var keys = Keys(Vector3.zero, new Vector3(0f, 0f, 10f), new Vector3(0f, 0f, 20f));
            var previous = CameraTrack.FovAlong(keys, 0f);

            for (var i = 1; i <= 100; i++)
            {
                var fov = CameraTrack.FovAlong(keys, i / 100f);
                var delta = fov - previous;

                // Никакого разворота направления: зум один на весь кадр.
                if (!Mathf.Approximately(delta, 0f))
                    Assert.AreEqual(-1f, Mathf.Sign(delta), 0f, "зум дышит на шаге " + i);
                Assert.LessOrEqual(Mathf.Abs(delta) / (Seconds / 100f), 1f,
                    "скорость зума на шаге " + i);
                previous = fov;
            }

            Assert.AreEqual(48f, previous, 1e-3f);
        }

        [Test]
        public void Aim_LooksAtTargetAndKeepsHorizon()
        {
            var rotation = CameraTrack.Aim(Vector3.zero, new Vector3(3f, 1f, 4f));
            var forward = rotation * Vector3.forward;
            var wanted = new Vector3(3f, 1f, 4f).normalized;

            Assert.Less(Vector3.Distance(wanted, forward), 1e-4f);
            Assert.Less(Mathf.Abs(Vector3.Dot(rotation * Vector3.right, Vector3.up)), 0.01f);

            var same = CameraTrack.Aim(Vector3.zero, Vector3.zero);
            Assert.IsFalse(float.IsNaN(same.x));
        }

        // ------------------------------------------------------------------ плавность

        /// <summary>
        /// Профиль путевой скорости обязан иметь ровно одну вершину: ноль на старте,
        /// подъём, спуск, ноль на финише. Две вершины — это «то быстрее, то медленнее».
        /// Порог — доля от пика, а не абсолют: на плосой вершине bell-кривой производная
        /// почти нулевая, и абсолютный epsilon ловил бы шум float вместо второго горба.
        /// </summary>
        [Test]
        public void DepotPath_SpeedHasSinglePeak()
        {
            var speeds = LinearSpeeds();
            var peak = 0f;
            foreach (var speed in speeds)
                peak = Mathf.Max(peak, speed);

            var epsilon = peak * 0.02f;
            var phase = 0;

            for (var i = 1; i < speeds.Count; i++)
            {
                var delta = speeds[i] - speeds[i - 1];
                if (delta < -epsilon)
                    phase = Mathf.Max(phase, 1);
                else if (delta > epsilon && phase == 1)
                    Assert.Fail("у профиля скорости вторая вершина на шаге " + i);
            }
        }

        [Test]
        public void DepotPath_SpeedDoesNotJump()
        {
            var speeds = LinearSpeeds();
            var peak = 0f;
            foreach (var speed in speeds)
                peak = Mathf.Max(peak, speed);

            Assert.Greater(peak, 0.01f, "путь не проходит камеру");

            for (var i = 1; i < speeds.Count; i++)
                Assert.LessOrEqual(Mathf.Abs(speeds[i] - speeds[i - 1]), peak * 0.12f,
                    "скачок скорости на шаге " + i);

            // Отношение максимума к минимуму в середине кадра: bell-кривая разрешает
            // мягкий подъём, 22-кратная разница прежней time-ключевой версии — нет.
            var from = speeds.Count / 4;
            var to = speeds.Count * 3 / 4;
            var min = float.MaxValue;
            var max = 0f;

            for (var i = from; i < to; i++)
            {
                min = Mathf.Min(min, speeds[i]);
                max = Mathf.Max(max, speeds[i]);
            }

            Assert.LessOrEqual(max, min * 2.6f, "разброс скоростей в середине кадра");
        }

        [Test]
        public void DepotPath_SpeedStaysCinematic()
        {
            var total = CameraCurve().TotalMeters;
            var peak = 0f;
            foreach (var speed in LinearSpeeds())
                peak = Mathf.Max(peak, speed);

            // Пик bell-кривой — 1.875 длины пути в секунду; запас на изгиб дуги.
            // Прежняя time-ключевая версия давала 5.58 м/с на этом же кадре.
            Assert.LessOrEqual(peak, 2.4f * total / Seconds);
        }

        [Test]
        public void DepotPath_AngularSpeedIsBounded()
        {
            var speeds = AngularSpeeds();
            var peak = 0f;
            var sum = 0f;

            foreach (var speed in speeds)
            {
                peak = Mathf.Max(peak, speed);
                sum += speed;
            }

            // Бюджет поворота задаёт монтаж, который просил пользователь: камера
            // закручивается вокруг вышедшего героя и встаёт ему за спину. Это около
            // ста градусов, и они размазаны по последним семи секундам кадра.
            // Предел остаётся в разы ниже прежнего разворота на 180° за пару секунд
            // вокруг стоящего на месте робота.
            Assert.LessOrEqual(peak, 40f, "рыскание камеры, град/с");
            Assert.LessOrEqual(sum / speeds.Count, 10f, "среднее рыскание камеры, град/с");
        }

        [Test]
        public void DepotPath_AngularAccelerationIsBounded()
        {
            var speeds = AngularSpeeds();

            // Рывок рыскания глаз читает как «рваная камера» лучше всего: постоянный
            // поворот не мешает, а скачок скорости поворота мешает. Верх — цена
            // авторского закручивания вокруг героя; прежний проезд давал сотни.
            var peak = 0f;
            for (var i = 1; i < speeds.Count; i++)
                peak = Mathf.Max(peak, Mathf.Abs(speeds[i] - speeds[i - 1]) * 60f);

            Assert.LessOrEqual(peak, 100f, "угловое ускорение камеры, град/с^2");
        }

        [Test]
        public void DepotPath_StaysOutsideTheWagon()
        {
            // Кузов вагона в покое: x от -12 до -4. Ни в одной точке пути камера не
            // должна входить в вагон и проходить сквозь его стену.
            var curve = CameraCurve();
            var route = PlayerCurve();

            for (var elapsed = 0f; elapsed <= Seconds; elapsed += Step)
            {
                var position = PathPose(curve, route, DepotArrivalDirector.DefaultDoorAim, elapsed).Position;
                Assert.Greater(position.x, -2.5f,
                    "камера у борта вагона на " + elapsed.ToString("0.0") + " с");
            }
        }

        [Test]
        public void DepotPath_EndsOnThePlayersBack()
        {
            var curve = CameraCurve();
            var route = PlayerCurve();
            var pose = PathPose(curve, route, DepotArrivalDirector.DefaultDoorAim, Seconds);

            var toPlayer = PlayerAt(route, Seconds) + Vector3.up * DepotArrivalDirector.AimHeight
                           - pose.Position;
            var forward = pose.Rotation * Vector3.forward;

            Assert.Less(Vector3.Distance(forward.normalized, toPlayer.normalized), 0.05f);
            Assert.Greater(toPlayer.magnitude, 2.5f, "камера впритык к игроку");
            Assert.Less(toPlayer.magnitude, 4.5f, "камера слишком далеко от игрока");
        }

        /// <summary>
        /// Угол между поворотами через atan2(|cross|, dot). Quaternion.Angle считает
        /// acos и на малых углах теряет точность в float: даёт ровно 0, а потом
        /// «выпрыгивает» — метрика показывала рывок там, гдеcamera шла плавно.
        /// </summary>
        private static float AngleBetween(Quaternion a, Quaternion b)
        {
            var va = a * Vector3.forward;
            var vb = b * Vector3.forward;
            return Mathf.Rad2Deg * Mathf.Atan2(
                Vector3.Cross(va, vb).magnitude, Vector3.Dot(va, vb));
        }

        private static List<float> LinearSpeeds()
        {
            var curve = CameraCurve();
            var route = PlayerCurve();
            var speeds = new List<float>();
            var previous = PathPose(curve, route, DepotArrivalDirector.DefaultDoorAim, 0f).Position;

            for (var elapsed = Step; elapsed <= Seconds; elapsed += Step)
            {
                var position = PathPose(curve, route, DepotArrivalDirector.DefaultDoorAim, elapsed).Position;
                speeds.Add(Vector3.Distance(position, previous) / Step);
                previous = position;
            }

            return speeds;
        }

        private static List<float> AngularSpeeds()
        {
            var curve = CameraCurve();
            var route = PlayerCurve();
            var speeds = new List<float>();
            var previous = PathPose(curve, route, DepotArrivalDirector.DefaultDoorAim, 0f).Rotation;

            for (var elapsed = Step; elapsed <= Seconds; elapsed += Step)
            {
                var rotation = PathPose(curve, route, DepotArrivalDirector.DefaultDoorAim, elapsed).Rotation;
                speeds.Add(AngleBetween(rotation, previous) / Step);
                previous = rotation;
            }

            return speeds;
        }
    }
}

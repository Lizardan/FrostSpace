using FrostSpace.Features.Map;
using NUnit.Framework;
using UnityEngine;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Математика миникарты: критерий `RD-05` дословно — «маркер не теряется за
    /// краем и не врёт о направлении». Оба проверяются числами, потому что в
    /// Play Mode глаз прощает и то и другое.
    /// </summary>
    public sealed class MiniMapProjectionTests
    {
        private const float RadiusMeters = 900f;
        private const float RadiusPixels = 140f;

        [Test]
        public void NorthIsUpAndEastIsRight()
        {
            var center = new Vector2(-1200f, -1200f);

            var north = MiniMapProjection.Project(new Vector2(-1200f, -300f), center,
                RadiusMeters, RadiusPixels);
            Assert.That(north.x, Is.EqualTo(0f).Within(1e-3f), "север не уезжает вбок");
            Assert.That(north.y, Is.EqualTo(RadiusPixels).Within(1e-3f),
                "900 м на север — ровно верх круга");

            var east = MiniMapProjection.Project(new Vector2(-300f, -1200f), center,
                RadiusMeters, RadiusPixels);
            Assert.That(east.x, Is.EqualTo(RadiusPixels).Within(1e-3f));
            Assert.That(east.y, Is.EqualTo(0f).Within(1e-3f));

            var atCenter = MiniMapProjection.Project(center, center, RadiusMeters,
                RadiusPixels);
            Assert.That(atCenter.sqrMagnitude, Is.EqualTo(0f).Within(1e-6f),
                "сам игрок — в центре");
        }

        [Test]
        public void ScaleIsLinear()
        {
            var center = Vector2.zero;
            var half = MiniMapProjection.Project(new Vector2(0f, 450f), center,
                RadiusMeters, RadiusPixels);
            var full = MiniMapProjection.Project(new Vector2(0f, 900f), center,
                RadiusMeters, RadiusPixels);

            Assert.That(full.magnitude, Is.EqualTo(half.magnitude * 2f).Within(1e-3f),
                "удаление вдвое — это и на карте вдвое");
        }

        [Test]
        public void PolarMatchesCartesian()
        {
            var byPolar = MiniMapProjection.ProjectPolar(900f, 45f, RadiusMeters,
                RadiusPixels);
            var byWorld = MiniMapProjection.Project(
                new Vector2(900f * Mathf.Sin(Mathf.PI * 0.25f),
                            900f * Mathf.Cos(Mathf.PI * 0.25f)),
                Vector2.zero, RadiusMeters, RadiusPixels);

            var diff = (byPolar - byWorld).magnitude;
            Assert.That(diff, Is.LessThan(1e-2f),
                "азимут 45° от депо к станции и та же точка мира — один маркер");
        }

        [Test]
        public void FarGoalIsPinnedToRimAndKeepsBearing()
        {
            var center = new Vector2(-1200f, -1200f);
            var station = MiniMapProjection.Project(Vector2.zero, center,
                RadiusMeters, RadiusPixels);
            var before = Mathf.Atan2(station.y, station.x);

            var clamped = station;
            var didClamp = MiniMapProjection.ClampToRim(ref clamped, RadiusPixels);

            Assert.That(didClamp, Is.True,
                "станция в 1697 м при радиусе 900 м обязана встать на обод");
            Assert.That(clamped.magnitude, Is.EqualTo(RadiusPixels).Within(1e-3f),
                "на ободе, а не за ним");

            var after = Mathf.Atan2(clamped.y, clamped.x);
            Assert.That(after, Is.EqualTo(before).Within(1e-4f),
                "прижатие не меняет направление: маркер не врёт, куда ехать");
        }

        [Test]
        public void NearGoalIsNotMoved()
        {
            var point = new Vector2(30f, -40f);
            var clamped = point;
            var didClamp = MiniMapProjection.ClampToRim(ref clamped, RadiusPixels);

            Assert.That(didClamp, Is.False);
            Assert.That(clamped, Is.EqualTo(point),
                "цель внутри дальности остаётся там, где она есть");
        }

        [Test]
        public void DegenerateInputsDoNotProduceNaN()
        {
            var point = MiniMapProjection.Project(new Vector2(100f, 100f),
                Vector2.zero, 0f, RadiusPixels);
            Assert.That(float.IsNaN(point.x) || float.IsNaN(point.y), Is.False,
                "нулевая дальность не должна рождать NaN");

            var clamped = new Vector2(10f, 10f);
            MiniMapProjection.ClampToRim(ref clamped, 0f);
            Assert.That(clamped, Is.EqualTo(Vector2.zero),
                "нулевой радиус круга — точка в центр, а не деление на ноль");
        }

        [Test]
        public void SegmentInsideDiscIsUnchanged()
        {
            Vector2 a, b;
            var ok = MiniMapProjection.TryClipToDisc(new Vector2(-20f, 10f),
                new Vector2(30f, -5f), RadiusPixels, out a, out b);

            Assert.That(ok, Is.True);
            Assert.That((a - new Vector2(-20f, 10f)).magnitude, Is.LessThan(1e-4f),
                "внутренний отрезок не двигается");
            Assert.That((b - new Vector2(30f, -5f)).magnitude, Is.LessThan(1e-4f),
                "и правый конец тоже");
        }

        [Test]
        public void SegmentCrossingRimIsTrimmedToRim()
        {
            Vector2 a, b;
            var ok = MiniMapProjection.TryClipToDisc(new Vector2(0f, 0f),
                new Vector2(0f, RadiusPixels * 2f), RadiusPixels, out a, out b);

            Assert.That(ok, Is.True);
            Assert.That(a.magnitude, Is.EqualTo(0f).Within(1e-3f),
                "внутренний конец не должен уезжать");
            Assert.That(b.magnitude, Is.EqualTo(RadiusPixels).Within(1e-3f),
                "внешний конец встаёт ровно на обод");
        }

        [Test]
        public void ChordWithBothEndsOutsideStillDrawsItsMiddle()
        {
            // Ровно тот случай, который и сломал кадр: оба конца за дальностью,
            // но отрезок проходит через круг.
            Vector2 a, b;
            var ok = MiniMapProjection.TryClipToDisc(
                new Vector2(-RadiusPixels * 3f, 40f),
                new Vector2(RadiusPixels * 3f, 40f), RadiusPixels, out a, out b);

            Assert.That(ok, Is.True,
                "отрезок через круг нельзя выбрасывать целиком");
            Assert.That(a.magnitude, Is.EqualTo(RadiusPixels).Within(1e-2f));
            Assert.That(b.magnitude, Is.EqualTo(RadiusPixels).Within(1e-2f));
            Assert.That(b.x, Is.GreaterThan(a.x), "порядок концов сохранён");
        }

        [Test]
        public void SegmentFarOutsideIsDropped()
        {
            Vector2 a, b;
            var ok = MiniMapProjection.TryClipToDisc(
                new Vector2(RadiusPixels * 2f, RadiusPixels * 2f),
                new Vector2(RadiusPixels * 3f, RadiusPixels * 2f), RadiusPixels,
                out a, out b);

            Assert.That(ok, Is.False, "мимо круга рисовать нечего");
        }

        [Test]
        public void DegenerateAndEmptyRadiusAreSafe()
        {
            Vector2 a, b;
            Assert.That(MiniMapProjection.TryClipToDisc(new Vector2(5f, 5f),
                new Vector2(5f, 5f), RadiusPixels, out a, out b), Is.True,
                "точка внутри круга остаётся точкой");
            Assert.That(MiniMapProjection.TryClipToDisc(new Vector2(900f, 900f),
                new Vector2(900f, 900f), RadiusPixels, out a, out b), Is.False,
                "точка снаружи не рисуется");
            Assert.That(MiniMapProjection.TryClipToDisc(Vector2.zero, Vector2.zero,
                0f, out a, out b), Is.False, "нулевой радиус — ничего нет");
        }

        [Test]
        public void SegmentLengthSkipsSubpixelNoise()
        {
            var tiny = MiniMapProjection.SegmentLength(new Vector2(0f, 0f),
                new Vector2(0.4f, 0f));
            Assert.That(tiny, Is.LessThan(2f),
                "отрезок меньше пикселя не должен попадать в иерархию UI");

            var span = MiniMapProjection.SegmentLength(new Vector2(0f, 0f),
                new Vector2(60f, 80f));
            Assert.That(span, Is.EqualTo(100f).Within(1e-3f));
        }
    }
}

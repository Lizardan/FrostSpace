using FrostSpace.Features.Scan;
using FrostSpace.Features.View;
using NUnit.Framework;

namespace FrostSpace.Tests
{
    /// <summary>
    /// Проверяет математику ленты модулей и чистку имён для подписи сканера.
    ///
    /// Сам HUD тестом не покрывается: он собирает Canvas и зависит от порядка
    /// вызовов Unity. Здесь только чистые функции, которые решают, какой модуль
    /// выбран и как выглядит имя объекта в подписи.
    /// </summary>
    public sealed class ModuleCarouselTests
    {
        // ---------- выбор модуля ----------

        [Test]
        public void Step_ScrollUpMovesForward()
        {
            Assert.AreEqual(1, ModuleCarousel.Step(0, 1f, 2));
        }

        [Test]
        public void Step_ScrollDownMovesBack()
        {
            Assert.AreEqual(0, ModuleCarousel.Step(1, -1f, 2));
        }

        [Test]
        public void Step_WrapsAroundBothEnds()
        {
            // Лента замкнута: у края выбор перескакивает на противоположный конец.
            Assert.AreEqual(0, ModuleCarousel.Step(1, 1f, 2));
            Assert.AreEqual(1, ModuleCarousel.Step(0, -1f, 2));
        }

        [Test]
        public void Step_IgnoresTrackpadNoise()
        {
            Assert.AreEqual(0, ModuleCarousel.Step(0, 0.004f, 2));
            Assert.AreEqual(1, ModuleCarousel.Step(1, -0.004f, 2));
        }

        [Test]
        public void Step_WithoutModules_StaysAtZero()
        {
            Assert.AreEqual(0, ModuleCarousel.Step(0, 1f, 0));
        }

        // ---------- лента ----------

        [Test]
        public void Offset_SelectedItemSitsInTheMiddle()
        {
            Assert.AreEqual(0f, ModuleCarousel.Offset(1, 1, 0f), 1e-5f);
            // Элемент выше выбранного уезжает вверх (−), ниже — вниз (+).
            Assert.AreEqual(-1f, ModuleCarousel.Offset(0, 1, 0f), 1e-5f);
            Assert.AreEqual(1f, ModuleCarousel.Offset(2, 1, 0f), 1e-5f);
        }

        [Test]
        public void Offset_ScrollShiftsTheWholeStrip()
        {
            Assert.AreEqual(0.4f, ModuleCarousel.Offset(1, 1, 0.4f), 1e-5f);
        }

        [Test]
        public void Emphasis_IsFullOnSelectedAndZeroOneStepAway()
        {
            Assert.AreEqual(1f, ModuleCarousel.Emphasis(0f), 1e-5f);
            Assert.AreEqual(0.5f, ModuleCarousel.Emphasis(0.5f), 1e-5f);
            Assert.AreEqual(0f, ModuleCarousel.Emphasis(1f), 1e-5f);
            Assert.AreEqual(0f, ModuleCarousel.Emphasis(-3f), 1e-5f);
        }

        [Test]
        public void Approach_ReachesTheTargetAndNeverOvershoots()
        {
            var value = 1f;
            for (var i = 0; i < 200; i++)
                value = ModuleCarousel.Approach(value, 0f, 16f, 1f / 60f);

            Assert.Less(value, 0.001f);
            Assert.GreaterOrEqual(value, 0f);
        }

        [Test]
        public void Approach_ZeroDeltaTimeKeepsTheValue()
        {
            Assert.AreEqual(0.7f, ModuleCarousel.Approach(0.7f, 0f, 16f, 0f), 1e-6f);
        }

        // ---------- подпись сканера ----------

        [Test]
        public void Sanitize_RemovesSuffixAndUnderscores()
        {
            Assert.AreEqual("SNOW CABIN", ScanTarget.Sanitize("Snow_Cabin"));
            Assert.AreEqual("HOVERBIKE", ScanTarget.Sanitize("Hoverbike_Model"));
        }

        [Test]
        public void Sanitize_FallsBackToUnknownForEmptyNames()
        {
            Assert.AreEqual("UNKNOWN", ScanTarget.Sanitize(null));
            Assert.AreEqual("UNKNOWN", ScanTarget.Sanitize("   "));
            Assert.AreEqual("UNKNOWN", ScanTarget.Sanitize("_Model"));
        }
    }
}

using UnityEngine;

namespace FrostSpace.Features.View
{
    /// <summary>
    /// Модуль восприятия, выбранный на HUD: правая кнопка мыши запускает его эффект.
    /// Оба эффекта живут в цифровом слое и используют один циановый акцент.
    /// </summary>
    public enum ViewModule
    {
        /// <summary>Круговой просвет сквозь перекрытие в точке перекрестия.</summary>
        Aperture = 0,

        /// <summary>Сканирование объекта: контур, бегущая полоса и подпись с именем.</summary>
        Scanner = 1
    }

    /// <summary>
    /// Чистая математика ленты модулей: ступенчатый выбор по колёсику и сглаженное
    /// смещение ленты к выбранному элементу. Вынесена из HUD, чтобы проверяться
    /// EditMode-тестом без сцены.
    /// </summary>
    public static class ModuleCarousel
    {
        /// <summary>Порог, ниже которого прокрутка считается дребезгом тачпада.</summary>
        public const float ScrollDeadZone = 0.01f;

        /// <summary>
        /// Следующий выбранный элемент по прокрутке: вверх — дальше по списку,
        /// вниз — назад. Без заворота: у края списка выбор стоит на месте.
        /// </summary>
        /// <summary>
        /// Следующий выбранный элемент по прокрутке: вверх — дальше по списку,
        /// вниз — назад. Список замкнут: с последнего вниз попадаешь на первый,
        /// с первого вверх — на последний.
        /// </summary>
        public static int Step(int index, float scroll, int count)
        {
            if (count <= 0)
                return 0;

            index = Mathf.Clamp(index, 0, count - 1);
            if (scroll > ScrollDeadZone)
                return (index + 1) % count;
            if (scroll < -ScrollDeadZone)
                return (index - 1 + count) % count;
            return index;
        }

        /// <summary>Позиция элемента в ленте: 0 — под перекрестием выбора.</summary>
        public static float Offset(int item, int selected, float scroll)
        {
            return item - selected + scroll;
        }

        /// <summary>
        /// Экспоненциальное сглаживание ленты: за <c>1 / smoothing</c> секунд остаток
        /// пути уменьшается в e раз. Кадронезависимо, поэтому WebGL и редактор ведут
        /// себя одинаково.
        /// </summary>
        public static float Approach(float current, float target, float smoothing, float deltaTime)
        {
            var rate = Mathf.Max(0.01f, smoothing);
            var t = 1f - Mathf.Exp(-rate * Mathf.Max(0f, deltaTime));
            return Mathf.Lerp(current, target, t);
        }

        /// <summary>
        /// Насколько элемент ленты «выбран» по своему положению: 1 в центре,
        /// 0 на расстоянии одного шага и дальше. Нужен для плавного затухания
        /// яркости при прокрутке, а не только для подсветки выбранного.
        /// </summary>
        public static float Emphasis(float offset)
        {
            return Mathf.Clamp01(1f - Mathf.Abs(offset));
        }
    }
}

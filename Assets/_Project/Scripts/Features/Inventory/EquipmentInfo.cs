namespace FrostSpace.Features.Inventory
{
    /// <summary>
    /// Подписи снаряжения для окна экипировки. Держится здесь, а не в экране:
    /// описание смотрит на предмет, а не на то, где он лежит, и не должно
    /// переписываться вместе с геометрией интерфейса.
    /// </summary>
    public static class EquipmentInfo
    {
        /// <summary>Род предмета — над названием.</summary>
        public static string KindLabel(EquipmentKind kind) => "Оборудование";

        /// <summary>Название, как его знает игрок.</summary>
        public static string Title(EquipmentKind kind)
        {
            return kind switch
            {
                EquipmentKind.Scanner => "Сонар курьерский",
                EquipmentKind.Map => "Карта пояса",
                _ => "Предмет",
            };
        }

        /// <summary>Что предмет делает — одна-две строки, без обучения.</summary>
        public static string Description(EquipmentKind kind)
        {
            return kind switch
            {
                EquipmentKind.Scanner =>
                    "Круговая волна показывает тёплые узлы, руины и груз сквозь метель.\n" +
                    "Q — один импульс, подписи держатся несколько секунд.",
                EquipmentKind.Map =>
                    "Миникарта в правом верхнем углу: блоки, узлы, руины, цель\n" +
                    "и стрелка взгляда. Ориентация — на север.",
                _ => string.Empty,
            };
        }

        /// <summary>
        /// Короткое имя для подписи под слотом: полное название не влезает в
        /// 140 px между двумя соседними слотами и накладывается на чужую подпись.
        /// </summary>
        public static string ShortTitle(EquipmentKind kind)
        {
            return kind switch
            {
                EquipmentKind.Scanner => "СОНАР",
                EquipmentKind.Map => "КАРТА",
                _ => "СЛОТ",
            };
        }

        /// <summary>Клавиша, которую предмет включает, или пусто, если клавиши нет.</summary>
        public static string KeyHint(EquipmentKind kind)
        {
            return kind switch
            {
                EquipmentKind.Scanner => "Q",
                _ => string.Empty,
            };
        }
    }
}

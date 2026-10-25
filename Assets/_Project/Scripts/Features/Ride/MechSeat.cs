using UnityEngine;

namespace FrostSpace.Features.Ride
{
    /// <summary>
    /// Посадочная точка, которая едет за костью ходуна.
    ///
    /// Игрок садится в кабину внутри груди, и кабина ходит вместе с корпусом:
    /// на шаге тело ходуна качается вверх-вниз, и неподвижное сиденье оставляло бы
    /// наездника висеть то в проёме, то над ним. Поэтому якорь сиденья — не кость
    /// (у неё масштаб арматуры 100, и наездник унаследовал бы его), а обычный
    /// объект корня, который каждый кадр встаёт в точку кости `Chest`.
    ///
    /// Поворот якоря берётся у корня транспорта, а не у кости: качание корпуса
    /// должно двигать наездника, но не наклонять его — иначе на шаге робот
    /// заваливался бы вместе с грудью. Якорь двигается в LateUpdate до
    /// <see cref="RiderPose"/> (у позы порядок 220), чтобы поза считалась
    /// по уже сдвинутому сиденью.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class MechSeat : MonoBehaviour
    {
        [Tooltip("Кость корпуса, за которой едет сиденье; пусто — ищется по имени Chest/Torso")]
        [SerializeField] private Transform _chest;

        [Tooltip("Смещение сиденья от кости в осях транспорта, м (считается при старте)")]
        [SerializeField] private Vector3 _offset;

        private Transform _root;

        /// <summary>Смещение сиденья от кости в осях транспорта, м.</summary>
        public Vector3 Offset
        {
            get => _offset;
            set => _offset = value;
        }

        /// <summary>Кость корпуса, за которой едет сиденье.</summary>
        public Transform Chest
        {
            get => _chest;
            set => _chest = value;
        }

        /// <summary>
        /// Имена кости корпуса по ригам пака: у `Mech_*` это `Chest`, у
        /// `Enemy_Large` человеческий риг, и грудной кости там `Torso`.
        /// </summary>
        private static readonly string[] ChestBones = { "Chest", "Torso" };

        private void Awake()
        {
            _root = transform.root;
            if (_chest == null)
            {
                foreach (var boneName in ChestBones)
                {
                    _chest = FindDeep(_root, boneName);
                    if (_chest != null)
                        break;
                }
            }

            // Смещение меряется здесь, а не хранится в префабе: rest-поза костей
            // в FBX слегка гуляет от пересборки к пересборке (экспорт учитывает
            // позу, в которой оставлена арматура), и записанное число устаревало
            // бы после каждого прогона пайплайна. Позиция самого сиденья — из
            // манифеста, её и проверяет MechVisualPrefabTests.
            if (_chest != null)
            {
                var rotation = _root != null ? _root.rotation : Quaternion.identity;
                _offset = Quaternion.Inverse(rotation) * (transform.position - _chest.position);
            }
        }

        private void LateUpdate()
        {
            if (_chest == null)
                return;

            // Корень мог смениться (префаб вложили в другую сцену) — берём текущий.
            if (_root == null)
                _root = transform.root;

            var rotation = _root != null ? _root.rotation : Quaternion.identity;
            transform.position = _chest.position + rotation * _offset;
            transform.rotation = rotation;
        }

        private static Transform FindDeep(Transform root, string boneName)
        {
            if (root == null)
                return null;

            if (root.name == boneName)
                return root;

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), boneName);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}

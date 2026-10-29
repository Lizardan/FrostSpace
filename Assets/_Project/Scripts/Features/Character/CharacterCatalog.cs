using System;
using UnityEngine;

namespace FrostSpace.Features.Character
{
    /// <summary>
    /// Список корпусов для экрана выбора. Порядок элементов — порядок карточек;
    /// <c>_default</c> используется, когда сохранённого выбора нет или он устарел.
    /// </summary>
    [CreateAssetMenu(
        menuName = "FrostSpace/Character Catalog",
        fileName = "CharacterCatalog")]
    public sealed class CharacterCatalog : ScriptableObject
    {
        [SerializeField] private RobotProfile _default;
        [SerializeField] private RobotProfile[] _profiles = Array.Empty<RobotProfile>();

        public RobotProfile[] Profiles => _profiles;

        public RobotProfile Default
        {
            get
            {
                if (_default != null)
                    return _default;

                return _profiles.Length > 0 ? _profiles[0] : null;
            }
        }

        /// <summary>Поиск по идентификатору; null, если такого корпуса больше нет.</summary>
        public RobotProfile Find(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            for (var i = 0; i < _profiles.Length; i++)
            {
                if (_profiles[i] != null && _profiles[i].Id == id)
                    return _profiles[i];
            }

            return null;
        }
    }
}

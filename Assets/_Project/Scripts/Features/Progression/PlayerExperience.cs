using UnityEngine;

namespace FrostSpace.Features.Progression
{
    /// <summary>
    /// Опыт игрока и кривая уровней. Состояние живёт на сценовом актёре, статики
    /// нет; опыт начисляют квесты и другие сценовые системы через <see cref="AddExperience"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerExperience : MonoBehaviour
    {
        [Header("Старт")]
        [SerializeField, Min(1)] private int _level = 1;
        [SerializeField, Min(0)] private int _experience;

        [Header("Кривая")]
        [Tooltip("Опыт до второго уровня")]
        [SerializeField, Min(1)] private int _baseRequirement = 1000;
        [Tooltip("Во сколько раз растёт требование на каждом уровне")]
        [SerializeField, Min(1f)] private float _growth = 1.35f;

        public int Level => _level;

        public int Experience => _experience;

        /// <summary>Сколько опыта нужно до следующего уровня.</summary>
        public int ExperienceToNextLevel =>
            Mathf.Max(1, Mathf.RoundToInt(
                _baseRequirement * Mathf.Pow(_growth, Mathf.Max(0, _level - 1))));

        /// <summary>Заполнение полосы от 0 до 1.</summary>
        public float Progress01 =>
            Mathf.Clamp01((float)_experience / ExperienceToNextLevel);

        /// <summary>Начисляет опыт и повышает уровни, пока хватает требования.</summary>
        public void AddExperience(int amount)
        {
            if (amount <= 0)
                return;

            _experience += amount;
            while (_experience >= ExperienceToNextLevel)
            {
                _experience -= ExperienceToNextLevel;
                _level++;
            }
        }

        private void OnValidate()
        {
            _level = Mathf.Max(1, _level);
            _experience = Mathf.Max(0, _experience);
            _baseRequirement = Mathf.Max(1, _baseRequirement);
            _growth = Mathf.Max(1f, _growth);
        }
    }
}

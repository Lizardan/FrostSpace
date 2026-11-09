using FrostSpace.Features.Quest;
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
        public int ExperienceToNextLevel => RequirementFor(_level);

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

        /// <summary>
        /// Восстанавливает счётчик из сохранения. Нулевой уровень читается как первый:
        /// так старые записи без полей опыта остаются совместимыми.
        /// </summary>
        public bool Restore(int level, int experience)
        {
            var restoredLevel = level < 1 ? 1 : level;
            if (experience < 0 || experience >= RequirementFor(restoredLevel))
                return false;

            _level = restoredLevel;
            _experience = experience;
            return true;
        }

        private void Awake()
        {
            if (ProgressSave.TryLoad(out var data))
                Restore(data.level, data.experience);
        }

        private int RequirementFor(int level) =>
            Mathf.Max(1, Mathf.RoundToInt(
                _baseRequirement * Mathf.Pow(_growth, Mathf.Max(0, level - 1))));

        private void OnValidate()
        {
            _level = Mathf.Max(1, _level);
            _experience = Mathf.Max(0, _experience);
            _baseRequirement = Mathf.Max(1, _baseRequirement);
            _growth = Mathf.Max(1f, _growth);
        }
    }
}

using UnityEngine;

namespace FrostSpace.Features.Character
{
    /// <summary>
    /// Выбираемый корпус игрока: визуальный префаб и множители к базовым
    /// значениям сценового актёра (ADR 0028).
    ///
    /// Базовые скорости и высота прыжка заданы на сцене в
    /// <c>RobotLocomotion</c>; профиль их не переписывает, а масштабирует.
    /// Поэтому корпуса можно балансировать данными, не трогая сцены.
    /// </summary>
    [CreateAssetMenu(
        menuName = "FrostSpace/Character Profile",
        fileName = "RobotProfile")]
    public sealed class RobotProfile : ScriptableObject
    {
        [Header("Описание")]
        [Tooltip("Идентификатор для PlayerPrefs и тестов, например R1_PolarMechanic")]
        [SerializeField] private string _id = "R1_PolarMechanic";
        [Tooltip("Строка карточки: латиница, атлас меню без кириллицы")]
        [SerializeField] private string _displayName = "POLAR MECHANIC";
        [Tooltip("Роль одной строкой")]
        [SerializeField] private string _role = "UNIVERSAL";
        [TextArea(2, 4)]
        [SerializeField] private string _description;

        [Header("Визуал")]
        [Tooltip("R*_Model.prefab: только модель и Animator, без игровой логики")]
        [SerializeField] private GameObject _visualPrefab;

        [Header("Множители к базовым значениям сцены")]
        [SerializeField] private float _walkMultiplier = 1f;
        [SerializeField] private float _sprintMultiplier = 1f;
        [Tooltip("Скорость переноски предмета относительно ходьбы")]
        [SerializeField] private float _carryMultiplier = 1f;
        [SerializeField] private float _jumpMultiplier = 1f;

        public string Id => _id;
        public string DisplayName => _displayName;
        public string Role => _role;
        public string Description => _description;
        public GameObject VisualPrefab => _visualPrefab;

        public float WalkMultiplier => Mathf.Max(0.01f, _walkMultiplier);
        public float SprintMultiplier => Mathf.Max(0.01f, _sprintMultiplier);
        public float CarryMultiplier => Mathf.Max(0.01f, _carryMultiplier);
        public float JumpMultiplier => Mathf.Max(0.01f, _jumpMultiplier);
    }
}

using FrostSpace.Features.Locomotion;
using FrostSpace.Features.View;
using UnityEngine;

namespace FrostSpace.Features.Character
{
    /// <summary>
    /// Ставит на сценового актёра визуал выбранного корпуса и перепривязывает
    /// к нему системы, которые держат ссылки на кости (ADR 0028).
    ///
    /// Порядок важен: биндер выполняется раньше остальных компонентов
    /// (<c>-200</c>), поэтому их <c>Awake</c> уже видит новый Animator, кость
    /// головы и <c>SkinnedMeshRenderer</c>. Визуал — дочерний объект без
    /// игровой логики; <c>CharacterController</c> и ввод остаются на актёре.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class PlayerVisualBinder : MonoBehaviour
    {
        [Header("Данные")]
        [Tooltip("Каталог корпусов: выбранный профиль и значение по умолчанию")]
        [SerializeField] private CharacterCatalog _catalog;

        [Header("Ссылки сценового актёра")]
        [SerializeField] private RobotLocomotion _locomotion;
        [SerializeField] private PlayerCameraRig _rig;
        [SerializeField] private PlayerLocalView _localView;
        [SerializeField] private PlayerAimPose _aimPose;

        [Tooltip("Куда кладётся визуал; пусто — сам актёр")]
        [SerializeField] private Transform _visualParent;

        private GameObject _visual;

        public RobotProfile Profile { get; private set; }
        public Animator Animator { get; private set; }

        private void Awake()
        {
            ResolveReferences();

            var profile = CharacterSelection.Resolve(_catalog);
            if (profile == null)
            {
                Debug.LogError(
                    "PlayerVisualBinder: не выбран корпус — нет каталога или профилей", this);
                return;
            }

            Apply(profile);
        }

        /// <summary>
        /// Сменить корпус на лету: старый визуал удаляется, новый поднимается
        /// на том же актёре. Нужен экрану выбора для превью и смене героя.
        /// </summary>
        public void Apply(RobotProfile profile)
        {
            if (profile == null)
            {
                Debug.LogError("PlayerVisualBinder: профиль не задан", this);
                return;
            }

            if (profile.VisualPrefab == null)
            {
                Debug.LogError(
                    $"PlayerVisualBinder: у профиля {profile.Id} нет визуального префаба", this);
                return;
            }

            RemoveVisual();

            var parent = _visualParent != null ? _visualParent : transform;
            _visual = Instantiate(profile.VisualPrefab, parent, false);
            _visual.name = profile.VisualPrefab.name;
            _visual.transform.localPosition = Vector3.zero;
            _visual.transform.localRotation = Quaternion.identity;

            Animator = _visual.GetComponentInChildren<Animator>();
            if (Animator == null)
                Debug.LogError($"PlayerVisualBinder: у {profile.Id} нет Animator", this);

            var head = FindBone("Head");
            var neck = FindBone("neck_01");
            var skin = _visual.GetComponentInChildren<SkinnedMeshRenderer>(true);

            _locomotion?.RebindAnimator(Animator);
            _locomotion?.ApplyProfile(
                profile.WalkMultiplier,
                profile.SprintMultiplier,
                profile.CarryMultiplier,
                profile.JumpMultiplier);
            _rig?.RebindHead(head);
            _localView?.RebindHead(head);
            _aimPose?.Rebind(neck, head);

            Profile = profile;
            Debug.Log($"[PlayerVisualBinder] корпус {profile.Id}", this);
        }

        private void RemoveVisual()
        {
            if (_visual == null)
                return;

            _visual.SetActive(false);
            Destroy(_visual);
            _visual = null;
            Animator = null;
        }

        /// <summary>Кость ищется по имени рига: путь в FBX длинный и меняется при реимпорте.</summary>
        private Transform FindBone(string boneName)
        {
            if (_visual == null)
                return null;

            var transforms = _visual.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name == boneName)
                    return transforms[i];
            }

            return null;
        }

        private void ResolveReferences()
        {
            if (_locomotion == null)
                _locomotion = GetComponent<RobotLocomotion>();
            if (_rig == null)
                _rig = GetComponent<PlayerCameraRig>();
            if (_localView == null)
                _localView = GetComponent<PlayerLocalView>();
            if (_aimPose == null)
                _aimPose = GetComponent<PlayerAimPose>();
        }
    }
}

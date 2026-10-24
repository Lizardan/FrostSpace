using System.Collections.Generic;
using UnityEngine;

namespace FrostSpace.Features.Menu
{
    /// <summary>Сюжетная поза робота в главном меню.</summary>
    public enum MenuPose
    {
        /// <summary>Стоит и поник: плечи вниз, голова опущена. Ожидание без надежды.</summary>
        Slump,

        /// <summary>Тянется руками к железу: чинит генератор, стол, корпус.</summary>
        Repair,

        /// <summary>Стоит на коленях и работает руками в открытом люке.</summary>
        Kneel,

        /// <summary>Держит лампу высоко — светит второму роботу.</summary>
        HoldLamp,

        /// <summary>Сидит на ящике: бёдра вперёд, голени вниз.</summary>
        Sit,

        /// <summary>Несёт груз перед собой: корпус вперёд, руки на грузе.</summary>
        Carry
    }

    /// <summary>
    /// Процедурная поза робота поверх клипа Animator: меню нужно «чинит»,
    /// «стоит на коленях», «держит лампу» и «сидит», а таких клипов в наборе нет.
    ///
    /// Поза повторяет схему <c>RiderPose</c>: кости находятся по именам рига,
    /// их локальные значения снимаются один раз и возвращаются каждый кадр перед
    /// правкой, руки решаются двумя костями в точки хвата. Возврат к снимку
    /// обязателен: Animator пишет не все кости каждый кадр, и без него повороты
    /// складывались бы сами с собой.
    ///
    /// Углы считаются от <see cref="Time.unscaledTime"/>, поэтому поза не зависит
    /// от паузы и смены <c>Time.timeScale</c>. Снимок берётся в первом
    /// <c>LateUpdate</c>, а не в <c>Awake</c>: в <c>Awake</c> Animator ещё не
    /// выставил позу клипа, и за базу попала бы T-поза.
    /// </summary>
    [DefaultExecutionOrder(220)]
    [DisallowMultipleComponent]
    public sealed class MenuRobotPose : MonoBehaviour
    {
        [Header("Поза")]
        [SerializeField] private MenuPose _pose = MenuPose.Slump;

        [Tooltip("Точка левой кисти (пусто — руки опущены вдоль корпуса)")]
        [SerializeField] private Transform _handTargetLeft;

        [Tooltip("Точка правой кисти (пусто — руки опущены вдоль корпуса)")]
        [SerializeField] private Transform _handTargetRight;

        [Header("Корпус")]
        [Tooltip("Множитель наклона корпуса: 1 — авторский угол позы")]
        [SerializeField] private float _leanScale = 1f;

        [Tooltip("Насколько таз опускается к земле в позах Kneel и Sit, м")]
        [SerializeField] private float _pelvisDrop = 0.45f;

        [Header("Живость")]
        [Tooltip("Дыхание корпуса, градусы")]
        [SerializeField] private float _breathDegrees = 1.5f;

        [Tooltip("Период дыхания, с")]
        [SerializeField] private float _breathSeconds = 6.5f;

        [Tooltip("Размах возни руками в позе Repair, м")]
        [SerializeField] private float _tinkerAmplitude = 0.05f;

        [Tooltip("Период возни руками, с")]
        [SerializeField] private float _tinkerSeconds = 3.2f;

        [Tooltip("Скорость входа в позу, 1/с")]
        [SerializeField] private float _weightSpeed = 3.5f;

        private Transform _modelRoot;
        private Transform _pelvis;
        private Transform _spine;
        private Transform _spineTop;
        private Transform _neck;
        private Transform _head;
        private Transform _upperArmL;
        private Transform _lowerArmL;
        private Transform _handL;
        private Transform _indexL;
        private Transform _upperArmR;
        private Transform _lowerArmR;
        private Transform _handR;
        private Transform _indexR;
        private Transform _thighL;
        private Transform _calfL;
        private Transform _footL;
        private Transform _ballL;
        private Transform _thighR;
        private Transform _calfR;
        private Transform _footR;
        private Transform _ballR;

        private bool _captured;
        private float _weight;
        private Vector3 _rootLocalPosition;

        private Transform[] _posedBones;
        private Vector3[] _restPositions;
        private Quaternion[] _restRotations;

        /// <summary>Текущая поза.</summary>
        public MenuPose Pose => _pose;

        /// <summary>Вес позы 0..1: 0 — чистый клип Animator.</summary>
        public float Weight => _weight;

        /// <summary>Поза задаётся сцене до старта, но менять её можно и на ходу.</summary>
        public void SetPose(MenuPose pose)
        {
            _pose = pose;
        }

        /// <summary>Точки хвата: левая и правая кисть. Пустой аргумент оставляет руку свободной.</summary>
        public void SetHandTargets(Transform left, Transform right)
        {
            _handTargetLeft = left;
            _handTargetRight = right;
        }

        private void OnEnable()
        {
            if (_modelRoot == null)
                ResolveBones();

            // Вид меню выключается целиком, и OnEnable приходит на каждый возврат.
            // Снимок и вес позы сохраняются: иначе робот каждый раз заново входил
            // в позу из клипа, и это читалось как рывок. Сброс — только в первый раз.
            if (!_captured)
                _weight = 0f;
        }

        /// <summary>
        /// Поиск костей идёт только внутри модели (Animator): на сцене рядом стоят
        /// другие роботы с теми же именами костей.
        /// </summary>
        private void ResolveBones()
        {
            var animator = GetComponentInChildren<Animator>(true);
            _modelRoot = animator != null ? animator.transform : transform;
            _rootLocalPosition = _modelRoot.localPosition;

            _pelvis = FindBone("pelvis");
            _spine = FindBone("spine_01");
            _spineTop = FindBone("spine_02");
            _neck = FindBone("neck_01");
            _head = FindBone("Head");

            _upperArmL = FindBone("upperarm_l");
            _lowerArmL = FindBone("lowerarm_l");
            _handL = FindBone("hand_l");
            _indexL = FindBone("index_01_l");
            _upperArmR = FindBone("upperarm_r");
            _lowerArmR = FindBone("lowerarm_r");
            _handR = FindBone("hand_r");
            _indexR = FindBone("index_01_r");

            _thighL = FindBone("thigh_l");
            _calfL = FindBone("calf_l");
            _footL = FindBone("foot_l");
            _ballL = FindBone("ball_l");
            _thighR = FindBone("thigh_r");
            _calfR = FindBone("calf_r");
            _footR = FindBone("foot_r");
            _ballR = FindBone("ball_r");

            if (_pelvis == null || _spine == null || _upperArmL == null || _upperArmR == null)
            {
                Debug.LogError(
                    $"[MenuRobotPose] {name}: не найдены кости рига — поза «{_pose}» не будет работать", this);
                return;
            }

            Debug.Log($"[MenuRobotPose] {name}: кости разрешены, поза «{_pose}»", this);
        }

        /// <summary>Кость ищется по имени рига: путь в FBX длинный и меняется при реимпорте.</summary>
        private Transform FindBone(string boneName)
        {
            var root = _modelRoot != null ? _modelRoot : transform;
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name == boneName)
                    return transforms[i];
            }

            return null;
        }

        /// <summary>Снимок локальных значений костей: база, к которой возвращается каждый кадр.</summary>
        private void Capture()
        {
            var bones = new List<Transform>(20);
            AddBone(bones, _pelvis);
            AddBone(bones, _spine);
            AddBone(bones, _spineTop);
            AddBone(bones, _neck);
            AddBone(bones, _head);
            AddBone(bones, _upperArmL);
            AddBone(bones, _lowerArmL);
            AddBone(bones, _handL);
            AddBone(bones, _upperArmR);
            AddBone(bones, _lowerArmR);
            AddBone(bones, _handR);
            AddBone(bones, _thighL);
            AddBone(bones, _calfL);
            AddBone(bones, _footL);
            AddBone(bones, _thighR);
            AddBone(bones, _calfR);
            AddBone(bones, _footR);

            _posedBones = bones.ToArray();
            _restPositions = new Vector3[_posedBones.Length];
            _restRotations = new Quaternion[_posedBones.Length];
            for (var i = 0; i < _posedBones.Length; i++)
            {
                _restPositions[i] = _posedBones[i].localPosition;
                _restRotations[i] = _posedBones[i].localRotation;
            }

            _captured = true;
        }

        private static void AddBone(List<Transform> bones, Transform bone)
        {
            if (bone != null)
                bones.Add(bone);
        }

        /// <summary>Возврат к снимку клипа перед правкой: иначе правки складываются сами с собой.</summary>
        private void ApplyBasePose()
        {
            if (_posedBones == null)
                return;

            for (var i = 0; i < _posedBones.Length; i++)
            {
                if (_posedBones[i] == null)
                    continue;

                _posedBones[i].localPosition = _restPositions[i];
                _posedBones[i].localRotation = _restRotations[i];
            }
        }

        private void LateUpdate()
        {
            if (_pelvis == null)
                return;

            // Первый кадр только снимаем базу: в Awake Animator ещё не выставил клип.
            if (!_captured)
            {
                Capture();
                return;
            }

            ApplyBasePose();
            _weight = Mathf.MoveTowards(_weight, 1f, _weightSpeed * Time.unscaledDeltaTime);
            if (_weight <= 0.001f)
                return;

            var time = Time.unscaledTime;

            ApplyRootDrop();
            PoseTorso(time);
            PoseArms(time);
            PoseLegs();
        }

        // ------------------------------------------------------------------ корпус

        private void PoseTorso(float time)
        {
            var lean = LeanForPose() * _leanScale;
            var headDown = HeadDownForPose();
            lean += Mathf.Sin(time * Mathf.PI * 2f / Mathf.Max(_breathSeconds, 0.1f)) * _breathDegrees;

            var right = transform.right;
            RotateAround(_spineTop != null ? _spineTop : _spine, Quaternion.AngleAxis(lean, right));

            if (_neck != null)
                RotateAround(_neck, Quaternion.AngleAxis(-lean * 0.45f + headDown, right));

            if (_head != null)
                RotateAround(_head, Quaternion.AngleAxis(headDown * 0.35f, right));
        }

        private float LeanForPose()
        {
            switch (_pose)
            {
                case MenuPose.Repair: return 34f;
                case MenuPose.Kneel: return 26f;
                case MenuPose.HoldLamp: return -5f;
                case MenuPose.Sit: return 6f;
                case MenuPose.Carry: return 14f;
                default: return 10f;
            }
        }

        private float HeadDownForPose()
        {
            switch (_pose)
            {
                case MenuPose.Repair: return 12f;
                case MenuPose.Kneel: return 16f;
                case MenuPose.HoldLamp: return -10f;
                case MenuPose.Sit: return 14f;
                case MenuPose.Carry: return 6f;
                default: return 28f;
            }
        }

        // -------------------------------------------------------------------- руки

        private void PoseArms(float time)
        {
            var hasLeft = _handTargetLeft != null;
            var hasRight = _handTargetRight != null;

            var tinker = Vector3.zero;
            if (_pose == MenuPose.Repair || _pose == MenuPose.Kneel)
            {
                var phase = time * Mathf.PI * 2f / Mathf.Max(_tinkerSeconds, 0.1f);
                tinker = transform.forward * (Mathf.Sin(phase) * _tinkerAmplitude) +
                         transform.up * (Mathf.Cos(phase * 0.8f) * _tinkerAmplitude * 0.7f);
            }

            if (hasLeft)
                PoseArm(_upperArmL, _lowerArmL, _handL, _indexL, _handTargetLeft.position + tinker, -1f);
            else
                HangArm(_upperArmL, _lowerArmL, _handL, _indexL, -1f);

            if (hasRight)
                PoseArm(_upperArmR, _lowerArmR, _handR, _indexR, _handTargetRight.position + tinker, 1f);
            else
                HangArm(_upperArmR, _lowerArmR, _handR, _indexR, 1f);
        }

        /// <summary>Двухкостный IK: плечо и локоть доводятся так, чтобы кисть встала в точку.</summary>
        private void PoseArm(
            Transform upper, Transform lower, Transform hand, Transform finger,
            Vector3 target, float side)
        {
            if (upper == null || lower == null || hand == null)
                return;

            var upperLength = Vector3.Distance(upper.position, lower.position);
            var lowerLength = Vector3.Distance(lower.position, hand.position);
            if (upperLength < 1e-4f || lowerLength < 1e-4f)
                return;

            var toTarget = target - upper.position;
            var reach = Mathf.Clamp(
                toTarget.magnitude,
                Mathf.Abs(upperLength - lowerLength) + 0.02f,
                upperLength + lowerLength - 0.02f);
            var direction = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : transform.forward;

            var cos = Mathf.Clamp(
                (upperLength * upperLength + reach * reach - lowerLength * lowerLength) /
                (2f * upperLength * reach), -1f, 1f);
            var angle = Mathf.Acos(cos);

            // Локоть уходит вниз и наружу, а не в корпус.
            var pole = Vector3.ProjectOnPlane(-transform.up + transform.right * (side * 0.85f), direction);
            if (pole.sqrMagnitude < 1e-5f)
                pole = Vector3.ProjectOnPlane(Vector3.down, direction);
            pole.Normalize();

            var elbowDirection = direction * Mathf.Cos(angle) + pole * Mathf.Sin(angle);

            AimBone(upper, lower, elbowDirection);
            AimBone(lower, hand, target - lower.position);

            if (finger != null)
                AimBone(hand, finger, (transform.forward * 0.6f - transform.up).normalized);
        }

        /// <summary>Свободная рука: висит вдоль корпуса, чуть наружу.</summary>
        private void HangArm(
            Transform upper, Transform lower, Transform hand, Transform finger, float side)
        {
            if (upper == null || lower == null)
                return;

            var hang = (-transform.up + transform.right * (side * 0.16f) +
                        transform.forward * 0.05f).normalized;
            AimBone(upper, lower, hang);

            if (hand != null)
                AimBone(lower, hand, (-transform.up + transform.right * (side * 0.05f)).normalized);

            if (hand != null && finger != null)
                AimBone(hand, finger, (-transform.up * 0.6f + transform.forward * 0.8f).normalized);
        }

        // -------------------------------------------------------------------- ноги

        private void PoseLegs()
        {
            if (_pose == MenuPose.Kneel)
            {
                KneelLeg(_thighL, _calfL, _footL, _ballL, 0.62f);
                KneelLeg(_thighR, _calfR, _footR, _ballR, 0.50f);
            }
            else if (_pose == MenuPose.Sit)
            {
                SitLeg(_thighL, _calfL, _footL, _ballL);
                SitLeg(_thighR, _calfR, _footR, _ballR);
            }
        }

        /// <summary>Колено на снегу: бедро вперёд-вниз, голень назад вдоль земли.</summary>
        private void KneelLeg(
            Transform thigh, Transform calf, Transform foot, Transform ball, float forward)
        {
            if (thigh == null || calf == null)
                return;

            var thighDirection = (transform.forward * forward - transform.up * 0.78f).normalized;
            var calfDirection = (-transform.forward * 0.94f - transform.up * 0.34f).normalized;
            var footDirection = (-transform.forward * 0.9f + transform.up * 0.12f).normalized;

            AimBone(thigh, calf, thighDirection);
            if (foot != null)
                AimBone(calf, foot, calfDirection);
            if (foot != null && ball != null)
                AimBone(foot, ball, footDirection);
        }

        /// <summary>Сидит на ящике: бёдра вперёд, голени вниз, стопы на земле.</summary>
        private void SitLeg(Transform thigh, Transform calf, Transform foot, Transform ball)
        {
            if (thigh == null || calf == null)
                return;

            var thighDirection = (transform.forward * 0.96f - transform.up * 0.28f).normalized;
            var calfDirection = (-transform.up * 0.96f + transform.forward * 0.14f).normalized;
            var footDirection = (transform.forward * 0.94f - transform.up * 0.2f).normalized;

            AimBone(thigh, calf, thighDirection);
            if (foot != null)
                AimBone(calf, foot, calfDirection);
            if (foot != null && ball != null)
                AimBone(foot, ball, footDirection);
        }

        /// <summary>Таз опускается к земле: иначе колени и бёдра висят в воздухе.</summary>
        private void ApplyRootDrop()
        {
            if (_modelRoot == null)
                return;

            var drop = _pose == MenuPose.Kneel
                ? _pelvisDrop
                : _pose == MenuPose.Sit ? _pelvisDrop * 0.62f : 0f;

            _modelRoot.localPosition = _rootLocalPosition - Vector3.up * (drop * _weight);
        }

        // ------------------------------------------------------------------ математика

        private void RotateAround(Transform bone, Quaternion worldDelta)
        {
            if (bone == null)
                return;

            bone.rotation = Quaternion.Slerp(Quaternion.identity, worldDelta, _weight) * bone.rotation;
        }

        /// <summary>
        /// Доворот кости так, чтобы её дочерняя кость смотрела в заданном направлении.
        /// Направление берётся по фактическому положению дочерней кости, поэтому
        /// ориентация осей в рест-позе конкретного рига не важна.
        /// </summary>
        private void AimBone(Transform bone, Transform child, Vector3 desiredDirection)
        {
            if (bone == null || child == null)
                return;
            if (desiredDirection.sqrMagnitude < 1e-8f)
                return;

            var current = child.position - bone.position;
            if (current.sqrMagnitude < 1e-8f)
                return;

            var delta = Quaternion.FromToRotation(current.normalized, desiredDirection.normalized);
            bone.rotation = Quaternion.Slerp(Quaternion.identity, delta, _weight) * bone.rotation;
        }
    }
}

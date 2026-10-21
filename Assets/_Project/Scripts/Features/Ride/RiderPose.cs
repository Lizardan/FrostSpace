using System.Collections.Generic;
using UnityEngine;

namespace FrostSpace.Features.Ride
{
    /// <summary>
    /// Поза наездника: пока игрок на ховербайке, робот сидит на сиденье, наклоняет
    /// корпус к рулю и держится руками за рукоятки.
    ///
    /// Поза не анимируется клипом, а собирается поверх клипа каждый кадр (LateUpdate,
    /// после Animator): корпус наклоняется вокруг оси сиденья, бёдра и голени
    /// доворачиваются к посадке, руки решаются двумя костями в точки хвата. Поэтому
    /// посадка работает на обоих байках и не зависит от наличия клипа «езды».
    ///
    /// Перед первой правкой снимаются локальные позиции и повороты всех затрагиваемых
    /// костей, а на сходе возвращаются обратно: Animator пишет не все кости каждый кадр,
    /// и без этого снимка поза наездника оставалась в модели после спешивания.
    ///
    /// Точки хвата считаются от сиденья: у модели A руль стоит ровно на
    /// <c>0.86 м</c> вперёд и <c>0.06 м</c> выше посадочной точки, у B — те же
    /// пропорции, а отдельного объекта руля в модели B нет.
    /// </summary>
    [DefaultExecutionOrder(220)]
    [DisallowMultipleComponent]
    public sealed class RiderPose : MonoBehaviour
    {
        [Header("Кости (пусто — ищутся по имени в модели)")]
        [SerializeField] private Transform _pelvis;
        [SerializeField] private Transform _spine;
        [SerializeField] private Transform _neck;

        [Header("Наклон корпуса")]
        [Tooltip("Наклон корпуса к рулю, градусы")]
        [SerializeField] private float _leanAngle = 26f;
        [Tooltip("Доля наклона, которую забирает шея — чтобы робот смотрел вперёд, а не в бак")]
        [SerializeField, Range(0f, 1f)] private float _neckShare = 0.6f;

        [Header("Точки хвата")]
        [Tooltip("Вынос руля вперёд от сиденья, м")]
        [SerializeField] private float _gripForward = 0.86f;
        [Tooltip("Высота руля над сиденьем, м")]
        [SerializeField] private float _gripUp = 0.06f;
        [Tooltip("Полуширина руля: расстояние от центра до рукоятки, м")]
        [SerializeField] private float _gripHalfWidth = 0.22f;

        [Header("Ноги")]
        [Tooltip("Насколько колени опущены относительно сиденья")]
        [SerializeField] private float _kneeDrop = 0.15f;
        [Tooltip("Насколько колени разведены в стороны — иначе ноги входят в корпус байка")]
        [SerializeField] private float _kneeSplay = 0.35f;
        [Tooltip("Насколько голени уходят назад под корпус")]
        [SerializeField] private float _shinBack = 0.2f;

        [Header("Вес позы")]
        [Tooltip("Скорость входа в позу, 1/с")]
        [SerializeField] private float _weightSpeed = 7f;

        private Transform _seat;
        private Transform _modelRoot;
        private Transform _thighL;
        private Transform _calfL;
        private Transform _footL;
        private Transform _ballL;
        private Transform _thighR;
        private Transform _calfR;
        private Transform _footR;
        private Transform _ballR;
        private Transform _upperArmL;
        private Transform _lowerArmL;
        private Transform _handL;
        private Transform _indexL;
        private Transform _upperArmR;
        private Transform _lowerArmR;
        private Transform _handR;
        private Transform _indexR;
        private float _weight;
        private bool _riding;

        // Снимок клипа на момент посадки: всё, что трогает поза, возвращается на сходе.
        private Transform[] _posedBones;
        private Vector3[] _restPositions;
        private Quaternion[] _restRotations;

        /// <summary>Игрок сидит на байке.</summary>
        public bool IsRiding => _riding;

        /// <summary>Вес позы 0..1: 0 — обычный клип Animator.</summary>
        public float Weight => _weight;

        private void Awake()
        {
            ResolveBones();
        }

        /// <summary>
        /// Поиск костей. Ищется только внутри модели (Animator): на сцене есть другие
        /// объекты с теми же именами костей (силуэт игрока), и глобальный поиск мог
        /// взять чужие кости.
        /// </summary>
        private void ResolveBones()
        {
            var animator = GetComponentInChildren<Animator>(true);
            _modelRoot = animator != null ? animator.transform : transform;

            _pelvis = _pelvis != null ? _pelvis : FindBone("pelvis");
            _spine = _spine != null ? _spine : FindBone("spine_01");
            _neck = _neck != null ? _neck : FindBone("neck_01");

            _thighL = _thighL != null ? _thighL : FindBone("thigh_l");
            _calfL = _calfL != null ? _calfL : FindBone("calf_l");
            _footL = _footL != null ? _footL : FindBone("foot_l");
            _ballL = _ballL != null ? _ballL : FindBone("ball_l");
            _thighR = _thighR != null ? _thighR : FindBone("thigh_r");
            _calfR = _calfR != null ? _calfR : FindBone("calf_r");
            _footR = _footR != null ? _footR : FindBone("foot_r");
            _ballR = _ballR != null ? _ballR : FindBone("ball_r");

            _upperArmL = _upperArmL != null ? _upperArmL : FindBone("upperarm_l");
            _lowerArmL = _lowerArmL != null ? _lowerArmL : FindBone("lowerarm_l");
            _handL = _handL != null ? _handL : FindBone("hand_l");
            _indexL = _indexL != null ? _indexL : FindBone("index_01_l");
            _upperArmR = _upperArmR != null ? _upperArmR : FindBone("upperarm_r");
            _lowerArmR = _lowerArmR != null ? _lowerArmR : FindBone("lowerarm_r");
            _handR = _handR != null ? _handR : FindBone("hand_r");
            _indexR = _indexR != null ? _indexR : FindBone("index_01_r");
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

        /// <summary>
        /// Сажает игрока: таз ставится ровно в посадочную точку байка, поза входит
        /// за доли секунды — в это же время камера уезжает в вид сверху.
        /// </summary>
        public void Sit(Transform seat)
        {
            if (seat == null)
                return;

            // Кости могли не разрешиться на Awake — пробуем ещё раз в момент посадки.
            if (_pelvis == null)
                ResolveBones();

            if (_pelvis == null)
            {
                Debug.LogError("[RiderPose] не найдены кости модели — поза наездника работать не будет", this);
                return;
            }

            _seat = seat;
            _riding = true;
            _weight = 0f;

            CapturePose();

            // Корень игрока уже висит на сиденье, но геометрия модели начинается
            // с пяток: без этой поправки робот стоял бы на сиденье, а не сидел.
            transform.position += _seat.position - _pelvis.position;

            Debug.Log("[RiderPose] посадка верхом", this);
        }

        /// <summary>
        /// Сход: поза снимается сразу, кости возвращаются к снимку клипа, а дальше позу
        /// полностью ведёт Animator — без остатков посадки в руках и ногах.
        /// </summary>
        public void Stand()
        {
            if (!_riding)
                return;

            _riding = false;
            _weight = 0f;
            _seat = null;
            RestorePose();
        }

        /// <summary>Снимок локальных значений костей до первой правки позы.</summary>
        private void CapturePose()
        {
            var bones = new List<Transform>(24);
            AddBone(bones, _pelvis);
            AddBone(bones, _spine);
            AddBone(bones, _neck);
            AddBone(bones, _thighL);
            AddBone(bones, _calfL);
            AddBone(bones, _footL);
            AddBone(bones, _ballL);
            AddBone(bones, _thighR);
            AddBone(bones, _calfR);
            AddBone(bones, _footR);
            AddBone(bones, _ballR);
            AddBone(bones, _upperArmL);
            AddBone(bones, _lowerArmL);
            AddBone(bones, _handL);
            AddBone(bones, _indexL);
            AddBone(bones, _upperArmR);
            AddBone(bones, _lowerArmR);
            AddBone(bones, _handR);
            AddBone(bones, _indexR);

            _posedBones = bones.ToArray();
            _restPositions = new Vector3[_posedBones.Length];
            _restRotations = new Quaternion[_posedBones.Length];

            for (var i = 0; i < _posedBones.Length; i++)
            {
                _restPositions[i] = _posedBones[i].localPosition;
                _restRotations[i] = _posedBones[i].localRotation;
            }
        }

        /// <summary>Возврат костей к снимку клипа и освобождение снимка — на сходе.</summary>
        private void RestorePose()
        {
            ApplyBasePose();

            _posedBones = null;
            _restPositions = null;
            _restRotations = null;
        }

        /// <summary>
        /// Кости возвращаются к снимку клипа перед каждой правкой. Без этого повороты
        /// копились поверх самих себя: Animator пишет не все кости каждый кадр
        /// (в зале на позвоночник он не пишет вовсе), и корпус крутился с частотой кадров.
        /// </summary>
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

        private static void AddBone(List<Transform> bones, Transform bone)
        {
            if (bone != null)
                bones.Add(bone);
        }

        private void LateUpdate()
        {
            if (!_riding || _seat == null)
                return;

            _weight = Mathf.MoveTowards(_weight, 1f, _weightSpeed * Time.deltaTime);

            // Каждый кадр начинаем с позы клипа: иначе правки складываются с прошлым
            // кадром, и поза крутится/пульсирует с частотой кадров.
            ApplyBasePose();

            if (_weight <= 0.001f)
                return;

            PoseTorso();
            PoseArms();
            PoseLegs();
        }

        /// <summary>Наклон корпуса вперёд и подъём головы: взгляд остаётся на дороге.</summary>
        private void PoseTorso()
        {
            var right = _seat.right;

            if (_spine != null)
            {
                var lean = Quaternion.AngleAxis(_leanAngle, right);
                _spine.rotation = Quaternion.Slerp(Quaternion.identity, lean, _weight) * _spine.rotation;
            }

            if (_neck != null)
            {
                var lift = Quaternion.AngleAxis(-_leanAngle * _neckShare, right);
                _neck.rotation = Quaternion.Slerp(Quaternion.identity, lift, _weight) * _neck.rotation;
            }
        }

        /// <summary>
        /// Руки в точки хвата. Решается аналитически по двум костям: угол в плече
        /// из теоремы косинусов, локоть уводится вниз-наружу, кисть доворачивается
        /// пальцами вдоль руля.
        /// </summary>
        private void PoseArms()
        {
            var gripCenter = _seat.position +
                             _seat.forward * _gripForward +
                             _seat.up * _gripUp;
            var half = _seat.right * _gripHalfWidth;

            PoseArm(_upperArmL, _lowerArmL, _handL, _indexL, gripCenter - half, -1f);
            PoseArm(_upperArmR, _lowerArmR, _handR, _indexR, gripCenter + half, +1f);
        }

        private void PoseArm(
            Transform upper, Transform lower, Transform hand, Transform finger,
            Vector3 target, float side)
        {
            if (upper == null || lower == null || hand == null)
                return;

            var shoulder = upper.position;
            var upperLength = Vector3.Distance(upper.position, lower.position);
            var lowerLength = Vector3.Distance(lower.position, hand.position);
            if (upperLength < 1e-4f || lowerLength < 1e-4f)
                return;

            var toTarget = target - shoulder;
            var reach = Mathf.Clamp(
                toTarget.magnitude,
                Mathf.Abs(upperLength - lowerLength) + 0.01f,
                upperLength + lowerLength - 0.01f);
            var direction = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : _seat.forward;

            var cos = Mathf.Clamp(
                (upperLength * upperLength + reach * reach - lowerLength * lowerLength) /
                (2f * upperLength * reach), -1f, 1f);
            var angle = Mathf.Acos(cos);

            // Полюс задаёт плоскость изгиба: локоть уходит вниз и наружу, а не в корпус.
            var pole = Vector3.ProjectOnPlane(-_seat.up + _seat.right * (side * 0.9f), direction);
            if (pole.sqrMagnitude < 1e-5f)
                pole = Vector3.ProjectOnPlane(Vector3.down, direction);
            pole.Normalize();

            var elbowDirection = direction * Mathf.Cos(angle) + pole * Mathf.Sin(angle);

            AimBone(upper, lower, elbowDirection);
            AimBone(lower, hand, target - lower.position);

            if (finger != null)
                AimBone(hand, finger, _seat.right * side);
        }

        /// <summary>Посадка ногами: бедро вперёд-вниз, голень вниз-назад, стопа по ходу байка.</summary>
        private void PoseLegs()
        {
            PoseLeg(_thighL, _calfL, _footL, _ballL, -1f);
            PoseLeg(_thighR, _calfR, _footR, _ballR, +1f);
        }

        private void PoseLeg(
            Transform thigh, Transform calf, Transform foot, Transform ball, float side)
        {
            if (thigh == null || calf == null)
                return;

            var thighDirection = (_seat.forward -
                                  _seat.up * _kneeDrop +
                                  _seat.right * (side * _kneeSplay)).normalized;
            var calfDirection = (-_seat.up - _seat.forward * _shinBack).normalized;
            var footDirection = (_seat.forward - _seat.up * 0.2f).normalized;

            AimBone(thigh, calf, thighDirection);
            if (foot != null)
                AimBone(calf, foot, calfDirection);
            if (foot != null && ball != null)
                AimBone(foot, ball, footDirection);
        }

        /// <summary>
        /// Доворот кости так, чтобы её дочерняя кость смотрела в заданном направлении.
        /// Направление берётся по фактическому положению дочерней кости, поэтому
        /// неважно, как ориентированы оси в рест-позе конкретного рига.
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

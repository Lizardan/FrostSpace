using System.Collections.Generic;
using UnityEngine;

namespace FrostSpace.Features.Ride
{
    /// <summary>
    /// Поза наездника: пока игрок на байке, робот сидит на сиденье, наклоняет
    /// корпус к рулю и держится руками за рукоятки.
    ///
    /// Поза не анимируется клипом, а собирается поверх клипа каждый кадр (LateUpdate,
    /// после Animator): корпус наклоняется вокруг оси сиденья, плечи выставляются
    /// в плоскость байка, голова остаётся ровной, руки и ноги решаются двумя костями
    /// в точки кокпита. Поэтому посадка работает на обоих байках и не зависит от
    /// наличия клипа «езды».
    ///
    /// Верх корпуса (таз, позвоночник, шея, голова, ключицы) берётся не из живого
    /// idle-клипа, а из нейтральной базы, снятой до первой анимации: иначе в посадку
    /// замерзал случайный наклон головы и плеч, который был в момент посадки.
    /// Снимок клипа на момент посадки нужен только для возврата на сходе.
    ///
    /// Точки кокпита ищутся по именам якорей в модели байка (`Grip L`, `Grip R`,
    /// `Foot L`, `Foot R`). Наклон корпуса, доля шеи, боковой вынос колена и точка
    /// таза берутся из компонента <see cref="RiderCockpit"/> на байке: так спортбайк
    /// и прогулочный мотоцикл сидят по-разному без веток в коде. Если кокпита или
    /// якорей нет, работают константы этого компонента.
    /// </summary>
    [DefaultExecutionOrder(220)]
    [DisallowMultipleComponent]
    public sealed class RiderPose : MonoBehaviour
    {
        [Header("Кости (пусто — ищутся по имени в модели)")]
        [SerializeField] private Transform _pelvis;
        [SerializeField] private Transform _spine;
        [SerializeField] private Transform _neck;

        [Header("Посадка на сиденье")]
        [Tooltip("Насколько таз выше посадочной точки: низ корпуса не входит в подушку, м")]
        [SerializeField] private float _seatLift = 0.08f;

        [Tooltip("Насколько таз сдвинут вперёд по подушке от посадочной точки, м")]
        [SerializeField] private float _seatForward = 0.12f;

        [Header("Наклон корпуса")]
        [Tooltip("Суммарный наклон корпуса к рулю, градусы")]
        [SerializeField] private float _leanAngle = 30f;

        [Tooltip("Доля наклона, которую забирает шея — чтобы робот смотрел вперёд, а не в бак")]
        [SerializeField, Range(0f, 1f)] private float _neckShare = 0.45f;

        [Header("Точки хвата (если в байке нет якорей)")]
        [Tooltip("Вынос руля вперёд от сиденья, м")]
        [SerializeField] private float _gripForward = 0.86f;

        [Tooltip("Высота руля над сиденьем, м")]
        [SerializeField] private float _gripUp = 0.06f;

        [Tooltip("Полуширина руля: расстояние от центра до рукоятки, м")]
        [SerializeField] private float _gripHalfWidth = 0.22f;

        [Tooltip("Вынос кисти назад от рукоятки: рукоятка ложится в ладонь, а не в сгиб, м")]
        [SerializeField] private float _palmOffset = 0.06f;

        [Header("Ноги (если в байке нет якорей)")]
        [Tooltip("Вынос ступни вперёд от сиденья, м")]
        [SerializeField] private float _footForward = 0.60f;

        [Tooltip("Насколько ступня ниже сиденья, м")]
        [SerializeField] private float _footDrop = 0f;

        [Tooltip("Полуширина постановки ступней, м")]
        [SerializeField] private float _footHalfWidth = 0.27f;

        [Tooltip("Носок вниз от горизонтали, градусы")]
        [SerializeField] private float _footToeDown = 12f;

        [Tooltip("Боковой вынос колена, если в байке нет RiderCockpit")]
        [SerializeField] private float _kneeOut = 0.25f;

        [Header("Вес позы")]
        [Tooltip("Скорость входа в позу, 1/с")]
        [SerializeField] private float _weightSpeed = 7f;

        private Transform _seat;
        private Transform _modelRoot;
        private Transform _spineMid;
        private Transform _spineTop;
        private Transform _head;
        private Transform _clavicleL;
        private Transform _clavicleR;
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

        private Transform _gripLeft;
        private Transform _gripRight;
        private Transform _footLeft;
        private Transform _footRight;

        // Данные кокпита текущего байка: подставляются при посадке.
        private RiderCockpit _cockpit;
        private float _rideLean;
        private float _rideNeck;
        private float _rideKneeOut;
        private Transform _seatAnchor;

        private float _weight;
        private bool _riding;

        // Снимок клипа на момент посадки: всё, что трогает поза, возвращается на сходе.
        private Transform[] _posedBones;
        private Vector3[] _restPositions;
        private Quaternion[] _restRotations;

        // Нейтральная база торса: bind-поза до первой анимации.
        private Transform[] _neutralBones;
        private Vector3[] _neutralPositions;
        private Quaternion[] _neutralRotations;

        // Для каждой кости позы — индекс в нейтральной базе или -1.
        private int[] _neutralIndex;

        /// <summary>Игрок сидит на байке.</summary>
        public bool IsRiding => _riding;

        /// <summary>Вес позы 0..1: 0 — обычный клип Animator.</summary>
        public float Weight => _weight;

        private void Awake()
        {
            ResolveBones();
            CaptureNeutral();
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
            _spineMid = _spineMid != null ? _spineMid : FindBone("spine_02");
            _spineTop = _spineTop != null ? _spineTop : FindBone("spine_03");
            _neck = _neck != null ? _neck : FindBone("neck_01");
            _head = _head != null ? _head : FindBone("Head");
            _clavicleL = _clavicleL != null ? _clavicleL : FindBone("clavicle_l");
            _clavicleR = _clavicleR != null ? _clavicleR : FindBone("clavicle_r");

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
        /// Нейтральная база торса снимается до первой анимации: Animator выставляет
        /// клип позже, а bind-поза даёт ровные плечи и прямую шею. Если компонент
        /// включился уже после первого кадра анимации (меню), база совпадёт с клипом —
        /// тогда позу выравнивают доворот плеч и компенсация наклона головы.
        /// </summary>
        private void CaptureNeutral()
        {
            var bones = new List<Transform>(8);
            AddBone(bones, _pelvis);
            AddBone(bones, _spine);
            AddBone(bones, _spineMid);
            AddBone(bones, _spineTop);
            AddBone(bones, _neck);
            AddBone(bones, _head);
            AddBone(bones, _clavicleL);
            AddBone(bones, _clavicleR);

            _neutralBones = bones.ToArray();
            _neutralPositions = new Vector3[_neutralBones.Length];
            _neutralRotations = new Quaternion[_neutralBones.Length];
            for (var i = 0; i < _neutralBones.Length; i++)
            {
                _neutralPositions[i] = _neutralBones[i].localPosition;
                _neutralRotations[i] = _neutralBones[i].localRotation;
            }
        }

        /// <summary>
        /// Сажает игрока: таз ставится в посадочную точку байка, поза входит
        /// за доли секунды — в это же время камера уезжает в вид сверху.
        /// </summary>
        public void Sit(Transform seat)
        {
            if (seat == null || _riding)
                return;

            // Кости могли не разрешиться на Awake — пробуем ещё раз в момент посадки.
            if (_pelvis == null)
                ResolveBones();

            if (_pelvis == null)
            {
                Debug.LogError("[RiderPose] не найдены кости модели — поза наездника работать не будет", this);
                return;
            }

            if (_neutralBones == null)
                CaptureNeutral();

            _seat = seat;
            _riding = true;
            _weight = 0f;

            ReadCockpit();
            CapturePose();
            FindCockpit();

            // Корень игрока уже висит на сиденье, но геометрия модели начинается
            // с пяток: без этой поправки робот стоял бы на сиденье, а не сидел.
            // Считаем по нейтральной базе — по idle-снимку таз после выпрямления
            // корпуса съезжал с подушки.
            ApplyBasePose(1f);
            transform.position += SeatPoint - _pelvis.position;

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
            _cockpit = null;
            _seatAnchor = null;
            _gripLeft = null;
            _gripRight = null;
            _footLeft = null;
            _footRight = null;
            RestorePose();
        }

        /// <summary>Точка таза: якорь кокпита либо посадочная точка с подъёмом и сдвигом.</summary>
        private Vector3 SeatPoint =>
            _seatAnchor != null
                ? _seatAnchor.position
                : _seat.position + _seat.forward * _seatForward + _seat.up * _seatLift;

        /// <summary>
        /// Данные посадки с байка: наклон, шея, точка таза. Компонент висит на корне
        /// байка, поэтому ищется вверх от сиденья, а не по всей сцене.
        /// </summary>
        private void ReadCockpit()
        {
            _cockpit = _seat != null ? _seat.GetComponentInParent<RiderCockpit>() : null;
            _rideLean = _cockpit != null ? _cockpit.LeanAngle : _leanAngle;
            _rideNeck = _cockpit != null ? _cockpit.NeckShare : _neckShare;
            _rideKneeOut = _cockpit != null ? _cockpit.KneeOut : _kneeOut;
            _seatAnchor = _cockpit != null ? _cockpit.SeatPoint : null;
        }

        /// <summary>Якоря кокпита в модели байка: рукоятки и подножки.</summary>
        private void FindCockpit()
        {
            var root = _seat != null ? _seat.root : null;
            _gripLeft = FindIn(root, "Grip L");
            _gripRight = FindIn(root, "Grip R");
            _footLeft = FindIn(root, "Foot L");
            _footRight = FindIn(root, "Foot R");

            if (_gripLeft == null || _gripRight == null)
            {
                Debug.Log(
                    "[RiderPose] якорей рукояток нет — точки хвата считаются от сиденья", this);
            }
        }

        private static Transform FindIn(Transform root, string anchorName)
        {
            if (root == null)
                return null;

            if (root.name == anchorName)
                return root;

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindIn(root.GetChild(i), anchorName);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>Снимок локальных значений костей до первой правки позы.</summary>
        private void CapturePose()
        {
            var bones = new List<Transform>(26);
            AddBone(bones, _pelvis);
            AddBone(bones, _spine);
            AddBone(bones, _spineMid);
            AddBone(bones, _spineTop);
            AddBone(bones, _neck);
            AddBone(bones, _head);
            AddBone(bones, _clavicleL);
            AddBone(bones, _clavicleR);
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
            _neutralIndex = new int[_posedBones.Length];

            for (var i = 0; i < _posedBones.Length; i++)
            {
                _restPositions[i] = _posedBones[i].localPosition;
                _restRotations[i] = _posedBones[i].localRotation;
                _neutralIndex[i] = NeutralIndexOf(_posedBones[i]);
            }
        }

        private int NeutralIndexOf(Transform bone)
        {
            if (_neutralBones == null)
                return -1;

            for (var i = 0; i < _neutralBones.Length; i++)
            {
                if (_neutralBones[i] == bone)
                    return i;
            }

            return -1;
        }

        /// <summary>Возврат костей к снимку клипа и освобождение снимка — на сходе.</summary>
        private void RestorePose()
        {
            ApplyBasePose(0f);

            _posedBones = null;
            _restPositions = null;
            _restRotations = null;
            _neutralIndex = null;
        }

        /// <summary>
        /// Кости возвращаются к снимку клипа перед каждой правкой. Без этого повороты
        /// копились поверх самих себя: Animator пишет не все кости каждый кадр
        /// (в зале на позвоночник он не пишет вовсе), и корпус крутился с частотой кадров.
        /// Торс при этом плавно уходит из клипа в нейтральную базу — на весе 1 в посадке
        /// не остаётся случайного наклона из idle.
        /// </summary>
        private void ApplyBasePose(float blend)
        {
            if (_posedBones == null)
                return;

            blend = Mathf.Clamp01(blend);

            for (var i = 0; i < _posedBones.Length; i++)
            {
                if (_posedBones[i] == null)
                    continue;

                var position = _restPositions[i];
                var rotation = _restRotations[i];

                var neutral = _neutralIndex[i];
                if (neutral >= 0 && blend > 0f)
                {
                    position = Vector3.Lerp(position, _neutralPositions[neutral], blend);
                    rotation = Quaternion.Slerp(rotation, _neutralRotations[neutral], blend);
                }

                _posedBones[i].localPosition = position;
                _posedBones[i].localRotation = rotation;
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

            // Меню живёт на unscaled-времени: пауза не должна замораживать вход в позу.
            _weight = Mathf.MoveTowards(_weight, 1f, _weightSpeed * Time.unscaledDeltaTime);

            // Каждый кадр начинаем с позы клипа: иначе правки складываются с прошлым
            // кадром, и поза крутится/пульсирует с частотой кадров.
            ApplyBasePose(_weight);

            if (_weight <= 0.001f)
                return;

            PoseTorso();
            PoseArms();
            PoseLegs();
        }

        /// <summary>
        /// Наклон распределяется по всей цепочке позвонков, плечи выставляются
        /// в плоскость байка, а шея и голова возвращают взгляд к дороге. Так корпус
        /// не ломается в одной точке, а голова не остаётся повёрнутой как в idle.
        /// </summary>
        private void PoseTorso()
        {
            var right = _seat.right;
            var lean = _rideLean * _weight;

            Rotate(_spine, right, lean * 0.40f);
            Rotate(_spineMid, right, lean * 0.35f);
            Rotate(_spineTop, right, lean * 0.25f);

            SquareShoulders();

            Rotate(_neck, right, -lean * _rideNeck);
            Rotate(_head, right, -lean * (1f - _rideNeck));
        }

        /// <summary>
        /// Линия плеч приводится в плоскость байка: сначала убирается рыскание
        /// (одно плечо вперёд), затем крен (одно плечо выше). Это снимает перекос,
        /// который пришёл из клипа, не завися от того, как ориентированы оси рига.
        /// </summary>
        private void SquareShoulders()
        {
            if (_spineTop == null || _upperArmL == null || _upperArmR == null)
                return;

            var line = _upperArmR.position - _upperArmL.position;
            var flat = Vector3.ProjectOnPlane(line, _seat.up);
            if (flat.sqrMagnitude < 0.0025f)
                return;

            var yaw = Vector3.SignedAngle(flat, _seat.right, _seat.up);
            Rotate(_spineTop, _seat.up, yaw * _weight);

            line = _upperArmR.position - _upperArmL.position;
            var level = Vector3.ProjectOnPlane(line, _seat.up);
            if (level.sqrMagnitude < 0.0025f)
                return;

            var roll = Vector3.SignedAngle(line, level, _seat.forward);
            Rotate(_spineTop, _seat.forward, roll * _weight);
        }

        /// <summary>Поворот кости вокруг мировой оси: дочерние кости идут за ней.</summary>
        private static void Rotate(Transform bone, Vector3 axis, float degrees)
        {
            if (bone == null || Mathf.Abs(degrees) < 1e-4f || axis.sqrMagnitude < 1e-8f)
                return;

            bone.rotation = Quaternion.AngleAxis(degrees, axis.normalized) * bone.rotation;
        }

        /// <summary>
        /// Руки в точки хвата. Рукоятка ложится в ладонь, а не в сгиб кисти, поэтому
        /// цель сдвинута назад по линии плечо-рукоятка. Локоть уводится вниз-наружу,
        /// кисть доворачивается пальцами вдоль руля.
        /// </summary>
        private void PoseArms()
        {
            if (_gripLeft != null && _gripRight != null)
            {
                PoseArm(_upperArmL, _lowerArmL, _handL, _indexL, _gripLeft.position, -1f);
                PoseArm(_upperArmR, _lowerArmR, _handR, _indexR, _gripRight.position, +1f);
                return;
            }

            var gripCenter = _seat.position +
                             _seat.forward * _gripForward +
                             _seat.up * _gripUp;
            var half = _seat.right * _gripHalfWidth;

            PoseArm(_upperArmL, _lowerArmL, _handL, _indexL, gripCenter - half, -1f);
            PoseArm(_upperArmR, _lowerArmR, _handR, _indexR, gripCenter + half, +1f);
        }

        private void PoseArm(
            Transform upper, Transform lower, Transform hand, Transform finger,
            Vector3 grip, float side)
        {
            if (upper == null || lower == null || hand == null)
                return;

            var shoulder = upper.position;
            var wrist = grip;
            var toGrip = grip - shoulder;
            if (_palmOffset > 0f && toGrip.sqrMagnitude > 1e-6f)
                wrist = grip - toGrip.normalized * _palmOffset;

            var upperLength = Vector3.Distance(upper.position, lower.position);
            var lowerLength = Vector3.Distance(lower.position, hand.position);

            // Полюс задаёт плоскость изгиба: локоть уходит вниз и наружу, а не в корпус.
            var pole = -_seat.up + _seat.right * (side * 0.9f);
            if (!TrySolveMiddle(shoulder, wrist, upperLength, lowerLength, pole, out var elbow))
                return;

            AimBone(upper, lower, elbow - upper.position);
            AimBone(lower, hand, wrist - lower.position);

            if (finger != null)
                AimBone(hand, finger, _seat.right * side);
        }

        /// <summary>
        /// Ноги в точки подножек: колено уходит вперёд-вверх-наружу, ступня ставится
        /// на опору, носок чуть опущен. Цель — положение стопы, а не направление,
        /// поэтому ступня не висит в воздухе и не входит в корпус байка.
        /// </summary>
        private void PoseLegs()
        {
            var fallback = _seat.position +
                           _seat.forward * _footForward -
                           _seat.up * _footDrop;
            var half = _seat.right * _footHalfWidth;

            var left = _footLeft != null ? _footLeft.position : fallback - half;
            var right = _footRight != null ? _footRight.position : fallback + half;

            PoseLeg(_thighL, _calfL, _footL, _ballL, left, -1f);
            PoseLeg(_thighR, _calfR, _footR, _ballR, right, +1f);
        }

        private void PoseLeg(
            Transform thigh, Transform calf, Transform foot, Transform ball,
            Vector3 ankle, float side)
        {
            if (thigh == null || calf == null || foot == null)
                return;

            var upperLength = Vector3.Distance(thigh.position, calf.position);
            var lowerLength = Vector3.Distance(calf.position, foot.position);

            // Колено смотрит вперёд, вверх и немного наружу — как на подножках.
            // Боковой вынос задаёт кокпит: на ховербайке колени поджаты,
            // на мотоцикле остаются шире, по посадке.
            var pole = _seat.forward + _seat.up * 0.6f +
                       _seat.right * (side * _rideKneeOut);
            if (!TrySolveMiddle(thigh.position, ankle, upperLength, lowerLength, pole, out var knee))
                return;

            AimBone(thigh, calf, knee - thigh.position);
            AimBone(calf, foot, ankle - calf.position);

            if (ball != null)
            {
                var toe = Quaternion.AngleAxis(_footToeDown, _seat.right) * _seat.forward;
                AimBone(foot, ball, toe);
            }
        }

        /// <summary>
        /// Положение средней кости (локтя или колена) для двухкостной конечности.
        /// Длины берутся из фактических костей, поэтому масштаб рига не важен;
        /// при недосягаемой цели конечность просто выпрямляется в её сторону.
        /// </summary>
        private static bool TrySolveMiddle(
            Vector3 root, Vector3 target, float upper, float lower, Vector3 pole,
            out Vector3 middle)
        {
            middle = root;

            var toTarget = target - root;
            var distance = toTarget.magnitude;
            if (distance < 1e-4f || upper < 1e-4f || lower < 1e-4f)
                return false;

            var reach = Mathf.Clamp(
                distance,
                Mathf.Abs(upper - lower) + 0.01f,
                upper + lower - 0.01f);
            var direction = toTarget / distance;
            var along = (upper * upper - lower * lower + reach * reach) / (2f * reach);
            var height = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));

            var plane = Vector3.ProjectOnPlane(pole, direction);
            if (plane.sqrMagnitude < 1e-6f)
                plane = Vector3.ProjectOnPlane(Vector3.up, direction);
            if (plane.sqrMagnitude < 1e-6f)
                return false;

            middle = root + direction * along + plane.normalized * height;
            return true;
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

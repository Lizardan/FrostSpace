using System.Collections;
using FrostSpace.Features.Carry;
using FrostSpace.Features.Locomotion;
using FrostSpace.Features.Quest;
using FrostSpace.Features.View;
using UnityEngine;

namespace FrostSpace.Features.Story
{
    /// <summary>
    /// Прибытие в депо (DP-01): поезд выезжает из метели и встаёт у платформы,
    /// раздвигаются боковые створки, R13 спускается на платформу, за ним
    /// выезжает тележка с четырьмя аккумуляторами. Всё это время игрок стоит
    /// без управления; камеру ведёт сценовый кадр, затем обзор возвращается
    /// за спину игрока.
    ///
    /// R13, тележка и игрок — дети вагона: пока поезд едет, они находятся внутри
    /// и движутся вместе с ним. Перед выходом R13 и тележка отцепляются от вагона
    /// и идут по мировым координатам; игрок отцепляется в конце, оставаясь внутри
    /// вагона — управление возвращается там, где он стоял при разговоре с R13,
    /// и на платформу он выходит сам.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-30)]
    public sealed class DepotArrivalDirector : MonoBehaviour
    {
        private static readonly int MovingId = Animator.StringToHash("Moving");

        [Header("Ссылки")]
        [SerializeField] private RobotLocomotion _locomotion;
        [Tooltip("Корень игрока: после выхода обзор возвращается за спину")]
        [SerializeField] private Transform _player;
        [Tooltip("Риг камеры: на время прибытия ведёт сценовый кадр")]
        [SerializeField] private PlayerCameraRig _rig;
        [Tooltip("Вагон: R13 и тележка — его дети")]
        [SerializeField] private Transform _train;
        [SerializeField] private Transform _doorLeft;
        [SerializeField] private Transform _doorRight;
        [Tooltip("R13 — сценовый актёр с Animator")]
        [SerializeField] private Transform _r13;
        [SerializeField] private Animator _r13Animator;
        [Tooltip("Тележка с четырьмя гнёздами")]
        [SerializeField] private Transform _cart;
        [SerializeField] private CarrySlot[] _slots = new CarrySlot[0];
        [SerializeField] private Carryable[] _cargo = new Carryable[0];

        [Header("Прибытие")]
        [Tooltip("Пауза перед началом, с")]
        [SerializeField] private float _startDelay = 0.2f;
        [Tooltip("Откуда выезжает вагон относительно места остановки, м")]
        [SerializeField] private Vector3 _trainStartOffset = new(0f, 0f, -42f);
        [Tooltip("Сколько вагон идёт до платформы, с")]
        [SerializeField] private float _trainSeconds = 4.8f;
        [Tooltip("Пауза после остановки, с")]
        [SerializeField] private float _doorSettle = 0.2f;
        [Tooltip("Сколько раздвигаются створки, с")]
        [SerializeField] private float _doorSeconds = 0.9f;
        [Tooltip("Насколько створка уезжает в сторону, м")]
        [SerializeField] private float _doorSlide = 1.35f;

        [Header("Выход")]
        [Tooltip("Точка внутри проёма в локальных координатах вагона: через неё идёт выход")]
        [SerializeField] private Vector3 _doorInside = new(3.0f, 0f, 1.0f);
        [Tooltip("Точка снаружи проёма в локальных координатах вагона; y — уровень платформы")]
        [SerializeField] private Vector3 _doorOutside = new(5.4f, -0.4f, 1.0f);
        [Tooltip("Куда выходит R13 после проёма, мир")]
        [SerializeField] private Vector3 _r13Exit = new(-1.6f, 0.6f, 0.8f);
        [Tooltip("Сколько R13 идёт по коридору, с. 0,912 м/с — скорость шага клипа")]
        [SerializeField] private float _r13Seconds = 7.0f;
        [Tooltip("Скорость шага клипа Walk, м/с: Animator.speed подгоняется под путь")]
        [SerializeField] private float _r13ClipSpeed = 0.912f;
        [Tooltip("Скорость доворота корпуса по ходу, град/с")]
        [SerializeField] private float _r13TurnSpeed = 240f;
        [Tooltip("Задержка тележки после старта R13, с: она идёт следом, а не после")]
        [SerializeField] private float _cartDelay = 0.4f;
        [Tooltip("Куда выезжает тележка после проёма, мир")]
        [SerializeField] private Vector3 _cartExit = new(0.8f, 0.6f, -1.2f);
        [Tooltip("Сколько тележка идёт следом; медленнее R13, чтобы не догнать")]
        [SerializeField] private float _cartSeconds = 12.0f;
        [Tooltip("Точка тележки перед проёмом в локальных координатах вагона: сначала выравнивание по проёму")]
        [SerializeField] private Vector3 _cartApproach = new(1.6f, 0f, 1.0f);
        [Tooltip("Пауза на месте перед возвратом управления, с")]
        [SerializeField] private float _settleSeconds = 0.3f;

        [Header("Кадр прибытия")]
        [Tooltip("Точка камеры на время прибытия")]
        [SerializeField] private Vector3 _shotPosition = new(7.5f, 3.4f, 4f);
        [Tooltip("Куда смотрит камера на время прибытия")]
        [SerializeField] private Vector3 _shotTarget = new(-3f, 1.8f, -1f);

        private void Awake()
        {
            if (_locomotion == null)
                _locomotion = FindAnyObjectByType<RobotLocomotion>();
            if (_player == null && _locomotion != null)
                _player = _locomotion.transform;
            if (_rig == null)
                _rig = FindAnyObjectByType<PlayerCameraRig>();
            if (_r13Animator == null && _r13 != null)
                _r13Animator = _r13.GetComponentInChildren<Animator>(true);
        }

        private void Start()
        {
            // Сохранение переезжает в депо (ADR 0031): «Продолжить» должно вести
            // сюда, а не в уже пройденный вагон.
            ProgressSave.MarkScene(ProgressSave.FrontierScene);
            StartCoroutine(Play());
        }

        private IEnumerator Play()
        {
            _locomotion?.SetConversationLock(true);

            // Игрок едет внутри вагона. Пока локомоция жива, она каждый кадр
            // двигает CharacterController гравитацией, и на ходу вагона его
            // выбивает из кузова. Отключаем её до конца заставки и включаем
            // обратно, когда игрок уже отцеплен и стоит на полу.
            if (_locomotion != null)
                _locomotion.enabled = false;

            BeginShot();
            LoadCargo();

            var trainRest = _train != null ? _train.position : Vector3.zero;
            if (_train != null)
                _train.position = trainRest + _trainStartOffset;

            yield return Wait(_startDelay);

            if (_train != null)
                yield return MoveTrain(_train, trainRest + _trainStartOffset, trainRest, _trainSeconds);

            yield return Wait(_doorSettle);
            yield return OpenDoors();

            DetachCouriers();

            var doorInside = DoorPoint(_doorInside);
            var doorOutside = DoorPoint(_doorOutside);

            // R13 идёт первым. Флаг Moving снимается в ExitR13 ровно в конце
            // маршрута: иначе робот продолжает шагать на месте, пока едет тележка.
            if (_r13 != null)
                StartCoroutine(ExitR13(doorInside, doorOutside));

            yield return Wait(_cartDelay);

            // Тележка стартует почти одновременно и тем же коридором, но медленнее
            // R13: она следует за ним, а не догоняет и не режет его модель.
            if (_cart != null)
            {
                StartCoroutine(MovePath(_cart,
                    new[] { _cart.position, DoorPoint(_cartApproach), doorOutside, _cartExit },
                    _cartSeconds));
            }

            var r13Remaining = _r13 != null ? Mathf.Max(0f, _r13Seconds - _cartDelay) : 0f;
            var cartDuration = _cart != null ? _cartSeconds : 0f;
            yield return Wait(Mathf.Max(r13Remaining, cartDuration));

            // Страховка: если корутина R13 была прервана, Idle всё равно включится.
            _r13Animator?.SetBool(MovingId, false);
            yield return Wait(_settleSeconds);

            // Игрок был ребёнком вагона всю поездку; дальше он идёт сам от проёма.
            DetachPlayer();
            if (_locomotion != null)
                _locomotion.enabled = true;
            EndShot();
            _locomotion?.SetConversationLock(false);
        }

        /// <summary>Точка в локальных координатах вагона — в мировые.</summary>
        private Vector3 DoorPoint(Vector3 local)
        {
            return _train != null ? _train.TransformPoint(local) : local;
        }

        /// <summary>Отцепляет игрока от вагона, сохраняя мировую позицию.</summary>
        private void DetachPlayer()
        {
            if (_player == null || _train == null || !_player.IsChildOf(_train))
                return;

            _player.SetParent(null, true);
        }

        private void LoadCargo()
        {
            var count = Mathf.Min(_slots.Length, _cargo.Length);
            for (var i = 0; i < count; i++)
            {
                var slot = _slots[i];
                var item = _cargo[i];
                if (slot == null || item == null || !slot.IsFree)
                    continue;

                item.Take(slot.transform);
                slot.TryPlace(item);
            }
        }

        /// <summary>Створки разъезжаются в стороны от центра проёма одновременно.</summary>
        private IEnumerator OpenDoors()
        {
            if (_doorLeft == null || _doorRight == null)
                yield break;

            var leftFrom = _doorLeft.localPosition;
            var rightFrom = _doorRight.localPosition;
            var leftTo = leftFrom + Vector3.forward * _doorSlide;
            var rightTo = rightFrom - Vector3.forward * _doorSlide;

            // Обе створки стартуют в одном кадре и идут с одной длительностью:
            // последовательный запуск давал рывок правой.
            var left = StartCoroutine(MoveDoor(_doorLeft, leftFrom, leftTo, _doorSeconds));
            yield return MoveDoor(_doorRight, rightFrom, rightTo, _doorSeconds);
            yield return left;
        }

        private void DetachCouriers()
        {
            if (_cart != null)
                _cart.SetParent(null, true);
            if (_r13 != null)
                _r13.SetParent(null, true);
        }

        /// <summary>
        /// Сценовый кадр на время прибытия: риг не переписывает позу, поэтому
        /// случайное движение мыши не сбивает обзор и вагон виден целиком.
        /// </summary>
        private void BeginShot()
        {
            if (_rig == null)
                return;

            _rig.SetCinematic(true);
            var camera = _rig.ViewCamera;
            if (camera == null)
                return;

            var direction = _shotTarget - _shotPosition;
            camera.transform.position = _shotPosition;
            if (direction.sqrMagnitude > 0.0001f)
                camera.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        /// <summary>Возвращает обзор за спину игрока и снимает кинематографическую блокировку.</summary>
        private void EndShot()
        {
            if (_rig == null)
                return;

            if (_player != null)
                _rig.SetViewYaw(_player.eulerAngles.y);
            _rig.SetCinematic(false);
        }

        /// <summary>Вагон тормозит: скорость падает к концу пути.</summary>
        private static IEnumerator MoveTrain(
            Transform target, Vector3 from, Vector3 to, float seconds)
        {
            if (seconds <= 0f)
            {
                target.position = to;
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / seconds);
                var eased = 1f - (1f - t) * (1f - t);
                target.position = Vector3.LerpUnclamped(from, to, eased);
                yield return null;
            }

            target.position = to;
        }

        /// <summary>
        /// Ведёт R13 по маршруту и снимает Moving сразу по прибытии, не дожидаясь
        /// тележки: иначе Walk остаётся включённым, пока тележка едет следом.
        /// </summary>
        private IEnumerator ExitR13(Vector3 doorInside, Vector3 doorOutside)
        {
            _r13Animator?.SetBool(MovingId, true);
            yield return MovePath(_r13,
                new[] { _r13.position, doorInside, doorOutside, _r13Exit },
                _r13Seconds, _r13Animator, _r13ClipSpeed, _r13TurnSpeed);
            _r13Animator?.SetBool(MovingId, false);
        }

        /// <summary>
        /// Ведёт объект по ломаной с равномерной скоростью: точки задают коридор
        /// (место → проём → платформа → точка выхода), поэтому путь не режет борт.
        ///
        /// Если передан <paramref name="animator"/>, корпус доворачивается по
        /// касательной, а скорость клипа подгоняется под фактическую — шаг не
        /// скользит. Это не настоящий root motion (клипы in-place, ADR 0022/0023),
        /// но визуально совпадает с ним.
        /// </summary>
        private static IEnumerator MovePath(
            Transform target, Vector3[] points, float seconds,
            Animator animator = null, float clipSpeed = 0.912f,
            float turnSpeed = 240f)
        {
            if (target == null || points == null || points.Length == 0)
                yield break;

            if (points.Length == 1 || seconds <= 0f)
            {
                target.position = points[points.Length - 1];
                if (animator != null)
                    animator.speed = 1f;
                yield break;
            }

            var elapsed = 0f;
            var speed = 0f;
            while (elapsed < seconds)
            {
                var deltaTime = Time.deltaTime;
                elapsed += deltaTime;
                var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / seconds));
                var next = PointOnPath(points, t);

                var delta = next - target.position;
                var planar = new Vector3(delta.x, 0f, delta.z);

                // Доворот по ходу: без него R13 идёт боком и спиной вперёд.
                if (planar.sqrMagnitude > 0.000001f)
                {
                    var rotation = Quaternion.LookRotation(planar.normalized, Vector3.up);
                    target.rotation = Quaternion.RotateTowards(
                        target.rotation, rotation, turnSpeed * deltaTime);
                }

                // Шаг без скольжения: скорость клипа = фактическая / скорость клипа.
                if (animator != null && deltaTime > 0.0001f)
                {
                    var actual = planar.magnitude / deltaTime;
                    speed = Mathf.MoveTowards(speed, actual, 6f * deltaTime);
                    animator.speed = Mathf.Clamp(
                        speed / Mathf.Max(clipSpeed, 0.01f), 0.35f, 1.6f);
                }

                target.position = next;
                yield return null;
            }

            target.position = points[points.Length - 1];
            // Обязательно: иначе Idle останется с поджатой скоростью.
            if (animator != null)
                animator.speed = 1f;
        }

        /// <summary>Точка на ломаной по доле пройденного пути 0..1.</summary>
        private static Vector3 PointOnPath(Vector3[] points, float t)
        {
            var total = 0f;
            for (var i = 1; i < points.Length; i++)
                total += Vector3.Distance(points[i - 1], points[i]);

            if (total <= 0.0001f)
                return points[points.Length - 1];

            var distance = total * Mathf.Clamp01(t);
            for (var i = 1; i < points.Length; i++)
            {
                var segment = Vector3.Distance(points[i - 1], points[i]);
                if (distance <= segment || i == points.Length - 1)
                {
                    var k = segment <= 0.0001f ? 1f : distance / segment;
                    return Vector3.Lerp(points[i - 1], points[i], Mathf.Clamp01(k));
                }

                distance -= segment;
            }

            return points[points.Length - 1];
        }

        private static IEnumerator MoveDoor(
            Transform target, Vector3 from, Vector3 to, float seconds)
        {
            if (seconds <= 0f)
            {
                target.localPosition = to;
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / seconds));
                target.localPosition = Vector3.LerpUnclamped(from, to, t);
                yield return null;
            }

            target.localPosition = to;
        }

        private static IEnumerator Wait(float seconds)
        {
            if (seconds <= 0f)
                yield break;

            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
    }
}

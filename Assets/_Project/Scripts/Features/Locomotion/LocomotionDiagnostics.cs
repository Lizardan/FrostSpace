using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FrostSpace.Features.Locomotion
{
    /// <summary>
    /// Проверочный стенд: ведёт робота вперёд шагом, потом бегом, затем резко меняет
    /// курс. Считает скольжение стопы по земле и занос корпуса — угол между взглядом
    /// и фактическим движением.
    ///
    /// Контакт определяется относительно минимальной высоты стопы за окно замера:
    /// абсолютный порог зависел бы от того, насколько персонаж просел в землю.
    /// Отчёт печатается в Console строками PROBE.
    /// </summary>
    public sealed class LocomotionDiagnostics : MonoBehaviour
    {
        [SerializeField] private float _runSeconds = 2.5f;
        [Tooltip("Насколько стопа может быть выше минимальной, чтобы считаться на земле")]
        [SerializeField] private float _contactHeight = 0.03f;
        [SerializeField] private float _contactRiseLimit = 0.25f;
        [Tooltip("После замеров проверить прыжок: высота вершины и время полёта")]
        [SerializeField] private bool _probeJump = true;

        private RobotLocomotion _locomotion;
        private Transform _foot;

        private void Start()
        {
            _locomotion = GetComponent<RobotLocomotion>();
            _foot = FindDeep(transform, "ball_l");
            if (_locomotion == null || _foot == null)
            {
                Debug.LogWarning("LocomotionDiagnostics: нет движения или кости ball_l");
                return;
            }

            Debug.Log("PROBE start: стенд локомоции активен");
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            yield return new WaitForSeconds(1f);
            yield return Measure("walk", false);
            yield return new WaitForSeconds(0.6f);
            yield return Measure("sprint", true);
            yield return new WaitForSeconds(0.4f);
            yield return MeasureSteer();
            if (_probeJump)
                yield return MeasureJump();
            _locomotion.ClearInputOverride();
            Debug.Log("PROBE done");
        }

        private IEnumerator Measure(string label, bool sprint)
        {
            // Бежим на юг: там нет забора, дома и камней — чистая полоса снега.
            _locomotion.SetInputOverride(Vector2.down, sprint);
            yield return new WaitForSeconds(0.8f);

            var elapsed = 0f;
            var start = _locomotion.transform.position;
            var positions = new List<Vector3>();
            var times = new List<float>();

            while (elapsed < _runSeconds)
            {
                yield return null;
                var dt = Time.deltaTime;
                if (dt <= 0f)
                    continue;

                elapsed += dt;
                positions.Add(_foot.position);
                times.Add(Time.time);
            }

            var minHeight = float.MaxValue;
            foreach (var position in positions)
                minHeight = Mathf.Min(minHeight, position.y);

            var contacts = 0;
            var slipSum = 0f;
            var slipWorst = 0f;
            for (var i = 1; i < positions.Count; i++)
            {
                var dt = times[i] - times[i - 1];
                if (dt <= 0f)
                    continue;

                var delta = positions[i] - positions[i - 1];
                if (positions[i].y > minHeight + _contactHeight)
                    continue;
                if (Mathf.Abs(delta.y / dt) > _contactRiseLimit)
                    continue;

                var slip = new Vector2(delta.x, delta.z).magnitude / dt;
                contacts++;
                slipSum += slip;
                slipWorst = Mathf.Max(slipWorst, slip);
            }

            var distance = Vector3.Distance(start, _locomotion.transform.position);
            var speed = _locomotion.PlanarSpeed;
            var slipAvg = contacts > 0 ? slipSum / contacts : 0f;
            Debug.Log(string.Format(
                "PROBE {0} speed={1:F2} animSpeed={2:F2} distance={3:F2} footMinY={4:F3} " +
                "contacts={5} slipAvg={6:F2} slipPercent={7:F0} slipWorst={8:F2}",
                label, speed, _locomotion.AnimatorSpeed, distance, minHeight,
                contacts, slipAvg, speed > 0.05f ? slipAvg / speed * 100f : 0f, slipWorst));
        }

        private IEnumerator MeasureSteer()
        {
            // После sprint-замера робот уже бежит: резко влево, без разгона.
            // Скорость и корпус должны успеть довернуться без заноса.
            _locomotion.SetInputOverride(Vector2.left, true);
            var elapsed = 0f;
            var maxAngle = 0f;
            var angleSum = 0f;
            var samples = 0;
            while (elapsed < 1.2f)
            {
                yield return null;
                var dt = Time.deltaTime;
                if (dt <= 0f)
                    continue;

                elapsed += dt;
                var velocity = _locomotion.PlanarVelocity;
                if (velocity.sqrMagnitude < 0.25f)
                    continue;

                var forward = _locomotion.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.001f)
                    continue;

                var angle = Vector3.Angle(forward, velocity);
                maxAngle = Mathf.Max(maxAngle, angle);
                angleSum += angle;
                samples++;
            }

            Debug.Log(string.Format(
                "PROBE steer maxAngle={0:F1} avgAngle={1:F1} samples={2}",
                maxAngle, samples > 0 ? angleSum / samples : 0f, samples));
        }

        /// <summary>
        /// Прыжок без клавиатуры: компонент сам ставит запрос и меряет вершину
        /// траектории и время от нажатия до приземления.
        /// </summary>
        private IEnumerator MeasureJump()
        {
            _locomotion.ClearInputOverride();
            yield return new WaitForSeconds(0.5f);

            var startY = transform.position.y;
            var start = Time.time;
            var peak = 0f;
            var leftGround = false;
            _locomotion.RequestJump();

            while (Time.time - start < 2.5f)
            {
                yield return null;
                peak = Mathf.Max(peak, transform.position.y - startY);
                if (_locomotion.IsAirborne)
                    leftGround = true;
                if (leftGround && !_locomotion.IsAirborne && Time.time - start > 0.2f)
                    break;
            }

            Debug.Log(string.Format(
                "PROBE jump height={0:F2} airtime={1:F2} airborne={2}",
                peak, Time.time - start, _locomotion.IsAirborne));
        }

        /// <summary>Кость ищется по имени: иерархия визуала может меняться.</summary>
        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
                return root;

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}

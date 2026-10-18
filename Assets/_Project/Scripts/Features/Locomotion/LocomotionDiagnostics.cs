using System.Collections;
using UnityEngine;

namespace FrostSpace.Features.Locomotion
{
    /// <summary>
    /// Проверочный стенд: ведёт робота вперёд сначала шагом, потом бегом, и
    /// считает, насколько стопа скользит по земле в момент контакта.
    ///
    /// Скольжение = горизонтальная скорость стопы в мире в те кадры, когда она
    /// у земли. Если клип и скорость персонажа согласованы, эта величина близка
    /// к нулю. Отчёт печатается в Console одной строкой PROBE.
    /// </summary>
    public sealed class LocomotionDiagnostics : MonoBehaviour
    {
        [SerializeField] private float _runSeconds = 3.5f;
        [SerializeField] private float _contactHeight = 0.05f;
        [SerializeField] private float _contactRiseLimit = 0.25f;

        private RobotLocomotion _locomotion;
        private Transform _foot;
        private string _ballPath =
            "Visual/Armature/root/pelvis/thigh_l/calf_l/foot_l/ball_l";

        private void Start()
        {
            _locomotion = GetComponent<RobotLocomotion>();
            _foot = transform.Find(_ballPath);
            if (_locomotion == null || _foot == null)
            {
                Debug.LogWarning("LocomotionDiagnostics: нет движения или кости ball_l");
                return;
            }

            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            yield return new WaitForSeconds(1f);
            yield return Measure("walk", false);
            yield return new WaitForSeconds(0.6f);
            yield return Measure("sprint", true);
            _locomotion.ClearInputOverride();
        }

        private IEnumerator Measure(string label, bool sprint)
        {
            // Бежим на юг: там нет забора, дома и камней — чистая полоса снега.
            _locomotion.SetInputOverride(Vector2.down, sprint);
            yield return new WaitForSeconds(0.8f);

            var elapsed = 0f;
            var contacts = 0;
            var slipSum = 0f;
            var slipWorst = 0f;
            var previousBody = _locomotion.transform.position;
            var previousFoot = _foot.position;
            var start = _locomotion.transform.position;

            while (elapsed < _runSeconds)
            {
                yield return null;
                var dt = Time.deltaTime;
                if (dt <= 0f)
                    continue;

                var body = _locomotion.transform.position;
                var foot = _foot.position;
                var bodyVelocity = (body - previousBody) / dt;
                var footVelocity = (foot - previousFoot) / dt;
                bodyVelocity.y = 0f;
                footVelocity.y = 0f;

                var planted = foot.y <= _contactHeight &&
                              Mathf.Abs(footVelocity.y) <= _contactRiseLimit;
                if (planted)
                {
                    var slip = footVelocity.magnitude;
                    contacts++;
                    slipSum += slip;
                    slipWorst = Mathf.Max(slipWorst, slip);
                }

                previousBody = body;
                previousFoot = foot;
                elapsed += dt;
            }

            var distance = Vector3.Distance(start, _locomotion.transform.position);
            var speed = _locomotion.PlanarSpeed;
            var slipAvg = contacts > 0 ? slipSum / contacts : 0f;
            Debug.Log(string.Format(
                "PROBE {0} speed={1:F2} animSpeed={2:F2} distance={3:F2} " +
                "contacts={4} slipAvg={5:F2} slipPercent={6:F0} slipWorst={7:F2}",
                label, speed, _locomotion.AnimatorSpeed, distance, contacts,
                slipAvg, speed > 0.05f ? slipAvg / speed * 100f : 0f, slipWorst));
        }
    }
}

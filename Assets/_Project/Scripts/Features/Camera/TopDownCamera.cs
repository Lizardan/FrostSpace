using UnityEngine;

namespace FrostSpace.Features.View
{
    /// <summary>
    /// Камера сверху-сзади: держит игрока в кадре и задаёт направление WASD.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class TopDownCamera : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private Vector3 _offset = new(0f, 11f, -9f);
        [SerializeField] private float _smoothing = 8f;
        [SerializeField] private float _lookHeight = 1.0f;

        private void LateUpdate()
        {
            if (_target == null)
                return;

            var desired = _target.position + _offset;
            var t = 1f - Mathf.Exp(-_smoothing * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, t);
            transform.LookAt(_target.position + Vector3.up * _lookHeight);
        }

        public void SetTarget(Transform target)
        {
            _target = target;
        }
    }
}

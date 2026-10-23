using UnityEngine;

namespace FrostSpace.Features.Menu
{
    /// <summary>
    /// Проезд в меню: мотик стоит в кадре, а местность едет назад по петле из
    /// сегментов. Даёт ощущение движения без перемещения камеры и байка.
    ///
    /// Петля задаётся массивом сегментов и их шагом: сегмент уезжает назад и,
    /// как только уходит за камеру, переставляется вперёд на полную длину
    /// петли. Поэтому вершины на стыке обязаны совпадать — это условие сборки
    /// полос в Blender, а не свойство кода.
    ///
    /// Параллакс — множитель скорости на компоненте: дальний план идёт
    /// медленнее, ближний быстрее. Отсчёт от <see cref="Time.unscaledTime"/>,
    /// чтобы пауза не замораживала движение.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(205)]
    public sealed class MenuRideScroll : MonoBehaviour
    {
        [Header("Петля")]
        [Tooltip("Сегменты местности по порядку движения")]
        [SerializeField] private Transform[] _segments;

        [Tooltip("Длина одного сегмента вдоль пути, м")]
        [SerializeField] private float _segmentLength = 24f;

        [Tooltip("Скорость слоя, м/с; 1 — скорость мотика")]
        [SerializeField] private float _layerSpeed = 9f;

        [Tooltip("Множитель для параллакса: 1 — как полосы, 0.5 — вдвое медленнее")]
        [SerializeField] private float _parallax = 1f;

        [Header("Мотик")]
        [Tooltip("Корень байка с наездником: покачивается на подушке")]
        [SerializeField] private Transform _bike;

        [Tooltip("Амплитуда покачивания по высоте, м")]
        [SerializeField] private float _bobAmplitude = 0.055f;

        [Tooltip("Период покачивания, с")]
        [SerializeField] private float _bobSeconds = 1.7f;

        [Tooltip("Амплитуда кивка корпуса, градусы")]
        [SerializeField] private float _pitchAmplitude = 1.6f;

        private Vector3[] _segmentBase;
        private float[] _segmentZ;
        private Vector3 _bikeBase;
        private Quaternion _bikeRotation;
        private float _loopLength;
        private float _trailZ;
        private bool _ready;

        private void OnEnable()
        {
            _ready = BuildLoop();
            if (!_ready)
                return;

            if (_bike != null)
            {
                _bikeBase = _bike.localPosition;
                _bikeRotation = _bike.localRotation;
            }

            Debug.Log(
                $"[MenuRideScroll] {name}: петля из {_segments.Length} сегментов " +
                $"по {_segmentLength:0.#} м, слой {_layerSpeed * _parallax:0.#} м/с", this);
        }

        /// <summary>
        /// Петля должна быть непрерывной: без единой длины шаг перестановки
        /// разойдётся с фактическим расстоянием и на стыке появится щель.
        /// </summary>
        private bool BuildLoop()
        {
            if (_segments == null || _segments.Length == 0)
            {
                Debug.LogError(
                    "[MenuRideScroll] не назначены сегменты — движение отключено", this);
                return false;
            }

            if (_segmentLength <= 0.01f)
            {
                Debug.LogError(
                    "[MenuRideScroll] _segmentLength должен быть больше нуля", this);
                return false;
            }

            _loopLength = _segmentLength * _segments.Length;
            _segmentBase = new Vector3[_segments.Length];
            _segmentZ = new float[_segments.Length];
            _trailZ = float.MaxValue;

            for (var i = 0; i < _segments.Length; i++)
            {
                var segment = _segments[i];
                if (segment == null)
                {
                    Debug.LogError(
                        $"[MenuRideScroll] сегмент {i} не назначен — движение отключено", this);
                    return false;
                }

                _segmentBase[i] = segment.localPosition;
                _segmentZ[i] = segment.localPosition.z;
                _trailZ = Mathf.Min(_trailZ, segment.localPosition.z);
            }

            return true;
        }

        private void Update()
        {
            if (!_ready)
                return;

            var deltaTime = Time.unscaledDeltaTime;
            var step = _layerSpeed * _parallax * deltaTime;

            // Мотик едет вперёд, значит мир уезжает назад — по -Z.
            // Перестановка идёт по самому заднему сегменту петли, а не по
            // собственному старту: иначе первый уехавший сегмент возвращается на
            // всю длину петли позже остальных и в цепочке появляется разрыв.
            for (var i = 0; i < _segments.Length; i++)
            {
                _segmentZ[i] -= step;
                if (_segmentZ[i] < _trailZ - _segmentLength)
                    _segmentZ[i] += _loopLength;

                var position = _segmentBase[i];
                position.z = _segmentZ[i];
                _segments[i].localPosition = position;
            }

            BobBike();
        }

        /// <summary>Подушка байка дышит: высота и кивок по двум разным периодам.</summary>
        private void BobBike()
        {
            if (_bike == null)
                return;

            var time = Time.unscaledTime;
            var phase = time * Mathf.PI * 2f / Mathf.Max(0.1f, _bobSeconds);
            var position = _bikeBase;
            position.y += Mathf.Sin(phase) * _bobAmplitude;
            _bike.localPosition = position;

            // Кивок чуть отстаёт по фазе — так покачивание не выглядит маятником.
            var pitch = Mathf.Sin(phase * 0.5f + 0.8f) * _pitchAmplitude;
            _bike.localRotation = _bikeRotation * Quaternion.Euler(pitch, 0f, 0f);
        }
    }
}

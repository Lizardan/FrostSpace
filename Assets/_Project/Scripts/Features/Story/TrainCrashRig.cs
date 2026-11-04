using System.Collections;
using UnityEngine;

namespace FrostSpace.Features.Story
{
    /// <summary>
    /// Авария в конце рейса (TR-09): поезд не останавливается у платформы, а бьётся.
    ///
    /// Сцена играет аварию без физики и без новых ассетов, потому что оба уже есть в
    /// вагоне: мир за окнами — <see cref="TrainMotionRig"/>, корпус — <c>TrainCar</c>,
    /// свет — три лампы кабины, снег — две системы у окон. Авария только распоряжается
    /// ими: тормозит мир, роняет и кренит корпус, гасит свет с искрой, выбивает снег
    /// внутрь и оставляет одну аварийную лампу.
    ///
    /// Камера приходит сюда от режиссёра: в кинематографическом режиме риг её не
    /// пишет, поэтому тряска — единственный владелец кадра. После аварии камера
    /// возвращается ровно в исходную позу, иначе занавес закрывался бы на дрожащем
    /// кадре.
    ///
    /// Числа тряски — чистые функции (<see cref="ShakeOffset"/>, <see cref="ShakeRoll"/>):
    /// их проверяют EditMode-тесты, а не глаз.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrainCrashRig : MonoBehaviour
    {
        /// <summary>Торможение до удара: мир за окнами ещё катится, но уже теряет ход.</summary>
        public const float BrakeSeconds = 1.4f;

        /// <summary>Сам удар: просадка, крен, тряска камеры.</summary>
        public const float ImpactSeconds = 0.45f;

        /// <summary>Оседание: второй толчок, скрежет, аварийная лампа.</summary>
        public const float SettleSeconds = 1.2f;

        /// <summary>Тишина перед занавесом: кадр стоит, слышен только ветер.</summary>
        public const float QuietSeconds = 1.0f;

        [Header("Ссылки")]
        [Tooltip("Корпус вагона: его роняем и креним")]
        [SerializeField] private Transform _body;
        [Tooltip("Лампы кабины: гаснут от удара, одна возвращается аварийной")]
        [SerializeField] private Light[] _lamps = new Light[0];
        [Tooltip("Параллакс за окнами: тормозит до удара")]
        [SerializeField] private TrainMotionRig _motion;
        [Tooltip("Системы снега у окон: удар выбивает из них залп внутрь")]
        [SerializeField] private ParticleSystem[] _snow = new ParticleSystem[0];

        [Header("Звук")]
        [Tooltip("Удар: низкий толчок с металлом")]
        [SerializeField] private AudioClip _impact;
        [Tooltip("Скрежет оседания")]
        [SerializeField] private AudioClip _groan;
        [SerializeField, Range(0f, 1f)] private float _volume = 0.9f;

        [Header("Удар")]
        [Tooltip("Просадка корпуса вниз, м")]
        [SerializeField] private float _dropMeters = 0.14f;
        [Tooltip("Крен корпуса после удара, градусы")]
        [SerializeField] private float _rollDegrees = 7f;
        [Tooltip("Тангаж корпуса после удара, градусы")]
        [SerializeField] private float _pitchDegrees = 2f;
        [Tooltip("Размах тряски камеры, м")]
        [SerializeField] private float _shakeMeters = 0.11f;
        [Tooltip("Крен камеры при тряске, градусы")]
        [SerializeField] private float _shakeRollDegrees = 2.2f;
        [Tooltip("Цвет аварийной лампы после удара")]
        [SerializeField] private Color _emergencyColor = new(1f, 0.62f, 0.25f, 1f);
        [Tooltip("Яркость аварийной лампы после удара")]
        [SerializeField, Range(0f, 2f)] private float _emergencyIntensity = 0.28f;
        [Tooltip("Сколько частиц снега выбивает ударом, на систему")]
        [SerializeField] private int _snowBurst = 90;

        private AudioSource _audio;
        private Vector3 _bodyPosition;
        private Quaternion _bodyRotation;
        private float[] _lampIntensity = new float[0];
        private Color[] _lampColor = new Color[0];

        private void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            Recall();
        }

        /// <summary>
        /// Запомнить позу корпуса и ламп. Авария всегда считается от этой позы:
        /// повторный запуск после отладки не должен копить крен.
        /// </summary>
        public void Recall()
        {
            if (_body != null)
            {
                _bodyPosition = _body.localPosition;
                _bodyRotation = _body.localRotation;
            }

            _lampIntensity = new float[_lamps.Length];
            _lampColor = new Color[_lamps.Length];
            for (var i = 0; i < _lamps.Length; i++)
            {
                if (_lamps[i] == null)
                    continue;

                _lampIntensity[i] = _lamps[i].intensity;
                _lampColor[i] = _lamps[i].color;
            }
        }

        /// <summary>
        /// Проиграть аварию. Камера возвращается в исходную позу: занавес закрывает
        /// уже спокойный кадр, а не трясущийся.
        /// </summary>
        public IEnumerator Play(Camera camera)
        {
            Recall();

            if (_motion != null)
                _motion.Stop(BrakeSeconds);

            yield return Brake();
            yield return Impact(camera);
            yield return Settle(camera);

            if (QuietSeconds > 0f)
                yield return Quiet();
        }

        /// <summary>
        /// Торможение: мир замедляется, лампы дают два коротких провала — машина
        /// ещё пытается ехать. Провал короткий и неполный, чтобы это читалось как
        /// тревога, а не как уже случившаяся авария.
        /// </summary>
        private IEnumerator Brake()
        {
            var elapsed = 0f;
            while (elapsed < BrakeSeconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / BrakeSeconds);

                // Два провала за торможение, каждый — 0,18 с.
                var dip = Mathf.Sin(t * Mathf.PI * 4f);
                var level = 1f - 0.75f * Mathf.Abs(dip);
                SetLampLevel(level);

                // Вагон качает на торможении: без этого удар приходит из неподвижного
                // кадра и читается как смена света, а не как потеря хода.
                if (_body != null)
                    _body.localRotation = _bodyRotation * Quaternion.Euler(
                        0f, 0f, Mathf.Sin(t * Mathf.PI * 2f) * 0.9f);
                yield return null;
            }

            SetLampLevel(1f);
            if (_body != null)
                _body.localRotation = _bodyRotation;
        }

        /// <summary>Удар: корпус падает и кренится, свет гаснет, снег бьёт внутрь.</summary>
        private IEnumerator Impact(Camera camera)
        {
            var cameraTransform = camera != null ? camera.transform : null;
            var cameraPosition = cameraTransform != null ? cameraTransform.position : Vector3.zero;
            var cameraRotation = cameraTransform != null ? cameraTransform.rotation : Quaternion.identity;

            PlayOne(_impact, 1f, 0.85f);
            Burst();
            var spark = Spark();

            var elapsed = 0f;
            while (elapsed < ImpactSeconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / ImpactSeconds);
                var ease = Ease(t);

                PushBody(ease, 1f);

                if (t < 0.12f)
                    SetLampLevel(1f - t / 0.12f);
                else
                    SetLampLevel(0f);

                ShakeCamera(cameraTransform, cameraPosition, cameraRotation, t, 1f);
                yield return null;
            }

            PushBody(1f, 1f);
            SetLampLevel(0f);
            RestoreCamera(cameraTransform, cameraPosition, cameraRotation);

            if (spark != null)
                Destroy(spark.gameObject);
        }

        /// <summary>
        /// Оседание: остаточная дрожь, аварийная лампа на четверть, скрежет.
        /// Корпус остаётся в крене — поезд сошёл и не встанет сам.
        /// </summary>
        private IEnumerator Settle(Camera camera)
        {
            var cameraTransform = camera != null ? camera.transform : null;
            var cameraPosition = cameraTransform != null ? cameraTransform.position : Vector3.zero;
            var cameraRotation = cameraTransform != null ? cameraTransform.rotation : Quaternion.identity;

            PlayOne(_groan, 0.8f, 0.55f);

            var elapsed = 0f;
            while (elapsed < SettleSeconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / SettleSeconds);

                // Второй, слабый толчок на четверти оседания: корпус ещё садится.
                var judder = Mathf.Sin(t * Mathf.PI * 6f) * 0.012f * (1f - t);
                PushBody(1f, 1f - 0.25f * t, judder);

                var level = Mathf.Lerp(0f, _emergencyIntensity, Ease(t));
                if (_lamps.Length > 0 && _lamps[0] != null)
                {
                    _lamps[0].color = _emergencyColor;
                    _lamps[0].intensity = level;
                }

                ShakeCamera(cameraTransform, cameraPosition, cameraRotation, t, 0.55f);
                yield return null;
            }

            PushBody(1f, 0.75f);
            ShakeCamera(cameraTransform, cameraPosition, cameraRotation, 1f, 0.55f);
            RestoreCamera(cameraTransform, cameraPosition, cameraRotation);
        }

        /// <summary>
        /// Тишина: кадр почти стоит, но аварийная лампа доживает — редкое мигание
        /// оставляет в кадре живой свет. Ровная лампа после удара выглядит как
        /// выключенная сцена, а не как подбитый вагон.
        /// </summary>
        private IEnumerator Quiet()
        {
            var lamp = _lamps.Length > 0 ? _lamps[0] : null;
            var elapsed = 0f;
            while (elapsed < QuietSeconds)
            {
                elapsed += Time.deltaTime;
                if (lamp != null)
                    lamp.intensity = _emergencyIntensity *
                        (0.68f + 0.32f * Mathf.Abs(Mathf.Sin(elapsed * 19f)));
                yield return null;
            }

            if (lamp != null)
                lamp.intensity = _emergencyIntensity;
        }

        /// <summary>Корпус: просадка и крен, доля <paramref name="ease"/> от полной амплитуды.</summary>
        private void PushBody(float ease, float scale, float extraDown = 0f)
        {
            if (_body == null)
                return;

            var drop = _dropMeters * ease * scale + Mathf.Abs(extraDown);
            _body.localPosition = _bodyPosition + Vector3.down * drop;
            _body.localRotation = _bodyRotation * Quaternion.Euler(
                _pitchDegrees * ease * scale, 0f, _rollDegrees * ease * scale);
        }

        /// <summary>
        /// Тряска камеры: смещение по локальным осям и крен. Поза восстанавливается
        /// каждый кадр от исходной, поэтому тряска не накапливается.
        /// </summary>
        private void ShakeCamera(
            Transform camera, Vector3 position, Quaternion rotation, float t, float scale)
        {
            if (camera == null)
                return;

            var offset = ShakeOffset(t) * _shakeMeters * scale;
            camera.position = position + rotation * offset;
            camera.rotation = rotation * Quaternion.Euler(
                0f, 0f, ShakeRoll(t) * _shakeRollDegrees * scale);
        }

        private static void RestoreCamera(Transform camera, Vector3 position, Quaternion rotation)
        {
            if (camera == null)
                return;

            camera.position = position;
            camera.rotation = rotation;
        }

        private void Burst()
        {
            if (_snowBurst <= 0)
                return;

            for (var i = 0; i < _snow.Length; i++)
            {
                if (_snow[i] != null)
                    _snow[i].Emit(_snowBurst);
            }
        }

        /// <summary>
        /// Искра удара: короткая тёплая вспышка у стены. Живёт один удар и удаляется,
        /// чтобы не оставлять в сцене лишний источник света.
        /// </summary>
        private Light Spark()
        {
            if (_body == null)
                return null;

            var go = new GameObject("CrashSpark");
            go.transform.position = _body.TransformPoint(new Vector3(0f, 1.9f, 2.4f));

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.36f, 1f);
            light.intensity = 7f;
            light.range = 11f;
            light.shadows = LightShadows.None;
            return light;
        }

        private void SetLampLevel(float level)
        {
            for (var i = 0; i < _lamps.Length; i++)
            {
                if (_lamps[i] == null)
                    continue;

                _lamps[i].intensity = _lampIntensity[i] * Mathf.Clamp01(level);
            }
        }

        private void PlayOne(AudioClip clip, float volumeScale, float pitch)
        {
            if (clip == null || _audio == null)
                return;

            _audio.pitch = pitch;
            _audio.PlayOneShot(clip, _volume * volumeScale);
        }

        /// <summary>Резкая, но не линейная просадка: удар приходит быстро, оседает мягко.</summary>
        public static float Ease(float t01)
        {
            var t = Mathf.Clamp01(t01);
            return 1f - Mathf.Pow(1f - t, 3f);
        }

        /// <summary>
        /// Смещение тряски в единицах амплитуды: ноль до удара и после него, пик — в
        /// первые кадры. Разные частоты по осям дают дрожь, а не качание маятника.
        /// </summary>
        public static Vector3 ShakeOffset(float t01)
        {
            var amp = Envelope(t01);
            if (amp <= 0f)
                return Vector3.zero;

            return new Vector3(
                Mathf.Sin(t01 * 71f) * 0.55f,
                Mathf.Sin(t01 * 53f + 1.7f) * 0.85f,
                Mathf.Sin(t01 * 61f + 3.1f) * 0.35f) * amp;
        }

        /// <summary>Крен камеры от тряски: та же огибающая, другая частота.</summary>
        public static float ShakeRoll(float t01)
        {
            var amp = Envelope(t01);
            if (amp <= 0f)
                return 0f;

            return Mathf.Sin(t01 * 47f + 0.6f) * amp;
        }

        /// <summary>Огибающая тряски: быстрый вход за 8 % удара и затухание в квадрате.</summary>
        private static float Envelope(float t01)
        {
            if (t01 <= 0f || t01 >= 1f)
                return 0f;

            var attack = Mathf.Clamp01(t01 / 0.08f);
            var decay = (1f - t01) * (1f - t01);
            return attack * decay;
        }
    }
}

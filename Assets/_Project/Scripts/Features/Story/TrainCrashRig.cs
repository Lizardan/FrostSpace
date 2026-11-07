using System.Collections;
using UnityEngine;

namespace FrostSpace.Features.Story
{
    /// <summary>
    /// Авария в конце рейса (TR-09): поезд не останавливается у платформы, а бьётся.
    ///
    /// Событие должно быть видно в кадре, поэтому у него три слоя: корпус
    /// (вертикальный отскок, который возвращается в уровень), свет (провалы,
    /// гашение, янтарная лампа) и частицы — дым из-под обшивки, пыль под потолком
    /// и стружка коротких замыканий. Без частиц удар читался как «сменили освещение
    /// и наклонили вагон».
    ///
    /// Наклона корпуса нет намеренно: крен, оставшийся после удара, расходился бы
    /// с составом, который приезжает в депо ровным. Удар держится на просадке,
    /// тряске камеры, дыме и звуке.
    ///
    /// Огня нет по решению владельца: тёплый свет дают только вспышки замыканий,
    /// постоянный источник один — аварийная лампа. Дым идёт и в тишине перед
    /// занавесом, потому что `Frontier` обязан принять подбитый состав, а не целый.
    ///
    /// Камера приходит сюда от режиссёра: в кинематографическом режиме риг её не
    /// пишет, поэтому тряска — единственный владелец кадра. Тряска возвращается,
    /// а сдвиг к дыму остаётся: финальный кадр смотрит на тлеющую обшивку.
    ///
    /// Числа тряски и сдвига — чистые функции (<see cref="ShakeOffset"/>,
    /// <see cref="ShakeRoll"/>, <see cref="DriftOffset"/>): их проверяют EditMode-тесты,
    /// а не глаз.
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
        [Tooltip("Треск короткого замыкания — один клип на три вспышки, pitch разный")]
        [SerializeField] private AudioClip _click;
        [Tooltip("Шипение пара: идёт всё оседание и тишину, пока дым в кадре")]
        [SerializeField] private AudioClip _steam;
        [SerializeField, Range(0f, 1f)] private float _volume = 0.9f;
        [SerializeField, Range(0f, 1f)] private float _steamVolume = 0.35f;

        [Header("Удар")]
        [Tooltip("Первая просадка корпуса вниз, м")]
        [SerializeField] private float _dropMeters = 0.14f;
        [Tooltip("Как быстро затухает отскок (долей в секунду)")]
        [SerializeField] private float _bounceDamping = 4.2f;
        [Tooltip("Частота отскока подвески, Гц")]
        [SerializeField] private float _bounceHz = 1.6f;
        [Tooltip("Остаточная дрожь корпуса на оседании, доля просадки")]
        [SerializeField] private float _settleTremble = 0.09f;
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

        [Header("Дым, пыль, искры")]
        [Tooltip("Дым из-под обшивки у кабины")]
        [SerializeField] private ParticleSystem _cabinSmoke;
        [Tooltip("Дым из щели под потолком")]
        [SerializeField] private ParticleSystem _ceilingSmoke;
        [Tooltip("Пыль и снежная крошка под потолком")]
        [SerializeField] private ParticleSystem _dust;
        [Tooltip("Стружка коротких замыканий")]
        [SerializeField] private ParticleSystem _sparks;
        [Tooltip("Частиц дыма на удар, на систему")]
        [SerializeField] private int _smokeBurst = 90;
        [Tooltip("Частиц пыли на удар")]
        [SerializeField] private int _dustBurst = 60;
        [Tooltip("Частиц стружки на одно замыкание")]
        [SerializeField] private int _sparkBurst = 30;
        [Tooltip("Моменты замыканий от начала оседания, с")]
        [SerializeField] private float[] _faultTimes = new[] { 0.35f, 0.70f, 1.05f };
        [Tooltip("Локальные точки замыканий в вагоне — туда же бьёт вспышка")]
        [SerializeField] private Vector3[] _faultPoints = new[]
        {
            new Vector3(-1.6f, 2.05f, 3.2f),
            new Vector3(1.9f, 2.25f, -2.4f),
            new Vector3(-0.4f, 1.95f, -6.1f),
        };
        [Tooltip("Цвет вспышки замыкания")]
        [SerializeField] private Color _faultColor = new(1f, 0.78f, 0.42f, 1f);
        [SerializeField] private float _faultLightIntensity = 5f;
        [SerializeField] private float _faultLightRange = 7f;

        [Header("Камера после удара")]
        [Tooltip("Куда уходит камера во время оседания — к источнику дыма. Сдвиг остаётся: финальный кадр смотрит на тлеющую обшивку")]
        [SerializeField] private Vector3 _cameraDrift = new(0.18f, -0.12f, 0.34f);
        [Tooltip("За сколько оседания камера доходит до новой позы, с")]
        [SerializeField] private float _cameraDriftSeconds = 0.9f;

        private AudioSource _audio;
        private AudioSource _steamSource;
        private Vector3 _bodyPosition;
        private float[] _lampIntensity = new float[0];
        private Color[] _lampColor = new Color[0];
        private ParticleSystem[] _smoke = new ParticleSystem[0];
        private float[] _smokeRate = new float[0];
        private Vector3 _cameraDrifted;
        private readonly System.Collections.Generic.List<Light> _faultLights = new();

        private void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;

            _steamSource = gameObject.AddComponent<AudioSource>();
            _steamSource.playOnAwake = false;
            _steamSource.loop = true;
            _steamSource.clip = _steam;
            _steamSource.spatialBlend = 1f;

            _smoke = BuildSmoke();
            Recall();
        }

        private ParticleSystem[] BuildSmoke()
        {
            var list = new System.Collections.Generic.List<ParticleSystem>(2);
            if (_cabinSmoke != null) list.Add(_cabinSmoke);
            if (_ceilingSmoke != null) list.Add(_ceilingSmoke);
            return list.ToArray();
        }

        /// <summary>
        /// Запомнить позу корпуса и ламп. Авария всегда считается от этой позы:
        /// повторный запуск после отладки не должен копить крен.
        /// </summary>
        public void Recall()
        {
            if (_body != null)
                _bodyPosition = _body.localPosition;

            _lampIntensity = new float[_lamps.Length];
            _lampColor = new Color[_lamps.Length];
            for (var i = 0; i < _lamps.Length; i++)
            {
                if (_lamps[i] == null)
                    continue;

                _lampIntensity[i] = _lamps[i].intensity;
                _lampColor[i] = _lamps[i].color;
            }

            // Дым и пыль переживают повторный Play только как накопленный кадр:
            // второй запуск без сброса показал бы салон, уже затянутый дымом от
            // прошлого. Исходную плотность подсматриваем один раз — иначе второй
            // Recall запомнил бы уже изменённую нами.
            if (_smokeRate.Length != _smoke.Length)
            {
                _smokeRate = new float[_smoke.Length];
                for (var i = 0; i < _smoke.Length; i++)
                    _smokeRate[i] = _smoke[i].emission.rateOverTime.constant;
            }

            ResetSmoke(1f);
            ClearParticles(_dust);
            ClearParticles(_sparks);

            for (var i = _faultLights.Count - 1; i >= 0; i--)
            {
                if (_faultLights[i] != null)
                    Destroy(_faultLights[i].gameObject);
            }

            _faultLights.Clear();
            _cameraDrifted = Vector3.zero;

            if (_steamSource != null)
                _steamSource.Stop();
        }

        /// <summary>Плотность дыма как доля от дизайнерской: 0 — выключен, 1 — как в сцене.</summary>
        private void ResetSmoke(float level)
        {
            for (var i = 0; i < _smoke.Length; i++)
            {
                var ps = _smoke[i];
                if (ps == null)
                    continue;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = _smokeRate[i] * Mathf.Clamp01(level);
                ps.Stop();
                ps.Clear();
            }
        }

        private static void ClearParticles(ParticleSystem ps)
        {
            if (ps == null)
                return;

            ps.Stop();
            ps.Clear();
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

            // До удара дым уже есть: изоляция тлеет, и удар забирает не пустой салон.
            SetSmokeLevel(0.30f);

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
        ///
        /// Корпус на торможении не качается: наклон, оставшийся после удара,
        /// расходился бы с ровным составом, который приезжает в депо.
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
                yield return null;
            }

            SetLampLevel(1f);
        }

        /// <summary>Удар: корпус падает и кренится, свет гаснет, снег бьёт внутрь.</summary>
        private IEnumerator Impact(Camera camera)
        {
            var cameraTransform = camera != null ? camera.transform : null;
            var cameraPosition = cameraTransform != null ? cameraTransform.position : Vector3.zero;
            var cameraRotation = cameraTransform != null ? cameraTransform.rotation : Quaternion.identity;

            PlayOne(_impact, 1f, 0.85f);
            Burst();
            SmokeBurst();
            DustBurst();
            SetSmokeLevel(1f);
            StartSteam();
            var spark = Spark();

            var elapsed = 0f;
            while (elapsed < ImpactSeconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / ImpactSeconds);

                JoltBody(Jolt(t, _bounceDamping, _bounceHz));

                if (t < 0.12f)
                    SetLampLevel(1f - t / 0.12f);
                else
                    SetLampLevel(0f);

                ShakeCamera(cameraTransform, cameraPosition, cameraRotation, t, 1f);
                yield return null;
            }

            JoltBody(0f);
            SetLampLevel(0f);
            RestoreCamera(cameraTransform, cameraPosition, cameraRotation);

            if (spark != null)
                Destroy(spark.gameObject);
        }

        /// <summary>
        /// Оседание: остаточная дрожь подвески, аварийная лампа на четверть, скрежет,
        /// замыкания и сдвиг камеры к дыму. Корпус к концу стоит ровно: в депо состав
        /// приезжает не перевёрнутым, а подбитым — это разная история.
        /// </summary>
        private IEnumerator Settle(Camera camera)
        {
            var cameraTransform = camera != null ? camera.transform : null;
            var cameraPosition = cameraTransform != null ? cameraTransform.position : Vector3.zero;
            var cameraRotation = cameraTransform != null ? cameraTransform.rotation : Quaternion.identity;

            PlayOne(_groan, 0.8f, 0.55f);
            SetSmokeLevel(0.90f);

            var elapsed = 0f;
            var fault = 0;
            while (elapsed < SettleSeconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / SettleSeconds);

                // Остаточная дрожь: подвеска доигрывает отскок и возвращается в уровень.
                JoltBody(Mathf.Sin(t * Mathf.PI * 6f) * _settleTremble * (1f - t));

                var level = Mathf.Lerp(0f, _emergencyIntensity, Ease(t));
                if (_lamps.Length > 0 && _lamps[0] != null)
                {
                    _lamps[0].color = _emergencyColor;
                    _lamps[0].intensity = level;
                }

                // Замыкания идут по расписанию, а не кадрами: момент вспышки
                // держится в числах сцены и проверяется тестом.
                while (fault < _faultTimes.Length && elapsed >= _faultTimes[fault])
                {
                    Fault(fault);
                    fault++;
                }

                _cameraDrifted = DriftOffset(elapsed, _cameraDriftSeconds, _cameraDrift);
                ShakeCamera(cameraTransform, cameraPosition, cameraRotation, t, 0.55f);
                yield return null;
            }

            JoltBody(0f);
            _cameraDrifted = DriftOffset(SettleSeconds, _cameraDriftSeconds, _cameraDrift);
            ShakeCamera(cameraTransform, cameraPosition, cameraRotation, 1f, 0.55f);
            RestoreCamera(cameraTransform, cameraPosition, cameraRotation);
            SetSmokeLevel(0.5f);
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
                var t = Mathf.Clamp01(elapsed / QuietSeconds);
                if (lamp != null)
                    lamp.intensity = _emergencyIntensity *
                        (0.68f + 0.32f * Mathf.Abs(Mathf.Sin(elapsed * 19f)));

                // Дым в тишине не стоит на месте: он редеет, но не исчезает —
                // занавес закрывается на всё ещё тлеющий салон.
                SetSmokeLevel(Mathf.Lerp(0.55f, 0.40f, t));
                yield return null;
            }

            if (lamp != null)
                lamp.intensity = _emergencyIntensity;
        }

        /// <summary>
        /// Корпус: только вертикальное смещение в единицах просадки. Поворота нет —
        /// наклон пережил бы занавес и разошёлся с ровным составом, который приезжает
        /// в депо.
        /// </summary>
        private void JoltBody(float down)
        {
            if (_body == null)
                return;

            _body.localPosition = _bodyPosition + Vector3.down * (_dropMeters * down);
        }

        /// <summary>
        /// Отскок подвески: 1 в момент удара, затухающие качания к нулю. Косинус
        /// даёт и подлёт выше уровня после первого прогиба — так держит рессора,
        /// а не падающее тело.
        /// </summary>
        public static float Jolt(float t01, float damping, float hz)
        {
            var t = Mathf.Clamp01(t01);
            if (t <= 0f)
                return 1f;
            if (t >= 1f)
                return 0f;

            return Mathf.Exp(-damping * t) * Mathf.Cos(2f * Mathf.PI * hz * t);
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

            // Тряска пишется поверх накопленного сдвига, а не вместо него: иначе
            // последний кадр оседания вернулся бы на исходную позицию и удар
            // выглядел бы как «камеру тряхнуло и отпустили».
            var offset = ShakeOffset(t) * _shakeMeters * scale + _cameraDrifted;
            camera.position = position + rotation * offset;
            camera.rotation = rotation * Quaternion.Euler(
                0f, 0f, ShakeRoll(t) * _shakeRollDegrees * scale);
        }

        private void RestoreCamera(Transform camera, Vector3 position, Quaternion rotation)
        {
            if (camera == null)
                return;

            camera.position = position + rotation * _cameraDrifted;
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

        /// <summary>Дым на удар: залп из обеих систем, дальше работает штатный emission.</summary>
        private void SmokeBurst()
        {
            if (_smokeBurst <= 0)
                return;

            for (var i = 0; i < _smoke.Length; i++)
            {
                var ps = _smoke[i];
                if (ps == null)
                    continue;

                ps.Play();
                ps.Emit(_smokeBurst);
            }
        }

        /// <summary>Пыль и снежная крошка под потолком: один короткий залп на удар.</summary>
        private void DustBurst()
        {
            if (_dust == null || _dustBurst <= 0)
                return;

            _dust.Play();
            _dust.Emit(_dustBurst);
        }

        /// <summary>Плотность дыма как доля от дизайнерской. Выключенным не становится никогда.</summary>
        private void SetSmokeLevel(float level)
        {
            var k = Mathf.Clamp01(level);
            for (var i = 0; i < _smoke.Length; i++)
            {
                var ps = _smoke[i];
                if (ps == null)
                    continue;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = _smokeRate[i] * k;
                if (!ps.isPlaying)
                    ps.Play();
            }
        }

        private void StartSteam()
        {
            if (_steamSource == null || _steam == null || _steamSource.isPlaying)
                return;

            _steamSource.volume = _steamVolume;
            _steamSource.Play();
        }

        /// <summary>
        /// Короткое замыкание: стружка, щелчок и вспышка в своей точке. Огня нет —
        /// тёплый свет даёт только вспышка, янтарная лампа остаётся единственным
        /// постоянным источником финала.
        /// </summary>
        private void Fault(int index)
        {
            var local = index < _faultPoints.Length && _faultPoints.Length > 0
                ? _faultPoints[index]
                : new Vector3(0f, 2f, 0f);
            var world = _body != null ? _body.TransformPoint(local) : transform.position + local;

            if (_sparks != null)
            {
                _sparks.transform.position = world;
                _sparks.Play();
                _sparks.Emit(_sparkBurst);
            }

            PlayOne(_click, 0.75f + 0.08f * index, 0.9f + 0.13f * index);
            StartCoroutine(FaultFlash(world, index));
        }

        private IEnumerator FaultFlash(Vector3 world, int index)
        {
            var go = new GameObject("CrashFault" + index);
            go.transform.position = world;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = _faultColor;
            light.range = _faultLightRange;
            light.intensity = _faultLightIntensity;
            light.shadows = LightShadows.None;
            _faultLights.Add(light);

            const float life = 0.09f;
            var elapsed = 0f;
            while (elapsed < life)
            {
                elapsed += Time.deltaTime;
                light.intensity = _faultLightIntensity * (1f - elapsed / life);
                yield return null;
            }

            _faultLights.Remove(light);
            Destroy(go);
        }

        /// <summary>
        /// Сдвиг камеры к дыму: доля от полного смещения по времени оседания.
        /// Отдельная функция, потому что величина финального кадра — это число
        /// постановки, а не побочный эффект тряски.
        /// </summary>
        public static Vector3 DriftOffset(float elapsed, float seconds, Vector3 drift)
        {
            return drift * Ease(Mathf.Clamp01(elapsed / Mathf.Max(0.01f, seconds)));
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

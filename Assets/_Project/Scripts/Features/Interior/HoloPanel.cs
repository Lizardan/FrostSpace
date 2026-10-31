using TMPro;
using UnityEngine;

namespace FrostSpace.Features.Interior
{
    /// <summary>
    /// Декоративная голопанель на столе у кабины.
    ///
    /// Непрерывное движение (скан-полоса и бегущие строки) живёт в шейдере,
    /// сюда выносятся только фазы: <c>MaterialPropertyBlock</c> не создаёт
    /// копию материала и не ломает SRP-батчинг для остальных объектов.
    /// Читаемый текст — 3D TMP, он меняется раз в несколько секунд.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HoloPanel : MonoBehaviour
    {
        private static readonly int ScanId = Shader.PropertyToID("_ScanPhase");
        private static readonly int LineId = Shader.PropertyToID("_LinePhase");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");

        [Header("Ссылки")]
        [SerializeField] private Renderer _screen;
        [Tooltip("Текст панели; меняется блок целиком")]
        [SerializeField] private TMP_Text _body;

        [Header("Анимация")]
        [Tooltip("Скорость скан-полосы, долей экрана в секунду")]
        [SerializeField] private float _scanSpeed = 0.22f;
        [Tooltip("Скорость бегущих строк")]
        [SerializeField] private float _lineSpeed = 0.35f;
        [Tooltip("Как часто меняется блок текста, с")]
        [SerializeField] private float _lineInterval = 2.5f;
        [SerializeField] private string[] _messages = new string[0];

        private MaterialPropertyBlock _block;
        private float _scan;
        private float _line;
        private float _timer;
        private int _index;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (_body != null && _messages.Length > 0)
                _body.text = _messages[0];

            if (_screen == null)
                return;

            _screen.GetPropertyBlock(_block);
            _block.SetFloat(SeedId, Random.value * 10f);
            _screen.SetPropertyBlock(_block);
        }

        private void Update()
        {
            if (_screen == null)
                return;

            _scan = Mathf.Repeat(_scan + _scanSpeed * Time.deltaTime, 1f);
            _line = Mathf.Repeat(_line + _lineSpeed * Time.deltaTime, 1f);

            _screen.GetPropertyBlock(_block);
            _block.SetFloat(ScanId, _scan);
            _block.SetFloat(LineId, _line);
            _screen.SetPropertyBlock(_block);

            if (_body == null || _messages.Length == 0)
                return;

            _timer += Time.deltaTime;
            if (_timer < _lineInterval)
                return;

            _timer = 0f;
            _index = (_index + 1) % _messages.Length;
            _body.text = _messages[_index];
        }
    }
}

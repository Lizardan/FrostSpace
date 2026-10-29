using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace FrostSpace.Features.Dialogue
{
    /// <summary>
    /// Плашка «E» над роботом, с которым можно поговорить. Живёт на сценовом
    /// актёре рядом с <see cref="DialogueSpeaker"/>; видна только когда разговор
    /// доступен, поэтому после выбора реплики или во время разговора гаснет.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DialogueMarker : MonoBehaviour
    {
        [SerializeField] private DialogueSpeaker _speaker;
        [Tooltip("Высота плашки над пивотом актёра, м")]
        [SerializeField] private float _height = 2.35f;
        [SerializeField] private Color _color = new(0.302f, 0.902f, 1f, 1f);

        private TextMeshPro _label;
        private Camera _camera;

        private void Awake()
        {
            if (_speaker == null)
                _speaker = GetComponent<DialogueSpeaker>();

            var go = new GameObject("TalkMarker", typeof(RectTransform), typeof(TextMeshPro));
            go.transform.SetParent(transform, false);

            _label = go.GetComponent<TextMeshPro>();
            if (TMP_Settings.defaultFontAsset != null)
                _label.font = TMP_Settings.defaultFontAsset;
            _label.text = "E";
            _label.fontSize = 0.5f;
            _label.alignment = TextAlignmentOptions.Center;
            _label.color = _color;
            _label.rectTransform.sizeDelta = new Vector2(1f, 0.7f);
            _label.textWrappingMode = TextWrappingModes.NoWrap;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private void LateUpdate()
        {
            if (_label == null)
                return;

            var visible = _speaker != null && _speaker.CanTalk;
            _label.gameObject.SetActive(visible);
            if (!visible)
                return;

            _label.transform.position = transform.position + Vector3.up * _height;

            if (_camera == null)
                _camera = Camera.main;
            if (_camera != null)
                _label.transform.rotation = _camera.transform.rotation;
        }
    }
}

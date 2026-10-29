using UnityEngine;
using UnityEngine.UI;

namespace FrostSpace.Features.Character
{
    /// <summary>
    /// 3D-превью корпуса внутри карточки. Отдельная камера рендерит модель
    /// в <see cref="RenderTexture"/>, а карточка показывает её как RawImage:
    /// три превью живут одновременно и не мешают друг другу.
    ///
    /// Рендер-риги стоят далеко под сценой и разнесены по X, поэтому ни одна
    /// камера не видит чужую модель, а главная камера не видит ни одну.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterCardPreview : MonoBehaviour
    {
        private const float SlotSpacing = 24f;
        private const float StageDepth = -500f;

        [SerializeField] private Vector2Int _resolution = new(512, 640);
        [SerializeField] private float _fieldOfView = 32f;
        [Tooltip("Камера смотрит на эту высоту модели, м")]
        [SerializeField] private float _lookHeight = 1f;

        private GameObject _rig;
        private Camera _camera;
        private RenderTexture _texture;

        /// <summary>Поставить в карточку превью корпуса; соседние карточки не трогаются.</summary>
        public void Show(RobotProfile profile, RawImage target, int slot)
        {
            Clear();
            if (profile == null || profile.VisualPrefab == null || target == null)
                return;

            // Риг живёт отдельно от карточки: компонент висит на UI-объекте,
            // и его собственный transform трогать нельзя — RawImage уедет с экрана.
            _rig = new GameObject("PreviewRig_" + profile.Id);
            _rig.transform.position = new Vector3(slot * SlotSpacing, StageDepth, 0f);

            var model = Instantiate(profile.VisualPrefab, _rig.transform, false);
            model.name = profile.VisualPrefab.name;
            model.transform.localPosition = Vector3.zero;

            // Модели экспортированы лицом по +Z, а камера смотрит с -Z:
            // без разворота карточка показывала бы спину.
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            // Карточка стоит вне основного вида: без этого Unity останавливает
            // анимацию Idle у моделей за краем камеры.
            var animator = model.GetComponentInChildren<Animator>();
            if (animator != null)
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var cameraObject = new GameObject("PreviewCamera");
            cameraObject.transform.SetParent(_rig.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, _lookHeight + 0.2f, -4.4f);
            cameraObject.transform.LookAt(_rig.transform.position + Vector3.up * _lookHeight);

            _camera = cameraObject.AddComponent<Camera>();
            _camera.fieldOfView = _fieldOfView;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.035f, 0.055f, 0.075f, 1f);
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 10f;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;

            _texture = new RenderTexture(_resolution.x, _resolution.y, 16, RenderTextureFormat.ARGB32)
            {
                name = "Preview_" + profile.Id,
                filterMode = FilterMode.Bilinear,
            };

            _camera.targetTexture = _texture;
            target.texture = _texture;
        }

        public void Clear()
        {
            if (_camera != null)
                _camera.targetTexture = null;

            if (_rig != null)
            {
                _rig.SetActive(false);
                Destroy(_rig);
                _rig = null;
            }

            _camera = null;

            if (_texture != null)
            {
                Destroy(_texture);
                _texture = null;
            }
        }

        private void OnDestroy()
        {
            Clear();
        }
    }
}

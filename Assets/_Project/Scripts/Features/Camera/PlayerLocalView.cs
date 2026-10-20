using UnityEngine;

namespace FrostSpace.Features.View
{
    /// <summary>
    /// Локальный вид игрока: то, что видит только владелец модели.
    ///
    /// В первом лице камера стоит в голове робота, и собственная голова закрывает
    /// обзор. Кость головы схлопывается в точку: скиннинг стягивает все её вершины,
    /// поэтому голова исчезает только у этого игрока — сама модель и её материалы
    /// не меняются, а чужие игроки видят робота целиком.
    ///
    /// Силуэт перекрытий тоже локальный (<see cref="PlayerSilhouette"/>): у удалённых
    /// моделей он выключается, чтобы в мультиплеере у других ничего не пропадало.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(320)]
    public sealed class PlayerLocalView : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Риг камеры: по его режиму скрывается голова")]
        [SerializeField] private PlayerCameraRig _rig;
        [Tooltip("Кость головы собственной модели")]
        [SerializeField] private Transform _headBone;
        [Tooltip("Силуэт перекрытий этого же игрока")]
        [SerializeField] private PlayerSilhouette _silhouette;

        [Header("Локальность")]
        [Tooltip("Модель принадлежит локальному игроку: только ему можно что-то скрывать")]
        [SerializeField] private bool _isLocalPlayer = true;

        [Tooltip("Во сколько раз сжимается кость головы, чтобы вершины ушли в точку")]
        [SerializeField] private float _headCollapse = 0.001f;

        private Vector3 _headScale = Vector3.one;
        private bool _saved;
        private bool _hidden;

        public bool IsLocalPlayer => _isLocalPlayer;

        /// <summary>
        /// Пометка владельца модели. Вызывается слоем сети для чужих игроков:
        /// у них ничего не скрывается и силуэт не включается.
        /// </summary>
        public void SetLocalPlayer(bool isLocal)
        {
            _isLocalPlayer = isLocal;
            if (!isLocal)
            {
                ShowHead();
                if (_silhouette != null)
                    _silhouette.SetLocalPlayer(false);
            }
        }

        private void Awake()
        {
            if (_rig == null)
                _rig = FindFirstObjectByType<PlayerCameraRig>();
            if (_silhouette == null)
                _silhouette = GetComponent<PlayerSilhouette>();
            if (_headBone == null)
                _headBone = FindHeadBone();

            if (!_isLocalPlayer)
            {
                if (_silhouette != null)
                    _silhouette.SetLocalPlayer(false);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (!_isLocalPlayer || _rig == null)
                return;

            if (_rig.Mode == CameraMode.FirstPerson)
                HideHead();
            else
                ShowHead();
        }

        private void HideHead()
        {
            if (_headBone == null || _hidden)
                return;

            if (!_saved)
            {
                _headScale = _headBone.localScale;
                _saved = true;
            }

            _headBone.localScale = _headScale * _headCollapse;
            _hidden = true;
            Debug.Log("[PlayerLocalView] голова скрыта: камера от первого лица", this);
        }

        private void ShowHead()
        {
            if (_headBone == null || !_hidden)
                return;

            _headBone.localScale = _saved ? _headScale : Vector3.one;
            _hidden = false;
            Debug.Log("[PlayerLocalView] голова показана", this);
        }

        /// <summary>Кость головы ищется по имени рига: путь длинный и зависит от FBX.</summary>
        private Transform FindHeadBone()
        {
            var transforms = GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name == "Head")
                    return transforms[i];
            }

            return null;
        }

        private void OnDisable()
        {
            if (_isLocalPlayer)
                ShowHead();
        }
    }
}

using System.Collections;
using FrostSpace.Core;
using FrostSpace.Features.Dialogue;
using FrostSpace.Features.Locomotion;
using FrostSpace.Features.Quest;
using FrostSpace.Features.View;
using UnityEngine;

namespace FrostSpace.Features.Story
{
    /// <summary>
    /// Финал вагона: после сдачи квеста камера уезжает к окну, параллакс
    /// замедляется, на белом появляется титул, затем грузится депо.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-20)]
    public sealed class TrainSequenceDirector : MonoBehaviour
    {
        [Header("Ссылки")]
        [SerializeField] private QuestDirector _quest;
        [SerializeField] private DialogueController _dialogue;
        [SerializeField] private RobotLocomotion _locomotion;
        [SerializeField] private PlayerCameraRig _rig;
        [SerializeField] private TrainMotionRig _motion;
        [SerializeField] private TitleCardScreen _titleCard;

        [Header("Финал")]
        [SerializeField] private string _nextScene = "Frontier";
        [Tooltip("Сколько камера едет к окну, с")]
        [SerializeField] private float _cameraSeconds = 2.4f;
        [Tooltip("Позиция камеры у правого окна")]
        [SerializeField] private Vector3 _windowPosition = new(3.2f, 2f, -4f);
        [Tooltip("Куда смотрит камера за окном")]
        [SerializeField] private Vector3 _windowLookAt = new(9f, 1.8f, -4f);

        private bool _running;

        private void OnEnable()
        {
            if (_quest != null)
                _quest.QuestCompleted += OnQuestCompleted;
        }

        private void OnDisable()
        {
            if (_quest != null)
                _quest.QuestCompleted -= OnQuestCompleted;
        }

        private void OnQuestCompleted(QuestLog log)
        {
            if (_running)
                return;

            StartCoroutine(PlayFinale());
        }

        private IEnumerator PlayFinale()
        {
            _running = true;

            // Финальный узел диалога ещё открыт в момент CompleteQuest.
            while (_dialogue != null && _dialogue.IsOpen)
                yield return null;

            _locomotion?.SetConversationLock(true);
            _motion?.Stop();
            _rig?.SetCinematic(true);

            var camera = _rig != null ? _rig.ViewCamera : Camera.main;
            if (camera == null)
            {
                Debug.LogError("TrainSequenceDirector: нет камеры для финала", this);
                _running = false;
                yield break;
            }

            var cameraTransform = camera.transform;
            var startPosition = cameraTransform.position;
            var startRotation = cameraTransform.rotation;
            var targetRotation = Quaternion.LookRotation(
                (_windowLookAt - _windowPosition).normalized, Vector3.up);
            var elapsed = 0f;

            while (elapsed < _cameraSeconds)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _cameraSeconds));
                cameraTransform.position = Vector3.Lerp(startPosition, _windowPosition, t);
                cameraTransform.rotation = Quaternion.Slerp(startRotation, targetRotation, t);
                yield return null;
            }

            if (_titleCard != null)
                _titleCard.Play();
            else
                Debug.LogWarning("TrainSequenceDirector: не назначен TitleCardScreen", this);

            var controller = SceneController.Instance;
            if (controller == null)
            {
                Debug.LogError("TrainSequenceDirector: нет SceneController", this);
                _running = false;
                yield break;
            }

            controller.LoadScene(_nextScene);
            _running = false;
        }
    }
}

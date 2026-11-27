using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Project.LinuxOS;

namespace Project.LarpCore
{
    /// <summary>
    /// Универсальный интерактивный объект в квартире (входная дверь, шкаф, энергетик на столе):
    /// клик ЛКМ (с авто-подходом персонажа) или клавиша E рядом.
    /// </summary>
    public class RoomPropInteractable : MonoBehaviour
    {
        public static readonly List<RoomPropInteractable> All = new List<RoomPropInteractable>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => All.Clear();

        public string PropId { get; private set; }
        public string PromptLabel { get; set; }
        public bool IsInteractionEnabled { get; set; } = true;
        public bool IsHovered { get; private set; }
        public bool IsPlayerNear { get; private set; }

        private float _interactDistance = 3.0f;
        private Vector3 _approachWorldPoint;
        private bool _useCustomApproach;
        private Action _onInteract;
        private Collider[] _colliders;
        private Move _playerMove;
        private Vector3 _centerPoint;

        public void Configure(string propId, string promptLabel, float interactDistance, Action onInteract, Vector3? customApproachPoint = null)
        {
            PropId = propId;
            PromptLabel = promptLabel;
            _interactDistance = interactDistance;
            _onInteract = onInteract;
            if (customApproachPoint.HasValue)
            {
                _approachWorldPoint = customApproachPoint.Value;
                _useCustomApproach = true;
            }
            CacheGeometry();
        }

        private void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        private void OnDisable()
        {
            All.Remove(this);
            IsHovered = false;
            IsPlayerNear = false;
        }

        private void Start()
        {
            _playerMove = FindAnyObjectByType<Move>();
            CacheGeometry();
        }

        private void CacheGeometry()
        {
            _colliders = GetComponentsInChildren<Collider>();
            if (_colliders.Length > 0)
            {
                Bounds b = _colliders[0].bounds;
                for (int i = 1; i < _colliders.Length; i++)
                    b.Encapsulate(_colliders[i].bounds);
                _centerPoint = b.center;
            }
            else
            {
                _centerPoint = transform.position;
            }
        }

        private void Update()
        {
            if (!IsInteractionEnabled ||
                (GameManager.Instance != null && (GameManager.Instance.IsModalOpen || GameManager.Instance.IsGameOver)) ||
                (RouterPlacementController.Instance != null && RouterPlacementController.Instance.IsPlacing) ||
                (PlayerStateController.Instance != null && (PlayerStateController.Instance.IsAtComputer || PlayerStateController.Instance.IsFlying)))
            {
                IsHovered = false;
                IsPlayerNear = false;
                return;
            }

            if (_playerMove == null)
                _playerMove = FindAnyObjectByType<Move>();

            if (_playerMove != null)
            {
                Vector3 p = _playerMove.transform.position;
                Vector2 delta = new Vector2(p.x - _centerPoint.x, p.z - _centerPoint.z);
                IsPlayerNear = delta.magnitude <= _interactDistance;
            }

            if (IsPlayerNear && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                TriggerInteract();
                return;
            }

            if (Mouse.current == null || Camera.main == null)
                return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                IsHovered = false;
                return;
            }

            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            bool hitSelf = false;
            if (Physics.Raycast(ray, out RaycastHit hit, 120f))
            {
                for (int i = 0; i < _colliders.Length; i++)
                {
                    if (_colliders[i] == hit.collider)
                    {
                        hitSelf = true;
                        break;
                    }
                }
            }

            IsHovered = hitSelf;

            if (hitSelf && Mouse.current.leftButton.wasPressedThisFrame)
            {
                ApproachAndInteract();
            }
        }

        public void ApproachAndInteract()
        {
            if (!IsInteractionEnabled) return;

            if (_playerMove != null && !IsPlayerNear)
            {
                Vector3 target = _useCustomApproach ? _approachWorldPoint : new Vector3(_centerPoint.x, 0f, _centerPoint.z);
                _playerMove.MoveToPoint(target, TriggerInteract, 2.0f);
            }
            else
            {
                TriggerInteract();
            }
        }

        public void TriggerInteract()
        {
            if (!IsInteractionEnabled) return;
            AudioFeedbackManager.Instance?.PlayClick();
            _onInteract?.Invoke();
        }
    }
}

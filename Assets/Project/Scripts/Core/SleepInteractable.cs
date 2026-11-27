using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Project.LinuxOS;

namespace Project.LarpCore
{
    /// <summary>
    /// Интерактивный объект отдыха (диван/кровать в квартире).
    /// Позволяет кликом ЛКМ или клавишей E рядом завершить день и открыть экран «Итоги дня».
    /// </summary>
    public class SleepInteractable : MonoBehaviour
    {
        [SerializeField] private float _interactDistance = 3.2f;
        [SerializeField] private Vector3 _approachOffset = new Vector3(0f, 0f, 1.4f);

        public static SleepInteractable ActiveInstance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ActiveInstance = null;

        public bool IsHovered { get; private set; }
        public bool IsPlayerNear { get; private set; }

        private Collider[] _colliders;
        private Move _playerMove;
        private Vector3 _centerPoint;

        private void Awake()
        {
            ActiveInstance = this;
            CacheGeometry();
        }

        private void Start()
        {
            _playerMove = FindAnyObjectByType<Move>();
        }

        private void OnDestroy()
        {
            if (ActiveInstance == this)
                ActiveInstance = null;
        }

        private void CacheGeometry()
        {
            _colliders = GetComponentsInChildren<Collider>();
            if (_colliders.Length > 0)
            {
                Bounds combined = _colliders[0].bounds;
                for (int i = 1; i < _colliders.Length; i++)
                    combined.Encapsulate(_colliders[i].bounds);
                _centerPoint = combined.center;
            }
            else
            {
                _centerPoint = transform.position;
            }
        }

        private void Update()
        {
            if (GameManager.Instance == null || GameManager.Instance.IsModalOpen || GameManager.Instance.IsGameOver ||
                (RouterPlacementController.Instance != null && RouterPlacementController.Instance.IsPlacing))
            {
                IsHovered = false;
                IsPlayerNear = false;
                return;
            }

            if (PlayerStateController.Instance != null &&
                (PlayerStateController.Instance.IsAtComputer || PlayerStateController.Instance.IsFlying))
            {
                IsHovered = false;
                IsPlayerNear = false;
                return;
            }

            if (_playerMove == null)
                _playerMove = FindAnyObjectByType<Move>();

            if (_playerMove != null)
            {
                Vector3 playerPos = _playerMove.transform.position;
                Vector2 flatDelta = new Vector2(playerPos.x - _centerPoint.x, playerPos.z - _centerPoint.z);
                IsPlayerNear = flatDelta.magnitude <= _interactDistance;
            }

            if (IsPlayerNear && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                TriggerSleep();
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
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
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
                if (_playerMove != null && !IsPlayerNear)
                {
                    Vector3 targetPos = new Vector3(_centerPoint.x + _approachOffset.x, 0f, _centerPoint.z + _approachOffset.z);
                    _playerMove.MoveToPoint(targetPos, TriggerSleep, 1.8f);
                    GameManager.Instance.PostToast("Идём к дивану, чтобы лечь спать...");
                }
                else
                {
                    TriggerSleep();
                }
            }
        }

        public void TriggerSleep()
        {
            if (GameManager.Instance == null || GameManager.Instance.IsModalOpen)
                return;

            GameManager.Instance.SleepAndEndDay();
        }
    }
}

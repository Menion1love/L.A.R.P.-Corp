using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Project.LarpCore;

namespace Project.LinuxOS
{
    [RequireComponent(typeof(Collider))]
    public class MonitorInteractable : MonoBehaviour
    {
        [Header("Подсветка при наведении (опционально)")]
        [SerializeField] private Renderer _monitorRenderer;
        [SerializeField] private Color _hoverTint = new Color(1.2f, 1.2f, 1.2f, 1f);

        [Header("Дистанция взаимодействия")]
        [SerializeField] private float _interactDistance = 3.2f;

        public static MonitorInteractable ActiveInstance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ActiveInstance = null;

        public bool IsHovered => _isHovered;
        public bool IsPlayerNear => _isPlayerNear;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Color _originalColor = Color.white;
        private int _colorPropertyId = -1;
        private Material _runtimeMaterial;
        private bool _isHovered;
        private bool _isPlayerNear;
        private Collider _collider;
        private Move _playerMove;

        private void Awake()
        {
            ActiveInstance = this;
            _collider = GetComponent<Collider>();
        }

        private void Start()
        {
            _playerMove = FindAnyObjectByType<Move>();

            if (_monitorRenderer != null && _monitorRenderer.sharedMaterial != null)
            {
                _runtimeMaterial = _monitorRenderer.material;
                if (_runtimeMaterial.HasProperty(BaseColorId))
                {
                    _colorPropertyId = BaseColorId;
                    _originalColor = _runtimeMaterial.GetColor(BaseColorId);
                }
                else if (_runtimeMaterial.HasProperty(ColorId))
                {
                    _colorPropertyId = ColorId;
                    _originalColor = _runtimeMaterial.GetColor(ColorId);
                }
            }
        }

        private void OnDestroy()
        {
            if (ActiveInstance == this)
                ActiveInstance = null;

            if (_runtimeMaterial != null)
                Destroy(_runtimeMaterial);
        }

        private void Update()
        {
            if (PlayerStateController.Instance == null ||
                PlayerStateController.Instance.IsAtComputer ||
                PlayerStateController.Instance.IsFlying ||
                (RouterPlacementController.Instance != null && RouterPlacementController.Instance.IsPlacing) ||
                (GameManager.Instance != null && (GameManager.Instance.IsModalOpen || GameManager.Instance.IsGameOver)))
            {
                SetHover(false);
                _isPlayerNear = false;
                return;
            }

            if (_playerMove == null)
                _playerMove = FindAnyObjectByType<Move>();

            if (_playerMove != null)
            {
                Vector3 pPos = _playerMove.transform.position;
                Vector3 mPos = transform.position;
                Vector2 flatDelta = new Vector2(pPos.x - mPos.x, pPos.z - mPos.z);
                _isPlayerNear = flatDelta.magnitude <= _interactDistance;
            }
            else
            {
                _isPlayerNear = true;
            }

            // Быстрый вход по клавише E рядом с компьютерным столом
            if (_isPlayerNear && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                SetHover(false);
                PlayerStateController.Instance.EnterComputer();
                return;
            }

            if (Mouse.current == null || Camera.main == null)
                return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                SetHover(false);
                return;
            }

            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(mousePos);

            bool hitThisMonitor = Physics.Raycast(ray, out RaycastHit hit, 100f) && hit.collider == _collider;
            SetHover(hitThisMonitor);

            if (hitThisMonitor && Mouse.current.leftButton.wasPressedThisFrame)
            {
                SetHover(false);
                InteractWithMonitor();
            }
        }

        public void InteractWithMonitor()
        {
            if (PlayerStateController.Instance == null || PlayerStateController.Instance.IsAtComputer)
                return;

            if (_playerMove != null && !_isPlayerNear)
            {
                // Подходим к свободному пространству перед компьютерным стулом и автоматически садимся
                Vector3 approachPoint = transform.position - transform.forward * 2.1f;
                approachPoint.y = 0f;
                _playerMove.MoveToPoint(approachPoint, () =>
                {
                    if (PlayerStateController.Instance != null)
                        PlayerStateController.Instance.EnterComputer();
                }, 2.2f);

                GameManager.Instance?.PostToast("Идём к рабочему компьютеру...");
            }
            else
            {
                PlayerStateController.Instance.EnterComputer();
            }
        }

        private void SetHover(bool hovered)
        {
            if (_isHovered == hovered) return;
            _isHovered = hovered;

            if (_runtimeMaterial != null && _colorPropertyId != -1)
            {
                _runtimeMaterial.SetColor(_colorPropertyId, _isHovered ? _originalColor * _hoverTint : _originalColor);
            }
        }
    }
}

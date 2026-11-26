using UnityEngine;
using UnityEngine.InputSystem;

namespace Project.LinuxOS
{
    [RequireComponent(typeof(Collider))]
    public class MonitorInteractable : MonoBehaviour
    {
        [Header("Подсветка при наведении (опционально)")]
        [SerializeField] private Renderer _monitorRenderer;
        [SerializeField] private Color _hoverTint = new Color(1.2f, 1.2f, 1.2f, 1f);

        private Color _originalColor;
        private bool _hasMaterial;
        private bool _isHovered;
        private Collider _collider;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
        }

        private void Start()
        {
            if (_monitorRenderer != null && _monitorRenderer.material != null)
            {
                _originalColor = _monitorRenderer.material.color;
                _hasMaterial = true;
            }
        }

        private void Update()
        {
            if (PlayerStateController.Instance == null || PlayerStateController.Instance.IsAtComputer)
            {
                SetHover(false);
                return;
            }

            if (Mouse.current == null || Camera.main == null)
                return;

            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(mousePos);

            bool hitThisMonitor = Physics.Raycast(ray, out RaycastHit hit) && hit.collider == _collider;
            SetHover(hitThisMonitor);

            if (hitThisMonitor && Mouse.current.leftButton.wasPressedThisFrame)
            {
                SetHover(false);
                PlayerStateController.Instance.EnterComputer();
            }
        }

        private void SetHover(bool hovered)
        {
            if (_isHovered == hovered) return;
            _isHovered = hovered;

            if (_hasMaterial)
                _monitorRenderer.material.color = _isHovered ? _originalColor * _hoverTint : _originalColor;
        }
    }
}
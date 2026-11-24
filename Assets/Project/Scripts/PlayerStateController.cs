using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem; 

namespace Project.LinuxOS
{
    public class PlayerStateController : MonoBehaviour
    {
        public static PlayerStateController Instance { get; private set; }

        [Header("Камера и две точки (сверху в комнате <-> у монитора)")]
        [SerializeField] private Camera _mainCamera;
        [SerializeField] private Transform _roomCameraPoint;
        [SerializeField] private Transform _monitorCameraPoint;
        [SerializeField] private float _flyDuration = 0.6f;

        [Header("Экран ОС (чтобы нельзя было кликнуть по кнопкам издалека)")]
        [SerializeField] private GraphicRaycaster _screenRaycaster;

        private bool _isAtComputer;
        private bool _isFlying;
        [SerializeField] private MonitorPowerController _monitorPower;
        public bool IsAtComputer => _isAtComputer;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (_monitorPower != null)
                _monitorPower.RefreshPowerButtonVisibility(false);
            _isAtComputer = false;
            if (_mainCamera != null && _roomCameraPoint != null)
            {
                _mainCamera.transform.SetPositionAndRotation(_roomCameraPoint.position, _roomCameraPoint.rotation);
            }

            if (_screenRaycaster != null)
                _screenRaycaster.enabled = false;
        }

        private void Update()
        {
            // Читаем нажатие Esc через новый Input System
            if (_isAtComputer && !_isFlying && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                ExitComputer();
            }
        }

        public void EnterComputer()
        {
            if (_isAtComputer || _isFlying || _mainCamera == null || _monitorCameraPoint == null)
                return;

            StartCoroutine(FlyCamera(_monitorCameraPoint, enteringPC: true));
        }

        public void ExitComputer()
        {
            if (_monitorPower != null)
                _monitorPower.RefreshPowerButtonVisibility(false);
            if (!_isAtComputer || _isFlying || _mainCamera == null || _roomCameraPoint == null)
                return;

            if (_screenRaycaster != null)
                _screenRaycaster.enabled = false;

            StartCoroutine(FlyCamera(_roomCameraPoint, enteringPC: false));
        }

        private IEnumerator FlyCamera(Transform target, bool enteringPC)
        {
            _isFlying = true;

            Vector3 startPos = _mainCamera.transform.position;
            Quaternion startRot = _mainCamera.transform.rotation;
            float elapsed = 0f;

            while (elapsed < _flyDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / _flyDuration);

                _mainCamera.transform.position = Vector3.Lerp(startPos, target.position, t);
                _mainCamera.transform.rotation = Quaternion.Slerp(startRot, target.rotation, t);
                yield return null;
            }

            _mainCamera.transform.SetPositionAndRotation(target.position, target.rotation);
            _isAtComputer = enteringPC;
            _isFlying = false;

            if (_screenRaycaster != null)
                _screenRaycaster.enabled = enteringPC;
                
            if (_monitorPower != null)
                _monitorPower.RefreshPowerButtonVisibility(enteringPC);
        }
    }
}
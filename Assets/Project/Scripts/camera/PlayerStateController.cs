using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Project.LarpCore;
using Project.UI;

namespace Project.LinuxOS
{
    public class PlayerStateController : MonoBehaviour
    {
        public static PlayerStateController Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        [Header("Камера и две точки (сверху в комнате <-> у монитора)")]
        [SerializeField] private Camera _mainCamera;
        [SerializeField] private Transform _roomCameraPoint;
        [SerializeField] private Transform _monitorCameraPoint;
        [SerializeField] private float _flyDuration = 0.6f;

        [Header("Экран ОС (чтобы нельзя было кликнуть по кнопкам издалека)")]
        [SerializeField] private GraphicRaycaster _screenRaycaster;

        [SerializeField] private MonitorPowerController _monitorPower;

        [Header("Авто-привязка монитора к столу в квартире")]
        [SerializeField] private bool _autoAlignMonitorToApartmentDesk = true;

        private bool _isAtComputer;
        private bool _isFlying;
        private CameraFollow _cameraFollow;
        private Move _playerMove;
        private Vector3 _savedRoomPos;
        private Quaternion _savedRoomRot;

        public bool IsAtComputer => _isAtComputer;
        public bool IsFlying => _isFlying;
        public MonitorPowerController MonitorPower => _monitorPower;
        public event Action<bool> OnComputerStateChanged;

        private void Awake()
        {
            Instance = this;

            // Гарантируем наличие ключевых систем геймлупа, сюжета и меню при запуске Dev_Scene
            PlayerState.EnsureInstance();
            GameManager.EnsureInstance();
            AudioFeedbackManager.EnsureInstance();
            RouterPlacementController.EnsureInstance();
            Day1StoryController.EnsureInstance();
            ApartmentHudController.EnsureInstance();
            MainMenuController.EnsureInstance();
        }

        private void Start()
        {
            if (_mainCamera == null)
                _mainCamera = Camera.main;

            if (_mainCamera != null)
                _cameraFollow = _mainCamera.GetComponent<CameraFollow>();

            _playerMove = FindAnyObjectByType<Move>();

            if (_autoAlignMonitorToApartmentDesk)
                TryAlignMonitorRootToApartmentDesk();

            TryAttachSleepInteractableToSofa();

            // Гарантируем правильную камеру у World Space Canvas монитора
            if (_screenRaycaster != null)
            {
                var canvas = _screenRaycaster.GetComponent<Canvas>();
                if (canvas != null && canvas.worldCamera == null)
                    canvas.worldCamera = _mainCamera;
                _screenRaycaster.enabled = false;
            }

            if (_monitorPower != null)
                _monitorPower.RefreshPowerButtonVisibility(false);

            _isAtComputer = false;

            // Если на камере есть CameraFollow (изометрическое слежение за персонажем в квартире),
            // НЕ телепортируем камеру в старую тестовую точку RoomCameraPoint вне квартиры!
            if (_cameraFollow != null)
            {
                _cameraFollow.SetFollowEnabled(true);
                _cameraFollow.SnapToTarget();
            }
            else if (_mainCamera != null && _roomCameraPoint != null)
            {
                _mainCamera.transform.SetPositionAndRotation(_roomCameraPoint.position, _roomCameraPoint.rotation);
            }
        }

        private void Update()
        {
            // Читаем нажатие Esc через новый Input System
            if (_isAtComputer && !_isFlying && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (GameManager.Instance == null || !GameManager.Instance.IsModalOpen)
                {
                    ExitComputer();
                }
            }
        }

        public void EnterComputer()
        {
            if (_isAtComputer || _isFlying || _mainCamera == null || _monitorCameraPoint == null)
                return;

            if (_playerMove != null)
                _playerMove.StopMovement();

            _savedRoomPos = _mainCamera.transform.position;
            _savedRoomRot = _cameraFollow != null ? _cameraFollow.RoomRotation : _mainCamera.transform.rotation;

            if (_cameraFollow != null)
                _cameraFollow.SetFollowEnabled(false);

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            AudioFeedbackManager.Instance?.PlayClick();
            StartCoroutine(FlyCamera(_monitorCameraPoint.position, _monitorCameraPoint.rotation, enteringPC: true));
        }

        public void ExitComputer()
        {
            if (_monitorPower != null)
                _monitorPower.RefreshPowerButtonVisibility(false);

            if (!_isAtComputer || _isFlying || _mainCamera == null)
                return;

            if (_screenRaycaster != null)
                _screenRaycaster.enabled = false;

            Vector3 returnPos;
            Quaternion returnRot;
            if (_cameraFollow != null)
            {
                returnPos = _cameraFollow.GetDesiredPosition();
                returnRot = _cameraFollow.RoomRotation;
            }
            else if (_roomCameraPoint != null)
            {
                returnPos = _roomCameraPoint.position;
                returnRot = _roomCameraPoint.rotation;
            }
            else
            {
                returnPos = _savedRoomPos;
                returnRot = _savedRoomRot;
            }

            AudioFeedbackManager.Instance?.PlayClick();
            StartCoroutine(FlyCamera(returnPos, returnRot, enteringPC: false));
        }

        private IEnumerator FlyCamera(Vector3 targetPos, Quaternion targetRot, bool enteringPC)
        {
            _isFlying = true;

            if (enteringPC && _playerMove != null)
                _playerMove.SetVisualsVisible(false);

            Vector3 startPos = _mainCamera.transform.position;
            Quaternion startRot = _mainCamera.transform.rotation;
            float elapsed = 0f;

            while (elapsed < _flyDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / _flyDuration);

                _mainCamera.transform.position = Vector3.Lerp(startPos, targetPos, t);
                _mainCamera.transform.rotation = Quaternion.Slerp(startRot, targetRot, t);
                yield return null;
            }

            _mainCamera.transform.SetPositionAndRotation(targetPos, targetRot);
            _isAtComputer = enteringPC;
            _isFlying = false;

            if (!enteringPC)
            {
                if (_playerMove != null)
                    _playerMove.SetVisualsVisible(true);

                if (_cameraFollow != null)
                    _cameraFollow.SetFollowEnabled(true);
            }

            if (_screenRaycaster != null)
                _screenRaycaster.enabled = enteringPC;

            if (_monitorPower != null)
                _monitorPower.RefreshPowerButtonVisibility(enteringPC);

            PlayerState.Instance?.SetAtComputer(enteringPC);
            OnComputerStateChanged?.Invoke(enteringPC);
        }

        private void TryAlignMonitorRootToApartmentDesk()
        {
            if (_monitorPower == null) return;
            Transform monitorRoot = _monitorPower.transform;

            // Если MonitorRoot уже был вручную переставлен пользователем на стол — не трогаем
            if (monitorRoot.position.sqrMagnitude > 2f)
                return;

            GameObject flatGo = GameObject.Find("Flat");
            if (flatGo == null) return;

            Transform deskScreen = flatGo.transform.Find("Комната2_10x6/Компьютерный_стол/Монитор/Экран");
            if (deskScreen == null) return;

            // Вычисляем масштаб и позицию так, чтобы MonitorFrame идеально сел вместо заглушки «Экран» на стойку стола
            const float deskScale = 0.82f;
            monitorRoot.localScale = Vector3.one * deskScale;

            Transform frame = monitorRoot.Find("MonitorFrame");
            Vector3 frameLocal = frame != null ? frame.localPosition : new Vector3(-0.1f, 1.414f, 0f);
            Vector3 desiredFrameWorld = deskScreen.position + new Vector3(0f, 0.03f, -0.02f);

            monitorRoot.position = desiredFrameWorld - frameLocal * deskScale;
            monitorRoot.rotation = Quaternion.identity;

            // Скрываем статичную заглушку «Экран», чтобы не было Z-fighting и перекрытия луча клика
            deskScreen.gameObject.SetActive(false);
        }

        private void TryAttachSleepInteractableToSofa()
        {
            if (SleepInteractable.ActiveInstance != null)
                return;

            GameObject flatGo = GameObject.Find("Flat");
            if (flatGo == null) return;

            Transform sofa = flatGo.transform.Find("Комната3_10x10/Диван_ЮЗ");
            if (sofa != null && sofa.GetComponent<SleepInteractable>() == null)
            {
                sofa.gameObject.AddComponent<SleepInteractable>();
            }
        }
    }
}

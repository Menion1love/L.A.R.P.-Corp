using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.InputSystem;
using Project.LarpCore;

namespace Project.LinuxOS
{
    public class MonitorPowerController : MonoBehaviour
    {
        public enum PowerState
        {
            Off,
            Booting,
            Login,
            LoggingIn,
            Desktop
        }

        [Header("UI Элементы экрана")]
        [SerializeField] private Image _screenDisplay;
        [SerializeField] private GameObject _powerButtonObject;
        [SerializeField] private Button _powerButton;

        [Header("Видео загрузки ОС (Шаг 1)")]
        [SerializeField] private RawImage _bootVideoScreen;
        [SerializeField] private VideoPlayer _videoPlayer;
        [SerializeField] private VideoClip _bootVideoClip;

        [Header("Экран входа и анимация логина (Шаг 2)")]
        [SerializeField] private GameObject _loginScreen;
        [SerializeField] private Button _loginButton;
        [SerializeField] private RawImage _loginSpinnerVideo;
        [SerializeField] private VideoClip _loginSpinnerClip;

        [Header("Цвета состояний")]
        [SerializeField] private Color _offColor = Color.black;
        [SerializeField] private Color _desktopColor = new Color(0.05f, 0.07f, 0.09f, 1f);

        [Header("Контейнер рабочего стола")]
        [SerializeField] private GameObject _desktopContent;

        private PowerState _currentState = PowerState.Off;
        private PowerState _pendingFallbackState = PowerState.Desktop;
        private float _videoTimeoutTimer = -1f;
        private LarpDesktopOS _desktopOS;

        public PowerState CurrentState => _currentState;
        public event Action<PowerState> OnPowerStateChanged;

        private void Awake()
        {
            if (_powerButton != null)
                _powerButton.onClick.AddListener(TurnOn);

            if (_loginButton != null)
                _loginButton.onClick.AddListener(StartLoginSequence);

            if (_videoPlayer != null)
            {
                _videoPlayer.loopPointReached += OnVideoFinished;
                _videoPlayer.errorReceived += OnVideoError;
            }
        }

        private void Start()
        {
            EnsureDesktopCreated();
            TurnOff();
        }

        private void Update()
        {
            // Страховочный таймер на случай зависания видеокодека + быстрый пропуск видео по Space
            if (_currentState == PowerState.Booting || _currentState == PowerState.LoggingIn)
            {
                if (_videoTimeoutTimer > 0f)
                {
                    _videoTimeoutTimer -= Time.unscaledDeltaTime;
                    if (_videoTimeoutTimer <= 0f)
                    {
                        AdvanceFromVideo();
                        return;
                    }
                }

                if (PlayerStateController.Instance != null &&
                    PlayerStateController.Instance.IsAtComputer &&
                    Keyboard.current != null &&
                    Keyboard.current.spaceKey.wasPressedThisFrame)
                {
                    AdvanceFromVideo();
                }
            }
        }

        private void OnDestroy()
        {
            if (_powerButton != null)
                _powerButton.onClick.RemoveListener(TurnOn);

            if (_loginButton != null)
                _loginButton.onClick.RemoveListener(StartLoginSequence);

            if (_videoPlayer != null)
            {
                _videoPlayer.loopPointReached -= OnVideoFinished;
                _videoPlayer.errorReceived -= OnVideoError;
            }
        }

        private void EnsureDesktopCreated()
        {
            if (_desktopContent == null)
            {
                Transform canvasParent = _screenDisplay != null ? _screenDisplay.transform.parent : null;
                if (canvasParent != null)
                {
                    var desktopGo = new GameObject("LarpDesktopOS", typeof(RectTransform));
                    desktopGo.transform.SetParent(canvasParent, false);
                    var rt = desktopGo.GetComponent<RectTransform>();
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;

                    _desktopContent = desktopGo;
                    _desktopOS = desktopGo.AddComponent<LarpDesktopOS>();
                }
            }
            else
            {
                _desktopOS = _desktopContent.GetComponent<LarpDesktopOS>();
                if (_desktopOS == null)
                    _desktopOS = _desktopContent.AddComponent<LarpDesktopOS>();
            }
        }

        public void RefreshPowerButtonVisibility(bool isAtComputer)
        {
            if (_powerButtonObject == null) return;

            bool shouldShow = (_currentState == PowerState.Off) && isAtComputer;
            _powerButtonObject.SetActive(shouldShow);
        }

        public void TurnOn()
        {
            if (_currentState != PowerState.Off)
                return;

            AudioFeedbackManager.Instance?.PlayClick();
            SetState(PowerState.Booting);
            PlayClip(_bootVideoClip, PowerState.Login);
        }

        public void StartLoginSequence()
        {
            if (_currentState != PowerState.Login)
                return;

            AudioFeedbackManager.Instance?.PlayClick();
            SetState(PowerState.LoggingIn);
            PlayClip(_loginSpinnerClip, PowerState.Desktop);
        }

        public void TurnOff()
        {
            if (_videoPlayer != null)
                _videoPlayer.Stop();

            _videoTimeoutTimer = -1f;
            SetState(PowerState.Off);
        }

        private void PlayClip(VideoClip clipToPlay, PowerState fallbackState)
        {
            _pendingFallbackState = fallbackState;

            if (_videoPlayer != null && clipToPlay != null)
            {
                _videoPlayer.gameObject.SetActive(true);
                _videoPlayer.enabled = true;
                _videoPlayer.Stop();

                if (_videoPlayer.targetTexture != null)
                {
                    RenderTexture active = RenderTexture.active;
                    RenderTexture.active = _videoPlayer.targetTexture;
                    GL.Clear(true, true, Color.clear);
                    RenderTexture.active = active;
                }

                _videoPlayer.clip = clipToPlay;
                _videoPlayer.Play();
                _videoTimeoutTimer = Mathf.Max(4f, (float)clipToPlay.length + 2.5f);
            }
            else
            {
                _videoTimeoutTimer = -1f;
                SetState(fallbackState);
            }
        }

        private void OnVideoFinished(VideoPlayer source)
        {
            AdvanceFromVideo();
        }

        private void OnVideoError(VideoPlayer source, string message)
        {
            Debug.LogWarning($"[MonitorPowerController] Video error: {message}. Advancing to {_pendingFallbackState}.");
            AdvanceFromVideo();
        }

        private void AdvanceFromVideo()
        {
            _videoTimeoutTimer = -1f;
            if (_videoPlayer != null && _videoPlayer.isPlaying)
                _videoPlayer.Stop();

            if (_currentState == PowerState.Booting)
            {
                SetState(PowerState.Login);
            }
            else if (_currentState == PowerState.LoggingIn)
            {
                SetState(PowerState.Desktop);
            }
        }

        private void SetState(PowerState newState)
        {
            _currentState = newState;
            EnsureDesktopCreated();

            switch (_currentState)
            {
                case PowerState.Off:
                    if (_screenDisplay != null) _screenDisplay.color = _offColor;
                    bool atPC = PlayerStateController.Instance != null && PlayerStateController.Instance.IsAtComputer;
                    if (_powerButtonObject != null) _powerButtonObject.SetActive(atPC);
                    if (_bootVideoScreen != null) _bootVideoScreen.gameObject.SetActive(false);
                    if (_loginScreen != null) _loginScreen.SetActive(false);
                    if (_desktopContent != null) _desktopContent.SetActive(false);
                    break;

                case PowerState.Booting:
                    if (_screenDisplay != null) _screenDisplay.color = _offColor;
                    if (_powerButtonObject != null) _powerButtonObject.SetActive(false);
                    if (_bootVideoScreen != null) _bootVideoScreen.gameObject.SetActive(true);
                    if (_loginScreen != null) _loginScreen.SetActive(false);
                    if (_desktopContent != null) _desktopContent.SetActive(false);
                    break;

                case PowerState.Login:
                    if (_powerButtonObject != null) _powerButtonObject.SetActive(false);
                    if (_bootVideoScreen != null) _bootVideoScreen.gameObject.SetActive(false);
                    if (_loginScreen != null) _loginScreen.SetActive(true);
                    if (_loginButton != null) _loginButton.gameObject.SetActive(true);
                    if (_loginSpinnerVideo != null) _loginSpinnerVideo.gameObject.SetActive(false);
                    if (_desktopContent != null) _desktopContent.SetActive(false);
                    break;

                case PowerState.LoggingIn:
                    if (_powerButtonObject != null) _powerButtonObject.SetActive(false);
                    if (_bootVideoScreen != null) _bootVideoScreen.gameObject.SetActive(false);
                    if (_loginScreen != null) _loginScreen.SetActive(true);
                    if (_loginButton != null) _loginButton.gameObject.SetActive(false);
                    if (_loginSpinnerVideo != null) _loginSpinnerVideo.gameObject.SetActive(true);
                    if (_desktopContent != null) _desktopContent.SetActive(false);
                    break;

                case PowerState.Desktop:
                    if (_screenDisplay != null) _screenDisplay.color = _desktopColor;
                    if (_powerButtonObject != null) _powerButtonObject.SetActive(false);
                    if (_bootVideoScreen != null) _bootVideoScreen.gameObject.SetActive(false);
                    if (_loginScreen != null) _loginScreen.SetActive(false);
                    if (_desktopContent != null)
                    {
                        _desktopContent.SetActive(true);
                        _desktopOS?.OnDesktopShown();
                    }
                    AudioFeedbackManager.Instance?.PlayNotification();
                    break;
            }

            PlayerState.Instance?.SetComputerPowerState(_currentState);
            OnPowerStateChanged?.Invoke(_currentState);
        }
    }
}

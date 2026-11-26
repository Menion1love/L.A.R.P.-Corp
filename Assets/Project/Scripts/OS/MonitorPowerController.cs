using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

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
        [SerializeField] private Color _desktopColor = new Color(0.95f, 0.95f, 0.95f, 1f);

        [Header("Контейнер рабочего стола")]
        [SerializeField] private GameObject _desktopContent;

        private PowerState _currentState = PowerState.Off;

        private void Awake()
        {
            if (_powerButton != null)
                _powerButton.onClick.AddListener(TurnOn);

            if (_loginButton != null)
                _loginButton.onClick.AddListener(StartLoginSequence);

            if (_videoPlayer != null)
                _videoPlayer.loopPointReached += OnVideoFinished;
        }

        private void Start()
        {
            TurnOff();
        }

        private void OnDestroy()
        {
            if (_powerButton != null)
                _powerButton.onClick.RemoveListener(TurnOn);

            if (_loginButton != null)
                _loginButton.onClick.RemoveListener(StartLoginSequence);

            if (_videoPlayer != null)
                _videoPlayer.loopPointReached -= OnVideoFinished;
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

            SetState(PowerState.Booting);
            PlayClip(_bootVideoClip, PowerState.Login);
        }

        public void StartLoginSequence()
        {
            if (_currentState != PowerState.Login)
                return;

            SetState(PowerState.LoggingIn);
            PlayClip(_loginSpinnerClip, PowerState.Desktop);
        }

        public void TurnOff()
        {
            if (_videoPlayer != null)
                _videoPlayer.Stop();

            SetState(PowerState.Off);
        }

        private void PlayClip(VideoClip clipToPlay, PowerState fallbackState)
        {
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
            }
            else
            {
                SetState(fallbackState);
            }
        }

        private void OnVideoFinished(VideoPlayer source)
        {
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
                    if (_desktopContent != null) _desktopContent.SetActive(true);
                    break;
            }
        }
    }
}
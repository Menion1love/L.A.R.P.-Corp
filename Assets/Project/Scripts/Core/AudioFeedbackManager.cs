using UnityEngine;

namespace Project.LarpCore
{
    /// <summary>
    /// Процедурный генератор звуковых откликов (Game Feel со слайда 18 ГДД):
    /// щелчки, открытие скрытых элементов, декодирование, таймер мини-игры, зуммер ошибки, завершение скачивания.
    /// Не требует внешних аудиофайлов.
    /// </summary>
    public class AudioFeedbackManager : MonoBehaviour
    {
        public static AudioFeedbackManager Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private AudioSource _source;
        private AudioClip _clickClip;
        private AudioClip _clueClip;
        private AudioClip _decodeClip;
        private AudioClip _tickClip;
        private AudioClip _errorClip;
        private AudioClip _successClip;
        private AudioClip _notifyClip;
        private AudioClip _doorbellClip;
        private AudioClip _refreshClip;

        public static AudioFeedbackManager EnsureInstance()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("AudioFeedbackManager");
            return go.AddComponent<AudioFeedbackManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.volume = 0.25f;

            _clickClip = GenerateTone(720f, 540f, 0.035f, 0.35f);
            _clueClip = GenerateChime(new[] { 587f, 880f }, 0.08f, 0.45f);
            _decodeClip = GenerateChime(new[] { 523f, 659f, 1046f }, 0.07f, 0.45f);
            _tickClip = GenerateTone(980f, 920f, 0.03f, 0.28f);
            _errorClip = GenerateBuzzer(190f, 130f, 0.22f, 0.55f);
            _successClip = GenerateChime(new[] { 440f, 554f, 659f, 880f }, 0.09f, 0.5f);
            _notifyClip = GenerateChime(new[] { 784f, 988f }, 0.08f, 0.38f);
            _doorbellClip = GenerateChime(new[] { 659f, 523f, 659f, 523f }, 0.22f, 0.55f);
            _refreshClip = GenerateChime(new[] { 392f, 523f, 659f, 784f }, 0.07f, 0.42f);
        }

        public void PlayClick() => Play(_clickClip, 0.22f);
        public void PlayClueFound() => Play(_clueClip, 0.32f);
        public void PlayDecodeSuccess() => Play(_decodeClip, 0.35f);
        public void PlayTimerTick() => Play(_tickClip, 0.20f);
        public void PlayErrorBuzzer() => Play(_errorClip, 0.40f);
        public void PlayDownloadComplete() => Play(_successClip, 0.38f);
        public void PlayNotification() => Play(_notifyClip, 0.30f);
        public void PlayDoorbell() => Play(_doorbellClip, 0.45f);
        public void PlayRefresh() => Play(_refreshClip, 0.36f);

        public void SetVolume(float volume01)
        {
            if (_source != null)
                _source.volume = Mathf.Clamp01(volume01) * 0.35f;
        }

        public void SetMuted(bool muted)
        {
            if (_source != null)
                _source.mute = muted;
        }

        private void Play(AudioClip clip, float volume)
        {
            if (_source != null && clip != null)
            {
                _source.PlayOneShot(clip, volume);
            }
        }

        private static AudioClip GenerateTone(float startFreq, float endFreq, float duration, float amplitude)
        {
            const int sampleRate = 22050;
            int samples = Mathf.Max(64, Mathf.RoundToInt(sampleRate * duration));
            float[] data = new float[samples];
            float phase = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                float freq = Mathf.Lerp(startFreq, endFreq, t);
                phase += 2f * Mathf.PI * freq / sampleRate;
                float env = Mathf.Sin(t * Mathf.PI);
                data[i] = Mathf.Sin(phase) * amplitude * env;
            }
            var clip = AudioClip.Create("Tone", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip GenerateBuzzer(float startFreq, float endFreq, float duration, float amplitude)
        {
            const int sampleRate = 22050;
            int samples = Mathf.Max(64, Mathf.RoundToInt(sampleRate * duration));
            float[] data = new float[samples];
            float phase = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                float freq = Mathf.Lerp(startFreq, endFreq, t);
                phase += 2f * Mathf.PI * freq / sampleRate;
                float env = 1f - t * 0.6f;
                float square = Mathf.Sign(Mathf.Sin(phase)) * 0.55f + Mathf.Sin(phase * 1.5f) * 0.45f;
                data[i] = square * amplitude * env;
            }
            var clip = AudioClip.Create("Buzzer", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip GenerateChime(float[] freqs, float noteDuration, float amplitude)
        {
            const int sampleRate = 22050;
            int perNote = Mathf.Max(64, Mathf.RoundToInt(sampleRate * noteDuration));
            int total = perNote * freqs.Length;
            float[] data = new float[total];
            for (int n = 0; n < freqs.Length; n++)
            {
                float phase = 0f;
                for (int i = 0; i < perNote; i++)
                {
                    float t = (float)i / perNote;
                    phase += 2f * Mathf.PI * freqs[n] / sampleRate;
                    float env = Mathf.Sin(t * Mathf.PI);
                    data[n * perNote + i] = Mathf.Sin(phase) * amplitude * env;
                }
            }
            var clip = AudioClip.Create("Chime", total, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

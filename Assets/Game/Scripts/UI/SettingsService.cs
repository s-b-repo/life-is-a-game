// Settings persistence.
//
// Reads and writes user preferences via PlayerPrefs so they survive
// across sessions. Split from SettingsPanel.cs so gameplay code
// (audio mixer, quality preset, sensitivity) can read settings without
// pulling in the UI layer.
//
// Applied at boot via [RuntimeInitializeOnLoadMethod] so PlayerPrefs
// values take effect even before the user opens the Settings panel.

using UnityEngine;
using UnityEngine.Audio;

namespace LifeIsAGame.UI
{
    public sealed class SettingsService : MonoBehaviour
    {
        // ---- keys — versioned so old saves can be migrated -------------
        private const string K_MASTER_VOL = "settings.audio.master.v1";
        private const string K_MUSIC_VOL  = "settings.audio.music.v1";
        private const string K_SFX_VOL    = "settings.audio.sfx.v1";
        private const string K_QUALITY    = "settings.graphics.quality.v1";
        private const string K_FULLSCREEN = "settings.graphics.fullscreen.v1";
        private const string K_VSYNC      = "settings.graphics.vsync.v1";
        private const string K_MOUSE_SEN  = "settings.gameplay.mouseSens.v1";
        private const string K_FOV        = "settings.gameplay.fov.v1";
        private const string K_INVERT_Y   = "settings.gameplay.invertY.v1";
        private const string K_DISPLAY    = "settings.player.displayName.v1";

        // ---- mixer parameter names (must match AudioMixer exposed params)
        private const string MIXER_MASTER = "MasterVolume";
        private const string MIXER_MUSIC  = "MusicVolume";
        private const string MIXER_SFX    = "SfxVolume";

        [SerializeField] private AudioMixer mixer;

        // Singleton for gameplay + UI access. Created via GameBootstrap-like
        // [RuntimeInitializeOnLoadMethod] below.
        public static SettingsService Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("~SettingsService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SettingsService>();
            Instance.ApplyAll();
        }

        // ---- typed accessors --------------------------------------------

        public float MasterVolume
        {
            get => PlayerPrefs.GetFloat(K_MASTER_VOL, 1f);
            set { PlayerPrefs.SetFloat(K_MASTER_VOL, Mathf.Clamp01(value)); ApplyAudio(); }
        }

        public float MusicVolume
        {
            get => PlayerPrefs.GetFloat(K_MUSIC_VOL, 1f);
            set { PlayerPrefs.SetFloat(K_MUSIC_VOL, Mathf.Clamp01(value)); ApplyAudio(); }
        }

        public float SfxVolume
        {
            get => PlayerPrefs.GetFloat(K_SFX_VOL, 1f);
            set { PlayerPrefs.SetFloat(K_SFX_VOL, Mathf.Clamp01(value)); ApplyAudio(); }
        }

        public int QualityLevel
        {
            get => PlayerPrefs.GetInt(K_QUALITY, QualitySettings.GetQualityLevel());
            set
            {
                int clamped = Mathf.Clamp(value, 0, QualitySettings.names.Length - 1);
                PlayerPrefs.SetInt(K_QUALITY, clamped);
                QualitySettings.SetQualityLevel(clamped, applyExpensiveChanges: true);
            }
        }

        public bool Fullscreen
        {
            get => PlayerPrefs.GetInt(K_FULLSCREEN, Screen.fullScreen ? 1 : 0) == 1;
            set
            {
                PlayerPrefs.SetInt(K_FULLSCREEN, value ? 1 : 0);
                Screen.fullScreenMode = value ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            }
        }

        public bool VSync
        {
            get => PlayerPrefs.GetInt(K_VSYNC, QualitySettings.vSyncCount) > 0;
            set
            {
                PlayerPrefs.SetInt(K_VSYNC, value ? 1 : 0);
                QualitySettings.vSyncCount = value ? 1 : 0;
            }
        }

        public float MouseSensitivity
        {
            get => PlayerPrefs.GetFloat(K_MOUSE_SEN, 50f);
            set => PlayerPrefs.SetFloat(K_MOUSE_SEN, Mathf.Clamp(value, 1f, 500f));
        }

        public float Fov
        {
            get => PlayerPrefs.GetFloat(K_FOV, 75f);
            set => PlayerPrefs.SetFloat(K_FOV, Mathf.Clamp(value, 30f, 130f));
        }

        public bool InvertY
        {
            get => PlayerPrefs.GetInt(K_INVERT_Y, 0) == 1;
            set => PlayerPrefs.SetInt(K_INVERT_Y, value ? 1 : 0);
        }

        public string DisplayName
        {
            get => PlayerPrefs.GetString(K_DISPLAY, string.Empty);
            set => PlayerPrefs.SetString(K_DISPLAY, (value ?? string.Empty).Trim());
        }

        // ---- apply state to the running player --------------------------

        public void ApplyAll()
        {
            ApplyAudio();
            QualitySettings.SetQualityLevel(QualityLevel, applyExpensiveChanges: false);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Screen.fullScreenMode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }

        public void ApplyAudio()
        {
            if (mixer == null) return;
            mixer.SetFloat(MIXER_MASTER, LinearToDb(MasterVolume));
            mixer.SetFloat(MIXER_MUSIC,  LinearToDb(MusicVolume));
            mixer.SetFloat(MIXER_SFX,    LinearToDb(SfxVolume));
        }

        public void Save() => PlayerPrefs.Save();

        public void ResetToDefaults()
        {
            PlayerPrefs.DeleteKey(K_MASTER_VOL);
            PlayerPrefs.DeleteKey(K_MUSIC_VOL);
            PlayerPrefs.DeleteKey(K_SFX_VOL);
            PlayerPrefs.DeleteKey(K_QUALITY);
            PlayerPrefs.DeleteKey(K_FULLSCREEN);
            PlayerPrefs.DeleteKey(K_VSYNC);
            PlayerPrefs.DeleteKey(K_MOUSE_SEN);
            PlayerPrefs.DeleteKey(K_FOV);
            PlayerPrefs.DeleteKey(K_INVERT_Y);
            ApplyAll();
        }

        private static float LinearToDb(float linear)
        {
            // AudioMixer expects dB. Convert 0..1 with a floor at ~-80 dB.
            return linear <= 0.0001f ? -80f : Mathf.Log10(linear) * 20f;
        }
    }
}

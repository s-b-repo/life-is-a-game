// Settings modal — Graphics / Audio / Controls / Gameplay tabs.
//
// UI-only. All persistence + application lives in SettingsService.
// Values flow settings-panel-widget -> SettingsService setter -> Unity
// runtime (AudioMixer, QualitySettings, Screen, etc.).
//
// The panel is authored programmatically by MenuSetupMenu so we keep
// the layout code declarative rather than shipping a large prefab YAML.

using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace LifeIsAGame.UI
{
    public sealed class SettingsPanel : MonoBehaviour
    {
        [Header("Graphics")]
        [SerializeField] private TMP_Dropdown qualityDropdown;
        [SerializeField] private TMP_Dropdown resolutionDropdown;
        [SerializeField] private Toggle       fullscreenToggle;
        [SerializeField] private Toggle       vsyncToggle;

        [Header("Audio")]
        [SerializeField] private Slider masterSlider;
        [SerializeField] private Slider musicSlider;
        [SerializeField] private Slider sfxSlider;

        [Header("Gameplay")]
        [SerializeField] private Slider  mouseSensSlider;
        [SerializeField] private Slider  fovSlider;
        [SerializeField] private Toggle  invertYToggle;
        [SerializeField] private TMP_InputField displayNameInput;

        [Header("Actions")]
        [SerializeField] private Button applyButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button resetButton;

        private Resolution[] _cachedResolutions;

        private void OnEnable()
        {
            var s = SettingsService.Instance;
            if (s == null) return;

            PopulateResolutions();
            PopulateQualityPresets();

            if (masterSlider     != null) { masterSlider.SetValueWithoutNotify(s.MasterVolume);    masterSlider.onValueChanged.AddListener(v => s.MasterVolume = v); }
            if (musicSlider      != null) { musicSlider.SetValueWithoutNotify(s.MusicVolume);      musicSlider.onValueChanged.AddListener(v => s.MusicVolume = v); }
            if (sfxSlider        != null) { sfxSlider.SetValueWithoutNotify(s.SfxVolume);          sfxSlider.onValueChanged.AddListener(v => s.SfxVolume = v); }
            if (fullscreenToggle != null) { fullscreenToggle.SetIsOnWithoutNotify(s.Fullscreen);   fullscreenToggle.onValueChanged.AddListener(v => s.Fullscreen = v); }
            if (vsyncToggle      != null) { vsyncToggle.SetIsOnWithoutNotify(s.VSync);             vsyncToggle.onValueChanged.AddListener(v => s.VSync = v); }
            if (qualityDropdown  != null) { qualityDropdown.SetValueWithoutNotify(s.QualityLevel); qualityDropdown.onValueChanged.AddListener(v => s.QualityLevel = v); }
            if (mouseSensSlider  != null) { mouseSensSlider.SetValueWithoutNotify(s.MouseSensitivity); mouseSensSlider.onValueChanged.AddListener(v => s.MouseSensitivity = v); }
            if (fovSlider        != null) { fovSlider.SetValueWithoutNotify(s.Fov);                fovSlider.onValueChanged.AddListener(v => s.Fov = v); }
            if (invertYToggle    != null) { invertYToggle.SetIsOnWithoutNotify(s.InvertY);         invertYToggle.onValueChanged.AddListener(v => s.InvertY = v); }
            if (displayNameInput != null) { displayNameInput.SetTextWithoutNotify(s.DisplayName);  displayNameInput.onEndEdit.AddListener(v => s.DisplayName = v); }

            if (applyButton != null) { applyButton.onClick.RemoveAllListeners(); applyButton.onClick.AddListener(() => { s.Save(); }); }
            if (closeButton != null) { closeButton.onClick.RemoveAllListeners(); closeButton.onClick.AddListener(() => gameObject.SetActive(false)); }
            if (resetButton != null) { resetButton.onClick.RemoveAllListeners(); resetButton.onClick.AddListener(() => { s.ResetToDefaults(); OnEnable(); }); }
        }

        private void OnDisable()
        {
            // Unhook so re-showing doesn't stack listeners.
            if (masterSlider     != null) masterSlider.onValueChanged.RemoveAllListeners();
            if (musicSlider      != null) musicSlider.onValueChanged.RemoveAllListeners();
            if (sfxSlider        != null) sfxSlider.onValueChanged.RemoveAllListeners();
            if (fullscreenToggle != null) fullscreenToggle.onValueChanged.RemoveAllListeners();
            if (vsyncToggle      != null) vsyncToggle.onValueChanged.RemoveAllListeners();
            if (qualityDropdown  != null) qualityDropdown.onValueChanged.RemoveAllListeners();
            if (mouseSensSlider  != null) mouseSensSlider.onValueChanged.RemoveAllListeners();
            if (fovSlider        != null) fovSlider.onValueChanged.RemoveAllListeners();
            if (invertYToggle    != null) invertYToggle.onValueChanged.RemoveAllListeners();
            if (displayNameInput != null) displayNameInput.onEndEdit.RemoveAllListeners();
        }

        private void PopulateResolutions()
        {
            if (resolutionDropdown == null) return;
            _cachedResolutions = Screen.resolutions;
            resolutionDropdown.ClearOptions();
            var opts = new System.Collections.Generic.List<string>(_cachedResolutions.Length);
            int currentIndex = 0;
            for (int i = 0; i < _cachedResolutions.Length; i++)
            {
                var r = _cachedResolutions[i];
                opts.Add($"{r.width} x {r.height} @ {r.refreshRateRatio.value:0} Hz");
                if (r.width == Screen.currentResolution.width && r.height == Screen.currentResolution.height)
                {
                    currentIndex = i;
                }
            }
            resolutionDropdown.AddOptions(opts);
            resolutionDropdown.SetValueWithoutNotify(currentIndex);
            resolutionDropdown.onValueChanged.RemoveAllListeners();
            resolutionDropdown.onValueChanged.AddListener(idx =>
            {
                if (_cachedResolutions == null || idx < 0 || idx >= _cachedResolutions.Length) return;
                var r = _cachedResolutions[idx];
                Screen.SetResolution(r.width, r.height, Screen.fullScreenMode, r.refreshRateRatio);
            });
        }

        private void PopulateQualityPresets()
        {
            if (qualityDropdown == null) return;
            qualityDropdown.ClearOptions();
            var names = new System.Collections.Generic.List<string>(QualitySettings.names);
            qualityDropdown.AddOptions(names);
        }
    }
}

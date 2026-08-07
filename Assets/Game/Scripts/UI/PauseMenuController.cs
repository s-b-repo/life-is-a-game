// In-gameplay pause menu.
//
// Escape while playing pops this overlay. Resume unpauses, Settings
// re-uses the same SettingsPanel prefab as the main menu, Leave calls
// StopHost/StopClient and returns to MainMenu via SceneFlow.
//
// Time.timeScale is pinned to 0 while paused so gameplay physics + AI
// freeze. UI works on unscaled time via Time.unscaledDeltaTime in
// LateUpdate — most Unity UI does this by default.

using Mirror;
using UnityEngine;
using UnityEngine.UI;
using LifeIsAGame.Net;

namespace LifeIsAGame.UI
{
    public sealed class PauseMenuController : MonoBehaviour
    {
        [SerializeField] private GameObject panel;           // the visible pause overlay
        [SerializeField] private GameObject settingsPanel;   // shared with the main menu; toggled on Settings click
        [SerializeField] private Button     resumeButton;
        [SerializeField] private Button     settingsButton;
        [SerializeField] private Button     leaveButton;
        [SerializeField] private Button     quitButton;

        private bool _paused;

        private void Start()
        {
            if (panel != null) panel.SetActive(false);
            if (resumeButton   != null) resumeButton.onClick.AddListener(Resume);
            if (settingsButton != null) settingsButton.onClick.AddListener(() => { if (settingsPanel != null) settingsPanel.SetActive(true); });
            if (leaveButton    != null) leaveButton.onClick.AddListener(LeaveToLobby);
            if (quitButton     != null) quitButton.onClick.AddListener(Quit);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (settingsPanel != null && settingsPanel.activeSelf)
                {
                    settingsPanel.SetActive(false);
                    return;
                }
                Toggle();
            }
        }

        public void Toggle() { if (_paused) Resume(); else Pause(); }

        public void Pause()
        {
            _paused = true;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            if (panel != null) panel.SetActive(true);
        }

        public void Resume()
        {
            _paused = false;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
            if (panel != null) panel.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(false);
        }

        public void LeaveToLobby()
        {
            Time.timeScale = 1f;
            var nm = GameNetworkManager.singleton;
            if (nm != null)
            {
                if (NetworkServer.active && NetworkClient.isConnected) nm.StopHost();
                else if (NetworkClient.isConnected)                    nm.StopClient();
                else if (NetworkServer.active)                         nm.StopServer();
            }
            SceneFlow.Instance.LoadScene(SceneFlow.MainMenu);
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}

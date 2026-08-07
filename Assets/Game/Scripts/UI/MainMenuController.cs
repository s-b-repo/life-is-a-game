// Main menu logic.
//
// Wired to buttons in MainMenu.unity by MenuSetupMenu.cs. Handles the
// top-level actions (Play/Host/Join/Settings/Quit) and delegates scene
// transitions to SceneFlow, network start to GameNetworkManager, and
// preferences to SettingsService.

using Mirror;
using UnityEngine;
using LifeIsAGame.Net;

namespace LifeIsAGame.UI
{
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] private GameObject settingsPanel;   // instantiated from SettingsPanel prefab into this scene
        [SerializeField] private GameObject joinDialog;      // simple ip-input dialog

        // Buttons wire these methods via UnityEvents in the authored scene.

        public void OnPlayClicked()
        {
            // "Play" = host a solo local game — start server, connect as
            // client, load gameplay immediately.
            var nm = GameNetworkManager.singleton;
            if (nm == null) return;
            nm.StartHost();
            SceneFlow.Instance.LoadScene(SceneFlow.Gameplay);
        }

        public void OnHostClicked()
        {
            var nm = GameNetworkManager.singleton;
            if (nm == null) return;
            nm.StartHost();
            SceneFlow.Instance.LoadScene(SceneFlow.Lobby);
        }

        public void OnJoinClicked()
        {
            if (joinDialog != null) joinDialog.SetActive(true);
        }

        /// <summary>Called by the JoinDialog's confirm button (input field bound to ip).</summary>
        public void OnJoinConfirm(string address)
        {
            var nm = GameNetworkManager.singleton;
            if (nm == null || string.IsNullOrWhiteSpace(address)) return;
            nm.networkAddress = address.Trim();
            nm.StartClient();
            // Lobby scene loads on OnClientConnect via SceneFlow; keep it
            // simple and load unconditionally here.
            SceneFlow.Instance.LoadScene(SceneFlow.Lobby);
        }

        public void OnSettingsClicked()
        {
            if (settingsPanel != null) settingsPanel.SetActive(true);
        }

        public void OnQuitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Update()
        {
            // Escape closes the join dialog / settings if open; from the
            // pristine main menu with nothing open it does nothing (users
            // press Quit).
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (settingsPanel != null && settingsPanel.activeSelf) { settingsPanel.SetActive(false); return; }
                if (joinDialog   != null && joinDialog.activeSelf)    { joinDialog.SetActive(false);    return; }
            }
        }
    }
}

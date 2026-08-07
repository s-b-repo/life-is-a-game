// Lobby scene controller.
//
// Consolidates what the plan called RoomListPanel + CreateRoomDialog +
// InRoomPanel + PlayerSlot + ChatBox into one MonoBehaviour that owns
// the entire lobby scene UI. Compact because Lobby has only two
// mutually-exclusive states — "browsing rooms" and "inside a room".
//
// Room browsing is powered by GameNetworkDiscovery. Once the local peer
// hosts or joins, the discovery listener stops and the in-room panel
// takes over: player slots read from GameNetworkManager.Players, the
// host sees Kick + Start Game buttons.

using System.Collections.Generic;
using System.Net;
using Mirror;
using Mirror.Discovery;
using UnityEngine;
using UnityEngine.UI;
using LifeIsAGame.Net;
using TMPro;

namespace LifeIsAGame.UI
{
    public sealed class LobbyController : MonoBehaviour
    {
        [Header("Panels (assigned by MenuSetupMenu authoring)")]
        [SerializeField] private GameObject roomListPanel;
        [SerializeField] private GameObject createRoomDialog;
        [SerializeField] private GameObject inRoomPanel;

        [Header("Room list")]
        [SerializeField] private Transform roomListContent;   // ScrollView content
        [SerializeField] private GameObject roomEntryPrefab;  // instantiated per discovered room
        [SerializeField] private Button refreshButton;
        [SerializeField] private Button createRoomButton;

        [Header("Create-room dialog")]
        [SerializeField] private TMP_InputField createRoomNameInput;
        [SerializeField] private TMP_InputField createRoomPasswordInput;
        [SerializeField] private TMP_InputField createRoomMaxInput;
        [SerializeField] private Button        createRoomConfirmButton;

        [Header("In-room")]
        [SerializeField] private Transform playerSlotContent;
        [SerializeField] private GameObject playerSlotPrefab;
        [SerializeField] private Button    startGameButton;   // host-only
        [SerializeField] private Button    leaveButton;
        [SerializeField] private TMP_Text  roomTitle;
        [SerializeField] private TMP_InputField chatInput;
        [SerializeField] private TMP_Text       chatLog;

        private GameNetworkDiscovery _discovery;
        private readonly Dictionary<long, ServerResponse> _discovered = new Dictionary<long, ServerResponse>();

        // -----------------------------------------------------------------
        // Lifecycle
        // -----------------------------------------------------------------

        private void Start()
        {
            _discovery = FindObjectOfType<GameNetworkDiscovery>();
            if (_discovery != null)
            {
                _discovery.OnServerFound.AddListener(OnDiscoveredServer);
            }

            if (refreshButton      != null) refreshButton.onClick.AddListener(RefreshRoomList);
            if (createRoomButton   != null) createRoomButton.onClick.AddListener(() => createRoomDialog.SetActive(true));
            if (createRoomConfirmButton != null) createRoomConfirmButton.onClick.AddListener(HostRoomFromDialog);
            if (leaveButton        != null) leaveButton.onClick.AddListener(LeaveRoom);
            if (startGameButton    != null) startGameButton.onClick.AddListener(StartGame);
            if (chatInput          != null) chatInput.onSubmit.AddListener(SendChat);

            EnterBrowsingState();
        }

        private void OnDestroy()
        {
            if (_discovery != null) _discovery.OnServerFound.RemoveListener(OnDiscoveredServer);
        }

        private void Update()
        {
            // Refresh player slots + start-button state each frame. Cheap;
            // one iteration over the connected-player dictionary.
            if (inRoomPanel != null && inRoomPanel.activeSelf) RefreshPlayerSlots();
        }

        // -----------------------------------------------------------------
        // States
        // -----------------------------------------------------------------

        private void EnterBrowsingState()
        {
            if (roomListPanel != null)  roomListPanel.SetActive(true);
            if (inRoomPanel != null)    inRoomPanel.SetActive(false);
            if (createRoomDialog != null) createRoomDialog.SetActive(false);

            _discovered.Clear();
            ClearContent(roomListContent);
            if (_discovery != null) _discovery.StartDiscovery();
        }

        private void EnterInRoomState()
        {
            if (roomListPanel != null)  roomListPanel.SetActive(false);
            if (inRoomPanel != null)    inRoomPanel.SetActive(true);
            if (createRoomDialog != null) createRoomDialog.SetActive(false);
            if (_discovery != null) _discovery.StopDiscovery();

            var nm = GameNetworkManager.singleton;
            if (roomTitle != null)
                roomTitle.text = _discovery != null
                    ? $"Room: {_discovery.CurrentRoomInfo.RoomName}"
                    : "Room";
            if (startGameButton != null)
                startGameButton.gameObject.SetActive(NetworkServer.active);
        }

        // -----------------------------------------------------------------
        // Room list
        // -----------------------------------------------------------------

        private void RefreshRoomList()
        {
            _discovered.Clear();
            ClearContent(roomListContent);
            if (_discovery != null)
            {
                _discovery.StopDiscovery();
                _discovery.StartDiscovery();
            }
        }

        private void OnDiscoveredServer(ServerResponse response)
        {
            if (_discovered.ContainsKey(response.serverId)) return;
            _discovered[response.serverId] = response;

            if (roomEntryPrefab == null || roomListContent == null) return;

            GameObject entry = Instantiate(roomEntryPrefab, roomListContent);
            var label = entry.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = $"{response.uri?.Host ?? "unknown"}:{response.uri?.Port}";
            var btn = entry.GetComponent<Button>();
            if (btn != null) btn.onClick.AddListener(() => JoinRoom(response));
        }

        private void JoinRoom(ServerResponse response)
        {
            var nm = GameNetworkManager.singleton;
            if (nm == null || response.uri == null) return;
            nm.StartClient(response.uri);
            EnterInRoomState();
        }

        // -----------------------------------------------------------------
        // Create room
        // -----------------------------------------------------------------

        private void HostRoomFromDialog()
        {
            byte cap = 8;
            if (createRoomMaxInput != null && byte.TryParse(createRoomMaxInput.text, out byte parsed))
            {
                cap = parsed;
            }

            string name = createRoomNameInput != null ? createRoomNameInput.text : "Untitled Room";
            string pw   = createRoomPasswordInput != null ? createRoomPasswordInput.text : string.Empty;

            if (_discovery != null) _discovery.ConfigureRoom(name, cap, !string.IsNullOrEmpty(pw));

            var nm = GameNetworkManager.singleton;
            if (nm == null) return;
            nm.StartHost();
            if (_discovery != null) _discovery.AdvertiseServer();

            EnterInRoomState();
        }

        // -----------------------------------------------------------------
        // In-room
        // -----------------------------------------------------------------

        private void RefreshPlayerSlots()
        {
            var nm = GameNetworkManager.singleton;
            if (nm == null || playerSlotContent == null || playerSlotPrefab == null) return;

            // Simple rebuild — the slot count is tiny (<= 16).
            ClearContent(playerSlotContent);
            foreach (var kv in nm.Players)
            {
                GameObject slot = Instantiate(playerSlotPrefab, playerSlotContent);
                var label = slot.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    string dn = string.IsNullOrEmpty(kv.Value.DisplayName)
                        ? $"Player {kv.Value.PlayerIdSlot}"
                        : kv.Value.DisplayName;
                    label.text = dn;
                }
            }
        }

        private void StartGame()
        {
            var nm = GameNetworkManager.singleton;
            if (nm == null) return;
            nm.StartGameplay();
        }

        private void LeaveRoom()
        {
            var nm = GameNetworkManager.singleton;
            if (nm == null) return;
            if (NetworkServer.active && NetworkClient.isConnected) nm.StopHost();
            else if (NetworkClient.isConnected)                    nm.StopClient();
            else if (NetworkServer.active)                         nm.StopServer();

            SceneFlow.Instance.LoadScene(SceneFlow.MainMenu);
        }

        private void SendChat(string message)
        {
            if (string.IsNullOrWhiteSpace(message) || chatInput == null) return;
            // Phase 3: chat is optional and Mirror doesn't ship a built-in
            // chat. Append locally for now; a full networked chat message
            // is Phase 5 alongside signed inputs.
            if (chatLog != null) chatLog.text += $"\n<me> {message}";
            chatInput.text = string.Empty;
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private static void ClearContent(Transform container)
        {
            if (container == null) return;
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                Destroy(container.GetChild(i).gameObject);
            }
        }
    }
}

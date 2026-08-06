// Mirror NetworkManager subclass for Life-is-a-Game.
//
// Responsibilities:
//   * Own the singleton lifecycle (DontDestroyOnLoad).
//   * Track connected players so the Lobby UI (Phase 3) can render slots.
//   * Assign spawn points to new players via RandomSpawn components already
//     placed in the gameplay scene.
//   * Broadcast a lobby seed to all peers at StartGame so DetRandom
//     (Phase 4) can seed the shared RNG.
//   * Route scene transitions: MainMenu -> Lobby -> gameplay.
//
// The class stays small; game-specific logic lives on NetworkPlayer and
// (in Phase 5) LockstepEngine. The lockstep engine will hook OnStartServer
// / OnServerConnect to gate InputCommand acceptance.

using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LifeIsAGame.Net
{
    public sealed class GameNetworkManager : NetworkManager
    {
        [Header("Life-is-a-Game")]
        [Tooltip("Scene loaded after everyone in the lobby readies up and " +
                 "the host presses Start. Set to the gameplay scene asset " +
                 "in the Inspector (BuildSettings index used at runtime).")]
        [Scene]
        [SerializeField] private string gameplayScene;

        [Tooltip("Menu scene we bounce back to on disconnect.")]
        [Scene]
        [SerializeField] private string mainMenuScene;

        /// <summary>Everyone currently connected. Key = connection id.</summary>
        public IReadOnlyDictionary<int, NetworkPlayer> Players => _players;
        private readonly Dictionary<int, NetworkPlayer> _players = new Dictionary<int, NetworkPlayer>();

        /// <summary>Public accessor for other systems (Lobby UI, LockstepEngine) that need the running instance.</summary>
        public new static GameNetworkManager singleton => (GameNetworkManager)NetworkManager.singleton;

        // -----------------------------------------------------------------
        // Server callbacks
        // -----------------------------------------------------------------

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            // Spawn at a random registered spawn point if any exist,
            // otherwise at the NetworkManager's start position.
            Transform spawn = GetStartPosition();
            Vector3    pos  = spawn != null ? spawn.position : Vector3.zero;
            Quaternion rot  = spawn != null ? spawn.rotation : Quaternion.identity;

            GameObject go = Instantiate(playerPrefab, pos, rot);
            go.name = $"Player [conn={conn.connectionId}]";
            NetworkServer.AddPlayerForConnection(conn, go);

            if (go.TryGetComponent(out NetworkPlayer np))
            {
                np.PlayerIdSlot = (byte)conn.connectionId;
                _players[conn.connectionId] = np;
            }
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            _players.Remove(conn.connectionId);
            // NetworkManager.OnServerDisconnect handles the actual player destroy.
            base.OnServerDisconnect(conn);
        }

        // -----------------------------------------------------------------
        // Client callbacks — bounce back to the menu cleanly on disconnect.
        // -----------------------------------------------------------------

        public override void OnClientDisconnect()
        {
            base.OnClientDisconnect();
            if (!string.IsNullOrEmpty(mainMenuScene) &&
                SceneManager.GetActiveScene().name != mainMenuScene)
            {
                SceneManager.LoadScene(mainMenuScene);
            }
        }

        // -----------------------------------------------------------------
        // Public host API used by the Lobby "Start" button.
        // -----------------------------------------------------------------

        /// <summary>Host-only: kick off the gameplay scene load for all connected clients.</summary>
        public void StartGameplay()
        {
            if (!NetworkServer.active)
            {
                Debug.LogWarning($"{nameof(StartGameplay)} called on a non-host; ignoring.");
                return;
            }
            if (string.IsNullOrEmpty(gameplayScene))
            {
                Debug.LogError($"{nameof(GameNetworkManager)}: gameplayScene not assigned.");
                return;
            }
            ServerChangeScene(gameplayScene);
        }
    }
}

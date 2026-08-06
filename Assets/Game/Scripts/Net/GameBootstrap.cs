// Runtime NetworkManager bootstrap.
//
// Mirror requires a single NetworkManager instance present in the scene.
// Rather than ship a hand-crafted prefab (fragile YAML, sensitive to
// Mirror version upgrades since the KcpTransport script GUID depends on
// which UPM package resolved), we synthesize the NetworkManager
// GameObject programmatically on first scene load and mark it
// DontDestroyOnLoad.
//
// The bootstrap runs BEFORE any scene's Awake calls thanks to
// [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]. If the user later
// wants Inspector-configurable networking they can drop a NetworkManager
// prefab of their own into a scene — this bootstrap detects it and
// skips, so custom setups still work.
//
// The player prefab, on the other hand, MUST be a real Unity asset so
// Mirror gets a stable assetId that matches host + client on spawn.
// Create it once via `Life-is-a-Game/Setup/Create NetworkPlayer Prefab`
// (see NetworkSetupMenu.cs in the Editor assembly). Once created it
// lives at Assets/Resources/NetworkPlayerPrefab.prefab and this
// bootstrap loads it via Resources.Load.

using Mirror;
using UnityEngine;
using kcp2k;

namespace LifeIsAGame.Net
{
    public static class GameBootstrap
    {
        public const string PlayerPrefabResourcePath = "NetworkPlayerPrefab";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            // Bail if someone already provided a NetworkManager in a scene.
            if (Object.FindObjectOfType<NetworkManager>(includeInactive: true) != null) return;

            // ---- NetworkManager GameObject --------------------------------
            var go = new GameObject("~GameNetworkManager");
            Object.DontDestroyOnLoad(go);

            KcpTransport kcp = go.AddComponent<KcpTransport>();
            kcp.Port         = 7777;

            GameNetworkManager nm = go.AddComponent<GameNetworkManager>();
            nm.transport            = kcp;
            Transport.active        = kcp;
            nm.networkAddress       = "localhost";
            nm.dontDestroyOnLoad    = true;
            nm.autoStartServerBuild = false;

            // ---- Player prefab from Resources -----------------------------
            GameObject playerPrefab = Resources.Load<GameObject>(PlayerPrefabResourcePath);
            if (playerPrefab != null)
            {
                nm.playerPrefab = playerPrefab;
            }
            else
            {
                Debug.LogWarning(
                    $"[{nameof(GameBootstrap)}] Resources/{PlayerPrefabResourcePath}.prefab not found. " +
                    "Run 'Life-is-a-Game > Setup > Create NetworkPlayer Prefab' from the Unity menu bar " +
                    "to create it. Networking will start but nothing will spawn.");
            }

            // ---- LAN discovery ----------------------------------------
            var discovery = go.AddComponent<GameNetworkDiscovery>();
            discovery.transport = kcp;

            Debug.Log($"[{nameof(GameBootstrap)}] NetworkManager + KcpTransport + Discovery ready.");
        }

        /// <summary>Convenience for other systems to grab the singleton or spawn it if missing.</summary>
        public static GameNetworkManager EnsureManager()
        {
            Init();
            return GameNetworkManager.singleton;
        }
    }
}

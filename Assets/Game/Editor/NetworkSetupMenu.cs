// One-click Editor helper that authors the NetworkPlayer prefab.
//
// Adding a MonoBehaviour like NetworkIdentity to a prefab at runtime is
// fine, but Mirror needs the prefab to be a real Unity asset with a
// stable assetId so host and client agree on what to spawn. The tidy
// way to guarantee that is to build the prefab inside the Editor and
// save it under Resources/, then have GameBootstrap load it via
// Resources.Load at runtime.
//
// Ships as a menu item under `Life-is-a-Game > Setup > Create
// NetworkPlayer Prefab`. Idempotent — safe to run repeatedly; existing
// prefab is overwritten.

#if UNITY_EDITOR
using System.IO;
using Mirror;
using UnityEditor;
using UnityEngine;
using LifeIsAGame.Net;
using LifeIsAGame.Player;

namespace LifeIsAGame.EditorTools
{
    public static class NetworkSetupMenu
    {
        private const string TargetDir  = "Assets/Resources";
        private const string TargetPath = "Assets/Resources/NetworkPlayerPrefab.prefab";

        [MenuItem("Life-is-a-Game/Setup/Create NetworkPlayer Prefab")]
        public static void CreatePlayerPrefab()
        {
            if (!Directory.Exists(TargetDir))
            {
                Directory.CreateDirectory(TargetDir);
                AssetDatabase.Refresh();
            }

            // Build the template in-scene.
            GameObject player = new GameObject("NetworkPlayer");

            player.AddComponent<NetworkIdentity>();

            Rigidbody rb        = player.AddComponent<Rigidbody>();
            rb.mass             = 1f;
            rb.freezeRotation   = true;

            CapsuleCollider cap = player.AddComponent<CapsuleCollider>();
            cap.height          = 2f;
            cap.radius          = 0.5f;
            cap.center          = new Vector3(0f, 1f, 0f);

            // Camera socket (head height) and orientation node.
            var camSocket = new GameObject("CamSocket").transform;
            camSocket.SetParent(player.transform, worldPositionStays: false);
            camSocket.localPosition = new Vector3(0f, 1.6f, 0f);

            var orientation = new GameObject("Orientation").transform;
            orientation.SetParent(player.transform, worldPositionStays: false);

            PlayerMovement pm = player.AddComponent<PlayerMovement>();
            // The two Transform refs are private-serialized on PlayerMovement;
            // use SerializedObject so Unity records the assignment properly.
            SerializedObject so = new SerializedObject(pm);
            so.FindProperty("playerCam").objectReferenceValue   = camSocket;
            so.FindProperty("orientation").objectReferenceValue = orientation;
            so.ApplyModifiedPropertiesWithoutUndo();

            player.AddComponent<NetworkTransformReliable>();
            player.AddComponent<NetworkPlayer>();

            // Save as prefab, then destroy the scene instance.
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(player, TargetPath);
            Object.DestroyImmediate(player);

            EditorGUIUtility.PingObject(saved);
            Selection.activeObject = saved;
            Debug.Log($"Created {TargetPath}");
        }

        [MenuItem("Life-is-a-Game/Setup/Add NetworkManager to Current Scene")]
        public static void AddNetworkManagerToScene()
        {
            if (Object.FindObjectOfType<NetworkManager>() != null)
            {
                Debug.Log("A NetworkManager already exists in this scene. Skipping.");
                return;
            }
            // Trigger GameBootstrap manually (it normally runs BeforeSceneLoad).
            // In Edit-mode we just create the same GameObject as GameBootstrap would.
            var go   = new GameObject("~GameNetworkManager");
            var kcp  = go.AddComponent<kcp2k.KcpTransport>();
            kcp.Port = 7777;
            var nm            = go.AddComponent<GameNetworkManager>();
            nm.transport      = kcp;
            nm.playerPrefab   = AssetDatabase.LoadAssetAtPath<GameObject>(TargetPath);
            go.AddComponent<GameNetworkDiscovery>().transport = kcp;
            Selection.activeObject = go;
            Debug.Log("NetworkManager added to current scene.");
        }
    }
}
#endif

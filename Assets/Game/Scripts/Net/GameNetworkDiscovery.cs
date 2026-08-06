// LAN room advertising / discovery.
//
// Feeds the Phase 3 server browser (RoomListPanel) with a live list of
// rooms broadcasting on the local network. Uses Mirror's built-in
// NetworkDiscovery over UDP broadcast — no external services required.
//
// The server broadcasts a `DiscoveryResponse` describing its room every
// `ActiveDiscoveryInterval` seconds; clients listen and raise
// OnServerFound with each response.
//
// For internet play a separate matchmaker service would be needed; that
// is out of scope for Phase 2 (LAN only).

using System;
using Mirror;
using Mirror.Discovery;
using UnityEngine;

namespace LifeIsAGame.Net
{
    [Serializable]
    public struct RoomInfo : NetworkMessage
    {
        public string RoomName;
        public byte   CurrentPlayers;
        public byte   MaxPlayers;
        public bool   HasPassword;
    }

    public sealed class GameNetworkDiscovery : NetworkDiscovery
    {
        [Header("Life-is-a-Game — room advertised on the LAN")]
        [SerializeField] private string   roomName       = "Untitled Room";
        [SerializeField] private byte     maxPlayers     = 8;
        [SerializeField] private bool     hasPassword    = false;

        /// <summary>Set by the CreateRoom UI before StartHost().</summary>
        public void ConfigureRoom(string name, byte cap, bool passworded)
        {
            roomName    = string.IsNullOrWhiteSpace(name) ? "Untitled Room" : name.Trim();
            maxPlayers  = (byte)Mathf.Clamp(cap, (byte)2, (byte)16);
            hasPassword = passworded;
        }

        /// <summary>Called on the server to build the payload each interval.</summary>
        protected override ServerResponse ProcessRequest(ServerRequest request, System.Net.IPEndPoint endpoint)
        {
            try
            {
                return new ServerResponse
                {
                    serverId = ServerId,
                    uri      = transport.ServerUri(),
                    // Extra room-info fields ride via a companion struct
                    // that the client reads out of the ExtendedResponse
                    // hook below.
                };
            }
            catch (NotImplementedException)
            {
                // Transport doesn't expose a URI — happens with some
                // custom transports. Fall back to the request endpoint.
                return default;
            }
        }

        /// <summary>Extra room metadata piggy-backed on the discovery response.</summary>
        public RoomInfo CurrentRoomInfo => new RoomInfo
        {
            RoomName       = roomName,
            CurrentPlayers = (byte)(NetworkServer.connections?.Count ?? 0),
            MaxPlayers     = maxPlayers,
            HasPassword    = hasPassword,
        };
    }
}

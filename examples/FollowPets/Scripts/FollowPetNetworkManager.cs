using JamesFrowen.Logging;
using JamesFrowen.PositionSync;
using Mirror;
using UnityEngine;

namespace JamesFrowen.NetworkPositionSync.Examples.FollowPets
{
    public class FollowPetNetworkManager : NetworkManager
    {
        private SpawnPet spawnPet;
        private SyncPositionSystem syncPostionSystem;

        [SerializeField] private LogType logLevel = LogType.Warning;

        public override void Awake()
        {
            base.Awake();
            spawnPet = GetComponent<SpawnPet>();
            syncPostionSystem = GetComponent<SyncPositionSystem>();
            SimpleLogger.Logger = new Logger(Debug.unityLogger) { filterLogType = logLevel };
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            spawnPet.ServerStarted();
            syncPostionSystem.RegisterServerHandlers();
        }
        public override void OnStopServer()
        {
            base.OnStopServer();
            syncPostionSystem.UnregisterServerHandlers();
        }

        public override void OnServerConnect(NetworkConnection conn)
        {
            base.OnServerConnect(conn);
            spawnPet.PlayerConnected(conn);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            syncPostionSystem.RegisterClientHandlers();
        }
        public override void OnStopClient()
        {
            base.OnStopClient();
            syncPostionSystem.UnregisterClientHandlers();
        }
    }
}

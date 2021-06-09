using Mirror;

namespace JamesFrowen.NetworkPositionSync.Examples.FollowPets
{
    public class FollowPetNetworkManager : NetworkManager
    {
        private SpawnPet spawnPet;

        public override void Awake()
        {
            base.Awake();
            spawnPet = GetComponent<SpawnPet>();
        }
        public override void OnStartServer()
        {
            base.OnStartServer();
            spawnPet.ServerStarted();
        }
        public override void OnServerConnect(NetworkConnection conn)
        {
            base.OnServerConnect(conn);
            spawnPet.PlayerConnected(conn);
        }
    }
}

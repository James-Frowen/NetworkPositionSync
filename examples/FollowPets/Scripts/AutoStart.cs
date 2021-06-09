using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace JamesFrowen.NetworkPositionSync.Examples.FollowPets
{
    public class AutoStart : MonoBehaviour
    {
        [Header("Editor only")]
        [SerializeField] bool editorStartClient;
        [SerializeField] string address = "localhost";
        [SerializeField] bool editorStartServer;

        private void Start()
        {
#if UNITY_EDITOR
            if (editorStartServer)
            {
                StartServer();
            }
            else if (editorStartClient)
            {
                StartClient(new List<string>() {
                    "-client",
                    address
                });
            }
#else
            var args = GetArgs();
            if (args.Contains("-server"))
            {
                this.StartServer();
            }
            else if (args.Contains("-client"))
            {
                this.StartClient(args);
            }
#endif
        }

        private void StartClient(List<string> args)
        {
            if (NetworkClient.active || NetworkServer.active) { return; }

            int indexOf = args.IndexOf("-client");
            string address = args[indexOf + 1];
            Debug.Log("Starting Client");
            NetworkManager.singleton.networkAddress = address;
            NetworkManager.singleton.StartClient();
        }

        private void StartServer()
        {
            if (NetworkClient.active || NetworkServer.active) { return; }

            Debug.Log("Starting Server");
            NetworkManager.singleton.StartServer();
        }

        private static List<string> GetArgs()
        {
            return new List<string>(System.Environment.GetCommandLineArgs());
        }
    }
}

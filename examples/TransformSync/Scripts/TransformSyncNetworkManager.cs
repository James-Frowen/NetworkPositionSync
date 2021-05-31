using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

namespace JamesFrowen.PositionSync.Example
{
    public class TransformSyncNetworkManager : NetworkManager
    {
        [Header("References")]
        [SerializeField] SyncPositionSystem system;

        [Header("Moving objects")]
        [SerializeField] int cubeCount = 10;
        [SerializeField] GameObject cubePrefab;
        [SerializeField] bool showGUI = true;
        [SerializeField] Rect guiRect = new Rect(100, 0, 200, 200);

        List<GameObject> cubes = new List<GameObject>();
        [SerializeField] private bool autoStartFromCLI;

        public override void Start()
        {
            base.Start();

            if (autoStartFromCLI)
            {
                var args = Environment.GetCommandLineArgs().ToList();
                int addressIndex = args.IndexOf("-address");
                if (addressIndex != -1)
                {
                    if (addressIndex + 1 < args.Count)
                    {
                        string address = args[addressIndex + 1];
                        networkAddress = address;
                    }
                    else
                    {
                        Debug.LogError("Useage '-address <hostname>'");
                    }
                }

                Application.targetFrameRate = serverTickRate;
                if (args.Contains("-server"))
                {
                    StartServer();
                }
                else if (args.Contains("-client"))
                {
                    StartClient();
                }
                else
                {
                    Debug.LogWarning("no Startup command found");
                }
            }
        }
        public override void OnStartClient()
        {
            system.RegisterHandlers();
            NetworkClient.RegisterPrefab(cubePrefab);
        }

        public override void OnStartServer()
        {
            system.RegisterHandlers();
            for (int i = 0; i < cubeCount; i++)
            {
                SpawnNewCube();
            }
        }

        private void SpawnNewCube()
        {
            GameObject clone = Instantiate(cubePrefab);
            NetworkServer.Spawn(clone);
            cubes.Add(clone);
        }

        public override void OnStopClient()
        {
            system.UnregisterHandlers();
            system.packer.ClearBehaviours();
        }
        public override void OnStopServer()
        {
            system.UnregisterHandlers();
            system.packer.ClearBehaviours();
        }

        public override void OnApplicationQuit()
        {
            base.OnApplicationQuit();
            system.packer.ClearBehaviours();
        }

        private void OnGUI()
        {
            if (!showGUI) { return; }
            if (!NetworkServer.active) { return; }

            using (new GUILayout.AreaScope(guiRect))
            {
                GUILayout.Label($"Cube Count: {cubes.Count}");
                if (GUILayout.Button("Add Cube"))
                {
                    SpawnNewCube();
                }
                if (GUILayout.Button("Remove Cube"))
                {
                    if (cubes.Count > 0) { cubes.RemoveAt(cubes.Count - 1); }
                }
            }
        }
    }
}

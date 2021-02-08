using Mirror;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace JamesFrowen.PositionSync.Example
{
    public class TransformSyncNetworkManager : NetworkManager
    {
        [Header("References")]
        [SerializeField] SyncPositionSystem system;
        [SerializeField] SyncPositionBehaviourRuntimeDictionary behaviours;

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

            if (this.autoStartFromCLI)
            {
                var args = Environment.GetCommandLineArgs().ToList();
                var addressIndex = args.IndexOf("-address");
                if (addressIndex != -1)
                {
                    if (addressIndex + 1 < args.Count)
                    {
                        var address = args[addressIndex + 1];
                        this.networkAddress = address;
                    }
                    else
                    {
                        Debug.LogError("Useage '-address <hostname>'");
                    }
                }

                Application.targetFrameRate = this.serverTickRate;
                if (args.Contains("-server"))
                {
                    this.StartServer();
                }
                else if (args.Contains("-client"))
                {
                    this.StartClient();
                }
                else
                {
                    Debug.LogWarning("no Startup command found");
                }
            }
        }
        public override void OnStartClient()
        {
            this.system.RegisterHandlers();
            ClientScene.RegisterPrefab(this.cubePrefab);
        }

        public override void OnStartServer()
        {
            this.system.RegisterHandlers();
            for (var i = 0; i < this.cubeCount; i++)
            {
                this.SpawnNewCube();
            }
        }

        private void SpawnNewCube()
        {
            var clone = Instantiate(this.cubePrefab);
            NetworkServer.Spawn(clone);
            this.cubes.Add(clone);
        }

        public override void OnStopClient()
        {
            this.system.UnregisterHandlers();
            this.behaviours.Clear();
        }
        public override void OnStopServer()
        {
            this.system.UnregisterHandlers();
            this.behaviours.Clear();
        }

        public override void OnApplicationQuit()
        {
            base.OnApplicationQuit();
            this.behaviours.Clear();
        }

        private void OnGUI()
        {
            if (!this.showGUI) { return; }
            if (!NetworkServer.active) { return; }

            using (new GUILayout.AreaScope(this.guiRect))
            {
                GUILayout.Label($"Cube Count: {this.cubes.Count}");
                if (GUILayout.Button("Add Cube"))
                {
                    this.SpawnNewCube();
                }
                if (GUILayout.Button("Remove Cube"))
                {
                    if (this.cubes.Count > 0) { this.cubes.RemoveAt(this.cubes.Count - 1); }
                }
            }
        }
    }
}

using Mirror;
using Mirror.Tests.Runtime;
using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;

namespace JamesFrowen.PositionSync.Tests.Runtime
{
    [Category("NetworkPositionSync")]
    public class NetworkTransformSnapshotInterpolationTest : HostSetup
    {
        readonly List<GameObject> spawned = new List<GameObject>();

        private SyncPositionBehaviour serverNT;
        private SyncPositionBehaviour clientNT;

        protected override bool AutoAddPlayer => false;

        protected override void afterStartHost()
        {
            var serverGO = new GameObject("server object");
            var clientGO = new GameObject("client object");
            this.spawned.Add(serverGO);
            this.spawned.Add(clientGO);

            var serverNI = serverGO.AddComponent<NetworkIdentity>();
            var clientNI = clientGO.AddComponent<NetworkIdentity>();

            this.serverNT = serverGO.AddComponent<SyncPositionBehaviour>();
            this.clientNT = clientGO.AddComponent<SyncPositionBehaviour>();

            // set up Identitys so that server object can send message to client object in host mode
            FakeSpawnServerClientIdentity(serverNI, clientNI);

            // reset both transforms
            serverGO.transform.position = Vector3.zero;
            clientGO.transform.position = Vector3.zero;
        }

        protected override void beforeStopHost()
        {
            foreach (var obj in this.spawned)
            {
                Object.Destroy(obj);
            }
        }

        [UnityTest]
        public IEnumerator SyncPositionFromServerToClient()
        {
            var positions = new Vector3[] {
                new Vector3(1, 2, 3),
                new Vector3(2, 2, 3),
                new Vector3(2, 3, 5),
                new Vector3(2, 3, 5),
            };

            foreach (var position in positions)
            {
                this.serverNT.transform.position = position;
                // wait more than needed to check end position is reached
                yield return new WaitForSeconds(0.5f);

                Assert.That(this.clientNT.transform.position, Is.EqualTo(position));
            }
        }
    }
}

using System.Collections;
using Cysharp.Threading.Tasks;
using Mirage;
using Mirage.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mirage.SyncPosition.Tests.Runtime
{
    [Category("NetworkPositionSync")]
    public class NetworkTransformSnapshotInterpolationTest : ClientServerSetup<NetworkTransform3D>
    {
        public override void ExtraSetup()
        {
            base.ExtraSetup();
            var serverSystem = serverGo.AddComponent<SyncPositionSystem>();
            var clientSystem = clientGo.AddComponent<SyncPositionSystem>();

            serverSystem.Server = server;
            serverSystem.Awake();

            clientSystem.Client = client;
            clientSystem.Awake();
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
                serverComponent.transform.position = position;
                // wait more than needed to check end position is reached
                yield return new WaitForSeconds(0.5f);

                Assert.That(clientComponent.transform.position, Is.EqualTo(position));
            }
        }
    }

    [Category("NetworkPositionSync")]
    public class MultipleBehavioursTest : ClientServerSetup<NetworkTransform3D>
    {
        private NetworkIdentity prefabWithMultiple;
        private NetworkIdentity serverObj;
        private NetworkIdentity clientObj;


        public override void ExtraSetup()
        {
            base.ExtraSetup();
            var serverSystem = serverGo.AddComponent<SyncPositionSystem>();
            var clientSystem = clientGo.AddComponent<SyncPositionSystem>();

            serverSystem.Server = server;
            serverSystem.Awake();

            clientSystem.Client = client;
            clientSystem.Awake();
        }

        public override async UniTask LateSetup()
        {
            prefabWithMultiple = CreateNetworkIdentity();
            prefabWithMultiple.gameObject.SetActive(false); // disable to stop awake being called
            var child1 = new GameObject("Child 1");
            var child2 = new GameObject("Child 2");
            child1.transform.parent = prefabWithMultiple.transform;
            child2.transform.parent = prefabWithMultiple.transform;

            prefabWithMultiple.gameObject.AddComponent<NetworkTransform3D>();
            child1.AddComponent<NetworkTransform3D>();
            child2.AddComponent<NetworkTransform3D>();

            const int PrefabHash = 1000;
            clientObjectManager.RegisterPrefab(prefabWithMultiple, PrefabHash);

            serverObj = GameObject.Instantiate(prefabWithMultiple);
            serverObj.gameObject.SetActive(true);
            serverObjectManager.Spawn(serverObj, PrefabHash);
            var netId = serverObj.NetId;

            clientObj = await AsyncUtil.WaitUntilSpawn(client.World, netId);
        }

        [UnityTest]
        public IEnumerator SyncsAllPositions()
        {
            var rootPos = new Vector3(30, 10, 10);
            var child1Pos = new Vector3(40, 10, 10);
            var child2Pos = new Vector3(50, 10, 10);
            var serverChild1 = serverObj.transform.Find($"Child 1");
            var serverChild2 = serverObj.transform.Find($"Child 2");
            serverObj.transform.localPosition = rootPos;
            serverChild1.localPosition = child1Pos;
            serverChild2.localPosition = child2Pos;

            yield return new WaitForSeconds(0.5f);

            var clientChild1 = clientObj.transform.Find($"Child 1");
            var clientChild2 = clientObj.transform.Find($"Child 2");
            Assert.That(clientObj.transform.localPosition, Is.EqualTo(rootPos));
            Assert.That(clientChild1.localPosition, Is.EqualTo(child1Pos));
            Assert.That(clientChild2.localPosition, Is.EqualTo(child2Pos));
        }
    }
}

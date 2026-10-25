using System.Collections;
using Cysharp.Threading.Tasks;
using Mirage.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// Current snapshot is sent in the spawn payload, so that a client that sees an object for the first time gets its current pose even if it isn't moving.
    /// <para>
    /// Mirage's spawn message only contains the root transform's local values,
    /// so without this a NetworkTransform on a child (or one using Local coordinates) would stay at its prefab pose until it next moves
    /// </para>
    /// </summary>
    [Category("NetworkPositionSync")]
    public class SpawnPayloadTests : SyncPositionTestBase<NetworkTransform3D>
    {
        private const int PREFAB_HASH = 1000;
        private const float POSITION_TOLERANCE = 0.01f;
        private const float ANGLE_TOLERANCE = 1f;

        private static readonly Vector3 RootPosition = new Vector3(30, 10, 10);
        private static readonly Quaternion RootRotation = Quaternion.Euler(0, 45, 0);
        private static readonly Vector3 Child1LocalPosition = new Vector3(40, 10, 10);
        private static readonly Quaternion Child1LocalRotation = Quaternion.Euler(10, 20, 30);
        private static readonly Vector3 Child2LocalPosition = new Vector3(-5, 2, 7);
        private static readonly Quaternion Child2LocalRotation = Quaternion.Euler(-30, 60, 5);

        private NetworkIdentity _prefabWithChildren;
        private NetworkIdentity _serverObj;
        private NetworkIdentity _clientObj;

        private struct Pose
        {
            public Vector3 Position;
            public Quaternion Rotation;

            public static Pose World(Transform t) => new Pose { Position = t.position, Rotation = t.rotation };
            public static Pose Local(Transform t) => new Pose { Position = t.localPosition, Rotation = t.localRotation };

            public override string ToString() => $"[{Position}, {Rotation.eulerAngles}]";
        }

        protected override async UniTask LateSetup()
        {
            await base.LateSetup();

            _prefabWithChildren = CreateNetworkIdentity(disable: true); // disable to stop awake being called
            _prefabWithChildren.name = "prefab with children";
            var child1 = new GameObject("Child 1");
            var child2 = new GameObject("Child 2");
            child1.transform.parent = _prefabWithChildren.transform;
            child2.transform.parent = _prefabWithChildren.transform;

            _prefabWithChildren.gameObject.AddComponent<NetworkTransform3D>();
            // child 1 uses world coordinates (default)
            child1.AddComponent<TestNetworkTransform3D>().SetCoordinates(Coordinates.World);
            // child 2 uses local coordinates
            child2.AddComponent<TestNetworkTransform3D>().SetCoordinates(Coordinates.Local);

            clientObjectManager.RegisterPrefab(_prefabWithChildren, PREFAB_HASH);

            _serverObj = InstantiateForTest(_prefabWithChildren);
            serverObjectManager.Spawn(_serverObj, PREFAB_HASH);

            _clientObj = await AsyncUtil.WaitUntilSpawn(client.World, _serverObj.NetId);
        }

        private static Transform Child1(NetworkIdentity identity) => identity.transform.Find("Child 1");
        private static Transform Child2(NetworkIdentity identity) => identity.transform.Find("Child 2");

        private static bool Matches(Pose actual, Pose expected)
        {
            return Vector3.Distance(actual.Position, expected.Position) < POSITION_TOLERANCE
                && Quaternion.Angle(actual.Rotation, expected.Rotation) < ANGLE_TOLERANCE;
        }

        private static void AssertPose(Pose actual, Pose expected, string message)
        {
            Assert.That(Vector3.Distance(actual.Position, expected.Position), Is.LessThan(POSITION_TOLERANCE), $"{message} position. Expected:{expected} Actual:{actual}");
            Assert.That(Quaternion.Angle(actual.Rotation, expected.Rotation), Is.LessThan(ANGLE_TOLERANCE), $"{message} rotation. Expected:{expected} Actual:{actual}");
        }

        private bool AllMatch(NetworkIdentity a, NetworkIdentity b)
        {
            return Matches(Pose.World(a.transform), Pose.World(b.transform))
                && Matches(Pose.World(Child1(a)), Pose.World(Child1(b)))
                && Matches(Pose.Local(Child2(a)), Pose.Local(Child2(b)));
        }

        /// <summary>
        /// Moves object on server, and waits for first client to have the same pose.
        /// After that the object is not moving, so server will not send any more snapshots for it
        /// </summary>
        private IEnumerator MoveOnServerAndWaitForSync()
        {
            _serverObj.transform.SetPositionAndRotation(RootPosition, RootRotation);
            Child1(_serverObj).localPosition = Child1LocalPosition;
            Child1(_serverObj).localRotation = Child1LocalRotation;
            Child2(_serverObj).localPosition = Child2LocalPosition;
            Child2(_serverObj).localRotation = Child2LocalRotation;

            yield return SyncPositionTestHelpers.WaitUntil(() => AllMatch(_serverObj, _clientObj), Timeout);
            Assert.That(AllMatch(_serverObj, _clientObj), Is.True, "First client should have synced pose from server");

            // check that children are not at prefab pose, otherwise test would pass without spawn payload
            Assert.That(Matches(Pose.Local(Child1(_serverObj)), Pose.Local(Child1(_prefabWithChildren))), Is.False);
            Assert.That(Matches(Pose.Local(Child2(_serverObj)), Pose.Local(Child2(_prefabWithChildren))), Is.False);
        }

        /// <summary>
        /// Creates a client that is not connected yet, registers prefab on it, and invokes <paramref name="onSpawn"/> from its world's onSpawn event.
        /// </summary>
        private ClientInstance CreateLateClient(System.Action<NetworkIdentity> onSpawn = null)
        {
            var lateClient = CreateClientInstance();
            lateClient.ClientObjectManager.RegisterPrefab(_prefabWithChildren, PREFAB_HASH);

            if (onSpawn != null)
            {
                // world is created inside Connect, Started is invoked after that but before any spawn message is received
                lateClient.Client.Started.AddListener(() => lateClient.Client.World.onSpawn += onSpawn);
            }

            return lateClient;
        }

        private IEnumerator ConnectAndWaitForSpawn(ClientInstance lateClient, uint netId)
        {
            yield return AddClient(lateClient, SpawnCharacterOnConnect).ToCoroutine();
            yield return SyncPositionTestHelpers.WaitUntil(() => lateClient.Client.World.TryGetIdentity(netId, out _), Timeout);
            Assert.That(lateClient.Client.World.TryGetIdentity(netId, out _), Is.True, "Object should have spawned on late client");
        }

        [UnityTest]
        public IEnumerator LateClientGetsCurrentPoseOfChildrenInSpawnPayload()
        {
            yield return MoveOnServerAndWaitForSync();

            var netId = _serverObj.NetId;
            var expectedRoot = Pose.World(_serverObj.transform);
            var expectedChild1 = Pose.World(Child1(_serverObj));
            var expectedChild2 = Pose.Local(Child2(_serverObj));

            // record pose at the moment the object is spawned on the late client.
            // payload is deserialized before onSpawn is invoked, so there is no chance for interpolation or later messages to have changed it
            var spawned = false;
            Pose spawnRoot = default, spawnChild1 = default, spawnChild2 = default;
            var lateClient = CreateLateClient((identity) =>
            {
                if (identity.NetId != netId)
                    return;

                spawned = true;
                spawnRoot = Pose.World(identity.transform);
                spawnChild1 = Pose.World(Child1(identity));
                spawnChild2 = Pose.Local(Child2(identity));
            });

            yield return ConnectAndWaitForSpawn(lateClient, netId);
            var lateObj = lateClient.Get(_serverObj);

            Assert.That(spawned, Is.True, "onSpawn should have been invoked for object");
            AssertPose(spawnRoot, expectedRoot, "Root at spawn");
            AssertPose(spawnChild1, expectedChild1, "Child 1 (world coordinates) at spawn");
            AssertPose(spawnChild2, expectedChild2, "Child 2 (local coordinates) at spawn");

            // nothing should move it after spawn
            AssertPose(Pose.World(lateObj.transform), expectedRoot, "Root after spawn");
            AssertPose(Pose.World(Child1(lateObj)), expectedChild1, "Child 1 (world coordinates) after spawn");
            AssertPose(Pose.Local(Child2(lateObj)), expectedChild2, "Child 2 (local coordinates) after spawn");

            // spawn snapshot is applied directly, not added to buffer
            foreach (var behaviour in lateObj.GetComponentsInChildren<NetworkTransform3D>())
                Assert.That(behaviour._snapshotBuffer.SnapshotCount, Is.Zero, $"{behaviour.name} should have no snapshots after spawn");

            errorLogs.AssertNoErrors();
        }

        /// <summary>
        /// Late client should still get later movement after spawn
        /// </summary>
        [UnityTest]
        public IEnumerator LateClientSyncsMovementAfterSpawn()
        {
            yield return MoveOnServerAndWaitForSync();

            var lateClient = CreateLateClient();
            yield return ConnectAndWaitForSpawn(lateClient, _serverObj.NetId);
            var lateObj = lateClient.Get(_serverObj);

            Child1(_serverObj).localPosition = new Vector3(1, 2, 3);

            yield return SyncPositionTestHelpers.WaitUntil(() => AllMatch(_serverObj, lateObj), Timeout);
            Assert.That(AllMatch(_serverObj, lateObj), Is.True, $"Late client should sync movement after spawn. Child 1 was {Child1(lateObj).localPosition}");
            errorLogs.AssertNoErrors();
        }

        [Test]
        public void BehavioursAreControlledBySystem()
        {
            foreach (var behaviour in _serverObj.GetComponentsInChildren<NetworkTransform3D>())
                Assert.That(behaviour.IsStandalone, Is.False, $"{behaviour.name} on server");
            foreach (var behaviour in _clientObj.GetComponentsInChildren<NetworkTransform3D>())
                Assert.That(behaviour.IsStandalone, Is.False, $"{behaviour.name} on client");
        }
    }
}

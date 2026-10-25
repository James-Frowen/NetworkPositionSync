using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Mirage.Logging;
using Mirage.Serialization;
using Mirage.Tests;
using Mirage.Tests.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mirage.SyncPosition.Tests.Runtime
{
    public class MockNetworkTransform : NetworkTransformBase<MockNetworkTransform.Data>
    {
        // todo calculate actual maxSize from pack settings
        //      it is fine to overestimate for now
        public override int MaxWriteSize => 4 * 1; // snapshot is 1 floats

        private static readonly ILogger logger = LogFactory.GetLogger<MockNetworkTransform>();

        public event Action OnSetup;
        public event Action OnCreateSnapshot;
        public event Action OnApplySnapshot;
        public event Action OnHasChanged;
        public event Action OnWriteSnapshot;
        public event Action OnReadSnapshot;
        public event Action OnCreateInterpolator;

        public float Value;

        public override void Setup()
        {
            OnSetup?.Invoke();
        }
        protected override bool CreateSnapshot(out Data newSnapshot, bool force)
        {
            newSnapshot = new Data { Value = Value };

            if (logger.LogEnabled()) logger.Log($"{Time.time}:{name} CreateSnapshot {newSnapshot.Value}");
            OnCreateSnapshot?.Invoke();

            var changed = newSnapshot.Value != _snapshot.Value;
            if (logger.LogEnabled()) logger.Log($"{Time.time}:{name} HasChanged {changed} old:{_snapshot.Value} new:{newSnapshot.Value}");
            OnHasChanged?.Invoke();

            return force || changed;
        }

        protected override void ApplySnapshot(Data newSnapshot)
        {
            Value = newSnapshot.Value;

            if (logger.LogEnabled()) logger.Log($"{Time.time}:{name} ApplySnapshot {Value}");
            OnApplySnapshot?.Invoke();
        }

        protected override void WriteSnapshot(NetworkWriter writer, Data snapshot)
        {
            writer.Write(snapshot);

            if (logger.LogEnabled()) logger.Log($"{Time.time}:{name} WriteSnapshot {snapshot.Value}");
            OnWriteSnapshot?.Invoke();
        }
        protected override Data ReadSnapshot(NetworkReader reader)
        {
            var data = reader.Read<Data>();

            if (logger.LogEnabled()) logger.Log($"{Time.time}:{name} ReadSnapshot {data.Value}");
            OnReadSnapshot?.Invoke();
            return data;
        }

        protected override ISnapshotInterpolator<Data> CreateInterpolator()
        {
            OnCreateInterpolator?.Invoke();
            return new DataInterpolator();
        }

        [NetworkMessage]
        public struct Data
        {
            public float Value;

            public override string ToString()
            {
                return $"Data={Value}";
            }
        }

        public class DataInterpolator : ISnapshotInterpolator<Data>
        {
            public Data Interpolate(double from, Data a, double to, Data b, float alpha)
            {
                return new Data
                {
                    Value = Mathf.Lerp(a.Value, b.Value, alpha)
                };
            }
        }
    }

    [Category("NetworkPositionSync")]
    public class ClientAuthorityTransformTest : MultiRemoteClientSetup<MockNetworkTransform>
    {
        public const float START_VALUE = 5;
        public const float FIRST_VALUE = 10;

        protected override int RemoteClientCount => 2;

        private SyncPositionSystem serverSystem;
        private readonly List<SyncPositionSystem> clientSystem = new List<SyncPositionSystem>();

        // objects on server
        private MockNetworkTransform _serverOwned;

        public SyncPositionSystem.Settings Settings;
        private readonly List<ClientTestState> _previousClientValues = new List<ClientTestState>();

        private float Timeout => SyncPositionTestHelpers.Timeout(Settings);

        private class ClientTestState
        {
            public float Value;
            public bool Finished;
        }
        public ClientAuthorityTransformTest()
        {
            Settings = SyncPositionSystem.Settings.Default;
            Settings.IntervalTiming = SyncTiming.Fixed;
        }

        protected override void ExtraServerSetup()
        {
            // fixture is reused between tests, clear lists from previous test
            clientSystem.Clear();
            _previousClientValues.Clear();

            // note: server only, so this also tests client authority when server is not host
            serverSystem = SyncPositionTestHelpers.AddSystem(serverGo, server, null, Settings);
        }

        protected override void ExtraClientSetup(IClientInstance instance)
        {
            var system = SyncPositionTestHelpers.AddSystem(instance.GameObject, null, instance.Client, Settings);
            clientSystem.Add(system);
        }

        protected override async UniTask LateSetup()
        {
            await base.LateSetup();

            var clone = InstantiateForTest(_characterPrefab);
            serverObjectManager.Spawn(clone);
            _serverOwned = clone.GetComponent<MockNetworkTransform>();

            // wait for object to spawn on all clients
            foreach (var remote in _remoteClients)
                await AsyncUtil.WaitUntilSpawn(remote.Client.World, clone.NetId);

            RunOnAll(_serverOwned, (c) =>
            {
                c._clientAuthority = false;
                c.Value = START_VALUE;
            });

            for (var i = 0; i < RemoteClientCount; i++)
            {
                var clientOwned = ServerComponent(i);

                // objects owned by clients use client authority
                RunOnAll(clientOwned, (c) =>
                {
                    c._clientAuthority = true;
                    c.Value = START_VALUE;
                });

                _previousClientValues.Add(new ClientTestState { Value = START_VALUE });
            }
        }

        [Test]
        public void SetupCorrectly()
        {
            Assert.That(_serverOwned, Is.Not.Null);

            for (var i = 0; i < RemoteClientCount; i++)
            {
                // server object exists on client
                Assert.That(_remoteClients[i].Get(_serverOwned), Is.Not.Null);

                // client has their own character
                var clientBehaviour = ClientComponent(i);
                Assert.That(clientBehaviour, Is.Not.Null);
                // check character exists on server
                Assert.That(_serverInstance.Get(clientBehaviour), Is.Not.Null);

                for (var j = 0; j < RemoteClientCount; j++)
                {
                    // dont check self here
                    if (i == j)
                        continue;

                    // check that client instance also exists on other clients
                    Assert.That(_remoteClients[j].Get(clientBehaviour), Is.Not.Null);
                }
            }
        }

        private static bool LastSnapshotIs(MockNetworkTransform behaviour, float value)
        {
            var buffer = behaviour._snapshotBuffer;
            return buffer.SnapshotCount > 0 && buffer.DebugBuffer[buffer.SnapshotCount - 1].state.Value == value;
        }

        private static void AssertBufferHasNewValue(MockNetworkTransform behaviour)
        {
            var buffer = behaviour._snapshotBuffer;
            var list = buffer.DebugBuffer;

            var snapshotString = $"Snapshots:[{string.Join(",", list)}]";

            Assert.That(buffer.SnapshotCount, Is.GreaterThanOrEqualTo(2), $"Snapshot count should be atleast 2, check was {buffer.SnapshotCount}. Should have received new Value, and added current value. {snapshotString}");

            // all expect last should be START_VALUE
            for (var i = 0; i < buffer.SnapshotCount - 1; i++)
            {
                Assert.That(list[i].state.Value, Is.EqualTo(START_VALUE), $"First snapshot should be the starting value. {snapshotString}");
            }
            Assert.That(list[buffer.SnapshotCount - 1].state.Value, Is.EqualTo(FIRST_VALUE), $"Second snapshot should be new value {snapshotString}");
        }

        /// <summary>
        /// Checks each frame that value only moves towards FIRST_VALUE, until it reaches it
        /// </summary>
        private IEnumerator AssertInterpolatesToFirst(MockNetworkTransform behaviour, ClientTestState state, Action extraChecks = null)
        {
            var end = Time.unscaledTime + Timeout;
            while (!state.Finished && Time.unscaledTime < end)
            {
                yield return null;
                extraChecks?.Invoke();

                var previous = state.Value;
                var current = behaviour.Value;

                Assert.That(current, Is.GreaterThanOrEqualTo(previous), "Should only move closer to First");
                Assert.That(current, Is.LessThanOrEqualTo(FIRST_VALUE), "Should not move past First");

                if (current == FIRST_VALUE)
                    state.Finished = true;

                state.Value = current;
            }

            Assert.That(state.Finished, Is.True, $"Value should have reached {FIRST_VALUE}, but was {behaviour.Value}");
        }

        [UnityTest]
        public IEnumerator SyncsFromServer()
        {
            _serverOwned.Value = FIRST_VALUE;

            // it doesn't check each frame for changes, only each interval
            yield return SyncPositionTestHelpers.WaitUntil(() =>
            {
                for (var i = 0; i < RemoteClientCount; i++)
                {
                    if (!LastSnapshotIs(_remoteClients[i].Get(_serverOwned), FIRST_VALUE))
                        return false;
                }
                return true;
            }, Timeout);

            Assert.That(_serverOwned.Value, Is.EqualTo(FIRST_VALUE));
            for (var i = 0; i < RemoteClientCount; i++)
            {
                AssertBufferHasNewValue(_remoteClients[i].Get(_serverOwned));
            }

            // client should then interpolate towards new value
            for (var i = 0; i < RemoteClientCount; i++)
            {
                yield return AssertInterpolatesToFirst(_remoteClients[i].Get(_serverOwned), _previousClientValues[i]);
            }
        }

        [UnityTest]
        public IEnumerator SyncsFromOwnerToServerAndRemote()
        {
            var ownerBehaviour = ClientComponent(0);
            var remoteBehaviour = _remoteClients[1].Get(ownerBehaviour);
            var serverBehaviour = _serverInstance.Get(ownerBehaviour);
            var remoteState = _previousClientValues[1];

            ownerBehaviour.Value = FIRST_VALUE;

            // it doesn't check each frame for changes, only each interval
            yield return SyncPositionTestHelpers.WaitUntil(() => serverBehaviour.Value == FIRST_VALUE && LastSnapshotIs(remoteBehaviour, FIRST_VALUE), Timeout);

            Assert.That(ownerBehaviour.Value, Is.EqualTo(FIRST_VALUE), "Owner value should not have changed");
            Assert.That(serverBehaviour.Value, Is.EqualTo(FIRST_VALUE), "Server value should have been set without interpolation");
            Assert.That(remoteBehaviour.Value, Is.EqualTo(START_VALUE), "Remote value should not have changed yet, it should wait for interpolation");
            AssertBufferHasNewValue(remoteBehaviour);

            // remote should then interpolate towards new value
            yield return AssertInterpolatesToFirst(remoteBehaviour, remoteState, () =>
            {
                Assert.That(ownerBehaviour.Value, Is.EqualTo(FIRST_VALUE), "Owner value should not have changed");
                Assert.That(serverBehaviour.Value, Is.EqualTo(FIRST_VALUE), "Server value should not have changed");
            });
        }
    }
}

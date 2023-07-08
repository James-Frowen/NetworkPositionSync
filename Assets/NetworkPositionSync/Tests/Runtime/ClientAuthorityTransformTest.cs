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
        protected override Data CreateSnapshot()
        {
            var snapshot = new Data { Value = Value };

            if (logger.LogEnabled()) logger.Log($"{Time.time}:{name} CreateSnapshot {snapshot.Value}");
            OnCreateSnapshot?.Invoke();
            return snapshot;
        }

        protected override void ApplySnapshot(Data newSnapshot)
        {
            Value = newSnapshot.Value;

            if (logger.LogEnabled()) logger.Log($"{Time.time}:{name} ApplySnapshot {Value}");
            OnApplySnapshot?.Invoke();
        }


        protected override bool HasChanged(Data newSnapshot)
        {
            var changed = newSnapshot.Value != _snapshot.Value;

            if (logger.LogEnabled()) logger.Log($"{Time.time}:{name} HasChanged {changed} old:{_snapshot.Value} new:{newSnapshot.Value}");
            OnHasChanged?.Invoke();
            return changed;
        }

        protected override void WriteSnapshot(NetworkWriter writer)
        {
            writer.Write(_snapshot);

            if (logger.LogEnabled()) logger.Log($"{Time.time}:{name} WriteSnapshot {_snapshot.Value}");
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
            public Data Lerp(Data a, Data b, float alpha)
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
        private List<SyncPositionSystem> clientSystem = new List<SyncPositionSystem>();

        // objects on server
        private MockNetworkTransform _serverOwned;

        public SyncPositionSystem.Settings Settings;
        private List<ClientTestState> _previousClientValues = new List<ClientTestState>();

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
            serverSystem = serverGo.AddComponent<SyncPositionSystem>();
            serverSystem.Server = server;

            serverSystem.Setup(server: server, settings: Settings);
        }

        protected override void ExtraClientSetup(IClientInstance instance)
        {
            var system = instance.GameObject.AddComponent<SyncPositionSystem>();
            system.Setup(client: instance.Client, settings: Settings);
        }

        protected override async UniTask LateSetup()
        {
            await base.LateSetup();

            for (var i = 0; i < RemoteClientCount; i++)
            {
                clientSystem.Add(_remoteClients[i].GameObject.GetComponent<SyncPositionSystem>());
            }

            var clone = InstantiateForTest(_characterPrefab);
            serverObjectManager.Spawn(clone);
            _serverOwned = clone.GetComponent<MockNetworkTransform>();

            await UniTask.DelayFrame(2);

            RunOnAll(_serverOwned, (c) =>
            {
                c._clientAuthority = false;
                c.Value = START_VALUE;
            });

            for (var i = 0; i < RemoteClientCount; i++)
            {
                var clientOwned = ServerComponent(i);

                RunOnAll(clientOwned, (c) =>
                {
                    c._clientAuthority = false;
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

        [UnityTest]
        public IEnumerator SyncsFromServer()
        {
            _serverOwned.Value = FIRST_VALUE;

            // wait have to wait for whole interval
            // it doesn't check each frame for changes, only each interval
            yield return new WaitForSeconds(Settings.SyncInterval);

            RunOnAll(_serverOwned, (c) =>
            {
                // dont test server's copy
                if (_serverOwned == c)
                {
                    Assert.That(_serverOwned.Value, Is.EqualTo(FIRST_VALUE));
                    return;
                }

                var buffer = c._snapshotBuffer;
                var list = buffer.DebugBuffer;

                var snapshotString = $"Snapshots:[{string.Join(",", list)}]";

                Assert.That(buffer.SnapshotCount, Is.GreaterThanOrEqualTo(2), $"Snapshot count should be atleast 2, check was {buffer.SnapshotCount}. Should have received new Value, and added current value. {snapshotString}");

                // all expect last should be START_VALUE
                for (var i = 0; i < buffer.SnapshotCount - 1; i++)
                {
                    Assert.That(list[i].state.Value, Is.EqualTo(START_VALUE), $"First snapshot should be the starting value. {snapshotString}");
                }
                Assert.That(list[buffer.SnapshotCount - 1].state.Value, Is.EqualTo(FIRST_VALUE), $"Second snapshot should be new value {snapshotString}");
            });

            // wait for delay
            yield return new WaitForSeconds(Settings.SyncInterval * Settings.InterpolationDelay);

            // client should then interpolate towards new value over SyncInterval
            var start = Time.time;
            var end = start + Settings.SyncInterval;

            for (var now = start; now < end; now += Time.deltaTime)
            {
                // wait at start, so that we get first updated of applied values
                yield return null;

                for (var i = 0; i < RemoteClientCount; i++)
                {
                    var behaviour = _remoteClients[i].Get(_serverOwned);
                    if (_previousClientValues[i].Finished)
                        continue;

                    var previous = _previousClientValues[i].Value;
                    var current = behaviour.Value;

                    Assert.That(current, Is.GreaterThan(previous), "Should have moved closer to First");

                    if (current == FIRST_VALUE)
                        _previousClientValues[i].Finished = true;

                    _previousClientValues[i].Value = current;
                }
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

            // we have to wait for whole interval
            // it doesn't check each frame for changes, only each interval
            yield return new WaitForSeconds(Settings.SyncInterval);

            Assert.That(ownerBehaviour.Value, Is.EqualTo(FIRST_VALUE), "Owner value should not have changed");
            Assert.That(serverBehaviour.Value, Is.EqualTo(FIRST_VALUE), "Server value should have been set without interpolation");
            Assert.That(remoteBehaviour.Value, Is.EqualTo(START_VALUE), "Remote value should not have changed yet, it should wait for interpolation");

            {
                var buffer = remoteBehaviour._snapshotBuffer;
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

            // wait for delay
            yield return new WaitForSeconds(Settings.SyncInterval * Settings.InterpolationDelay);

            // client should then interpolate towards new value over SyncInterval
            var start = Time.time;
            var end = start + Settings.SyncInterval;

            for (var now = start; now < end; now += Time.deltaTime)
            {
                // wait at start, so that we get first updated of applied values
                yield return null;

                Assert.That(ownerBehaviour.Value, Is.EqualTo(FIRST_VALUE), "Owner value should not have changed");
                Assert.That(serverBehaviour.Value, Is.EqualTo(FIRST_VALUE), "Server value should not have changed");

                if (remoteState.Finished)
                    continue;

                var previous = remoteState.Value;
                var current = remoteBehaviour.Value;

                Assert.That(current, Is.GreaterThan(previous), "Should have moved closer to First");

                if (current == FIRST_VALUE)
                    remoteState.Finished = true;

                remoteState.Value = current;
            }
        }
    }
}

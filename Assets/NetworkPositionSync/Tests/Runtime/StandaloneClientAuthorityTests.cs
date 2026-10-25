using System.Collections;
using Cysharp.Threading.Tasks;
using Mirage.Tests;
using Mirage.Tests.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// Without a <see cref="SyncPositionSystem"/>, client authority objects should sync from owner to (dedicated) server and then to other clients
    /// </summary>
    [Category("NetworkPositionSync")]
    public class StandaloneClientAuthorityTests : MultiRemoteClientSetup<MockNetworkTransform>
    {
        private const float START_VALUE = 5;
        private const float FIRST_VALUE = 10;

        protected override int RemoteClientCount => 2;

        private ErrorLogCollector errorLogs;

        /// <summary>Character owned by client 0, on client 0</summary>
        private MockNetworkTransform _owner;
        /// <summary>Character owned by client 0, on server</summary>
        private MockNetworkTransform _server;
        /// <summary>Character owned by client 0, on client 1</summary>
        private MockNetworkTransform _remote;

        private float Timeout => StandaloneTestHelpers.Timeout(_owner);

        protected override void ExtraServerSetup()
        {
            base.ExtraServerSetup();
            errorLogs?.Dispose();
            errorLogs = new ErrorLogCollector();
        }

        protected override void ExtraPrefabSetup(NetworkIdentity prefab)
        {
            base.ExtraPrefabSetup(prefab);
            // set on prefab so that it is set on all copies before they are spawned
            prefab.GetComponent<MockNetworkTransform>()._clientAuthority = true;
        }

        public override void ExtraTearDown()
        {
            base.ExtraTearDown();
            errorLogs?.Dispose();
            errorLogs = null;
        }

        protected override async UniTask LateSetup()
        {
            await base.LateSetup();

            _owner = ClientComponent(0);
            _server = _serverInstance.Get(_owner);
            _remote = _remoteClients[1].Get(_owner);

            // set start value on owner, and wait for it to reach everyone
            _owner.Value = START_VALUE;
            await AsyncUtil.WaitUntilWithTimeout(() => _server.Value == START_VALUE && _remote.Value == START_VALUE, Timeout);
        }

        [Test]
        public void SetupCorrectly()
        {
            Assert.That(server.LocalPlayer, Is.Null, "Server should not be host");
            Assert.That(_owner.HasAuthority, Is.True);
            Assert.That(_remote.HasAuthority, Is.False);
            Assert.That(_server.Owner, Is.EqualTo(ServerPlayer(0)));

            Assert.That(_owner.IsStandalone, Is.True);
            Assert.That(_server.IsStandalone, Is.True);
            Assert.That(_remote.IsStandalone, Is.True);
            errorLogs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator SyncsFromOwnerToServerAndRemote()
        {
            var remoteTracker = new MonotonicTracker("remote", () => _remote.Value, FIRST_VALUE);

            _owner.Value = FIRST_VALUE;

            yield return remoteTracker.WaitUntilReached(Timeout, () =>
            {
                Assert.That(_owner.Value, Is.EqualTo(FIRST_VALUE), "Owner value should not have changed");
                Assert.That(_owner._snapshotBuffer.SnapshotCount, Is.Zero, "Owner should not add snapshots to its buffer");
                // dedicated server applies snapshot from owner directly, without interpolation
                Assert.That(_server.Value, Is.EqualTo(START_VALUE).Or.EqualTo(FIRST_VALUE), "Server should apply value directly without interpolation");
            });

            Assert.That(_server.Value, Is.EqualTo(FIRST_VALUE), "Server should have received value from owner");
            Assert.That(_owner._snapshotBuffer.SnapshotCount, Is.Zero, "Owner should not add snapshots to its buffer");
            errorLogs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator SyncsContinuousMovementFromOwner()
        {
            const float SPEED = 10;
            var duration = _owner._standaloneSyncInterval * 10;
            var endValue = START_VALUE + (SPEED * duration);

            var serverTracker = new MonotonicTracker("server", () => _server.Value, endValue);
            var remoteTracker = new MonotonicTracker("remote", () => _remote.Value, endValue);

            var start = Time.unscaledTime;
            while (Time.unscaledTime < start + duration)
            {
                _owner.Value = START_VALUE + (SPEED * (Time.unscaledTime - start));
                yield return null;

                serverTracker.Sample();
                remoteTracker.Sample();
                Assert.That(_owner._snapshotBuffer.SnapshotCount, Is.Zero, "Owner should not add snapshots to its buffer");
            }
            _owner.Value = endValue;

            yield return remoteTracker.WaitUntilReached(Timeout, () =>
            {
                serverTracker.Sample();
                Assert.That(_owner.Value, Is.EqualTo(endValue), "Owner value should not have changed");
                Assert.That(_owner._snapshotBuffer.SnapshotCount, Is.Zero, "Owner should not add snapshots to its buffer");
            });

            Assert.That(_server.Value, Is.EqualTo(endValue), "Server should have received final value from owner");
            Assert.That(remoteTracker.DistinctValues, Is.GreaterThanOrEqualTo(10), "Remote should have interpolated between snapshots");
            errorLogs.AssertNoErrors();
        }
    }
}

using System.Collections;
using Cysharp.Threading.Tasks;
using Mirage.Tests;
using Mirage.Tests.Runtime.Host;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// Without a <see cref="SyncPositionSystem"/>, host should interpolate client authority objects owned by remote clients
    /// </summary>
    [Category("NetworkPositionSync")]
    public class StandaloneHostTests : HostSetup<MockNetworkTransform>
    {
        private const float START_VALUE = 5;
        private const float FIRST_VALUE = 10;

        private ErrorLogCollector errorLogs;
        private ClientInstance _remoteClient;

        /// <summary>Character owned by remote client, on remote client</summary>
        private MockNetworkTransform _owner;
        /// <summary>Character owned by remote client, on host</summary>
        private MockNetworkTransform _onHost;

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

            _remoteClient = await AddClient();

            _owner = _remoteClient.character.GetComponent<MockNetworkTransform>();
            _onHost = ServerComponent(0);
            Assert.That(_onHost.NetId, Is.EqualTo(_owner.NetId));

            _owner.Value = START_VALUE;
            await AsyncUtil.WaitUntilWithTimeout(() => _onHost.Value == START_VALUE, Timeout);
        }

        [Test]
        public void SetupCorrectly()
        {
            Assert.That(server.IsHost, Is.True);
            Assert.That(_owner.HasAuthority, Is.True);
            Assert.That(_onHost.HasAuthority, Is.False);
            Assert.That(_onHost.Owner, Is.EqualTo(ServerPlayer(0)));

            Assert.That(_owner.IsStandalone, Is.True);
            Assert.That(_onHost.IsStandalone, Is.True);
            Assert.That(hostComponent.IsStandalone, Is.True);
            errorLogs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator RemoteOwnerChangeInterpolatesOnHost()
        {
            var tracker = new MonotonicTracker("host", () => _onHost.Value, FIRST_VALUE);

            _owner.Value = FIRST_VALUE;

            yield return tracker.WaitUntilReached(Timeout, () =>
            {
                Assert.That(_owner.Value, Is.EqualTo(FIRST_VALUE), "Owner value should not have changed");
                Assert.That(_owner._snapshotBuffer.SnapshotCount, Is.Zero, "Owner should not add snapshots to its buffer");
            });

            errorLogs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator RemoteOwnerContinuousMovementIsSmoothOnHost()
        {
            const float SPEED = 10;
            var duration = _owner._standaloneSyncInterval * 10;
            var endValue = START_VALUE + (SPEED * duration);

            var tracker = new MonotonicTracker("host", () => _onHost.Value, endValue);

            var start = Time.unscaledTime;
            while (Time.unscaledTime < start + duration)
            {
                _owner.Value = START_VALUE + (SPEED * (Time.unscaledTime - start));
                yield return null;
                tracker.Sample();
            }
            _owner.Value = endValue;

            yield return tracker.WaitUntilReached(Timeout, () =>
            {
                Assert.That(_owner._snapshotBuffer.SnapshotCount, Is.Zero, "Owner should not add snapshots to its buffer");
            });

            // 10 intervals of movement, so there should be atleast that many snapshots, and interpolation will add values between them
            Assert.That(tracker.DistinctValues, Is.GreaterThanOrEqualTo(10), "Host should have interpolated between snapshots");
            errorLogs.AssertNoErrors();
        }

        /// <summary>
        /// Host is owner of its character (with client authority), so host should send it to remote clients
        /// </summary>
        [UnityTest]
        public IEnumerator HostOwnedClientAuthorityObjectSyncsToRemote()
        {
            var onRemote = _remoteClient.Get(hostComponent);

            hostComponent.Value = START_VALUE;
            yield return SyncPositionTestHelpers.WaitUntil(() => onRemote.Value == START_VALUE, Timeout);
            Assert.That(onRemote.Value, Is.EqualTo(START_VALUE));

            var tracker = new MonotonicTracker("remote", () => onRemote.Value, FIRST_VALUE);
            hostComponent.Value = FIRST_VALUE;

            yield return tracker.WaitUntilReached(Timeout, () =>
            {
                Assert.That(hostComponent.Value, Is.EqualTo(FIRST_VALUE), "Host value should not have changed");
            });

            errorLogs.AssertNoErrors();
        }
    }
}

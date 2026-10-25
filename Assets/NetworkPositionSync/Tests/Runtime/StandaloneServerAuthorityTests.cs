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
    /// Without a <see cref="SyncPositionSystem"/>, server authority objects should sync themselves from server to clients
    /// </summary>
    [Category("NetworkPositionSync")]
    public class StandaloneServerAuthorityTests : ClientServerSetup<MockNetworkTransform>
    {
        private const float START_VALUE = 5;
        private const float FIRST_VALUE = 10;

        private ErrorLogCollector errorLogs;
        private SpawnedPair<MockNetworkTransform> _object;

        private float Timeout => StandaloneTestHelpers.Timeout(_object.Server);

        protected override void ExtraServerSetup()
        {
            base.ExtraServerSetup();
            errorLogs?.Dispose();
            errorLogs = new ErrorLogCollector();
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

            // object not owned by anyone, server authority
            var clone = InstantiateForTest(_characterPrefab);
            clone.name = "object (server)";
            serverObjectManager.Spawn(clone);
            var clientIdentity = await AsyncUtil.WaitUntilSpawn(client.World, clone.NetId);
            _object = new SpawnedPair<MockNetworkTransform>
            {
                Server = clone.GetComponent<MockNetworkTransform>(),
                Client = clientIdentity.GetComponent<MockNetworkTransform>(),
            };

            // only set on server, wait for it to be synced so that both start from the same value
            _object.Server.Value = START_VALUE;
            await AsyncUtil.WaitUntilWithTimeout(() => _object.Client.Value == START_VALUE, Timeout);
        }

        [Test]
        public void IsStandaloneOnServerAndClient()
        {
            Assert.That(_object.Server.IsStandalone, Is.True);
            Assert.That(_object.Client.IsStandalone, Is.True);
            Assert.That(serverComponent.IsStandalone, Is.True);
            Assert.That(clientComponent.IsStandalone, Is.True);
            Assert.That(serverGo.GetComponent<SyncPositionSystem>(), Is.Null);
            Assert.That(clientGo.GetComponent<SyncPositionSystem>(), Is.Null);
            errorLogs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator SyncsFromServerToClient()
        {
            Assert.That(_object.Client.Value, Is.EqualTo(START_VALUE));
            var tracker = new MonotonicTracker("client", () => _object.Client.Value, FIRST_VALUE);

            _object.Server.Value = FIRST_VALUE;

            yield return tracker.WaitUntilReached(Timeout, () =>
            {
                Assert.That(_object.Server.Value, Is.EqualTo(FIRST_VALUE), "Server value should not have changed");
            });

            errorLogs.AssertNoErrors();
        }

        /// <summary>
        /// Server moves the object every frame, client should follow it smoothly (many values in between, never moving backwards)
        /// </summary>
        [UnityTest]
        public IEnumerator ClientInterpolatesContinuousMovement()
        {
            const float SPEED = 10;
            var duration = _object.Server._standaloneSyncInterval * 10;
            var endValue = START_VALUE + (SPEED * duration);

            var tracker = new MonotonicTracker("client", () => _object.Client.Value, endValue);

            var start = Time.unscaledTime;
            while (Time.unscaledTime < start + duration)
            {
                _object.Server.Value = START_VALUE + (SPEED * (Time.unscaledTime - start));
                yield return null;
                tracker.Sample();
            }
            _object.Server.Value = endValue;

            yield return tracker.WaitUntilReached(Timeout);

            // 10 intervals of movement, so there should be atleast that many snapshots, and interpolation will add values between them
            Assert.That(tracker.DistinctValues, Is.GreaterThanOrEqualTo(10), "Client should have interpolated between snapshots");
            errorLogs.AssertNoErrors();
        }

        /// <summary>
        /// Owner of server authority object should follow server like any other client
        /// </summary>
        [UnityTest]
        public IEnumerator SyncsToOwnerOfServerAuthorityObject()
        {
            serverComponent.Value = START_VALUE;
            yield return SyncPositionTestHelpers.WaitUntil(() => clientComponent.Value == START_VALUE, Timeout);
            Assert.That(clientComponent.Value, Is.EqualTo(START_VALUE));

            var tracker = new MonotonicTracker("owner", () => clientComponent.Value, FIRST_VALUE);
            serverComponent.Value = FIRST_VALUE;
            yield return tracker.WaitUntilReached(Timeout);

            errorLogs.AssertNoErrors();
        }
    }
}

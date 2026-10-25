using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using Mirage.SocketLayer;
using Mirage.Tests;
using Mirage.Tests.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// Without a <see cref="SyncPositionSystem"/>, server should only apply CmdSnapshot from the owner of a behaviour that uses client authority
    /// </summary>
    [Category("NetworkPositionSync")]
    public class StandaloneRejectsUnauthorizedTests : ClientServerSetup<MockNetworkTransform>
    {
        private const float START_VALUE = 5;
        private const float CLIENT_VALUE = 10;
        private const float SENT_VALUE = 20;
        private const float SENT_VALUE_2 = 30;

        private ErrorLogCollector errorLogs;

        private float Timeout => StandaloneTestHelpers.Timeout(serverComponent);

        protected override void ExtraServerSetup()
        {
            base.ExtraServerSetup();
            errorLogs?.Dispose();
            errorLogs = new ErrorLogCollector();

            // refill very slowly so that tokens used by errors can be checked without them being refilled
            server.ErrorRateLimitConfig = new RateLimitBucket.RefillConfig
            {
                MaxTokens = 200,
                Refill = 1,
                Interval = 600,
            };
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

            // character is owned by client but uses server authority (prefab default)
            Assert.That(serverComponent._clientAuthority, Is.False);
            Assert.That(clientComponent._clientAuthority, Is.False);

            serverComponent.Value = START_VALUE;
            await AsyncUtil.WaitUntilWithTimeout(() => clientComponent.Value == START_VALUE, Timeout);
        }

        private float Tokens => serverPlayer.ErrorRateLimit.Tokens;
        private int MaxTokens => serverPlayer.ErrorRateLimit.Config.MaxTokens;

        private void AssertNoAuthorityErrors()
        {
            Assert.That(serverPlayer.ErrorFlags & PlayerErrorFlags.NoAuthority, Is.EqualTo(PlayerErrorFlags.None), "Player should not have NoAuthority error");
            Assert.That(Tokens, Is.EqualTo(MaxTokens), "Player should not have used any error tokens");
        }

        private void AssertStillConnected()
        {
            Assert.That(client.IsConnected, Is.True, "Client should still be connected");
            Assert.That(server.AllPlayers, Has.Member(serverPlayer), "Server should still have player");
        }

        [UnityTest]
        public IEnumerator OwnerWithoutClientAuthority_ChangeIsNotSentToServer()
        {
            Assert.That(clientComponent.HasAuthority, Is.True);

            clientComponent.Value = CLIENT_VALUE;
            yield return StandaloneTestHelpers.WaitForSyncs(serverComponent, 10);

            Assert.That(serverComponent.Value, Is.EqualTo(START_VALUE), "Server value should not have changed");
            // client should not even try to send, so no error on server
            AssertNoAuthorityErrors();
            AssertStillConnected();
            errorLogs.AssertNoErrors();
        }

        /// <summary>
        /// Client is the owner (so Mirage allows the ServerRpc) but behaviour does not use client authority
        /// </summary>
        [UnityTest]
        public IEnumerator CmdSnapshotFromOwnerWithoutClientAuthority_IsRejectedByServer()
        {
            Assert.That(clientComponent.HasAuthority, Is.True);
            AssertNoAuthorityErrors();

            StandaloneTestHelpers.InvokeCmdSnapshot(clientComponent, Time.unscaledTimeAsDouble, StandaloneTestHelpers.CreateMockPayload(SENT_VALUE));

            yield return SyncPositionTestHelpers.WaitUntil(() => (serverPlayer.ErrorFlags & PlayerErrorFlags.NoAuthority) != 0, Timeout);
            Assert.That(serverPlayer.ErrorFlags & PlayerErrorFlags.NoAuthority, Is.EqualTo(PlayerErrorFlags.NoAuthority), "Server should set NoAuthority error on player");
            Assert.That(Tokens, Is.EqualTo(MaxTokens - NetworkTransformBase.NO_AUTHORITY_ERROR_COST), "Error should use tokens from player's error rate limit");

            // wait extra time incase server applies it later (eg interpolation)
            yield return StandaloneTestHelpers.WaitForSyncs(serverComponent, 5);

            Assert.That(serverComponent.Value, Is.EqualTo(START_VALUE), "Server value should not have changed");
            Assert.That(Tokens, Is.EqualTo(MaxTokens - NetworkTransformBase.NO_AUTHORITY_ERROR_COST), "Only 1 error should have been set");
            AssertStillConnected();
            errorLogs.AssertNoErrors();
        }

        /// <summary>
        /// Mirage's requireAuthority check stops non-owner sending the ServerRpc
        /// </summary>
        [UnityTest]
        public IEnumerator CmdSnapshotFromNonOwner_IsRejected()
        {
            var clone = InstantiateForTest(_characterPrefab);
            serverObjectManager.Spawn(clone);
            var serverObject = clone.GetComponent<MockNetworkTransform>();
            // use client authority on server, so that only ownership stops the value being applied
            serverObject._clientAuthority = true;
            serverObject.Value = START_VALUE;

            NetworkIdentity clientIdentity = null;
            yield return SyncPositionTestHelpers.WaitUntil(() => client.World.TryGetIdentity(clone.NetId, out clientIdentity), Timeout);
            Assert.That(clientIdentity, Is.Not.Null, "Object should have spawned on client");
            var clientObject = clientIdentity.GetComponent<MockNetworkTransform>();
            Assert.That(clientObject.HasAuthority, Is.False);

            Assert.Throws<InvalidOperationException>(() =>
            {
                StandaloneTestHelpers.InvokeCmdSnapshot(clientObject, Time.unscaledTimeAsDouble, StandaloneTestHelpers.CreateMockPayload(SENT_VALUE));
            }, "Mirage should not let client send ServerRpc for object it does not own");

            yield return StandaloneTestHelpers.WaitForSyncs(serverObject, 5);

            Assert.That(serverObject.Value, Is.EqualTo(START_VALUE), "Server value should not have changed");
            // rejected on client, so server never received it
            AssertNoAuthorityErrors();
            AssertStillConnected();
            errorLogs.AssertNoErrors();
        }

        /// <summary>
        /// Checks that <see cref="StandaloneTestHelpers.InvokeCmdSnapshot"/> does reach the server, so that the tests above are valid
        /// </summary>
        [UnityTest]
        public IEnumerator CmdSnapshotFromOwnerWithClientAuthority_IsApplied()
        {
            // only server copy, so that client doesn't send its own snapshots
            serverComponent._clientAuthority = true;

            StandaloneTestHelpers.InvokeCmdSnapshot(clientComponent, 100, StandaloneTestHelpers.CreateMockPayload(SENT_VALUE));

            yield return SyncPositionTestHelpers.WaitUntil(() => serverComponent.Value == SENT_VALUE, Timeout);
            Assert.That(serverComponent.Value, Is.EqualTo(SENT_VALUE), "Server should apply value from owner with client authority");
            AssertNoAuthorityErrors();
            AssertStillConnected();
            errorLogs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator CmdSnapshot_DropsOutOfOrderMessages()
        {
            serverComponent._clientAuthority = true;

            StandaloneTestHelpers.InvokeCmdSnapshot(clientComponent, 100, StandaloneTestHelpers.CreateMockPayload(SENT_VALUE));
            yield return SyncPositionTestHelpers.WaitUntil(() => serverComponent.Value == SENT_VALUE, Timeout);
            Assert.That(serverComponent.Value, Is.EqualTo(SENT_VALUE));

            // older and same time should be ignored
            StandaloneTestHelpers.InvokeCmdSnapshot(clientComponent, 50, StandaloneTestHelpers.CreateMockPayload(SENT_VALUE_2));
            StandaloneTestHelpers.InvokeCmdSnapshot(clientComponent, 100, StandaloneTestHelpers.CreateMockPayload(SENT_VALUE_2));
            yield return StandaloneTestHelpers.WaitForSyncs(serverComponent, 5);
            Assert.That(serverComponent.Value, Is.EqualTo(SENT_VALUE), "Server should ignore messages with older or same client time");

            // newer should be applied
            StandaloneTestHelpers.InvokeCmdSnapshot(clientComponent, 101, StandaloneTestHelpers.CreateMockPayload(SENT_VALUE_2));
            yield return SyncPositionTestHelpers.WaitUntil(() => serverComponent.Value == SENT_VALUE_2, Timeout);
            Assert.That(serverComponent.Value, Is.EqualTo(SENT_VALUE_2), "Server should apply message with newer client time");

            AssertStillConnected();
            errorLogs.AssertNoErrors();
        }
    }
}

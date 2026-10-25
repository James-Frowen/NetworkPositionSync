using Cysharp.Threading.Tasks;
using Mirage.Serialization;
using Mirage.Tests;
using Mirage.Tests.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// Server should only apply positions from a client for behaviours that use client authority and are owned by that client
    /// </summary>
    [Category("NetworkPositionSync")]
    public class ServerRejectsUnauthorizedPositionTests : MultiRemoteClientSetup<MockNetworkTransform>
    {
        private const float START_VALUE = 5;
        private const float SENT_VALUE = 10;
        private const float SENT_VALUE_2 = 20;

        protected override int RemoteClientCount => 2;

        private SyncPositionSystem serverSystem;
        private ErrorLogCollector errorLogs;

        /// <summary>extra object owned by client 0 that uses client authority</summary>
        private MockNetworkTransform _serverExtraOwnedBy0;

        protected override void ExtraServerSetup()
        {
            errorLogs?.Dispose();
            errorLogs = new ErrorLogCollector();
            serverSystem = SyncPositionTestHelpers.AddSystem(serverGo, server, null, SyncPositionTestHelpers.DefaultSettings);
        }

        protected override void ExtraClientSetup(IClientInstance instance)
        {
            SyncPositionTestHelpers.AddSystem(instance.GameObject, null, instance.Client, SyncPositionTestHelpers.DefaultSettings);
        }

        public override void ExtraTearDown()
        {
            errorLogs?.Dispose();
            errorLogs = null;
        }

        protected override async UniTask LateSetup()
        {
            await base.LateSetup();

            var clone = InstantiateForTest(_characterPrefab);
            serverObjectManager.Spawn(clone, ServerPlayer(0));
            _serverExtraOwnedBy0 = clone.GetComponent<MockNetworkTransform>();
            foreach (var remote in _remoteClients)
                await AsyncUtil.WaitUntilSpawn(remote.Client.World, clone.NetId);

            // only server copies have client authority set, client copies will not send anything.
            // That way only the messages created by the tests reach the server
            _serverExtraOwnedBy0._clientAuthority = true;
            _serverExtraOwnedBy0.Value = START_VALUE;
            for (var i = 0; i < RemoteClientCount; i++)
            {
                ServerComponent(i)._clientAuthority = true;
                ServerComponent(i).Value = START_VALUE;
            }
        }

        private static void WriteEntry(NetworkWriter metaWriter, NetworkWriter dataWriter, NetworkBehaviour behaviour, float value, bool includeWriteSize)
        {
            // same format as NetworkTransformBase.WriteIfDirty
            metaWriter.WriteNetworkBehaviour(behaviour);
            var start = dataWriter.BitPosition;
            dataWriter.Write(new MockNetworkTransform.Data { Value = value });
            if (includeWriteSize)
            {
                var size = dataWriter.BitPosition - start;
                metaWriter.WritePackedUInt32((uint)size);
            }
        }

        /// <summary>
        /// Creates message with entries and passes it to server as if it came from <paramref name="sender"/>
        /// </summary>
        private void ReceiveFromClient(INetworkPlayer sender, bool includeWriteSize, params (NetworkBehaviour behaviour, float value)[] entries)
        {
            serverSystem._includeWriteSize = includeWriteSize;

            using (PooledNetworkWriter metaWriter = NetworkWriterPool.GetWriter(), dataWriter = NetworkWriterPool.GetWriter())
            {
                foreach (var entry in entries)
                    WriteEntry(metaWriter, dataWriter, entry.behaviour, entry.value, includeWriteSize);

                var msg = new SyncPositionSystem.PositionMessage
                {
                    Time = Time.unscaledTimeAsDouble,
                    MetaPayload = metaWriter.ToArraySegment(),
                    DataPayload = dataWriter.ToArraySegment(),
                };
                serverSystem.ServerHandleNetworkPositionMessage(sender, msg);
            }
        }

        [Test]
        public void AppliesPositionFromOwnerWithClientAuthority()
        {
            ReceiveFromClient(ServerPlayer(0), true, (ServerComponent(0), SENT_VALUE));

            Assert.That(ServerComponent(0).Value, Is.EqualTo(SENT_VALUE));
            errorLogs.AssertNoErrors();
        }

        [Test]
        public void RejectsPositionForObjectOwnedByAnotherClient([Values(true, false)] bool otherIsFirst)
        {
            var other = ServerComponent(1);
            var own = ServerComponent(0);

            // valid entry should still be applied if it is before or after rejected entry
            if (otherIsFirst)
                ReceiveFromClient(ServerPlayer(0), true, (other, SENT_VALUE), (own, SENT_VALUE_2));
            else
                ReceiveFromClient(ServerPlayer(0), true, (own, SENT_VALUE_2), (other, SENT_VALUE));

            Assert.That(other.Value, Is.EqualTo(START_VALUE), "Object owned by other client should not have changed");
            Assert.That(own.Value, Is.EqualTo(SENT_VALUE_2), "Valid entry in same message should still be applied");
            errorLogs.AssertNoErrors();
        }

        [Test]
        public void RejectsPositionForOwnObjectWithoutClientAuthority([Values(true, false)] bool invalidIsFirst)
        {
            var noAuthority = ServerComponent(0);
            noAuthority._clientAuthority = false;
            var withAuthority = _serverExtraOwnedBy0;

            if (invalidIsFirst)
                ReceiveFromClient(ServerPlayer(0), true, (noAuthority, SENT_VALUE), (withAuthority, SENT_VALUE_2));
            else
                ReceiveFromClient(ServerPlayer(0), true, (withAuthority, SENT_VALUE_2), (noAuthority, SENT_VALUE));

            Assert.That(noAuthority.Value, Is.EqualTo(START_VALUE), "Object without client authority should not have changed");
            Assert.That(withAuthority.Value, Is.EqualTo(SENT_VALUE_2), "Valid entry in same message should still be applied");
            errorLogs.AssertNoErrors();
        }

        [Test]
        public void RejectsPositionForServerObject()
        {
            var clone = InstantiateForTest(_characterPrefab);
            serverObjectManager.Spawn(clone);
            var serverObject = clone.GetComponent<MockNetworkTransform>();
            serverObject._clientAuthority = true;
            serverObject.Value = START_VALUE;

            ReceiveFromClient(ServerPlayer(0), true, (serverObject, SENT_VALUE));

            Assert.That(serverObject.Value, Is.EqualTo(START_VALUE), "Object without owner should not have changed");
            errorLogs.AssertNoErrors();
        }

        [Test]
        public void WithoutWriteSize_RejectsOtherClientsObject()
        {
            var other = ServerComponent(1);
            var own = ServerComponent(0);

            // without write size server can't skip rejected entry, so put valid entry first
            ReceiveFromClient(ServerPlayer(0), false, (own, SENT_VALUE_2), (other, SENT_VALUE));

            Assert.That(other.Value, Is.EqualTo(START_VALUE), "Object owned by other client should not have changed");
            Assert.That(own.Value, Is.EqualTo(SENT_VALUE_2), "Valid entry before rejected entry should be applied");
            errorLogs.AssertNoErrors();
        }

        [Test]
        public void WithoutWriteSize_RejectsOwnObjectWithoutClientAuthority()
        {
            var noAuthority = ServerComponent(0);
            noAuthority._clientAuthority = false;
            var withAuthority = _serverExtraOwnedBy0;

            ReceiveFromClient(ServerPlayer(0), false, (withAuthority, SENT_VALUE_2), (noAuthority, SENT_VALUE));

            Assert.That(noAuthority.Value, Is.EqualTo(START_VALUE), "Object without client authority should not have changed");
            Assert.That(withAuthority.Value, Is.EqualTo(SENT_VALUE_2), "Valid entry before rejected entry should be applied");
            errorLogs.AssertNoErrors();
        }
    }
}

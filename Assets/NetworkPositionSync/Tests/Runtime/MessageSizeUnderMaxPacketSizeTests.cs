using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Mirage.Serialization;
using Mirage.SocketLayer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// Behaviour with a large snapshot where <see cref="MaxWriteSize"/> is exactly the size written.
    /// <para>
    /// NetworkTransform3D/2D over estimate MaxWriteSize, which hides problems with message size calculation.
    /// This size was picked so that the old size calculation (4 byte time, no batch header) filled a message to 1298 bytes,
    /// which is too big for Mirage's unreliable batch with MaxPacketSize=1300
    /// </para>
    /// </summary>
    public class LargeSnapshotNetworkTransform : NetworkTransformBase<float>
    {
        public const int SNAPSHOT_SIZE = 253;
        private const int PADDING_SIZE = SNAPSHOT_SIZE - 4;
        private static readonly byte[] padding = new byte[PADDING_SIZE];

        public float Value;

        public override int MaxWriteSize => SNAPSHOT_SIZE;

        protected override bool CreateSnapshot(out float newSnapshot, bool force)
        {
            newSnapshot = Value;
            return force || newSnapshot != _snapshot;
        }

        protected override void ApplySnapshot(float newSnapshot)
        {
            Value = newSnapshot;
        }

        protected override void WriteSnapshot(NetworkWriter writer, float snapshot)
        {
            writer.Write(snapshot);
            writer.WriteBytes(padding, 0, PADDING_SIZE);
        }

        protected override float ReadSnapshot(NetworkReader reader)
        {
            var value = reader.Read<float>();
            reader.Skip(PADDING_SIZE * 8);
            return value;
        }

        protected override ISnapshotInterpolator<float> CreateInterpolator() => new Interpolator();

        private class Interpolator : ISnapshotInterpolator<float>
        {
            public float Interpolate(double from, float a, double to, float b, float alpha) => Mathf.Lerp(a, b, alpha);
        }
    }

    /// <summary>
    /// Messages should never be bigger than Mirage allows for unreliable messages, and all objects should still be received.
    /// <para>
    /// Note: Mirage 149's NetworkServer.SendToAll(authenticatedOnly: true) ignores the channel and sends reliable (which can fragment),
    /// so the unreliable paths are tested: client to server, and server using SendToObservers.
    /// </para>
    /// </summary>
    public abstract class MessageSizeTestBase : SyncPositionTestBase<LargeSnapshotNetworkTransform>
    {
        // enough that objects need multiple messages
        protected const int OBJECT_COUNT = 10;

        protected override bool SpawnCharacterOnConnect => false;

        protected List<SpawnedPair<LargeSnapshotNetworkTransform>> _objects;
        private readonly List<int> _sentSizes = new List<int>();

        protected abstract INetworkPlayer Owner { get; }

        protected override void ExtraPrefabSetup(NetworkIdentity prefab)
        {
            base.ExtraPrefabSetup(prefab);
            // only used when objects have owner
            prefab.GetComponent<LargeSnapshotNetworkTransform>()._clientAuthority = true;
        }

        protected override async UniTask LateSetup()
        {
            await base.LateSetup();
            _objects = await SpawnObjects(OBJECT_COUNT, Owner);

            _sentSizes.Clear();
            NetworkDiagnostics.OutMessageEvent += OnOutMessage;
        }

        public override void ExtraTearDown()
        {
            NetworkDiagnostics.OutMessageEvent -= OnOutMessage;
            base.ExtraTearDown();
        }

        private void OnOutMessage(NetworkDiagnostics.MessageInfo info)
        {
            if (info.message is SyncPositionSystem.PositionMessage)
                _sentSizes.Add(info.bytes);
        }

        [Test]
        public void ObjectsNeedMoreThanOnePacket()
        {
            var maxPacketSize = server.SocketFactory.MaxPacketSize;
            Assert.That(LargeSnapshotNetworkTransform.SNAPSHOT_SIZE * OBJECT_COUNT, Is.GreaterThan(maxPacketSize), "Test setup should need more than 1 message");
        }

        private bool AllReceived(bool fromClient)
        {
            for (var i = 0; i < _objects.Count; i++)
            {
                var receiver = fromClient ? _objects[i].Server : _objects[i].Client;
                if (receiver.Value != 1000 + i)
                    return false;
            }
            return true;
        }

        protected IEnumerator SetValuesAndCheckReceived(bool fromClient)
        {
            // set all at once, so they are all sent in the same sync
            for (var i = 0; i < _objects.Count; i++)
            {
                var sender = fromClient ? _objects[i].Client : _objects[i].Server;
                sender.Value = 1000 + i;
            }

            yield return SyncPositionTestHelpers.WaitUntil(() => AllReceived(fromClient), Timeout);

            for (var i = 0; i < _objects.Count; i++)
            {
                var receiver = fromClient ? _objects[i].Server : _objects[i].Client;
                Assert.That(receiver.Value, Is.EqualTo(1000 + i), $"Object {i} should have been received");
            }

            // Mirage adds batch header to unreliable messages, see ReliableConnection.SendUnreliable
            var maxMessageSize = server.SocketFactory.MaxPacketSize - (1 + Batch.MESSAGE_LENGTH_SIZE);
            Assert.That(_sentSizes, Is.Not.Empty);
            foreach (var size in _sentSizes)
                Assert.That(size, Is.LessThanOrEqualTo(maxMessageSize), $"Message sizes: [{string.Join(", ", _sentSizes)}]");

            errorLogs.AssertNoErrors();
        }
    }

    [Category("NetworkPositionSync")]
    public class MessageSizeUnderMaxPacketSize_ClientToServerTests : MessageSizeTestBase
    {
        // objects owned by client using client authority
        protected override INetworkPlayer Owner => serverPlayer;

        [UnityTest]
        public IEnumerator AllObjectsReachServerWithoutErrors()
        {
            yield return SetValuesAndCheckReceived(fromClient: true);
        }
    }

    [Category("NetworkPositionSync")]
    public class MessageSizeUnderMaxPacketSize_SendToObserversTests : MessageSizeTestBase
    {
        public MessageSizeUnderMaxPacketSize_SendToObserversTests()
        {
            // SendToObservers sends unreliable to each player
            Settings.SyncMode = SyncPositionSystem.SyncMode.SendToObservers;
        }

        // server objects
        protected override INetworkPlayer Owner => null;

        [UnityTest]
        public IEnumerator AllObjectsReachClientWithoutErrors()
        {
            yield return SetValuesAndCheckReceived(fromClient: false);
        }
    }
}

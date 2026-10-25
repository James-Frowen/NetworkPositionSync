using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// When the server first starts, a sync of many small objects should fit in a single message
    /// <para>Checks that MaxPacketSize is set before the sender is created</para>
    /// </summary>
    [Category("NetworkPositionSync")]
    public class SingleMessagePerSyncTests : SyncPositionTestBase<NetworkTransform3D>
    {
        private const int OBJECT_COUNT = 10;

        // no characters, so only server sends position messages
        protected override bool SpawnCharacterOnConnect => false;

        private List<SpawnedPair<NetworkTransform3D>> _objects;

        /// <summary>number of position messages received by client for each sync, using message time as key</summary>
        private readonly Dictionary<double, int> _messagesPerSync = new Dictionary<double, int>();
        private int _messagesWithData;

        protected override async UniTask LateSetup()
        {
            await base.LateSetup();
            _objects = await SpawnObjects(OBJECT_COUNT);

            _messagesPerSync.Clear();
            _messagesWithData = 0;
            NetworkDiagnostics.InMessageEvent += OnInMessage;
        }

        public override void ExtraTearDown()
        {
            NetworkDiagnostics.InMessageEvent -= OnInMessage;
            base.ExtraTearDown();
        }

        private void OnInMessage(NetworkDiagnostics.MessageInfo info)
        {
            // client never sends in this test (it owns nothing), so all position messages are server -> client
            if (info.message is SyncPositionSystem.PositionMessage msg)
            {
                _messagesPerSync.TryGetValue(msg.Time, out var count);
                _messagesPerSync[msg.Time] = count + 1;

                if (msg.MetaPayload.Count > 0)
                    _messagesWithData++;
            }
        }

        [UnityTest]
        public IEnumerator ManyMovingObjectsAreSentInOneMessage()
        {
            // move every object each frame, so all of them are written every sync
            var end = Time.unscaledTime + (Settings.SyncInterval * 5);
            var frame = 0;
            while (Time.unscaledTime < end)
            {
                frame++;
                for (var i = 0; i < _objects.Count; i++)
                    _objects[i].Server.transform.position = new Vector3(i, frame * 0.1f, 0);

                yield return null;
            }
            // let last messages arrive
            yield return null;
            yield return null;

            Assert.That(_messagesWithData, Is.GreaterThan(0), "Client should have received position data");
            foreach (var kvp in _messagesPerSync)
            {
                Assert.That(kvp.Value, Is.EqualTo(1), $"Sync at time {kvp.Key:0.000} should have been sent in a single message. Messages per sync: [{string.Join(", ", _messagesPerSync.Values)}]");
            }
            errorLogs.AssertNoErrors();
        }
    }
}

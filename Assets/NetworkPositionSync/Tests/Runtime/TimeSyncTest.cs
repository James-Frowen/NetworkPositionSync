using Mirage.Serialization;
using NUnit.Framework;

namespace Mirage.SyncPosition.Tests.Runtime
{
    [Category("NetworkPositionSync")]
    public class TimeSyncTest
    {
        /// <summary>
        /// Time is sent as a double, so should be exactly the same after it is read
        /// </summary>
        [Test]
        public void TimeSerialzesCorrectly([Range(0, 3600 * 24 * 30, 1000)] double time)
        {
            var writer = new NetworkWriter(1200);
            var payload = new byte[] { 1, 2, 3 };

            writer.Write(new SyncPositionSystem.PositionMessage
            {
                Time = time,
                MetaPayload = new System.ArraySegment<byte>(payload),
                DataPayload = new System.ArraySegment<byte>(payload),
            });

            var reader = new NetworkReader();
            reader.Reset(writer.ToArraySegment());

            var outMsg = reader.Read<SyncPositionSystem.PositionMessage>();

            Assert.That(outMsg.Time, Is.EqualTo(time));
            Assert.That(outMsg.MetaPayload, Is.EquivalentTo(payload));
            Assert.That(outMsg.DataPayload, Is.EquivalentTo(payload));
        }
    }
}

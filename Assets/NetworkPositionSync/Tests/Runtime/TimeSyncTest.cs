using Mirage.Serialization;
using NUnit.Framework;
using UnityEngine;

namespace JamesFrowen.PositionSync.Tests
{
    [Category("NetworkPositionSync")]
    public class TimeSyncTest : MonoBehaviour
    {
        [Test]
        public void TimeSerialzesCorrectly([NUnit.Framework.Range(0, 3600 * 24 * 30, 1000)] double time)
        {
            var settings = new SyncSettings();
            var writer = new NetworkWriter(1200);

            var packer = settings.CreateTimePacker();

            packer.Pack(writer, time);

            var reader = new NetworkReader();
            reader.Reset(writer.ToArraySegment());

            var outTime = packer.Unpack(reader);

            // setting should be sending 0.1ms, so should be within that
            Assert.That(outTime, Is.EqualTo(time).Within(1 / 10000f));
        }
    }
}

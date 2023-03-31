using System;
using Mirage.Logging;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RangeAttribute = NUnit.Framework.RangeAttribute;

namespace Mirage.SyncPosition.Tests.SnapshotBufferTests
{
    public class SnapshotBufferTestBase
    {
        public static SnapshotBuffer<Snapshot> CreateBuffer()
        {
            return new SnapshotBuffer<Snapshot>(Snapshot.CreateInterpolator());
        }

        public struct Snapshot
        {
            public readonly Vector3 position;
            public readonly Quaternion rotation;

            public Snapshot(Vector3 position, Quaternion rotation)
            {
                this.position = position;
                this.rotation = rotation;
            }

            public override string ToString()
            {
                return $"[{position}, {rotation}]";
            }

            public static ISnapshotInterpolator<Snapshot> CreateInterpolator() => new Interpolator();

            private class Interpolator : ISnapshotInterpolator<Snapshot>
            {
                public Snapshot Lerp(Snapshot a, Snapshot b, float alpha)
                {
                    var pos = Vector3.Lerp(a.position, b.position, alpha);
                    var rot = Quaternion.Slerp(a.rotation, b.rotation, alpha);
                    return new Snapshot(pos, rot);
                }
            }
        }
    }
    [Category("NetworkPositionSync")]
    public class SnapshotBuffer_IsEmpty : SnapshotBufferTestBase
    {

        [Test]
        public void ShouldBeTrueOnNewBuffer()
        {
            var buffer = CreateBuffer();
            Assert.That(buffer.IsEmpty, Is.True);
        }

        [Test]
        public void ShouldBeFalseAfterAddingToBuffer()
        {
            var buffer = CreateBuffer();
            buffer.AddSnapShot(default, default);
            Assert.That(buffer.IsEmpty, Is.False);
        }

        [Test]
        public void ShouldBeTrueAfterRemovingFromBuffer()
        {
            var buffer = CreateBuffer();
            buffer.AddSnapShot(default, 0);
            buffer.RemoveOldSnapshots(1);
            Assert.That(buffer.IsEmpty, Is.True);
        }
    }

    [Category("NetworkPositionSync")]
    public class SnapshotBuffer_RemoveSnapshots : SnapshotBufferTestBase
    {
        [Test]
        public void ShouldOnlyRemoveOld()
        {
            var buffer = CreateBuffer();
            buffer.AddSnapShot(default, 0);
            buffer.AddSnapShot(default, 1);
            Assert.That(buffer.SnapshotCount, Is.EqualTo(2));

            buffer.RemoveOldSnapshots(0.5f);
            Assert.That(buffer.SnapshotCount, Is.EqualTo(1));

            buffer.RemoveOldSnapshots(1.5f);
            Assert.That(buffer.SnapshotCount, Is.EqualTo(0));
        }
    }

    [Category("NetworkPositionSync")]
    public class SnapshotBuffer_AddSnapshot : SnapshotBufferTestBase
    {
        [Test]
        public void ShouldIncreaseCount([Range(1, 5)] int count)
        {
            var buffer = CreateBuffer();
            for (var i = 0; i < count; i++)
            {
                Assert.That(buffer.SnapshotCount, Is.EqualTo(i));
                buffer.AddSnapShot(default, i);
                Assert.That(buffer.SnapshotCount, Is.EqualTo(i + 1));
            }
        }

        [Test]
        public void ShouldGiveErrorIfOutOfOrder()
        {
            const float time1 = 1.1f;
            const float time2 = 0.9f;

            var buffer = CreateBuffer();
            buffer.AddSnapShot(default, time1);

            var exception = Assert.Throws<ArgumentException>(() => buffer.AddSnapShot(default, time2));
            Assert.That(exception, Has.Message.EqualTo($"Can not add snapshot to buffer. This would cause the buffer to be out of order. Last t={time1:0.000}, new t={time2:0.000}"));
        }
    }

    [Category("NetworkPositionSync")]
    public class SnapshotBuffer_GetInterpolation : SnapshotBufferTestBase
    {
        [Test]
        public void ShouldGiveErrorWhenEmpty()
        {
            var buffer = CreateBuffer();

            var exception = Assert.Throws<InvalidOperationException>(() => buffer.GetLinearInterpolation(default));
            Assert.That(exception, Has.Message.EqualTo("No snapshots in buffer."));
        }

        [Test]
        public void ShouldReturnFirstIfOnly1Snapshot([Range(0, 10, 1f)] float now)
        {
            var state = new Snapshot(Vector3.one, Quaternion.identity);
            var buffer = CreateBuffer();
            buffer.AddSnapShot(state, default);

            using (new SetLogLevel<SnapshotBuffer<Snapshot>>(LogType.Log))
            {
                LogAssert.Expect(LogType.Log, "First snapshot");
                var value = buffer.GetLinearInterpolation(now);
                Assert.That(value, Is.EqualTo(state));
            }
        }

        [Test]
        public void ShouldReturnFirstIfNowIsBeforeFirst([Range(0, 1, 0.1f)] float now)
        {
            const float time1 = 1.1f;
            const float time2 = 1.5f;
            var state1 = new Snapshot(Vector3.one, Quaternion.identity);
            var state2 = new Snapshot(Vector3.one * 2, Quaternion.identity);

            var buffer = CreateBuffer();
            buffer.AddSnapShot(state1, time1);
            buffer.AddSnapShot(state2, time2);

            using (new SetLogLevel<SnapshotBuffer<Snapshot>>(LogType.Log))
            {
                LogAssert.Expect(LogType.Log, $"No snapshots for t = {now:0.000}, using earliest t = {time1:0.000}");
                var value = buffer.GetLinearInterpolation(now);
                Assert.That(value, Is.EqualTo(state1));
            }
        }

        [Test]
        public void ShouldReturnLastIfNowIsAfterLast([Range(1.6f, 2.6f, 0.1f)] float now)
        {
            const float time1 = 1.1f;
            const float time2 = 1.5f;
            var state1 = new Snapshot(Vector3.one, Quaternion.identity);
            var state2 = new Snapshot(Vector3.one * 2, Quaternion.identity);

            var buffer = CreateBuffer();
            buffer.AddSnapShot(state1, time1);
            buffer.AddSnapShot(state2, time2);

            using (new SetLogLevel<SnapshotBuffer<Snapshot>>(LogType.Log))
            {
                LogAssert.Expect(LogType.Log, $"No snapshots for t = {now:0.000}, using first t = {time1:0.000}, last t = {time2:0.000}");
                var value = buffer.GetLinearInterpolation(now);
                Assert.That(value, Is.EqualTo(state2));
            }
        }

        [Test]
        public void ShouldReturnInterpolationForNowBetween2Snapshots([Range(1f, 2f, 0.1f)] float now)
        {
            const float time1 = 1f;
            const float time2 = 2f;
            var state1 = new Snapshot(Vector3.one, Quaternion.identity);
            var state2 = new Snapshot(Vector3.one * 2, Quaternion.identity);
            var expected = new Snapshot(Vector3.Lerp(state1.position, state2.position, now - time1), Quaternion.identity);

            var buffer = CreateBuffer();
            buffer.AddSnapShot(state1, time1);
            buffer.AddSnapShot(state2, time2);

            var value = buffer.GetLinearInterpolation(now);
            Assert.That(value, Is.EqualTo(expected));
        }
    }

    [Category("NetworkPositionSync")]
    public class SnapshotBuffer_DebugToString : SnapshotBufferTestBase
    {
        [Test]
        public void ShouldSayEmptyBuffer()
        {
            var buffer = CreateBuffer();
            var str = buffer.ToDebugString(0);
            Assert.That(str, Is.EqualTo("Buffer Empty"));
        }
        [Test]
        [Ignore("NotImplemented")]
        public void ShouldListSnapshotsInBuffer()
        {
            var buffer = CreateBuffer();
            var str = buffer.ToString();
            //Assert.Ignore("NotImplemented");
        }
    }

    internal struct SetLogLevel<T> : IDisposable
    {
        private ILogger logger;
        private LogType startingLevel;

        public SetLogLevel(LogType targetLevel)
        {
            logger = LogFactory.GetLogger<T>();
            startingLevel = logger.filterLogType;
            logger.filterLogType = targetLevel;
        }
        public void Dispose()
        {
            logger.filterLogType = startingLevel;
        }
    }
}

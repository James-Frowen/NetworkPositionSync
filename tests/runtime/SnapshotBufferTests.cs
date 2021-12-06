using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RangeAttribute = NUnit.Framework.RangeAttribute;

namespace JamesFrowen.PositionSync.Tests.SnapshotBufferTests
{
    public class SnapshotBufferTestBase
    {
        public static SnapshotBuffer<TransformState> CreateBuffer()
        {
            return new SnapshotBuffer<TransformState>(TransformState.CreateInterpolator());
        }
    }
    [Category("NetworkPositionSync")]
    public class SnapshotBuffer_IsEmpty : SnapshotBufferTestBase
    {

        [Test]
        public void ShouldBeTrueOnNewBuffer()
        {
            SnapshotBuffer<TransformState> buffer = CreateBuffer();
            Assert.That(buffer.IsEmpty, Is.True);
        }

        [Test]
        public void ShouldBeFalseAfterAddingToBuffer()
        {
            SnapshotBuffer<TransformState> buffer = CreateBuffer();
            buffer.AddSnapShot(default, default);
            Assert.That(buffer.IsEmpty, Is.False);
        }

        [Test]
        public void ShouldBeTrueAfterRemovingFromBuffer()
        {
            SnapshotBuffer<TransformState> buffer = CreateBuffer();
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
            SnapshotBuffer<TransformState> buffer = CreateBuffer();
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
            SnapshotBuffer<TransformState> buffer = CreateBuffer();
            for (int i = 0; i < count; i++)
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

            SnapshotBuffer<TransformState> buffer = CreateBuffer();
            buffer.AddSnapShot(default, time1);

            ArgumentException exception = Assert.Throws<ArgumentException>(() => buffer.AddSnapShot(default, time2));
            Assert.That(exception, Has.Message.EqualTo($"Can not add Snapshot to buffer out of order, last t={time1:0.000}, new t={time2:0.000}"));
        }
    }

    [Category("NetworkPositionSync")]
    public class SnapshotBuffer_GetInterpolation : SnapshotBufferTestBase
    {
        [Test]
        public void ShouldGiveErrorWhenEmpty()
        {
            SnapshotBuffer<TransformState> buffer = CreateBuffer();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => buffer.GetLinearInterpolation(default));
            Assert.That(exception, Has.Message.EqualTo("No snapshots in buffer"));
        }

        [Test]
        public void ShouldReturnFirstIfOnly1Snapshot([Range(0, 10, 1f)] float now)
        {
            var state = new TransformState(Vector3.one, Quaternion.identity);
            SnapshotBuffer<TransformState> buffer = CreateBuffer();
            buffer.AddSnapShot(state, default);

            LogAssert.Expect(LogType.Log, "[DEBUG] First snapshot");
            TransformState value = buffer.GetLinearInterpolation(now);
            Assert.That(value, Is.EqualTo(state));
        }

        [Test]
        public void ShouldReturnFirstIfNowIsBeforeFirst([Range(0, 1, 0.1f)] float now)
        {
            const float time1 = 1.1f;
            const float time2 = 1.5f;
            var state1 = new TransformState(Vector3.one, Quaternion.identity);
            var state2 = new TransformState(Vector3.one * 2, Quaternion.identity);

            SnapshotBuffer<TransformState> buffer = CreateBuffer();
            buffer.AddSnapShot(state1, time1);
            buffer.AddSnapShot(state2, time2);

            LogAssert.Expect(LogType.Log, $"[DEBUG] No snapshots for t={now:0.000}, using earliest t={time1:0.000}");
            TransformState value = buffer.GetLinearInterpolation(now);
            Assert.That(value, Is.EqualTo(state1));
        }

        [Test]
        public void ShouldReturnLastIfNowIsAfterLast([Range(1.6f, 2.6f, 0.1f)] float now)
        {
            const float time1 = 1.1f;
            const float time2 = 1.5f;
            var state1 = new TransformState(Vector3.one, Quaternion.identity);
            var state2 = new TransformState(Vector3.one * 2, Quaternion.identity);

            SnapshotBuffer<TransformState> buffer = CreateBuffer();
            buffer.AddSnapShot(state1, time1);
            buffer.AddSnapShot(state2, time2);

            LogAssert.Expect(LogType.Warning, $"[WARN] No snapshots for t={now:0.000}, using first t={time1:0.000} last t={time2:0.000}");
            TransformState value = buffer.GetLinearInterpolation(now);
            Assert.That(value, Is.EqualTo(state2));
        }

        [Test]
        public void ShouldReturnInterpolationForNowBetween2Snapshots([Range(1f, 2f, 0.1f)] float now)
        {
            const float time1 = 1f;
            const float time2 = 2f;
            var state1 = new TransformState(Vector3.one, Quaternion.identity);
            var state2 = new TransformState(Vector3.one * 2, Quaternion.identity);
            var expected = new TransformState(Vector3.Lerp(state1.position, state2.position, now - time1), Quaternion.identity);

            SnapshotBuffer<TransformState> buffer = CreateBuffer();
            buffer.AddSnapShot(state1, time1);
            buffer.AddSnapShot(state2, time2);

            TransformState value = buffer.GetLinearInterpolation(now);
            Assert.That(value, Is.EqualTo(expected));
        }
    }

    [Category("NetworkPositionSync")]
    public class SnapshotBuffer_DebugToString : SnapshotBufferTestBase
    {
        [Test]
        public void ShouldSayEmptyBuffer()
        {
            SnapshotBuffer<TransformState> buffer = CreateBuffer();
            string str = buffer.ToString();
            Assert.That(str, Is.EqualTo("Buffer Empty"));
        }
        [Test]
        [Ignore("NotImplemented")]
        public void ShouldListSnapshotsInBuffer()
        {
            SnapshotBuffer<TransformState> buffer = CreateBuffer();
            string str = buffer.ToString();
            //Assert.Ignore("NotImplemented");
        }
    }
}

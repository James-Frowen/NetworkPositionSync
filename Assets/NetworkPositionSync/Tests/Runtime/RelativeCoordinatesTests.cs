using Mirage.Tests;
using NUnit.Framework;
using UnityEngine;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// Snapshot created on one object then applied to another should give the same pose in the chosen coordinate space.
    /// <para>Relative means pose in RelativeTo's space (position rotated into RelativeTo's rotation, scale ignored)</para>
    /// </summary>
    [Category("NetworkPositionSync")]
    public class RelativeCoordinatesTests : TestBase
    {
        private const float POSITION_TOLERANCE = 0.001f;
        private const float ANGLE_TOLERANCE = 0.1f;

        [TearDown]
        public void TearDown()
        {
            TearDownTestObjects();
        }

        private Transform CreateTransform(Vector3 position, Quaternion rotation, Transform parent = null)
        {
            var t = CreateGameObject().transform;
            t.SetParent(parent, false);
            t.SetPositionAndRotation(position, rotation);
            return t;
        }

        private T CreateTransform<T>(Vector3 position, Quaternion rotation, Transform parent = null) where T : NetworkBehaviour
        {
            var behaviour = CreateBehaviour<T>();
            behaviour.transform.SetParent(parent, false);
            behaviour.transform.SetPositionAndRotation(position, rotation);
            return behaviour;
        }

        private static Vector3 PositionRelativeTo(Transform t, Transform relativeTo)
        {
            return Quaternion.Inverse(relativeTo.rotation) * (t.position - relativeTo.position);
        }

        private static Quaternion RotationRelativeTo(Transform t, Transform relativeTo)
        {
            return Quaternion.Inverse(relativeTo.rotation) * t.rotation;
        }

        private static Vector2 Rotate2D(Vector2 v, float angle) => Quaternion.Euler(0, 0, angle) * v;

        private static Vector2 PositionRelativeTo2D(Transform t, Transform relativeTo)
        {
            return Rotate2D(t.position - relativeTo.position, -relativeTo.eulerAngles.z);
        }

        private static float AngleRelativeTo2D(Transform t, Transform relativeTo)
        {
            return t.eulerAngles.z - relativeTo.eulerAngles.z;
        }

        private static void AssertPosition(Vector3 actual, Vector3 expected, string message)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(POSITION_TOLERANCE), $"{message}. Expected:{expected} Actual:{actual}");
        }

        private static void AssertRotation(Quaternion actual, Quaternion expected, string message)
        {
            Assert.That(Quaternion.Angle(actual, expected), Is.LessThan(ANGLE_TOLERANCE), $"{message}. Expected:{expected.eulerAngles} Actual:{actual.eulerAngles}");
        }

        private static void AssertAngle(float actual, float expected, string message)
        {
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(actual, expected)), Is.LessThan(ANGLE_TOLERANCE), $"{message}. Expected:{expected} Actual:{actual}");
        }

        #region 3D
        [Test]
        public void Relative3D_RoundTrip()
        {
            var reference = CreateTransform(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 30));
            var reference2 = CreateTransform(new Vector3(-7, 8, 2), Quaternion.Euler(-30, 70, 15));

            var a = CreateTransform<TestNetworkTransform3D>(new Vector3(4, -5, 6), Quaternion.Euler(40, 50, 60));
            a.SetCoordinates(Coordinates.Relative, reference);
            var b = CreateTransform<TestNetworkTransform3D>(new Vector3(100, 100, 100), Quaternion.identity);
            b.SetCoordinates(Coordinates.Relative, reference2);

            b.ApplySnapshotForTest(a.CreateSnapshotForTest());

            AssertPosition(PositionRelativeTo(b.transform, reference2), PositionRelativeTo(a.transform, reference), "Position relative to reference should be the same");
            AssertRotation(RotationRelativeTo(b.transform, reference2), RotationRelativeTo(a.transform, reference), "Rotation relative to reference should be the same");
        }

        [Test]
        public void Relative3D_SameReference_GivesSamePose()
        {
            var reference = CreateTransform(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 30));

            var a = CreateTransform<TestNetworkTransform3D>(new Vector3(4, -5, 6), Quaternion.Euler(40, 50, 60));
            a.SetCoordinates(Coordinates.Relative, reference);
            var b = CreateTransform<TestNetworkTransform3D>(Vector3.zero, Quaternion.identity);
            b.SetCoordinates(Coordinates.Relative, reference);

            b.ApplySnapshotForTest(a.CreateSnapshotForTest());

            AssertPosition(b.transform.position, a.transform.position, "World position should be the same");
            AssertRotation(b.transform.rotation, a.transform.rotation, "World rotation should be the same");
        }

        [Test]
        public void Relative3D_SnapshotIsPoseInReferenceSpace_IgnoringScale()
        {
            var reference = CreateTransform(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 30));
            reference.localScale = new Vector3(2, 3, 4);

            var a = CreateTransform<TestNetworkTransform3D>(new Vector3(4, -5, 6), Quaternion.Euler(40, 50, 60));
            a.SetCoordinates(Coordinates.Relative, reference);

            var snapshot = a.CreateSnapshotForTest();

            AssertPosition(snapshot.Position, PositionRelativeTo(a.transform, reference), "Snapshot position should be in reference's space");
            AssertRotation(snapshot.Rotation, RotationRelativeTo(a.transform, reference), "Snapshot rotation should be in reference's space");
        }

        [Test]
        public void World3D_RoundTrip()
        {
            var a = CreateTransform<TestNetworkTransform3D>(new Vector3(4, -5, 6), Quaternion.Euler(40, 50, 60));
            a.SetCoordinates(Coordinates.World);
            var b = CreateTransform<TestNetworkTransform3D>(new Vector3(100, 100, 100), Quaternion.identity);
            b.SetCoordinates(Coordinates.World);

            b.ApplySnapshotForTest(a.CreateSnapshotForTest());

            AssertPosition(b.transform.position, a.transform.position, "World position should be the same");
            AssertRotation(b.transform.rotation, a.transform.rotation, "World rotation should be the same");
        }

        [Test]
        public void Local3D_RoundTrip()
        {
            var parent = CreateTransform(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 30));
            var parent2 = CreateTransform(new Vector3(-7, 8, 2), Quaternion.Euler(-30, 70, 15));

            var a = CreateTransform<TestNetworkTransform3D>(Vector3.zero, Quaternion.identity, parent);
            a.transform.localPosition = new Vector3(4, -5, 6);
            a.transform.localRotation = Quaternion.Euler(40, 50, 60);
            a.SetCoordinates(Coordinates.Local);
            var b = CreateTransform<TestNetworkTransform3D>(new Vector3(100, 100, 100), Quaternion.identity, parent2);
            b.SetCoordinates(Coordinates.Local);

            b.ApplySnapshotForTest(a.CreateSnapshotForTest());

            AssertPosition(b.transform.localPosition, a.transform.localPosition, "Local position should be the same");
            AssertRotation(b.transform.localRotation, a.transform.localRotation, "Local rotation should be the same");
        }
        #endregion

        #region 2D
        [Test]
        public void Relative2D_RoundTrip()
        {
            var reference = CreateTransform(new Vector3(1, 2, 0), Quaternion.Euler(0, 0, 30));
            var reference2 = CreateTransform(new Vector3(-3, 4, 0), Quaternion.Euler(0, 0, -75));

            var a = CreateTransform<TestNetworkTransform2D>(new Vector3(5, -1, 0), Quaternion.Euler(0, 0, 110));
            a.SetCoordinates(Coordinates.Relative, reference);
            var b = CreateTransform<TestNetworkTransform2D>(new Vector3(100, 100, 0), Quaternion.identity);
            b.SetCoordinates(Coordinates.Relative, reference2);

            b.ApplySnapshotForTest(a.CreateSnapshotForTest());

            Vector3 bRelative = PositionRelativeTo2D(b.transform, reference2);
            Vector3 aRelative = PositionRelativeTo2D(a.transform, reference);
            AssertPosition(bRelative, aRelative, "Position relative to reference should be the same");
            AssertAngle(AngleRelativeTo2D(b.transform, reference2), AngleRelativeTo2D(a.transform, reference), "Angle relative to reference should be the same");
        }

        [Test]
        public void Relative2D_SameReference_GivesSamePose()
        {
            var reference = CreateTransform(new Vector3(1, 2, 0), Quaternion.Euler(0, 0, 30));

            var a = CreateTransform<TestNetworkTransform2D>(new Vector3(5, -1, 0), Quaternion.Euler(0, 0, 110));
            a.SetCoordinates(Coordinates.Relative, reference);
            var b = CreateTransform<TestNetworkTransform2D>(Vector3.zero, Quaternion.identity);
            b.SetCoordinates(Coordinates.Relative, reference);

            b.ApplySnapshotForTest(a.CreateSnapshotForTest());

            AssertPosition(b.transform.position, a.transform.position, "World position should be the same");
            AssertAngle(b.transform.eulerAngles.z, a.transform.eulerAngles.z, "World angle should be the same");
        }

        [Test]
        public void Relative2D_SnapshotIsPoseInReferenceSpace()
        {
            var reference = CreateTransform(new Vector3(1, 2, 0), Quaternion.Euler(0, 0, 30));
            reference.localScale = new Vector3(2, 3, 1);

            var a = CreateTransform<TestNetworkTransform2D>(new Vector3(5, -1, 0), Quaternion.Euler(0, 0, 110));
            a.SetCoordinates(Coordinates.Relative, reference);

            var snapshot = a.CreateSnapshotForTest();

            AssertPosition(snapshot.Position, PositionRelativeTo2D(a.transform, reference), "Snapshot position should be in reference's space");
            AssertAngle(snapshot.Rotation, AngleRelativeTo2D(a.transform, reference), "Snapshot angle should be relative to reference");
        }

        [Test]
        public void World2D_RoundTrip()
        {
            var a = CreateTransform<TestNetworkTransform2D>(new Vector3(5, -1, 0), Quaternion.Euler(0, 0, 110));
            a.SetCoordinates(Coordinates.World);
            var b = CreateTransform<TestNetworkTransform2D>(new Vector3(100, 100, 0), Quaternion.identity);
            b.SetCoordinates(Coordinates.World);

            b.ApplySnapshotForTest(a.CreateSnapshotForTest());

            AssertPosition(b.transform.position, a.transform.position, "World position should be the same");
            AssertAngle(b.transform.eulerAngles.z, a.transform.eulerAngles.z, "World angle should be the same");
        }

        [Test]
        public void Local2D_RoundTrip()
        {
            var parent = CreateTransform(new Vector3(1, 2, 0), Quaternion.Euler(0, 0, 30));
            var parent2 = CreateTransform(new Vector3(-3, 4, 0), Quaternion.Euler(0, 0, -75));

            var a = CreateTransform<TestNetworkTransform2D>(Vector3.zero, Quaternion.identity, parent);
            a.transform.localPosition = new Vector3(5, -1, 0);
            a.transform.localRotation = Quaternion.Euler(0, 0, 110);
            a.SetCoordinates(Coordinates.Local);
            var b = CreateTransform<TestNetworkTransform2D>(new Vector3(100, 100, 0), Quaternion.identity, parent2);
            b.SetCoordinates(Coordinates.Local);

            b.ApplySnapshotForTest(a.CreateSnapshotForTest());

            AssertPosition(b.transform.localPosition, a.transform.localPosition, "Local position should be the same");
            AssertAngle(b.transform.localEulerAngles.z, a.transform.localEulerAngles.z, "Local angle should be the same");
        }
        #endregion
    }
}

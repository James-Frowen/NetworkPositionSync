using Mirage.Tests;
using NUnit.Framework;
using UnityEngine;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// NetworkTransform2D only syncs x/y position and z rotation.
    /// When applying a snapshot it should keep the existing z position and x/y rotation
    /// </summary>
    [Category("NetworkPositionSync")]
    public class NetworkTransform2DKeepsUnsyncedValuesTests : TestBase
    {
        private const float POSITION_TOLERANCE = 0.001f;
        private const float ANGLE_TOLERANCE = 0.1f;

        private static readonly Vector3 A_POSITION = new Vector3(5, -1, 0);
        private const float A_ANGLE = 110;

        private const float B_Z_POSITION = 5;
        private const float B_X_ANGLE = 10;
        private const float B_Y_ANGLE = 30;

        [TearDown]
        public void TearDown()
        {
            TearDownTestObjects();
        }

        private TestNetworkTransform2D Create(Transform parent)
        {
            var behaviour = CreateBehaviour<TestNetworkTransform2D>();
            behaviour.transform.SetParent(parent, false);
            return behaviour;
        }

        [Test]
        public void KeepsZPositionAndXYRotation([Values(Coordinates.World, Coordinates.Local, Coordinates.Relative)] Coordinates space)
        {
            // used as parent for Local, and as RelativeTo for Relative
            var other = CreateGameObject().transform;
            other.SetPositionAndRotation(new Vector3(1, 2, 0), Quaternion.Euler(0, 0, 30));

            // only parent for local, so that world and local values are the same for world and relative.
            // (x/y/z euler angles are kept in the space that is synced)
            var parent = space == Coordinates.Local ? other : null;

            var a = Create(parent);
            a.transform.localPosition = A_POSITION;
            a.transform.localRotation = Quaternion.Euler(0, 0, A_ANGLE);
            a.SetCoordinates(space, other);

            var b = Create(parent);
            b.transform.localPosition = new Vector3(100, 100, B_Z_POSITION);
            b.transform.localRotation = Quaternion.Euler(B_X_ANGLE, B_Y_ANGLE, 0);
            b.SetCoordinates(space, other);

            b.ApplySnapshotForTest(a.CreateSnapshotForTest());

            // b and a have the same parent, so local values can be checked for all spaces
            var expectedPosition = new Vector3(A_POSITION.x, A_POSITION.y, B_Z_POSITION);
            Assert.That(Vector3.Distance(b.transform.localPosition, expectedPosition), Is.LessThan(POSITION_TOLERANCE),
                $"x/y should be synced, z should be kept. Expected:{expectedPosition} Actual:{b.transform.localPosition}");

            var expectedRotation = Quaternion.Euler(B_X_ANGLE, B_Y_ANGLE, A_ANGLE);
            Assert.That(Quaternion.Angle(b.transform.localRotation, expectedRotation), Is.LessThan(ANGLE_TOLERANCE),
                $"z rotation should be synced, x/y rotation should be kept. Expected:{expectedRotation.eulerAngles} Actual:{b.transform.localEulerAngles}");
        }
    }
}

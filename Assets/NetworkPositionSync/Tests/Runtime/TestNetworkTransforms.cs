using UnityEngine;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// Exposes protected methods so tests can create and apply snapshots without network
    /// </summary>
    public class TestNetworkTransform3D : NetworkTransform3D
    {
        public Snapshot CreateSnapshotForTest()
        {
            CreateSnapshot(out var snapshot, true);
            return snapshot;
        }

        public void ApplySnapshotForTest(Snapshot snapshot) => ApplySnapshot(snapshot);

        public void SetCoordinates(Coordinates space, Transform relativeTo = null)
        {
            _coordinatesType = new CoordinatesType { Space = space, RelativeTo = relativeTo };
        }
    }

    /// <summary>
    /// Exposes protected methods so tests can create and apply snapshots without network
    /// </summary>
    public class TestNetworkTransform2D : NetworkTransform2D
    {
        public Snapshot CreateSnapshotForTest()
        {
            CreateSnapshot(out var snapshot, true);
            return snapshot;
        }

        public void ApplySnapshotForTest(Snapshot snapshot) => ApplySnapshot(snapshot);

        public void SetCoordinates(Coordinates space, Transform relativeTo = null)
        {
            _coordinatesType = new CoordinatesType { Space = space, RelativeTo = relativeTo };
        }
    }
}

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// Owner of client authority object should not store snapshots that server sends back to it, otherwise buffer grows forever
    /// </summary>
    [Category("NetworkPositionSync")]
    public class OwnerSnapshotBufferTests : SyncPositionTestBase<NetworkTransform3D>
    {
        protected override void ExtraPrefabSetup(NetworkIdentity prefab)
        {
            base.ExtraPrefabSetup(prefab);
            // set on prefab so that owner never has snapshots in buffer from before it had client authority
            prefab.GetComponent<NetworkTransform3D>()._clientAuthority = true;
        }

        [UnityTest]
        public IEnumerator OwnerBufferStaysEmptyWhileMoving()
        {
            Assert.That(clientComponent.HasAuthority, Is.True);
            Assert.That(clientComponent._clientAuthority, Is.True);

            var maxCount = 0;
            var end = Time.unscaledTime + (Settings.SyncInterval * 10);
            var frame = 0;
            while (Time.unscaledTime < end)
            {
                frame++;
                clientComponent.transform.position = new Vector3(frame * 0.1f, 0, 0);
                yield return null;

                maxCount = Mathf.Max(maxCount, clientComponent._snapshotBuffer.SnapshotCount);
            }

            // check that movement was synced, so we know server was receiving and sending snapshots
            Assert.That(serverComponent.transform.position.x, Is.GreaterThan(0), "Server should have received position from owner");
            Assert.That(maxCount, Is.Zero, "Owner should not add snapshots to its buffer");
            Assert.That(clientComponent._snapshotBuffer.SnapshotCount, Is.Zero, "Owner should not add snapshots to its buffer");
            errorLogs.AssertNoErrors();
        }
    }
}

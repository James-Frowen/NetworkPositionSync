using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// Client authority should work when server is not host (server system has no client)
    /// </summary>
    [Category("NetworkPositionSync")]
    public class ClientAuthorityDedicatedServerTests : SyncPositionTestBase<NetworkTransform3D>
    {
        protected override void ExtraPrefabSetup(NetworkIdentity prefab)
        {
            base.ExtraPrefabSetup(prefab);
            prefab.GetComponent<NetworkTransform3D>()._clientAuthority = true;
        }

        [Test]
        public void ServerSystemHasNoClient()
        {
            Assert.That(serverSystem.Client, Is.Null);
            Assert.That(serverSystem.ServerActive, Is.True);
        }

        [UnityTest]
        public IEnumerator ServerReceivesPositionFromOwner()
        {
            var positions = new Vector3[] {
                new Vector3(1, 2, 3),
                new Vector3(-4, 5, 10),
            };

            foreach (var position in positions)
            {
                clientComponent.transform.position = position;

                yield return SyncPositionTestHelpers.WaitUntil(() => SyncPositionTestHelpers.Approximately(serverComponent.transform.position, position), Timeout);

                Assert.That(SyncPositionTestHelpers.Approximately(serverComponent.transform.position, position), Is.True,
                    $"Server position should be {position} but was {serverComponent.transform.position}");
            }

            errorLogs.AssertNoErrors();
        }
    }
}

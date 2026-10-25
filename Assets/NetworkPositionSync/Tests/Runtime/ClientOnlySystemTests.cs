using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// System that only has a client (no server) should be able to update without errors
    /// </summary>
    [Category("NetworkPositionSync")]
    public class ClientOnlySystemTests : SyncPositionTestBase<NetworkTransform3D>
    {
        // no owned objects, so this only tests the client system's update loop
        protected override bool SpawnCharacterOnConnect => false;

        private List<SpawnedPair<NetworkTransform3D>> _objects;

        protected override async UniTask LateSetup()
        {
            await base.LateSetup();
            _objects = await SpawnObjects(1);
        }

        [Test]
        public void ClientSystemHasNoServer()
        {
            Assert.That(clientSystem.Server, Is.Null);
            Assert.That(clientSystem.ClientActive, Is.True);
        }

        [UnityTest]
        public IEnumerator UpdatesForSeveralIntervalsWithoutErrors()
        {
            var position = new Vector3(1, 2, 3);
            _objects[0].Server.transform.position = position;

            yield return SyncPositionTestHelpers.WaitForSyncs(Settings, 5);
            yield return SyncPositionTestHelpers.WaitUntil(() => SyncPositionTestHelpers.Approximately(_objects[0].Client.transform.position, position), Timeout);

            errorLogs.AssertNoErrors();
            Assert.That(clientSystem.InterpolationTime.LatestServerTime, Is.GreaterThan(0), "Client should have received messages from server");
            Assert.That(SyncPositionTestHelpers.Approximately(_objects[0].Client.transform.position, position), Is.True,
                $"Client position should be {position} but was {_objects[0].Client.transform.position}");
        }
    }
}

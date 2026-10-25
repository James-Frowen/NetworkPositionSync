using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// Client should only send behaviours that it owns and that have client authority
    /// </summary>
    [Category("NetworkPositionSync")]
    public class ClientOnlySendsClientAuthorityTests : SyncPositionTestBase<NetworkTransform3D>
    {
        private ServerPositionMessageCounter _fromClient;

        protected override async UniTask LateSetup()
        {
            await base.LateSetup();

            // character is owned by client, but does not use client authority (default)
            Assert.That(clientComponent.HasAuthority, Is.True);
            Assert.That(serverComponent._clientAuthority, Is.False);
            Assert.That(clientComponent._clientAuthority, Is.False);

            _fromClient = SyncPositionTestHelpers.CountClientMessages(server, serverSystem);
        }

        [UnityTest]
        public IEnumerator OwnedObjectWithoutClientAuthorityIsNotSent()
        {
            var serverPosition = new Vector3(1, 2, 3);
            serverComponent.transform.position = serverPosition;

            // let server position reach client first
            yield return SyncPositionTestHelpers.WaitUntil(() => SyncPositionTestHelpers.Approximately(clientComponent.transform.position, serverPosition), Timeout);
            Assert.That(SyncPositionTestHelpers.Approximately(clientComponent.transform.position, serverPosition), Is.True, "server position should have reached client");

            // change on client, this should not be sent
            clientComponent.transform.position = new Vector3(10, 20, 30);

            yield return SyncPositionTestHelpers.WaitForSyncs(Settings, 5);

            Assert.That(_fromClient.WithData, Is.Zero, "Client should not have sent any position data");
            Assert.That(SyncPositionTestHelpers.Approximately(serverComponent.transform.position, serverPosition), Is.True,
                $"Server position should stay as the server set it, but was {serverComponent.transform.position}");
            errorLogs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator OwnedObjectWithClientAuthorityIsSent()
        {
            serverComponent._clientAuthority = true;
            clientComponent._clientAuthority = true;

            var clientPosition = new Vector3(10, 20, 30);
            clientComponent.transform.position = clientPosition;

            yield return SyncPositionTestHelpers.WaitUntil(() => SyncPositionTestHelpers.Approximately(serverComponent.transform.position, clientPosition), Timeout);

            Assert.That(_fromClient.WithData, Is.GreaterThan(0), "Client should have sent position data");
            Assert.That(SyncPositionTestHelpers.Approximately(serverComponent.transform.position, clientPosition), Is.True,
                $"Server position should be set by client, but was {serverComponent.transform.position}");
            errorLogs.AssertNoErrors();
        }
    }
}

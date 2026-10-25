using System.Collections;
using Cysharp.Threading.Tasks;
using Mirage.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// When authority is removed the client should stop sending and go back to interpolating server snapshots.
    /// When an owned object is destroyed, there should be no errors on later syncs
    /// </summary>
    [Category("NetworkPositionSync")]
    public class ClientAuthorityRemovedTests : SyncPositionTestBase<NetworkTransform3D>
    {
        private SpawnedPair<NetworkTransform3D> _owned;
        private ServerPositionMessageCounter _fromClient;

        protected override void ExtraPrefabSetup(NetworkIdentity prefab)
        {
            base.ExtraPrefabSetup(prefab);
            // all objects use client authority, (only owned objects will be sent by client)
            prefab.GetComponent<NetworkTransform3D>()._clientAuthority = true;
        }

        protected override async UniTask LateSetup()
        {
            await base.LateSetup();

            // extra object (not character) so that authority can be removed
            var pairs = await SpawnObjects(1, serverPlayer);
            _owned = pairs[0];

            _fromClient = SyncPositionTestHelpers.CountClientMessages(server, serverSystem);
        }

        /// <summary>
        /// Moves owned object on client and waits for it to reach server, so we know client is sending it
        /// </summary>
        private IEnumerator MoveOnClientAndWaitForServer(Vector3 position)
        {
            Assert.That(_owned.Client.HasAuthority, Is.True);

            _owned.Client.transform.position = position;
            yield return SyncPositionTestHelpers.WaitUntil(() => SyncPositionTestHelpers.Approximately(_owned.Server.transform.position, position), Timeout);
            Assert.That(SyncPositionTestHelpers.Approximately(_owned.Server.transform.position, position), Is.True, "Client authority position should have reached server");
        }

        [UnityTest]
        public IEnumerator AfterAuthorityRemoved_ClientStopsSendingAndInterpolatesServerPositions()
        {
            yield return MoveOnClientAndWaitForServer(new Vector3(1, 2, 3));

            _owned.Server.Identity.RemoveClientAuthority();
            yield return SyncPositionTestHelpers.WaitUntil(() => !_owned.Client.HasAuthority, Timeout);
            Assert.That(_owned.Client.HasAuthority, Is.False, "client should have lost authority");

            // wait for any messages already sent to arrive, then reset counter
            yield return SyncPositionTestHelpers.WaitForSyncs(Settings, 2);
            _fromClient.WithData = 0;

            // move on server, client should now follow server
            var serverPosition = new Vector3(-10, 5, 20);
            _owned.Server.transform.position = serverPosition;

            yield return SyncPositionTestHelpers.WaitUntil(() => SyncPositionTestHelpers.Approximately(_owned.Client.transform.position, serverPosition), Timeout);
            Assert.That(SyncPositionTestHelpers.Approximately(_owned.Client.transform.position, serverPosition), Is.True,
                $"Client should interpolate to server position after authority is removed, but was {_owned.Client.transform.position}");

            // local change on client should not be sent any more
            _owned.Client.transform.position = new Vector3(100, 100, 100);
            yield return SyncPositionTestHelpers.WaitForSyncs(Settings, 5);

            Assert.That(_fromClient.WithData, Is.Zero, "Client should not send after authority is removed");
            Assert.That(SyncPositionTestHelpers.Approximately(_owned.Server.transform.position, serverPosition), Is.True,
                $"Server position should stay as the server set it, but was {_owned.Server.transform.position}");
            errorLogs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator DestroyingOwnedObject_HasNoErrorsOnLaterSyncs()
        {
            yield return MoveOnClientAndWaitForServer(new Vector3(1, 2, 3));

            var netId = _owned.Server.NetId;
            serverObjectManager.Destroy(_owned.Server.Identity);
            yield return SyncPositionTestHelpers.WaitUntil(() => !client.World.TryGetIdentity(netId, out _), Timeout);
            Assert.That(client.World.TryGetIdentity(netId, out _), Is.False, "Object should have been destroyed on client");

            // keep moving character so client still has something to send
            clientComponent.transform.position = new Vector3(5, 5, 5);
            yield return SyncPositionTestHelpers.WaitForSyncs(Settings, 5);

            errorLogs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator UnspawnOwnedObject_HasNoErrorsOnLaterSyncs()
        {
            yield return MoveOnClientAndWaitForServer(new Vector3(1, 2, 3));

            var netId = _owned.Server.NetId;
            // unspawn without destroying server object
            serverObjectManager.Destroy(_owned.Server.Identity, destroyServerObject: false);
            yield return SyncPositionTestHelpers.WaitUntil(() => !client.World.TryGetIdentity(netId, out _), Timeout);
            Assert.That(client.World.TryGetIdentity(netId, out _), Is.False, "Object should have been destroyed on client");

            clientComponent.transform.position = new Vector3(5, 5, 5);
            yield return SyncPositionTestHelpers.WaitForSyncs(Settings, 5);

            errorLogs.AssertNoErrors();
        }
    }
}

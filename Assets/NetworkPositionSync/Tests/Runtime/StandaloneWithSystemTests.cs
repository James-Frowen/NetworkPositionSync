using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// Behaviours controlled by a <see cref="SyncPositionSystem"/> should not use standalone mode
    /// </summary>
    [Category("NetworkPositionSync")]
    public class StandaloneWithSystemTests : SyncPositionTestBase<MockNetworkTransform>
    {
        [Test]
        public void BehavioursAreNotStandaloneWhenSystemExists()
        {
            Assert.That(serverComponent.IsStandalone, Is.False);
            Assert.That(clientComponent.IsStandalone, Is.False);
            Assert.That(serverComponent._system, Is.SameAs(serverSystem));
            Assert.That(clientComponent._system, Is.SameAs(clientSystem));
            errorLogs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator SpawnedObjectIsNotStandalone() => UniTask.ToCoroutine(async () =>
        {
            var pairs = await SpawnObjects(1);

            Assert.That(pairs[0].Server.IsStandalone, Is.False);
            Assert.That(pairs[0].Client.IsStandalone, Is.False);
            Assert.That(pairs[0].Server._system, Is.SameAs(serverSystem));
            Assert.That(pairs[0].Client._system, Is.SameAs(clientSystem));
            errorLogs.AssertNoErrors();
        });

        [UnityTest]
        public IEnumerator SystemIsClearedWhenUnspawned() => UniTask.ToCoroutine(async () =>
        {
            var pairs = await SpawnObjects(1);
            var serverObject = pairs[0].Server;
            Assert.That(serverObject._system, Is.SameAs(serverSystem));

            // unspawn without destroying so we can check the server object after
            serverObjectManager.Destroy(serverObject.Identity, destroyServerObject: false);

            Assert.That(serverObject._system, Is.Null, "System should be cleared when object is unspawned");
            Assert.That(serverObject.IsStandalone, Is.True);
            errorLogs.AssertNoErrors();
        });

        [UnityTest]
        public IEnumerator SyncsWithSystem()
        {
            const float VALUE = 10;
            serverComponent.Value = VALUE;

            yield return SyncPositionTestHelpers.WaitUntil(() => clientComponent.Value == VALUE, Timeout);
            Assert.That(clientComponent.Value, Is.EqualTo(VALUE));
            Assert.That(clientComponent.IsStandalone, Is.False);
            errorLogs.AssertNoErrors();
        }
    }
}

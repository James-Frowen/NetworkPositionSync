using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// When write size is not included, all objects in a message should still be read (including the last one)
    /// </summary>
    [Category("NetworkPositionSync")]
    public class WithoutWriteSizeTests : SyncPositionTestBase<NetworkTransform3D>
    {
        private const int OBJECT_COUNT = 5;

        protected override bool IncludeWriteSize => false;
        protected override bool SpawnCharacterOnConnect => false;

        private List<SpawnedPair<NetworkTransform3D>> _objects;

        protected override async UniTask LateSetup()
        {
            await base.LateSetup();
            // spawn before moving, without write size client can't skip objects it doesn't know about
            _objects = await SpawnObjects(OBJECT_COUNT);
        }

        [Test]
        public void SystemsDoNotIncludeWriteSize()
        {
            Assert.That(serverSystem._includeWriteSize, Is.False);
            Assert.That(clientSystem._includeWriteSize, Is.False);
        }

        [UnityTest]
        public IEnumerator AllObjectsSync()
        {
            var expected = new Vector3[OBJECT_COUNT];
            for (var i = 0; i < OBJECT_COUNT; i++)
            {
                expected[i] = new Vector3(i + 1, 2 * i, -i);
                _objects[i].Server.transform.position = expected[i];
            }

            yield return SyncPositionTestHelpers.WaitUntil(() =>
            {
                for (var i = 0; i < OBJECT_COUNT; i++)
                {
                    if (!SyncPositionTestHelpers.Approximately(_objects[i].Client.transform.position, expected[i]))
                        return false;
                }
                return true;
            }, Timeout);

            for (var i = 0; i < OBJECT_COUNT; i++)
            {
                Assert.That(SyncPositionTestHelpers.Approximately(_objects[i].Client.transform.position, expected[i]), Is.True,
                    $"Object {i} should be at {expected[i]} but was {_objects[i].Client.transform.position}");
            }
            errorLogs.AssertNoErrors();
        }
    }
}

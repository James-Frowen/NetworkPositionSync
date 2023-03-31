using System.Collections;
using Cysharp.Threading.Tasks;
using Mirage;
using Mirage.Tests;
using UnityEngine.TestTools;

namespace Mirage.SyncPosition.Tests.Runtime
{
    public class ClientServerSetup<T> : ClientServerSetupBase<T> where T : NetworkBehaviour
    {
        [UnitySetUp]
        public IEnumerator UnitySetUp() => ClientServerSetUp().ToCoroutine();

        [UnityTearDown]
        public IEnumerator UnityTearDown() => ClientServerTearDown().ToCoroutine();
    }
}

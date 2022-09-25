using Cysharp.Threading.Tasks;
using Mirage;
using Mirage.Tests;
using System.Collections;
using UnityEngine.TestTools;

namespace JamesFrowen.PositionSync.Tests.Runtime
{
    public class ClientServerSetup<T> : ClientServerSetupBase<T> where T : NetworkBehaviour
    {
        [UnitySetUp]
        public IEnumerator UnitySetUp() => ClientServerSetUp().ToCoroutine();

        [UnityTearDown]
        public IEnumerator UnityTearDown() => ClientServerTearDown().ToCoroutine();
    }
}

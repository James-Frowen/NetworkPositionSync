using System.Collections;
using System.Linq;
using Mirror;
using UnityEngine;

namespace JamesFrowen.NetworkPositionSync.Examples.FollowPets
{
    public class ObjectCount : MonoBehaviour
    {
        int playerCount;
        int objectCount;

        private IEnumerator Start()
        {
            while (!NetworkClient.active)
            {
                yield return null;
            }


            while (true)
            {
                System.Collections.Generic.Dictionary<uint, NetworkIdentity> objects = NetworkIdentity.spawned;

                objectCount = objects.Count;
                playerCount = objects.Where(x => x.Value.TryGetComponent(out AutoMovePlayer _)).Count();

                yield return new WaitForSeconds(1);
            }
        }

        private void OnGUI()
        {
            GUILayout.Label($"PlayerCount: {playerCount}");
            GUILayout.Label($"ObjectCount: {objectCount}");
        }
    }
}

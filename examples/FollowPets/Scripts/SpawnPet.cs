using Mirror;
using UnityEngine;

namespace JamesFrowen.NetworkPositionSync.Examples.FollowPets
{
    public class SpawnPet : MonoBehaviour
    {
        [SerializeField] GameObject prefab;
        [SerializeField] Vector3 bounds = new Vector3(25, 25, 25);

        [SerializeField] int minPets = 10;
        [SerializeField] int maxPets = 100;
        [SerializeField] float petMultiple = 1;

        int current = 0;

        private void OnValidate()
        {
            if (minPets > maxPets)
            {
                Debug.LogWarning("Min can not be greater than max");
                minPets = maxPets;
            }
        }
        public void ServerStarted()
        {
            for (int i = 0; i < minPets; i++)
            {
                SpawnOne();
            }
        }

        private void SpawnOne()
        {
            if (current < maxPets)
            {
                GameObject clone = Instantiate(prefab);
                clone.transform.position = GetRandomPosition();
                NetworkServer.Spawn(clone);
                current++;
            }
        }

        public void PlayerConnected(NetworkConnection _)
        {
            int playerCount = NetworkServer.connections.Count;
            float targetPetCount = Mathf.Min(playerCount * petMultiple, maxPets);
            while (targetPetCount > current)
            {
                SpawnOne();
            }
        }

        private Vector3 GetRandomPosition()
        {
            return new Vector3(
                Random.Range(-bounds.x, bounds.x),
                Random.Range(-bounds.y, bounds.y),
                Random.Range(-bounds.z, bounds.z)
                );
        }
    }
}

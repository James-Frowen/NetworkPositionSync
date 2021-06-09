using Mirror;
using UnityEngine;

namespace JamesFrowen.NetworkPositionSync.Examples.FollowPets
{
    [RequireComponent(typeof(Collider))]
    public class PetFollower : NetworkBehaviour
    {
        [SerializeField] float minSpeed = 1.5f;
        [SerializeField] float maxSpeed = 1.5f;
        float speed;
        Transform target;
        bool hasTarget;

        private void Awake()
        {
            speed = Random.Range(minSpeed, maxSpeed);
        }

        [ServerCallback()]
        private void OnTriggerEnter(Collider other)
        {
            if (!hasTarget && other.TryGetComponent(out NetworkIdentity identity))
            {
                // is a player
                if (identity.connectionToClient != null)
                {
                    target = identity.transform;
                    hasTarget = true;
                }
            }
        }

        [ServerCallback()]
        private void OnTriggerExit(Collider other)
        {
            if (hasTarget && other.transform == target)
            {
                target = null;
                hasTarget = false;
            }
        }

        [ServerCallback()]
        private void Update()
        {
            // check if target has been destroyed
            if (target == null)
            {
                target = null;
                hasTarget = false;
            }

            if (hasTarget)
            {
                transform.position = Vector3.MoveTowards(transform.position, target.position, speed * Time.deltaTime);
                transform.forward = ((target.position - transform.position) + (target.forward * 0.1f)).normalized;
            }
        }
    }
}

using System.Collections;
using Mirror;
using UnityEngine;

namespace JamesFrowen.NetworkPositionSync.Examples.FollowPets
{
    public class AutoMovePlayer : NetworkBehaviour
    {
        [SerializeField] Vector3 bounds = new Vector3(25, 25, 25);
        [SerializeField] float distanceToTarget = 0.1f;
        [SerializeField] float speed = 2;
        [SerializeField] float pauseTime = 5;

        [SerializeField] new Renderer renderer;

        Vector3 target;

        static int playerCount;
        private void Awake()
        {
            name = $"player {playerCount++}";
        }

        public override void OnStartLocalPlayer()
        {
            Debug.Log("Starting MoveRandomly");
            StartCoroutine(MoveRandomly());

            renderer.material.color = Color.cyan;
        }

        private IEnumerator MoveRandomly()
        {
            while (true)
            {
                if (CloseToTarget())
                {
                    yield return new WaitForSeconds(pauseTime);
                    target = GetRandomTarget();
                }

                MoveTowardsTarget();
                yield return null;
            }
        }

        private void MoveTowardsTarget()
        {
            float frameSpeed = speed * Time.deltaTime;

            // dont overshoot target
            float max = Mathf.Min(frameSpeed, DistanceToTarget());
            transform.position = Vector3.MoveTowards(transform.position, target, max);
            transform.LookAt(target);
        }

        private bool CloseToTarget()
        {
            return DistanceToTarget() < distanceToTarget;
        }
        private float DistanceToTarget()
        {
            return Vector3.Distance(transform.position, target);
        }

        private Vector3 GetRandomTarget()
        {
            return new Vector3(
                Random.Range(-bounds.x, bounds.x),
                Random.Range(-bounds.y, bounds.y),
                Random.Range(-bounds.z, bounds.z)
                );
        }
    }
}

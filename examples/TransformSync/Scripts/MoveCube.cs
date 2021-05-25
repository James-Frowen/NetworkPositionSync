using Mirror;
using UnityEngine;
using UnityEngine.AI;

namespace JamesFrowen.PositionSync.Example
{
    /// <summary>
    /// Sets NavMesh destination for cube. The server will then sync the position to the clients using NetworkTransformBehaviour
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class MoveCube : NetworkBehaviour
    {
        [SerializeField] Vector3 min;
        [SerializeField] Vector3 max;
        private NavMeshAgent navMeshAgent;

        private void Awake()
        {
            this.navMeshAgent = this.GetComponent<NavMeshAgent>();
        }

        public override void OnStartClient()
        {
            this.navMeshAgent.enabled = this.hasAuthority;
        }
        public override void OnStartAuthority()
        {
            this.navMeshAgent.enabled = this.hasAuthority;
        }
        public override void OnStartServer()
        {
            this.navMeshAgent.enabled = this.connectionToClient == null;
        }


        void Update()
        {
            if (this.hasAuthority || this.connectionToClient == null && this.isServer)
            {
                // if close to destination, set new destination
                if (Vector3.Distance(this.transform.position, this.navMeshAgent.destination) < 1f)
                {
                    this.navMeshAgent.destination = this.RandomPointInBounds();
                }
            }
        }

        public Vector3 RandomPointInBounds()
        {
            return new Vector3(
                Random.Range(this.min.x, this.max.x),
                Random.Range(this.min.y, this.max.y),
                Random.Range(this.min.z, this.max.z)
            );
        }
    }
}

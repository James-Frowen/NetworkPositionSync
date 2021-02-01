using UnityEngine;

namespace JamesFrowen.PositionSync
{
    [AddComponentMenu("Network/SyncPosition/System")]
    public class SyncPositionSystem : MonoBehaviour
    {
        [SerializeField] SyncPositionBehaviourRuntimeSet _behaviourSet;
    }
}

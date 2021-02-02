using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace JamesFrowen.PositionSync
{
    [CreateAssetMenu(menuName = "ScriptableVariables/Runtime Sets/SyncPositionBehaviour")]
    // todo make runtime dictionary
    public class SyncPositionBehaviourRuntimeDictionary : ScriptableObject, IEnumerable<ISyncPositionBehaviour>
    {
        public event ObjectSet<ISyncPositionBehaviour>.OnChange onChange;

        [SerializeField] protected Dictionary<uint, ISyncPositionBehaviour> _items = new Dictionary<uint, ISyncPositionBehaviour>();

        public void Add(ISyncPositionBehaviour thing)
        {
            var netId = thing.netId;
            this._items.Add(netId, thing);
        }

        public void Remove(ISyncPositionBehaviour thing)
        {
            var netId = thing.netId;
            this._items.Remove(netId);
        }

        public bool Contains(ISyncPositionBehaviour thing)
        {
            var netId = thing.netId;
            return this._items.ContainsKey(netId);
        }

        public int Count => this._items.Count;
        public bool IsEmpty => this._items.Count == 0;

        public ISyncPositionBehaviour this[uint netId] => this._items[netId];

        public bool TryGet(uint netId, out ISyncPositionBehaviour behaviour) => this._items.TryGetValue(netId, out behaviour);

        IEnumerator<ISyncPositionBehaviour> IEnumerable<ISyncPositionBehaviour>.GetEnumerator() => this._items.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => this._items.GetEnumerator();
    }
}

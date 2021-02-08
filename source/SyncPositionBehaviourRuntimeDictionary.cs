using Mirror;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace JamesFrowen.PositionSync
{
    [CreateAssetMenu(menuName = "PositionSync/Behaviour Set")]
    // todo make runtime dictionary
    public class SyncPositionBehaviourRuntimeDictionary : ScriptableObject, IEnumerable<ISyncPositionBehaviour>
    {
        static readonly ILogger logger = LogFactory.GetLogger<SyncPositionBehaviour>(LogType.Error);

        public event ObjectSet<ISyncPositionBehaviour>.OnChange onChange;

        [NonSerialized] Dictionary<uint, ISyncPositionBehaviour> _items = new Dictionary<uint, ISyncPositionBehaviour>();

        public void Clear() => this._items.Clear();

        public void Add(ISyncPositionBehaviour thing)
        {
            var netId = thing.netId;
            this._items.Add(netId, thing);


            if (this._items.TryGetValue(netId, out var existingValue))
            {
                if (existingValue != thing)
                {
                    if (logger.ErrorEnabled()) logger.LogError("Parent can't be set without control");
                }
            }
            else
            {
                this._items.Add(netId, thing);
                onChange?.Invoke(thing, true);
            }
        }

        public void Remove(ISyncPositionBehaviour thing)
        {
            var netId = thing.netId;
            this._items.Remove(netId);
            onChange?.Invoke(thing, false);
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

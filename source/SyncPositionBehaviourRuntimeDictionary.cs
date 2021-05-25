using System;
using System.Collections;
using System.Collections.Generic;
using JamesFrowen.Logging;
using JamesFrowen.ScriptableVariables;
using UnityEngine;

namespace JamesFrowen.PositionSync
{
    [CreateAssetMenu(menuName = "PositionSync/Behaviour Set")]
    // todo make runtime dictionary
    public class SyncPositionBehaviourRuntimeDictionary : ScriptableObject, IEnumerable<SyncPositionBehaviour>
    {
        public event ObjectSet<SyncPositionBehaviour>.OnChange onChange;

        [NonSerialized] Dictionary<uint, SyncPositionBehaviour> _items = new Dictionary<uint, SyncPositionBehaviour>();

        public void Clear() => _items.Clear();

        public void Add(SyncPositionBehaviour thing)
        {
            uint netId = thing.netId;
            _items.Add(netId, thing);


            if (_items.TryGetValue(netId, out SyncPositionBehaviour existingValue))
            {
                if (existingValue != thing)
                {
                    // todo what is this log?
                    SimpleLogger.Error("Parent can't be set without control");
                }
            }
            else
            {
                _items.Add(netId, thing);
                onChange?.Invoke(thing, true);
            }
        }

        public void Remove(SyncPositionBehaviour thing)
        {
            uint netId = thing.netId;
            _items.Remove(netId);
            onChange?.Invoke(thing, false);
        }

        public bool Contains(SyncPositionBehaviour thing)
        {
            uint netId = thing.netId;
            return _items.ContainsKey(netId);
        }

        public int Count => _items.Count;
        public bool IsEmpty => _items.Count == 0;

        public SyncPositionBehaviour this[uint netId] => _items[netId];

        public bool TryGet(uint netId, out SyncPositionBehaviour behaviour) => _items.TryGetValue(netId, out behaviour);

        IEnumerator<SyncPositionBehaviour> IEnumerable<SyncPositionBehaviour>.GetEnumerator() => _items.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }
}

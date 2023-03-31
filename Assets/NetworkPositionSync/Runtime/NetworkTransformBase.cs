/*
MIT License

Copyright (c) 2021 James Frowen

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using Mirage;
using Mirage.Serialization;
using UnityEngine;

namespace JamesFrowen.PositionSync
{
    /// <summary>
    /// non generic base class that can be used too call functions from outside
    /// </summary>
    public abstract class NetworkTransformBase : NetworkBehaviour
    {
        public abstract void WriteIfDirty(NetworkWriter writer);
        public abstract void ClientUpdate(float viewTime, float removeTime);

        public static void ReadAll(PooledNetworkReader reader, float insertTime)
        {
            // assume 1 state is atleast 3 bytes
            // (it should be more, but there shouldn't be random left over bits in reader so 3 is enough for check)
            const int MIN_READ_SIZE = 3;

            while (reader.CanReadBytes(MIN_READ_SIZE))
            {
                var behavior = reader.ReadNetworkBehaviour<NetworkTransformBase>();
                behavior.ReadAndInsertSnapshot(reader, insertTime);
            }
        }
        protected abstract void ReadAndInsertSnapshot(NetworkReader reader, float serverTime);
    }

    public abstract class NetworkTransformBase<T> : NetworkTransformBase
    {
        [Tooltip("If true, we will use local position and rotation. If false, we use world position and rotation.")]
        [SerializeField] protected bool useLocalSpace = true;

        [Header("Authority")]
        [Tooltip("Set to true if moves come from owner client, set to false if moves always come from server")]
        [SerializeField] private bool clientAuthority = false;

        /// <summary>
        /// Current Snapshot
        /// </summary>
        protected T _snapshot;
        protected SnapshotBuffer<T> _snapshotBuffer;
        private void Awake()
        {
            _snapshotBuffer = new SnapshotBuffer<T>(CreateInterpolator());
            Identity.OnStartServer.AddListener(NetworkStart);
            Identity.OnStartClient.AddListener(NetworkStart);
        }

        protected abstract void NetworkStart();
        protected abstract ISnapshotInterpolator<T> CreateInterpolator();
        protected abstract void WriteSnapshot(NetworkWriter writer);
        protected abstract T ReadSnapshot(NetworkReader reader);

        /// <summary>Create a snapshot from the current Transform</summary>
        protected abstract T CreateSnapshot();
        protected abstract void ApplySnapshot(T newSnapshot);
        protected abstract bool HasMoved(T newSnapshot);

        protected override void ReadAndInsertSnapshot(NetworkReader reader, float serverTime)
        {
            var snap = ReadSnapshot(reader);
            if (_snapshotBuffer.IsEmpty)
                // insert a snapshot so that we have a starting point
                // time isn't important, just has to be before servertime
                _snapshotBuffer.AddSnapShot(CreateSnapshot(), serverTime - 0.1f);

            _snapshotBuffer.AddSnapShot(snap, serverTime);
        }

        public override void WriteIfDirty(NetworkWriter writer)
        {
            var snap = CreateSnapshot();
            if (!HasMoved(snap))
                return;
            _snapshot = snap;

            writer.WriteNetworkBehaviour(this);
            WriteSnapshot(writer);
        }

        public override void ClientUpdate(float viewTime, float removeTime)
        {
            var remoteOwner = !(clientAuthority && HasAuthority);
            // only run Interpolation for remote owner
            if (!remoteOwner)
                return;

            if (_snapshotBuffer.IsEmpty)
                return;

            var snapshot = _snapshotBuffer.GetLinearInterpolation(viewTime);
            ApplySnapshot(snapshot);

            _snapshotBuffer.RemoveOldSnapshots(removeTime);
        }
    }
}

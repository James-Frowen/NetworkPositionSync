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

using Mirage.Logging;
using Mirage.Serialization;
using UnityEngine;

namespace Mirage.SyncPosition
{
    /// <summary>
    /// non generic base class that can be used too call functions from outside
    /// </summary>
    public abstract class NetworkTransformBase : NetworkBehaviour
    {
        private static readonly ILogger logger = LogFactory.GetLogger<NetworkTransformBase>();

        /// <summary>
        /// Max possible size of write. Neede to know if message needs to be split up
        /// </summary>
        public abstract int MaxWriteSize { get; }
        public abstract void Setup();
        public abstract void WriteIfDirty(NetworkWriter headerWriter, PooledNetworkWriter dataWriter, bool includeWriteSize);
        public abstract void ClientUpdate(float viewTime, float removeTime);

        public static void ReadAll(float insertTime, PooledNetworkReader metaReader, PooledNetworkReader dataReader, bool includeWriteSize)
        {
            // assume 1 state is atleast 3 bytes
            // (it should be more, but there shouldn't be random left over bits in reader so 3 is enough for check)
            const int MIN_READ_SIZE = 3;

            while (metaReader.CanReadBytes(MIN_READ_SIZE))
            {
                var behavior = metaReader.ReadNetworkBehaviour<NetworkTransformBase>();
                if (includeWriteSize)
                {
                    var size = (int)metaReader.ReadPackedUInt32();

                    if (behavior == null)
                    {
                        dataReader.Skip(size);
                        continue;
                    }
                }
                else
                {
                    // if null we can't keep reading because we dont know the length
                    // this can happen if there is a new spawned object, and then state message arrives first
                    if (behavior == null)
                        return;
                }

                if (logger.LogEnabled()) logger.Log($"Reading {behavior.NetId}");

                behavior.ReadAndInsertSnapshot(dataReader, insertTime);
            }
        }
        protected abstract void ReadAndInsertSnapshot(NetworkReader reader, float insertTime);
    }

    public enum Coordinates
    {
        World,
        Local,
        Relative
    }

    [System.Serializable]
    public struct CoordinatesType
    {
        public Coordinates Space;
        public Transform RelativeTo;
    }

    public abstract class NetworkTransformBase<T> : NetworkTransformBase
    {
        // just use base type for this logger, dont need different logger for generic
        private static readonly ILogger logger = LogFactory.GetLogger<NetworkTransformBase>();

        [Tooltip("Set what values to usee for position and rotation.\nworld use transform.position\nlocal uses transform.localPosition\nrelative uses (RelativeTo.position - transform.position)")]
        [SerializeField] protected CoordinatesType _coordinatesType = new CoordinatesType { Space = Coordinates.World };

        [Tooltip("Set to true if moves come from owner client, set to false if moves always come from server")]
        [SerializeField] internal bool _clientAuthority = false;

        /// <summary>
        /// Current Snapshot
        /// </summary>
        protected T _snapshot;
        protected internal SnapshotBuffer<T> _snapshotBuffer;

        private bool _hasClientAuthoritySnapshot;
        private T _clientAuthoritySnapshot;

        protected NetworkTransformBase()
        {
            _snapshotBuffer = new SnapshotBuffer<T>(CreateInterpolator());
        }

        protected abstract ISnapshotInterpolator<T> CreateInterpolator();
        protected abstract void WriteSnapshot(NetworkWriter writer);
        protected abstract T ReadSnapshot(NetworkReader reader);

        /// <summary>Create a snapshot from the current Transform</summary>
        protected abstract T CreateSnapshot();
        protected abstract void ApplySnapshot(T newSnapshot);
        protected abstract bool HasChanged(T newSnapshot);

        protected sealed override void ReadAndInsertSnapshot(NetworkReader reader, float insertTime)
        {
            var snap = ReadSnapshot(reader);

            if (IsServer)
            {
                // store latest client snapshot so server can forward it to other clients
                _clientAuthoritySnapshot = snap;
                _hasClientAuthoritySnapshot = true;
            }

            if (IsClient)
            {
                // insert a snapshot so that we have a starting point
                // time isn't important, just has to be before servertime
                if (_snapshotBuffer.IsEmpty)
                    _snapshotBuffer.AddSnapShot(CreateSnapshot(), insertTime - 0.1f);

                _snapshotBuffer.AddSnapShot(snap, insertTime);
            }
            else
            {
                // apply snapshot right away on server
                ApplySnapshot(snap);
            }
        }

        public sealed override void WriteIfDirty(NetworkWriter metaWriter, PooledNetworkWriter dataWriter, bool includeWriteSize)
        {
            if (!GetChangedSnapShot(out var snap))
                return;

            _snapshot = snap;

            metaWriter.WriteNetworkBehaviour(this);
            var startPos = dataWriter.BitPosition;
            WriteSnapshot(dataWriter);

            if (includeWriteSize)
            {
                var endPos = dataWriter.BitPosition;
                var size = endPos - startPos;
                metaWriter.WritePackedUInt32((uint)size);
            }
        }

        private bool GetChangedSnapShot(out T snapshot)
        {
            // if client auth, then we dont need to create and check if snapshot is changed,
            // server can just forward it
            // todo host mode?
            if (_clientAuthority && Owner != null)
            {
                // no new state from client to forward
                if (_hasClientAuthoritySnapshot)
                {
                    // reset flag till we get new snapshot from client
                    _hasClientAuthoritySnapshot = false;
                    snapshot = _clientAuthoritySnapshot;
                    return true;
                }
                else
                {
                    snapshot = default;
                    return false;
                }
            }
            else
            {
                snapshot = CreateSnapshot();
                return HasChanged(snapshot);
            }
        }

        public sealed override void ClientUpdate(float viewTime, float removeTime)
        {
            var localOwner = _clientAuthority && HasAuthority;
            // only run Interpolation for remote owner
            if (localOwner)
                return;

            if (_snapshotBuffer.IsEmpty)
                return;

            var snapshot = _snapshotBuffer.GetLinearInterpolation(viewTime);
            ApplySnapshot(snapshot);

            _snapshotBuffer.RemoveOldSnapshots(removeTime);
        }
    }
}

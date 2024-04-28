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
        /// Max possible size of write. Need to know if message needs to be split up
        /// </summary>
        public abstract int MaxWriteSize { get; }
        /// <summary>
        /// Called once this behaviour is added to <see cref="SyncPositionSystem"/>. Which will happen from the <see cref="NetworkWorld.onSpawn"/> event.
        /// </summary>
        public virtual void Setup() { }
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

        /// <summary>
        /// Create a snapshot from the current Transform.
        /// can return false if the Transform is unchanged in order to not send any data.
        /// If <paramref name="force"/> is true then the snapshot must be returned even if unchanged.
        /// </summary>
        /// <param name="newSnapshot"></param>
        /// <param name="force"></param>
        /// <returns></returns>
        protected abstract bool CreateSnapshot(out T newSnapshot, bool force);
        /// <summary>
        /// Used to apply a snapshot to the object.
        /// Should use <paramref name="newSnapshot"/> to set position/etc on transform or rigidbody
        /// </summary>
        /// <param name="newSnapshot"></param>
        protected abstract void ApplySnapshot(T newSnapshot);

        /// <summary>
        /// Optional override for when you want to change how this behaviour serializes a snapshot
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="snapshot"></param>
        protected virtual void WriteSnapshot(NetworkWriter writer, T snapshot) => writer.Write(snapshot);
        /// <summary>
        /// Optional override for when you want to change how this behaviour serializes a snapshot
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="snapshot"></param>
        protected virtual T ReadSnapshot(NetworkReader reader) => reader.Read<T>();


        protected sealed override void ReadAndInsertSnapshot(NetworkReader reader, float insertTime)
        {
            var newSnapshot = ReadSnapshot(reader);

            if (IsServer)
            {
                // store latest client snapshot so server can forward it to other clients
                _clientAuthoritySnapshot = newSnapshot;
                _hasClientAuthoritySnapshot = true;
            }

            if (IsClient)
            {
                // insert a snapshot so that we have a starting point
                // time isn't important, just has to be before servertime
                if (_snapshotBuffer.IsEmpty)
                {
                    var created = CreateSnapshot(out var clientSnapshot, true);
                    Debug.Assert(created, "Snapshot not created when force was true");
                    _snapshotBuffer.AddSnapShot(clientSnapshot, insertTime - 0.1f);
                }
                _snapshotBuffer.AddSnapShot(newSnapshot, insertTime);
            }
            else
            {
                // apply snapshot right away on server
                ApplySnapshot(newSnapshot);
            }
        }

        public sealed override void WriteIfDirty(NetworkWriter metaWriter, PooledNetworkWriter dataWriter, bool includeWriteSize)
        {
            if (!GetChangedSnapShot(out var newSnapshot))
                return;

            _snapshot = newSnapshot;

            metaWriter.WriteNetworkBehaviour(this);
            var startPos = dataWriter.BitPosition;
            WriteSnapshot(dataWriter, _snapshot);

            // TODO use c# object as proxy instead of sending size
            //      we can then leave that project in the object dictionary for 1-2 seconds after it is destroyed so that we can continue to read the data
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
                return CreateSnapshot(out snapshot, false);
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

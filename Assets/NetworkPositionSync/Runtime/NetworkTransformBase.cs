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

using System;
using Mirage.Logging;
using Mirage.RemoteCalls;
using Mirage.Serialization;
using UnityEngine;

namespace Mirage.SyncPosition
{
    /// <summary>
    /// non generic base class that can be used too call functions from outside
    /// <para>
    /// If there is a <see cref="SyncPositionSystem"/> then it will sync this behaviour, batching the changes from many objects into a few messages.
    /// If there is no system then this behaviour syncs itself using RPCs (standalone mode).
    /// </para>
    /// </summary>
    public abstract class NetworkTransformBase : NetworkBehaviour
    {
        private static readonly ILogger logger = LogFactory.GetLogger<NetworkTransformBase>();

        [Tooltip("Set to true if moves come from owner client, set to false if moves always come from server")]
        [SerializeField] internal bool _clientAuthority = false;

        /// <summary>
        /// Set to true if moves come from owner client, set to false if moves always come from server
        /// <para>Should be set on both server and clients</para>
        /// </summary>
        public bool ClientAuthority
        {
            get => _clientAuthority;
            set => _clientAuthority = value;
        }

        [Header("Standalone")]
        [Tooltip("Only used when there is no SyncPositionSystem.\nHow often changes are sent")]
        [SerializeField] internal float _standaloneSyncInterval = 0.1f;
        [Tooltip("Only used when there is no SyncPositionSystem.\nDelay client uses so that it always has snapshots to interpolate between. Multiple of sync interval")]
        [SerializeField] internal float _standaloneInterpolationDelay = 2.5f;

        /// <summary>
        /// System that syncs this behaviour. If null then this behaviour syncs itself using RPCs
        /// </summary>
        internal SyncPositionSystem _system;

        /// <summary>
        /// True if this behaviour syncs itself using RPCs, false if it is synced by a <see cref="SyncPositionSystem"/>
        /// </summary>
        public bool IsStandalone => _system == null;

        /// <summary>
        /// Max possible size of write. Need to know if message needs to be split up
        /// </summary>
        public abstract int MaxWriteSize { get; }
        /// <summary>
        /// Called once when this behaviour starts syncing after it is spawned.
        /// <para>By <see cref="SyncPositionSystem"/> from the <see cref="NetworkWorld.onSpawn"/> event, or in standalone mode on the first update or message after spawning</para>
        /// </summary>
        public virtual void Setup() { }
        public abstract void WriteIfDirty(NetworkWriter headerWriter, PooledNetworkWriter dataWriter, bool includeWriteSize);
        /// <summary>
        /// Writes the snapshot if it has changed since it was last written
        /// </summary>
        /// <returns>true if snapshot was written</returns>
        public abstract bool WriteChanged(NetworkWriter writer);
        /// <summary>
        /// Makes next <see cref="WriteChanged"/> write even if unchanged, eg because last write was not sent
        /// </summary>
        public abstract void ForceNextWrite();
        public abstract void ClientUpdate(double viewTime, double removeTime);

        /// <summary>
        /// Error cost for each entry a client sends for an object it does not have client authority over
        /// </summary>
        public const int NO_AUTHORITY_ERROR_COST = 2;

        /// <param name="sender">Player that sent the data, null if it came from the server. When not null, data is only applied to behaviours that player has client authority over</param>
        public static void ReadAll(double insertTime, PooledNetworkReader metaReader, PooledNetworkReader dataReader, bool includeWriteSize, INetworkPlayer sender = null)
        {
            // each entry in meta is atleast 2 bytes: netId (packed, 1 byte if <= 240) and componentIndex (1 byte)
            // meta only contains whole bytes, so there are no left over bits that could be mistaken for an entry
            const int MIN_READ_SIZE = 2;

            while (metaReader.CanReadBytes(MIN_READ_SIZE))
            {
                var behavior = metaReader.ReadNetworkBehaviour<NetworkTransformBase>();

                // client can only send data for objects it owns and that use client authority
                // treat anything else like an unknown object, so it is skipped
                if (behavior != null && sender != null && !behavior.IsClientAuthorityFor(sender))
                {
                    if (logger.WarnEnabled()) logger.LogWarning($"Player {sender} sent position for {behavior.NetId} but does not have client authority over it");
                    // small cost because this can happen without cheating, eg if a message was sent just before authority was removed.
                    // a client that keeps doing it will still hit the error limit
                    sender.SetError(NO_AUTHORITY_ERROR_COST, PlayerErrorFlags.NoAuthority);
                    behavior = null;
                }

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
        protected abstract void ReadAndInsertSnapshot(NetworkReader reader, double insertTime);

        private bool IsClientAuthorityFor(INetworkPlayer player)
        {
            return _clientAuthority && Owner == player;
        }

        #region Standalone
        /// <summary>
        /// NetId when standalone state was last reset, used to detect when object is spawned again (eg when pooled)
        /// </summary>
        private uint _standaloneNetId;
        private double _standaloneNextSyncTime;
        private double _standalonePreviousTime;

        /// <summary>
        /// Clock that follows server time, used by remote clients to interpolate
        /// </summary>
        internal InterpolationTime _standaloneTime;

        /// <summary>
        /// Time of last <see cref="CmdSnapshot"/>, used to drop messages that arrive out of order
        /// </summary>
        private double _lastCmdTime;
        private INetworkPlayer _lastCmdOwner;

        private float StandaloneDelay => _standaloneSyncInterval * _standaloneInterpolationDelay;

        /// <summary>
        /// Sends and interpolates in standalone mode. Does nothing when synced by <see cref="SyncPositionSystem"/>
        /// <para>Call base.Update() if overriding</para>
        /// </summary>
        protected virtual void Update()
        {
            if (!IsStandalone || !Identity.IsSpawned)
                return;

            EnsureStandaloneStarted();

            var now = Time.unscaledTimeAsDouble;
            if (IsClient)
                StandaloneInterpolate(now);

            if (now > _standaloneNextSyncTime)
            {
                _standaloneNextSyncTime = now + _standaloneSyncInterval;
                StandaloneSend(now);
            }
        }

        private void EnsureStandaloneStarted()
        {
            // netId will be different each time object is spawned
            if (_standaloneNetId == NetId)
                return;

            _standaloneNetId = NetId;
            _standaloneNextSyncTime = 0;
            _standalonePreviousTime = Time.unscaledTimeAsDouble;
            _standaloneTime = new InterpolationTime(_standaloneSyncInterval, tickDelay: _standaloneInterpolationDelay);
            _lastCmdTime = 0;
            _lastCmdOwner = null;
            Setup();
        }

        private void StandaloneInterpolate(double now)
        {
            double viewTime;
            if (IsServer)
            {
                // host: snapshots from clients are inserted using local time, so can just use local time for interpolation
                viewTime = now - StandaloneDelay;
            }
            else
            {
                var deltaTime = now - _standalonePreviousTime;
                _standalonePreviousTime = now;
                _standaloneTime.OnUpdate((float)deltaTime);
                viewTime = _standaloneTime.Time;
            }

            ClientUpdate(viewTime, viewTime - (StandaloneDelay * 1.5f));
        }

        private void StandaloneSend(double now)
        {
            using (var writer = NetworkWriterPool.GetWriter())
            {
                if (!WriteChanged(writer))
                    return;

                var payload = writer.ToArraySegment();
                if (IsServer)
                {
                    // owner of client authority object doesn't need its own position
                    if (_clientAuthority && Owner != null)
                        RpcSnapshotExcludeOwner(now, payload);
                    else
                        RpcSnapshot(now, payload);
                }
                else
                {
                    // only the owner of a client authority object will have written anything
                    // time is client's local time, only used by server to drop old messages
                    try
                    {
                        CmdSnapshot(now, payload);
                    }
                    catch (ReturnRpcException e)
                    {
                        // Mirage checks the RateLimit on the client too, and throws instead of sending.
                        // make sure the next write sends, otherwise if the object stopped moving its final position would never be sent
                        if (logger.WarnEnabled()) logger.LogWarning($"Position for {NetId} not sent: {e.Message}");
                        ForceNextWrite();
                    }
                }
            }
        }

        [ClientRpc(channel = Channel.Unreliable, excludeHost = true)]
        private void RpcSnapshot(double serverTime, ArraySegment<byte> payload)
        {
            ClientReceiveSnapshot(serverTime, payload);
        }

        [ClientRpc(channel = Channel.Unreliable, excludeHost = true, excludeOwner = true)]
        private void RpcSnapshotExcludeOwner(double serverTime, ArraySegment<byte> payload)
        {
            ClientReceiveSnapshot(serverTime, payload);
        }

        private void ClientReceiveSnapshot(double serverTime, ArraySegment<byte> payload)
        {
            // client has a system, but server didn't. Ignore because they are not set up the same
            if (!IsStandalone)
                return;

            EnsureStandaloneStarted();

            // unreliable messages can arrive out of order, ignore old ones
            if (serverTime <= _standaloneTime.LatestServerTime)
                return;

            using (var reader = NetworkReaderPool.GetReader(payload, World))
            {
                ReadAndInsertSnapshot(reader, serverTime);
            }
            _standaloneTime.OnMessage(serverTime);
        }

        // RateLimit is for each player, shared between all objects they own.
        // 200 per second allows eg 10 objects at 20 updates per second. Use SyncPositionSystem if more is needed, it sends batched messages instead
        [ServerRpc(channel = Channel.Unreliable)]
        [RateLimit(Interval = 1, Refill = 200, MaxTokens = 400, Penalty = 1)]
        private void CmdSnapshot(double clientTime, ArraySegment<byte> payload)
        {
            // ServerRpc already checks that sender is owner (requireAuthority)
            if (!_clientAuthority)
            {
                if (logger.WarnEnabled()) logger.LogWarning($"Player {Owner} sent position for {NetId} but it does not use client authority");
                Owner.SetError(NO_AUTHORITY_ERROR_COST, PlayerErrorFlags.NoAuthority);
                return;
            }

            // new owner will have a different clock
            if (_lastCmdOwner != Owner)
            {
                _lastCmdOwner = Owner;
                _lastCmdTime = double.MinValue;
            }

            // unreliable messages can arrive out of order, ignore old ones
            if (clientTime <= _lastCmdTime)
                return;
            _lastCmdTime = clientTime;

            EnsureStandaloneStarted();

            // insert using server time, client time is not linked to server time
            using (var reader = NetworkReaderPool.GetReader(payload, World))
            {
                ReadAndInsertSnapshot(reader, Time.unscaledTimeAsDouble);
            }
        }
        #endregion
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

        /// <summary>
        /// Current Snapshot
        /// </summary>
        protected T _snapshot;
        protected internal SnapshotBuffer<T> _snapshotBuffer;

        /// <summary>
        /// Is this client the owner of an object using client authority
        /// </summary>
        private bool IsLocalClientInControl => _clientAuthority && HasAuthority;

        private bool _forceNextWrite;
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


        public override bool OnSerialize(NetworkWriter writer, bool initialState)
        {
            var written = base.OnSerialize(writer, initialState);

            // include current snapshot in spawn payload,
            // so that new observers start at the correct position even if the object is not moving
            // (spawn message only has root transform's values, and not for local/relative coordinates)
            if (initialState)
            {
                CreateSnapshot(out var snapshot, true);
                WriteSnapshot(writer, snapshot);
                written = true;
            }

            return written;
        }

        public override void OnDeserialize(NetworkReader reader, bool initialState)
        {
            base.OnDeserialize(reader, initialState);

            if (initialState)
            {
                var snapshot = ReadSnapshot(reader);
                // apply right away, there is nothing to interpolate from yet
                _snapshotBuffer.ClearBuffer();
                ApplySnapshot(snapshot);
            }
        }

        protected sealed override void ReadAndInsertSnapshot(NetworkReader reader, double insertTime)
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
                // server also sends client authority objects back to their owner,
                // owner is in control so ignore them, otherwise buffer will grow forever because ClientUpdate doesn't remove them
                if (IsLocalClientInControl)
                    return;

                // insert a snapshot so that we have a starting point
                // time isn't important, just has to be before serverTime
                if (_snapshotBuffer.IsEmpty)
                {
                    var created = CreateSnapshot(out var clientSnapshot, true);
                    Debug.Assert(created, "Snapshot not created when force was true");
                    _snapshotBuffer.AddSnapShot(clientSnapshot, insertTime - 0.1);
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
            var startPos = dataWriter.BitPosition;
            if (!WriteChanged(dataWriter))
                return;

            metaWriter.WriteNetworkBehaviour(this);

            // TODO use c# object as proxy instead of sending size
            //      we can then leave that project in the object dictionary for 1-2 seconds after it is destroyed so that we can continue to read the data
            if (includeWriteSize)
            {
                var endPos = dataWriter.BitPosition;
                var size = endPos - startPos;
                metaWriter.WritePackedUInt32((uint)size);
            }
        }

        public sealed override bool WriteChanged(NetworkWriter writer)
        {
            if (!GetChangedSnapShot(out var newSnapshot))
                return false;

            _snapshot = newSnapshot;
            _forceNextWrite = false;
            WriteSnapshot(writer, _snapshot);
            return true;
        }

        public sealed override void ForceNextWrite()
        {
            _forceNextWrite = true;
        }

        private bool GetChangedSnapShot(out T snapshot)
        {
            // client can only send objects that it controls,
            // checked here (instead of when adding to send list) so it is correct even if authority or _clientAuthority changes
            if (!IsServer && !IsLocalClientInControl)
            {
                snapshot = default;
                return false;
            }

            // if client auth, then we dont need to create and check if snapshot is changed,
            // server can just forward it
            if (_clientAuthority && Owner != null && Owner != Server.LocalPlayer)
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
                return CreateSnapshot(out snapshot, _forceNextWrite);
            }
        }

        public sealed override void ClientUpdate(double viewTime, double removeTime)
        {
            // only run Interpolation for remote owner
            if (IsLocalClientInControl)
                return;

            if (_snapshotBuffer.IsEmpty)
                return;

            var snapshot = _snapshotBuffer.GetLinearInterpolation(viewTime);
            ApplySnapshot(snapshot);

            _snapshotBuffer.RemoveOldSnapshots(removeTime);
        }
    }
}

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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Mirage;
using Mirage.Logging;
using Mirage.Serialization;
using UnityEngine;

namespace JamesFrowen.PositionSync
{
    public class NetworkTransform3D : NetworkTransformBase
    {
        [SerializeField] private PackSettings3D _packSettings;
        [SerializeField] private float _positionSensitivity = 0.01f;
        [SerializeField] private float _rotationSensitivity = 0.01f;

        private VarVector3Packer _positionPacker;
        private QuaternionPacker _rotationPacker;

        private Vector3 _previousPosition;
        private Quaternion _previousRotation;

        private readonly SnapshotBuffer<Snapshot> snapshotBuffer = new SnapshotBuffer<Snapshot>(Snapshot.CreateInterpolator());

        private void Awake()
        {
            Identity.OnStartServer.AddListener(NetworkStart);
            Identity.OnStartClient.AddListener(NetworkStart);
        }

        public void NetworkStart()
        {
            _positionPacker = _packSettings.GetPositionPacker();
            _rotationPacker = _packSettings.GetRotationPacker();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector3 GetPosition() => useLocalSpace ? transform.localPosition : transform.position;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Quaternion GetRotation() => useLocalSpace ? transform.localRotation : transform.rotation;

        protected override bool IsDirty()
        {
            var pos = GetPosition();
            var rot = GetRotation();
            if (HasMoved(pos) || HasRotated(rot))
            {
                _previousPosition = pos;
                _previousRotation = rot;
                return true;
            }

            return false;
        }

        private bool HasMoved(Vector3 newPos)
        {
            return Vector3.Distance(newPos, _previousPosition) > _positionSensitivity;
        }

        private bool HasRotated(Quaternion newRot)
        {
            return Quaternion.Angle(newRot, _previousRotation) > _rotationSensitivity;
        }

        protected override void WriteValues(NetworkWriter writer)
        {
            // we store values in these fields when Isdirty is called,
            // so get them from that instead of transform again
            _positionPacker.Pack(writer, _previousPosition);
            _rotationPacker.Pack(writer, _previousRotation);
        }
        protected override void ReadValuesIntoBuffer(NetworkReader reader, float serverTime)
        {
            var pos = _positionPacker.Unpack(reader);
            var rot = _rotationPacker.Unpack(reader);

            if (snapshotBuffer.IsEmpty)
                // insert a snapshot so that we have a starting point
                // time isn't important, just has to be before servertime
                snapshotBuffer.AddSnapShot(new Snapshot(GetPosition(), GetRotation()), serverTime - 0.1f);
            snapshotBuffer.AddSnapShot(new Snapshot(pos, rot), serverTime);
        }

        public struct Snapshot
        {
            public readonly Vector3 position;
            public readonly Quaternion rotation;

            public Snapshot(Vector3 position, Quaternion rotation)
            {
                this.position = position;
                this.rotation = rotation;
            }

            public override string ToString()
            {
                return $"[{position}, {rotation}]";
            }

            public static ISnapshotInterpolator<Snapshot> CreateInterpolator() => new Interpolator();

            private class Interpolator : ISnapshotInterpolator<Snapshot>
            {
                public Snapshot Lerp(Snapshot a, Snapshot b, float alpha)
                {
                    var pos = Vector3.Lerp(a.position, b.position, alpha);
                    var rot = Quaternion.Slerp(a.rotation, b.rotation, alpha);
                    return new Snapshot(pos, rot);
                }
            }
        }
    }
    public class NetworkTransform2D : NetworkTransformBase
    {
        [SerializeField] private PackSettings2D _packSettings;
        [SerializeField] private float _positionSensitivity = 0.01f;
        [SerializeField] private float _rotationSensitivity = 0.01f;

        private VarVector2Packer _positionPacker;
        private AnglePacker _rotationPacker;

        private Vector2 _previousPosition;
        private float _previousRotation;

        private readonly SnapshotBuffer<Snapshot> snapshotBuffer = new SnapshotBuffer<Snapshot>(Snapshot.CreateInterpolator());

        private void Awake()
        {
            Identity.OnStartServer.AddListener(NetworkStart);
            Identity.OnStartClient.AddListener(NetworkStart);
        }

        public void NetworkStart()
        {
            _positionPacker = _packSettings.GetPositionPacker();
            _rotationPacker = _packSettings.GetRotationPacker();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector2 GetPosition() => useLocalSpace ? transform.localPosition : transform.position;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetRotation() => useLocalSpace ? transform.localEulerAngles.z : transform.eulerAngles.z;

        protected override bool IsDirty()
        {
            var pos = GetPosition();
            var rot = GetRotation();
            if (HasMoved(pos) || HasRotated(rot))
            {
                _previousPosition = pos;
                _previousRotation = rot;
                return true;
            }

            return false;
        }

        private bool HasMoved(Vector2 newPos)
        {
            return Vector2.Distance(newPos, _previousPosition) > _positionSensitivity;
        }

        private bool HasRotated(float newRot)
        {
            return Mathf.Abs(Mathf.DeltaAngle(newRot, _previousRotation)) > _rotationSensitivity;
        }

        protected override void WriteValues(NetworkWriter writer)
        {
            // we store values in these fields when Isdirty is called,
            // so get them from that instead of transform again
            _positionPacker.Pack(writer, _previousPosition);
            _rotationPacker.Pack(writer, _previousRotation);
        }

        protected override void ReadValuesIntoBuffer(NetworkReader reader, float serverTime)
        {
            var pos = _positionPacker.Unpack(reader);
            var rot = _rotationPacker.Unpack(reader);

            if (snapshotBuffer.IsEmpty)
                // insert a snapshot so that we have a starting point
                // time isn't important, just has to be before servertime
                snapshotBuffer.AddSnapShot(new Snapshot(GetPosition(), GetRotation()), serverTime - 0.1f);
            snapshotBuffer.AddSnapShot(new Snapshot(pos, rot), serverTime);
        }

        public struct Snapshot
        {
            public readonly Vector2 position;
            public readonly float rotation;

            public Snapshot(Vector2 position, float rotation)
            {
                this.position = position;
                this.rotation = rotation;
            }

            public override string ToString()
            {
                return $"[{position}, {rotation}]";
            }

            public static ISnapshotInterpolator<Snapshot> CreateInterpolator() => new Interpolator();

            private class Interpolator : ISnapshotInterpolator<Snapshot>
            {
                public Snapshot Lerp(Snapshot a, Snapshot b, float alpha)
                {
                    var pos = Vector2.Lerp(a.position, b.position, alpha);
                    var rot = Mathf.LerpAngle(a.rotation, b.rotation, alpha);
                    return new Snapshot(pos, rot);
                }
            }
        }
    }

    // todo , can we make this generic?
    public abstract class NetworkTransformBase : NetworkBehaviour
    {
        [Tooltip("If true, we will use local position and rotation. If false, we use world position and rotation.")]
        [SerializeField] protected bool useLocalSpace = true;

        [Header("Authority")]
        [Tooltip("Set to true if moves come from owner client, set to false if moves always come from server")]
        [SerializeField] private bool clientAuthority = false;

        protected abstract bool IsDirty();
        protected abstract void WriteValues(NetworkWriter writer);
        protected abstract void ReadValuesIntoBuffer(NetworkReader reader, float time);


        public void WriteIfDirty(NetworkWriter writer)
        {
            if (!IsDirty())
                return;

            writer.WriteNetworkBehaviour(this);
            WriteValues(writer);
        }


        // assume 1 state is atleast 3 bytes
        // (it should be more, but there shouldn't be random left over bits in reader so 3 is enough for check)
        private const int MIN_READ_SIZE = 3;
        public static void ReadAll(PooledNetworkReader reader, float insertTime)
        {
            while (reader.CanReadBytes(MIN_READ_SIZE))
            {
                var behavior = reader.ReadNetworkBehaviour<NetworkTransformBase>();
                behavior.ReadValuesIntoBuffer(reader, insertTime);
            }
        }
    }


    [Serializable]
    public enum SyncMode
    {
        SendToAll = 1,
        SendToObservers,
    }

    /// <summary>
    /// Systems that sync <see cref="SyncPositionBehaviour"/>
    ///
    /// <para>
    /// Optimized version of <see cref="SyncPositionBehaviour_StandAlone"/> that sends Position of multiple objects instead of just one at once
    /// </para>
    /// </summary>
    [AddComponentMenu("Network/SyncPosition/SyncPositionSystem")]
    public class SyncPositionSystem : MonoBehaviour
    {
        private static readonly ILogger logger = LogFactory.GetLogger<SyncPositionSystem>();

        // todo add maxMessageSize (splits up update message into multiple messages if too big)

        public NetworkClient Client;
        public NetworkServer Server;

        [Tooltip("SendToAll option skips visibility and sends position to all ready connections.")]
        [SerializeField] private SyncMode _syncMode = SyncMode.SendToAll;
        [SerializeField] private float _syncInterval = 0.1f;
        [SerializeField] private SyncTiming _intervalTiming = SyncTiming.Variable;
        [SerializeField] private float _interpolationDelay = 2.5f;
        private float _nextSyncTime;
        private int _maxMessageSize;

        private readonly Dictionary<INetworkPlayer, PooledNetworkWriter> _writerPool = new Dictionary<INetworkPlayer, PooledNetworkWriter>();

        private readonly List<NetworkTransformBase> _behaviours = new List<NetworkTransformBase>();

        public InterpolationTime InterpolationTime { [MethodImpl(MethodImplOptions.AggressiveInlining)] get; private set; }
        public bool ClientActive => Client != null && Client.Active;
        public bool ServerActive => Server != null && Server.Active;

        internal void Awake()
        {
            InterpolationTime = new InterpolationTime(_syncInterval, tickDelay: _interpolationDelay);

            Server?.Started.AddListener(ServerStarted);
            Client?.Started.AddListener(ClientStarted);

            Server?.Stopped.AddListener(ServerStopped);
            Client?.Disconnected.AddListener(ClientStopped);
        }

        private void OnDestroy()
        {
            Server?.Started.RemoveListener(ServerStarted);
            Client?.Started.RemoveListener(ClientStarted);
            Server?.Stopped.RemoveListener(ServerStopped);
            Client?.Disconnected.RemoveListener(ClientStopped);
        }

        private void ClientStarted()
        {
            // nothing to do in host mode
            if (ServerActive)
                return;

            Client.MessageHandler.RegisterHandler<PositionMessage>(ClientHandleNetworkPositionMessage);
            var world = Client.World;
            AddWorldEvents(world);
        }

        private void ServerStarted()
        {
            Server.MessageHandler.RegisterHandler<PositionMessage>(ServerHandleNetworkPositionMessage);
        }

        private void ClientStopped(ClientStoppedReason arg0)
        {
            // nothing to do in host mode
            if (ServerActive)
                return;

            _behaviours.Clear();
        }

        private void ServerStopped()
        {
            _behaviours.Clear();
        }

        private void AddWorldEvents(NetworkWorld world)
        {
            world.onSpawn += World_onSpawn;
            world.onUnspawn += World_onUnspawn;
            foreach (var identity in Client.World.SpawnedIdentities)
            {
                World_onSpawn(identity);
            }
        }

        private static readonly List<NetworkTransformBase> _getCache = new List<NetworkTransformBase>();
        private void World_onSpawn(NetworkIdentity obj)
        {
            obj.gameObject.GetComponentsInChildren(true, _getCache);
            for (var i = 0; i < _getCache.Count; i++)
                _behaviours.Add(_getCache[i]);
        }
        private void World_onUnspawn(NetworkIdentity obj)
        {
            obj.gameObject.GetComponentsInChildren(true, _getCache);
            for (var i = 0; i < _getCache.Count; i++)
                _behaviours.Remove(_getCache[i]);
        }

        private void Update()
        {
            InterpolationTime.OnUpdate(Time.deltaTime);
        }

        private void LateUpdate()
        {
            if (ServerActive)
            {
                var now = Time.time;
                if (now > _nextSyncTime)
                {
                    SyncSettings.UpdateTime(_syncInterval, _intervalTiming, ref _nextSyncTime, now);
                    ServerUpdate(now);
                }
            }
        }
        private void ServerUpdate(float time)
        {
            // syncs every frame, each Behaviour will track its own timer
            switch (_syncMode)
            {
                case SyncMode.SendToAll:
                    SendToAll(time);
                    break;
                case SyncMode.SendToObservers:
                    SendToObservers(time);
                    break;
            }
        }

        internal void SendToAll(float time)
        {
            using (var writer = NetworkWriterPool.GetWriter())
            {
                var msg = new PositionMessage
                {
                    time = time,
                };

                var hasSent = false;
                foreach (var behaviour in _behaviours)
                {
                    behaviour.WriteIfDirty(writer);

                    // send if full
                    if (writer.ByteLength + PositionMessage.FRAGMENT_SPLIT_LIMIT > _maxMessageSize)
                    {
                        msg.payload = writer.ToArraySegment();
                        Server.SendToAll(msg, Channel.Unreliable);
                        writer.Reset();
                        hasSent = true;
                    }
                }

                // small chance that we send msg above at max size, and then get here with empty writer.
                // if empty, but has already send full payload previously
                if (hasSent && writer.ByteLength == 0)
                    return;

                // send even if empty, we always want too tell client the time
                msg.payload = writer.ToArraySegment();
                Server.SendToAll(msg, Channel.Unreliable);
            }
        }

        /// <summary>
        /// Loops through all dirty objects, and then their observers and then writes that behaviouir to a cahced writer
        /// <para>But Packs once and copies bytes</para>
        /// </summary>
        /// <param name="time"></param>
        internal void SendToObservers(float time)
        {
            var msg = new PositionMessage
            {
                time = time,
            };

            var hostPlayer = Server.LocalPlayer;

            using (var packWriter = NetworkWriterPool.GetWriter())
            {
                foreach (var behaviour in _behaviours)
                {
                    // no observers, dont need to check if we should write
                    if (behaviour.Identity.observers.Count == 0)
                        continue;

                    // pack behaviour into writer
                    packWriter.Reset();
                    behaviour.WriteIfDirty(packWriter);

                    // copy from writer into buffers for each observers
                    foreach (var observer in behaviour.Identity.observers)
                    {
                        // we never need to send from server to host player
                        if (observer == hostPlayer)
                            continue;

                        // get or create
                        if (!_writerPool.TryGetValue(observer, out var writer))
                        {
                            writer = NetworkWriterPool.GetWriter();
                            _writerPool[observer] = writer;
                        }

                        writer.CopyFromWriter(packWriter);
                        // send to this player if full
                        if (writer.ByteLength + PositionMessage.FRAGMENT_SPLIT_LIMIT > _maxMessageSize)
                        {
                            msg.payload = writer.ToArraySegment();
                            observer.Send(msg, Channel.Unreliable);
                            writer.Reset();
                        }
                    }
                }
            }


            foreach (var player in Server.Players)
            {
                // if no writer, then no objects were written to this player
                if (_writerPool.TryGetValue(player, out var writer))
                    msg.payload = writer.ToArraySegment();

                // send even if no payload, player still needs time
                player.Send(msg, Channel.Unreliable);

                // release and remove writer
                writer?.Release();
                _writerPool.Remove(player);
            }
            // release any extra writers,
            // there should be none, but it would be leak if we dont check
            foreach (var writer in _writerPool.Values)
                writer.Release();
            _writerPool.Clear();
        }

        internal void ClientHandleNetworkPositionMessage(PositionMessage msg)
        {
            // hostMode
            if (ServerActive)
                return;

            using (var reader = NetworkReaderPool.GetReader(msg.payload, null))
            {
                var time = msg.time;

                // todo IMPORTANT ensure old message are dropped, otherwise snapshot buffer will throw

                NetworkTransformBase.ReadAll(reader, time);

                InterpolationTime.OnMessage(time);
            }
        }

        /// <summary>
        /// Position from client to server
        /// </summary>
        internal void ServerHandleNetworkPositionMessage(INetworkPlayer _, PositionMessage msg)
        {
            using (var reader = NetworkReaderPool.GetReader(msg.payload, null))
            {
                //float time = packer.UnpackTime(reader);
                packer.UnpackNext(reader, out var id, out var pos, out var rot);

                if (_behaviours.Lookup.TryGetValue(id, out var behaviour))
                    // todo fix host mode time
                    behaviour.ApplyOnServer(new TransformState(pos, rot), timer.Now);
                else
                    if (logger.WarnEnabled())
                    logger.LogWarning($"Could not find a NetworkBehaviour with id {id}");
            }
        }
    }

    [NetworkMessage]
    public struct PositionMessage
    {
        /// <summary>
        /// how close to MTU we can get before sending 2 message instead of 1
        /// </summary>
        // Header (msgId,time,payload) + max size of 1 behaviour (worse case)
        public const int FRAGMENT_SPLIT_LIMIT = 10 + 12 + 16;

        public float time;
        public ArraySegment<byte> payload;
    }
}

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
    /// <summary>
    /// Systems that sync <see cref="NetworkTransformBase"/>
    /// <para>
    /// Optimized version of <see cref="SyncPositionBehaviour_StandAlone"/> that sends Position of multiple objects instead of just one at once
    /// </para>
    /// </summary>
    [AddComponentMenu("Network/SyncPosition/SyncPositionSystem")]
    public class SyncPositionSystem : MonoBehaviour
    {
        private static readonly ILogger logger = LogFactory.GetLogger<SyncPositionSystem>();
        private static readonly List<NetworkTransformBase> _getCache = new List<NetworkTransformBase>();

        // todo add maxMessageSize (splits up update message into multiple messages if too big)

        public NetworkClient Client;
        public NetworkServer Server;

        [Tooltip("SendToAll option skips visibility and sends position to all ready connections.")]
        [SerializeField] private SyncMode _syncMode = SyncMode.SendToAll;
        [SerializeField] private float _syncInterval = 0.1f;
        [SerializeField] private SyncTiming _intervalTiming = SyncTiming.Variable;
        [SerializeField] private float _interpolationDelay = 2.5f;
        private float _nextSyncTime;
        private int _maxPacketSize;

        private readonly Dictionary<INetworkPlayer, PooledNetworkWriter> _writerPool = new Dictionary<INetworkPlayer, PooledNetworkWriter>();

        private readonly List<NetworkTransformBase> _behaviours = new List<NetworkTransformBase>();
        private readonly List<NetworkTransformBase> _clientAuthority = new List<NetworkTransformBase>();

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

            _maxPacketSize = Client.SocketFactory.MaxPacketSize;
            AddWorldEvents(Client.World);
            Client.MessageHandler.RegisterHandler<PositionMessage>(ClientHandleNetworkPositionMessage);
        }

        private void ServerStarted()
        {
            _maxPacketSize = Client.SocketFactory.MaxPacketSize;
            AddWorldEvents(Client.World);
            Server.MessageHandler.RegisterHandler<PositionMessage>(ServerHandleNetworkPositionMessage);
        }

        private void ClientStopped(ClientStoppedReason arg0)
        {
            // nothing to do in host mode
            if (ServerActive)
                return;

            _behaviours.Clear();
            _clientAuthority.Clear();
        }

        private void ServerStopped()
        {
            _behaviours.Clear();
        }

        private void AddWorldEvents(NetworkWorld world)
        {
            world.onUnspawn += World_onUnspawn;
            world.AddAndInvokeOnSpawn(World_onSpawn);
            world.AddAndInvokeOnAuthorityChanged(World_onAuthorityChanged);
        }

        private void World_onSpawn(NetworkIdentity identity)
        {
            identity.gameObject.GetComponentsInChildren(true, _getCache);
            for (var i = 0; i < _getCache.Count; i++)
                _behaviours.Add(_getCache[i]);
        }
        private void World_onUnspawn(NetworkIdentity identity)
        {
            identity.gameObject.GetComponentsInChildren(true, _getCache);
            for (var i = 0; i < _getCache.Count; i++)
                _behaviours.Remove(_getCache[i]);
        }
        private void World_onAuthorityChanged(NetworkIdentity identity, bool hasAuthority, INetworkPlayer owner)
        {
            if (!ClientActive)
                return;

            identity.gameObject.GetComponentsInChildren(true, _getCache);
            if (hasAuthority)
            {
                for (var i = 0; i < _getCache.Count; i++)
                    _clientAuthority.Add(_getCache[i]);
            }
            else
            {
                for (var i = 0; i < _getCache.Count; i++)
                    _behaviours.Remove(_getCache[i]);
            }
        }

        private void Update()
        {
            if (!ClientActive)
                return;

            InterpolationTime.OnUpdate(Time.deltaTime);

            var snapshotTime = InterpolationTime.Time;
            var removeTime = snapshotTime - (InterpolationTime.ClientDelay * 1.5f);
            foreach (var behaviour in _behaviours)
            {
                behaviour.ClientUpdate(snapshotTime, removeTime);
            }
        }

        private void LateUpdate()
        {
            var now = Time.time;
            if (now > _nextSyncTime)
            {
                SyncSettings.UpdateTime(_syncInterval, _intervalTiming, ref _nextSyncTime, now);
                if (ServerActive)
                {
                    ServerUpdate(now);
                }
                else if (ClientActive) // client only
                {
                    OwnerUpdate(now);
                }
            }
        }

        internal void OwnerUpdate(float time)
        {
            // no owned objects, nothing to send to server
            if (_clientAuthority.Count == 0)
                return;

            SendAllDirtyObjects(time, _clientAuthority, _maxPacketSize, (msg) => Client.Send(msg, Channel.Unreliable));
        }

        /// <summary>
        /// shared method for server and owner udpates
        /// </summary>
        /// <param name=""></param>
        /// <param name="send"></param>
        private static void SendAllDirtyObjects(float time, List<NetworkTransformBase> behaviours, int maxPacketSize, Action<PositionMessage> send)
        {
            using (var writer = NetworkWriterPool.GetWriter())
            {
                var msg = new PositionMessage
                {
                    time = time,
                };

                var hasSent = false;
                foreach (var behaviour in behaviours)
                {
                    behaviour.WriteIfDirty(writer);

                    // send if full
                    if (writer.ByteLength + PositionMessage.FRAGMENT_SPLIT_LIMIT > maxPacketSize)
                    {
                        msg.payload = writer.ToArraySegment();
                        send.Invoke(msg);
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
                send.Invoke(msg);
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

        private void SendToAll(float time)
        {
            SendAllDirtyObjects(time, _behaviours, _maxPacketSize, (msg) => Server.SendToAll(msg, Channel.Unreliable));
        }

        /// <summary>
        /// Loops through all dirty objects, and then their observers and then writes that behaviouir to a cahced writer
        /// <para>But Packs once and copies bytes</para>
        /// </summary>
        /// <param name="time"></param>
        private void SendToObservers(float time)
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
                        if (writer.ByteLength + PositionMessage.FRAGMENT_SPLIT_LIMIT > _maxPacketSize)
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

        private void ClientHandleNetworkPositionMessage(PositionMessage msg)
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
            throw new NotImplementedException();

            //using (var reader = NetworkReaderPool.GetReader(msg.payload, null))
            //{
            //    //float time = packer.UnpackTime(reader);
            //    packer.UnpackNext(reader, out var id, out var pos, out var rot);

            //    if (_behaviours.Lookup.TryGetValue(id, out var behaviour))
            //        // todo fix host mode time
            //        behaviour.ApplyOnServer(new TransformState(pos, rot), timer.Now);
            //    else
            //        if (logger.WarnEnabled())
            //        logger.LogWarning($"Could not find a NetworkBehaviour with id {id}");
            //}
        }


        [Serializable]
        public enum SyncMode
        {
            SendToAll = 1,
            SendToObservers,
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
}

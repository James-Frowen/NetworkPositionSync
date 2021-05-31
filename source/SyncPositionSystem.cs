using Mirror;
using System;
using UnityEngine;
using BitReader = JamesFrowen.BitPacking.NetworkReader;
using BitWriter = JamesFrowen.BitPacking.NetworkWriter;

namespace JamesFrowen.PositionSync
{
    [AddComponentMenu("Network/SyncPosition/SyncPositionSystem")]
    public class SyncPositionSystem : MonoBehaviour
    {
        // todo make this work with network Visibility
        // todo add maxMessageSize (splits up update message into multiple messages if too big)
        // todo test sync interval vs fixed hz 

        [Header("Reference")]
        [SerializeField] SyncPositionBehaviourRuntimeDictionary _behaviours;
        [SerializeField] SyncPositionPacker packer;

        [Header("Sync")]
        [Tooltip("How often 1 behaviour should update")]
        public float syncInterval = 0.1f;
        [Tooltip("Check if behaviours need update every frame, If false then checks every syncInterval")]
        public bool checkEveryFrame = true;
        [Tooltip("Skips Visibility and sends position to all ready connections")]
        public bool sendToAll = true;

        [NonSerialized] float nextSyncInterval;


        private void OnValidate()
        {
            if (!this.sendToAll)
            {
                this.sendToAll = true;
                UnityEngine.Debug.LogWarning("sendToAll disabled is not implemented yet");
            }
        }

        private void OnDrawGizmos()
        {
            if (this.packer != null)
                this.packer.DrawGizmo();
        }

        public void RegisterHandlers()
        {
            // todo find a way to register these handles so it doesn't need to be done from NetworkManager
            if (NetworkClient.active)
            {
                NetworkClient.RegisterHandler<NetworkPositionMessage>(this.ClientHandleNetworkPositionMessage);
            }

            if (NetworkServer.active)
            {
                NetworkServer.RegisterHandler<NetworkPositionSingleMessage>(this.ServerHandleNetworkPositionMessage);
            }
        }

        public void UnregisterHandlers()
        {
            // todo find a way to unregister these handles so it doesn't need to be done from NetworkManager
            if (NetworkClient.active)
            {
                NetworkClient.UnregisterHandler<NetworkPositionMessage>();
            }

            if (NetworkServer.active)
            {
                NetworkServer.UnregisterHandler<NetworkPositionSingleMessage>();
            }
        }

        #region Sync Server -> Client
        [ServerCallback]
        private void LateUpdate()
        {
            if (this.checkEveryFrame || this.ShouldSync())
            {
                this.SendUpdateToAll();
            }
        }

        bool ShouldSync()
        {
            var now = Time.time;
            if (now > this.nextSyncInterval)
            {
                this.nextSyncInterval += this.syncInterval;
                return true;
            }
            else
            {
                return false;
            }
        }

        internal void SendUpdateToAll()
        {
            // dont send message if no behaviours
            if (this._behaviours.Count == 0) { return; }

            // todo dont create new buffer each time
            var bitWriter = new BitWriter(this._behaviours.Count * 32);
            var anyNeedUpdate = this.PackBehaviours(bitWriter, (float)NetworkTime.time);

            // dont send anything if nothing was written (eg, nothing dirty)
            if (!anyNeedUpdate) { return; }

            var tempBuffer = new byte[1300];
            NetworkServer.SendToAll(new NetworkPositionMessage
            {
                payload = new ArraySegment<byte>(tempBuffer)
            });
        }

        internal bool PackBehaviours(BitWriter bitWriter, float time)
        {
            this.packer.PackTime(bitWriter, time);
            var anyNeedUpdate = false;
            foreach (var behaviour in this._behaviours)
            {
                if (!behaviour.NeedsUpdate())
                    continue;

                anyNeedUpdate = true;

                this.packer.PackNext(bitWriter, behaviour);

                // todo handle client authority updates better
                behaviour.ClearNeedsUpdate(this.syncInterval);
            }
            return anyNeedUpdate;
        }

        internal void ClientHandleNetworkPositionMessage(NetworkConnection _conn, NetworkPositionMessage msg)
        {
            var length = msg.payload.Count;
            // todo stop alloc
            using (var bitReader = new BitReader())
            {
                bitReader.Reset(msg.payload);
                var time = this.packer.UnpackTime(bitReader);
                var count = this.packer.UnpackCount(bitReader);

                for (uint i = 0; i < count; i++)
                {
                    this.packer.UnpackNext(bitReader, out var id, out var pos, out var rot);

                    if (this._behaviours.TryGet(id, out var behaviour))
                    {
                        behaviour.ApplyOnClient(new TransformState(pos, rot), time);
                    }

                }

                // todo check we need this
                foreach (var item in this._behaviours)
                {
                    item.OnServerTime(time);
                }
            }
        }

        #endregion


        #region Sync Client Auth -> Server


        /// <summary>
        /// Position from client to server
        /// </summary>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        internal void ServerHandleNetworkPositionMessage(NetworkConnection _conn, NetworkPositionSingleMessage msg)
        {
            var length = msg.payload.Count;
            // todo stop alloc
            using (var bitReader = new BitReader())
            {
                bitReader.Reset(msg.payload);

                var time = this.packer.UnpackTime(bitReader);
                this.packer.UnpackNext(bitReader, out var id, out var pos, out var rot);

                if (this._behaviours.TryGet(id, out var behaviour))
                {
                    behaviour.ApplyOnServer(new TransformState(pos, rot), time);
                }
            }
        }
        #endregion
    }

    public struct NetworkPositionMessage : NetworkMessage
    {
        public ArraySegment<byte> payload;
    }
    public struct NetworkPositionSingleMessage : NetworkMessage
    {
        public ArraySegment<byte> payload;
    }
}

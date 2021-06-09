using System;
using Mirror;
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
        public SyncPositionPacker packer;

        [NonSerialized] float nextSyncInterval;

        private void OnDrawGizmos()
        {
            if (packer != null)
                packer.DrawGizmo();
        }

        public void RegisterHandlers()
        {
            // todo find a way to register these handles so it doesn't need to be done from NetworkManager
            if (NetworkClient.active)
            {
                NetworkClient.RegisterHandler<NetworkPositionMessage>(ClientHandleNetworkPositionMessage);
            }

            if (NetworkServer.active)
            {
                NetworkServer.RegisterHandler<NetworkPositionSingleMessage>(ServerHandleNetworkPositionMessage);
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

        private void Awake()
        {
            packer.SetSystem(this);
        }
        private void OnDestroy()
        {
            packer.ClearSystem(this);
        }

        #region Sync Server -> Client
        [ServerCallback]
        private void LateUpdate()
        {
            if (packer.checkEveryFrame || ShouldSync())
            {
                SendUpdateToAll();
            }
        }

        bool ShouldSync()
        {
            float now = Time.time;
            if (now > nextSyncInterval)
            {
                nextSyncInterval += packer.syncInterval;
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
            if (packer.Behaviours.Count == 0) { return; }

            // todo dont create new buffer each time
            var bitWriter = new BitWriter(packer.Behaviours.Count * 32);
            bool anyNeedUpdate = PackBehaviours(bitWriter, (float)NetworkTime.time);

            // dont send anything if nothing was written (eg, nothing dirty)
            if (!anyNeedUpdate) { return; }

            byte[] tempBuffer = new byte[1300];
            NetworkServer.SendToAll(new NetworkPositionMessage
            {
                payload = new ArraySegment<byte>(tempBuffer)
            });
        }

        internal bool PackBehaviours(BitWriter bitWriter, float time)
        {
            packer.PackTime(bitWriter, time);
            bool anyNeedUpdate = false;
            foreach (SyncPositionBehaviour behaviour in packer.Behaviours.Values)
            {
                if (!behaviour.NeedsUpdate())
                    continue;

                anyNeedUpdate = true;

                packer.PackNext(bitWriter, behaviour);

                // todo handle client authority updates better
                behaviour.ClearNeedsUpdate(packer.syncInterval);
            }
            return anyNeedUpdate;
        }

        internal void ClientHandleNetworkPositionMessage(NetworkPositionMessage msg)
        {
            int length = msg.payload.Count;
            // todo stop alloc
            using (var bitReader = new BitReader())
            {
                bitReader.Reset(msg.payload);
                float time = packer.UnpackTime(bitReader);
                ulong count = packer.UnpackCount(bitReader);

                for (uint i = 0; i < count; i++)
                {
                    packer.UnpackNext(bitReader, out uint id, out Vector3 pos, out Quaternion rot);

                    if (packer.Behaviours.TryGetValue(id, out SyncPositionBehaviour behaviour))
                    {
                        behaviour.ApplyOnClient(new TransformState(pos, rot), time);
                    }

                }

                // todo check we need this
                foreach (SyncPositionBehaviour item in packer.Behaviours.Values)
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
            int length = msg.payload.Count;
            // todo stop alloc
            using (var bitReader = new BitReader())
            {
                bitReader.Reset(msg.payload);

                float time = packer.UnpackTime(bitReader);
                packer.UnpackNext(bitReader, out uint id, out Vector3 pos, out Quaternion rot);

                if (packer.Behaviours.TryGetValue(id, out SyncPositionBehaviour behaviour))
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

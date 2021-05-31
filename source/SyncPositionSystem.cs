using System;
using JamesFrowen.BitPacking;
using Mirror;
using UnityEngine;

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
            if (!sendToAll)
            {
                sendToAll = true;
                UnityEngine.Debug.LogWarning("sendToAll disabled is not implemented yet");
            }
        }

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

        #region Sync Server -> Client
        [ServerCallback]
        private void LateUpdate()
        {
            if (checkEveryFrame || ShouldSync())
            {
                SendUpdateToAll();
            }
        }

        bool ShouldSync()
        {
            float now = Time.time;
            if (now > nextSyncInterval)
            {
                nextSyncInterval += syncInterval;
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
            if (_behaviours.Count == 0) { return; }

            // todo dont create new buffer each time
            BitWriter bitWriter = new BitWriter(_behaviours.Count * 32);
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
            foreach (SyncPositionBehaviour behaviour in _behaviours)
            {
                if (!behaviour.NeedsUpdate())
                    continue;

                anyNeedUpdate = true;

                packer.PackNext(bitWriter, behaviour);

                // todo handle client authority updates better
                behaviour.ClearNeedsUpdate(syncInterval);
            }
            return anyNeedUpdate;
        }

        internal void ClientHandleNetworkPositionMessage(NetworkConnection _conn, NetworkPositionMessage msg)
        {
            int length = msg.payload.Count;
            BitReader bitReader = new BitReader(length);
            bitReader.CopyToBuffer(msg.payload);
            float time = packer.UnpackTime(bitReader);
            ulong count = packer.UnpackCount(bitReader);

            for (uint i = 0; i < count; i++)
            {
                packer.UnpackNext(bitReader, out uint id, out Vector3 pos, out Quaternion rot);

                if (_behaviours.TryGet(id, out SyncPositionBehaviour behaviour))
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
            BitReader bitReader = new BitReader(length);
            bitReader.CopyToBuffer(msg.payload);

            float time = packer.UnpackTime(bitReader);
            packer.UnpackNext(bitReader, out uint id, out Vector3 pos, out Quaternion rot);

            if (_behaviours.TryGet(id, out SyncPositionBehaviour behaviour))
            {
                behaviour.ApplyOnServer(new TransformState(pos, rot), time);
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

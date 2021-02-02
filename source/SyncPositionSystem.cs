using JamesFrowen.BitPacking;
using Mirror;
using System;
using UnityEngine;


namespace JamesFrowen.PositionSync
{
    [AddComponentMenu("Network/SyncPosition/System")]
    public class SyncPositionSystem : MonoBehaviour
    {
        // todo make this work with network Visibility
        // todo add maxMessageSize (splits up update message into multiple messages if too big)
        // todo test sync interval vs fixed hz 

        [Header("Reference")]
        [SerializeField] SyncPositionBehaviourRuntimeDictionary _behaviours;

        [Header("Sync")]
        [Tooltip("How often 1 behaviour should update")]
        public float syncInterval = 0.1f;
        [Tooltip("Check if behaviours need update every frame, If false then checks every syncInterval")]
        public bool checkEveryFrame = true;

        [Header("timer Compression")]
        [SerializeField] float maxTime = 60 * 60 * 24;
        [SerializeField] float timePrecision = 1 / 1000f;

        [Header("Id Compression")]
        [SerializeField] int smallBitCount = 6;
        [SerializeField] int mediumBitCount = 12;
        [SerializeField] int largeBitCount = 18;

        [Header("Position Compression")]
        [SerializeField] Vector3 min = Vector3.one * -100;
        [SerializeField] Vector3 max = Vector3.one * 100;
        [SerializeField] float precision = 0.01f;

        [Header("Rotation Compression")]
        [SerializeField] int bitCount = 9;


        [Header("Position Debug And Gizmo")]
        // todo replace these serialized fields with custom editor
        [SerializeField] private bool drawGizmo;
        [SerializeField] private Color gizmoColor;
        [Tooltip("readonly")]
        [SerializeField] private int _bitCount;
        [Tooltip("readonly")]
        [SerializeField] private Vector3Int _bitCountAxis;
        [Tooltip("readonly")]
        [SerializeField] private int _byteCount;


        [NonSerialized] internal FloatPacker timePacker;
        [NonSerialized] internal UIntVariablePacker idPacker;
        [NonSerialized] internal PositionPacker positionPacker;
        [NonSerialized] internal QuaternionPacker rotationPacker;


        [NonSerialized] float nextSyncInterval;

        private void Awake()
        {
            // time precision 1000 times more than interval
            this.timePacker = new FloatPacker(0, this.maxTime, this.timePrecision);
            this.idPacker = new UIntVariablePacker(this.smallBitCount, this.mediumBitCount, this.largeBitCount);
            this.positionPacker = new PositionPacker(this.min, this.max, this.precision);
            this.rotationPacker = new QuaternionPacker(this.bitCount);
        }

        private void OnValidate()
        {
            this.positionPacker = new PositionPacker(this.min, this.max, this.precision);
            this._bitCount = this.positionPacker.bitCount;
            this._bitCountAxis = this.positionPacker.BitCountAxis;
            this._byteCount = Mathf.CeilToInt(this._bitCount / 8f);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!this.drawGizmo) { return; }
            Gizmos.color = this.gizmoColor;
            Bounds bounds = default;
            bounds.min = this.min;
            bounds.max = this.max;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
#endif

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

            NetworkServer.SendToAll(new NetworkPositionMessage
            {
                payload = bitWriter.ToArraySegment()
            });
        }

        internal bool PackBehaviours(BitWriter bitWriter, float time)
        {
            this.timePacker.Pack(bitWriter, time);
            var anyNeedUpdate = false;
            foreach (var behaviour in this._behaviours)
            {
                if (!behaviour.NeedsUpdate())
                    continue;

                anyNeedUpdate = true;

                var id = behaviour.netId;
                var state = behaviour.State;

                this.idPacker.Pack(bitWriter, id);
                this.positionPacker.Pack(bitWriter, state.position);
                this.rotationPacker.Pack(bitWriter, state.rotation);

                // todo handle client authority updates better
                behaviour.ClearNeedsUpdate(this.syncInterval);
            }
            bitWriter.Flush();
            return anyNeedUpdate;
        }

        internal void ClientHandleNetworkPositionMessage(NetworkConnection _conn, NetworkPositionMessage msg)
        {
            var count = msg.payload.Count;
            var bitReader = new BitReader(msg.payload);
            var time = this.timePacker.Unpack(bitReader);

            while (bitReader.Position < count)
            {
                var id = this.idPacker.Unpack(bitReader);
                var pos = this.positionPacker.Unpack(bitReader);
                var rot = this.rotationPacker.Unpack(bitReader);

                if (this._behaviours.TryGet(id, out var behaviour))
                {
                    behaviour.ApplyOnClient(new TransformState(pos, rot), time);
                }

            }
            Debug.Assert(bitReader.Position == count, "should have read exact amount");
        }

        #endregion


        #region Sync Client Auth -> Server
        public void SendMessageToServer(ISyncPositionBehaviour behaviour)
        {
            // todo dont create new buffer each time
            var bitWriter = new BitWriter(32);

            var id = behaviour.netId;
            var state = behaviour.State;

            this.idPacker.Pack(bitWriter, id);
            this.positionPacker.Pack(bitWriter, state.position);
            this.rotationPacker.Pack(bitWriter, state.rotation);

            behaviour.ClearNeedsUpdate(this.syncInterval);

            bitWriter.Flush();

            NetworkClient.Send(new NetworkPositionSingleMessage
            {
                payload = bitWriter.ToArraySegment()
            });
        }

        /// <summary>
        /// Position from client to server
        /// </summary>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        internal void ServerHandleNetworkPositionMessage(NetworkConnection _conn, NetworkPositionSingleMessage msg)
        {
            var bitReader = new BitReader(msg.payload);
            var id = this.idPacker.Unpack(bitReader);
            var pos = this.positionPacker.Unpack(bitReader);
            var rot = this.rotationPacker.Unpack(bitReader);

            if (this._behaviours.TryGet(id, out var behaviour))
            {
                behaviour.ApplyOnServer(new TransformState(pos, rot));
            }

            Debug.Assert(bitReader.Position == msg.payload.Count, "should have read exact amount");
        }
        #endregion
    }

    public interface ISyncPositionBehaviour : IUnityEqualsChecks
    {
        #region properties
        uint netId { get; }
        TransformState State { get; }
        #endregion

        #region server methods
        /// <summary>
        /// Checks if this objects needs updating
        /// <para>client auth: has client sent update?</para>
        /// <para>server auth: has object moved?</para>
        /// </summary>
        bool NeedsUpdate();
        /// <summary>
        /// Reset needs update flag and sets next sync time
        /// </summary>
        /// <param name="syncInterval"></param>
        void ClearNeedsUpdate(float syncInterval);

        /// <summary>
        /// Applies position on server from client authority
        /// </summary>
        /// <param name="transformState"></param>
        void ApplyOnServer(TransformState transformState);
        #endregion

        #region client methods
        /// <summary>
        /// 
        /// </summary>
        /// <param name="transformState"></param>
        /// <param name="time"></param>
        void ApplyOnClient(TransformState transformState, float time);
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
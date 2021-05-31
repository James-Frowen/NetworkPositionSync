using JamesFrowen.Logging;
using Mirror;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using BitWriter = JamesFrowen.BitPacking.NetworkWriter;

namespace JamesFrowen.PositionSync
{
    /// <summary>
    /// Behaviour to sync position and rotation, This behaviour is used by <see cref="SyncPositionSystem"/> in order to sync
    /// <para>for standalone version see <see cref="SyncPositionBehaviourStandalone"/></para>
    /// </summary>
    [AddComponentMenu("Network/SyncPosition/SyncPositionBehaviour")]
    public class SyncPositionBehaviour : NetworkBehaviour
    {
        #region ISyncPositionBehaviour
        internal bool NeedsUpdate() => this.ServerNeedsToSendUpdate();


        internal void ApplyOnServer(TransformState state, float time)
        {
            // this should not happen, Exception to disconnect attacker
            if (!this.clientAuthority) { throw new InvalidOperationException("Client is not allowed to send updated when clientAuthority is false"); }

            this._latestState = state;

            // if host apply using interpolation otherwise apply exact 
            if (this.isClient)
            {
                this.AddSnapShotToBuffer(state, time);
            }
            else
            {
                this.ApplyStateNoInterpolation(state);
            }
        }

        internal void ApplyOnClient(TransformState state, float time)
        {
            // not host
            // host will have already handled movement in servers code
            if (this.isServer)
                return;

            this.AddSnapShotToBuffer(state, time);
        }

        // todo check we need this
        internal void OnServerTime(float serverTime)
        {
            this.interpolationTime.OnMessage(serverTime);
        }
        #endregion


        [Header("References")]
        [SerializeField] SyncPositionBehaviourRuntimeDictionary _behaviourSet;
        [SerializeField] SyncPositionPacker packer;

        [Tooltip("Which transform to sync")]
        [SerializeField] Transform target;

        [Header("Authority")]
        [Tooltip("Set to true if moves come from owner client, set to false if moves always come from server")]
        [SerializeField] bool clientAuthority = false;

        [Tooltip("If true uses local position and rotation, if value uses world position and rotation")]
        [SerializeField] bool useLocalSpace = true;

        // todo make 0 Sensitivity always send (and avoid doing distance/angle check)
        [Tooltip("How far position has to move before it is synced")]
        [SerializeField] float positionSensitivity = 0.1f;

        [Tooltip("How far rotation has to move before it is synced")]
        [SerializeField] float rotationSensitivity = 0.1f;

        [Header("Snapshot Interpolation")]
        [Tooltip("Delay to add to client time to make sure there is always a snapshot to interpolate towards. High delay can handle more jitter, but adds latancy to the position.")]
        [SerializeField] float clientDelay = 0.2f;

        [Tooltip("Client Authority Sync Interval")]
        [SerializeField] float clientSyncInterval = 0.1f;

        [SerializeField] bool showDebugGui = false;

        /// <summary>
        /// Set when client with authority updates the server
        /// </summary>
        bool _needsUpdate;

        uint? parentId;

        /// <summary>
        /// latest values from client
        /// </summary>
        TransformState _latestState;

        float _nextSyncInterval;

        // values for HasMoved/Rotated
        Vector3 lastPosition;
        Quaternion lastRotation;

        // client
        readonly SnapshotBuffer snapshotBuffer = new SnapshotBuffer();
        InterpolationTime interpolationTime;

        void OnGUI()
        {
            if (this.showDebugGui)
            {
                GUILayout.Label($"ServerTime: {this.interpolationTime.ServerTime}");
                GUILayout.Label($"LocalTime: {this.interpolationTime.ClientTime}");
                GUILayout.Label(this.snapshotBuffer.ToString());
            }
        }

        void OnValidate()
        {
            if (this.target == null)
                this.target = this.transform;
        }

        /// <summary>
        /// server auth or no owner, or host
        /// </summary>
        bool IsControlledByServer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => !this.clientAuthority || this.connectionToClient == null || this.connectionToClient == NetworkServer.localConnection;
        }

        /// <summary>
        /// client auth and owner
        /// </summary>
        bool IsLocalClientInControl
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this.clientAuthority && this.hasAuthority;
        }

        /// <summary>
        /// is this server/client in control of the object
        /// </summary>
        bool InControl
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (this.isServer && this.IsControlledByServer) || (this.isClient && this.IsLocalClientInControl);
        }

        Vector3 Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return this.useLocalSpace ? this.target.localPosition : this.target.position;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (this.useLocalSpace)
                {
                    this.target.localPosition = value;
                }
                else
                {
                    this.target.position = value;
                }
            }
        }

        Quaternion Rotation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return this.useLocalSpace ? this.target.localRotation : this.target.rotation;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                if (this.useLocalSpace)
                {
                    this.target.localRotation = value;
                }
                else
                {
                    this.target.rotation = value;
                }
            }
        }

        public TransformState TransformState
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new TransformState(this.Position, this.Rotation);
        }

        float Time
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => UnityEngine.Time.unscaledTime;
        }

        float DeltaTime
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => UnityEngine.Time.unscaledDeltaTime;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsTimeToUpdate()
        {
            return this.Time > this._nextSyncInterval;
        }

        /// <summary>
        /// Resets values, called after syncing to client
        /// <para>Called on server</para>
        /// </summary>
        internal void ClearNeedsUpdate(float interval)
        {
            this._needsUpdate = false;
            this._nextSyncInterval = this.Time + interval;
            this.lastPosition = this.Position;
            this.lastRotation = this.Rotation;
        }

        /// <summary>
        /// Has target moved since we last checked
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool HasMoved()
        {
            var moved = Vector3.Distance(this.lastPosition, this.Position) > this.positionSensitivity;

            if (moved)
            {
                this.lastPosition = this.Position;
            }
            return moved;
        }

        /// <summary>
        /// Has target moved since we last checked
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool HasRotated()
        {
            var rotated = Quaternion.Angle(this.lastRotation, this.Rotation) > this.rotationSensitivity;

            if (rotated)
            {
                this.lastRotation = this.Rotation;
            }
            return rotated;
        }


        public override void OnStartClient()
        {
            this.interpolationTime = new InterpolationTime(this.clientDelay);
            this._behaviourSet.Add(this);
        }
        public override void OnStartServer()
        {
            this._behaviourSet.Add(this);
        }
        public override void OnStopClient()
        {
            this._behaviourSet.Remove(this);
        }
        public override void OnStopServer()
        {
            this._behaviourSet.Remove(this);
        }

        void Update()
        {
            if (this.isClient)
            {
                if (this.IsLocalClientInControl)
                {
                    this.ClientAuthorityUpdate();
                }
                else
                {
                    this.ClientInterpolation();
                }
            }
        }

        #region Server Sync Update
        /// <summary>
        /// Checks if object needs syncing to clients
        /// <para>Called on server</para>
        /// </summary>
        /// <returns></returns>
        bool ServerNeedsToSendUpdate()
        {
            if (this.IsControlledByServer)
            {
                return this.IsTimeToUpdate() && (this.HasMoved() || this.HasRotated());
            }
            else
            {
                // dont care about time here, if client authority has sent snapshot then always relay it to other clients
                // todo do we need a check for attackers sending too many snapshots?
                return this._needsUpdate;
            }
        }

        /// <summary>
        /// Applies values to target transform on client
        /// <para>Adds to buffer for interpolation</para>
        /// </summary>
        /// <param name="state"></param>
        void AddSnapShotToBuffer(TransformState state, float serverTime)
        {
            // dont apply on local owner
            if (this.IsLocalClientInControl)
                return;

            // todo do we need this, or do we set it elsewhere?
            //this.interpolationTime.OnMessage(serverTime);

            // buffer will be empty if first snapshot or hasn't moved for a while.
            // in this case we can add a snapshot for (serverTime-syncinterval) for interoplation
            // this assumes snapshots are sent in order!
            if (this.snapshotBuffer.IsEmpty)
            {
                this.snapshotBuffer.AddSnapShot(this.TransformState, serverTime - this.clientSyncInterval);
            }
            this.snapshotBuffer.AddSnapShot(state, serverTime);
        }
        #endregion


        #region Client Sync Update 
        void ClientAuthorityUpdate()
        {
            if (this.IsTimeToUpdate() && (this.HasMoved() || this.HasRotated()))
            {
                this.SendMessageToServer();
                this.ClearNeedsUpdate(this.clientSyncInterval);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SendMessageToServer()
        {
            // todo dont create new buffer each time
            var bitWriter = new BitWriter(64);
            this.packer.PackTime(bitWriter, (float)NetworkTime.time);
            this.packer.PackNext(bitWriter, this);

            // todo optimize
            var temp = bitWriter.ToArray();

            NetworkClient.Send(new NetworkPositionSingleMessage
            {
                payload = new ArraySegment<byte>(temp)
            });
        }

        /// <summary>
        /// Applies values to target transform on server
        /// <para>no need to interpolate on server</para>
        /// </summary>
        /// <param name="state"></param>
        void ApplyStateNoInterpolation(TransformState state)
        {
            this.Position = state.position;
            this.Rotation = state.rotation;
            this._needsUpdate = true;
        }
        #endregion


        #region Client Interpolation
        void ClientInterpolation()
        {
            if (this.snapshotBuffer.IsEmpty) { return; }

            this.interpolationTime.OnTick(this.DeltaTime);

            var snapshotTime = this.interpolationTime.ClientTime;
            var state = this.snapshotBuffer.GetLinearInterpolation(snapshotTime);
            SimpleLogger.Trace($"p1:{this.Position.x} p2:{state.position.x} delta:{this.Position.x - state.position.x}");


            this.Position = state.position;

            if (this.packer.SyncRotation)
                this.Rotation = state.rotation;

            // remove snapshots older than 2times sync interval, they will never be used by Interpolation
            var removeTime = snapshotTime - (this.clientDelay * 1.5f);
            this.snapshotBuffer.RemoveOldSnapshots(removeTime);
        }
        #endregion
    }
}

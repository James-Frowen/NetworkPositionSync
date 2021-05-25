using Mirror;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace JamesFrowen.PositionSync
{
    /// <summary>
    /// Standalone version of <see cref="SyncPositionBehaviour"/>
    /// </summary>
    [AddComponentMenu("Network/SyncPosition/Standalone Behaviour")]
    public class SyncPositionBehaviourStandalone : NetworkBehaviour
    {
        static readonly ILogger logger = LogFactory.GetLogger<SyncPositionBehaviourStandalone>(LogType.Error);

        [Header("References")]
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

        double localTime;

        /// <summary>
        /// Set when client with authority updates the server
        /// </summary>
        bool _needsUpdate;

        /// <summary>
        /// latest values from client
        /// </summary>
        TransformState _latestState;

        // todo does this need to be a double, it uses NetworkTime.time
        float _nextSyncInterval;

        // values for HasMoved/Rotated
        Vector3 lastPosition;
        Quaternion lastRotation;

        // client
        readonly SnapshotBuffer snapshotBuffer = new SnapshotBuffer();

        void OnGUI()
        {
            if (this.showDebugGui)
            {
                GUILayout.Label($"ServerTime: {NetworkTime.time}");
                GUILayout.Label($"LocalTime: {this.localTime}");
                GUILayout.Label(this.snapshotBuffer.ToString());
            }
        }

        void OnValidate()
        {
            if (this.target == null)
                this.target = this.transform;
        }

        bool IsControlledByServer
        {
            // server auth or no owner, or host
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => !this.clientAuthority || this.connectionToClient == null || this.connectionToClient == NetworkServer.localConnection;
        }

        bool IsLocalClientInControl
        {
            // client auth and owner
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this.clientAuthority && this.hasAuthority;
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

        TransformState TransformState
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new TransformState(this.Position, this.Rotation);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IsTimeToUpdate()
        {
            return Time.time > this._nextSyncInterval;
        }

        /// <summary>
        /// Resets values, called after syncing to client
        /// <para>Called on server</para>
        /// </summary>
        void ClearNeedsUpdate(float interval)
        {
            this._needsUpdate = false;
            this._nextSyncInterval = Time.time + interval;
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

        void Update()
        {
            if (this.isServer)
            {
                this.ServerSyncUpdate();
            }

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
        void ServerSyncUpdate()
        {
            if (this.ServerNeedsToSendUpdate())
            {
                this.SendMessageToClient();
                this.ClearNeedsUpdate(this.clientSyncInterval);
            }
        }

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SendMessageToClient()
        {
            // if client has send update, then we want to use the state it gave us
            // this is to make sure we are not sending host's interpolations position as the snapshot insteading sending the client auth snapshot
            var state = this.IsControlledByServer ? this.TransformState : this._latestState;
            // todo is this correct time?
            this.RpcServerSync(state, NetworkTime.time);
        }

        [ClientRpc]
        void RpcServerSync(TransformState state, double time)
        {
            // not host
            // host will have already handled movement in servers code
            if (this.isServer)
                return;

            this.AddSnapShotToBuffer(state, time);
        }

        /// <summary>
        /// Applies values to target transform on client
        /// <para>Adds to buffer for interpolation</para>
        /// </summary>
        /// <param name="state"></param>
        void AddSnapShotToBuffer(TransformState state, double serverTime)
        {
            // dont apply on local owner
            if (this.IsLocalClientInControl)
                return;

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
            // todo, is this the correct time?
            this.CmdClientAuthoritySync(this.TransformState, NetworkTime.time);
        }

        [Command]
        void CmdClientAuthoritySync(TransformState state, double time)
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

            // we want to set local time to the estimated time that the server was when it send the snapshot
            var serverTime = NetworkTime.time;
            this.localTime = serverTime - NetworkTime.rtt / 2;

            // we then subtract clientDelay to handle any jitter
            var snapshotTime = this.localTime - this.clientDelay;
            var state = this.snapshotBuffer.GetLinearInterpolation(snapshotTime);
            if (logger.LogEnabled()) { logger.Log($"p1:{this.Position.x} p2:{state.position.x} delta:{this.Position.x - state.position.x}"); }
            this.Position = state.position;
            this.Rotation = state.rotation;

            // remove snapshots older than 2times sync interval, they will never be used by Interpolation
            var removeTime = (float)(snapshotTime - (this.clientSyncInterval * 2));
            this.snapshotBuffer.RemoveOldSnapshots(removeTime);
        }
        #endregion
    }
}

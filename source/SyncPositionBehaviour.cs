using JamesFrowen.BitPacking;
using Mirror;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace JamesFrowen.PositionSync
{
    /// <summary>
    /// Behaviour to sync position and rotation, This behaviour is used by <see cref="SyncPositionSystem"/> in order to sync
    /// <para>for standalone version see <see cref="SyncPositionBehaviourStandalone"/></para>
    /// </summary>
    [AddComponentMenu("Network/SyncPosition/SyncPositionBehaviour")]
    public class SyncPositionBehaviour : NetworkBehaviour, ISyncPositionBehaviour
    {
        #region ISyncPositionBehaviour
        bool ISyncPositionBehaviour.NeedsUpdate() => this.ServerNeedsToSendUpdate();

        void ISyncPositionBehaviour.ClearNeedsUpdate(float syncInterval) => this.ClearNeedsUpdate(syncInterval);

        void ISyncPositionBehaviour.ApplyOnServer(TransformState state, float time)
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

        void ISyncPositionBehaviour.ApplyOnClient(TransformState state, float time)
        {
            // not host
            // host will have already handled movement in servers code
            if (this.isServer)
                return;

            this.AddSnapShotToBuffer(state, time);
        }
        #endregion




        static readonly ILogger logger = LogFactory.GetLogger<SyncPositionBehaviour>(LogType.Error);

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

        private void Start()
        {
            this.interpolationTime = new InterpolationTime(this.clientDelay);
        }

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
        void ClearNeedsUpdate(float interval)
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

            this.interpolationTime.OnMessage(serverTime);

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
            bitWriter.Flush();

            NetworkClient.Send(new NetworkPositionSingleMessage
            {
                payload = bitWriter.ToArraySegment()
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
            if (logger.LogEnabled()) { logger.Log($"p1:{this.Position.x} p2:{state.position.x} delta:{this.Position.x - state.position.x}"); }
            this.Position = state.position;
            this.Rotation = state.rotation;

            // remove snapshots older than 2times sync interval, they will never be used by Interpolation
            var removeTime = snapshotTime - this.clientDelay * 1.5f;
            this.snapshotBuffer.RemoveOldSnapshots(removeTime);
        }
        #endregion
    }
}

using JamesFrowen.BitPacking;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace JamesFrowen.PositionSync
{
    [CreateAssetMenu(menuName = "PositionSync/Packer")]
    public class SyncPositionPacker : ScriptableObject
    {
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
        [SerializeField] bool syncRotation;
        [SerializeField] int bitCount = 9;

        [Header("Transform Parent")]
        [SerializeField] bool syncParent;



        [Header("Position Debug And Gizmo")]
        // todo replace these serialized fields with custom editor
        [SerializeField] private bool drawGizmo;
        [SerializeField] private Color gizmoColor;
        [Tooltip("readonly")]
        [SerializeField] private int _posBitCount;
        [Tooltip("readonly")]
        [SerializeField] private Vector3Int _posBitCountAxis;
        [Tooltip("readonly")]
        [SerializeField] private int _posByteCount;

        [SerializeField] private int _totalBitCountMin;
        [SerializeField] private int _totalBitCountMax;
        [SerializeField] private int _totalByteCountMin;
        [SerializeField] private int _totalByteCountMax;


        [NonSerialized] internal FloatPacker timePacker;
        [NonSerialized] internal UIntVariablePacker idPacker;
        [NonSerialized] internal UIntVariablePacker parentPacker;
        [NonSerialized] internal PositionPacker positionPacker;
        [NonSerialized] internal QuaternionPacker rotationPacker;


        public bool SyncRotation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this.syncRotation;
        }

        private void OnEnable()
        {
            // time precision 1000 times more than interval
            this.timePacker = new FloatPacker(0, this.maxTime, this.timePrecision);
            this.idPacker = new UIntVariablePacker(this.smallBitCount, this.mediumBitCount, this.largeBitCount);
            // parent can use same packer as id for now
            this.parentPacker = this.idPacker;
            this.positionPacker = new PositionPacker(this.min, this.max, this.precision);
            this.rotationPacker = new QuaternionPacker(this.bitCount);
        }

        private void OnValidate()
        {
            this.positionPacker = new PositionPacker(this.min, this.max, this.precision);
            this._posBitCount = this.positionPacker.bitCount;
            this._posBitCountAxis = this.positionPacker.BitCountAxis;
            this._posByteCount = Mathf.CeilToInt(this._posBitCount / 8f);

            this.timePacker = new FloatPacker(0, this.maxTime, this.timePrecision);
            this.idPacker = new UIntVariablePacker(this.smallBitCount, this.mediumBitCount, this.largeBitCount);
            this.parentPacker = this.idPacker;
            this.rotationPacker = new QuaternionPacker(this.bitCount);


            this._totalBitCountMin = this.idPacker.minBitCount + (this.syncRotation ? this.rotationPacker.bitCount : 0) + this.positionPacker.bitCount + (this.syncParent ? 1 : 0);
            this._totalBitCountMax = this.idPacker.maxBitCount + (this.syncRotation ? this.rotationPacker.bitCount : 0) + this.positionPacker.bitCount + (this.syncParent ? (1 + this.parentPacker.maxBitCount) : 0);
            this._totalByteCountMin = Mathf.CeilToInt(this._totalBitCountMin / 8f);
            this._totalByteCountMax = Mathf.CeilToInt(this._totalBitCountMax / 8f);
        }

        [Conditional("UNITY_EDITOR")]
        internal void DrawGizmo()
        {
#if UNITY_EDITOR
            if (!this.drawGizmo) { return; }
            Gizmos.color = this.gizmoColor;
            Bounds bounds = default;
            bounds.min = this.min;
            bounds.max = this.max;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
#endif  
        }

        public void PackTime(BitWriter writer, float time)
        {
            this.timePacker.Pack(writer, time);
        }

        public void PackNext(BitWriter writer, ISyncPositionBehaviour behaviour)
        {
            var id = behaviour.netId;
            var state = behaviour.TransformState;

            this.idPacker.Pack(writer, id);
            this.positionPacker.Pack(writer, state.position);

            if (this.syncRotation)
            {
                this.rotationPacker.Pack(writer, state.rotation);
            }

            if (this.syncParent)
            {
                this.parentPacker.PackNullable(writer, behaviour.ParentNetId);
            }
        }

        public float UnpackTime(BitReader reader)
        {
            return this.timePacker.Unpack(reader);
        }

        public void UnpackNext(BitReader reader, out uint id, out Vector3 pos, out Quaternion rot, out uint? parentId)
        {
            id = this.idPacker.Unpack(reader);
            pos = this.positionPacker.Unpack(reader);
            rot = this.syncRotation
                ? this.rotationPacker.Unpack(reader)
                : Quaternion.identity;

            parentId = this.syncParent
                ? this.parentPacker.UnpackNullable(reader)
                : null;
        }
    }
}
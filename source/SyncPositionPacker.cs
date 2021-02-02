using JamesFrowen.BitPacking;
using System;
using System.Diagnostics;
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
            var state = behaviour.State;

            this.idPacker.Pack(writer, id);
            this.positionPacker.Pack(writer, state.position);
            this.rotationPacker.Pack(writer, state.rotation);
        }

        public float UnpackTime(BitReader reader)
        {
            throw new NotImplementedException();
        }

        public void UnpackNext(BitReader bitReader, out uint id, out Vector3 pos, out Quaternion rot)
        {
            id = this.idPacker.Unpack(bitReader);
            pos = this.positionPacker.Unpack(bitReader);
            rot = this.rotationPacker.Unpack(bitReader);
        }
    }
}
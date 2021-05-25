using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JamesFrowen.BitPacking;
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
        [SerializeField] bool syncRotation = true;
        [SerializeField] int bitCount = 9;


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

        [SerializeField] internal int _totalBitCountMin;
        [SerializeField] internal int _totalBitCountMax;
        [SerializeField] private int _totalByteCountMin;
        [SerializeField] private int _totalByteCountMax;



        // packers
        [NonSerialized] internal FloatPacker timePacker;
        [NonSerialized] internal UIntVariablePacker2 countPacker;
        [NonSerialized] internal UIntVariablePacker idPacker;
        [NonSerialized] internal UIntVariablePacker parentPacker;
        [NonSerialized] internal PositionPacker positionPacker;
        [NonSerialized] internal QuaternionPacker rotationPacker;


        public bool SyncRotation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => syncRotation;
        }

        private void OnEnable()
        {
            // time precision 1000 times more than interval
            timePacker = new FloatPacker(0, maxTime, timePrecision);
            countPacker = new UIntVariablePacker2(4, 10);
            idPacker = new UIntVariablePacker(smallBitCount, mediumBitCount, largeBitCount);
            // parent can use same packer as id for now
            parentPacker = idPacker;
            positionPacker = new PositionPacker(min, max, precision);
            rotationPacker = new QuaternionPacker(bitCount);
        }

        private void OnValidate()
        {
            positionPacker = new PositionPacker(min, max, precision);
            _posBitCount = positionPacker.bitCount;
            _posBitCountAxis = positionPacker.BitCountAxis;
            _posByteCount = Mathf.CeilToInt(_posBitCount / 8f);

            timePacker = new FloatPacker(0, maxTime, timePrecision);
            idPacker = new UIntVariablePacker(smallBitCount, mediumBitCount, largeBitCount);
            parentPacker = idPacker;
            rotationPacker = new QuaternionPacker(bitCount);


            _totalBitCountMin = idPacker.minBitCount + (syncRotation ? rotationPacker.bitCount : 0) + positionPacker.bitCount;
            _totalBitCountMax = idPacker.maxBitCount + (syncRotation ? rotationPacker.bitCount : 0) + positionPacker.bitCount;
            _totalByteCountMin = Mathf.CeilToInt(_totalBitCountMin / 8f);
            _totalByteCountMax = Mathf.CeilToInt(_totalBitCountMax / 8f);
        }

        [Conditional("UNITY_EDITOR")]
        internal void DrawGizmo()
        {
#if UNITY_EDITOR
            if (!drawGizmo) { return; }
            Gizmos.color = gizmoColor;
            Bounds bounds = default;
            bounds.min = min;
            bounds.max = max;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
#endif  
        }

        public void PackTime(BitWriter writer, float time)
        {
            timePacker.Pack(writer, time);
        }

        public void PackNext(BitWriter writer, SyncPositionBehaviour behaviour)
        {
            uint id = behaviour.netId;
            TransformState state = behaviour.TransformState;

            idPacker.Pack(writer, id);
            positionPacker.Pack(writer, state.position);

            if (syncRotation)
            {
                rotationPacker.Pack(writer, state.rotation);
            }
        }

        public float UnpackTime(BitReader reader)
        {
            return timePacker.Unpack(reader);
        }

        public ulong UnpackCount(BitReader bitReader)
        {
            return countPacker.Unpack(bitReader);
        }

        public void UnpackNext(BitReader reader, out uint id, out Vector3 pos, out Quaternion rot)
        {
            id = (uint)idPacker.Unpack(reader);
            pos = positionPacker.Unpack(reader);
            rot = syncRotation
                ? rotationPacker.Unpack(reader)
                : Quaternion.identity;
        }
    }
}

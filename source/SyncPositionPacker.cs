using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JamesFrowen.BitPacking;
using JamesFrowen.Logging;
using UnityEngine;
using BitReader = JamesFrowen.BitPacking.NetworkReader;
using BitWriter = JamesFrowen.BitPacking.NetworkWriter;

namespace JamesFrowen.PositionSync
{
    [Serializable]
    public class SyncSettings
    {
        [Header("timer Compression")]
        public float maxTime = 60 * 60 * 24;
        public float timePrecision = 1 / 1000f;

        [Header("Id Compression")]
        public int smallBitCount = 6;
        public int mediumBitCount = 12;
        public int largeBitCount = 18;

        [Header("Position Compression")]
        public Vector3 min = Vector3.one * -100;
        public Vector3 max = Vector3.one * 100;
        public float precision = 0.01f;

        [Header("Rotation Compression")]
        public bool syncRotation = true;
        public int bitCount = 9;


        public FloatPacker CreateTimePacker()
        {
            return new FloatPacker(0, maxTime, timePrecision);
        }
        public UIntVariablePacker2 CreateCountPacker()
        {
            return new UIntVariablePacker2(4, 10);
        }
        public UIntVariablePacker CreateIdPacker()
        {
            return new UIntVariablePacker(smallBitCount, mediumBitCount, largeBitCount);
        }
        public PositionPacker CreatePositionPacker()
        {
            return new PositionPacker(min, max, precision);
        }
        public QuaternionPacker CreateRotationPacker()
        {
            return new QuaternionPacker(bitCount);
        }
    }
    [Serializable]
    public class SyncSettingsDebug
    {
        // todo replace these serialized fields with custom editor
        public bool drawGizmo;
        public Color gizmoColor;
        [Tooltip("readonly")]
        public int _posBitCount;
        [Tooltip("readonly")]
        public Vector3Int _posBitCountAxis;
        [Tooltip("readonly")]
        public int _posByteCount;

        public int _totalBitCountMin;
        public int _totalBitCountMax;
        public int _totalByteCountMin;
        public int _totalByteCountMax;

        internal void SetValues(SyncSettings settings)
        {
            var positionPacker = new PositionPacker(settings.min, settings.max, settings.precision);
            _posBitCount = positionPacker.bitCount;
            _posBitCountAxis = positionPacker.BitCountAxis;
            _posByteCount = Mathf.CeilToInt(_posBitCount / 8f);

            var timePacker = new FloatPacker(0, settings.maxTime, settings.timePrecision);
            var idPacker = new UIntVariablePacker(settings.smallBitCount, settings.mediumBitCount, settings.largeBitCount);
            UIntVariablePacker parentPacker = idPacker;
            var rotationPacker = new QuaternionPacker(settings.bitCount);


            _totalBitCountMin = idPacker.minBitCount + (settings.syncRotation ? rotationPacker.bitCount : 0) + positionPacker.bitCount;
            _totalBitCountMax = idPacker.maxBitCount + (settings.syncRotation ? rotationPacker.bitCount : 0) + positionPacker.bitCount;
            _totalByteCountMin = Mathf.CeilToInt(_totalBitCountMin / 8f);
            _totalByteCountMax = Mathf.CeilToInt(_totalBitCountMax / 8f);
        }
    }
    [CreateAssetMenu(menuName = "PositionSync/Packer")]
    public class SyncPositionPacker : ScriptableObject
    {
        [Header("Compression Settings")]
        [SerializeField] SyncSettings settings;

        [Header("Position Debug And Gizmo")]
        [SerializeField] SyncSettingsDebug settingsDebug;



        // packers
        [NonSerialized] internal FloatPacker timePacker;
        [NonSerialized] internal UIntVariablePacker2 countPacker;
        [NonSerialized] internal UIntVariablePacker idPacker;
        [NonSerialized] internal PositionPacker positionPacker;
        [NonSerialized] internal QuaternionPacker rotationPacker;


        [NonSerialized] internal Dictionary<uint, SyncPositionBehaviour> Behaviours = new Dictionary<uint, SyncPositionBehaviour>();
        private SyncPositionSystem system;

        public bool SyncRotation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => settings.syncRotation;
        }

        private void OnEnable()
        {
            timePacker = settings.CreateTimePacker();
            countPacker = settings.CreateCountPacker();
            idPacker = settings.CreateIdPacker();
            positionPacker = settings.CreatePositionPacker();
            rotationPacker = settings.CreateRotationPacker();
        }

        private void OnValidate()
        {
            settingsDebug.SetValues(settings);
        }

        [Conditional("UNITY_EDITOR")]
        internal void DrawGizmo()
        {
#if UNITY_EDITOR
            if (!settingsDebug.drawGizmo) { return; }
            Gizmos.color = settingsDebug.gizmoColor;
            Bounds bounds = default;
            bounds.min = settings.min;
            bounds.max = settings.max;
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

            if (settings.syncRotation)
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
            rot = settings.syncRotation
                ? rotationPacker.Unpack(reader)
                : Quaternion.identity;
        }

        public void AddBehaviour(SyncPositionBehaviour thing)
        {
            uint netId = thing.netId;
            Behaviours.Add(netId, thing);


            if (Behaviours.TryGetValue(netId, out SyncPositionBehaviour existingValue))
            {
                if (existingValue != thing)
                {
                    // todo what is this log?
                    SimpleLogger.Error("Parent can't be set without control");
                }
            }
            else
            {
                Behaviours.Add(netId, thing);
            }
        }

        public void RemoveBehaviour(SyncPositionBehaviour thing)
        {
            uint netId = thing.netId;
            Behaviours.Remove(netId);
        }
        public void ClearBehaviours()
        {
            Behaviours.Clear();
        }
    }
}

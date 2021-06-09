using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JamesFrowen.BitPacking;
using JamesFrowen.Logging;
using Mirror;
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
        [SerializeField] SyncSettings settings = new SyncSettings();

        [Header("Position Debug And Gizmo")]
        [SerializeField] SyncSettingsDebug settingsDebug = new SyncSettingsDebug();

        [Header("Sync")]
        [Tooltip("How often 1 behaviour should update")]
        public float syncInterval = 0.1f;
        [Tooltip("Check if behaviours need update every frame, If false then checks every syncInterval")]
        public bool checkEveryFrame = true;
        [Tooltip("Skips Visibility and sends position to all ready connections")]
        public bool sendToAll = true;
        [Tooltip("Create new system object if missing when first Behaviour is added")]
        public bool CreateSystemIfMissing = false;


        // packers
        [NonSerialized] internal FloatPacker timePacker;
        [NonSerialized] internal UIntVariablePacker2 countPacker;
        [NonSerialized] internal UIntVariablePacker idPacker;
        [NonSerialized] internal PositionPacker positionPacker;
        [NonSerialized] internal QuaternionPacker rotationPacker;


        [NonSerialized] internal Dictionary<uint, SyncPositionBehaviour> Behaviours = new Dictionary<uint, SyncPositionBehaviour>();
        private SyncPositionSystem _system;

        public bool SyncRotation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => settings.syncRotation;
        }
        public void SetSystem(SyncPositionSystem value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (_system != null) throw new InvalidOperationException($"SyncPositionPacker[{name}] Can not set System to {value} because it was already equal to {_system}");

            _system = value;
        }
        public void ClearSystem(SyncPositionSystem oldValue)
        {
            if (oldValue == null) throw new ArgumentNullException(nameof(oldValue));
            if (_system != oldValue) throw new InvalidOperationException($"SyncPositionPacker[{name}] Can not clear System from {_system} because the old value was not equal to {oldValue}");

            _system = null;
        }

        public void CheckIfSysteIsMissing()
        {
            if (!CreateSystemIfMissing) return;
            if (_system != null) return;

            _system = NetworkManager.singleton.gameObject.AddComponent<SyncPositionSystem>();
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

            if (!sendToAll)
            {
                sendToAll = true;
                UnityEngine.Debug.LogWarning("sendToAll disabled is not implemented yet");
            }
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

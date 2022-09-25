/*
MIT License

Copyright (c) 2021 James Frowen

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using Mirage;
using Mirage.Serialization;
using UnityEngine;

namespace JamesFrowen.PositionSync
{
    [Serializable]
    public class SyncSettings
    {
        [Header("Object Settings")]
        [Tooltip("Required if using multiple SyncPositionBehaviour per NetworkIdentity, but increase bandwidth")]
        public bool IncludeComponentIndex;

        [Header("Timer Compression")]
        // public float maxTime = 60 * 60 * 24;
        // 0.1ms
        public float timePrecision = 1 / 10_000f;

        [Header("Var Size Compression")]
        [Tooltip("How many bits will be used for a value before having to include another block.\nBest value will be a fraction of log2(worldSize / precision).\n" +
            "The default values of 5 Block Size and 1/1000 precision will mean values under 54m will be 18 bits, values under 1747m will be 24 bits")]
        public int blockSize = 5;

        [Header("Position Compression")]
        //public Vector3 max = Vector3.one * 100;
        public Vector3 precision = Vector3.one / 300f;

        [Header("Rotation Compression")]
        //public bool syncRotation = true;
        public int bitCount = 10;

        // Data Packers.
        public VarFloatPacker CreateTimePacker()
        {
            return new VarFloatPacker(timePrecision, blockSize);
        }
        public VarVector3Packer CreatePositionPacker()
        {
            return new VarVector3Packer(precision, blockSize);
        }
        public QuaternionPacker CreateRotationPacker()
        {
            return new QuaternionPacker(bitCount);
        }
    }

    public class SyncPacker
    {
        // packers
        private readonly VarFloatPacker timePacker;
        private readonly VarVector3Packer positionPacker;
        private readonly QuaternionPacker rotationPacker;
        private readonly int blockSize;
        private readonly bool includeCompId;

        public SyncPacker(SyncSettings settings)
        {
            timePacker = settings.CreateTimePacker();
            positionPacker = settings.CreatePositionPacker();
            rotationPacker = settings.CreateRotationPacker();
            blockSize = settings.blockSize;
            includeCompId = settings.IncludeComponentIndex;
        }

        public void PackTime(NetworkWriter writer, float time)
        {
            timePacker.Pack(writer, time);
        }

        public void PackNext(NetworkWriter writer, SyncPositionBehaviour behaviour)
        {
            var id = behaviour.NetId;
            var state = behaviour.TransformState;


            VarIntBlocksPacker.Pack(writer, id, blockSize);

            if (includeCompId)
            {
                VarIntBlocksPacker.Pack(writer, (uint)behaviour.ComponentIndex, blockSize);
            }

            positionPacker.Pack(writer, state.position);
            rotationPacker.Pack(writer, state.rotation);
        }


        public float UnpackTime(NetworkReader reader)
        {
            return timePacker.Unpack(reader);
        }

        public void UnpackNext(NetworkReader reader, out NetworkBehaviour.Id id, out Vector3 pos, out Quaternion rot)
        {
            var netId = (uint)VarIntBlocksPacker.Unpack(reader, blockSize);
            if (includeCompId)
            {
                var componentIndex = (int)VarIntBlocksPacker.Unpack(reader, blockSize);
                id = new NetworkBehaviour.Id(netId, componentIndex);
            }
            else
            {
                id = new NetworkBehaviour.Id(netId, 0);
            }

            pos = positionPacker.Unpack(reader);
            rot = rotationPacker.Unpack(reader);
        }

        internal bool TryUnpackNext(PooledNetworkReader reader, out NetworkBehaviour.Id id, out Vector3 pos, out Quaternion rot)
        {
            // assume 1 state is atleast 3 bytes
            // (it should be more, but there shouldn't be random left over bits in reader so 3 is enough for check)
            const int minSize = 3;
            if (reader.CanReadBytes(minSize))
            {
                UnpackNext(reader, out id, out pos, out rot);
                return true;
            }
            else
            {
                id = default;
                pos = default;
                rot = default;
                return false;
            }
        }
    }

    //[Serializable]
    //public class SyncSettingsDebug
    //{
    //    // todo replace these serialized fields with custom editor
    //    public bool drawGizmo;
    //    public Color gizmoColor;
    //    [Tooltip("readonly")]
    //    public int _posBitCount;
    //    [Tooltip("readonly")]
    //    public Vector3Int _posBitCountAxis;
    //    [Tooltip("readonly")]
    //    public int _posByteCount;

    //    public int _totalBitCountMin;
    //    public int _totalBitCountMax;
    //    public int _totalByteCountMin;
    //    public int _totalByteCountMax;

    //    internal void SetValues(SyncSettings settings)
    //    {
    //        var positionPacker = new Vector3Packer(settings.max, settings.precision);
    //        _posBitCount = positionPacker.bitCount;
    //        _posBitCountAxis = positionPacker.BitCountAxis;
    //        _posByteCount = Mathf.CeilToInt(_posBitCount / 8f);

    //        var timePacker = new FloatPacker(0, settings.maxTime, settings.timePrecision);
    //        var idPacker = new UIntVariablePacker(settings.smallBitCount, settings.mediumBitCount, settings.largeBitCount);
    //        UIntVariablePacker parentPacker = idPacker;
    //        var rotationPacker = new QuaternionPacker(settings.bitCount);


    //        _totalBitCountMin = idPacker.minBitCount + (settings.syncRotation ? rotationPacker.bitCount : 0) + positionPacker.bitCount;
    //        _totalBitCountMax = idPacker.maxBitCount + (settings.syncRotation ? rotationPacker.bitCount : 0) + positionPacker.bitCount;
    //        _totalByteCountMin = Mathf.CeilToInt(_totalBitCountMin / 8f);
    //        _totalByteCountMax = Mathf.CeilToInt(_totalBitCountMax / 8f);
    //    }
    //}
}

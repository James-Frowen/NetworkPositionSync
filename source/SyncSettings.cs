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
using Mirage.Serialization;
using UnityEngine;

namespace JamesFrowen.PositionSync
{

    public sealed class VarFloatPacker
    {
        readonly int blockSize;
        readonly float precision;
        readonly float inversePrecision;

        public VarFloatPacker(float precision, int blockSize)
        {
            this.precision = precision;
            this.blockSize = blockSize;
            inversePrecision = 1 / precision;
        }

        public void Pack(NetworkWriter writer, float value)
        {
            int scaled = Mathf.RoundToInt(value * inversePrecision);
            uint zig = ZigZag.Encode(scaled);
            VarIntBlocksPacker.Pack(writer, zig, blockSize);
        }

        public float Unpack(NetworkReader reader)
        {
            uint zig = (uint)VarIntBlocksPacker.Unpack(reader, blockSize);
            int scaled = ZigZag.Decode(zig);
            return scaled * precision;
        }
    }
    public sealed class VarVector3Packer
    {
        readonly VarFloatPacker x;
        readonly VarFloatPacker y;
        readonly VarFloatPacker z;

        public VarVector3Packer(Vector3 precision, int blocksize)
        {
            x = new VarFloatPacker(precision.x, blocksize);
            y = new VarFloatPacker(precision.y, blocksize);
            z = new VarFloatPacker(precision.z, blocksize);
        }

        public void Pack(NetworkWriter writer, Vector3 position)
        {
            x.Pack(writer, position.x);
            y.Pack(writer, position.y);
            z.Pack(writer, position.z);
        }

        public Vector3 Unpack(NetworkReader reader)
        {
            Vector3 value = default;
            value.x = x.Unpack(reader);
            value.y = y.Unpack(reader);
            value.z = z.Unpack(reader);
            return value;
        }
    }

    [Serializable]
    public class SyncSettings
    {
        [Header("timer Compression")]
        //public float maxTime = 60 * 60 * 24;
        // 0.1ms
        public float timePrecision = 1 / 10_000f;

        [Header("Var size Compression, How many bits for a value before having to include another block. Best value will be a fraction of log2(worldSize / precision).\n" +
            "Default Value of 5 and 1/1000 precision will mean values under 54m will be 18 bits, values under 1747m will be 24 bits")]
        public int blockSize = 5;

        [Header("Position Compression")]
        //public Vector3 max = Vector3.one * 100;
        public Vector3 precision = Vector3.one / 300f;

        [Header("Rotation Compression")]
        //public bool syncRotation = true;
        public int bitCount = 10;


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
        readonly VarFloatPacker timePacker;
        readonly VarVector3Packer positionPacker;
        readonly QuaternionPacker rotationPacker;
        readonly int blockSize;

        public SyncPacker(SyncSettings settings)
        {
            timePacker = settings.CreateTimePacker();
            positionPacker = settings.CreatePositionPacker();
            rotationPacker = settings.CreateRotationPacker();
            blockSize = settings.blockSize;
        }

        public void PackTime(NetworkWriter writer, float time)
        {
            timePacker.Pack(writer, time);
        }

        public void PackNext(NetworkWriter writer, SyncPositionBehaviour behaviour)
        {
            uint id = behaviour.NetId;
            TransformState state = behaviour.TransformState;

            VarIntBlocksPacker.Pack(writer, id, blockSize);
            positionPacker.Pack(writer, state.position);
            rotationPacker.Pack(writer, state.rotation);
        }


        public float UnpackTime(NetworkReader reader)
        {
            return timePacker.Unpack(reader);
        }

        public void UnpackNext(NetworkReader reader, out uint id, out Vector3 pos, out Quaternion rot)
        {
            id = (uint)VarIntBlocksPacker.Unpack(reader, blockSize);
            pos = positionPacker.Unpack(reader);
            rot = rotationPacker.Unpack(reader);
        }

        internal bool TryUnpackNext(PooledNetworkReader reader, out uint id, out Vector3 pos, out Quaternion rot)
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

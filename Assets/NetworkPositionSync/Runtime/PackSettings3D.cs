/*
MIT License

Copyright (c) 2023 James Frowen

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

using Mirage.Serialization;
using UnityEngine;

namespace JamesFrowen.PositionSync
{
    [CreateAssetMenu(fileName = "PackSettings", menuName = "ScriptableObjects/PackSettings", order = 1)]
    public class PackSettings3D : ScriptableObject
    {
        [Header("Var Size Compression")]
        [Tooltip("How many bits will be used for a value before having to include another block.\nBest value will be a fraction of log2(worldSize / precision).\n" +
            "The default values of 5 Block Size and 1/1000 precision will mean values under 54m will be 18 bits, values under 1747m will be 24 bits")]
        [SerializeField] internal int _blockSize = 5;

        [Header("Position Compression")]
        [SerializeField] public float Precision = 1 / 100f;

        [Header("Rotation Compression")]
        [SerializeField] internal int _quaternionBitLength = 10;

        internal VarVector3Packer _positionPacker;
        internal QuaternionPacker _rotationPacker;
    }

    public static class PackSettings3DExtensions
    {
        private static readonly int defaultBlockSize = 5;
        private static readonly VarVector3Packer defaultPositionPacker = new VarVector3Packer(Vector3.one / 300f, defaultBlockSize);
        private static readonly QuaternionPacker defaultRotationPacker = new QuaternionPacker(10);

        public static VarVector3Packer GetPositionPacker(this PackSettings3D settings)
        {
            if (settings == null)
                return defaultPositionPacker;

            if (settings._positionPacker == null)
                settings._positionPacker = new VarVector3Packer(Vector3.one * settings.Precision, settings._blockSize);

            return settings._positionPacker;
        }

        public static QuaternionPacker GetRotationPacker(this PackSettings3D settings)
        {
            if (settings == null)
                return defaultRotationPacker;

            if (settings._rotationPacker == null)
                settings._rotationPacker = new QuaternionPacker(settings._quaternionBitLength);

            return settings._rotationPacker;
        }

        public static int GetBlockSize(this PackSettings3D settings)
        {
            if (settings == null)
                return 5;

            return settings._blockSize;
        }
    }
}

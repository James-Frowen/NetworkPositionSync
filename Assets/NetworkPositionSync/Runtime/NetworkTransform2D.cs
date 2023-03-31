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

using Mirage.Serialization;
using UnityEngine;

namespace JamesFrowen.PositionSync
{
    public class NetworkTransform2D : NetworkTransformBase<NetworkTransform2D.Snapshot>
    {
        [SerializeField] private PackSettings2D _packSettings;
        [SerializeField] private float _positionSensitivity = 0.01f;
        [SerializeField] private float _rotationSensitivity = 0.01f;

        private VarVector2Packer _positionPacker;
        private AnglePacker _rotationPacker;

        protected override void NetworkStart()
        {
            _positionPacker = _packSettings.GetPositionPacker();
            _rotationPacker = _packSettings.GetRotationPacker();
        }

        protected override Snapshot CreateSnapshot()
        {
            return new Snapshot(
                useLocalSpace ? transform.localPosition : transform.position,
                useLocalSpace ? transform.localEulerAngles.z : transform.eulerAngles.z
            );
        }

        protected override void ApplySnapshot(Snapshot newSnapshot)
        {
            if (useLocalSpace)
            {
                transform.localPosition = newSnapshot.Position;
                transform.localEulerAngles = Vector3.forward * newSnapshot.Rotation;
            }
            else
            {
                transform.position = newSnapshot.Position;
                transform.eulerAngles = Vector3.forward * newSnapshot.Rotation;
            }
        }

        protected override bool HasMoved(Snapshot newSnapshot)
        {
            return Vector2.Distance(newSnapshot.Position, _snapshot.Position) > _positionSensitivity
                || Mathf.Abs(Mathf.DeltaAngle(newSnapshot.Rotation, _snapshot.Rotation)) > _rotationSensitivity;
        }

        protected override void WriteSnapshot(NetworkWriter writer)
        {
            // we store values in these fields when Isdirty is called,
            // so get them from that instead of transform again
            _positionPacker.Pack(writer, _snapshot.Position);
            _rotationPacker.Pack(writer, _snapshot.Rotation);
        }

        protected override Snapshot ReadSnapshot(NetworkReader reader)
        {
            return new Snapshot(
                _positionPacker.Unpack(reader),
                _rotationPacker.Unpack(reader));
        }

        protected override ISnapshotInterpolator<Snapshot> CreateInterpolator() => new Interpolator();

        public struct Snapshot
        {
            public Vector2 Position;
            public float Rotation;

            public Snapshot(Vector2 position, float rotation)
            {
                Position = position;
                Rotation = rotation;
            }

            public override string ToString()
            {
                return $"[{Position}, {Rotation}]";
            }
        }

        private class Interpolator : ISnapshotInterpolator<Snapshot>
        {
            public Snapshot Lerp(Snapshot a, Snapshot b, float alpha)
            {
                var pos = Vector2.Lerp(a.Position, b.Position, alpha);
                var rot = Mathf.LerpAngle(a.Rotation, b.Rotation, alpha);
                return new Snapshot(pos, rot);
            }
        }
    }
}

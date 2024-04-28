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

namespace Mirage.SyncPosition
{
    public class NetworkTransform3D : NetworkTransformBase<NetworkTransform3D.Snapshot>
    {
        [SerializeField] private PackSettings3D _packSettings;
        [SerializeField] private float _positionSensitivity = 0.01f;
        [SerializeField] private float _rotationSensitivity = 0.01f;

        // todo calculate actual maxSize from pack settings
        //      it is fine to overestimate for now
        public override int MaxWriteSize => 4 * 7; // snapshot is 7 floats

        private VarVector3Packer _positionPacker;
        private QuaternionPacker _rotationPacker;

        public override void Setup()
        {
            _positionPacker = _packSettings.GetPositionPacker();
            _rotationPacker = _packSettings.GetRotationPacker();
        }

        protected override bool CreateSnapshot(out Snapshot newSnapshot, bool force)
        {
            newSnapshot = CreateSnapshot();
            return force || HasChanged(newSnapshot);
        }
        private Snapshot CreateSnapshot()
        {
            switch (_coordinatesType.Space)
            {
                default:
                case Coordinates.World:
                    return new Snapshot(transform.position, transform.rotation);
                case Coordinates.Local:
                    return new Snapshot(transform.localPosition, transform.localRotation);
                case Coordinates.Relative:
                    var other = _coordinatesType.RelativeTo;
                    var relPos = other.position - transform.position;
                    // todo needs testing
                    var relRot = Quaternion.Inverse(other.rotation) * transform.rotation;
                    return new Snapshot(relPos, relRot);
            }
        }
        private bool HasChanged(Snapshot newSnapshot)
        {
            return Vector3.Distance(newSnapshot.Position, _snapshot.Position) > _positionSensitivity
                || Quaternion.Angle(newSnapshot.Rotation, _snapshot.Rotation) > _rotationSensitivity;
        }

        protected override void ApplySnapshot(Snapshot newSnapshot)
        {
            switch (_coordinatesType.Space)
            {
                default:
                case Coordinates.World:
                    transform.position = newSnapshot.Position;
                    transform.rotation = newSnapshot.Rotation;
                    return;
                case Coordinates.Local:
                    transform.localPosition = newSnapshot.Position;
                    transform.localRotation = newSnapshot.Rotation;
                    return;
                case Coordinates.Relative:
                    var other = _coordinatesType.RelativeTo;
                    transform.position = other.position + newSnapshot.Position;
                    transform.rotation = other.rotation * newSnapshot.Rotation;
                    return;
            }
        }


        protected override void WriteSnapshot(NetworkWriter writer, Snapshot snapshot)
        {
            _positionPacker.Pack(writer, snapshot.Position);
            _rotationPacker.Pack(writer, snapshot.Rotation);
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
            public Vector3 Position;
            public Quaternion Rotation;

            public Snapshot(Vector3 position, Quaternion rotation)
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
                var pos = Vector3.Lerp(a.Position, b.Position, alpha);
                var rot = Quaternion.Slerp(a.Rotation, b.Rotation, alpha);
                return new Snapshot(pos, rot);
            }
        }
    }
}

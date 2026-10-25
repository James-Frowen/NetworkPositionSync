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
    [AddComponentMenu("Network/SyncPosition/NetworkTransform2D")]
    public class NetworkTransform2D : NetworkTransformBase<NetworkTransform2D.Snapshot>
    {
        [Tooltip("Set what values to use for position and rotation.\nworld use transform.position\nlocal uses transform.localPosition\nrelative uses position and rotation in RelativeTo's space (scale is ignored)")]
        [SerializeField] protected CoordinatesType _coordinatesType = new CoordinatesType { Space = Coordinates.World };

        [Header("Compression")]
        [SerializeField] private PackSettings2D _packSettings;
        [SerializeField] private float _positionSensitivity = 0.01f;
        [SerializeField] private float _rotationSensitivity = 0.01f;

        // todo calculate actual maxSize from pack settings
        //      it is fine to overestimate for now
        public override int MaxWriteSize => 4 * 3; // snapshot is 3 floats

        // get packers when used, instead of in Setup, so that snapshots can be read before Setup is called (eg from spawn payload)
        // packers are cached on PackSettings so this is cheap
        private VarVector2Packer PositionPacker => _packSettings.GetPositionPacker();
        private AnglePacker RotationPacker => _packSettings.GetRotationPacker();

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
                    return new Snapshot(transform.position, transform.eulerAngles.z);
                case Coordinates.Local:
                    return new Snapshot(transform.localPosition, transform.localEulerAngles.z);
                case Coordinates.Relative:
                    var other = _coordinatesType.RelativeTo;
                    var otherAngle = other.eulerAngles.z;
                    var relPos = Rotate(transform.position - other.position, -otherAngle);
                    var relRot = transform.eulerAngles.z - otherAngle;
                    return new Snapshot(relPos, relRot);
            }
        }
        private bool HasChanged(Snapshot newSnapshot)
        {
            return Vector2.Distance(newSnapshot.Position, _snapshot.Position) > _positionSensitivity
                || Mathf.Abs(Mathf.DeltaAngle(newSnapshot.Rotation, _snapshot.Rotation)) > _rotationSensitivity;
        }

        protected override void ApplySnapshot(Snapshot newSnapshot)
        {
            switch (_coordinatesType.Space)
            {
                default:
                case Coordinates.World:
                    transform.position = WithXY(transform.position, newSnapshot.Position);
                    transform.eulerAngles = WithZ(transform.eulerAngles, newSnapshot.Rotation);
                    return;
                case Coordinates.Local:
                    transform.localPosition = WithXY(transform.localPosition, newSnapshot.Position);
                    transform.localEulerAngles = WithZ(transform.localEulerAngles, newSnapshot.Rotation);
                    return;
                case Coordinates.Relative:
                    var other = _coordinatesType.RelativeTo;
                    var otherAngle = other.eulerAngles.z;
                    transform.position = WithXY(transform.position, (Vector2)other.position + Rotate(newSnapshot.Position, otherAngle));
                    transform.eulerAngles = WithZ(transform.eulerAngles, otherAngle + newSnapshot.Rotation);
                    return;
            }
        }

        // only x/y position and z rotation are synced, keep the other values that are already on the transform
        private static Vector3 WithXY(Vector3 current, Vector2 xy) => new Vector3(xy.x, xy.y, current.z);
        private static Vector3 WithZ(Vector3 current, float z) => new Vector3(current.x, current.y, z);

        /// <summary>
        /// Rotates a 2D vector around the z axis
        /// </summary>
        private static Vector2 Rotate(Vector2 vector, float angle)
        {
            return Quaternion.Euler(0, 0, angle) * vector;
        }

        protected override void WriteSnapshot(NetworkWriter writer, Snapshot snapshot)
        {
            PositionPacker.Pack(writer, snapshot.Position);
            RotationPacker.Pack(writer, snapshot.Rotation);
        }

        protected override Snapshot ReadSnapshot(NetworkReader reader)
        {
            return new Snapshot(
                PositionPacker.Unpack(reader),
                RotationPacker.Unpack(reader));
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
            public Snapshot Interpolate(double from, Snapshot a, double to, Snapshot b, float alpha)
            {
                var pos = Vector2.Lerp(a.Position, b.Position, alpha);
                var rot = Mathf.LerpAngle(a.Rotation, b.Rotation, alpha);
                return new Snapshot(pos, rot);
            }
        }
    }
}

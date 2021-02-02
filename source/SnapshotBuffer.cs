using Mirror;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace JamesFrowen.PositionSync
{
    public struct TransformState
    {
        public readonly Vector3 position;
        public readonly Quaternion rotation;

        public TransformState(Vector3 position, Quaternion rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }

        public override string ToString()
        {
            return $"[{this.position}, {this.rotation}]";
        }
    }

    public static class TransformStateWriter
    {
        public static void WriteTransformState(this NetworkWriter writer, TransformState value)
        {
            writer.WriteVector3(value.position);
            writer.WriteUInt32(Compression.CompressQuaternion(value.rotation));
        }

        public static TransformState ReadTransformState(this NetworkReader reader)
        {
            var position = reader.ReadVector3();
            var rotation = Compression.DecompressQuaternion(reader.ReadUInt32());

            return new TransformState(position, rotation);
        }
    }
    public class SnapshotBuffer
    {
        static readonly ILogger logger = LogFactory.GetLogger<SnapshotBuffer>(LogType.Error);

        struct Snapshot
        {
            /// <summary>
            /// Server Time
            /// </summary>
            public readonly double time;
            public readonly TransformState state;

            public Snapshot(TransformState state, double time) : this()
            {
                this.state = state;
                this.time = time;
            }
        }

        readonly List<Snapshot> buffer = new List<Snapshot>();

        public bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this.buffer.Count == 0;
        }
        public int SnapshotCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this.buffer.Count;
        }

        Snapshot First
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this.buffer[0];
        }
        Snapshot Last
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this.buffer[this.buffer.Count - 1];
        }

        public void AddSnapShot(TransformState state, double serverTime)
        {
            if (!this.IsEmpty && serverTime < this.Last.time)
            {
                throw new ArgumentException($"Can not add Snapshot to buffer out of order, last t={this.Last.time:0.000}, new t={serverTime:0.000}");
            }

            this.buffer.Add(new Snapshot(state, serverTime));
        }

        /// <summary>
        /// Gets snapshot to use for interpolation
        /// <para>this method should not be called when there are no snapshots in buffer</para>
        /// </summary>
        /// <param name="now"></param>
        /// <returns></returns>
        public TransformState GetLinearInterpolation(double now)
        {
            if (this.buffer.Count == 0)
            {
                throw new InvalidOperationException("No snapshots in buffer");
            }

            // first snapshot
            if (this.buffer.Count == 1)
            {
                if (logger.LogEnabled()) logger.Log("First snapshot");

                return this.First.state;
            }

            // if first snapshot is after now, there is no "from", so return same as first snapshot
            if (this.First.time > now)
            {
                if (logger.LogEnabled()) logger.Log($"No snapshots for t={now:0.000}, using earliest t={this.buffer[0].time:0.000}");

                return this.First.state;
            }

            // if last snapshot is before now, there is no "to", so return last snapshot
            // this can happen if server hasn't sent new data
            // there could be no new data from either lag or because object hasn't moved
            if (this.Last.time < now)
            {
                if (logger.WarnEnabled()) logger.LogWarning($"No snapshots for t={now:0.000}, using first t={this.buffer[0].time:0.000} last t={this.Last.time:0.000}");

                return this.Last.state;
            }

            // edge cases are returned about, if code gets to this for loop then a valid from/to should exist
            for (var i = 0; i < this.buffer.Count - 1; i++)
            {
                var from = this.buffer[i];
                var to = this.buffer[i + 1];
                var fromTime = this.buffer[i].time;
                var toTime = this.buffer[i + 1].time;

                // if between times, then use from/to
                if (fromTime <= now && now <= toTime)
                {
                    var alpha = (float)this.Clamp01((now - fromTime) / (toTime - fromTime));
                    if (logger.LogEnabled()) { logger.Log($"alpha:{alpha:0.000}"); }
                    var pos = Vector3.Lerp(from.state.position, to.state.position, alpha);
                    var rot = Quaternion.Slerp(from.state.rotation, to.state.rotation, alpha);
                    return new TransformState(pos, rot);
                }
            }

            logger.LogError("Should never be here! Code should have return from if or for loop above.");
            return this.Last.state;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double Clamp01(double v)
        {
            if (v < 0) { return 0; }
            if (v > 1) { return 1; }
            else { return v; }
        }

        /// <summary>
        /// removes snapshots older than <paramref name="oldTime"/>, but keeps atleast <paramref name="keepCount"/> snapshots in buffer that are older than oldTime
        /// <para>
        /// Keep atleast 1 snapshot older than old time so there is something to interoplate from
        /// </para>
        /// </summary>
        /// <param name="oldTime"></param>
        /// <param name="keepCount">minium number of snapshots to keep in buffer</param>
        public void RemoveOldSnapshots(float oldTime)
        {
            // loop from newest to oldest
            for (var i = this.buffer.Count - 1; i >= 0; i--)
            {
                // older than oldTime
                if (this.buffer[i].time < oldTime)
                {
                    this.buffer.RemoveAt(i);
                }
            }
        }

        public override string ToString()
        {
            if (this.buffer.Count == 0) { return "Buffer Empty"; }

            var builder = new StringBuilder();
            builder.AppendLine($"count:{this.buffer.Count}, minTime:{this.buffer[0].time}, maxTime:{this.buffer[this.buffer.Count - 1].time}");
            for (var i = 0; i < this.buffer.Count; i++)
            {
                builder.AppendLine($"  {i}: {this.buffer[i].time}");
            }
            return builder.ToString();
        }
    }
}

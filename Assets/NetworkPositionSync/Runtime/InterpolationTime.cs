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

using System.Runtime.CompilerServices;
using Mirage;
using Mirage.Logging;
using UnityEngine;

namespace JamesFrowen.PositionSync
{
    /// <summary>
    /// Synchronizes time between server and client via regular messages between server and client.
    /// <para>Can be used for snapshot interpolation</para>
    /// </summary>
    /// <remarks>
    /// This class will speed up or slow down the client time scale, depending if it is ahead or behind the lastest server time.
    /// <para>
    /// Every update we add DeltaTime * TimeScale to client time.
    /// </para>
    /// <para>
    /// On the server, when an update is performed, the server will send a message back with its time.<br/>
    /// When the client receives this message, it calculates the difference between server time and its own local time.<br/>
    /// This difference is stored in a moving average, which is smoothed out.
    /// </para>
    /// <para>
    /// If the calculated difference is greater or less than a threshold then we adjust the client time scale by speeding up or slowing down.<br/>
    /// If the calculated difference is between our defined threshold times, client time scale is set back to normal.
    /// </para>
    /// <para>
    /// This client time can then be used to snapshot interpolation using <c>InterpolationTime = ClientTime - Offset</c>
    /// </para>
    /// <para>
    /// Some other implementations include the offset in the time scale calculations itself,
    /// So that Client time is always (2) intervals behind the received server time. <br/>
    /// Moving that offset to outside this class should still give the same results.
    /// We are just trying to make the difference equal to 0 instead of negative offset.
    /// Then subtracking offset from the ClientTime before we do the interpolation
    /// </para>
    /// </remarks>
    public class InterpolationTime
    {
        private static readonly ILogger logger = LogFactory.GetLogger<InterpolationTime>();
        private bool initialized;

        /// <summary>
        /// The time value that the client uses to interpolate
        /// </summary>
        private float _clientTime;

        /// <summary>
        /// The client will multiply deltaTime by this scale time value each frame
        /// </summary>
        private float clientScaleTime;
        private readonly ExponentialMovingAverage diffAvg;

        /// <summary>
        /// How much above the goalOffset difference are we allowed to go before changing the timescale
        /// </summary>
        private readonly float positiveThreshold;

        /// <summary>
        /// How much below the goalOffset difference are we allowed to go before changing the timescale
        /// </summary>
        private readonly float negativeThreshold;

        /// <summary>
        /// how much to modify time scale by if client is ahead/behind the server
        /// </summary>
        private readonly float _scaleModifier;

        /// <summary>
        /// Is the difference between previous time and new time too far apart?
        /// If so, reset the client time.
        /// </summary>
        private readonly float _skipAheadThreshold;
        private float _clientDelay;

        // Used for debug purposes. Move along...
        private float _latestServerTime;

        /// <summary>
        /// Timer that follows server time
        /// </summary>
        public float ClientTime
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _clientTime;
        }
        /// <summary>
        /// Returns the last time received by the server
        /// </summary>
        public float LatestServerTime
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _latestServerTime;
        }

        [System.Obsolete("Use Time instead")]
        public float InterpolationTimeField
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Time;
        }

        /// <summary>
        /// Current time to use for interpolation 
        /// </summary>
        public float Time
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _clientTime - _clientDelay;
        }

        public float ClientDelay
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _clientDelay;
            set => _clientDelay = value;
        }

        // Used for debug purposes. Move along...
        public float DebugScale => clientScaleTime;

        /// <param name="diffThreshold">How far off client time can be before changing its speed. A good recommended value is half of SyncInterval.</param>
        /// <param name="skipThreshold">How many ticks behind before skipping ahead to catch up</param>
        /// <param name="movingAverageCount">How many ticks are used for averaging purposes, you may need to increase or decrease with frame rate.</param>
        public InterpolationTime(float tickInterval, float diffThreshold = 0.5f, float timeScale = 0.01f, float skipThreshold = 20f, float tickDelay = 2, int movingAverageCount = 30)
        {
            positiveThreshold = tickInterval * diffThreshold;
            negativeThreshold = -positiveThreshold;
            _skipAheadThreshold = tickInterval * skipThreshold;

            _scaleModifier = timeScale;

            _clientDelay = tickInterval * tickDelay;

            diffAvg = new ExponentialMovingAverage(movingAverageCount);

            // Client should always start at normal time scale.
            clientScaleTime = 1f;
        }

        /// <summary>
        /// Updates the client time.
        /// </summary>
        /// <param name="deltaTime"></param>
        public void OnUpdate(float deltaTime)
        {
            _clientTime += deltaTime * clientScaleTime;
        }

        public bool IsMessageOutOfOrder(float newServerTime)
        {
            return newServerTime < _latestServerTime;
        }

        /// <summary>
        /// Updates <see cref="clientScaleTime"/> to keep <see cref="ClientTime"/> in line with <see cref="LatestServerTime"/>
        /// </summary>
        /// <param name="serverTime"></param>
        public void OnMessage(float serverTime)
        {
            // only check this if we are initialized
            if (initialized)
                logger.Assert(serverTime > _latestServerTime, $"Received message out of order. Server Time: {serverTime} vs New Time: {_latestServerTime}");

            _latestServerTime = serverTime;

            // If this is the first message, set the client time to the server difference.
            // If we're too far behind, then we should reset things too.

            // todo check this is correct
            if (!initialized)
            {
                InitNew(serverTime);
                return;
            }

            // Calculate the difference.
            var diff = serverTime - _clientTime;

            // Are we falling behind?
            if (serverTime - _clientTime > _skipAheadThreshold)
            {
                logger.LogWarning($"Client fell behind, skipping ahead. Server Time: {serverTime:0.00}, Difference: {diff:0.00}");
                InitNew(serverTime);
                return;
            }

            diffAvg.Add(diff);

            // Adjust the client time scale with the appropriate value.
            // clamp just incase user given timeScale is a bad value
            clientScaleTime = Mathf.Clamp(CalculateTimeScale((float)diffAvg.Value), 0.5f, 2f);

            // todo add trace level
            if (logger.LogEnabled()) logger.Log($"st: {serverTime:0.00}, ct: {_clientTime:0.00}, diff: {diff * 1000:0.0}, wanted: {diffAvg.Value * 1000:0.0}, scale: {clientScaleTime}");
        }

        /// <summary>
        /// Call this when start new client to reset timer
        /// </summary>
        public void Reset()
        {
            // mark this so first server method will call InitNew
            initialized = false;
            _latestServerTime = 0;
        }

        /// <summary>
        /// Initializes and resets the system.
        /// </summary>
        private void InitNew(float serverTime)
        {
            _clientTime = serverTime;
            clientScaleTime = normalScale;
            diffAvg.Reset();
            initialized = true;
        }

        /// <summary>
        /// Adjusts the client time scale based on the provided difference.
        /// </summary>
        private float CalculateTimeScale(float diff)
        {
            // Difference is calculated between server and client.
            // So if that difference is positive, we can run the client faster to catch up.
            // However, if it's negative, we need to slow the client down otherwise we run out of snapshots.            
            // Ideally, we want the difference vs the goal to be as close to 0 as possible.

            if (diff > positiveThreshold * 10) // really far ahead,
                return 1 + (_scaleModifier * 8);

            else if (diff > positiveThreshold) // Server's ahead of us, we need to speed up.
                return 1 + _scaleModifier;

            else if (diff < negativeThreshold * 10) // really far behind
                return 1 - (_scaleModifier * 20);

            else if (diff < negativeThreshold) // Server is falling behind us, we need to slow down.
                // *2 here because we want to slow down faster, 
                // if we dont there wont be any new snapshots to interpolate towards and game will be jittery
                return 1 - _scaleModifier * 4; 

            else // Server and client are on par ("close enough"). Run at normal speed.
                return 1;
        }
    }
}

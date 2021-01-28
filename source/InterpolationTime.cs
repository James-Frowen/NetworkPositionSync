using System.Runtime.CompilerServices;
using Mirror;
using UnityEngine;

namespace JamesFrowen.PositionSync
{
    public class InterpolationTime
    {
        static readonly ILogger logger = LogFactory.GetLogger<InterpolationTime>(LogType.Error);

        bool intialized;
        /// <summary>
        /// time client uses to interoplolate
        /// </summary>
        double clientTime;
        float clientScaleTime;

        readonly ExponentialMovingAverage diffAvg;

        readonly float syncInterval = 0.1f;
        readonly float goalOffset;

        readonly float positiveOffset;
        readonly float negativeOffset;

        readonly float fastScale = 1.01f;
        readonly float normalScale = 1f;
        readonly float slowScale = 0.99f;

        // debug
        double previousServerTime;


        public double ClientTime
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => clientTime;
        }
        public double ServerTime
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => previousServerTime;
        }

        public InterpolationTime(float syncInterval, int movingAverageCount = 30)
        {
            this.syncInterval = syncInterval;
            goalOffset = syncInterval * 2;

            positiveOffset = syncInterval * 2;
            negativeOffset = syncInterval / 2;

            diffAvg = new ExponentialMovingAverage(movingAverageCount);
        }

        public void OnTick(float deltaTime)
        {
            clientTime += deltaTime * clientScaleTime;
        }

        public void OnMessage(double serverTime)
        {
            // if first message set client time to server-diff
            if (!intialized)
            {
                previousServerTime = serverTime;
                clientTime = serverTime - goalOffset;
                intialized = true;
                return;
            }

            Debug.Assert(serverTime > previousServerTime, "Recieved message out of order.");

            previousServerTime = serverTime;

            double diff = serverTime - clientTime;
            diffAvg.Add(diff);
            // diff is server-client,
            // we want client to be 2 frames behind so that there is always snapshots to interoplate towards
            // server-client-offset
            // if positive then server is ahead, => we can run client faster to catch up
            // if negative then server is behind, => we need to run client slow to not run out of spanshots

            // we want diffVsGoal to be as close to 0 as possible
            double diffVsGoal = diffAvg.Value - goalOffset;
            if (diffAvg.Value > positiveOffset)
                clientScaleTime = fastScale;
            else if (diffAvg.Value < negativeOffset)
                clientScaleTime = slowScale;
            else
                clientScaleTime = normalScale;

            if (logger.LogEnabled()) { logger.Log($"st {serverTime:0.00} ct {clientTime:0.00} diff {diff * 1000:0.0}, wanted:{diffVsGoal * 1000:0.0}, scale:{clientScaleTime}"); }
        }
    }
}

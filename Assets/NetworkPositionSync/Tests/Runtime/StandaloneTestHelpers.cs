using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Mirage.Serialization;
using NUnit.Framework;
using UnityEngine;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// Helpers for tests of standalone mode (behaviours that sync themselves using RPCs when there is no <see cref="SyncPositionSystem"/>)
    /// </summary>
    public static class StandaloneTestHelpers
    {
        /// <summary>
        /// Max time it should take for a change to be visible on another instance.
        /// <para>Includes interpolation delay with a lot of extra intervals so that it doesn't fail because of slow frames</para>
        /// </summary>
        public static float Timeout(NetworkTransformBase behaviour)
        {
            return behaviour._standaloneSyncInterval * (behaviour._standaloneInterpolationDelay + 10);
        }

        /// <summary>
        /// Waits for <paramref name="count"/> standalone sync intervals of <paramref name="behaviour"/>
        /// <para>Only use this when checking that something does NOT happen, otherwise use <see cref="SyncPositionTestHelpers.WaitUntil"/></para>
        /// </summary>
        public static IEnumerator WaitForSyncs(NetworkTransformBase behaviour, int count)
        {
            var end = Time.unscaledTime + (behaviour._standaloneSyncInterval * count);
            while (Time.unscaledTime < end)
                yield return null;
        }

        /// <summary>
        /// Writes <paramref name="value"/> the same way <see cref="MockNetworkTransform"/> writes its snapshot
        /// </summary>
        public static byte[] CreateMockPayload(float value)
        {
            using (var writer = NetworkWriterPool.GetWriter())
            {
                writer.Write(new MockNetworkTransform.Data { Value = value });
                return writer.ToArray();
            }
        }

        /// <summary>
        /// Calls the private CmdSnapshot ServerRpc on <paramref name="behaviour"/>.
        /// Because the method is woven, calling it on a client sends it to the server.
        /// <para>Exceptions thrown by the method are unwrapped from <see cref="TargetInvocationException"/></para>
        /// </summary>
        public static void InvokeCmdSnapshot(NetworkTransformBase behaviour, double clientTime, byte[] payload)
        {
            var method = FindMethod(behaviour.GetType(), "CmdSnapshot");
            Assert.That(method, Is.Not.Null, "Could not find CmdSnapshot method");

            try
            {
                method.Invoke(behaviour, new object[] { clientTime, new ArraySegment<byte>(payload) });
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            }
        }

        private static MethodInfo FindMethod(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var parameterTypes = new[] { typeof(double), typeof(ArraySegment<byte>) };
            for (var t = type; t != null; t = t.BaseType)
            {
                var method = t.GetMethod(name, flags, null, parameterTypes, null);
                if (method != null)
                    return method;
            }
            return null;
        }
    }

    /// <summary>
    /// Records values of an object each frame and checks that it only moves towards a target
    /// </summary>
    public sealed class MonotonicTracker
    {
        private readonly Func<float> _getValue;
        private readonly float _target;
        private readonly string _name;
        private readonly HashSet<float> _distinct = new HashSet<float>();
        private float _previous;

        public bool Reached { get; private set; }
        /// <summary>Number of different values seen, used to check that value was interpolated instead of jumping to target</summary>
        public int DistinctValues => _distinct.Count;
        public float Current => _getValue();

        /// <param name="target">Value must be greater than current value</param>
        public MonotonicTracker(string name, Func<float> getValue, float target)
        {
            _name = name;
            _getValue = getValue;
            _target = target;
            _previous = getValue();
            Assert.That(_previous, Is.LessThan(target), $"{_name} should start below target");
            _distinct.Add(_previous);
        }

        /// <summary>
        /// Call once per frame, asserts that value has not moved backwards or past target
        /// </summary>
        public void Sample(float? maxAllowed = null)
        {
            var current = _getValue();
            var max = maxAllowed ?? _target;

            Assert.That(current, Is.GreaterThanOrEqualTo(_previous), $"{_name} should only move towards {_target}, previous:{_previous} current:{current}");
            Assert.That(current, Is.LessThanOrEqualTo(max), $"{_name} should not move past {max}");

            _distinct.Add(current);
            if (current == _target)
                Reached = true;

            _previous = current;
        }

        /// <summary>
        /// Samples each frame until target is reached or timeout, then asserts it was reached
        /// </summary>
        public IEnumerator WaitUntilReached(float timeout, Action extraChecks = null)
        {
            var end = Time.unscaledTime + timeout;
            while (!Reached && Time.unscaledTime < end)
            {
                yield return null;
                extraChecks?.Invoke();
                Sample();
            }

            Assert.That(Reached, Is.True, $"{_name} should have reached {_target}, but was {_getValue()}");
        }
    }
}

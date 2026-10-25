using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Mirage.Tests;
using Mirage.Tests.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// Collects errors, asserts and exceptions logged while it is active.
    /// <para>
    /// Uses <see cref="Application.logMessageReceived"/> so that exceptions thrown from Unity callbacks (eg LateUpdate) are also caught.
    /// Unity's test runner also fails tests that log errors, this just gives a clearer failure message at the point it is checked.
    /// </para>
    /// </summary>
    public sealed class ErrorLogCollector : IDisposable
    {
        public readonly List<string> Errors = new List<string>();

        public ErrorLogCollector()
        {
            Application.logMessageReceived += OnLog;
        }

        public void Dispose()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                Errors.Add($"{type}: {condition}\n{stackTrace}");
        }

        public void AssertNoErrors()
        {
            Assert.That(Errors, Is.Empty, $"Expected no errors to be logged, but got {Errors.Count}:\n{string.Join("\n", Errors)}");
        }
    }

    /// <summary>
    /// Counts <see cref="SyncPositionSystem.PositionMessage"/> that the server receives from clients
    /// </summary>
    public sealed class ServerPositionMessageCounter
    {
        /// <summary>Messages that contain atleast 1 entry</summary>
        public int WithData;
        public int Total;
    }

    public static class SyncPositionTestHelpers
    {
        public static SyncPositionSystem.Settings DefaultSettings => SyncPositionSystem.Settings.Default;

        /// <summary>
        /// Adds and sets up a system, must be called before server/client is started
        /// </summary>
        public static SyncPositionSystem AddSystem(GameObject go, NetworkServer server, NetworkClient client, SyncPositionSystem.Settings settings, bool includeWriteSize = true)
        {
            var system = go.AddComponent<SyncPositionSystem>();
            system._includeWriteSize = includeWriteSize;
            system.Setup(server: server, client: client, settings: settings);
            return system;
        }

        /// <summary>
        /// Max time it should take for a change to be visible on a client.
        /// <para>Includes interpolation delay with a lot of extra intervals so that it doesn't fail because of slow frames</para>
        /// </summary>
        public static float Timeout(SyncPositionSystem.Settings settings)
        {
            return settings.SyncInterval * (settings.InterpolationDelay + 10);
        }

        /// <summary>
        /// Waits until <paramref name="predicate"/> is true, or until timeout. Caller should assert the condition after.
        /// </summary>
        public static IEnumerator WaitUntil(Func<bool> predicate, float timeoutSeconds)
        {
            var end = Time.unscaledTime + timeoutSeconds;
            while (!predicate() && Time.unscaledTime < end)
                yield return null;
        }

        /// <summary>
        /// Waits for <paramref name="count"/> sync intervals
        /// </summary>
        public static IEnumerator WaitForSyncs(SyncPositionSystem.Settings settings, int count)
        {
            var end = Time.unscaledTime + (settings.SyncInterval * count);
            while (Time.unscaledTime < end)
                yield return null;
        }

        /// <summary>
        /// Replaces server handler for <see cref="SyncPositionSystem.PositionMessage"/> with one that counts messages, and then passes them to <paramref name="serverSystem"/>
        /// <para>Must be called after server has started</para>
        /// </summary>
        public static ServerPositionMessageCounter CountClientMessages(NetworkServer server, SyncPositionSystem serverSystem)
        {
            var counter = new ServerPositionMessageCounter();
            MessageDelegateWithPlayer<SyncPositionSystem.PositionMessage> handler = (player, msg) =>
            {
                counter.Total++;
                if (msg.MetaPayload.Count > 0)
                    counter.WithData++;

                serverSystem.ServerHandleNetworkPositionMessage(player, msg);
            };
            server.MessageHandler.RegisterHandler(handler, false);
            return counter;
        }

        public static bool Approximately(Vector3 a, Vector3 b, float tolerance = 0.01f)
        {
            return Vector3.Distance(a, b) < tolerance;
        }
    }

    public class SpawnedPair<T> where T : NetworkBehaviour
    {
        public T Server;
        public T Client;
    }

    /// <summary>
    /// Server and 1 remote client, both with a <see cref="SyncPositionSystem"/>
    /// </summary>
    public abstract class SyncPositionTestBase<T> : ClientServerSetup<T> where T : NetworkBehaviour
    {
        protected SyncPositionSystem.Settings Settings = SyncPositionTestHelpers.DefaultSettings;
        protected SyncPositionSystem serverSystem;
        /// <summary>System for first client</summary>
        protected SyncPositionSystem clientSystem;
        protected ErrorLogCollector errorLogs;

        protected virtual bool IncludeWriteSize => true;

        protected float Timeout => SyncPositionTestHelpers.Timeout(Settings);

        protected override void ExtraServerSetup()
        {
            base.ExtraServerSetup();

            // fixture instance is reused between tests, so reset fields here
            clientSystem = null;
            errorLogs?.Dispose();
            errorLogs = new ErrorLogCollector();

            serverSystem = SyncPositionTestHelpers.AddSystem(serverGo, server, null, Settings, IncludeWriteSize);
        }

        protected override void ExtraClientSetup(IClientInstance instance)
        {
            base.ExtraClientSetup(instance);

            var system = SyncPositionTestHelpers.AddSystem(instance.GameObject, null, instance.Client, Settings, IncludeWriteSize);
            if (clientSystem == null)
                clientSystem = system;
        }

        public override void ExtraTearDown()
        {
            base.ExtraTearDown();
            errorLogs?.Dispose();
            errorLogs = null;
        }

        /// <summary>
        /// Spawns copies of _characterPrefab and waits for them to spawn on the first client
        /// </summary>
        protected async UniTask<List<SpawnedPair<T>>> SpawnObjects(int count, INetworkPlayer owner = null)
        {
            var identities = new List<NetworkIdentity>();
            for (var i = 0; i < count; i++)
            {
                var clone = InstantiateForTest(_characterPrefab);
                clone.name = $"object {i} (server)";
                serverObjectManager.Spawn(clone, owner);
                identities.Add(clone);
            }

            var pairs = new List<SpawnedPair<T>>();
            foreach (var identity in identities)
            {
                var clientIdentity = await AsyncUtil.WaitUntilSpawn(client.World, identity.NetId);
                pairs.Add(new SpawnedPair<T>
                {
                    Server = identity.GetComponent<T>(),
                    Client = clientIdentity.GetComponent<T>(),
                });
            }
            return pairs;
        }
    }
}

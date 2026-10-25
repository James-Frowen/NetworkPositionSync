using System;
using System.Collections;
using Mirage.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mirage.SyncPosition.Tests.Runtime
{
    /// <summary>
    /// InterpolationTime should be reset when client starts again, so that time from a previous server does not affect new connection
    /// </summary>
    [Category("NetworkPositionSync")]
    public class InterpolationTimeResetTests : SyncPositionTestBase<NetworkTransform3D>
    {
        /// <summary>
        /// Time that is less than any time the server will have sent before
        /// </summary>
        private const double RESTARTED_SERVER_TIME = 0.5;

        private IEnumerator Reconnect()
        {
            client.Disconnect();
            yield return SyncPositionTestHelpers.WaitUntil(() => !client.Active && server.AllPlayers.Count == 0, Timeout);
            Assert.That(client.Active, Is.False, "client should have stopped");
            Assert.That(server.AllPlayers.Count, Is.Zero, "server should have removed player");

            client.Connect("localhost");
        }

        private IEnumerator WaitForAuthenticated()
        {
            yield return SyncPositionTestHelpers.WaitUntil(() => client.IsConnected && client.Player.IsAuthenticated && server.AuthenticatedPlayers.Count == 1, Timeout);
            Assert.That(client.IsConnected, Is.True, "client should have connected again");
            Assert.That(client.Player.IsAuthenticated, Is.True, "client should be authenticated");
            Assert.That(server.AuthenticatedPlayers.Count, Is.EqualTo(1), "server should have authenticated player");
        }

        [UnityTest]
        public IEnumerator LatestServerTimeIsResetWhenClientStartsAgain()
        {
            // wait for messages from server
            yield return SyncPositionTestHelpers.WaitUntil(() => clientSystem.InterpolationTime.LatestServerTime > 0, Timeout);
            Assert.That(clientSystem.InterpolationTime.LatestServerTime, Is.GreaterThan(0), "Client should have received time from server");

            yield return Reconnect();

            // started event is invoked inside Connect, so time should be reset before any message is received
            Assert.That(clientSystem.InterpolationTime.LatestServerTime, Is.Zero, "Time should be reset when client starts");

            yield return WaitForAuthenticated();

            // syncing should work again after reconnect
            yield return SyncPositionTestHelpers.WaitUntil(() => clientSystem.InterpolationTime.LatestServerTime > 0, Timeout);
            Assert.That(clientSystem.InterpolationTime.LatestServerTime, Is.GreaterThan(0), "Client should have received time from server after reconnect");
            errorLogs.AssertNoErrors();
        }

        /// <summary>
        /// Simulates connecting to a different (or restarted) server whose time is less than the previous server
        /// </summary>
        [UnityTest]
        public IEnumerator AcceptsMessagesFromNewServerWithEarlierTime()
        {
            yield return SyncPositionTestHelpers.WaitUntil(() => clientSystem.InterpolationTime.LatestServerTime > RESTARTED_SERVER_TIME, Timeout);
            Assert.That(clientSystem.InterpolationTime.LatestServerTime, Is.GreaterThan(RESTARTED_SERVER_TIME), "Client should have received time from server");

            // stop server system so that only the message below is sent
            serverSystem.enabled = false;

            yield return Reconnect();
            yield return WaitForAuthenticated();

            server.AuthenticatedPlayers[0].Send(new SyncPositionSystem.PositionMessage
            {
                Time = RESTARTED_SERVER_TIME,
                MetaPayload = new ArraySegment<byte>(new byte[0]),
                DataPayload = new ArraySegment<byte>(new byte[0]),
            }, Channel.Unreliable);

            yield return SyncPositionTestHelpers.WaitUntil(() => clientSystem.InterpolationTime.LatestServerTime == RESTARTED_SERVER_TIME, Timeout);
            Assert.That(clientSystem.InterpolationTime.LatestServerTime, Is.EqualTo(RESTARTED_SERVER_TIME), "Message from new server should not be ignored as old");
            Assert.That(clientSystem.InterpolationTime.ClientTime, Is.EqualTo(RESTARTED_SERVER_TIME).Within(Settings.SyncInterval), "Client time should be initialized from new server time");

            // real messages from server should also be used
            serverSystem.enabled = true;
            yield return SyncPositionTestHelpers.WaitUntil(() => clientSystem.InterpolationTime.LatestServerTime > RESTARTED_SERVER_TIME, Timeout);
            Assert.That(clientSystem.InterpolationTime.LatestServerTime, Is.GreaterThan(RESTARTED_SERVER_TIME));

            errorLogs.AssertNoErrors();
        }
    }
}

using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using CozyTown.Unity.Npc;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class ProxyNpcDecisionJsonCodecTests
    {
        [TestCase("wait", NpcDecisionKind.Wait)]
        [TestCase("inspect_location", NpcDecisionKind.InspectLocation)]
        public void SupportedResponse_ParsesTheOperation(string operation, NpcDecisionKind expected)
        {
            var reply = new ProxyNpcDecisionJsonCodec().ParseResponse(
                "{\"schemaVersion\":1,\"operation\":\"" + operation + "\",\"locationId\":\"mina.work\"}");
            Assert.That(reply.Kind, Is.EqualTo(expected));
        }

        [TestCase("")]
        [TestCase("[]")]
        [TestCase("{broken}")]
        [TestCase("{\"operation\":\"wait\"}")]
        [TestCase("{\"schemaVersion\":2,\"operation\":\"wait\"}")]
        [TestCase("{\"schemaVersion\":1,\"operation\":\"give_coins\"}")]
        [TestCase("{\"schemaVersion\":1,\"operation\":\"inspect_location\"}")]
        [TestCase("{\"schemaVersion\":1,\"operation\":\"visit\",\"locationId\":\"mina.work\",\"activity\":\"home\"}")]
        public void InvalidResponse_IsRejected(string json)
            => Assert.Throws<System.FormatException>(() => new ProxyNpcDecisionJsonCodec().ParseResponse(json));

        [Test]
        public void OversizedResponse_IsRejectedBeforeParsing()
            => Assert.Throws<System.FormatException>(() => new ProxyNpcDecisionJsonCodec().ParseResponse(
                "{\"schemaVersion\":1,\"operation\":\"wait\",\"extra\":\"" + new string('界', 6000) + "\"}"));

        [Test]
        public void VisitResponse_ParsesOnlyTheCandidateWithoutAcceptingAnActorOrEconomicWrites()
        {
            var reply = new ProxyNpcDecisionJsonCodec().ParseResponse(
                "{\"schemaVersion\":1,\"operation\":\"visit\",\"locationId\":\"mina.work\",\"activity\":\"working\",\"durationGameMinutes\":20,\"npcId\":\"ren\",\"coins\":999}");
            Assert.That(reply, Is.Not.Null);
            Assert.That(reply.Kind, Is.EqualTo(NpcDecisionKind.Visit));
            Assert.That(reply.LocationId, Is.EqualTo("mina.work"));
            Assert.That(reply.Activity, Is.EqualTo(NpcActivity.Working));
            Assert.That(reply.DurationGameMinutes, Is.EqualTo(20));
        }

        [Test]
        public void WireRequest_IncludesBoundIdentityAndLimitsWithoutOtherResidentsPrivateContext()
        {
            var schedule = new NpcDailySchedule("mina", "mina.home", "mina.outside", "mina.entry", "mina.work",
                "mina.rest", "mina.afternoon", 360, 480, 720, 780, 1020, 1080);
            var renSchedule = new NpcDailySchedule("ren", "ren.home", "ren.outside", "ren.entry", "ren.work",
                "ren.rest", "ren.afternoon", 360, 480, 735, 780, 1020, 1080);
            var world = new NpcAgentWorld(new[] { schedule, renSchedule });
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 1));
            var client = new CaptureClient();
            using var scheduler = new NpcDecisionScheduler(world,
                new[] { new NpcDefinition("mina", "Mina", "Mina private persona", "Unused fallback"),
                    new NpcDefinition("ren", "Ren", "Ren private persona", "Hello") }, client,
                new NpcDecisionSettings(maxCallsPerDecision: 3));
            scheduler.Tick(0);

            string json = new ProxyNpcDecisionJsonCodec().SerializeRequest(client.Request);

            Assert.That(json, Is.Not.Empty);
            var payload = JsonUtility.FromJson<RequestPayload>(json);
            Assert.That(payload.schemaVersion, Is.EqualTo(1));
            Assert.That(payload.npcId, Is.EqualTo("mina"));
            Assert.That(payload.persona, Is.EqualTo("Mina private persona"));
            Assert.That(payload.worldRunId, Is.EqualTo(world.GetState("mina").WorldRunId.ToString("N")));
            Assert.That(payload.revision, Is.EqualTo(world.GetState("mina").Revision));
            Assert.That(payload.step, Is.EqualTo(1));
            Assert.That(payload.remainingCalls, Is.EqualTo(2));
            Assert.That(payload.maxActivityDurationGameMinutes, Is.EqualTo(1440));
            Assert.That(payload.knownLocationIds, Does.Contain("mina.work"));
            Assert.That(payload.hasLocationDetails, Is.False);
            Assert.That(json, Does.Not.Contain("ren.work"));
            Assert.That(json, Does.Not.Contain("Ren private persona"));
            Assert.That(json, Does.Not.Contain("Unused fallback"));
            Assert.That(json, Does.Not.Contain("inventory"));
            Assert.That(json, Does.Not.Contain("wallet"));
            scheduler.Tick(1);
            var disclosed = JsonUtility.FromJson<RequestPayload>(new ProxyNpcDecisionJsonCodec().SerializeRequest(client.Request));
            Assert.That(disclosed.step, Is.EqualTo(2));
            Assert.That(disclosed.remainingCalls, Is.EqualTo(1));
            Assert.That(disclosed.hasLocationDetails, Is.True);
            Assert.That(disclosed.locationDetails.locationId, Is.EqualTo("mina.work"));
            Assert.That(disclosed.locationDetails.isReachable, Is.True);
        }

        [Test]
        public void OversizedPersona_IsRejectedBeforeSendingTheContext()
        {
            var schedule = new NpcDailySchedule("mina", "home", "outside", "entry", "work", "rest", "work",
                360, 480, 720, 780, 1020, 1080);
            var world = new NpcAgentWorld(new[] { schedule });
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 1));
            var client = new CaptureClient();
            using var scheduler = new NpcDecisionScheduler(world,
                new[] { new NpcDefinition("mina", "Mina", new string('界', 12000), "Hello") }, client);
            scheduler.Tick(0);
            Assert.Throws<System.ArgumentException>(() => new ProxyNpcDecisionJsonCodec().SerializeRequest(client.Request));
        }

        private sealed class CaptureClient : INpcDecisionClient
        {
            public NpcDecisionRequest Request { get; private set; }

            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                Request = request;
                return Task.FromResult(new NpcDecisionReply(NpcDecisionKind.InspectLocation, "mina.work"));
            }
        }

        [System.Serializable]
        private sealed class RequestPayload
        {
            public int schemaVersion;
            public string npcId;
            public string persona;
            public string worldRunId;
            public long revision;
            public int step;
            public int remainingCalls;
            public double maxActivityDurationGameMinutes;
            public string[] knownLocationIds;
            public bool hasLocationDetails;
            public LocationPayload locationDetails;
        }

        [System.Serializable]
        private sealed class LocationPayload
        {
            public string locationId;
            public bool isReachable;
        }
    }
}

using System.Threading;
using System.Linq;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
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
        [Test]
        public void StandaloneStructuredCandidate_ParsesAFrameWithoutInventingAnIdentity()
        {
            var id = System.Guid.NewGuid();
            var reply = new ProxyNpcDecisionJsonCodec().ParseResponse("{\"schemaVersion\":4,\"operation\":\"say\",\"meetingId\":\""
                + id.ToString("N") + "\",\"speechIntent\":\"report_observation\",\"factId\":\"sora:region_name\",\"tone\":\"brief\"}");
            Assert.That(reply.MeetingId, Is.EqualTo(id));
            Assert.That(reply.SpeechFrame.FactId, Is.EqualTo("sora:region_name"));
            Assert.That(reply.Text, Is.Null);
        }

        [Test]
        public void StructuredSay_KeepsTheFrameSeparateFromDisplayedText()
        {
            var request = ConversationRequest(NpcSpeechMode.StructuredFacts);
            string json = "{\"schemaVersion\":2,\"operation\":\"say\",\"meetingId\":\""
                + request.Social.MeetingId.ToString("N")
                + "\",\"speechIntent\":\"express_wish\",\"factId\":\"@talk\",\"tone\":\"warm\"}";
            var codec = new ProxyNpcDecisionJsonCodec();
            Assert.That(codec.SerializeRequest(request), Does.Contain("\"mode\":\"structured_facts\""));
            var reply = codec.ParseResponse(json, request);
            Assert.That(reply.Text, Is.Null);
            Assert.That(reply.SpeechFrame.Intent, Is.EqualTo("express_wish"));
            Assert.That(reply.SpeechFrame.FactId, Is.EqualTo("@talk"));
            Assert.That(reply.SpeechFrame.Tone, Is.EqualTo("warm"));
        }

        private static NpcDecisionRequest ConversationRequest(NpcSpeechMode mode)
        {
            var world = new NpcAgentWorld(new[] { "ren", "sora" }.Select(id => new NpcDailySchedule(id,
                id + ".home", id + ".outside", id + ".entry", id + ".work", id + ".rest", id + ".afternoon",
                360, 480, 720, 810, 1020, 1080)));
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 1));
            var board = new NpcMeetingBoard(world, new[] { new NpcMeetingPlan("lunch", "sora", "ren", "pond",
                "sora.rest", "ren.rest", 720, 750, 780) }, (npc, location) => NpcMeetingPresence.Arrived);
            var meeting = board.Invite(world.GetState("sora"), "lunch").Value;
            Assert.That(board.Respond(world.GetState("ren"), meeting.Id, true).IsSuccess, Is.True);
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 750), 0, false, 1));
            board.Observe();
            var client = new CaptureClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { new NpcDefinition("sora", "Sora", "Cook", "Hello") },
                client, meetings: board, speechMode: mode);
            scheduler.Tick(0);
            return client.Request;
        }

        [TestCase("\"speechIntent\":\"express_wish\",\"factId\":\"@talk\",\"tone\":\"warm\",\"text\":\"I caught fish.\"", "candidate.expression_mode_mismatch", false)]
        [TestCase("\"speechIntent\":\"invent\",\"factId\":\"@talk\",\"tone\":\"warm\"", "candidate.speech_frame_invalid", true)]
        [TestCase("\"speechIntent\":\"express_wish\",\"factId\":\"@talk\",\"tone\":\"omniscient\"", "candidate.speech_frame_invalid", true)]
        [TestCase("\"speechIntent\":\"express_wish\",\"factId\":\"@talk\",\"tone\":\"warm\",\"quantity\":999", "candidate.speech_frame_invalid", true)]
        [TestCase("\"speechIntent\":\"express_wish\",\"factId\":\"@talk\",\"tone\":\"warm\",\"speakerId\":\"ren\"", "candidate.speech_frame_invalid", true)]
        [TestCase("\"speechIntent\":\"express_wish\",\"factId\":\"@talk\",\"tone\":\"warm\",\"tense\":\"past\"", "candidate.speech_frame_invalid", true)]
        public void StructuredSay_RejectsFreeAdditionsAndUnsupportedFrameValues(string fields, string code, bool correctable)
        {
            var request = ConversationRequest(NpcSpeechMode.StructuredFacts);
            string json = "{\"schemaVersion\":2,\"operation\":\"say\",\"meetingId\":\""
                + request.Social.MeetingId.ToString("N") + "\"," + fields + "}";
            var error = Assert.Throws<NpcCandidateException>(() => new ProxyNpcDecisionJsonCodec().ParseResponse(json, request));
            Assert.That(error.Code, Is.EqualTo(code));
            Assert.That(error.CanCorrect, Is.EqualTo(correctable));
        }

        [Test]
        public void FreeSay_RejectsAStructuredFrameEvenWhenFreeTextIsPresent()
        {
            var request = ConversationRequest(NpcSpeechMode.FreeText);
            string json = "{\"schemaVersion\":2,\"operation\":\"say\",\"meetingId\":\""
                + request.Social.MeetingId.ToString("N")
                + "\",\"text\":\"Hello.\",\"speechIntent\":\"express_wish\",\"factId\":\"@talk\",\"tone\":\"warm\"}";
            var error = Assert.Throws<NpcCandidateException>(() => new ProxyNpcDecisionJsonCodec().ParseResponse(json, request));
            Assert.That(error.Code, Is.EqualTo("candidate.expression_mode_mismatch"));
            Assert.That(error.CanCorrect, Is.False);
        }

        [Test]
        public void BoundedTradeObservation_WithEightObjectsAndLongParticipantLinesFitsTheRequestLimit()
        {
            var schedules = new[] { "ren", "sora" }.Select(id => new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry",
                id + ".work", id + ".rest", id + ".afternoon", 360, 480, 720, 810, 1020, 1080));
            var world = new NpcAgentWorld(schedules);
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 1));
            var store = new InMemoryEconomyStateStore(new[] {
                new CharacterEconomySnapshot("ren", new InventorySnapshot(new[] { new ItemStack("fish", 2) }), new WalletSnapshot(0)),
                new CharacterEconomySnapshot("sora", new InventorySnapshot(System.Array.Empty<ItemStack>()), new WalletSnapshot(50)) },
                System.Array.Empty<ShopEconomySnapshot>());
            var trading = new CharacterResourceTrading(store, new[] { new ItemDefinition("fish", "Fish", ItemCategory.Fish, 99) }, 2);
            var terms = new CharacterTradeTerms("ren", "sora", "fish", 1, 25);
            var board = new NpcMeetingBoard(world, new[] { new NpcMeetingPlan("supply", "sora", "ren", "pond", "sora.rest", "ren.rest",
                720, 750, 780, maxTurns: 6, resourceTerms: terms) }, (npc, location) => NpcMeetingPresence.Arrived, trading);
            var id = board.Invite(world.GetState("sora"), "supply").Value.Id;
            board.Respond(world.GetState("ren"), id, true);
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 750), 0, false, 1));
            board.Observe();
            Assert.That(board.Deliver(world.GetState("sora"), id).IsSuccess, Is.True);
            for (int i = 0; i < 4; i++)
                Assert.That(board.Speak(world.GetState(i % 2 == 0 ? "sora" : "ren"), id, new string('鱼', 240)).IsSuccess, Is.True);
            var scene = new NpcObservationScene(new[] { new NpcObservationRegion("pond", "池塘周边", -6, -6, 6, 6) },
                Enumerable.Range(0, 9).Select(i => new NpcObservationEntity("water-" + i, "池塘", 0, 0, "water", interactionId: "fishing", resourceItemId: "fish")));
            var client = new CaptureClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { new NpcDefinition("sora", "Sora", "A cook who enjoys local ingredients.", "Hello") }, client,
                meetings: board, observe: request => scene.Read(request.NpcId, request.Self.WorldRunId, world.TotalMinutes,
                    new[] { new NpcObservationBody("sora", 0, 0) }, request.Social, trading.Inspect(terms, "sora"), board.GetMemories("sora")));
            scheduler.Tick(0);
            var view = client.Request.Observation;
            Assert.That(view.NearbyEntityIds.Count, Is.EqualTo(8));
            Assert.That(view.NearbyComplete, Is.False);
            Assert.That(view.Facts.Count(f => f.Knowledge == "statement"), Is.EqualTo(4));
            Assert.That(view.Facts.Count, Is.LessThanOrEqualTo(64));
            Assert.That(view.Facts.Single(f => f.Predicate == "owned_quantity").Value, Is.EqualTo("1"));
            string json = new ProxyNpcDecisionJsonCodec().SerializeRequest(client.Request);
            Assert.That(System.Text.Encoding.UTF8.GetByteCount(json), Is.LessThan(ProxyNpcDecisionJsonCodec.MaximumRequestBytes));
            TestContext.WriteLine("BOUNDED_OBSERVATION_REQUEST=" + json);
        }

        [Test]
        public void ObservationEnvelope_SerializesBoundFactsWithoutChangingTheActionProtocol()
        {
            var world = new NpcAgentWorld(new[] { new NpcDailySchedule("mina", "home", "outside", "entry", "work", "rest", "work",
                360, 480, 720, 780, 1020, 1080) });
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 1));
            var scene = new NpcObservationScene(new[] { new NpcObservationRegion("pond", "Pond", -6, -6, 6, 6) },
                new[] { new NpcObservationEntity("water", "池塘", -1, 0, "landmark", interactionId: "fishing", resourceItemId: "fish") });
            var client = new CaptureClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { new NpcDefinition("mina", "Mina", "Shopkeeper", "Hello") }, client,
                observe: request => scene.Read(request.NpcId, request.Self.WorldRunId, 720, new[] { new NpcObservationBody("mina", -2, 0) }));
            scheduler.Tick(0);
            string json = new ProxyNpcDecisionJsonCodec().SerializeRequest(client.Request);
            var payload = JsonUtility.FromJson<ObservationRequestPayload>(json);
            Assert.That(json, Does.Contain("\"expression\":{\"schemaVersion\":1,\"mode\":\"free_text\"}"));
            Assert.That(payload.schemaVersion, Is.EqualTo(1));
            Assert.That(payload.hasObservation, Is.True);
            Assert.That(payload.observation.schemaVersion, Is.EqualTo(1));
            Assert.That(payload.observation.observerId, Is.EqualTo("mina"));
            Assert.That(payload.observation.worldRunId, Is.EqualTo(world.GetState("mina").WorldRunId.ToString("N")));
            Assert.That(payload.observation.x, Is.EqualTo(-2));
            Assert.That(payload.observation.observedAtGameTotalMinutes, Is.EqualTo(720));
            Assert.That(payload.observation.nearbyComplete, Is.True);
            Assert.That(payload.observation.facts.Single(f => f.predicate == "quantity").valueType, Is.EqualTo("unknown"));
            Assert.That(payload.observation.facts.Single(f => f.predicate == "quantity").value, Is.Empty);
            Assert.That(json, Does.Contain("池塘"));
            Assert.That(System.Text.Encoding.UTF8.GetByteCount(json), Is.LessThan(ProxyNpcDecisionJsonCodec.MaximumRequestBytes));
            TestContext.WriteLine("OBSERVATION_REQUEST=" + json);
        }

        [System.Serializable]
        private sealed class ObservationRequestPayload
        {
            public int schemaVersion;
            public bool hasObservation;
            public ObservationPayload observation;
        }

        [System.Serializable]
        private sealed class ObservationPayload
        {
            public int schemaVersion;
            public string observerId, worldRunId;
            public double x, observedAtGameTotalMinutes;
            public bool nearbyComplete;
            public ObservationFactPayload[] facts;
        }

        [System.Serializable]
        private sealed class ObservationFactPayload
        {
            public string predicate, valueType, value;
        }

        [TestCase("{\"schemaVersion\":4,\"operation\":\"invite\"}")]
        [TestCase("{\"schemaVersion\":4,\"operation\":\"invite\",\"social\":{\"planId\":\"lunch\"}}")]
        public void InvitationWithoutTopLevelPlanId_ReportsACorrectableFieldError(string json)
        {
            var error = Assert.Throws<NpcCandidateException>(() => new ProxyNpcDecisionJsonCodec().ParseResponse(json));
            Assert.That(error.Code, Is.EqualTo("candidate.plan_id_required"));
            Assert.That(error.CanCorrect, Is.True);
        }

        [TestCase("{\"schemaVersion\":4,\"operation\":\"accept_invite\"}", "candidate.meeting_id_required")]
        [TestCase("{\"schemaVersion\":4,\"operation\":\"accept_invite\",\"meetingId\":\"bad\"}", "candidate.meeting_id_invalid")]
        [TestCase("{\"schemaVersion\":4,\"operation\":\"say\",\"meetingId\":\"11111111111111111111111111111111\"}", "candidate.text_invalid")]
        [TestCase("{\"schemaVersion\":1,\"operation\":\"inspect_location\"}", "candidate.location_id_required")]
        [TestCase("{\"schemaVersion\":1,\"operation\":\"visit\",\"locationId\":\"work\",\"activity\":\"home\",\"durationGameMinutes\":20}", "candidate.activity_invalid")]
        [TestCase("{\"schemaVersion\":1,\"operation\":\"visit\",\"locationId\":\"work\",\"activity\":\"working\"}", "candidate.duration_invalid")]
        [TestCase("{\"schemaVersion\":1,\"operation\":\"visit\",\"locationId\":\"work\",\"activity\":\"working\",\"durationGameMinutes\":1441}", "candidate.duration_invalid")]
        public void RequiredCandidateFields_ReportTheirOwnFailure(string json, string code)
        {
            var error = Assert.Throws<NpcCandidateException>(() => new ProxyNpcDecisionJsonCodec().ParseResponse(json));
            Assert.That(error.Code, Is.EqualTo(code));
            Assert.That(error.CanCorrect, Is.True);
        }

        [TestCase("{\"schemaVersion\":true,\"operation\":\"wait\"}")]
        [TestCase("{\"schemaVersion\":1.0,\"operation\":\"wait\"}")]
        [TestCase("{\"schemaVersion\":\"1\",\"operation\":\"wait\"}")]
        [TestCase("{\"schemaVersion\":4,\"operation\":\"invite\",\"planId\":5}")]
        [TestCase("{\"schemaVersion\":1,\"operation\":\"visit\",\"locationId\":\"work\",\"activity\":\"working\",\"durationGameMinutes\":\"20\"}")]
        public void CandidateFieldTypes_AreNotCoercedIntoValidValues(string json)
            => Assert.Catch<System.FormatException>(() => new ProxyNpcDecisionJsonCodec().ParseResponse(json));

        [TestCase("{}")]
        [TestCase("{\"schemaVersion\":true}")]
        [TestCase("{\"schemaVersion\":1.0}")]
        [TestCase("{\"schemaVersion\":\"1\"}")]
        public void InvalidSchemaField_PreservesItsNoncorrectableDiagnostic(string json)
        {
            var error = Assert.Throws<NpcCandidateException>(() => new ProxyNpcDecisionJsonCodec().ParseResponse(json));
            Assert.That(error.Code, Is.EqualTo("candidate.schema_mismatch"));
            Assert.That(error.CanCorrect, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SocialIdentifiers_MustBindTheCandidateToTheRequestedPlanOrMeeting(bool invitation)
        {
            var request = SocialRequest(invitation);
            string json = invitation ? "{\"schemaVersion\":2,\"operation\":\"accept_invite\",\"meetingId\":\"11111111111111111111111111111111\"}"
                : "{\"schemaVersion\":2,\"operation\":\"invite\",\"planId\":\"another-plan\"}";
            var error = Assert.Throws<NpcCandidateException>(() => new ProxyNpcDecisionJsonCodec().ParseResponse(json, request));
            Assert.That(error.Code, Is.EqualTo(invitation ? "candidate.meeting_id_mismatch" : "candidate.plan_id_mismatch"));
            Assert.That(error.CanCorrect, Is.False);
        }

        [TestCase("N")]
        [TestCase("D")]
        public void MeetingIdentifier_UsesGuidValueWhileKeepingRequestBinding(string format)
        {
            var request = SocialRequest(true);
            var reply = new ProxyNpcDecisionJsonCodec().ParseResponse("{\"schemaVersion\":2,\"operation\":\"accept_invite\",\"meetingId\":\""
                + request.Social.MeetingId.ToString(format).ToUpperInvariant() + "\"}", request);
            Assert.That(reply.MeetingId, Is.EqualTo(request.Social.MeetingId));
        }

        [TestCase(120, true)]
        [TestCase(121, false)]
        public void SpeechLength_UsesTheSameUtf16BoundaryAsTheMeetingBoard(int pairs, bool accepted)
        {
            string text = string.Concat(Enumerable.Repeat("\U0001F41F", pairs));
            string json = "{\"schemaVersion\":4,\"operation\":\"say\",\"meetingId\":\"11111111111111111111111111111111\",\"text\":\"" + text + "\"}";
            if (accepted) Assert.That(new ProxyNpcDecisionJsonCodec().ParseResponse(json).Text, Is.EqualTo(text));
            else Assert.That(Assert.Throws<NpcCandidateException>(() => new ProxyNpcDecisionJsonCodec().ParseResponse(json)).Code,
                Is.EqualTo("candidate.text_invalid"));
        }

        [TestCase("{\"schemaVersion\":1,\"operation\":\"wait\",\"operation\":\"visit\"}")]
        [TestCase("{\"schemaVersion\":1,\"operation\":\"wait\"} {\"schemaVersion\":1,\"operation\":\"wait\"}")]
        public void AmbiguousJson_IsRejectedWithoutProducingACandidate(string json)
            => Assert.Throws<System.FormatException>(() => new ProxyNpcDecisionJsonCodec().ParseResponse(json));

        private static NpcDecisionRequest SocialRequest(bool invitation)
        {
            NpcDailySchedule Schedule(string id) => new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry",
                id + ".work", id + ".rest", id + ".afternoon", 360, 480, 720, 810, 1020, 1080);
            var world = new NpcAgentWorld(new[] { Schedule("ren"), Schedule("sora") });
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 1));
            var board = new NpcMeetingBoard(world, new[] { new NpcMeetingPlan("lunch", "sora", "ren", "pond", "sora.rest", "ren.rest", 720, 750, 780) });
            if (invitation) Assert.That(board.Invite(world.GetState("sora"), "lunch").IsSuccess, Is.True);
            string npc = invitation ? "ren" : "sora";
            var client = new CaptureClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { new NpcDefinition(npc, npc, "Resident", "Hello") }, client, meetings: board);
            scheduler.Tick(0);
            return client.Requests.Single();
        }

        [TestCase(3, "deliver", NpcDecisionKind.Deliver)]
        [TestCase(3, "cancel_exchange", NpcDecisionKind.CancelExchange)]
        [TestCase(4, "deliver", NpcDecisionKind.Deliver)]
        [TestCase(4, "cancel_exchange", NpcDecisionKind.CancelExchange)]
        public void ResourceResponse_UsesOnlyTheMeetingIdentifier(int version, string operation, NpcDecisionKind kind)
        {
            var id = System.Guid.NewGuid();
            var reply = new ProxyNpcDecisionJsonCodec().ParseResponse("{\"schemaVersion\":" + version + ",\"operation\":\"" + operation
                + "\",\"meetingId\":\"" + id.ToString("N") + "\",\"quantity\":999,\"buyerId\":\"player\",\"totalPrice\":0}");
            Assert.That(reply.Kind, Is.EqualTo(kind));
            Assert.That(reply.MeetingId, Is.EqualTo(id));
        }

        [TestCase(24, false, 1)]
        [TestCase(25, true, 0)]
        [TestCase(26, true, 0)]
        public void ResourceRequest_DisclosesFixedTermsAndOnlyTheCurrentResidentsResources(int balance, bool canPay, int missing)
        {
            NpcDailySchedule Schedule(string id) => new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry",
                id + ".work", id + ".rest", id + ".afternoon", 360, 480, 720, 810, 1020, 1080);
            var world = new NpcAgentWorld(new[] { Schedule("ren"), Schedule("sora") });
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 1));
            var terms = new CharacterTradeTerms("ren", "sora", "fish", 1, 25);
            var store = new InMemoryEconomyStateStore(new[] {
                new CharacterEconomySnapshot("ren", new InventorySnapshot(System.Array.Empty<ItemStack>()), new WalletSnapshot(98765)),
                new CharacterEconomySnapshot("sora", new InventorySnapshot(System.Array.Empty<ItemStack>()), new WalletSnapshot(balance)) }, System.Array.Empty<ShopEconomySnapshot>());
            var trading = new CharacterResourceTrading(store, new[] { new ItemDefinition("fish", "Fish", ItemCategory.Fish, 99) }, 2);
            var board = new NpcMeetingBoard(world, new[] { new NpcMeetingPlan("fish", "sora", "ren", "pond", "sora.rest", "ren.rest", 720, 750, 780, resourceTerms: terms) }, resources: trading);
            var client = new CaptureClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { new NpcDefinition("sora", "Sora", "Cook", "Hello") }, client, meetings: board);
            scheduler.Tick(0);
            string json = new ProxyNpcDecisionJsonCodec().SerializeRequest(client.Requests.Single());
            Assert.That(json, Does.Contain("\"schemaVersion\":4"));
            Assert.That(json, Does.Contain("\"ownedQuantity\":0"));
            Assert.That(json, Does.Contain("\"balance\":" + balance));
            Assert.That(json, Does.Contain("\"totalPrice\":25"));
            Assert.That(json, Does.Not.Contain("98765"));
            Assert.That(json, Does.Contain("\"hasSelfAssessment\":true"));
            Assert.That(json, Does.Contain("\"role\":\"buyer\""));
            Assert.That(json, Does.Contain("\"canMeetKnownTerms\":" + canPay.ToString().ToLowerInvariant()));
            Assert.That(json, Does.Contain("\"missingCoins\":" + missing));
            if (!canPay) Assert.That(json, Does.Contain("\"reasonCode\":\"wallet.insufficient_funds\""));
        }

        [Test]
        public void SocialContext_DisclosesPartnerAndTranscriptOnlyForTheCurrentPhase()
        {
            NpcDailySchedule Schedule(string id) => new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry",
                id + ".work", id + ".rest", id + ".afternoon", 360, 480, 720, 810, 1020, 1080);
            var world = new NpcAgentWorld(new[] { Schedule("ren"), Schedule("sora") });
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 1));
            var board = new NpcMeetingBoard(world, new[] { new NpcMeetingPlan("lunch", "ren", "sora", "pond", "ren.rest", "sora.rest", 720, 750, 780) });
            var client = new CaptureClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] {
                new NpcDefinition("ren", "Ren", "Ren private persona", "Hello"),
                new NpcDefinition("sora", "Sora", "Sora private persona", "Hello") }, client, meetings: board);
            scheduler.Tick(0);
            // Both are resting; the last ordinary request may belong to Sora.
            var opportunity = client.Requests.Find(item => item.Social != null);
            string json = new ProxyNpcDecisionJsonCodec().SerializeRequest(opportunity);
            Assert.That(json, Does.Contain("\"schemaVersion\":2"));
            Assert.That(json, Does.Contain("\"allowedOperations\":[\"invite\",\"wait\"]"));
            Assert.That(json, Does.Contain("\"partnerId\":\"sora\""));
            Assert.That(json, Does.Contain("\"transcript\":[]"));
            Assert.That(json, Does.Not.Contain("Sora private persona"));
            Assert.That(json, Does.Not.Contain("sora.work"));
            Assert.That(json, Does.Contain("\"knownLocationIds\":[]"));
        }
        [TestCase("invite", NpcDecisionKind.Invite)]
        [TestCase("accept_invite", NpcDecisionKind.AcceptInvitation)]
        [TestCase("decline_invite", NpcDecisionKind.DeclineInvitation)]
        [TestCase("say", NpcDecisionKind.Speak)]
        [TestCase("end_conversation", NpcDecisionKind.EndConversation)]
        public void SocialResponse_ParsesOnlyTheBoundCandidate(string operation, NpcDecisionKind kind)
        {
            var id = System.Guid.NewGuid();
            var reply = new ProxyNpcDecisionJsonCodec().ParseResponse(
                "{\"schemaVersion\":2,\"operation\":\"" + operation + "\",\"planId\":\"lunch\",\"meetingId\":\"" + id.ToString("N") + "\",\"text\":\"The pond is quiet.\",\"npcId\":\"someone-else\"}");
            Assert.That(reply.Kind, Is.EqualTo(kind));
            if (kind == NpcDecisionKind.Invite) Assert.That(reply.PlanId, Is.EqualTo("lunch"));
            else Assert.That(reply.MeetingId, Is.EqualTo(id));
            if (kind == NpcDecisionKind.Speak) Assert.That(reply.Text, Is.EqualTo("The pond is quiet."));
        }
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
        [TestCase("{\"schemaVersion\":5,\"operation\":\"wait\"}")]
        [TestCase("{\"schemaVersion\":1,\"operation\":\"give_coins\"}")]
        [TestCase("{\"schemaVersion\":1,\"operation\":\"inspect_location\"}")]
        [TestCase("{\"schemaVersion\":1,\"operation\":\"visit\",\"locationId\":\"mina.work\",\"activity\":\"home\"}")]
        public void InvalidResponse_IsRejected(string json)
            => Assert.Catch<System.FormatException>(() => new ProxyNpcDecisionJsonCodec().ParseResponse(json));

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
        public void VisitResponse_RejectsDurationBeyondTheDisclosedWindow()
        {
            var world = new NpcAgentWorld(new[] { new NpcDailySchedule("mina", "home", "outside", "entry", "work",
                "rest", "work", 360, 480, 720, 780, 1020, 1080) });
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 735), 0, false, 1));
            var client = new CaptureClient();
            using var scheduler = new NpcDecisionScheduler(world, new[] { new NpcDefinition("mina", "Mina", "Farmer", "Hello") }, client);
            scheduler.Tick(0);
            var codec = new ProxyNpcDecisionJsonCodec();
            const string candidate = "{\"schemaVersion\":1,\"operation\":\"visit\",\"locationId\":\"work\",\"activity\":\"working\",\"durationGameMinutes\":";
            Assert.That(codec.ParseResponse(candidate + "45}", client.Request).DurationGameMinutes, Is.EqualTo(45));
            var error = Assert.Throws<NpcCandidateException>(() => codec.ParseResponse(candidate + "720}", client.Request));
            Assert.That(error.Code, Is.EqualTo("candidate.duration_invalid"));
            Assert.That(error.CanCorrect, Is.True);
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
            Assert.That(payload.maxActivityDurationGameMinutes, Is.EqualTo(60));
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
            public readonly System.Collections.Generic.List<NpcDecisionRequest> Requests = new System.Collections.Generic.List<NpcDecisionRequest>();
            public NpcDecisionRequest Request { get; private set; }

            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
            {
                Request = request;
                Requests.Add(request);
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

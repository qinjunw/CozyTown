using System;
using System.Linq;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.NpcAgents
{
    public sealed class NpcFactSpeechTests
    {
        private NpcAgentWorld _world;
        private IEconomyStateStore _store;
        private CharacterResourceTrading _trading;
        private NpcMeetingBoard _board;
        private NpcObservationScene _scene;
        private CharacterTradeTerms _terms;

        [SetUp]
        public void SetUp()
        {
            _world = new NpcAgentWorld(new[] { Schedule("sora"), Schedule("ren") });
            _world.Observe(Time(720));
            _store = new InMemoryEconomyStateStore(new[] { Character("sora", 0, 50), Character("ren", 2, 0) },
                Array.Empty<ShopEconomySnapshot>());
            _trading = new CharacterResourceTrading(_store,
                new[] { new ItemDefinition("fish.carp", "Carp", ItemCategory.Fish, 99) }, 2);
            _terms = new CharacterTradeTerms("ren", "sora", "fish.carp", 1, 25);
            _board = new NpcMeetingBoard(_world, new[] { new NpcMeetingPlan("supply", "sora", "ren", "pond", "sora.rest", "ren.rest",
                720, 750, 780, maxTurns: 6, resourceTerms: _terms) }, (npc, location) => NpcMeetingPresence.Arrived, _trading);
            _board.Observe();
            _scene = new NpcObservationScene(new[] { new NpcObservationRegion("pond", "Pond walk", -10, -10, 10, 10) },
                new[] { new NpcObservationEntity("water", "Pond", 1, 0, "water", interactionId: "fishing", resourceItemId: "fish.carp"),
                    new NpcObservationEntity("lamp", "Road lamp", 2, 0, "decoration") });
        }

        [Test]
        public void CurrentOwnedQuantity_StatesTheObservedCountAndSpecificItem()
        {
            var observation = Observe("sora");
            var fact = observation.Facts.Single(item => item.Predicate == "owned_quantity");

            bool rendered = NpcFactSpeech.TryRender(observation,
                new NpcSpeechFrame("report_observation", fact.FactId, "neutral"), out var text, out var errorCode);

            Assert.That(rendered, Is.True);
            Assert.That(errorCode, Is.Null);
            Assert.That(text, Is.EqualTo("I currently hold 0 carp."));
        }

        [Test]
        public void UnmodelledPondQuantity_StatesUnknownWithoutInventingAZeroOrCount()
        {
            var observation = Observe("sora");
            var fact = observation.Facts.Single(item => item.EntityId == "water" && item.Predicate == "quantity");

            bool rendered = NpcFactSpeech.TryRender(observation,
                new NpcSpeechFrame("acknowledge_unknown", fact.FactId, "neutral"), out var text, out var errorCode);

            Assert.That(rendered, Is.True);
            Assert.That(errorCode, Is.Null);
            Assert.That(text, Is.EqualTo("I do not know how many carp are in Pond."));
        }

        [TestCase("delivery_result", "At game minute 750, the resource transfer was recorded as complete.")]
        [TestCase("delivered_quantity", "At game minute 750, the recorded transfer moved 1 carp.")]
        [TestCase("delivered_price", "At game minute 750, the recorded transfer paid 25 coins.")]
        public void CompletedTransferReceipt_ReportsThePastEventAfterCurrentHoldingsChange(string predicate, string expected)
        {
            Deliver();
            Assert.That(_store.CommitCharacter(Character("sora", 0, 5)).IsSuccess, Is.True);
            _world.Observe(Time(751));
            var observation = Observe("sora");
            var fact = observation.Facts.Single(item => item.Predicate == predicate);

            bool rendered = NpcFactSpeech.TryRender(observation,
                new NpcSpeechFrame("report_receipt", fact.FactId, "neutral"), out var text, out var errorCode);

            Assert.That(rendered, Is.True);
            Assert.That(errorCode, Is.Null);
            Assert.That(text, Is.EqualTo(expected));
            Assert.That(observation.Facts.Single(item => item.Predicate == "owned_quantity").Value, Is.EqualTo("0"));
        }

        [TestCase("ren", 751, 0, null)]
        [TestCase("sora", 751, 0, null)]
        [TestCase("ren", 749, 0, "speech.fact_invalid")]
        [TestCase("ren", 751, 180, "speech.text_too_long")]
        public void HeardStatement_QuotesItsSpeakerAndTimeOrRejectsAnInvalidHistoricalExpression(
            string speaker, int observedAt, int statementLength, string expectedError)
        {
            var meetingId = Deliver();
            string statement = statementLength == 0 ? "I caught five carp today." : new string('x', statementLength);
            Assert.That(_board.Speak(_world.GetState("sora"), meetingId, speaker == "sora" ? statement : "I have the recorded receipt.").IsSuccess, Is.True);
            Assert.That(_board.Speak(_world.GetState("ren"), meetingId, speaker == "ren" ? statement : "I heard you.").IsSuccess, Is.True);
            var observation = Observe("sora", observedAt);
            var fact = observation.Facts.Single(item => item.Predicate == "said" && item.SpeakerId == speaker);

            bool rendered = NpcFactSpeech.TryRender(observation,
                new NpcSpeechFrame("recall_statement", fact.FactId, "neutral"), out var text, out var errorCode);

            Assert.That(rendered, Is.EqualTo(expectedError == null));
            Assert.That(errorCode, Is.EqualTo(expectedError));
            Assert.That(text, Is.EqualTo(expectedError == null
                ? "At game minute 750, " + speaker + " said: \"I caught five carp today.\"" : null));
        }

        [TestCase("complete", "report_observation", "My complete list contains 3 registered nearby entities in this region within 3 units.", null)]
        [TestCase("empty", "report_observation", "My complete list contains no registered nearby entities in this region within 3 units.", null)]
        [TestCase("partial", "report_observation", "My list of registered nearby entities is incomplete; it cannot show that other objects are absent.", null)]
        [TestCase("unmapped", "report_observation", "My current region is unmapped, so I cannot provide a complete nearby list.", null)]
        [TestCase("partial", "acknowledge_unknown", "My list of registered nearby entities is incomplete; it cannot show that other objects are absent.", null)]
        [TestCase("unmapped", "acknowledge_unknown", "My current region is unmapped, so I cannot provide a complete nearby list.", null)]
        [TestCase("complete", "acknowledge_unknown", null, "speech.information_known")]
        [TestCase("empty", "acknowledge_unknown", null, "speech.information_known")]
        public void NearbyCoverage_SeparatesCompleteEmptyIncompleteAndUnmappedScopes(string context, string intent, string expected, string expectedError)
        {
            var observation = Observe("sora");
            if (context == "empty")
                observation = new NpcObservationScene(new[] { new NpcObservationRegion("pond", "Pond walk", -10, -10, 10, 10) },
                    Array.Empty<NpcObservationEntity>()).Read("sora", _world.GetState("sora").WorldRunId, 720,
                        new[] { new NpcObservationBody("sora", 0, 0) });
            else if (context == "partial")
                observation = new NpcObservationScene(new[] { new NpcObservationRegion("pond", "Pond walk", -10, -10, 10, 10) },
                    new[] { new NpcObservationEntity("a", "Lamp A", 0, 0, "decoration"),
                        new NpcObservationEntity("b", "Lamp B", 0, 0, "decoration") }, maxNearby: 1)
                    .Read("sora", _world.GetState("sora").WorldRunId, 720, new[] { new NpcObservationBody("sora", 0, 0) });
            else if (context == "unmapped")
                observation = _scene.Read("sora", _world.GetState("sora").WorldRunId, 720,
                    new[] { new NpcObservationBody("sora", 30, 0) });

            bool rendered = NpcFactSpeech.TryRender(observation, new NpcSpeechFrame(intent, "@nearby_coverage", "neutral"), out var text, out var errorCode);

            Assert.That(rendered, Is.EqualTo(expectedError == null));
            Assert.That(errorCode, Is.EqualTo(expectedError));
            Assert.That(text, Is.EqualTo(expected));
        }

        [TestCase("acknowledge_unknown", "@partner_assets", "neutral", true, "I do not know your current inventory or coin balance.", null)]
        [TestCase("ask_about", "@partner_assets", "neutral", true, "What resources do you currently have?", null)]
        [TestCase("acknowledge_unknown", "@partner_assets", "neutral", false, null, "speech.fact_forbidden")]
        [TestCase("express_wish", "@talk", "neutral", true, "I would like to talk with you.", null)]
        [TestCase("express_wish", "@talk", "warm", true, "I'd enjoy a chat with you.", null)]
        [TestCase("express_wish", "@talk", "brief", true, "I'd like to talk.", null)]
        [TestCase("express_wish", "@talk", "neutral", false, null, "speech.fact_forbidden")]
        [TestCase("express_wish", "@learn_cooking", "neutral", false, "I would like to learn about cooking.", null)]
        [TestCase("express_wish", "@learn_cooking", "warm", false, "I'd enjoy learning about cooking.", null)]
        [TestCase("express_wish", "@learn_cooking", "brief", false, "I'd like to learn cooking.", null)]
        [TestCase("report_receipt", "@talk", "neutral", true, null, "speech.intent_mismatch")]
        [TestCase("express_wish", "@unknown_wish", "neutral", true, null, "speech.fact_unknown")]
        public void ReservedConversationReferences_AllowOnlyUnknownPartnerAssetsAndExplicitWishes(
            string intent, string factId, string tone, bool hasListener, string expected, string expectedError)
        {
            var observation = hasListener ? Observe("sora") : _scene.Read("sora", _world.GetState("sora").WorldRunId, 720,
                new[] { new NpcObservationBody("sora", 0, 0) }, ownResources: _trading.Inspect(_terms, "sora"));

            bool rendered = NpcFactSpeech.TryRender(observation, new NpcSpeechFrame(intent, factId, tone), out var text, out var errorCode);

            Assert.That(rendered, Is.EqualTo(expectedError == null));
            Assert.That(errorCode, Is.EqualTo(expectedError));
            Assert.That(text, Is.EqualTo(expected));
        }

        [TestCase("sora:region", "I am in Pond walk.")]
        [TestCase("sora:region_name", "I am in Pond walk.")]
        [TestCase("lamp:name", "Road lamp is nearby.")]
        [TestCase("lamp:kind", "Road lamp is registered as decoration.")]
        [TestCase("lamp:interaction", "Road lamp has no supported interaction in this scene.")]
        [TestCase("water:name", "Pond is nearby.")]
        [TestCase("water:kind", "Pond is registered as water.")]
        [TestCase("water:interaction", "The registered interaction at Pond is fishing.")]
        [TestCase("ren:present", "ren is nearby.")]
        [TestCase("ren:kind", "ren is registered as a resident.")]
        [TestCase("sora:owned_quantity", "I currently hold 0 carp.")]
        [TestCase("plan:supply:terms_quantity", "The exchange terms specify 1 carp.")]
        [TestCase("plan:supply:terms_price", "The exchange terms specify 25 coins.")]
        [TestCase("plan:supply:seller", "The exchange terms name ren as seller.")]
        [TestCase("plan:supply:buyer", "The exchange terms name sora as buyer.")]
        [TestCase("plan:supply:decision_stage", "There is an opportunity to arrange a meeting.")]
        public void CurrentObservation_ReportsOnlyTheSelectedSupportedPredicate(string factId, string expected)
        {
            bool rendered = NpcFactSpeech.TryRender(Observe("sora"),
                new NpcSpeechFrame("report_observation", factId, "neutral"), out var text, out var errorCode);

            Assert.That(rendered, Is.True);
            Assert.That(errorCode, Is.Null);
            Assert.That(text, Is.EqualTo(expected));
        }

        [TestCase("water:name", "What can you tell me about Pond?")]
        [TestCase("water:quantity", "Do you know how many carp are in Pond?")]
        [TestCase("ren:present", "What can you tell me about ren?")]
        public void AskingAboutAnObservedSubject_UsesItsAuthoritativeIdentityWithoutClaimingAnUnknownAmount(string factId, string expected)
        {
            bool rendered = NpcFactSpeech.TryRender(Observe("sora"),
                new NpcSpeechFrame("ask_about", factId, "neutral"), out var text, out var errorCode);

            Assert.That(rendered, Is.True);
            Assert.That(errorCode, Is.Null);
            Assert.That(text, Is.EqualTo(expected));
        }

        [TestCase("warm", "I'd like to share this: I currently hold 0 carp.")]
        [TestCase("brief", "I currently hold 0 carp.")]
        public void ReportingTone_DoesNotChangeTheSelectedFact(string tone, string expected)
        {
            bool rendered = NpcFactSpeech.TryRender(Observe("sora"),
                new NpcSpeechFrame("report_observation", "sora:owned_quantity", tone), out var text, out var errorCode);

            Assert.That(rendered, Is.True);
            Assert.That(errorCode, Is.Null);
            Assert.That(text, Is.EqualTo(expected));
        }

        [TestCase("express_wish", "sora:owned_quantity", "speech.intent_mismatch")]
        [TestCase("ask_about", "sora:owned_quantity", "speech.intent_mismatch")]
        [TestCase("report_observation", "water:quantity", "speech.intent_mismatch")]
        [TestCase("acknowledge_unknown", "sora:owned_quantity", "speech.information_known")]
        [TestCase("report_observation", "sora:balance", "speech.fact_forbidden")]
        [TestCase("report_receipt", "sora:owned_quantity", "speech.intent_mismatch")]
        [TestCase("report_observation", "ren:owned_quantity", "speech.fact_unknown")]
        [TestCase("report_observation", "unseen:name", "speech.fact_unknown")]
        public void FactSelection_RejectsUnsupportedMeaningPrivateValuesAndMissingEvidence(string intent, string factId, string expectedError)
        {
            bool rendered = NpcFactSpeech.TryRender(Observe("sora"),
                new NpcSpeechFrame(intent, factId, "neutral"), out var text, out var errorCode);

            Assert.That(rendered, Is.False);
            Assert.That(errorCode, Is.EqualTo(expectedError));
            Assert.That(text, Is.Null);
        }

        [TestCase(false, "There is an opportunity to arrange a meeting.")]
        [TestCase(true, "A meeting invitation is awaiting a reply.")]
        public void OrdinaryMeetingStage_DoesNotInventAResourceExchange(bool invited, string expected)
        {
            var board = new NpcMeetingBoard(_world, new[] { new NpcMeetingPlan("chat", "sora", "ren", "pond", "sora.rest", "ren.rest",
                720, 750, 780) }, (npc, location) => NpcMeetingPresence.Arrived);
            board.Observe();
            if (invited) Assert.That(board.Invite(_world.GetState("sora"), "chat").IsSuccess, Is.True);
            string observer = invited ? "ren" : "sora";
            var observation = _scene.Read(observer, _world.GetState(observer).WorldRunId, 720,
                new[] { new NpcObservationBody("sora", 0, 0), new NpcObservationBody("ren", 2, 0) }, social: board.GetContext(observer));
            var fact = observation.Facts.Single(item => item.Predicate == "decision_stage");

            bool rendered = NpcFactSpeech.TryRender(observation,
                new NpcSpeechFrame("report_observation", fact.FactId, "neutral"), out var text, out var errorCode);

            Assert.That(rendered, Is.True);
            Assert.That(errorCode, Is.Null);
            Assert.That(text, Is.EqualTo(expected));
            Assert.That(observation.Facts.Any(item => item.Source == "meeting_terms"), Is.False);
        }

        [Test]
        public void DuplicateFactReferences_AreRejectedWithoutChoosingOneMeaning()
        {
            var scene = new NpcObservationScene(new[] { new NpcObservationRegion("pond", "Pond walk", -10, -10, 10, 10) },
                new[] { new NpcObservationEntity("ren", "Ren statue", 1, 0, "decoration") });
            var observation = scene.Read("sora", _world.GetState("sora").WorldRunId, 720,
                new[] { new NpcObservationBody("sora", 0, 0), new NpcObservationBody("ren", 2, 0) });

            bool rendered = NpcFactSpeech.TryRender(observation,
                new NpcSpeechFrame("report_observation", "ren:kind", "neutral"), out var text, out var errorCode);

            Assert.That(rendered, Is.False);
            Assert.That(errorCode, Is.EqualTo("speech.fact_ambiguous"));
            Assert.That(text, Is.Null);
        }

        [TestCase("coins")]
        [TestCase("fish.trout")]
        public void UnsupportedQuantityUnits_AreRejectedInsteadOfBeingRelabelledAsCarp(string unit)
        {
            var scene = new NpcObservationScene(new[] { new NpcObservationRegion("pond", "Pond walk", -10, -10, 10, 10) },
                new[] { new NpcObservationEntity("water", "Pond", 1, 0, "water", resourceItemId: unit) });
            var observation = scene.Read("sora", _world.GetState("sora").WorldRunId, 720, new[] { new NpcObservationBody("sora", 0, 0) });

            bool rendered = NpcFactSpeech.TryRender(observation,
                new NpcSpeechFrame("acknowledge_unknown", "water:quantity", "neutral"), out var text, out var errorCode);

            Assert.That(rendered, Is.False);
            Assert.That(errorCode, Is.EqualTo("speech.fact_invalid"));
            Assert.That(text, Is.Null);
        }

        [Test]
        public void AnUnobservedCatalogObject_CannotBeReportedFromItsIdentifier()
        {
            var observation = _scene.Read("sora", _world.GetState("sora").WorldRunId, 720, new[] { new NpcObservationBody("sora", 30, 0) });

            bool rendered = NpcFactSpeech.TryRender(observation,
                new NpcSpeechFrame("report_observation", "lamp:interaction", "neutral"), out var text, out var errorCode);

            Assert.That(rendered, Is.False);
            Assert.That(errorCode, Is.EqualTo("speech.fact_unknown"));
            Assert.That(text, Is.Null);
        }

        private Guid Deliver()
        {
            var invited = _board.Invite(_world.GetState("sora"), "supply");
            Assert.That(invited.IsSuccess, Is.True);
            Assert.That(_board.Respond(_world.GetState("ren"), invited.Value.Id, true).IsSuccess, Is.True);
            _world.Observe(Time(750));
            _board.Observe();
            Assert.That(_board.Deliver(_world.GetState("sora"), invited.Value.Id).IsSuccess, Is.True);
            return invited.Value.Id;
        }

        private NpcLocalObservation Observe(string npcId, double? observedAt = null)
            => _scene.Read(npcId, _world.GetState(npcId).WorldRunId, observedAt ?? _world.TotalMinutes,
                new[] { new NpcObservationBody("sora", 0, 0), new NpcObservationBody("ren", 2, 0) },
                social: _board.GetContext(npcId), ownResources: _trading.Inspect(_terms, npcId), memories: _board.GetMemories(npcId));

        private static NpcDailySchedule Schedule(string id) => new NpcDailySchedule(id, id + ".home", id + ".outside", id + ".entry",
            id + ".work", id + ".rest", id + ".afternoon", 360, 480, 720, 810, 1020, 1080);
        private static WorldTimeProgress Time(int minute) => new WorldTimeProgress(new GameClockSnapshot(1, minute), 0, false, 1);
        private static CharacterEconomySnapshot Character(string id, int fish, int coins) => new CharacterEconomySnapshot(id,
            new InventorySnapshot(fish == 0 ? Array.Empty<ItemStack>() : new[] { new ItemStack("fish.carp", fish) }), new WalletSnapshot(coins));
    }
}

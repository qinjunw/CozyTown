using System;
using System.Globalization;
using System.Linq;

namespace CozyTown.Runtime.NpcAgents
{
    public static class NpcFactSpeech
    {
        public static bool TryRender(NpcLocalObservation observation, NpcSpeechFrame frame, out string text, out string errorCode)
        {
            text = null;
            errorCode = "speech.frame_invalid";
            if (observation == null) { errorCode = "speech.observation_invalid"; return false; }
            if (frame == null || string.IsNullOrWhiteSpace(frame.FactId) || string.IsNullOrWhiteSpace(frame.Intent)
                || string.IsNullOrWhiteSpace(frame.Tone)) return false;
            if (frame.Intent != "report_observation" && frame.Intent != "acknowledge_unknown" && frame.Intent != "report_receipt"
                && frame.Intent != "recall_statement" && frame.Intent != "ask_about" && frame.Intent != "express_wish")
            { errorCode = "speech.intent_unsupported"; return false; }
            if (frame.Tone != "neutral" && frame.Tone != "warm" && frame.Tone != "brief")
            { errorCode = "speech.tone_unsupported"; return false; }
            if (frame.FactId.StartsWith("@", StringComparison.Ordinal))
            {
                if (!TryReserved(observation, frame, out text, out errorCode)) return false;
                return FinishText(text, frame, out text, out errorCode);
            }
            var facts = observation.Facts.Where(item => item.FactId == frame.FactId).ToArray();
            if (facts.Length != 1) { errorCode = facts.Length == 0 ? "speech.fact_unknown" : "speech.fact_ambiguous"; return false; }
            var fact = facts[0];
            if (!fact.CanExpress) { errorCode = "speech.fact_forbidden"; return false; }
            if (fact.ObserverId != observation.ObserverId
                || (frame.Intent != "report_receipt" && frame.Intent != "recall_statement"
                    && fact.ObservedAtTotalMinutes != observation.ObservedAtTotalMinutes))
            { errorCode = "speech.fact_invalid"; return false; }
            if (frame.Intent == "recall_statement")
            {
                if (fact.Knowledge != "statement") { errorCode = "speech.intent_mismatch"; return false; }
                if (fact.Source != "meeting_transcript" || fact.Scope != "heard_by_participant" || fact.Predicate != "said"
                    || fact.ValueType != "text" || fact.Unit != null || string.IsNullOrWhiteSpace(fact.Value)
                    || fact.Value.Any(char.IsControl) || string.IsNullOrWhiteSpace(fact.SpeakerId) || fact.EntityId != fact.SpeakerId
                    || (fact.SpeakerId != observation.ObserverId && fact.SpeakerId != observation.ListenerId)
                    || double.IsNaN(fact.ObservedAtTotalMinutes) || double.IsInfinity(fact.ObservedAtTotalMinutes)
                    || fact.ObservedAtTotalMinutes < 0 || fact.ObservedAtTotalMinutes > observation.ObservedAtTotalMinutes)
                { errorCode = "speech.fact_invalid"; return false; }
                string speaker = fact.SpeakerId == "npc.fisher_ren" ? "Ren" : fact.SpeakerId == "npc.cook_sora" ? "Sora" : fact.SpeakerId;
                text = "At game minute " + fact.ObservedAtTotalMinutes.ToString("G17", CultureInfo.InvariantCulture) + ", "
                    + speaker + " said: \"" + fact.Value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            }
            else if (frame.Intent == "report_receipt")
            {
                if (fact.Knowledge != "receipt") { errorCode = "speech.intent_mismatch"; return false; }
                if (fact.Source != "meeting_receipt" || fact.Scope != "completed_transfer"
                    || double.IsNaN(fact.ObservedAtTotalMinutes) || double.IsInfinity(fact.ObservedAtTotalMinutes)
                    || fact.ObservedAtTotalMinutes < 0 || fact.ObservedAtTotalMinutes > observation.ObservedAtTotalMinutes
                    || !fact.EntityId.StartsWith("meeting:", StringComparison.Ordinal)
                    || !Guid.TryParseExact(fact.EntityId.Substring(8), "N", out var meetingId) || meetingId == Guid.Empty)
                { errorCode = "speech.fact_invalid"; return false; }
                var receipts = observation.Facts.Where(item => item.EntityId == fact.EntityId && item.Predicate == "delivery_result").ToArray();
                if (receipts.Length != 1 || !receipts[0].CanExpress || receipts[0].ObserverId != observation.ObserverId
                    || receipts[0].Knowledge != "receipt" || receipts[0].Source != "meeting_receipt" || receipts[0].Scope != "completed_transfer"
                    || receipts[0].Value != "resource.delivered" || receipts[0].ValueType != "text" || receipts[0].Unit != null
                    || receipts[0].ObservedAtTotalMinutes != fact.ObservedAtTotalMinutes)
                { errorCode = "speech.fact_invalid"; return false; }
                string when = fact.ObservedAtTotalMinutes.ToString("G17", CultureInfo.InvariantCulture);
                if (fact.Predicate == "delivery_result")
                    text = "At game minute " + when + ", the resource transfer was recorded as complete.";
                else if (fact.ValueType == "number"
                    && int.TryParse(fact.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int amount))
                {
                    if (fact.Predicate == "delivered_quantity" && fact.Unit == "fish.carp" && amount > 0)
                        text = "At game minute " + when + ", the recorded transfer moved " + amount.ToString(CultureInfo.InvariantCulture) + " carp.";
                    else if (fact.Predicate == "delivered_price" && fact.Unit == "coins")
                        text = "At game minute " + when + ", the recorded transfer paid " + amount.ToString(CultureInfo.InvariantCulture) + " coins.";
                    else { errorCode = "speech.fact_invalid"; return false; }
                }
                else { errorCode = "speech.fact_invalid"; return false; }
            }
            else if (frame.Intent == "acknowledge_unknown")
            {
                if (fact.Knowledge != "unknown") { errorCode = "speech.information_known"; return false; }
                var names = observation.Facts.Where(item => item.EntityId == fact.EntityId && item.Predicate == "name").ToArray();
                if (fact.Predicate != "quantity" || fact.Source != "semantic_catalog.unmodelled_quantity"
                    || fact.Scope != "sampled_local_entity" || fact.ValueType != "unknown" || fact.Value != null || fact.Unit != "fish.carp"
                    || !observation.NearbyEntityIds.Contains(fact.EntityId) || names.Length != 1)
                { errorCode = "speech.fact_invalid"; return false; }
                var name = names[0];
                if (!name.CanExpress || name.ObserverId != observation.ObserverId || name.Knowledge != "observed"
                    || name.Source != "semantic_catalog" || name.Scope != "sampled_local_entity" || name.ValueType != "text"
                    || name.Unit != null || name.ObservedAtTotalMinutes != observation.ObservedAtTotalMinutes || string.IsNullOrWhiteSpace(name.Value))
                { errorCode = "speech.fact_invalid"; return false; }
                text = "I do not know how many carp are in " + name.Value + ".";
            }
            else if (frame.Intent == "express_wish") { errorCode = "speech.intent_mismatch"; return false; }
            else if (frame.Intent == "ask_about")
            {
                if (!TryQuestion(observation, fact, out text)) { errorCode = "speech.intent_mismatch"; return false; }
            }
            else if (fact.Knowledge != "observed" && fact.Knowledge != "authored")
            { errorCode = "speech.intent_mismatch"; return false; }
            else if (!TryCurrentReport(observation, fact, out text)) { errorCode = "speech.fact_invalid"; return false; }
            return FinishText(text, frame, out text, out errorCode);
        }

        private static bool TryCurrentReport(NpcLocalObservation observation, NpcObservationFact fact, out string text)
        {
            text = null;
            if ((fact.Predicate == "region" || fact.Predicate == "region_name") && fact.EntityId == observation.ObserverId
                && CurrentFact(observation, fact, "observed", "body_region", "sampled_body_position", "text", null)
                && observation.RegionId != null)
            {
                if (fact.Predicate == "region_name") text = "I am in " + fact.Value + ".";
                else
                {
                    var names = observation.Facts.Where(item => item.EntityId == observation.ObserverId && item.Predicate == "region_name").ToArray();
                    if (fact.Value != observation.RegionId || names.Length != 1
                        || !CurrentFact(observation, names[0], "observed", "body_region", "sampled_body_position", "text", null)) return false;
                    text = "I am in " + names[0].Value + ".";
                }
            }
            else if (fact.Predicate == "owned_quantity" && fact.EntityId == observation.ObserverId
                && CurrentFact(observation, fact, "observed", "character_resources", "own_current_assets", "number", "fish.carp")
                && int.TryParse(fact.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int quantity))
                text = "I currently hold " + quantity.ToString(CultureInfo.InvariantCulture) + " carp.";
            else if (observation.NearbyEntityIds.Contains(fact.EntityId)
                && (fact.Predicate == "name" || fact.Predicate == "kind" || fact.Predicate == "interaction")
                && CurrentFact(observation, fact, fact.Predicate == "name" ? "observed" : "authored",
                    "semantic_catalog", "sampled_local_entity", "text", null) && TryName(observation, fact.EntityId, out string name))
            {
                if (fact.Predicate == "name") text = name + " is nearby.";
                else if (fact.Predicate == "kind" && (fact.Value == "decoration" || fact.Value == "water" || fact.Value == "landmark"))
                    text = name + " is registered as " + fact.Value + ".";
                else if (fact.Predicate == "interaction" && fact.Value == "none")
                    text = name + " has no supported interaction in this scene.";
                else if (fact.Predicate == "interaction" && fact.Value == "fishing")
                    text = "The registered interaction at " + name + " is fishing.";
            }
            else if (TryResident(observation, fact.EntityId, out string resident))
            {
                if (fact.Predicate == "present" && fact.Value == "true"
                    && CurrentFact(observation, fact, "observed", "resident_body", "sampled_local_entity", "boolean", null))
                    text = resident + " is nearby.";
                else if (fact.Predicate == "kind" && fact.Value == "resident"
                    && CurrentFact(observation, fact, "authored", "resident_registry", "sampled_local_entity", "text", null))
                    text = resident + " is registered as a resident.";
            }
            if (text != null) return true;
            if (!fact.EntityId.StartsWith("plan:", StringComparison.Ordinal) || fact.EntityId.Length <= 5
                || string.IsNullOrWhiteSpace(observation.ListenerId) || observation.ListenerId == observation.ObserverId) return false;
            if (fact.Predicate == "decision_stage"
                && CurrentFact(observation, fact, "observed", "meeting_board", "participants", "text", null))
            {
                text = fact.Value == "opportunity" ? "There is an opportunity to arrange a meeting."
                    : fact.Value == "invitation" ? "A meeting invitation is awaiting a reply."
                    : fact.Value == "delivery" ? "We are at the delivery stage."
                    : fact.Value == "conversation" ? "We are in the conversation stage." : null;
                return text != null;
            }
            var sellers = observation.Facts.Where(item => item.EntityId == fact.EntityId && item.Predicate == "seller").ToArray();
            var buyers = observation.Facts.Where(item => item.EntityId == fact.EntityId && item.Predicate == "buyer").ToArray();
            if (sellers.Length != 1 || buyers.Length != 1
                || !CurrentFact(observation, sellers[0], "authored", "meeting_terms", "participants", "text", null)
                || !CurrentFact(observation, buyers[0], "authored", "meeting_terms", "participants", "text", null)
                || !((sellers[0].Value == observation.ObserverId && buyers[0].Value == observation.ListenerId)
                    || (buyers[0].Value == observation.ObserverId && sellers[0].Value == observation.ListenerId))) return false;
            if ((fact.Predicate == "seller" || fact.Predicate == "buyer")
                && CurrentFact(observation, fact, "authored", "meeting_terms", "participants", "text", null))
                text = "The exchange terms name " + ActorName(fact.Value) + " as " + fact.Predicate + ".";
            else if (fact.Predicate == "terms_quantity"
                && CurrentFact(observation, fact, "authored", "meeting_terms", "participants", "number", "fish.carp")
                && int.TryParse(fact.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int amount) && amount > 0)
                text = "The exchange terms specify " + amount.ToString(CultureInfo.InvariantCulture) + " carp.";
            else if (fact.Predicate == "terms_price"
                && CurrentFact(observation, fact, "authored", "meeting_terms", "participants", "number", "coins")
                && int.TryParse(fact.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int price))
                text = "The exchange terms specify " + price.ToString(CultureInfo.InvariantCulture) + " coins.";
            return text != null;
        }

        private static bool TryQuestion(NpcLocalObservation observation, NpcObservationFact fact, out string text)
        {
            text = null;
            if (fact.Predicate == "name" && TryName(observation, fact.EntityId, out string name))
                text = "What can you tell me about " + name + "?";
            else if (fact.Predicate == "present" && TryResident(observation, fact.EntityId, out string resident))
                text = "What can you tell me about " + resident + "?";
            else if (fact.Predicate == "quantity" && fact.Value == null
                && CurrentFact(observation, fact, "unknown", "semantic_catalog.unmodelled_quantity", "sampled_local_entity", "unknown", "fish.carp")
                && TryName(observation, fact.EntityId, out string quantityName))
                text = "Do you know how many carp are in " + quantityName + "?";
            return text != null;
        }

        private static bool TryName(NpcLocalObservation observation, string entityId, out string name)
        {
            name = null;
            var names = observation.Facts.Where(item => item.EntityId == entityId && item.Predicate == "name").ToArray();
            if (!observation.NearbyEntityIds.Contains(entityId) || names.Length != 1
                || !CurrentFact(observation, names[0], "observed", "semantic_catalog", "sampled_local_entity", "text", null)) return false;
            name = names[0].Value;
            return true;
        }

        private static bool TryResident(NpcLocalObservation observation, string entityId, out string name)
        {
            name = null;
            var presence = observation.Facts.Where(item => item.EntityId == entityId && item.Predicate == "present").ToArray();
            if (!observation.NearbyEntityIds.Contains(entityId) || entityId == observation.ObserverId || presence.Length != 1
                || presence[0].Value != "true"
                || !CurrentFact(observation, presence[0], "observed", "resident_body", "sampled_local_entity", "boolean", null)) return false;
            name = ActorName(entityId);
            return true;
        }

        private static bool CurrentFact(NpcLocalObservation observation, NpcObservationFact fact,
            string knowledge, string source, string scope, string valueType, string unit)
            => fact.CanExpress && fact.ObserverId == observation.ObserverId && fact.ObservedAtTotalMinutes == observation.ObservedAtTotalMinutes
                && fact.Knowledge == knowledge && fact.Source == source && fact.Scope == scope && fact.ValueType == valueType && fact.Unit == unit
                && (valueType == "unknown" ? fact.Value == null : !string.IsNullOrWhiteSpace(fact.Value));

        private static string ActorName(string actorId)
            => actorId == "npc.fisher_ren" ? "Ren" : actorId == "npc.cook_sora" ? "Sora"
                : actorId == "npc.shopkeeper_mina" ? "Mina" : actorId == "npc.farmer_eli" ? "Eli" : actorId;

        private static bool TryReserved(NpcLocalObservation observation, NpcSpeechFrame frame, out string text, out string errorCode)
        {
            text = null;
            errorCode = "speech.intent_mismatch";
            if (frame.FactId == "@talk" || frame.FactId == "@learn_cooking")
            {
                if (frame.Intent != "express_wish") return false;
                if (frame.FactId == "@talk")
                {
                    if (string.IsNullOrWhiteSpace(observation.ListenerId) || observation.ListenerId == observation.ObserverId)
                    { errorCode = "speech.fact_forbidden"; return false; }
                    text = frame.Tone == "warm" ? "I'd enjoy a chat with you."
                        : frame.Tone == "brief" ? "I'd like to talk." : "I would like to talk with you.";
                }
                else text = frame.Tone == "warm" ? "I'd enjoy learning about cooking."
                    : frame.Tone == "brief" ? "I'd like to learn cooking." : "I would like to learn about cooking.";
            }
            else if (frame.FactId == "@partner_assets")
            {
                if (frame.Intent != "acknowledge_unknown" && frame.Intent != "ask_about") return false;
                if (string.IsNullOrWhiteSpace(observation.ListenerId) || observation.ListenerId == observation.ObserverId)
                { errorCode = "speech.fact_forbidden"; return false; }
                if (observation.Facts.Any(fact => fact.EntityId == observation.ListenerId && fact.Knowledge == "observed"
                    && fact.Source == "character_resources" && fact.ObservedAtTotalMinutes == observation.ObservedAtTotalMinutes
                    && (fact.Predicate == "owned_quantity" || fact.Predicate == "balance") && fact.ValueType == "number" && fact.Value != null))
                { errorCode = "speech.information_known"; return false; }
                text = frame.Intent == "ask_about" ? "What resources do you currently have?"
                    : "I do not know your current inventory or coin balance.";
            }
            else if (frame.FactId == "@nearby_coverage")
            {
                if (frame.Intent != "report_observation" && frame.Intent != "acknowledge_unknown") return false;
                if (frame.Intent == "acknowledge_unknown" && observation.RegionId != null && observation.NearbyComplete)
                { errorCode = "speech.information_known"; return false; }
                if (observation.NearbyEntityIds.Distinct(StringComparer.Ordinal).Count() != observation.NearbyEntityIds.Count)
                { errorCode = "speech.fact_invalid"; return false; }
                if (observation.RegionId == null)
                    text = "My current region is unmapped, so I cannot provide a complete nearby list.";
                else if (!observation.NearbyComplete)
                    text = "My list of registered nearby entities is incomplete; it cannot show that other objects are absent.";
                else text = "My complete list contains " + (observation.NearbyEntityIds.Count == 0 ? "no" : observation.NearbyEntityIds.Count.ToString(CultureInfo.InvariantCulture))
                    + " registered nearby entities in this region within " + observation.Radius.ToString("G17", CultureInfo.InvariantCulture) + " units.";
            }
            else { errorCode = "speech.fact_unknown"; return false; }
            errorCode = null;
            return true;
        }

        private static bool FinishText(string candidate, NpcSpeechFrame frame, out string text, out string errorCode)
        {
            text = frame.Tone == "warm" && frame.Intent != "express_wish" ? "I'd like to share this: " + candidate : candidate;
            if (text.Length > 180) { text = null; errorCode = "speech.text_too_long"; return false; }
            errorCode = null;
            return true;
        }
    }
}

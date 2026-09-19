using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;
using CozyTown.Runtime.Economy;

namespace CozyTown.Runtime.NpcAgents
{
    public sealed class NpcObservationScene
    {
        private readonly NpcObservationRegion[] _regions;
        private readonly NpcObservationEntity[] _entities;
        public double Radius { get; }
        public int MaxNearby { get; }

        public string CaptureConfiguration()
        {
            var result = new StringBuilder();
            Append("local-observation-v1", Radius, MaxNearby, _regions.Length);
            foreach (var region in _regions)
                Append(region.Id, region.Name, region.SpaceId, region.MinX, region.MinY, region.MaxX, region.MaxY);
            Append(_entities.Length);
            foreach (var entity in _entities)
                Append(entity.Id, entity.Name, entity.X, entity.Y, entity.Kind, entity.SpaceId,
                    entity.InteractionId, entity.ResourceItemId);
            return result.ToString();

            void Append(params object[] values)
            {
                foreach (object value in values)
                {
                    string text = value is double number ? number.ToString("R", CultureInfo.InvariantCulture)
                        : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                    result.Append(text.Length).Append(':').Append(text);
                }
            }
        }

        public NpcObservationScene(IEnumerable<NpcObservationRegion> regions, IEnumerable<NpcObservationEntity> entities,
            double radius = 3, int maxNearby = 8)
        {
            _regions = (regions ?? throw new ArgumentNullException(nameof(regions))).ToArray();
            _entities = (entities ?? throw new ArgumentNullException(nameof(entities))).ToArray();
            if (!NpcObservationRegion.Finite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
            Radius = radius;
            if (maxNearby < 1 || maxNearby > 8) throw new ArgumentOutOfRangeException(nameof(maxNearby));
            MaxNearby = maxNearby;
            if (_regions.Any(item => item == null) || _regions.Select(item => item.Id).Distinct().Count() != _regions.Length
                || _entities.Any(item => item == null) || _entities.Select(item => item.Id).Distinct().Count() != _entities.Length)
                throw new ArgumentException("Observation catalog entries require unique IDs.");
            foreach (var a in _regions)
                foreach (var b in _regions)
                    if (!ReferenceEquals(a, b) && a.SpaceId == b.SpaceId
                        && a.MinX < b.MaxX && b.MinX < a.MaxX && a.MinY < b.MaxY && b.MinY < a.MaxY)
                        throw new ArgumentException("Observation regions in the same space must not overlap.");
        }

        public NpcLocalObservation Read(string npcId, Guid worldRunId, double observedAt,
            IEnumerable<NpcObservationBody> bodies, NpcSocialContext social = null, CharacterTradeResources ownResources = null,
            IEnumerable<NpcMeetingMemory> memories = null)
        {
            if (worldRunId == Guid.Empty || !NpcObservationRegion.Finite(observedAt) || observedAt < 0)
                throw new ArgumentException("Observation requires a world run and finite nonnegative time.");
            var bodyArray = (bodies ?? throw new ArgumentNullException(nameof(bodies))).ToArray();
            var body = bodyArray.Single(item => item.NpcId == npcId);
            var region = _regions.SingleOrDefault(item => item.Contains(body.X, body.Y, body.SpaceId));
            var visible = _entities.Concat(bodyArray.Where(item => item.NpcId != npcId && item.Observable)
                    .Select(item => new NpcObservationEntity(item.NpcId, item.NpcId, item.X, item.Y, "resident", item.SpaceId)))
                .Where(item => region != null && region.Contains(item.X, item.Y, item.SpaceId)
                && (item.X - body.X) * (item.X - body.X) + (item.Y - body.Y) * (item.Y - body.Y) <= Radius * Radius)
                .OrderBy(item => item.Id, StringComparer.Ordinal).Take(MaxNearby + 1).ToArray();
            var nearby = visible.Take(MaxNearby).ToArray();
            var facts = new List<NpcObservationFact>();
            Add(npcId, "region", region?.Id, region == null ? "unknown" : "text", null,
                region == null ? "unknown" : "observed", "body_region", "sampled_body_position");
            if (region != null)
                Add(npcId, "region_name", region.Name, "text", null, "observed", "body_region", "sampled_body_position");
            if (ownResources != null)
            {
                var terms = ownResources.Terms;
                if ((ownResources.Role == "buyer" ? terms.BuyerId : terms.SellerId) != npcId)
                    throw new ArgumentException("Resource observations must belong to the observer.", nameof(ownResources));
                bool participant = social != null && social.PartnerId == (npcId == terms.BuyerId ? terms.SellerId : terms.BuyerId);
                Add(npcId, "owned_quantity", Number(ownResources.OwnedQuantity), "number", terms.ItemId,
                    "observed", "character_resources", "own_current_assets", participant);
                Add(npcId, "balance", Number(ownResources.Balance), "number", "coins",
                    "observed", "character_resources", "own_current_assets", false);
                if (participant)
                {
                    string id = "plan:" + social.PlanId;
                    Add(id, "terms_quantity", Number(terms.Quantity), "number", terms.ItemId, "authored", "meeting_terms", "participants");
                    Add(id, "terms_price", Number(terms.TotalPrice), "number", "coins", "authored", "meeting_terms", "participants");
                    Add(id, "seller", terms.SellerId, "text", null, "authored", "meeting_terms", "participants");
                    Add(id, "buyer", terms.BuyerId, "text", null, "authored", "meeting_terms", "participants");
                }
            }
            if (social != null)
                Add("plan:" + social.PlanId, "decision_stage", social.Kind.ToString().ToLowerInvariant(), "text", null,
                    "observed", "meeting_board", "participants");
            var records = (memories ?? Array.Empty<NpcMeetingMemory>()).ToArray();
            var receipt = records.LastOrDefault(item => item.Kind == "resource.delivered");
            if (receipt != null)
            {
                Receipt("delivery_result", "resource.delivered", "text", null);
                if (receipt.MeetingId == social?.MeetingId && ownResources != null)
                {
                    Receipt("delivered_quantity", Number(ownResources.Terms.Quantity), "number", ownResources.Terms.ItemId);
                    Receipt("delivered_price", Number(ownResources.Terms.TotalPrice), "number", "coins");
                }

                void Receipt(string predicate, string value, string type, string unit)
                {
                    string entity = "meeting:" + receipt.MeetingId.ToString("N");
                    facts.Add(new NpcObservationFact(entity + ":" + predicate, entity, predicate, value, type, unit, "receipt",
                        "meeting_receipt", npcId, receipt.TotalMinutes, "completed_transfer", social?.PartnerId == receipt.PartnerId));
                }
            }
            var statements = records.Where(item => item.Kind == "meeting.spoken")
                .Reverse().Take(4).Reverse().ToArray();
            for (int i = 0; i < statements.Length; i++)
            {
                var statement = statements[i];
                facts.Add(new NpcObservationFact("statement:" + statement.MeetingId.ToString("N") + ":" + i,
                    statement.SpeakerId, "said", statement.Text, "text", null, "statement", "meeting_transcript", npcId,
                    statement.TotalMinutes, "heard_by_participant", social?.PartnerId == statement.PartnerId, statement.SpeakerId));
            }
            foreach (var entity in nearby)
            {
                if (entity.Kind == "resident")
                {
                    Add(entity.Id, "present", "true", "boolean", null, "observed", "resident_body");
                    Add(entity.Id, "kind", "resident", "text", null, "authored", "resident_registry");
                    continue;
                }
                Add(entity.Id, "name", entity.Name, "text", null, "observed", "semantic_catalog");
                Add(entity.Id, "kind", entity.Kind, "text", null, "authored", "semantic_catalog");
                Add(entity.Id, "interaction", entity.InteractionId, "text", null, "authored", "semantic_catalog");
                if (entity.ResourceItemId != null)
                    Add(entity.Id, "quantity", null, "unknown", entity.ResourceItemId, "unknown", "semantic_catalog.unmodelled_quantity");
            }
            return new NpcLocalObservation(npcId, worldRunId, observedAt, body, region?.Id, nearby.Select(item => item.Id),
                facts, Radius, region != null && visible.Length <= MaxNearby, social?.PartnerId);

            void Add(string entity, string predicate, string value, string type, string unit, string knowledge, string source,
                string scope = "sampled_local_entity", bool canExpress = true)
                => facts.Add(new NpcObservationFact(entity + ":" + predicate, entity, predicate, value, type, unit,
                    knowledge, source, npcId, observedAt, scope, canExpress));
        }

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}

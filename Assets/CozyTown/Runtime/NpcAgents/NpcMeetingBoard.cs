using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.NpcLife;

namespace CozyTown.Runtime.NpcAgents
{
    public sealed class NpcMeetingBoard
    {
        private NpcAgentWorld _world;
        private CharacterResourceTrading _resources;
        private readonly Dictionary<string, NpcMeetingPlan> _plans;
        private readonly Dictionary<string, Meeting> _current = new Dictionary<string, Meeting>(StringComparer.Ordinal);
        private readonly Dictionary<string, Meeting> _places = new Dictionary<string, Meeting>(StringComparer.Ordinal);
        private readonly Func<string, string, NpcMeetingPresence> _presence;
        private readonly Dictionary<string, NpcMeetingSnapshot> _latest = new Dictionary<string, NpcMeetingSnapshot>(StringComparer.Ordinal);
        private Guid _worldRunId;
        private readonly Dictionary<string, double> _offeredDays = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, NpcMeetingPlan> _opportunities = new Dictionary<string, NpcMeetingPlan>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<NpcMeetingMemory>> _memories = new Dictionary<string, List<NpcMeetingMemory>>(StringComparer.Ordinal);

        private sealed class Meeting
        {
            internal Guid Id = Guid.NewGuid();
            internal NpcMeetingPlan Plan;
            internal NpcMeetingState State = NpcMeetingState.Invited;
            internal double DayStart;
            internal double StartsAt;
            internal double EndsAt;
            internal NpcActivityRequest InitiatorActivity;
            internal NpcActivityRequest PartnerActivity;
            internal string SpeakerId;
            internal string DeliveryResultCode;
            internal readonly List<NpcConversationLine> Transcript = new List<NpcConversationLine>();
            internal NpcMeetingSnapshot Snapshot => new NpcMeetingSnapshot(Id, Plan.InitiatorId, Plan.PartnerId, State, SpeakerId, Transcript, Plan.ResourceTerms, DeliveryResultCode);
        }

        public NpcMeetingBoardSnapshot CaptureSnapshot()
        {
            if (_plans.Count > 0 && _world.GetState(_plans.Values.First().InitiatorId).WorldRunId != _worldRunId)
                throw new InvalidOperationException("Rebind the meeting board before capturing a different world timeline.");
            foreach (var meeting in _current.Values.Distinct())
                if (meeting.State != NpcMeetingState.Invited
                    && (!ReferenceEquals(_world.GetState(meeting.Plan.InitiatorId).ActiveActivity, meeting.InitiatorActivity)
                        || !ReferenceEquals(_world.GetState(meeting.Plan.PartnerId).ActiveActivity, meeting.PartnerActivity)))
                    throw new InvalidOperationException("Complete the pending meeting update before capturing released activities.");
            return new NpcMeetingBoardSnapshot(_plans.Values,
                _current.Values.Distinct().Select(meeting => new NpcMeetingStateSnapshot(meeting.Id,
                    meeting.Plan.Id, meeting.State, meeting.DayStart, meeting.StartsAt, meeting.EndsAt,
                    meeting.InitiatorActivity?.ActivityId ?? Guid.Empty, meeting.PartnerActivity?.ActivityId ?? Guid.Empty,
                    meeting.SpeakerId, meeting.DeliveryResultCode, meeting.Transcript)),
                _latest.Values.GroupBy(item => item.Id).Select(group => group.First()),
                _latest.Select(pair => new NpcLatestMeetingSnapshot(pair.Key, pair.Value.Id)),
                _offeredDays.Select(pair => new NpcMeetingDaySnapshot(pair.Key, pair.Value)),
                _opportunities.Select(pair => new NpcMeetingOpportunitySnapshot(pair.Key, pair.Value.Id)),
                _memories.Select(pair => new NpcResidentMemoriesSnapshot(pair.Key, pair.Value)));
        }

        public OperationResult<NpcMeetingBoard> PrepareRestore(NpcMeetingBoardSnapshot snapshot,
            NpcAgentWorld world, CharacterResourceTrading resources = null)
        {
            if (snapshot == null || world == null || snapshot.Plans == null || snapshot.Meetings == null
                || snapshot.LatestResults == null || snapshot.LatestByResident == null || snapshot.OfferedDays == null
                || snapshot.Opportunities == null || snapshot.Memories == null)
                return OperationResult<NpcMeetingBoard>.Failure("meeting.snapshot_invalid");
            var plans = _plans.Values.ToArray();
            if (snapshot.Plans.Count != plans.Length || snapshot.Plans.Where((plan, index) => !SamePlan(plan, plans[index])).Any())
                return OperationResult<NpcMeetingBoard>.Failure("meeting.snapshot_content_mismatch");
            var residents = world.CaptureSnapshot().Residents.ToDictionary(item => item.NpcId, StringComparer.Ordinal);
            var restoredResources = resources ?? _resources;
            if (plans.Any(plan => !residents.ContainsKey(plan.InitiatorId) || !residents.ContainsKey(plan.PartnerId)
                || (plan.ResourceTerms != null && (restoredResources?.Inspect(plan.ResourceTerms, plan.InitiatorId) == null
                    || restoredResources.Inspect(plan.ResourceTerms, plan.PartnerId) == null))))
                return OperationResult<NpcMeetingBoard>.Failure("meeting.snapshot_residents_invalid");
            if (snapshot.Meetings.Count > residents.Count / 2 || snapshot.LatestResults.Count > residents.Count
                || snapshot.LatestByResident.Count > residents.Count || snapshot.Memories.Count > residents.Count
                || snapshot.OfferedDays.Count > plans.Length || snapshot.Opportunities.Count > residents.Count)
                return OperationResult<NpcMeetingBoard>.Failure("meeting.snapshot_invalid");

            var candidate = new NpcMeetingBoard(world, plans, _presence, restoredResources);
            var meetingIds = new HashSet<Guid>();
            foreach (var saved in snapshot.Meetings)
            {
                if (saved == null || saved.Id == Guid.Empty || !meetingIds.Add(saved.Id)
                    || string.IsNullOrWhiteSpace(saved.PlanId) || !_plans.TryGetValue(saved.PlanId, out var plan)
                    || candidate._current.ContainsKey(plan.InitiatorId) || candidate._current.ContainsKey(plan.PartnerId)
                    || !ValidDay(saved.DayStart, world.TotalMinutes)
                    || saved.State < NpcMeetingState.Invited || saved.State > NpcMeetingState.Talking
                    || !ValidTranscript(saved.Transcript, plan.InitiatorId, plan.PartnerId, plan.MaxTurns, world.TotalMinutes)
                    || saved.Transcript.Count >= plan.MaxTurns)
                    return OperationResult<NpcMeetingBoard>.Failure("meeting.snapshot_meeting_invalid");
                var meeting = new Meeting { Id = saved.Id, Plan = plan, State = saved.State, DayStart = saved.DayStart,
                    StartsAt = saved.StartsAt, EndsAt = saved.EndsAt, SpeakerId = saved.SpeakerId,
                    DeliveryResultCode = saved.DeliveryResultCode };
                if (saved.State == NpcMeetingState.Invited)
                {
                    if (saved.StartsAt != 0 || saved.EndsAt != 0 || saved.InitiatorActivityId != Guid.Empty
                        || saved.PartnerActivityId != Guid.Empty || saved.SpeakerId != null || saved.Transcript.Count != 0
                        || saved.DeliveryResultCode != null || world.TotalMinutes < saved.DayStart + plan.InviteStartMinute
                        || world.TotalMinutes >= saved.DayStart + plan.InviteEndMinute)
                        return OperationResult<NpcMeetingBoard>.Failure("meeting.snapshot_meeting_invalid");
                }
                else
                {
                    if (!ValidMinute(saved.StartsAt, double.MaxValue) || !ValidMinute(saved.EndsAt, double.MaxValue)
                        || saved.StartsAt < saved.DayStart + plan.MeetingStartMinute
                        || saved.StartsAt >= saved.DayStart + plan.InviteEndMinute
                        || saved.EndsAt != saved.StartsAt + plan.DurationGameMinutes || saved.EndsAt <= world.TotalMinutes
                        || !MatchesActivity(residents[plan.InitiatorId].Activity, saved.InitiatorActivityId,
                            plan.InitiatorLocationId, saved.StartsAt, saved.EndsAt)
                        || !MatchesActivity(residents[plan.PartnerId].Activity, saved.PartnerActivityId,
                            plan.PartnerLocationId, saved.StartsAt, saved.EndsAt)
                        || candidate._places.ContainsKey(plan.PlaceId))
                        return OperationResult<NpcMeetingBoard>.Failure("meeting.snapshot_activity_invalid");
                    if (saved.State == NpcMeetingState.Talking)
                    {
                        string nextSpeaker = saved.Transcript.Count % 2 == 0 ? plan.InitiatorId : plan.PartnerId;
                        if (saved.StartsAt > world.TotalMinutes || saved.SpeakerId != nextSpeaker
                            || (saved.DeliveryResultCode != null && (plan.ResourceTerms == null || saved.DeliveryResultCode != "resource.delivered"))
                            || (plan.ResourceTerms != null && saved.Transcript.Count > 0 && saved.DeliveryResultCode != "resource.delivered"))
                            return OperationResult<NpcMeetingBoard>.Failure("meeting.snapshot_turn_invalid");
                    }
                    else if (saved.SpeakerId != null || saved.Transcript.Count != 0 || saved.DeliveryResultCode != null
                        || (saved.State == NpcMeetingState.Scheduled && saved.StartsAt < world.TotalMinutes)
                        || (saved.State == NpcMeetingState.Travelling && saved.StartsAt > world.TotalMinutes))
                        return OperationResult<NpcMeetingBoard>.Failure("meeting.snapshot_turn_invalid");
                    meeting.InitiatorActivity = world.GetState(plan.InitiatorId).ActiveActivity;
                    meeting.PartnerActivity = world.GetState(plan.PartnerId).ActiveActivity;
                    candidate._places.Add(plan.PlaceId, meeting);
                }
                meeting.Transcript.AddRange(saved.Transcript);
                candidate._current.Add(plan.InitiatorId, meeting);
                candidate._current.Add(plan.PartnerId, meeting);
            }

            if (residents.Values.Any(resident => resident.Activity?.IsMeetingActivity == true
                && (!candidate._current.TryGetValue(resident.NpcId, out var owner) || owner.State == NpcMeetingState.Invited)))
                return OperationResult<NpcMeetingBoard>.Failure("meeting.snapshot_activity_invalid");

            var results = new Dictionary<Guid, NpcMeetingSnapshot>();
            foreach (var result in snapshot.LatestResults)
            {
                if (result == null || result.Id == Guid.Empty || !meetingIds.Add(result.Id)
                    || !residents.ContainsKey(result.InitiatorId ?? string.Empty) || !residents.ContainsKey(result.PartnerId ?? string.Empty)
                    || result.InitiatorId == result.PartnerId || result.State < NpcMeetingState.Completed || result.State > NpcMeetingState.Expired
                    || result.SpeakerId != null || !ValidTranscript(result.Transcript, result.InitiatorId, result.PartnerId, 8, world.TotalMinutes)
                    || !plans.Any(plan => plan.InitiatorId == result.InitiatorId && plan.PartnerId == result.PartnerId
                        && SameTerms(plan.ResourceTerms, result.ResourceTerms) && ValidResultForPlan(result, plan)))
                    return OperationResult<NpcMeetingBoard>.Failure("meeting.snapshot_result_invalid");
                results.Add(result.Id, result);
            }
            foreach (var link in snapshot.LatestByResident)
            {
                if (link == null || string.IsNullOrWhiteSpace(link.NpcId) || candidate._latest.ContainsKey(link.NpcId)
                    || !results.TryGetValue(link.MeetingId, out var result)
                    || (link.NpcId != result.InitiatorId && link.NpcId != result.PartnerId))
                    return OperationResult<NpcMeetingBoard>.Failure("meeting.snapshot_result_invalid");
                candidate._latest.Add(link.NpcId, result);
            }
            if (results.Keys.Any(id => !candidate._latest.Values.Any(result => result.Id == id)))
                return OperationResult<NpcMeetingBoard>.Failure("meeting.snapshot_result_invalid");
            foreach (var offered in snapshot.OfferedDays)
            {
                if (offered == null || string.IsNullOrWhiteSpace(offered.PlanId) || !_plans.ContainsKey(offered.PlanId)
                    || candidate._offeredDays.ContainsKey(offered.PlanId) || !ValidDay(offered.DayStart, world.TotalMinutes))
                    return OperationResult<NpcMeetingBoard>.Failure("meeting.snapshot_opportunity_invalid");
                candidate._offeredDays.Add(offered.PlanId, offered.DayStart);
            }
            foreach (var meeting in snapshot.Meetings)
                if (!candidate._offeredDays.TryGetValue(meeting.PlanId, out double day) || day != meeting.DayStart)
                    return OperationResult<NpcMeetingBoard>.Failure("meeting.snapshot_opportunity_invalid");
            foreach (var opportunity in snapshot.Opportunities)
            {
                if (opportunity == null || string.IsNullOrWhiteSpace(opportunity.PlanId)
                    || !_plans.TryGetValue(opportunity.PlanId, out var plan) || opportunity.NpcId != plan.InitiatorId
                    || candidate._opportunities.ContainsKey(opportunity.NpcId) || !candidate._offeredDays.ContainsKey(plan.Id))
                    return OperationResult<NpcMeetingBoard>.Failure("meeting.snapshot_opportunity_invalid");
                candidate._opportunities.Add(opportunity.NpcId, plan);
            }
            foreach (var owner in snapshot.Memories)
            {
                if (owner == null || !residents.ContainsKey(owner.NpcId ?? string.Empty) || candidate._memories.ContainsKey(owner.NpcId)
                    || owner.Memories == null || owner.Memories.Count > 16)
                    return OperationResult<NpcMeetingBoard>.Failure("meeting.snapshot_memory_invalid");
                double last = 0;
                foreach (var memory in owner.Memories)
                {
                    if (memory == null || memory.MeetingId == Guid.Empty || string.IsNullOrWhiteSpace(memory.Kind) || memory.Kind.Length > 128
                        || !residents.ContainsKey(memory.PartnerId ?? string.Empty) || memory.PartnerId == owner.NpcId
                        || !plans.Any(plan => (plan.InitiatorId == owner.NpcId && plan.PartnerId == memory.PartnerId)
                            || (plan.PartnerId == owner.NpcId && plan.InitiatorId == memory.PartnerId))
                        || !ValidMinute(memory.TotalMinutes, world.TotalMinutes) || memory.TotalMinutes < last
                        || (memory.Kind == "meeting.spoken" ? ((memory.SpeakerId != owner.NpcId && memory.SpeakerId != memory.PartnerId)
                            || string.IsNullOrWhiteSpace(memory.Text) || memory.Text.Length > 240)
                            : memory.SpeakerId != null || memory.Text != null))
                        return OperationResult<NpcMeetingBoard>.Failure("meeting.snapshot_memory_invalid");
                    last = memory.TotalMinutes;
                }
                candidate._memories.Add(owner.NpcId, owner.Memories.ToList());
            }
            return OperationResult<NpcMeetingBoard>.Success(candidate);
        }

        private static bool MatchesActivity(NpcActivitySnapshot activity, Guid id, string location, double starts, double ends)
            => activity != null && activity.IsMeetingActivity && id != Guid.Empty && activity.ActivityId == id && activity.TargetLocationId == location
                && activity.Activity == NpcActivity.Resting && activity.StartsAtTotalMinutes == starts && activity.ExpiresAtTotalMinutes == ends;

        private static bool ValidDay(double dayStart, double now)
            => ValidMinute(dayStart, now) && dayStart % 1440 == 0;

        private static bool ValidMinute(double value, double maximum)
            => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= maximum;

        private static bool ValidTranscript(IReadOnlyList<NpcConversationLine> transcript, string initiator, string partner, int maxTurns, double now)
        {
            if (transcript == null || transcript.Count > maxTurns) return false;
            double last = 0;
            for (int i = 0; i < transcript.Count; i++)
            {
                var line = transcript[i];
                if (line == null || line.SpeakerId != (i % 2 == 0 ? initiator : partner)
                    || string.IsNullOrWhiteSpace(line.Text) || line.Text.Length > 240
                    || !ValidMinute(line.TotalMinutes, now) || line.TotalMinutes < last) return false;
                last = line.TotalMinutes;
            }
            return true;
        }

        private static bool SamePlan(NpcMeetingPlan first, NpcMeetingPlan second)
            => first != null && second != null && first.Id == second.Id && first.InitiatorId == second.InitiatorId
                && first.PartnerId == second.PartnerId && first.PlaceId == second.PlaceId
                && first.InitiatorLocationId == second.InitiatorLocationId && first.PartnerLocationId == second.PartnerLocationId
                && first.InviteStartMinute == second.InviteStartMinute && first.MeetingStartMinute == second.MeetingStartMinute
                && first.InviteEndMinute == second.InviteEndMinute && first.DurationGameMinutes == second.DurationGameMinutes
                && first.MaxTurns == second.MaxTurns && SameTerms(first.ResourceTerms, second.ResourceTerms);

        private static bool SameTerms(CharacterTradeTerms first, CharacterTradeTerms second)
            => first == null ? second == null : second != null && first.BuyerId == second.BuyerId
                && first.SellerId == second.SellerId && first.ItemId == second.ItemId
                && first.Quantity == second.Quantity && first.TotalPrice == second.TotalPrice;

        private static bool ValidResultForPlan(NpcMeetingSnapshot result, NpcMeetingPlan plan)
        {
            if (result.Transcript.Count > plan.MaxTurns
                || (result.State == NpcMeetingState.Completed ? result.Transcript.Count < 2 : result.Transcript.Count == plan.MaxTurns)
                || (result.State == NpcMeetingState.Declined && result.Transcript.Count != 0)) return false;
            if (plan.ResourceTerms == null) return result.DeliveryResultCode == null;
            if (result.State == NpcMeetingState.Completed || result.Transcript.Count > 0)
                return result.DeliveryResultCode == "resource.delivered";
            if (result.State == NpcMeetingState.Declined) return result.DeliveryResultCode == null;
            return result.DeliveryResultCode == null || result.DeliveryResultCode == "resource.delivered"
                || (result.State == NpcMeetingState.Cancelled && !string.IsNullOrWhiteSpace(result.DeliveryResultCode)
                    && result.DeliveryResultCode.Length <= 128);
        }

        public NpcMeetingBoard(NpcAgentWorld world, IEnumerable<NpcMeetingPlan> plans,
            Func<string, string, NpcMeetingPresence> presence = null, CharacterResourceTrading resources = null)
        {
            _resources = resources;
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _presence = presence ?? ((npc, location) => NpcMeetingPresence.Travelling);
            if (plans == null) throw new ArgumentNullException(nameof(plans));
            _plans = plans.ToDictionary(plan => plan.Id, StringComparer.Ordinal);
            foreach (var plan in _plans.Values)
            {
                if (plan.ResourceTerms != null && (_resources?.Inspect(plan.ResourceTerms, plan.InitiatorId) == null
                    || _resources.Inspect(plan.ResourceTerms, plan.PartnerId) == null))
                    throw new ArgumentException("Resource plans require assets owned by both participants.");
                _world.GetState(plan.InitiatorId);
                _world.GetState(plan.PartnerId);
            }
            if (_plans.Count > 0) _worldRunId = _world.GetState(_plans.Values.First().InitiatorId).WorldRunId;
        }

        public OperationResult<NpcMeetingSnapshot> Invite(NpcAgentSnapshot actor, string planId)
        {
            string invalid = ValidateActor(actor);
            if (invalid != null) return OperationResult<NpcMeetingSnapshot>.Failure(invalid);
            if (!_plans.TryGetValue(planId ?? string.Empty, out var plan))
                return OperationResult<NpcMeetingSnapshot>.Failure("meeting.plan_unknown");
            if (actor.NpcId != plan.InitiatorId)
                return OperationResult<NpcMeetingSnapshot>.Failure("meeting.actor_invalid");
            if (plan.ResourceTerms != null && !_resources.IsNeeded(plan.ResourceTerms))
                return OperationResult<NpcMeetingSnapshot>.Failure("resource.need_satisfied");
            if (plan.ResourceTerms != null && _resources.Inspect(plan.ResourceTerms, actor.NpcId).Balance < plan.ResourceTerms.TotalPrice)
                return OperationResult<NpcMeetingSnapshot>.Failure("wallet.insufficient_funds");
            if (_current.ContainsKey(plan.InitiatorId) || _current.ContainsKey(plan.PartnerId))
                return OperationResult<NpcMeetingSnapshot>.Failure("meeting.resident_reserved");
            if (_world.GetState(plan.InitiatorId).ActiveActivity != null || _world.GetState(plan.PartnerId).ActiveActivity != null)
                return OperationResult<NpcMeetingSnapshot>.Failure("meeting.resident_busy");
            double dayStart = Math.Floor(_world.TotalMinutes / 1440) * 1440;
            if (_world.TotalMinutes < dayStart + plan.InviteStartMinute || _world.TotalMinutes >= dayStart + plan.InviteEndMinute)
                return OperationResult<NpcMeetingSnapshot>.Failure("meeting.window_closed");
            var meeting = new Meeting { Plan = plan, DayStart = dayStart };
            _offeredDays[plan.Id] = dayStart;
            _opportunities.Remove(plan.InitiatorId);
            _current.Add(plan.InitiatorId, meeting);
            _current.Add(plan.PartnerId, meeting);
            Remember(meeting, "meeting.invited");
            return OperationResult<NpcMeetingSnapshot>.Success(meeting.Snapshot);
        }

        public OperationResult Deliver(NpcAgentSnapshot actor, Guid meetingId)
        {
            string invalid = ValidateActor(actor);
            if (invalid != null) return OperationResult.Failure(invalid);
            var latest = GetLatest(actor.NpcId);
            if (latest?.Id == meetingId && latest.ResourceTerms?.BuyerId == actor.NpcId && latest.DeliveryResultCode == "resource.delivered")
                return OperationResult.Success();
            invalid = ValidateSpeaker(actor, meetingId, out var meeting);
            if (invalid != null) return OperationResult.Failure(invalid);
            if (meeting.Plan.ResourceTerms == null || actor.NpcId != meeting.Plan.ResourceTerms.BuyerId)
                return OperationResult.Failure("resource.actor_invalid");
            var result = _resources.Exchange(meeting.Plan.ResourceTerms);
            meeting.DeliveryResultCode = result.IsSuccess ? "resource.delivered" : result.ErrorCode;
            Remember(meeting, meeting.DeliveryResultCode);
            if (!result.IsSuccess) Remove(meeting, NpcMeetingState.Cancelled);
            return result;
        }

        public OperationResult CancelExchange(NpcAgentSnapshot actor, Guid meetingId)
        {
            string invalid = ValidateActor(actor);
            if (invalid != null) return OperationResult.Failure(invalid);
            if (!_current.TryGetValue(actor.NpcId, out var meeting) || meeting.Id != meetingId || meeting.Plan.ResourceTerms == null)
                return OperationResult.Failure("meeting.unknown");
            if (meeting.DeliveryResultCode == "resource.delivered") return OperationResult.Failure("resource.already_delivered");
            Remove(meeting, NpcMeetingState.Cancelled);
            return OperationResult.Success();
        }

        public NpcMeetingSnapshot GetCurrent(string npcId)
            => _current.TryGetValue(npcId, out var meeting) ? meeting.Snapshot : null;

        public CharacterTradeResources GetResources(string npcId)
            => _resources?.Inspect(GetLatest(npcId)?.ResourceTerms, npcId);

        public void BindWorld(NpcAgentWorld world, CharacterResourceTrading resources = null)
        {
            ValidateWorldBinding(world, resources);
            if (resources != null) _resources = resources;
            _world = world;
            Observe();
        }

        public void ValidateWorldBinding(NpcAgentWorld world, CharacterResourceTrading resources = null)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            foreach (var plan in _plans.Values)
            {
                world.GetState(plan.InitiatorId);
                world.GetState(plan.PartnerId);
            }
            if (resources != null && !ReferenceEquals(resources, _resources))
            {
                if (_plans.Count > 0 && world.GetState(_plans.Values.First().InitiatorId).WorldRunId == _worldRunId)
                    throw new InvalidOperationException("Changing resource ownership requires a new world timeline.");
                foreach (var plan in _plans.Values.Where(plan => plan.ResourceTerms != null))
                    if (resources.Inspect(plan.ResourceTerms, plan.InitiatorId) == null || resources.Inspect(plan.ResourceTerms, plan.PartnerId) == null)
                        throw new ArgumentException("Resource plans require assets owned by both participants.");
            }
        }

        public NpcSocialContext GetContext(string npcId)
        {
            var memories = GetMemories(npcId).Reverse().Take(4).Reverse();
            if (_current.TryGetValue(npcId, out var meeting))
            {
                if (meeting.State == NpcMeetingState.Invited && npcId == meeting.Plan.PartnerId)
                    return new NpcSocialContext(NpcSocialContextKind.Invitation, meeting.Plan, npcId, meeting.Id,
                        meeting.DayStart + meeting.Plan.MeetingStartMinute, meeting.DayStart + meeting.Plan.InviteEndMinute, meeting.Transcript, memories,
                        _resources?.Inspect(meeting.Plan.ResourceTerms, npcId));
                if (meeting.State == NpcMeetingState.Talking && npcId == meeting.SpeakerId)
                    return new NpcSocialContext(meeting.Plan.ResourceTerms != null && meeting.DeliveryResultCode == null
                        ? NpcSocialContextKind.Delivery : NpcSocialContextKind.Conversation, meeting.Plan, npcId, meeting.Id,
                        meeting.StartsAt, meeting.EndsAt, meeting.Transcript, memories,
                        _resources?.Inspect(meeting.Plan.ResourceTerms, npcId), meeting.DeliveryResultCode);
                return null;
            }
            if (!_opportunities.TryGetValue(npcId, out var plan)) return null;
            if (plan.ResourceTerms != null && !_resources.IsNeeded(plan.ResourceTerms)) return null;
            double day = Math.Floor(_world.TotalMinutes / 1440) * 1440;
            if (!_offeredDays.TryGetValue(plan.Id, out double offered) || offered != day
                || _world.TotalMinutes < day + plan.InviteStartMinute || _world.TotalMinutes >= day + plan.InviteEndMinute) return null;
            return new NpcSocialContext(NpcSocialContextKind.Opportunity, plan, npcId, Guid.Empty,
                day + plan.MeetingStartMinute, day + plan.InviteEndMinute, Array.Empty<NpcConversationLine>(), memories,
                _resources?.Inspect(plan.ResourceTerms, npcId));
        }

        internal void TakeOpportunity(string npcId) => _opportunities.Remove(npcId);

        internal bool MatchesSnapshotDecisionContext(string npcId, NpcSocialContextKind kind, string planId,
            Guid meetingId, int spokenLines, double opportunityDayStart, bool wasDispatched)
        {
            if (string.IsNullOrWhiteSpace(npcId) || string.IsNullOrWhiteSpace(planId)
                || !_plans.TryGetValue(planId, out var plan)) return false;
            if (kind == NpcSocialContextKind.Opportunity)
            {
                if (plan.InitiatorId != npcId || meetingId != Guid.Empty || spokenLines != 0
                    || !ValidDay(opportunityDayStart, _world.TotalMinutes)
                    || !_offeredDays.TryGetValue(planId, out double offered) || offered != opportunityDayStart
                    || _world.TotalMinutes < opportunityDayStart + plan.InviteStartMinute
                    || _world.TotalMinutes >= opportunityDayStart + plan.InviteEndMinute
                    || _current.ContainsKey(npcId)) return false;
                return wasDispatched
                    ? !_opportunities.ContainsKey(npcId) && !_current.ContainsKey(plan.PartnerId)
                        && _world.GetState(npcId).ActiveActivity == null
                        && _world.GetState(plan.PartnerId).ActiveActivity == null
                    : _opportunities.TryGetValue(npcId, out var pending) && pending.Id == planId;
            }
            if (!_current.TryGetValue(npcId, out var meeting) || meeting.Id != meetingId
                || meeting.Plan.Id != planId || meeting.Transcript.Count != spokenLines) return false;
            if (kind == NpcSocialContextKind.Invitation)
                return meeting.State == NpcMeetingState.Invited && npcId == plan.PartnerId
                    && _world.TotalMinutes < meeting.DayStart + plan.InviteEndMinute;
            return (kind == NpcSocialContextKind.Conversation || kind == NpcSocialContextKind.Delivery)
                && meeting.State == NpcMeetingState.Talking && meeting.SpeakerId == npcId
                && _world.TotalMinutes < meeting.EndsAt
                && kind == (plan.ResourceTerms != null && meeting.DeliveryResultCode == null
                    ? NpcSocialContextKind.Delivery : NpcSocialContextKind.Conversation);
        }

        internal NpcSocialContext GetDecisionContinuationContext(string npcId, string planId, double opportunityDayStart)
        {
            if (string.IsNullOrWhiteSpace(planId) || !_plans.TryGetValue(planId, out var plan) || plan.InitiatorId != npcId
                || !ValidDay(opportunityDayStart, _world.TotalMinutes)
                || !_offeredDays.TryGetValue(planId, out double offered) || offered != opportunityDayStart
                || _world.TotalMinutes < opportunityDayStart + plan.InviteStartMinute
                || _world.TotalMinutes >= opportunityDayStart + plan.InviteEndMinute
                || _current.ContainsKey(plan.InitiatorId) || _current.ContainsKey(plan.PartnerId)
                || _world.GetState(plan.InitiatorId).ActiveActivity != null || _world.GetState(plan.PartnerId).ActiveActivity != null
                || (plan.ResourceTerms != null && !_resources.IsNeeded(plan.ResourceTerms))) return null;
            return new NpcSocialContext(NpcSocialContextKind.Opportunity, plan, npcId, Guid.Empty,
                opportunityDayStart + plan.MeetingStartMinute, opportunityDayStart + plan.InviteEndMinute,
                Array.Empty<NpcConversationLine>(), GetMemories(npcId).Reverse().Take(4).Reverse(),
                _resources?.Inspect(plan.ResourceTerms, npcId));
        }

        public void CancelAll()
        {
            foreach (var meeting in _current.Values.Distinct().ToArray()) Remove(meeting, NpcMeetingState.Cancelled);
            _opportunities.Clear();
        }

        public OperationResult Respond(NpcAgentSnapshot actor, Guid meetingId, bool accept)
        {
            string invalid = ValidateActor(actor);
            if (invalid != null) return OperationResult.Failure(invalid);
            if (!_current.TryGetValue(actor.NpcId, out var meeting) || meeting.Id != meetingId)
                return OperationResult.Failure("meeting.unknown");
            if (meeting.Plan.PartnerId != actor.NpcId || meeting.State != NpcMeetingState.Invited)
                return OperationResult.Failure("meeting.reply_invalid");
            if (_world.TotalMinutes >= meeting.DayStart + meeting.Plan.InviteEndMinute)
            {
                Remove(meeting, NpcMeetingState.Expired);
                return OperationResult.Failure("meeting.invitation_expired");
            }
            if (!accept)
            {
                Remove(meeting, NpcMeetingState.Declined);
                return OperationResult.Success();
            }
            if (meeting.Plan.ResourceTerms != null
                && _resources.Inspect(meeting.Plan.ResourceTerms, actor.NpcId).OwnedQuantity < meeting.Plan.ResourceTerms.Quantity)
            {
                Remove(meeting, NpcMeetingState.Cancelled);
                return OperationResult.Failure("inventory.insufficient_quantity");
            }
            if (IsBusy(meeting))
            {
                Remove(meeting, NpcMeetingState.Cancelled);
                return OperationResult.Failure("meeting.resident_busy");
            }
            if (_places.ContainsKey(meeting.Plan.PlaceId))
            {
                Remove(meeting, NpcMeetingState.Cancelled);
                return OperationResult.Failure("meeting.place_reserved");
            }
            meeting.StartsAt = Math.Max(_world.TotalMinutes, meeting.DayStart + meeting.Plan.MeetingStartMinute);
            meeting.EndsAt = meeting.StartsAt + meeting.Plan.DurationGameMinutes;
            if (_world.GetTargetAt(meeting.Plan.InitiatorId, meeting.StartsAt).ExpectedActivity != NpcActivity.Resting
                || _world.GetTargetAt(meeting.Plan.PartnerId, meeting.StartsAt).ExpectedActivity != NpcActivity.Resting)
            {
                Remove(meeting, NpcMeetingState.Cancelled);
                return OperationResult.Failure("meeting.schedule_conflict");
            }
            var initiator = _world.GetState(meeting.Plan.InitiatorId);
            var partner = _world.GetState(meeting.Plan.PartnerId);
            var first = new NpcActivityRequest(initiator.NpcId, initiator.WorldRunId, initiator.Revision,
                meeting.Plan.InitiatorLocationId, NpcActivity.Resting, meeting.EndsAt);
            var second = new NpcActivityRequest(partner.NpcId, partner.WorldRunId, partner.Revision,
                meeting.Plan.PartnerLocationId, NpcActivity.Resting, meeting.EndsAt);
            var accepted = _world.SubmitMeetingActivities(first, second, meeting.StartsAt);
            if (!accepted.IsSuccess)
            {
                Remove(meeting, NpcMeetingState.Cancelled);
                return accepted;
            }
            meeting.InitiatorActivity = first;
            meeting.PartnerActivity = second;
            meeting.State = NpcMeetingState.Scheduled;
            _places.Add(meeting.Plan.PlaceId, meeting);
            Remember(meeting, "meeting.accepted");
            return OperationResult.Success();
        }

        public bool IsPlaceReserved(string placeId) => _places.ContainsKey(placeId);

        public OperationResult EndConversation(NpcAgentSnapshot actor, Guid meetingId)
        {
            string invalid = ValidateSpeaker(actor, meetingId, out var meeting);
            if (invalid != null) return OperationResult.Failure(invalid);
            if (meeting.Plan.ResourceTerms != null && meeting.DeliveryResultCode != "resource.delivered")
                return OperationResult.Failure("resource.not_delivered");
            if (meeting.Transcript.Count < 2) return OperationResult.Failure("meeting.minimum_turns");
            Remove(meeting, NpcMeetingState.Completed);
            return OperationResult.Success();
        }

        public OperationResult Speak(NpcAgentSnapshot actor, Guid meetingId, string text)
        {
            string invalid = ValidateSpeaker(actor, meetingId, out var meeting);
            if (invalid != null) return OperationResult.Failure(invalid);
            if (meeting.Plan.ResourceTerms != null && meeting.DeliveryResultCode != "resource.delivered")
                return OperationResult.Failure("resource.not_delivered");
            if (string.IsNullOrWhiteSpace(text) || text.Length > 240)
                return OperationResult.Failure("meeting.speech_invalid");
            meeting.Transcript.Add(new NpcConversationLine(actor.NpcId, text.Trim(), _world.TotalMinutes));
            Remember(meeting, "meeting.spoken", actor.NpcId, text.Trim());
            if (meeting.Transcript.Count >= meeting.Plan.MaxTurns) Remove(meeting, NpcMeetingState.Completed);
            else meeting.SpeakerId = actor.NpcId == meeting.Plan.InitiatorId ? meeting.Plan.PartnerId : meeting.Plan.InitiatorId;
            return OperationResult.Success();
        }

        private string ValidateSpeaker(NpcAgentSnapshot actor, Guid meetingId, out Meeting meeting)
        {
            meeting = null;
            string invalid = ValidateActor(actor);
            if (invalid != null) return invalid;
            if (!_current.TryGetValue(actor.NpcId, out meeting) || meeting.Id != meetingId) return "meeting.unknown";
            if (meeting.State != NpcMeetingState.Talking || meeting.SpeakerId != actor.NpcId) return "meeting.turn_invalid";
            if (_world.TotalMinutes >= meeting.EndsAt) { Remove(meeting, NpcMeetingState.Expired); return "meeting.expired"; }
            if (_presence(meeting.Plan.InitiatorId, meeting.Plan.InitiatorLocationId) != NpcMeetingPresence.Arrived
                || _presence(meeting.Plan.PartnerId, meeting.Plan.PartnerLocationId) != NpcMeetingPresence.Arrived) return "meeting.not_arrived";
            return null;
        }

        public NpcMeetingSnapshot GetLatest(string npcId)
            => GetCurrent(npcId) ?? (_latest.TryGetValue(npcId, out var latest) ? latest : null);

        public IReadOnlyList<NpcMeetingMemory> GetMemories(string npcId)
            => _memories.TryGetValue(npcId, out var memory) ? Array.AsReadOnly(memory.ToArray()) : Array.Empty<NpcMeetingMemory>();

        public void Observe()
        {
            if (_plans.Count == 0) return;
            Guid liveRun = _world.GetState(_plans.Values.First().InitiatorId).WorldRunId;
            if (_worldRunId != liveRun)
            {
                _current.Clear();
                _places.Clear();
                _latest.Clear();
                _memories.Clear();
                _offeredDays.Clear();
                _opportunities.Clear();
                _worldRunId = liveRun;
                return;
            }
            foreach (var meeting in _current.Values.Distinct().ToArray())
            {
                if (IsBusy(meeting)) { Remove(meeting, NpcMeetingState.Cancelled); continue; }
                if (meeting.State == NpcMeetingState.Invited)
                {
                    if (_world.TotalMinutes >= meeting.DayStart + meeting.Plan.InviteEndMinute)
                        Remove(meeting, NpcMeetingState.Expired);
                    continue;
                }
                if (_world.TotalMinutes >= meeting.EndsAt)
                {
                    Remove(meeting, NpcMeetingState.Expired);
                    continue;
                }
                if (!ReferenceEquals(_world.GetState(meeting.Plan.InitiatorId).ActiveActivity, meeting.InitiatorActivity)
                    || !ReferenceEquals(_world.GetState(meeting.Plan.PartnerId).ActiveActivity, meeting.PartnerActivity))
                {
                    Remove(meeting, NpcMeetingState.Cancelled);
                    continue;
                }
                if (_world.TotalMinutes < meeting.StartsAt) continue;
                var first = _presence(meeting.Plan.InitiatorId, meeting.Plan.InitiatorLocationId);
                var second = _presence(meeting.Plan.PartnerId, meeting.Plan.PartnerLocationId);
                if (first == NpcMeetingPresence.Blocked || second == NpcMeetingPresence.Blocked
                    || first == NpcMeetingPresence.Busy || second == NpcMeetingPresence.Busy
                    || (meeting.State == NpcMeetingState.Talking
                        && (first != NpcMeetingPresence.Arrived || second != NpcMeetingPresence.Arrived)))
                {
                    Remove(meeting, NpcMeetingState.Cancelled);
                    continue;
                }
                if (meeting.State == NpcMeetingState.Scheduled && _world.TotalMinutes >= meeting.StartsAt)
                    meeting.State = NpcMeetingState.Travelling;
                if (meeting.State == NpcMeetingState.Travelling
                    && first == NpcMeetingPresence.Arrived && second == NpcMeetingPresence.Arrived)
                {
                    meeting.State = NpcMeetingState.Talking;
                    meeting.SpeakerId = meeting.Plan.InitiatorId;
                    Remember(meeting, "meeting.arrived");
                }
            }
            OfferOpportunities();
        }

        private bool IsBusy(Meeting meeting)
            => _presence(meeting.Plan.InitiatorId, meeting.Plan.InitiatorLocationId) == NpcMeetingPresence.Busy
                || _presence(meeting.Plan.PartnerId, meeting.Plan.PartnerLocationId) == NpcMeetingPresence.Busy;

        private void OfferOpportunities()
        {
            double day = Math.Floor(_world.TotalMinutes / 1440) * 1440;
            foreach (var plan in _plans.Values)
            {
                if (plan.ResourceTerms != null && !_resources.IsNeeded(plan.ResourceTerms)) continue;
                if (_offeredDays.TryGetValue(plan.Id, out double offered) && offered == day) continue;
                if (_world.TotalMinutes < day + plan.InviteStartMinute || _world.TotalMinutes >= day + plan.InviteEndMinute) continue;
                if (_current.ContainsKey(plan.InitiatorId) || _current.ContainsKey(plan.PartnerId)) continue;
                var initiator = _world.GetState(plan.InitiatorId);
                if (initiator.ActiveActivity != null || (plan.ResourceTerms == null && initiator.Target.ExpectedActivity != NpcActivity.Resting)
                    || _world.GetState(plan.PartnerId).ActiveActivity != null) continue;
                _offeredDays[plan.Id] = day;
                _opportunities[plan.InitiatorId] = plan;
                _world.NotifyMeeting(plan.InitiatorId, NpcAgentEventKind.SocialOpportunity);
            }
        }

        private void Remove(Meeting meeting, NpcMeetingState state)
        {
            meeting.State = state;
            meeting.SpeakerId = null;
            Remember(meeting, "meeting." + state.ToString().ToLowerInvariant());
            foreach (var request in new[] { meeting.InitiatorActivity, meeting.PartnerActivity })
            {
                if (request == null) continue;
                var live = _world.GetState(request.NpcId);
                if (ReferenceEquals(live.ActiveActivity, request))
                    _world.CancelActivity(live.NpcId, live.WorldRunId, live.Revision);
            }
            var snapshot = meeting.Snapshot;
            _latest[meeting.Plan.InitiatorId] = snapshot;
            _latest[meeting.Plan.PartnerId] = snapshot;
            _current.Remove(meeting.Plan.InitiatorId);
            _current.Remove(meeting.Plan.PartnerId);
            if (_places.TryGetValue(meeting.Plan.PlaceId, out var owner) && ReferenceEquals(owner, meeting))
                _places.Remove(meeting.Plan.PlaceId);
        }

        private void Remember(Meeting meeting, string kind, string speakerId = null, string text = null)
        {
            foreach (string npc in new[] { meeting.Plan.InitiatorId, meeting.Plan.PartnerId })
            {
                if (!_memories.TryGetValue(npc, out var memory))
                {
                    memory = new List<NpcMeetingMemory>();
                    _memories.Add(npc, memory);
                }
                if (memory.Count == 16) memory.RemoveAt(0);
                string partner = npc == meeting.Plan.InitiatorId ? meeting.Plan.PartnerId : meeting.Plan.InitiatorId;
                memory.Add(new NpcMeetingMemory(meeting.Id, kind, partner, _world.TotalMinutes, speakerId, text));
                _world.NotifyMeeting(npc, NpcAgentEventKind.MeetingChanged);
            }
        }

        private string ValidateActor(NpcAgentSnapshot actor)
        {
            if (actor == null) return "meeting.actor_invalid";
            var live = _world.GetState(actor.NpcId);
            if (live.WorldRunId != actor.WorldRunId || live.WorldRunId != _worldRunId) return "agent.world_stale";
            return live.Revision == actor.Revision ? null : "agent.decision_stale";
        }
    }
}

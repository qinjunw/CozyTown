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
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (resources != null && !ReferenceEquals(resources, _resources))
            {
                if (_plans.Count > 0 && world.GetState(_plans.Values.First().InitiatorId).WorldRunId == _worldRunId)
                    throw new InvalidOperationException("Changing resource ownership requires a new world timeline.");
                foreach (var plan in _plans.Values.Where(plan => plan.ResourceTerms != null))
                    if (resources.Inspect(plan.ResourceTerms, plan.InitiatorId) == null || resources.Inspect(plan.ResourceTerms, plan.PartnerId) == null)
                        throw new ArgumentException("Resource plans require assets owned by both participants.");
                _resources = resources;
            }
            _world = world ?? throw new ArgumentNullException(nameof(world));
            Observe();
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

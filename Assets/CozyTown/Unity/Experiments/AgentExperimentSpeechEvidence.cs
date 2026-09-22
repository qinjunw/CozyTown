using System;
using System.Collections.Generic;
using CozyTown.Runtime.NpcAgents;

namespace CozyTown.Unity.Experiments
{
    [Serializable]
    public sealed class AgentExperimentSpeechEvidence
    {
        public int outcomeIndex, turnIndex = -1;
        public string worldRunId, decisionId, npcId, meetingId, runMode;
        public double gameMinutes;
        public string text, source, missingReason;
    }

    public static class AgentExperimentSpeechEvidenceCollector
    {
        // Call immediately after Step. A committed transcript is not evidence that every UI frame displayed it.
        public static IReadOnlyList<AgentExperimentSpeechEvidence> Capture(AgentExperimentSession session, int outcomeStartIndex)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            var outcomes = session.Outcomes;
            if (outcomeStartIndex < 0 || outcomeStartIndex > outcomes.Count)
                throw new ArgumentOutOfRangeException(nameof(outcomeStartIndex));
            var rows = new List<AgentExperimentSpeechEvidence>();
            for (int index = outcomeStartIndex; index < outcomes.Count; index++)
            {
                var outcome = outcomes[index];
                if (outcome.Code != "meeting.say") continue;
                var row = new AgentExperimentSpeechEvidence {
                    outcomeIndex = index, worldRunId = outcome.WorldRunId.ToString("N"), decisionId = outcome.DecisionId.ToString("N"),
                    npcId = outcome.NpcId, meetingId = outcome.Reply?.MeetingId.ToString("N"), runMode = session.RunMode };
                row.missingReason = CaptureText(session, outcome, row);
                rows.Add(row);
            }
            return rows.AsReadOnly();
        }

        private static string CaptureText(AgentExperimentSession session, NpcDecisionOutcome outcome, AgentExperimentSpeechEvidence row)
        {
            var context = outcome.Context?.Social;
            if (outcome.Reply?.Kind != NpcDecisionKind.Speak || context?.Transcript == null
                || context.MeetingId != outcome.Reply.MeetingId)
                return "speech.outcome_context_missing";
            row.turnIndex = context.Transcript.Count;
            if (session.Controller == null) return "speech.world_unavailable";
            try
            {
                var state = session.Controller.GetAgentState(outcome.NpcId);
                if (state == null) return "speech.world_unavailable";
                if (state.WorldRunId != outcome.WorldRunId) return "speech.world_changed";
                var meeting = session.Controller.GetMeeting(outcome.NpcId);
                if (meeting == null || meeting.Id != outcome.Reply.MeetingId || meeting.Transcript == null)
                    return "speech.meeting_unavailable";
                if (row.turnIndex >= meeting.Transcript.Count) return "speech.turn_unavailable";
                var line = meeting.Transcript[row.turnIndex];
                if (line == null || line.SpeakerId != outcome.NpcId || string.IsNullOrEmpty(line.Text))
                    return "speech.turn_mismatch";
                if (outcome.ExecutionObservation != null && line.TotalMinutes != outcome.ExecutionObservation.ObservedAtTotalMinutes)
                    return "speech.execution_time_mismatch";
                row.gameMinutes = line.TotalMinutes;
                row.text = line.Text;
                row.source = "committed_transcript";
                return null;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return "speech.world_unavailable";
            }
        }
    }
}

using System;

namespace CozyTown.Runtime.NpcAgents
{
    public sealed class NpcCandidateException : FormatException
    {
        public NpcCandidateException(string code) : base(code)
        {
            if (!IsKnownCode(code)) throw new ArgumentOutOfRangeException(nameof(code));
            Code = code;
        }

        public string Code { get; }
        public bool CanCorrect => IsCorrectable(Code);

        public static bool IsKnownCode(string code) => IsCorrectable(code)
            || code == "candidate.schema_mismatch" || code == "candidate.operation_unavailable"
            || code == "candidate.plan_id_mismatch" || code == "candidate.meeting_id_mismatch"
            || code == "candidate.location_unknown" || code == "candidate.expression_mode_mismatch";

        private static bool IsCorrectable(string code) => code == "candidate.plan_id_required"
            || code == "candidate.location_id_required" || code == "candidate.meeting_id_required"
            || code == "candidate.meeting_id_invalid" || code == "candidate.activity_invalid"
            || code == "candidate.duration_invalid" || code == "candidate.text_invalid"
            || code == "candidate.speech_frame_invalid";
    }
}

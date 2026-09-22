using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using CozyTown.Runtime.NpcLife;

namespace CozyTown.Runtime.NpcAgents
{
    [DataContract]
    public sealed class NpcAgentWorldSnapshot
    {
        [DataMember(Name = "totalMinutes", IsRequired = true)] private readonly double _totalMinutes;
        [DataMember(Name = "schedules", IsRequired = true)] private readonly NpcDailySchedule[] _schedules;
        [DataMember(Name = "residents", IsRequired = true)] private readonly NpcResidentStateSnapshot[] _residents;

        public NpcAgentWorldSnapshot(double totalMinutes, IEnumerable<NpcDailySchedule> schedules,
            IEnumerable<NpcResidentStateSnapshot> residents)
        {
            _totalMinutes = totalMinutes;
            _schedules = schedules?.ToArray();
            _residents = residents?.ToArray();
        }

        public double TotalMinutes => _totalMinutes;
        public IReadOnlyList<NpcDailySchedule> Schedules => _schedules == null ? null : Array.AsReadOnly(_schedules);
        public IReadOnlyList<NpcResidentStateSnapshot> Residents => _residents == null ? null : Array.AsReadOnly(_residents);
    }

    [DataContract]
    public sealed class NpcResidentStateSnapshot
    {
        [DataMember(Name = "npcId", IsRequired = true)] private readonly string _npcId;
        [DataMember(Name = "revision", IsRequired = true)] private readonly long _revision;
        [DataMember(Name = "activity", IsRequired = true)] private readonly NpcActivitySnapshot _activity;
        [DataMember(Name = "events", IsRequired = true)] private readonly NpcAgentEvent[] _events;

        public NpcResidentStateSnapshot(string npcId, long revision, NpcActivitySnapshot activity,
            IEnumerable<NpcAgentEvent> events)
        {
            _npcId = npcId;
            _revision = revision;
            _activity = activity;
            _events = events?.ToArray();
        }

        public string NpcId => _npcId;
        public long Revision => _revision;
        public NpcActivitySnapshot Activity => _activity;
        public IReadOnlyList<NpcAgentEvent> Events => _events == null ? null : Array.AsReadOnly(_events);
    }

    [DataContract]
    public sealed class NpcActivitySnapshot
    {
        [DataMember(Name = "activityId", IsRequired = true)] private readonly Guid _activityId;
        [DataMember(Name = "targetLocationId", IsRequired = true)] private readonly string _targetLocationId;
        [DataMember(Name = "activity", IsRequired = true)] private readonly NpcActivity _activity;
        [DataMember(Name = "startsAtTotalMinutes", IsRequired = true)] private readonly double _startsAtTotalMinutes;
        [DataMember(Name = "expiresAtTotalMinutes", IsRequired = true)] private readonly double _expiresAtTotalMinutes;
        [DataMember(Name = "isMeetingActivity", IsRequired = true)] private readonly bool _isMeetingActivity;

        public NpcActivitySnapshot(Guid activityId, string targetLocationId, NpcActivity activity,
            double startsAtTotalMinutes, double expiresAtTotalMinutes, bool isMeetingActivity = false)
        {
            _activityId = activityId;
            _targetLocationId = targetLocationId;
            _activity = activity;
            _startsAtTotalMinutes = startsAtTotalMinutes;
            _expiresAtTotalMinutes = expiresAtTotalMinutes;
            _isMeetingActivity = isMeetingActivity;
        }

        public Guid ActivityId => _activityId;
        public string TargetLocationId => _targetLocationId;
        public NpcActivity Activity => _activity;
        public double StartsAtTotalMinutes => _startsAtTotalMinutes;
        public double ExpiresAtTotalMinutes => _expiresAtTotalMinutes;
        public bool IsMeetingActivity => _isMeetingActivity;
    }
}

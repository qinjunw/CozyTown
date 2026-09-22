using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using CozyTown.Runtime.NpcAgents;

namespace CozyTown.Runtime.Save
{
    [DataContract]
    public sealed class CompleteWorldSnapshot
    {
        [DataMember(Name = "contentConfiguration", IsRequired = true)] private readonly string _contentConfiguration;
        [DataMember(Name = "bodyConfiguration", IsRequired = true)] private readonly string _bodyConfiguration;
        [DataMember(Name = "world", IsRequired = true)] private readonly NpcAgentWorldSnapshot _world;
        [DataMember(Name = "meetingsEnabled", IsRequired = true)] private readonly bool _meetingsEnabled;
        [DataMember(Name = "meetings", IsRequired = true)] private readonly NpcMeetingBoardSnapshot _meetings;
        [DataMember(Name = "decisionsEnabled", IsRequired = true)] private readonly bool _decisionsEnabled;
        [DataMember(Name = "decisions", IsRequired = true)] private readonly NpcDecisionSchedulerSnapshot _decisions;
        [DataMember(Name = "residents", IsRequired = true)] private readonly NpcBodySnapshot[] _residents;
        [DataMember(Name = "player", IsRequired = true)] private readonly PlayerBodySnapshot _player;

        public CompleteWorldSnapshot(string contentConfiguration, string bodyConfiguration,
            NpcAgentWorldSnapshot world, bool meetingsEnabled, NpcMeetingBoardSnapshot meetings,
            bool decisionsEnabled, NpcDecisionSchedulerSnapshot decisions,
            IEnumerable<NpcBodySnapshot> residents, PlayerBodySnapshot player)
        {
            _contentConfiguration = contentConfiguration;
            _bodyConfiguration = bodyConfiguration;
            _world = world;
            _meetingsEnabled = meetingsEnabled;
            _meetings = meetings;
            _decisionsEnabled = decisionsEnabled;
            _decisions = decisions;
            _residents = residents?.ToArray();
            _player = player;
        }
        public string ContentConfiguration => _contentConfiguration;
        public string BodyConfiguration => _bodyConfiguration;
        public NpcAgentWorldSnapshot World => _world;
        public bool MeetingsEnabled => _meetingsEnabled;
        public NpcMeetingBoardSnapshot Meetings => _meetings;
        public bool DecisionsEnabled => _decisionsEnabled;
        public NpcDecisionSchedulerSnapshot Decisions => _decisions;
        public IReadOnlyList<NpcBodySnapshot> Residents => _residents == null ? null : Array.AsReadOnly(_residents);
        public PlayerBodySnapshot Player => _player;
    }
}

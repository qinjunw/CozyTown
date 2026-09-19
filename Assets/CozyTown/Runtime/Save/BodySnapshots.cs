using System;
using System.Runtime.Serialization;
using CozyTown.Runtime.NpcLife;

namespace CozyTown.Runtime.Save
{
    [DataContract]
    public sealed class Position2DSnapshot
    {
        [DataMember(Name = "x", IsRequired = true)] private readonly float _x;
        [DataMember(Name = "y", IsRequired = true)] private readonly float _y;
        public Position2DSnapshot(float x, float y) { _x = x; _y = y; }
        public float X => _x;
        public float Y => _y;
    }

    [DataContract]
    public sealed class TownRouteSnapshot
    {
        [DataMember(Name = "position", IsRequired = true)] private readonly Position2DSnapshot _position;
        [DataMember(Name = "facing", IsRequired = true)] private readonly Position2DSnapshot _facing;
        [DataMember(Name = "targetLocationId", IsRequired = true)] private readonly string _targetLocationId;
        [DataMember(Name = "waypoints", IsRequired = true)] private readonly Position2DSnapshot[] _waypoints;
        [DataMember(Name = "waypointIndex", IsRequired = true)] private readonly int _waypointIndex;
        [DataMember(Name = "status", IsRequired = true)] private readonly int _status;
        [DataMember(Name = "hasReplanned", IsRequired = true)] private readonly bool _hasReplanned;

        public TownRouteSnapshot(Position2DSnapshot position, Position2DSnapshot facing,
            string targetLocationId, Position2DSnapshot[] waypoints, int waypointIndex, int status, bool hasReplanned)
        {
            _position = position;
            _facing = facing;
            _targetLocationId = targetLocationId;
            _waypoints = waypoints == null ? null : (Position2DSnapshot[])waypoints.Clone();
            _waypointIndex = waypointIndex;
            _status = status;
            _hasReplanned = hasReplanned;
        }
        public Position2DSnapshot Position => _position;
        public Position2DSnapshot Facing => _facing;
        public string TargetLocationId => _targetLocationId;
        public Position2DSnapshot[] Waypoints => _waypoints == null ? null : (Position2DSnapshot[])_waypoints.Clone();
        public int WaypointIndex => _waypointIndex;
        public int Status => _status;
        public bool HasReplanned => _hasReplanned;
    }

    [DataContract]
    public sealed class NpcBodySnapshot
    {
        [DataMember(Name = "npcId", IsRequired = true)] private readonly string _npcId;
        [DataMember(Name = "route", IsRequired = true)] private readonly TownRouteSnapshot _route;
        [DataMember(Name = "activity", IsRequired = true)] private readonly NpcActivity _activity;
        [DataMember(Name = "noLegalPosition", IsRequired = true)] private readonly bool _noLegalPosition;
        public NpcBodySnapshot(string npcId, TownRouteSnapshot route, NpcActivity activity, bool noLegalPosition)
        { _npcId = npcId; _route = route; _activity = activity; _noLegalPosition = noLegalPosition; }
        public string NpcId => _npcId;
        public TownRouteSnapshot Route => _route;
        public NpcActivity Activity => _activity;
        public bool NoLegalPosition => _noLegalPosition;
    }

    [DataContract]
    public sealed class PlayerBodySnapshot
    {
        [DataMember(Name = "position", IsRequired = true)] private readonly Position2DSnapshot _position;
        [DataMember(Name = "facing", IsRequired = true)] private readonly Position2DSnapshot _facing;
        public PlayerBodySnapshot(Position2DSnapshot position, Position2DSnapshot facing)
        { _position = position; _facing = facing; }
        public Position2DSnapshot Position => _position;
        public Position2DSnapshot Facing => _facing;
    }
}

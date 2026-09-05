# T1 NPC daily navigation implementation

## Scope and status

Implementation started from `3a50bb4` for issues #31 and #32. The acceptance input is `TOWN_LIFE_PLAN.md` section 10 and ADR-0014. Issue #33 remains the joint regression and user-owned Scene-01 / Town-01 acceptance gate.

The current work adds deterministic daily movement to the existing town. It does not add live AI, NPC trading, automatic occupation production, opening hours, interior scenes, or actor-to-actor collision.

## Module boundaries

- `NpcLife` resolves each resident's configured activity periods and legal loading locations. It has no Unity dependency and does not claim arrival from a scheduled deadline.
- `TownMap2D` selects distance-weighted routes on the existing road graph. A point already travelling on a road remains the route's actual start position.
- `TownRouteFollower2D` consumes a distance budget, checks the resident's foot clearance against static world obstacles, and reports travelling, arrived, or blocked.
- `NpcWorldResident2D` combines a schedule with a journey and presents the resulting world presence.
- `CozyTownTownLifeController` receives accepted world-time progress. It prepares all resident results before publishing their transforms. Sleeping advances the existing journeys; successful loading reconstructs legal positions.

The world's integer-minute clock remains authoritative. The time progress output also carries the daytime coordinator's accepted fractional-minute remainder for smooth movement. NPCs do not run a second clock. The existing rate is 0.5 effective real seconds per game minute; the initial NPC speed is 2 world units per effective second.

The life controller subscribes for the lifetime of the bound world session, not for the enabled lifetime of its presentation component. Explicit sleep and loading must still update journeys while that component is disabled. Resident visibility and interaction obey the resident's own active state. Disabling the entire physical map requires pausing its clock; disabled colliders cannot provide navigation obstacles.

## Production routine configuration

The four-person slice uses the following per-resident configuration in `CozyTownTownLifeSceneUpgrader`. Arrival windows validate uninterrupted journeys and define legal loading stages; they do not teleport a late resident.

| Resident | Leave home | Morning arrival window ends | Rest starts | Afternoon starts | Return starts | Home arrival window ends |
| --- | --- | --- | --- | --- | --- | --- |
| Mina | 06:00 | 08:00 | 12:00 | 13:00 | 17:00 | 18:00 |
| Eli | 05:30 | 07:30 | 11:30 | 12:30 | 16:30 | 17:30 |
| Ren | 05:45 | 07:45 | 12:15 | 13:15 | 17:30 | 18:30 |
| Sora | 06:30 | 08:30 | 13:00 | 14:00 | 18:00 | 19:00 |

Mina uses the shop approach, Eli the farm edge, Ren the two designated pond banks, and Sora the kitchen approach. Each uses an owned residential entrance and a distinct rest location. The new-game clock is 06:00: Eli and Ren begin at their legal departure-window loading locations, rather than replaying the earlier morning. Full uninterrupted commute checks therefore use the following day.

## Test-first evidence

These are development test runs, not human acceptance results. Log and XML paths below are relative to the repository and are ignored build artifacts.

| Behavior | Observed RED | Observed GREEN |
| --- | --- | --- |
| Half-open personal schedule periods | `Logs/npc-schedule-red01.xml`: 8 failures in 14 cases | `Logs/npc-core-red02.xml`: all 14 schedule cases passed |
| Smooth accepted fractional time | `Logs/npc-core-red02.xml`: expected 360.5, actual 360 | `Logs/npc-core-red03.xml`: passed |
| Distance rather than road count | `Logs/npc-core-red02.xml`: selected the longer two-edge detour | `Logs/npc-core-red03.xml`: passed |
| Legal loading stage distinct from arrival | `Logs/npc-core-red03.xml`: 10 failures in 14 loading cases | `Logs/npc-schedule-red04.xml`: all 14 loading cases passed |
| Strictly future schedule boundary | `Logs/npc-schedule-red04.xml`: 14 failures | `Logs/npc-schedule-red05.xml`: all 14 boundary cases passed |
| Distance-budget movement | `Logs/npc-motion-red01.xml`: position stayed at start | `Logs/npc-motion-red02.xml`: passed |
| Accepted time drives resident motion | `Logs/npc-motion-red01.xml`: expected x=10, actual x=0 | `Logs/npc-motion-red02.xml`: passed |
| Mid-road target change | `Logs/npc-motion-red02.xml`: expected x=0.5, actual x=1 | `Logs/npc-motion-red07.xml`: all 8 route-follower cases passed |
| Personal schedule spanning midnight | `Logs/npc-schedule-red05.xml`: 10 failures in 12 shifted cases | `Logs/npc-art-red01.xml`: all 69 schedule cases passed |
| Stable identity after object rename | `Logs/npc-schedule-red05.xml`: upgrade created an eleventh interaction entity | `Logs/npc-edit-regression01.xml`: passed |
| Explicit world advance retains fractional movement | `Logs/npc-time-red08.xml`: expected 361.5, actual 361 | `Logs/npc-edit-regression01.xml`: passed |
| Rebinding the same clock preserves the current journey | `Logs/npc-motion-red07.xml`: resident moved back to the legal loading anchor | `Logs/npc-play-regression01.xml`: passed |
| Hidden resident clears its existing E bubble | `Logs/npc-motion-red07.xml`: bubble remained visible | `Logs/npc-play-regression01.xml`: passed |
| Disabled presentation retains the full sleep interval | `Logs/npc-session-red01.xml`: expected x=60.5, actual x=60 | `Logs/npc-animation-red01.xml`: passed |
| Load followed by movement while presentation is disabled | `Logs/npc-session-red01.xml`: expected x=2, actual x=0 | `Logs/npc-animation-red01.xml`: passed |

Mina traverses the real development-scene roads, pauses on focus loss, returns through her home entrance, hides at home, and leaves again the following morning in `DevelopmentScene_MinaTraversesActualRoadsAndReturnsHomeBeforeNextMorning`. This automated scenario passed in `Logs/npc-play-regression01.xml`.

The first whole-suite checkpoint recorded 432 EditMode cases (431 passed; the new Mina movement-sheet contract remained RED) and 92 PlayMode cases (91 passed; the new character foot-sorting rendering contract remained RED). These missing T1 art and sorting changes are subsequent slices, not waived acceptance conditions.

`Logs/npc-animation-red01.xml` also passed the three NPC-linked early-morning save cases (v1, v2, v3) and the real-scene 0.25-second speed check. Its only failure was the new animation adapter's first directional-sprite test.

## Delivery gate

Mina's time and scene integration is implemented. Four-person configuration and animation assets, character foot sorting, final whole-suite regression, independent review, and protected-branch delivery are still pending. Human Scene-01 and Town-01 remain unconfirmed.

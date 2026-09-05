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

`CozyTownNpcSpriteAnimator` displays four directional idle poses and two walking poses per direction at six frames per accepted effective second. It receives only committed journey progress. Stop, arrival, blocking and load reconstruction use idle poses; modal/focus pause and failed world operations preserve the current frame. The URP 2D renderer uses the Y sort axis, while player and NPC renderers share order 20 and bottom pivots. Building bases remain at order 5 and roof foregrounds at order 30.

Stationary work facing is separate from route heading. Only `Working` plus actual `Arrived` applies the configured work direction; travelling and blocked residents retain the follower's direction. Morning/afternoon directions are Mina left/left, Eli left/left, Ren right/left and Sora right/right, matching their work objects. Successful work-time loading applies the same directions. A zero configuration preserves route heading for existing generic fixtures.

### Obstructed reconstruction and arrival precision

A loaded resident first tries the configured phase location. If its foot circle overlaps a world obstacle, reconstruction tries the owned doorstep, entrance, morning work, rest and afternoon work points in that fixed order. The original scheduled target remains unchanged, and the actual route determines travelling or blocked state. This is an obstruction fallback, not a change to the normal loading table.

If every configured placement is obstructed, the level has no supported spawn location for that resident. The adapter keeps the last transform only as inactive presentation state, reports `Blocked` and `IsHome=false`, hides body and interaction, and warns once per incident. Ordinary time continues without moving that unavailable presentation; a later successful load with a clear configured point can restore it. The adapter does not throw after the world save has already committed or present an actor inside the obstacle.

Path following consumes the movement budget before considering a final rounding tail of at most `0.00001` world units. Completing that tail requires the same foot-clearance and swept-obstacle checks as other movement. An already reached waypoint is consumed before direction normalization, avoiding a zero-length division and invalid facing.

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
| Four resident configurations | `Logs/npc-four-anchor-red02.xml`: expected 4 residents, actual 1 | `Logs/npc-edit-regression02.xml`: passed |
| T1 movement sheets and owned home artwork | `Logs/npc-t1-art-scene-red01.xml`: 15 missing asset contracts and 2 scene bindings failed | `Logs/npc-edit-regression02.xml`: all 15 T1 asset contracts and both scene bindings passed |
| Player/NPC foot depth rendering | `Logs/npc-play-regression01.xml`: lower-foot character rendered behind the other actor | `Logs/npc-play-regression02.xml`: both overlap directions passed |
| Directional animation follows accepted time | `Logs/npc-animation-red02.xml`: stop/rebuild and resident integration failed | `Logs/npc-review-red02.xml`: all 8 animation cases passed |
| Load anchor occupied by a new obstacle | `Logs/npc-review-red02.xml`: the loaded resident overlapped the work-point wall | `Logs/npc-review-red03.xml`: work-point and doorstep obstruction cases passed |
| Rounded small steps preserve arrival and finite facing | `Logs/npc-review-red02.xml`: 60 small steps followed by another advance produced a NaN direction | `Logs/npc-review-red03.xml`: arrived/right and finite position/direction passed |
| Every configured loading location occupied | `Logs/npc-review-red03.xml`: the unavailable resident remained presented inside an obstacle | `Logs/npc-final-playmode03.xml`: hidden, blocked, non-interactable until a successful legal reconstruction |
| Short-route floating-point tail | `Logs/npc-rounding-red01.xml`: the 2-unit route remained travelling after 60 equal steps | `Logs/npc-final-playmode03.xml`: both 2-unit and 20-unit routes arrived with finite right-facing direction |
| Ren standing-pose height | `Logs/npc-final-editmode.xml`: visible top y29 instead of the required y30 | `Logs/npc-final-editmode03.xml`: y30 after a connected cap-crown correction; other 55 native cells unchanged |
| Arrived residents face their work objects | `Logs/npc-work-facing-red01.xml`: Mina expected left, actual down | `Logs/npc-final-playmode03.xml`: four residents face their work objects after morning/afternoon arrival and loading; travelling/blocked Ren retains route heading |

Mina traverses the real development-scene roads, pauses on focus loss, returns through her home entrance, hides at home, and leaves again the following morning in `DevelopmentScene_MinaTraversesActualRoadsAndReturnsHomeBeforeNextMorning`. This automated scenario passed in `Logs/npc-play-regression01.xml`.

The first whole-suite checkpoint recorded 432 EditMode cases (431 passed; the new Mina movement-sheet contract remained RED) and 92 PlayMode cases (91 passed; the new character foot-sorting rendering contract remained RED). These missing T1 art and sorting changes are subsequent slices, not waived acceptance conditions.

`Logs/npc-animation-red01.xml` also passed the three NPC-linked early-morning save cases (v1, v2, v3) and the real-scene 0.25-second speed check. Its only failure was the new animation adapter's first directional-sprite test.

`Logs/npc-animation-red02.xml` passed the complete four-resident day and two actual Bed comparisons against minute-step progression. The evening comparison ends at 18:01 with Sora still returning; the overnight comparison ends at Day 2 05:31 with Eli just departing. Both compare all four positions, destinations, travel states, facing and presence. This run still had three expected animation RED cases: stop/reset and resident integration.

### Measured uninterrupted commutes

The real development scene was advanced through `DaytimeClockDriver` at 0.5 effective seconds per game minute. Arrival was sampled once per game minute, so the observed duration can exceed the exact travel duration by less than 0.5 effective seconds. Measurements are logged in `Logs/npc-animation-red02.log`.

| Resident | Journey | Road length (world units) | Observed effective seconds | Observed game minutes | Configured window (game minutes) |
| --- | --- | ---: | ---: | ---: | ---: |
| Mina | Outbound / return | 18.104 | 9.5 | 19 | 120 / 60 |
| Eli | Outbound / return | 26.200 | 13.5 | 27 | 120 / 60 |
| Ren | Outbound | 14.454 | 7.5 | 15 | 120 |
| Ren | Return from afternoon bank | 18.354 | 9.5 | 19 | 60 |
| Sora | Outbound / return | 10.750 | 5.5 | 11 | 120 / 60 |

These measurements support the 2-unit walking speed without changing the approved world-time rate or the player's speed. They are automated timing evidence, not a human assessment of walking pace.

### Actual scene camera evidence

`TownLifeSceneVisualEvidencePlayModeTests` advances the actual development scene through `DaytimeClockDriver`, pauses it, moves the player to an observation position and captures the existing scene camera. It does not replace the town layout, NPC art or camera settings with a mock scene. The 960×540 images contain the world camera, not the screen-overlay HUD:

- `Logs/town-life-day1-0603-departure.png`: Mina travelling through the residential street.
- `Logs/town-life-day1-0830-work.png`: four residents at their configured work locations.

The test passed in `Logs/npc-review-red03.xml`; the main agent also inspected both images. That run passed 42 of 43 cases, with only the new all-reconstruction-locations-occupied case still RED. These captures do not establish human Scene-01 or Town-01 acceptance.

## Final automated regression

| Run | Result | Evidence |
| --- | --- | --- |
| Full EditMode | 450 passed, 0 failed, 0 skipped | `Logs/npc-final-editmode03.xml` and `.log` |
| Full graphics-enabled PlayMode | 115 passed, 0 failed, 0 skipped | `Logs/npc-final-playmode03.xml` and `.log` |
| Repeated T1 compilation | All six Production PNGs and six 4× previews retained identical SHA-256 hashes | `Logs/npc-t1-art-build03.log`, `Logs/npc-t1-art-build04.log` |

The final suites include all earlier RED cases, the 15 T1 asset contracts, all four identity-color checks, eight uninterrupted real-map commutes, both sleep-versus-step comparisons, v1/v2/v3 early-morning load integration, UI and world upgrade orders, and actual rendered character overlap. Both Unity test processes finished; the repeated compiler exited with code 0. No standalone player build was produced in this slice.

The source baseline was `3a50bb4`. The final tested scene SHA-256 is `434101DC3DA8EE3B8B9EC4F97E0764B2E520F9A6C48236BC9715819AFB4699CF`. The final SPEC reviewer inspected the same follower, resident and animator files tested by the main agent:

| File | SHA-256 |
| --- | --- |
| `TownRouteFollower2D.cs` | `270543B34ABFBD42284AF08BB18251889D72596782C87A6094FB810854DC0CD9` |
| `NpcWorldResident2D.cs` | `099EEFC19DAF3F11D315E21F75DE6AA07B5C75C469319F98EC9448D9AEDFCA90` |
| `CozyTownNpcSpriteAnimator.cs` | `0EAE06E107542B5D95D95363A3713D2DBC766CE986995E25BB259457513E8142` |

The independent reviews identified three SPEC P2 findings: occupied reconstruction placement, invalid facing after rounded waypoint arrival, and stationary work facing. All three and the subsequent short-route rounding-tail check are resolved. The final standards review found no actionable code findings in the assigned resident, follower, animation, scene-upgrade and public-test changes; the final SPEC review found no remaining requirement gaps in that scope. Reviewers performed static inspection; the main agent ran Unity and inspected the scene captures. Unity-generated platform settings and scene-template files are excluded from this delivery.

## Delivery gate

Four-person routines, owned-home artwork, directional animation, character foot sorting and automated regression are ready for manual playtesting. Issue #33 remains open until the user separately records Scene-01 and Town-01 results using `ART_ACCEPTANCE.md` section 15. The live AI endpoint stays disabled.

Open `Assets/CozyTown/Scenes/CozyTown_Dev.unity` and enter Play Mode. A new game starts at 06:00; walk north to observe the residential street. Mina departs immediately, Eli and Ren continue from their configured departure-window load locations, and Sora departs at 06:30. Keep focus and close modal panels for normal walking. A bed sleep advances journeys through intervening schedule boundaries; it is not a substitute for watching uninterrupted arrival and animation. At home, residents hide only after reaching their entrance, and have no active conversation target until they leave.

# Implement conserved character–shop economy

- Label: `wayfinder:map`
- Status: Open
- Tracker: https://github.com/qinjunw/CozyTown/issues/13
- Decision map: [守恒型角色—商店经济重构](map.md)

## Destination

Replace the global player-only economy with stable character and shop ownership, conserved buy/sell transfers, deterministic daily stock replacement, and schema v2 persistence while preserving the existing production loop.

## Sequence

1. [Add stable character and shop economy state storage](https://github.com/qinjunw/CozyTown/issues/14) — Complete; targeted `2/2` and full EditMode `203/203` passed.
2. [Make purchases transfer character and shop assets atomically](https://github.com/qinjunw/CozyTown/issues/15) — Complete; targeted `11/11` and full EditMode `214/214` passed.
3. [Make sales transfer character and shop assets atomically](https://github.com/qinjunw/CozyTown/issues/16) — Complete; targeted success `1/1`, rejection matrix `9/9`, and full EditMode `224/224` passed.
4. [Expose stock-aware shop trading projections](https://github.com/qinjunw/CozyTown/issues/17) — Complete; targeted `4/4` and full EditMode `228/228` passed.
5. [Replace shop stock deterministically for each new day](https://github.com/qinjunw/CozyTown/issues/18) — Complete; targeted `37/37` and full EditMode `236/236` passed.
6. [Publish shop restock atomically with the day transition](https://github.com/qinjunw/CozyTown/issues/19) — Complete; targeted `22/22`, full EditMode `244/244`, and full PlayMode `35/35` passed.
7. [Migrate main-slot saves from schema v1 to schema v2](https://github.com/qinjunw/CozyTown/issues/20) — Complete; targeted `38/38`, full EditMode `260/260`, and full PlayMode `35/35` passed.
8. [Wire Unity shop flows to stable character and shop identities](https://github.com/qinjunw/CozyTown/issues/21) — Complete; Presenter `7/7`, Unity View `2/2`, scene save/load slice `1/1`, full EditMode `249/249`, and full PlayMode `35/35` passed.
9. [Verify conserved economy and migration regressions](https://github.com/qinjunw/CozyTown/issues/22) — Ready.

## Constraints

- Runtime remains independent of UnityEngine.
- Each ticket advances one RED→GREEN public behavior at a time.
- Existing behavior remains wired until its replacement slice passes.
- Static definitions stay in read-only configuration; dynamic assets stay in character and shop state.
- Price fluctuation, relationship discounts, autonomous NPC schedules, AI-triggered transactions, multiple save slots, networking, and a database remain out of scope.

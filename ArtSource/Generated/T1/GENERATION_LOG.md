# T1 image generation record

## Selected sources and native processing

All eight PNGs in this directory were produced with the built-in imagegen tool on 2026-09-06. No external image API or image-generation CLI was used. The selected generated pictures are source artwork, not Production sheets. `ArtSource/Authored/T1/NormalizeT1Sprites.ps1` converts them to the exact palette and native canvas consumed by the existing authored-cell compiler seam.

| Generated file | Built-in output identifier | Selection and observed issue |
| --- | --- | --- |
| `npc_shopkeeper_mina_source_v01.png` | `exec-85ab1c14-e7cb-4bad-bb06-5407b7774450.png` | Selected. Twelve complete bodies; right profiles have a larger silhouette than left profiles. |
| `npc_shopkeeper_mina_source_v02.png` | `exec-b2036efb-e1f5-4c7e-b5b6-f7424d2aea38.png` | Rejected edit. Requested matching side profiles, but the right row remained larger and the output contained an opaque checkerboard. |
| `npc_shopkeeper_mina_source_v03.png` | `exec-1387cf45-c5df-4583-92fe-88ddd5a1aadf.png` | Rejected edit. Requested actual alpha transparency; corner alpha remained 255 and the checkerboard remained painted. |
| `npc_farmer_eli_source_v01.png` | `exec-7e314cb9-712b-4ee2-bd7d-f94bdb0e8a19.png` | Selected. Straw hat, green band, green sleeves and teal overalls remain visible. |
| `npc_fisher_ren_source_v01.png` | `exec-694d7229-fddf-4b44-aa1d-785b97cf719e.png` | Selected. Navy cap, cyan patch, blue sleeves and tan vest remain visible. |
| `npc_cook_sora_source_v01.png` | `exec-1fc40140-854a-41da-af46-a5e1bf0c2796.png` | Selected. Chef hat and orange hair remain visible; the outside neutral checkerboard requires removal. |
| `npc_homes_source_v01.png` | `exec-31227717-20ff-4578-ad19-468edbd2cd61.png` | Ren and Eli cells selected. Complete buildings, but doorway positions differ from the fixed native x38 anchor; outside checkerboard is opaque. |
| `npc_homes_source_v02.png` | `exec-871a0fe6-9fdf-415a-a76c-17f26bc7d504.png` | Mina and Sora cells selected after doorway-left edit. Ren's edited door is farther left than needed; v01 is retained for Ren and Eli. Outside checkerboard remains opaque. |

The initial Mina prompt appears below. The three other character prompts are in `NPC_PROMPTS.md`; both house prompts are in `HOME_PROMPTS.md`. The two rejected Mina edits are retained for provenance; the table records their requested changes rather than reconstructing unavailable verbatim prompts.

### Native NPC normalization

- The source's four occupied horizontal bands determine rows. Equal-height source quadrants were rejected during local review because irregular source spacing let a neighboring row's hat enter a crop.
- All selected poses of one NPC share one scale. Each pose is horizontally centered and translated to the same complete-boot baseline; there is no independent per-frame resize.
- With explicit authorization, each complete native left frame is mirrored to form its matching right frame. This removes the source's left/right size drift without redrawing identity details.
- The output is 48 authored 24×32 cells, top-to-bottom down/left/right/up and idle/walk00/walk01. Every cell uses the existing WarmRural32 palette and `.` transparency.
- Read-only native checks found 30–31 opaque rows, at most one row of height variation per NPC, x bounds within 2–21, centers within 11–12, at least four opaque boot pixels on the bottom row, and no opaque pixels disconnected from the feet in all 48 cells. Left/right silhouette overlap is exactly 1 after mirroring. The bottom eight rows differ between walking phases in all 16 directional pairs.
- The four 8× nearest-neighbor review sheets were inspected and retained as the T1 candidates. Mina's chestnut bob/brown apron, Eli's straw hat/green sleeves, Ren's navy cap/blue jacket, and Sora's chef hat/orange hair distinguish the four residents. No portrait crop substitutes for a full body.

| NPC | Common source-to-native scale | Visible native height |
| --- | --- | --- |
| Mina | 0.09064327 | 31 |
| Eli | 0.08310992 | 30–31 |
| Ren | 0.07769424 | 30–31 |
| Sora | 0.08201058 | 30–31 |

### Native house normalization

The four selected house cells share scale 0.10270270. Each source door center is explicitly placed at native x38, and each complete foundation is grounded at native y0. Source door-center x coordinates are 363, 1018, 378 and 1018 for Mina v02, Ren v01, Sora v02 and Eli v01 respectively. The opaque entry pixel at x38/y4 was checked in each 64×64 cell.

Mina retains brown window frames and supply crates; Ren retains blue frames and fishing gear; Sora retains red frames and an ingredient basket; Eli retains green frames and planted boxes. The native review shows the four color/prop pairs, roofs, chimneys, complete foundations and unobstructed front doors. The generated lower-row houses have less visible height than the upper-row houses; the normalization preserves the common scale rather than stretching them independently. All canvases remain 64×64.

Each roof cell is derived from its positioned building cell: the top 26 rows are copied unchanged and the bottom 38 rows are transparent. Roofs are not resized or grounded separately. The script emits four full buildings and four matching roof cells. Production import, 4× previews and scene head-clearance acceptance are run separately by the coordinating task; native-source checks do not establish those results.

### Outputs and reproduction

- Native sources: `ArtSource/Authored/T1/Characters/<owner>/npc_<owner>_<pose>.pixels` and `ArtSource/Authored/T1/Buildings/bld_home_<owner>[_roof_foreground].pixels`.
- Native and 8× review images: `ArtSource/Authored/T1/Review/`. These are normalization review files, not the Production compiler's `ArtSource/Previews/T1/` output.
- Reproduction commands and the pixel format are in `ArtSource/Authored/T1/README.md`.
- `CozyTownT1PixelArtBatchCompiler.Build` consumes all 56 native cells through `authoredCellSourcePaths`, avoiding the generated-source per-cell fit path. Its independent batch produces six PNGs without modifying A1 resources.

### Unity evidence

- Before compilation, `Logs/npc-t1-art-scene-red01.xml` contains 15 T1 art cases: 0 passed, 15 failed, 0 skipped. Each failed because a required T1 file, importer or Sprite set was absent. The combined run also contains non-art scene cases; its total is not the art-case count.
- `Logs/npc-t1-art-build01.log` records all six Production outputs and exits with code 0. The compiled Mina, house and roof 4× previews were inspected: pose order and house placement match the native reviews, and roof overlays retain their original canvas position.
- `Logs/npc-edit-regression02.xml` reports all 15 T1 art cases passed. Its separate existing world/portrait identity check stopped at Mina's palette coverage, which required the correction below. Scene acceptance is tracked separately from these asset tests.

### Portrait-color regression and correction

The existing scene identity check measures distinct shared colors divided by the corresponding portrait's distinct opaque colors. Read-only inspection of the first compiled idle-down cells found:

| NPC | Initial shared / portrait colors | Initial coverage | Opaque body pixels | Corrected native coverage |
| --- | --- | --- | --- | --- |
| Mina | 12 / 18 | 0.6667 | 430 | 14 / 18 = 0.7778 |
| Eli | 12 / 21 | 0.5714 | 404 | 15 / 21 = 0.7143 |
| Ren | 13 / 20 | 0.6500 | 368 | 14 / 20 = 0.7000 |
| Sora | 16 / 18 | 0.8889 | 379 | unchanged |

Majority-color sampling removed intermediate colors: Mina lost gray-brown cloth/hair shadows (`6F5A4A`) and warm skin transitions (`D28A48`, `C98256`, `8C4F32`); Eli lost warm hat/skin shadows (`8C4F32`) and peach skin transitions; Ren lost `8C4F32` face/vest transitions and lighter eye/skin shades. All four already exceeded the existing 320-pixel body-occupancy requirement.

The approved correction applies one deterministic rule across all 12 corresponding frames of Mina, Eli and Ren: use the RGB mean of each warm-colored source sampling block before mapping to the existing palette. Keep dark outlines, blue/green materials, ivory eye highlights and the gold highlight on the original majority-color path. For Eli, map the exposed face and hand orange shades to the portrait's `C98256` shadow, `F0B47A` midtone and `FFD3A1` highlight; the hat uses its original orange material ramp. The face range is native top-down rows 10–17 for front/side directions, and hands use rows 20–25. This is a material-color change, not added pixels.

The three corrected 8× sheets were inspected: eyes, hair/hat, sleeves, apron/overalls and stepping poses remain readable. Across all 9,216 pixels of each complete NPC sheet, the old and corrected alpha masks have zero differences. Occupancy, height, positions, complete feet and side mirroring are unchanged. Sora, the four houses and all A1 portraits remain unmodified. Temporary color comparisons were moved to ignored `Logs/t1-art-palette-trials-delivery/`; the versioned inputs are the selected authored cells, normalization script and original generated sources.

The corrected coverage values above were measured from native review pixels against the actual A1 portrait PNG. Unity recompile and the existing identity regression still require a subsequent run; these native measurements do not substitute for that result.

### Ren standing cap-crown correction

`Logs/npc-final-editmode.xml` subsequently passed the color checks and reached the existing full-body occupancy condition for Ren: his idle-down cell had max opaque y29, below the required y30. The T1 animation envelope accepts visible heights 30–31, but the existing standing-identity gate requires 31 visible rows. The other standing cells already satisfied the gate, including Sora.

Only Ren's idle-down cap crown changed. Five contiguous pixels at native x10–14/top-down y1 extend the rounded dark crown; the five pixels immediately beneath become the existing navy cap fill, retaining a one-pixel outline. No isolated pixels, per-frame resizing or foot translation was used. The other eleven Ren poses and all 44 remaining authored cells retain their prior hashes. The operation is reproduced by `NormalizeT1Sprites.ps1` and requires its expected empty/top-outline cells.

| Standing cell | Opaque pixels | Minimum / maximum opaque y from bottom | Shared / portrait colors |
| --- | --- | --- | --- |
| Mina | 430 | 0 / 30 | 14 / 18 = 0.7778 |
| Eli | 404 | 0 / 30 | 15 / 21 = 0.7143 |
| Ren | 373 | 0 / 30 | 14 / 20 = 0.7000 |
| Sora | 379 | 0 / 30 | 16 / 18 = 0.8889 |

The corrected Ren 8× review shows the continuous cap crown with the existing cap badge, face, vest and full boots preserved. Ren's alpha gains exactly those five connected crown pixels; his feet and all other poses are unchanged. The palette set is unchanged. The following SHA-256 changes cover the script and generated art outputs; README and this record also changed to describe the correction. Paths are relative to `ArtSource/`.

| File | Before | After |
| --- | --- | --- |
| `Authored/T1/NormalizeT1Sprites.ps1` | `4AC6D0204102E77B97243BA654A70D185E330D7CE681AFA23800DFC7C09BA704` | `096E4D898D91FBB9B1C86DF6B029CD378CB19EE60F22256B41AC4BA4B905CC2A` |
| `Authored/T1/Characters/fisher_ren/npc_fisher_ren_idle_down.pixels` | `4709989A2962052CB474671E787439FCD2BC1CB9BE477CF82B72E5B4D03071BF` | `8E54FB85D3AE7637725FBEAE9547815E7BFABA7B02A2AF1845FED95DDCE9C40D` |
| `Authored/T1/Review/npc_fisher_ren_normalized_8x.png` | `FB366DDE78697542B505515B446D32AF1DC61D817AEA2C096607D1534CB94B92` | `D3C956AC3470F80F0723A93B343162BE9303E339A5938EC1EBE585C770AD382C` |
| `Authored/T1/Review/npc_fisher_ren_normalized_native.png` | `6BEB412DD0248FBFAD96AA9740421C2D4D0083A568432FD3581D9E6AC5442C15` | `1FA4AFBBBFCD9E9E0E31E0554C67CFF814B5E5E0EB8421A268FF6E46FEC8A436` |

All other 55 `.pixels` SHA-256 values were unchanged. Rebuilt Production and Unity regression results are recorded by the coordinating task after this source correction.

## Mina v01

- Tool: built-in imagegen, 2026-09-06.
- Output: `npc_shopkeeper_mina_source_v01.png`.
- References: A1 `npc_townsfolk_idle_down_24x32_4x.png`, `npc_portraits_48_4x.png`, and `chr_player_move_24x32_4x.png` in `ArtSource/Previews/A1/`.
- Observed: all 12 full bodies and the down/left/right/up row order are present. The chestnut hair, ivory blouse, brown apron and boots remain visible. The output is 1086×1448, with square source cells rather than the requested 24:32 cell ratio; native cells therefore require one shared character scale and grounded placement. Side-frame stride and mirrored contours still require native-size inspection.
- Status: selected generated reference after native normalization; this source record does not establish Production acceptance.

### Prompt

Use case: identity-preserve. Asset type: production pixel-art full-body 4-direction NPC walking sprite sheet for CozyTown. Create a NEW animation sheet for MINA ONLY, preserving the first character in reference image 1 and first portrait in reference image 2. Reference 1 supplies her existing full-body identity; reference 2 supplies Mina's face, chestnut side-swept bob with outward side locks, ivory short-sleeved blouse/collar, dark chocolate-brown apron and brown shoes. Reference 3 supplies the approved compact whole-body proportions, discrete pixel-cluster rendering and exact animation layout; do not replace Mina with that player. Keep Mina recognizable in all directions. No hat, tools or extra props.

Output layout: exactly THREE equal columns and FOUR equal rows, exactly 12 entire bodies. Rows top-to-bottom are facing DOWN toward viewer, facing LEFT in full left profile, facing RIGHT in full right profile, facing UP showing only the back. Columns left-to-right are IDLE with both feet neutral, WALK phase 00 with one foot ahead, WALK phase 01 with the opposite foot ahead. All three frames in each row must show the specified direction. Side frames are true 90-degree profiles. Back frames show hair and apron ties, no eyes or front face.

Pixel grid: design the WHOLE SHEET as a native 72x128 pixel bitmap, each cell exactly 24x32 native pixels, and render it as an exact integer enlargement if needed (ideal 1152x2048, each native pixel a 16x16 hard square). Use the SAME scale and anatomy for all 12 cells. Body occupies at most native x=2..21, centered at x=11..12, with top at native y=1 or 2 from top and lowest boot pixels at the very bottom row of each cell. Feet remain completely inside the frame. Do not independently resize poses; each body has the same visible 30-31 pixel height with at most one-pixel walk bob. Keep head size, torso width, clothing outline and shoe size consistent. Distinct readable alternating legs/arms, connected limbs and complete shoes. Preserve the subtle clustered highlights and shadows of the existing sprites rather than simplifying to a stick figure.

Scene/backdrop: genuinely TRANSPARENT alpha, no floor, no cast shadows, no grid lines, no borders, no gaps between cells, no labels, no text, no watermark. Crisp hard pixel edges, flat discrete palette colors, no gradients, antialiasing or painterly high-resolution details. Warm earthy palette matching the references, upper-left light. Preserve transparency and present only the 12 sprites.

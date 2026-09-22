using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Lighting;
using CozyTown.Unity.Town;
using UnityEngine;

namespace CozyTown.Unity.Npc
{
    [DisallowMultipleComponent]
    public sealed class TownLocalObservation2D : MonoBehaviour
    {
        private NpcObservationRegion[] _interiorSpaces = Array.Empty<NpcObservationRegion>();
        private TownMap2D _developmentMap;
        private bool _usesDevelopmentMap;
        private NpcObservationScene _scene = new NpcObservationScene(
            Array.Empty<NpcObservationRegion>(), Array.Empty<NpcObservationEntity>());

        public NpcObservationScene Scene
        {
            get
            {
                if (_usesDevelopmentMap) TryConfigureDevelopmentMap(_developmentMap);
                return _scene;
            }
        }

        public string CaptureConfiguration()
        {
            var result = new StringBuilder();
            TownMap2D.AppendConfiguration(result, Scene.CaptureConfiguration(), _interiorSpaces.Length);
            foreach (var region in _interiorSpaces)
                TownMap2D.AppendConfiguration(result, region.Id, region.Name, region.SpaceId,
                    region.MinX, region.MinY, region.MaxX, region.MaxY);
            return result.ToString();
        }

        public void Configure(NpcObservationScene scene, IEnumerable<NpcObservationRegion> interiorSpaces)
        {
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            var interiors = (interiorSpaces ?? throw new ArgumentNullException(nameof(interiorSpaces))).ToArray();
            if (interiors.Any(item => item == null))
                throw new ArgumentException("Interior spaces require region definitions.", nameof(interiorSpaces));
            _scene = scene;
            _interiorSpaces = interiors;
            _developmentMap = null;
            _usesDevelopmentMap = false;
        }

        public NpcObservationBody[] CaptureBodies(IEnumerable<NpcWorldResident2D> residents)
        {
            if (residents == null) throw new ArgumentNullException(nameof(residents));
            return residents.Select(resident =>
            {
                Vector2 position = resident.Position;
                var interior = _interiorSpaces.SingleOrDefault(region =>
                    region.Contains(position.x, position.y, region.SpaceId));
                return new NpcObservationBody(resident.NpcId, position.x, position.y,
                    interior?.SpaceId ?? "outdoors", resident.IsPresentInWorld);
            }).ToArray();
        }

        public bool TryConfigureDevelopmentMap(TownMap2D map)
        {
            Configure(new NpcObservationScene(Array.Empty<NpcObservationRegion>(),
                Array.Empty<NpcObservationEntity>()), Array.Empty<NpcObservationRegion>());
            if (map == null || map.Homes.Count != 4
                || !MatchesHome(map, "home.shopkeeper_mina", DefaultMvpIds.Npcs.Shopkeeper, -10.15f)
                || !MatchesHome(map, "home.fisher_ren", DefaultMvpIds.Npcs.Fisher, -3.15f)
                || !MatchesHome(map, "home.cook_sora", DefaultMvpIds.Npcs.Cook, 3.85f)
                || !MatchesHome(map, "home.farmer_eli", DefaultMvpIds.Npcs.Farmer, 10.85f)
                || !MatchesLocation(map, "work.fisher_ren.morning", new Vector2(-4.2f, -3))
                || !MatchesLocation(map, "rest.fisher_ren", new Vector2(-3, -1.4f))
                || !MatchesLocation(map, "road.west_lane", new Vector2(-3, 0.4f)))
                return false;

            // Coordinates come from the committed Dev scene and scene upgraders at cd2daae.
            // Reading Scene revalidates these registered objects without mutating earlier snapshots.
            var entities = new List<NpcObservationEntity>();
            var pond = map.transform.Find("Interaction Points/Pond")?.GetComponent<TownInteractionPoint2D>();
            if (pond != null && pond.isActiveAndEnabled && pond.Kind == TownInteractionKind.Pond
                && MatchesPosition(pond.transform.position, new Vector2(0, -4)))
            {
                Vector2 position = pond.transform.position;
                entities.Add(new NpcObservationEntity("pond", "池塘", position.x, position.y, "water",
                    interactionId: "fishing", resourceItemId: DefaultMvpIds.Items.Carp));
            }
            AddLamp("Shop Main Road Lamp", "lamp.shop_main_road", new Vector2(-4.1875f, -0.25f));
            AddLamp("Player Home Lamp", "lamp.player_home", new Vector2(-4.8125f, -4.5f));
            AddLamp("West Lane South Lamp", "lamp.west_lane_south", new Vector2(-3.4375f, 3.625f));

            // Home spaces use the authored building footprint, including the doorway recess.
            var interiors = new[] {
                new NpcObservationRegion("home.shopkeeper_mina", "Mina 的家", -12.4, 10, -8.6, 12.4,
                    "home.shopkeeper_mina"),
                new NpcObservationRegion("home.fisher_ren", "Ren 的家", -5.4, 10, -1.6, 12.4,
                    "home.fisher_ren"),
                new NpcObservationRegion("home.cook_sora", "Sora 的家", 1.6, 10, 5.4, 12.4,
                    "home.cook_sora"),
                new NpcObservationRegion("home.farmer_eli", "Eli 的家", 8.6, 10, 12.4, 12.4,
                    "home.farmer_eli") };
            Configure(new NpcObservationScene(new[] {
                    new NpcObservationRegion("pond-surroundings", "池塘周边", -5, -6, 4, 1),
                    new NpcObservationRegion("town-west", "小镇西侧", -16, -6, -5, 1),
                    new NpcObservationRegion("town-east", "小镇东侧", 4, -6, 16, 1),
                    new NpcObservationRegion("town-north", "小镇北侧", -16, 1, 16, 16) }.Concat(interiors),
                entities, radius: 6, maxNearby: 8), interiors);
            _developmentMap = map;
            _usesDevelopmentMap = true;
            return true;

            void AddLamp(string objectName, string entityId, Vector2 position)
            {
                var lamp = map.transform.Find("Town Lighting/" + objectName)?.GetComponent<TownLamp2D>();
                if (lamp != null && lamp.isActiveAndEnabled && MatchesPosition(lamp.transform.position, position))
                {
                    Vector2 actual = lamp.transform.position;
                    entities.Add(new NpcObservationEntity(entityId, "路灯", actual.x, actual.y, "decoration"));
                }
            }
        }

        private static bool MatchesHome(TownMap2D map, string homeId, string npcId, float x)
            => map.TryGetHome(npcId, out var home) && home.HomeId == homeId
                && home.DoorstepLocationId == homeId + ".doorstep" && home.EntryLocationId == homeId + ".entry"
                && MatchesLocation(map, home.DoorstepLocationId, new Vector2(x, 9.5f))
                && MatchesLocation(map, home.EntryLocationId, new Vector2(x, 10.25f));

        private static bool MatchesLocation(TownMap2D map, string id, Vector2 position)
            => map.TryGetLocation(id, out var actual) && MatchesPosition(actual, position);

        private static bool MatchesPosition(Vector2 actual, Vector2 expected)
            => (actual - expected).sqrMagnitude <= 0.000001f;
    }
}

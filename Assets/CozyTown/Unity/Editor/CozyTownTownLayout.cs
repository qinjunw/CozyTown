using System;
using System.Collections.Generic;
using CozyTown.Runtime.Content;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Town;
using UnityEngine;

namespace CozyTown.Unity.Editor
{
    public static class CozyTownTownLayout
    {
        public static BoundsInt GroundCells => new BoundsInt(-16, -6, 0, 32, 22, 1);

        public static Rect GroundBounds => new Rect(
            GroundCells.xMin, GroundCells.yMin, GroundCells.size.x, GroundCells.size.y);

        public static Vector2 ShopkeeperWorkPosition => new Vector2(-4.2f, 0.35f);
        public static Vector2 FarmerWorkPosition => new Vector2(9.1f, -2f);
        public static Vector2 FisherWorkPosition => new Vector2(-4.2f, -3f);
        public static Vector2 CookWorkPosition => new Vector2(3f, 0.35f);

        public static IReadOnlyList<NpcHomeSpec> Homes { get; } = Array.AsReadOnly(new[]
        {
            new NpcHomeSpec("home.shopkeeper_mina", DefaultMvpIds.Npcs.Shopkeeper, new Vector2(-10.5f, 10f)),
            new NpcHomeSpec("home.fisher_ren", DefaultMvpIds.Npcs.Fisher, new Vector2(-3.5f, 10f)),
            new NpcHomeSpec("home.cook_sora", DefaultMvpIds.Npcs.Cook, new Vector2(3.5f, 10f)),
            new NpcHomeSpec("home.farmer_eli", DefaultMvpIds.Npcs.Farmer, new Vector2(10.5f, 10f))
        });

        private static readonly TownLocation[] Locations = CreateLocations();
        private static readonly TownRoad[] Roads = CreateRoads();
        private static readonly Dictionary<Vector2Int, int> RoadTiles = CreateRoadTiles();

        public static void ConfigureMap(TownMap2D map)
        {
            var homes = new TownHome[Homes.Count];
            for (var index = 0; index < Homes.Count; index++)
            {
                var specification = Homes[index];
                homes[index] = new TownHome(specification.HomeId, specification.NpcId,
                    specification.HomeId + ".doorstep", specification.HomeId + ".entry");
            }

            map.Configure(homes, Locations, Roads);
        }

        public static Vector2 GetLandmarkPosition(TownInteractionKind kind)
        {
            switch (kind)
            {
                case TownInteractionKind.Shop: return new Vector2(-7f, 1f);
                case TownInteractionKind.Bed: return new Vector2(-7f, -4f);
                case TownInteractionKind.Farm: return new Vector2(6f, -4f);
                case TownInteractionKind.Coop: return new Vector2(0f, 1f);
                case TownInteractionKind.Pond: return new Vector2(0f, -4f);
                case TownInteractionKind.Kitchen: return new Vector2(6.5f, 1f);
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "No fixed business landmark is defined.");
            }
        }

        public static void GetRoadTileAxes(int x, int y, out bool horizontal, out bool vertical)
        {
            RoadTiles.TryGetValue(new Vector2Int(x, y), out var axes);
            horizontal = (axes & 1) != 0;
            vertical = (axes & 2) != 0;
        }

        private static TownLocation[] CreateLocations()
        {
            var locations = new List<TownLocation>
            {
                new TownLocation("road.west", new Vector2(-11f, 0.4f)),
                new TownLocation("road.shop", new Vector2(-4.2f, 0.4f)),
                new TownLocation("road.west_lane", new Vector2(-3f, 0.4f)),
                new TownLocation("road.coop", new Vector2(0f, 0.4f)),
                new TownLocation("road.east_lane", new Vector2(3f, 0.4f)),
                new TownLocation("road.kitchen", new Vector2(9.1f, 0.4f)),
                new TownLocation("road.east", new Vector2(12f, 0.4f)),
                new TownLocation("road.residential", new Vector2(3f, 8.5f)),
                new TownLocation("road.pond_west", new Vector2(-3f, -3f)),
                new TownLocation("road.pond_east", new Vector2(2.9f, 0.4f)),
                new TownLocation("work.shopkeeper_mina", ShopkeeperWorkPosition),
                new TownLocation("work.farmer_eli", FarmerWorkPosition),
                new TownLocation("work.fisher_ren.morning", FisherWorkPosition),
                new TownLocation("work.fisher_ren.afternoon", new Vector2(2.9f, -2f)),
                new TownLocation("work.cook_sora", CookWorkPosition),
                new TownLocation("rest.shopkeeper_mina", new Vector2(-3f, 5.5f)),
                new TownLocation("rest.farmer_eli", new Vector2(2.65f, 1.2f)),
                new TownLocation("rest.fisher_ren", new Vector2(-3f, -1.4f)),
                new TownLocation("rest.cook_sora", new Vector2(3f, 5.5f))
            };
            foreach (var home in Homes)
            {
                var doorX = home.Position.x + 0.35f;
                locations.Add(new TownLocation(home.HomeId + ".street", new Vector2(doorX, 8.5f)));
                locations.Add(new TownLocation(home.HomeId + ".doorstep", new Vector2(doorX, home.Position.y - 0.5f)));
                locations.Add(new TownLocation(home.HomeId + ".entry", new Vector2(doorX, home.Position.y + 0.25f)));
            }

            return locations.ToArray();
        }

        private static TownRoad[] CreateRoads()
        {
            var roads = new List<TownRoad>
            {
                new TownRoad("road.west", "road.shop"),
                new TownRoad("road.shop", "road.west_lane"),
                new TownRoad("road.west_lane", "road.coop"),
                new TownRoad("road.coop", "road.east_lane"),
                new TownRoad("road.east_lane", "road.kitchen"),
                new TownRoad("road.kitchen", "road.east"),
                new TownRoad("road.shop", "work.shopkeeper_mina"),
                new TownRoad("road.kitchen", "work.farmer_eli"),
                new TownRoad("road.east_lane", "work.cook_sora"),
                new TownRoad("road.west_lane", "rest.fisher_ren"),
                new TownRoad("rest.fisher_ren", "road.pond_west"),
                new TownRoad("road.pond_west", "work.fisher_ren.morning"),
                new TownRoad("road.east_lane", "road.pond_east"),
                new TownRoad("road.pond_east", "work.fisher_ren.afternoon"),
                new TownRoad("road.west_lane", "rest.shopkeeper_mina"),
                new TownRoad("road.east_lane", "rest.farmer_eli"),
                new TownRoad("road.east_lane", "rest.cook_sora"),
                new TownRoad("rest.shopkeeper_mina", "rest.cook_sora"),
                new TownRoad("rest.cook_sora", "road.residential"),
                new TownRoad("rest.shopkeeper_mina", "home.fisher_ren.street"),
                new TownRoad("home.shopkeeper_mina.street", "home.fisher_ren.street"),
                new TownRoad("home.fisher_ren.street", "road.residential"),
                new TownRoad("road.residential", "home.cook_sora.street"),
                new TownRoad("home.cook_sora.street", "home.farmer_eli.street")
            };
            foreach (var home in Homes)
            {
                roads.Add(new TownRoad(home.HomeId + ".street", home.HomeId + ".doorstep"));
                roads.Add(new TownRoad(home.HomeId + ".doorstep", home.HomeId + ".entry"));
            }

            return roads.ToArray();
        }

        private static Dictionary<Vector2Int, int> CreateRoadTiles()
        {
            var result = new Dictionary<Vector2Int, int>();
            foreach (var road in Roads)
            {
                var from = Array.Find(Locations, location => location.Id == road.FromLocationId).Position;
                var to = Array.Find(Locations, location => location.Id == road.ToLocationId).Position;
                var cell = Vector2Int.FloorToInt(from);
                var destination = Vector2Int.FloorToInt(to);
                var dx = Mathf.Abs(destination.x - cell.x);
                var dy = -Mathf.Abs(destination.y - cell.y);
                var stepX = cell.x < destination.x ? 1 : -1;
                var stepY = cell.y < destination.y ? 1 : -1;
                var error = dx + dy;
                var axis = Mathf.Abs(to.x - from.x) > Mathf.Abs(to.y - from.y) ? 1 : 2;
                while (true)
                {
                    result.TryGetValue(cell, out var existing);
                    result[cell] = existing | axis;
                    if (cell == destination) break;
                    var doubledError = error * 2;
                    if (doubledError >= dy) { error += dy; cell.x += stepX; }
                    if (doubledError <= dx) { error += dx; cell.y += stepY; }
                }
            }

            return result;
        }

        public readonly struct NpcHomeSpec
        {
            public NpcHomeSpec(string homeId, string npcId, Vector2 position)
            {
                HomeId = homeId;
                NpcId = npcId;
                Position = position;
            }

            public string HomeId { get; }
            public string NpcId { get; }
            public Vector2 Position { get; }
        }
    }
}

using System;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.NpcLife;
using CozyTown.Unity.Core;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Town;
using UnityEngine;

namespace CozyTown.Unity.Editor
{
    public static class CozyTownTownLifeSceneUpgrader
    {
        public static void Configure(GameObject world, CozyTownBootstrap bootstrap)
        {
            var map = world.GetComponent<TownMap2D>();
            var presenters = world.GetComponentsInChildren<CozyTownNpcDebugPresenter>(true);
            var residents = new[]
            {
                ConfigureResident(presenters, map, DefaultMvpIds.Npcs.Shopkeeper,
                    "work.shopkeeper_mina", "rest.shopkeeper_mina", "work.shopkeeper_mina",
                    new[] { 360, 480, 720, 780, 1020, 1080 }, Vector2.left, Vector2.left),
                ConfigureResident(presenters, map, DefaultMvpIds.Npcs.Farmer,
                    "work.farmer_eli", "rest.farmer_eli", "work.farmer_eli",
                    new[] { 330, 450, 690, 750, 990, 1050 }, Vector2.left, Vector2.left),
                ConfigureResident(presenters, map, DefaultMvpIds.Npcs.Fisher,
                    "work.fisher_ren.morning", "rest.fisher_ren", "work.fisher_ren.afternoon",
                    new[] { 345, 465, 735, 795, 1050, 1110 }, Vector2.right, Vector2.left),
                ConfigureResident(presenters, map, DefaultMvpIds.Npcs.Cook,
                    "work.cook_sora", "rest.cook_sora", "work.cook_sora",
                    new[] { 390, 510, 780, 840, 1080, 1140 }, Vector2.right, Vector2.right)
            };
            var controller = bootstrap.GetComponent<CozyTownTownLifeController>()
                ?? bootstrap.gameObject.AddComponent<CozyTownTownLifeController>();
            controller.Configure(residents);
            bootstrap.RegisterTownLife(controller);
        }

        private static NpcWorldResident2D ConfigureResident(CozyTownNpcDebugPresenter[] presenters,
            TownMap2D map, string npcId, string morning, string rest, string afternoon, int[] times,
            Vector2 morningFacing, Vector2 afternoonFacing)
        {
            var presenter = presenters.Single(npc => npc.NpcId == npcId);
            if (!map.TryGetHome(presenter.NpcId, out var home))
                throw new InvalidOperationException($"Resident '{npcId}' requires an owned home before town life can be configured.");
            var resident = presenter.GetComponent<NpcWorldResident2D>()
                ?? presenter.gameObject.AddComponent<NpcWorldResident2D>();
            resident.Configure(map, new NpcDailySchedule(presenter.NpcId, home.HomeId,
                home.DoorstepLocationId, home.EntryLocationId, morning, rest, afternoon,
                times[0], times[1], times[2], times[3], times[4], times[5]),
                presenter.transform.Find("Visual").GetComponent<SpriteRenderer>(),
                morningFacing: morningFacing, afternoonFacing: afternoonFacing);
            var anchor = presenter.transform.Find("Prompt Anchor");
            if (anchor == null)
            {
                anchor = new GameObject("Prompt Anchor").transform;
                anchor.SetParent(presenter.transform, false);
            }
            anchor.localPosition = new Vector3(0, 2.25f, 0);
            presenter.GetComponent<TownInteractionPoint2D>().ConfigurePromptAnchor(anchor);
            return resident;
        }
    }
}

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
            var presenter = world.GetComponentsInChildren<CozyTownNpcDebugPresenter>(true)
                .Single(npc => npc.NpcId == DefaultMvpIds.Npcs.Shopkeeper);
            if (!map.TryGetHome(presenter.NpcId, out var home))
                throw new InvalidOperationException("Mina requires an owned home before town life can be configured.");
            var resident = presenter.GetComponent<NpcWorldResident2D>()
                ?? presenter.gameObject.AddComponent<NpcWorldResident2D>();
            resident.Configure(map, new NpcDailySchedule(presenter.NpcId, home.HomeId,
                home.DoorstepLocationId, home.EntryLocationId, "work.shopkeeper_mina",
                "rest.shopkeeper_mina", "work.shopkeeper_mina", 360, 480, 720, 780, 1020, 1080),
                presenter.transform.Find("Visual").GetComponent<SpriteRenderer>());
            var anchor = presenter.transform.Find("Prompt Anchor");
            if (anchor == null)
            {
                anchor = new GameObject("Prompt Anchor").transform;
                anchor.SetParent(presenter.transform, false);
            }
            anchor.localPosition = new Vector3(0, 2.25f, 0);
            presenter.GetComponent<TownInteractionPoint2D>().ConfigurePromptAnchor(anchor);
            var controller = bootstrap.GetComponent<CozyTownTownLifeController>()
                ?? bootstrap.gameObject.AddComponent<CozyTownTownLifeController>();
            controller.Configure(resident);
            bootstrap.RegisterTownLife(controller);
        }
    }
}

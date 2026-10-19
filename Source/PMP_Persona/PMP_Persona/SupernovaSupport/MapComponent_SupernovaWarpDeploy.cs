using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.SupernovaSupport
{
    public class MapComponent_SupernovaWarpDeploy : MapComponent
    {
        private List<PendingDeploy> pending = new List<PendingDeploy>(4);

        private class PendingDeploy : IExposable
        {
            public IntVec3 cell;
            public int ticksRemaining;
            public int totalTicks = SupernovaConstants.WarpDeployTicks;

            public void ExposeData()
            {
                Scribe_Values.Look(ref cell, "cell");
                Scribe_Values.Look(ref ticksRemaining, "ticksRemaining");
                Scribe_Values.Look(ref totalTicks, "totalTicks", SupernovaConstants.WarpDeployTicks);
            }
        }

        public MapComponent_SupernovaWarpDeploy(Map map)
            : base(map)
        {
        }

        public static MapComponent_SupernovaWarpDeploy Get(Map map)
        {
            if (map == null)
                return null;

            var existing = map.GetComponent<MapComponent_SupernovaWarpDeploy>();
            if (existing != null)
                return existing;

            existing = new MapComponent_SupernovaWarpDeploy(map);
            map.components.Add(existing);
            return existing;
        }

        public bool ScheduleDeploy(IntVec3 cell)
        {
            if (!cell.InBounds(map))
                return false;

            pending.Add(new PendingDeploy
            {
                cell = cell,
                ticksRemaining = SupernovaConstants.WarpDeployTicks
            });
            return true;
        }

        public bool TryGetPendingTarget(out IntVec3 cell)
        {
            if (pending != null && pending.Count > 0)
            {
                cell = pending[0].cell;
                return cell.IsValid && cell.InBounds(map);
            }
            cell = IntVec3.Invalid;
            return false;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (pending.Count == 0)
                return;

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingDeploy entry = pending[i];
                entry.ticksRemaining--;
                if (entry.ticksRemaining <= 0)
                {
                    SupernovaDeployer.SpawnSupernovaAt(map, entry.cell);
                    pending.RemoveAt(i);
                }
                else
                {
                    pending[i] = entry;
                }
            }
        }

        public override void MapComponentDraw()
        {
            if (Find.CurrentMap != map) return;
            foreach (PendingDeploy entry in pending)
                SupernovaWarpVisuals.Draw(entry.cell, entry.ticksRemaining, entry.totalTicks);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pending, "pmpSupernovaPendingDeploys", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && pending == null)
                pending = new List<PendingDeploy>(4);
        }
    }
}

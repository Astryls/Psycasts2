#nullable disable
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using VanillaPsycastsExpanded;

namespace PsycastSynergies
{
    // Informational health-tab readout of the pawn's synergies + specializations. No mechanical
    // effect of its own - the real bonuses live in SkillSystem/SpecEffects; this just reports them.
    public class Hediff_PsychicResonance : HediffWithComps
    {
        // Hidden from the health tab: the spec/synergy readout lives in the psycast tab now, so
        // this row was clutter. Override (not XML becomeVisible) because Hediff.visible latches
        // true in existing saves and would keep the row shown there.
        public override bool Visible => false;

        public override string LabelInBrackets
        {
            get
            {
                var sp = GameComponent_PsycastSynergies.Instance?.GetSpec(pawn);
                int n = sp?.owned.Count ?? 0;
                return n > 0 ? n + (n == 1 ? " spec" : " specs") : null;
            }
        }

        public override string Description
        {
            get
            {
                var gc = GameComponent_PsycastSynergies.Instance;
                var sp = gc?.GetSpec(pawn);
                var sb = new StringBuilder();
                sb.AppendLine("PS_AscensionCultivated".Translate());

                if (sp != null && sp.owned.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("PS_AscensionSpecs".Translate());
                    foreach (var id in sp.owned)
                    {
                        var s = Specs.Get(id);
                        if (s != null) sb.AppendLine("  \u2022 " + s.label.CapitalizeFirst());
                    }
                }

                if (gc != null)
                {
                    gc.SummarizeLevels(pawn, out int leveled, out int total);
                    if (leveled > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine(leveled + (leveled == 1 ? " skill leveled" : " skills leveled") + ", " + total + " levels invested.");
                    }
                }
                return sb.ToString().TrimEndNewlines();
            }
        }
    }

    // Archotech Ascendant: slow passive psyfocus regeneration, scaled by how much of the cluster
    // is owned (hediff severity).
    public class HediffCompProperties_PsyfocusRegen : HediffCompProperties
    {
        public float perInterval = 0.001f;   // psyfocus per 60-tick interval, multiplied by severity
        public HediffCompProperties_PsyfocusRegen() { compClass = typeof(HediffComp_PsyfocusRegen); }
    }

    public class HediffComp_PsyfocusRegen : HediffComp
    {
        private HediffCompProperties_PsyfocusRegen Props => (HediffCompProperties_PsyfocusRegen)props;
        private int lastTick = -1;

        public override void CompPostTick(ref float severityAdjustment) => Run();
        public override void CompPostTickInterval(ref float severityAdjustment, int delta) => Run();
        private void Run()
        {
            int now = Find.TickManager.TicksGame;
            if (now == lastTick) return;
            lastTick = now;
            var p = parent.pawn;
            if (p?.psychicEntropy == null || !p.IsHashIntervalTick(60)) return;
            p.psychicEntropy.OffsetPsyfocusDirectly(Props.perInterval * Mathf.Max(1f, parent.Severity));
        }
    }

    // Empyrean / Consonance: while the capstone is owned, bless nearby ALLIES (optionally only allied
    // psycasters) with a short psychic-sensitivity boon.
    public class HediffCompProperties_AllyAura : HediffCompProperties
    {
        public float radius = 9f;
        public string capstoneKey;
        public bool psycastersOnly;
        public HediffCompProperties_AllyAura() { compClass = typeof(HediffComp_AllyAura); }
    }

    public class HediffComp_AllyAura : HediffComp
    {
        private HediffCompProperties_AllyAura Props => (HediffCompProperties_AllyAura)props;
        private int lastTick = -1;

        public override void CompPostTick(ref float severityAdjustment) => Run();
        public override void CompPostTickInterval(ref float severityAdjustment, int delta) => Run();
        private void Run()
        {
            int now = Find.TickManager.TicksGame;
            if (now == lastTick) return;
            lastTick = now;
            var p = parent.pawn;
            if (p == null || !p.Spawned || p.Map == null || !p.IsHashIntervalTick(120)) return;
            if (!AscensionSystem.HasCapstone(p, Props.capstoneKey)) return;
            if (PsycastDefOf.PS_AscensionBlessing == null) return;

            foreach (var thing in GenRadial.RadialDistinctThingsAround(p.Position, p.Map, Props.radius, true))
            {
                if (!(thing is Pawn t) || t == p || t.Dead || t.health == null) continue;
                if (!t.RaceProps.Humanlike || t.HostileTo(p) || t.Faction != p.Faction) continue;
                if (Props.psycastersOnly && t.Psycasts() == null) continue;

                var hd = t.health.hediffSet.GetFirstHediffOfDef(PsycastDefOf.PS_AscensionBlessing);
                if (hd == null)
                {
                    var brain = t.health.hediffSet.GetBrain();
                    hd = HediffMaker.MakeHediff(PsycastDefOf.PS_AscensionBlessing, t, brain);
                    t.health.AddHediff(hd, brain);
                }
                else
                {
                    var dis = hd.TryGetComp<HediffComp_Disappears>();
                    if (dis != null) dis.ticksToDisappear = 300;
                }
            }
        }
    }

    // Umbral Sovereign: while the capstone is owned, smother nearby HOSTILE psycasters with a
    // short psychic-sensitivity debuff.
    public class HediffCompProperties_UmbralAura : HediffCompProperties
    {
        public float radius = 9f;
        public HediffCompProperties_UmbralAura() { compClass = typeof(HediffComp_UmbralAura); }
    }

    public class HediffComp_UmbralAura : HediffComp
    {
        private HediffCompProperties_UmbralAura Props => (HediffCompProperties_UmbralAura)props;
        private int lastTick = -1;

        public override void CompPostTick(ref float severityAdjustment) => Run();
        public override void CompPostTickInterval(ref float severityAdjustment, int delta) => Run();
        private void Run()
        {
            int now = Find.TickManager.TicksGame;
            if (now == lastTick) return;
            lastTick = now;
            var p = parent.pawn;
            if (p == null || !p.Spawned || p.Map == null || !p.IsHashIntervalTick(120)) return;
            if (!AscensionSystem.HasCapstone(p, "umbral")) return;

            foreach (var thing in GenRadial.RadialDistinctThingsAround(p.Position, p.Map, Props.radius, true))
            {
                if (!(thing is Pawn t) || t == p || t.Dead || t.health == null) continue;
                if (!t.RaceProps.Humanlike || !t.HostileTo(p) || t.Psycasts() == null) continue;

                var hd = t.health.hediffSet.GetFirstHediffOfDef(PsycastDefOf.PS_UmbralSuppression);
                if (hd == null)
                {
                    var brain = t.health.hediffSet.GetBrain();
                    hd = HediffMaker.MakeHediff(PsycastDefOf.PS_UmbralSuppression, t, brain);
                    t.health.AddHediff(hd, brain);
                }
                else
                {
                    var dis = hd.TryGetComp<HediffComp_Disappears>();
                    if (dis != null) dis.ticksToDisappear = 300;   // refresh
                }
            }
        }
    }
}

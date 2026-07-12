using System.Collections.Generic;
using RimWorld;
using Verse;
using VEF.Abilities;
using VanillaPsycastsExpanded;
using AbilityDef = VEF.Abilities.AbilityDef;

namespace PsycastSynergies
{
    public class HediffCompProperties_OSkillGrant : HediffCompProperties
    {
        public HediffCompProperties_OSkillGrant() { compClass = typeof(HediffComp_OSkillGrant); }
    }

    // Bookkeeping only: records which abilities (and whether the psycast implant) were granted by
    // Itemization Overhaul oskill gear, so they can be cleanly removed when the gear comes off.
    public class HediffComp_OSkillGrant : HediffComp
    {
        public List<string> granted = new List<string>();
        public bool implantByUs;

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Collections.Look(ref granted, "granted", LookMode.Value);
            Scribe_Values.Look(ref implantByUs, "implantByUs", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && granted == null) granted = new List<string>();
        }
    }

    /// <summary>Reconciles item-granted "oskills" against the REAL VPE psycast system. Called by
    /// Itemization Overhaul (via reflection) with the list of VPE ability defNames the wearer's oskill gear
    /// currently demands. It grants the real ability (giving a non-psycaster a scoped psycast implant so
    /// they can cast at all), and removes what it granted when the gear is unequipped. The +N rank magnitude
    /// is applied separately as an effective-level bonus (SkillSystem.ExternalBonus); this only ensures the
    /// wearer actually KNOWS and can cast the real thing, inheriting autocast, synergies and mod interactions.</summary>
    public static class OSkillReconcile
    {
        public static void Reconcile(Pawn pawn, List<string> desired)
        {
            if (pawn == null || pawn.Dead || pawn.health == null) return;
            var comp = pawn.GetComp<CompAbilities>();
            if (comp == null) return;
            desired = desired ?? new List<string>();

            var track = GetTrack(pawn);
            var gc = GameComponent_PsycastSynergies.Instance;

            // 1) Grant desired abilities the pawn doesn't already know.
            for (int i = 0; i < desired.Count; i++)
            {
                var abilityDef = DefDatabase<AbilityDef>.GetNamedSilentFail(desired[i]);
                if (abilityDef == null) continue;
                if (comp.HasAbility(abilityDef)) continue;   // natural or already ours - leave alone
                bool wasPsycaster = pawn.Psycasts() != null;
                var implant = MeditationSystem.EnsurePsycaster(pawn);
                if (implant == null) continue;               // couldn't make them a psycaster
                track = EnsureTrack(pawn);
                if (!wasPsycaster)
                {
                    track.implantByUs = true;
                    // A brand-new oskill psycaster starts at 0 psyfocus; top them up once so the granted
                    // psycast is usable immediately (it still costs real psyfocus/heat per cast thereafter).
                    try { pawn.psychicEntropy?.OffsetPsyfocusDirectly(1f); } catch { }
                }
                comp.GiveAbility(abilityDef);
                if (!track.granted.Contains(desired[i])) track.granted.Add(desired[i]);
            }

            // 2) Remove abilities WE granted that are no longer demanded by gear.
            if (track != null && track.granted.Count > 0)
            {
                for (int i = track.granted.Count - 1; i >= 0; i--)
                {
                    if (desired.Contains(track.granted[i])) continue;
                    var abilityDef = DefDatabase<AbilityDef>.GetNamedSilentFail(track.granted[i]);
                    if (abilityDef != null)
                    {
                        comp.LearnedAbilities.RemoveAll(a => a != null && a.def == abilityDef);
                        gc?.SetLevel(pawn, abilityDef, 0);
                    }
                    track.granted.RemoveAt(i);
                }
            }

            // 3) Tear down an implant we created, once nothing of ours remains (never a real psycaster).
            if (track != null && track.granted.Count == 0 && track.implantByUs)
            {
                if (gc == null || !gc.HasAnyInvestedLevels(pawn))
                {
                    try
                    {
                        var psy = pawn.Psycasts();
                        if (psy != null) pawn.health.RemoveHediff(psy);
                        var psylink = pawn.GetMainPsylinkSource();
                        if (psylink != null) pawn.health.RemoveHediff(psylink);
                    }
                    catch { }
                }
                track.implantByUs = false;
            }

            // 4) Drop the bookkeeping hediff when there's nothing left to track.
            if (track != null && track.granted.Count == 0 && !track.implantByUs)
                RemoveTrack(pawn);
        }

        private static HediffDef trackDef;
        private static HediffDef TrackDef => trackDef ?? (trackDef = DefDatabase<HediffDef>.GetNamedSilentFail("PS_OSkillGrant"));

        private static HediffComp_OSkillGrant GetTrack(Pawn pawn)
        {
            var def = TrackDef;
            if (def == null) return null;
            var hd = pawn.health?.hediffSet?.GetFirstHediffOfDef(def);
            return (hd as HediffWithComps)?.TryGetComp<HediffComp_OSkillGrant>();
        }

        private static HediffComp_OSkillGrant EnsureTrack(Pawn pawn)
        {
            var existing = GetTrack(pawn);
            if (existing != null) return existing;
            var def = TrackDef;
            if (def == null) return null;
            var hd = HediffMaker.MakeHediff(def, pawn);
            pawn.health.AddHediff(hd);
            return (hd as HediffWithComps)?.TryGetComp<HediffComp_OSkillGrant>();
        }

        private static void RemoveTrack(Pawn pawn)
        {
            var def = TrackDef;
            if (def == null) return;
            var hd = pawn.health?.hediffSet?.GetFirstHediffOfDef(def);
            if (hd != null) pawn.health.RemoveHediff(hd);
        }
    }
}

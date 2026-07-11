#nullable disable
using RimWorld;
using UnityEngine;
using Verse;

namespace PsycastSynergies
{
    [DefOf]
    public static class PSHediffDefOf
    {
        public static HediffDef PS_Enlightenment;
        static PSHediffDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(PSHediffDefOf));
    }

    // A pawn's enlightenment tier lives as a HEDIFF whose Severity == tier (1, 2, 3, ... open-ended). It is
    // readable on ANY humanlike (our colonists and enemy psycasters alike), shows on the Health tab, and is
    // the single source of truth for a pawn's tier. MeditationData.tier mirrors it for our bookkeeping.
    public static class EnlightenmentTier
    {
        public static readonly string[] Names = { "", "Awakened", "Enlightened", "Illuminated" };

        public static string Name(int tier)
        {
            if (tier <= 0) return "";
            var custom = TieringControl.TierName(tier);   // a TieringOverrideDef can reskin the whole ladder
            if (custom != null) return custom;
            if (tier <= 3) return ("PS_Tier_" + Names[tier]).Translate();
            return "PS_TierTranscendent".Translate(RomanNumerals.ToRoman(tier));   // tier 4+ named by the FULL tier (tier 6 -> "Transcendent VI")
        }

        public static int TierOf(Pawn p)
        {
            if (p?.health?.hediffSet == null || PSHediffDefOf.PS_Enlightenment == null) return 0;
            var h = p.health.hediffSet.GetFirstHediffOfDef(PSHediffDefOf.PS_Enlightenment);
            return h == null ? 0 : Mathf.RoundToInt(h.Severity);
        }

        // Set (or change) a pawn's tier: adds/updates/removes the hediff, mirrors MeditationData, and - when
        // the tier increases and grantRewards is set - awards the tier's bonus specialization points.
        public static void SetTier(Pawn p, int tier, bool grantRewards = true)
        {
            if (p?.health?.hediffSet == null || PSHediffDefOf.PS_Enlightenment == null) return;
            tier = Mathf.Clamp(tier, 0, 99);
            var h = p.health.hediffSet.GetFirstHediffOfDef(PSHediffDefOf.PS_Enlightenment);
            int cur = h == null ? 0 : Mathf.RoundToInt(h.Severity);
            if (cur == tier) return;
            if (tier <= 0)
            {
                if (h != null) p.health.RemoveHediff(h);
            }
            else if (h == null)
            {
                var brain = p.health.hediffSet.GetBrain();   // seat it in the brain, like a psylink
                h = HediffMaker.MakeHediff(PSHediffDefOf.PS_Enlightenment, p, brain);
                h.Severity = tier;
                p.health.AddHediff(h, brain);
            }
            else h.Severity = tier;

            var gc = GameComponent_PsycastSynergies.Instance;
            var med = gc?.GetMed(p, true);
            if (med != null) med.tier = tier;

            if (grantRewards && tier > cur) GrantTierPoints(gc, p, tier);
        }

        // Relocate a pre-brain hediff (older saves added it whole-body) onto the brain part, preserving severity.
        public static void EnsureOnBrain(Pawn p)
        {
            if (p?.health?.hediffSet == null || PSHediffDefOf.PS_Enlightenment == null) return;
            var h = p.health.hediffSet.GetFirstHediffOfDef(PSHediffDefOf.PS_Enlightenment);
            if (h == null) return;
            var brain = p.health.hediffSet.GetBrain();
            if (brain == null || h.Part == brain) return;
            float sev = h.Severity;
            p.health.RemoveHediff(h);
            var nh = HediffMaker.MakeHediff(PSHediffDefOf.PS_Enlightenment, p, brain);
            nh.Severity = sev;
            p.health.AddHediff(nh, brain);
        }

        private static void GrantTierPoints(GameComponent_PsycastSynergies gc, Pawn p, int tier)
        {
            if (gc == null) return;
            var s = PsycastSynergiesMod.Settings;
            int pts = TieringControl.SpecPoints(tier)   // a TieringOverrideDef can re-cost the ladder
                ?? (tier == 2 ? (s?.tier2SpecPoints ?? 2) : tier == 3 ? (s?.tier3SpecPoints ?? 3) : tier >= 4 ? 3 : 0);   // +3 per Transcendent tier
            if (pts <= 0) return;
            var spec = gc.GetSpec(p, true);
            if (spec != null) spec.points += pts;
        }
    }

    // Enlightenment tier hediff. Severity == tier. Custom so the bracket label reflects the tier name
    // (including future procedural tier-4+ names) regardless of which stage is active.
    public class Hediff_Enlightenment : HediffWithComps
    {
        public override string LabelInBrackets => EnlightenmentTier.Name(Mathf.RoundToInt(Severity));
    }
}

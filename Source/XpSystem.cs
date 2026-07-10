#nullable disable
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using VanillaPsycastsExpanded;

namespace PsycastSynergies
{
    // Psycast XP rework. Vanilla VPE grants XP ONLY from meditation (psyfocus gained \u00d7 100 \u00d7
    // XPPerPercent, via GainXpFromPsyfocus). We rebalance:
    //   \u2022 casting is the primary source, scaled by the skill's tier (CastScaling.PostCast \u2192 GrantCastXp);
    //   \u2022 meditation still trickles XP, scaled down by meditationXpMult;
    //   \u2022 a meditating pawn can randomly have an "Enlightenment" breakthrough for a big burst.

    // Scale down VPE's meditation XP at its single grant point.
    [HarmonyPatch(typeof(Pawn_EntropyTracker_GainPsyfocus_Postfix), "GainXpFromPsyfocus")]
    public static class Patch_MeditationXp
    {
        static void Prefix(ref float gain)
        {
            var s = PsycastSynergiesMod.Settings;
            if (s != null) gain *= s.meditationXpMult;
        }
    }

    public static class XpSystem
    {
        // Tier-scaled psycast XP granted on each cast (the primary XP source).
        public static void GrantCastXp(Pawn pawn, int tier)
        {
            var s = PsycastSynergiesMod.Settings;
            if (s == null || pawn == null) return;
            float xp = s.castXpPerTier * Mathf.Max(1, tier);
            if (xp > 0f) pawn.Psycasts()?.GainExperience(xp, false);
        }

    }
}

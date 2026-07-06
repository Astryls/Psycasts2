#nullable disable
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using VanillaPsycastsExpanded;

namespace PsycastSynergies
{
    // Each psycaster level grants a specialization point.
    [HarmonyPatch(typeof(Hediff_PsycastAbilities), nameof(Hediff_PsycastAbilities.ChangeLevel), new[] { typeof(int) })]
    public static class Patch_ChangeLevel_SpecPoints
    {
        static void Postfix(Hediff_PsycastAbilities __instance, int levelOffset)
        {
            if (levelOffset <= 0) return;
            var gc = GameComponent_PsycastSynergies.Instance;
            var d = gc?.GetSpec(__instance.pawn, create: true);
            if (d == null) return;
            int per = Mathf.Max(1, PsycastSynergiesMod.Settings.specLevelsPerPoint);
            d.levelProgress += levelOffset;
            while (d.levelProgress >= per) { d.levelProgress -= per; d.points++; }
        }
    }

    // Kills made shortly after a psycast grant bonus spec XP, and trigger Tempest's cost refund.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Kill_SpecPoints
    {
        static void Postfix(Pawn __instance, DamageInfo? dinfo)
        {
            try
            {
                Pawn killer = dinfo?.Instigator as Pawn;
                if (killer == null || killer == __instance) return;
                var d = GameComponent_PsycastSynergies.Instance?.GetSpec(killer);
                if (d == null) return;

                int dt = Find.TickManager.TicksGame - d.lastCastTick;
                if (dt < 0 || dt > 240) return; // not a recent psycast kill

                SpecPoints.AddXp(d, 8f);

                var ent = killer.psychicEntropy;
                if (ent != null)
                {
                    if (d.Owns("tempest"))
                    {
                        if (d.lastEntropy > 0f) ent.TryAddEntropy(-d.lastEntropy * 0.5f);
                        if (d.lastFocus > 0f) ent.OffsetPsyfocusDirectly(d.lastFocus * 0.5f);
                    }
                    else if (d.Owns("siphon") && d.lastFocus > 0f)
                    {
                        ent.OffsetPsyfocusDirectly(d.lastFocus * 0.25f);
                    }
                }
            }
            catch { }
        }
    }
}

#nullable disable
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace PsycastSynergies
{
    // Ascension mental-state immunity:
    //   Tranquil Mind  (capstone) -> immune to all (non-forced) mental breaks.
    //   Umbral Sovereign (capstone) -> immune to berserk and any psycast-induced mental state (mind control).
    [HarmonyPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
    public static class Patch_AscensionImmunity
    {
        static bool Prefix(MentalStateDef stateDef, bool forced, bool causedByPsycast, Pawn ___pawn, ref bool __result)
        {
            if (___pawn == null || forced || stateDef == null) return true;
            // One SpecData resolve for all three capstone probes (was three Instance+GetSpec chains).
            var sp = GameComponent_PsycastSynergies.Instance?.GetSpec(___pawn);
            if (sp == null || sp.owned.Count == 0) return true;

            if (AscensionSystem.HasCapstone(sp, "tranquil"))
            {
                __result = false;
                return false;   // tranquil: no mental breaks at all
            }

            if (AscensionSystem.HasCapstone(sp, "umbral")
                && (causedByPsycast || stateDef == MentalStateDefOf.Berserk))
            {
                __result = false;
                return false;   // umbral: immune to berserk + mind control
            }

            if (AscensionSystem.HasCapstone(sp, "pandemonium") && causedByPsycast)
            {
                __result = false;
                return false;   // pandemonium: no will commands the storm (mind-control immune)
            }

            return true;
        }
    }
}

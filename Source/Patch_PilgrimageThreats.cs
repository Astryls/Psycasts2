#nullable disable
using HarmonyLib;
using RimWorld;
using Verse;

namespace PsycastSynergies
{
    // Suppress random storyteller THREAT incidents (infestations / hives, manhunter packs, ambush raids, etc.)
    // on pilgrimage site maps. The anima chain is pacifist and must stay completely safe; the altar chain
    // supplies its OWN scripted Ancient Psycaster waves (spawned directly via LordJob, NOT through an
    // IncidentWorker), so blocking storyteller threats keeps both maps free of out-of-place ambushes like
    // hive infestations while a lone pilgrim meditates. Non-pilgrimage maps are untouched.
    [HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.CanFireNow))]
    public static class Patch_NoThreatsOnPilgrimage
    {
        static bool Prefix(IncidentWorker __instance, IncidentParms parms, ref bool __result)
        {
            var def = __instance?.def;
            if (def == null) return true;
            if (def.category != IncidentCategoryDefOf.ThreatBig && def.category != IncidentCategoryDefOf.ThreatSmall)
                return true;   // only gate threats; let everything else (quests, weather, etc.) through
            if (parms.target is Map map && PilgrimRouting.IsPilgrimageMap(map))
            {
                __result = false;
                return false;   // skip the original; this threat may not fire on a pilgrimage map
            }
            return true;
        }
    }
}

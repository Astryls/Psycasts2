#nullable disable
using System;
using HarmonyLib;
using Verse;
using VanillaPsycastsExpanded;

namespace PsycastSynergies
{
    // "Disable gene requirements": VPE gates some paths behind a specific gene (Archon, etc.) via the
    // private PsycasterPathDef.PawnHasGene. Force it true so those paths unlock without the gene.
    [HarmonyPatch(typeof(PsycasterPathDef), "PawnHasGene")]
    public static class Patch_PathGeneRequirement
    {
        static void Postfix(ref bool __result)
        {
            if (PsycastSynergiesMod.Settings?.disableGeneRequirements == true) __result = true;
        }
    }

    // "Unlock removed Mech-Mind trees": the Mechanitor addon ships its Mech-Mind paths
    // (Worker/DMS/Alpha/Reinforced/Milian/Warfare/Miho Mechanitor) but force-locks them as
    // "Removed content (Mech-Mind)". Re-enable any locked Mechanitor path. Runs after the addon's
    // own logic (postfix), so it overrides whatever keeps them locked.
    [HarmonyPatch(typeof(PsycasterPathDef), "CanPawnUnlock")]
    public static class Patch_PathMechMind
    {
        static void Postfix(PsycasterPathDef __instance, Pawn pawn, ref bool __result)
        {
            var s = PsycastSynergiesMod.Settings;
            // Paths are earned through Enlightenment (the awakening cards) or dev mode - lock the tab's
            // normal Unlock buttons. (The card pick calls UnlockPath directly, bypassing CanPawnUnlock.)
            // ONLY lock the PLAYER's pawns: enemy/NPC psycaster GENERATION relies on CanPawnUnlock to
            // grant each path's abilities, so locking everyone left raiders with no skills.
            if (s?.lockPathsToEnlightenment == true && pawn?.Faction != null && pawn.Faction.IsPlayer)
            { __result = false; return; }

            if (__result || __instance?.defName == null) return;
            if (s?.enableLockedMechTrees != true) return;
            bool mechPath = __instance.defName.IndexOf("Mechanitor", StringComparison.OrdinalIgnoreCase) >= 0;
            bool mechMind = !string.IsNullOrEmpty(__instance.lockedReason)
                            && __instance.lockedReason.IndexOf("Mech-Mind", StringComparison.OrdinalIgnoreCase) >= 0;
            if (mechPath || mechMind) __result = true;
        }
    }
}

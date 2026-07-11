#nullable disable
using HarmonyLib;
using UnityEngine;
using VanillaPsycastsExpanded;
using VanillaPsycastsExpanded.UI;
using Verse;

namespace PsycastSynergies
{
    // Mastered-tree celebration: any psycast tree card whose EVERY ability the pawn holds at the
    // absolute max skill level (10, or 15 with Convergence) gets a persistent pulsing gold glow +
    // ambient sparkle flecks in the card-selection style (SkillFx.DrawMasteredTree).
    //
    // ONE hook covers BOTH UIs: VPE's native tab AND Modern Psycasts UI's DrawTreeTile each render
    // an unlocked tree's ability grid through PsycastsUIUtility.DoPathAbilities(artRect, path, ...),
    // with PsycastsUIUtility.Hediff/.CompAbilities already set. (A mastered tree is by definition
    // unlocked, so the locked-tree paths that skip DoPathAbilities can never need the glow.)
    [HarmonyPatch(typeof(PsycastsUIUtility), nameof(PsycastsUIUtility.DoPathAbilities))]
    public static class Patch_TreeMastered
    {
        static void Postfix(Rect inRect, PsycasterPathDef path)
        {
            var hediff = PsycastsUIUtility.Hediff;
            var comp = PsycastsUIUtility.CompAbilities;
            if (hediff?.pawn == null || comp == null || path == null) return;
            if (SkillFx.TreeMastered(hediff.pawn, comp, path))
                SkillFx.DrawMasteredTree(inRect, path);
        }
    }
}

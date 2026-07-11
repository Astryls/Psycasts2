#nullable disable
using System.Reflection;
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
    // VPE's native tab draws each tree card through PsycastsUIUtility.DrawPathBackground; both UIs
    // set PsycastsUIUtility.Hediff/.CompAbilities before drawing, so the pawn comes from there.
    [HarmonyPatch(typeof(PsycastsUIUtility), nameof(PsycastsUIUtility.DrawPathBackground))]
    public static class Patch_TreeMasteredVpe
    {
        static void Postfix(ref Rect rect, PsycasterPathDef def)
        {
            var hediff = PsycastsUIUtility.Hediff;
            var comp = PsycastsUIUtility.CompAbilities;
            if (hediff?.pawn == null || comp == null || def == null) return;
            // DrawPathBackground shrank rect to the art area (TakeBottomPart cut the 30px label
            // bar), so the full card is the post-call rect plus that bar.
            var tile = new Rect(rect.x, rect.y, rect.width, rect.height + 30f);
            if (SkillFx.TreeMastered(hediff.pawn, comp, def))
                SkillFx.DrawMasteredTree(tile, def);
        }
    }

    // Modern Psycasts UI draws tree cards through its own ModernPsycastsDrawer.DrawTreeTile
    // (it does NOT route through DrawPathBackground). Soft target resolved by name so this
    // patch is skipped when that mod is absent.
    [HarmonyPatch]
    public static class Patch_TreeMasteredModernUI
    {
        static bool Prepare() => AccessTools.TypeByName("ModernPsycastsUI.ModernPsycastsDrawer") != null;

        static MethodBase TargetMethod() =>
            AccessTools.Method("ModernPsycastsUI.ModernPsycastsDrawer:DrawTreeTile");

        static void Postfix(Rect tile, PsycasterPathDef def)
        {
            var hediff = PsycastsUIUtility.Hediff;
            var comp = PsycastsUIUtility.CompAbilities;
            if (hediff?.pawn == null || comp == null || def == null) return;
            if (SkillFx.TreeMastered(hediff.pawn, comp, def))
                SkillFx.DrawMasteredTree(tile, def);
        }
    }
}

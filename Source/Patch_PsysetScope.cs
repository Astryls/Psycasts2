#nullable disable
using System;
using HarmonyLib;
using VanillaPsycastsExpanded.UI;
using Verse;

namespace PsycastSynergies
{
    // ================================ PSYSET EDITING SCOPE ================================
    // Both psyset editors (VPE's Dialog_Psyset and Modern Psycasts UI's embedded editor) draw
    // their ability icons through PsycastsUIUtility.DrawAbility and then register their OWN
    // Widgets.ButtonInvisible on the same rect - "click to add / remove from this set" (VPE also
    // makes the icon draggable). Our DrawAbility postfix adds a click-to-invest button on that
    // same rect, and the FIRST IMGUI button under the cursor consumes the click - so inside a
    // psyset editor every click was swallowed as "invest a level", and only skills already at
    // their absolute cap (where our button is not drawn) could be added to a set at all.
    //
    // Fix: scope-flag the psyset editors. While the flag is up, Patch_DrawAbility draws its
    // overlays but registers NO button and suppresses its own tooltip, so the host editor's
    // click handling and its "click to add" hints work exactly as designed. The flag is set in a
    // prefix and cleared in a FINALIZER, so a mid-draw exception can never latch it on.
    internal static class PsysetScope
    {
        private static int depth;
        internal static bool Active => depth > 0;

        // Public so both the attributed patch and the reflection-wired Modern UI patch share them.
        public static void Enter() => depth++;
        public static void Exit() { if (depth > 0) depth--; }
    }

    // VPE's stand-alone psyset window (also reached from the tab's smallMode "Edit" button).
    [HarmonyPatch(typeof(Dialog_Psyset), nameof(Dialog_Psyset.DoWindowContents))]
    public static class Patch_PsysetScope
    {
        public static void Prefix() => PsysetScope.Enter();
        public static void Finalizer() => PsysetScope.Exit();

        // ---- Modern Psycasts UI (soft, reflection only) ----------------------------------------
        // Its editor lives INSIDE the psycast tab (left panel, psysetMode == 2) while the tree panel
        // on the right keeps drawing normally - so scope the editor method itself, not the whole tab,
        // and click-to-invest stays available in the trees beside it.
        public static void TryWireModernUI(Harmony harmony)
        {
            try
            {
                var drawer = AccessTools.TypeByName("ModernPsycastsUI.ModernPsycastsDrawer");
                var editor = drawer == null ? null : AccessTools.Method(drawer, "DrawPsysetEditor");
                if (editor == null) return;   // Modern Psycasts UI absent or reshaped
                harmony.Patch(editor,
                    prefix: new HarmonyMethod(typeof(Patch_PsysetScope), nameof(Prefix)),
                    finalizer: new HarmonyMethod(typeof(Patch_PsysetScope), nameof(Finalizer)));
            }
            catch (Exception e)
            {
                Log.Warning("[Psycasts²] Psyset click fix: could not wire Modern Psycasts UI: " + e.Message);
            }
        }
    }
}

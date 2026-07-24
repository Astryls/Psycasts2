#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using VanillaPsycastsExpanded.UI;
using Verse;

namespace PsycastSynergies
{
    // Injects MeditationBars into both psycast-tab renderers:
    //
    // 1. VPE-native tab: transpiler on ITab_Pawn_Psycasts.FillTab. The bars must sit BETWEEN the
    //    foci grid and the "Customize Psysets" label, mid-Listing - so we insert a hook call into
    //    the Listing_Standard flow right before the smallMode branch that draws the psyset label.
    //    Everything below shifts down naturally and the psysets menu section auto-shrinks.
    //    Anchors: the first ldstr "VPE.PsysetCustomize", walked back to its `this.smallMode` load;
    //    the Listing_Standard local is found via its newobj -> stloc pair. Pattern miss = warning +
    //    original IL (never throws - plain PatchAll would die).
    //
    // 2. Modern Psycasts UI (optional, reflection only - no compile-time reference): postfixes on
    //    ModernPsycastsDrawer.DrawFoci (draw at the returned y cursor, then advance it) AND on
    //    LeftPanelHeight (grow the content-fit card by the same amount - it mirrors DrawLeftPanel's
    //    row advances, so an unmatched inflation would clip the psyset button below the card edge).
    [HarmonyPatch(typeof(ITab_Pawn_Psycasts), "FillTab")]
    public static class Patch_MeditationBars
    {
        // ---- 1. VPE-native tab ----------------------------------------------------------------

        public static void FillTabHook(Listing_Standard l, Pawn p)
        {
            MeditationBars.DrawInListing(l, p);   // internally guarded (broken fuse + try/catch)
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);

            // Listing_Standard local: first `newobj Listing_Standard()` -> following stloc.
            CodeInstruction ldListing = null;
            for (int i = 0; i < list.Count - 1; i++)
            {
                if (list[i].opcode == OpCodes.Newobj && list[i].operand is ConstructorInfo ci
                    && ci.DeclaringType == typeof(Listing_Standard))
                {
                    ldListing = LdlocFromStloc(list[i + 1]);
                    break;
                }
            }

            // Anchor: first "VPE.PsysetCustomize", walked back to the `ldarg.0; ldfld smallMode` pair.
            var smallModeF = AccessTools.Field(typeof(ITab_Pawn_Psycasts), "smallMode");
            var pawnF = AccessTools.Field(typeof(ITab_Pawn_Psycasts), "pawn");
            int insertAt = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode != OpCodes.Ldstr || (string)list[i].operand != "VPE.PsysetCustomize") continue;
                for (int j = i - 1; j >= 0 && j >= i - 20; j--)
                {
                    if (list[j].opcode == OpCodes.Ldfld && (FieldInfo)list[j].operand == smallModeF
                        && j > 0 && list[j - 1].opcode == OpCodes.Ldarg_0)
                    {
                        insertAt = j - 1;
                        break;
                    }
                }
                break;   // only the FIRST psyset-label occurrence matters
            }

            if (ldListing == null || insertAt < 0 || smallModeF == null || pawnF == null)
            {
                Log.Warning("[Psycasts²] Meditation bars: VPE's FillTab IL did not match; the VPE-native tab shows no bars.");
                return list;
            }

            var insert = new List<CodeInstruction>
            {
                ldListing,
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, pawnF),
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(Patch_MeditationBars), nameof(FillTabHook))),
            };
            insert[0].MoveLabelsFrom(list[insertAt]);   // branch targets on the ldarg.0 must land on our block
            list.InsertRange(insertAt, insert);
            return list;
        }

        private static CodeInstruction LdlocFromStloc(CodeInstruction st)
        {
            if (st.opcode == OpCodes.Stloc_0) return new CodeInstruction(OpCodes.Ldloc_0);
            if (st.opcode == OpCodes.Stloc_1) return new CodeInstruction(OpCodes.Ldloc_1);
            if (st.opcode == OpCodes.Stloc_2) return new CodeInstruction(OpCodes.Ldloc_2);
            if (st.opcode == OpCodes.Stloc_3) return new CodeInstruction(OpCodes.Ldloc_3);
            if (st.opcode == OpCodes.Stloc_S) return new CodeInstruction(OpCodes.Ldloc_S, st.operand);
            if (st.opcode == OpCodes.Stloc) return new CodeInstruction(OpCodes.Ldloc, st.operand);
            return null;
        }

        // ---- 2. Modern Psycasts UI (soft) ------------------------------------------------------

        private static AccessTools.FieldRef<Pawn> modernPawn;

        // Called from HarmonyInit after PatchAll. Never throws; absence of the mod is a silent no-op.
        public static void TryWireModernUI(Harmony harmony)
        {
            try
            {
                var t = AccessTools.TypeByName("ModernPsycastsUI.ModernPsycastsDrawer");
                if (t == null) return;
                var pawnF = AccessTools.Field(t, "pawn");
                var drawFoci = AccessTools.Method(t, "DrawFoci");
                var height = AccessTools.Method(t, "LeftPanelHeight");
                if (pawnF == null || !pawnF.IsStatic || drawFoci == null || height == null)
                {
                    Log.Warning("[Psycasts²] Meditation bars: Modern Psycasts UI drawer shape unexpected; its panel shows no bars.");
                    return;
                }
                modernPawn = AccessTools.StaticFieldRefAccess<Pawn>(pawnF);
                harmony.Patch(drawFoci, postfix: new HarmonyMethod(typeof(Patch_MeditationBars), nameof(ModernFociPostfix)));
                harmony.Patch(height, postfix: new HarmonyMethod(typeof(Patch_MeditationBars), nameof(ModernHeightPostfix)));
            }
            catch (Exception e)
            {
                Log.Warning("[Psycasts²] Meditation bars: could not wire Modern Psycasts UI: " + e.Message);
            }
        }

        // DrawFoci(Rect c, float y) returns the advanced layout cursor - draw at it, then push it.
        public static void ModernFociPostfix(Rect c, ref float __result)
        {
            var p = modernPawn == null ? null : modernPawn();
            float h = MeditationBars.HeightFor(p);
            if (h <= 0f) return;
            MeditationBars.Draw(new Rect(c.x, __result + 6f, c.width, h), p);
            __result += h + 8f;
        }

        // LeftPanelHeight mirrors DrawLeftPanel's advances; must grow by EXACTLY what ModernFociPostfix adds.
        public static void ModernHeightPostfix(ref float __result)
        {
            var p = modernPawn == null ? null : modernPawn();
            float h = MeditationBars.HeightFor(p);
            if (h > 0f) __result += h + 8f;
        }
    }
}

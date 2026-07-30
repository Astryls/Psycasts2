#nullable disable
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using VanillaPsycastsExpanded.UI;
using Verse;

namespace PsycastSynergies
{
    // VPE's psycast tab hardcodes the locked-path button label as "VPE.Locked".Translate() + ": "
    // + def.lockedReason. Under our card-based unlock scheme (lockPathsToEnlightenment) every path
    // is locked and most reasons are nulled, which leaves a dangling "Locked: " - and the few
    // reasons that survive ("Imperials only", "Tribals only") are misleading anyway, since unlocks
    // come from awakening cards rather than this button. Retarget the concat to a helper:
    //   - our progression ON: the button reads "Undiscovered" (the tree is not locked, it simply
    //     has not revealed itself yet - awakening cards are the only way in);
    //   - our progression OFF: VPE's own "Locked" + genuine reasons still show, only the dangling
    //     colon is dropped.
    // Modern Psycasts UI draws its own plain "VPE.Locked" label on locked tree tiles; ModernTranspiler
    // (wired by reflection from HarmonyInit) swaps that key for the same helper.
    [HarmonyPatch(typeof(ITab_Pawn_Psycasts), "DoPaths")]
    public static class Patch_LockedNoColon
    {
        private static readonly PerfCache.LangCache LblUndiscovered = new PerfCache.LangCache("PS_Undiscovered");

        // True while paths unlock ONLY through the awakening cards - i.e. "locked" is the wrong word.
        private static bool Undiscovered => PsycastSynergiesMod.Settings?.lockPathsToEnlightenment == true;

        // Signature must exactly mirror string.Concat(string, string, string) so the retargeted
        // call site keeps a valid stack transition.
        public static string LockedLabel(string locked, string sep, string reason)
        {
            if (Undiscovered) return LblUndiscovered.Value;
            if (string.IsNullOrWhiteSpace(reason)) return locked;
            return locked + sep + reason;
        }

        // Key fed to Translate() at Modern Psycasts UI's locked-tile label site.
        public static string LockedKey() => Undiscovered ? "PS_Undiscovered" : "VPE.Locked";

        // Anchor on the ldstr ": " and retarget the next 3-string Concat call. Operand-only swap
        // (same opcode, same static string->string shape) keeps labels and blocks intact. If VPE
        // reshapes the method, log and return the IL untouched - a dangling colon is not worth a
        // failed PatchAll.
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            var concat3 = AccessTools.Method(typeof(string), nameof(string.Concat),
                new[] { typeof(string), typeof(string), typeof(string) });
            var helper = AccessTools.Method(typeof(Patch_LockedNoColon), nameof(LockedLabel));
            bool armed = false, done = false;
            for (int i = 0; i < list.Count && !done; i++)
            {
                var ci = list[i];
                if (!armed)
                {
                    if (ci.opcode == OpCodes.Ldstr && (ci.operand as string) == ": ") armed = true;
                }
                else if (ci.Calls(concat3))
                {
                    ci.operand = helper;
                    done = true;
                }
            }
            if (!done)
                Log.Warning("[Psycasts²] Locked-label patch: VPE's DoPaths IL did not match; keeping the vanilla \"Locked:\" label.");
            return list;
        }

        // ---- Modern Psycasts UI (soft, reflection only) ----------------------------------------
        // Its DrawTreeTile paints a bare "VPE.Locked".Translate() over locked tiles. Swap the ldstr
        // for a call to LockedKey() (same stack shape: pushes one string) so the label follows the
        // same setting as the VPE-native tab. No Modern UI edit needed; absent mod = no-op.
        public static void TryWireModernUI(Harmony harmony)
        {
            try
            {
                var drawer = AccessTools.TypeByName("ModernPsycastsUI.ModernPsycastsDrawer");
                var tile = drawer == null ? null : AccessTools.Method(drawer, "DrawTreeTile");
                if (tile == null) return;   // Modern Psycasts UI absent or reshaped
                harmony.Patch(tile, transpiler: new HarmonyMethod(typeof(Patch_LockedNoColon), nameof(ModernTranspiler)));
            }
            catch (System.Exception e)
            {
                Log.Warning("[Psycasts²] Undiscovered label: could not wire Modern Psycasts UI: " + e.Message);
            }
        }

        public static IEnumerable<CodeInstruction> ModernTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            var helper = AccessTools.Method(typeof(Patch_LockedNoColon), nameof(LockedKey));
            int swapped = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode != OpCodes.Ldstr || (list[i].operand as string) != "VPE.Locked") continue;
                list[i].opcode = OpCodes.Call;   // in-place: any labels/blocks on this instruction survive
                list[i].operand = helper;
                swapped++;
            }
            if (swapped == 0)
                Log.Warning("[Psycasts²] Undiscovered label: Modern Psycasts UI's locked-tile label did not match; it keeps reading \"Locked\".");
            return list;
        }
    }
}

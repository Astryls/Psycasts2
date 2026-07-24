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
    //   - our progression ON: the button reads just "Locked" (VPE's own translated key);
    //   - our progression OFF: genuine reasons still show, only the dangling colon is dropped.
    [HarmonyPatch(typeof(ITab_Pawn_Psycasts), "DoPaths")]
    public static class Patch_LockedNoColon
    {
        // Signature must exactly mirror string.Concat(string, string, string) so the retargeted
        // call site keeps a valid stack transition.
        public static string LockedLabel(string locked, string sep, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return locked;
            if (PsycastSynergiesMod.Settings?.lockPathsToEnlightenment == true) return locked;
            return locked + sep + reason;
        }

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
    }
}

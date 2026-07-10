#nullable disable
using HarmonyLib;
using RimWorld;

namespace PsycastSynergies
{
    // Optional removal of psyfocus decay. Vanilla Pawn_PsychicEntropyTracker drains currentPsyfocus toward 0
    // each update by PsyfocusFallPerDay (a band-based fall rate). When "Remove psyfocus decay" is ON (default)
    // we force that rate to 0, so a pawn's psyfocus only ever changes from meditation (gain) or casting (spend)
    // - it never bleeds away while idle. PsyfocusFallPerDay is consumed ONLY by the decay tick (the psyfocus
    // tooltip reads FallRatePerPsyfocusBand directly), so this override is clean and display-safe. The getter
    // already returns 0f for an unlinked pawn, so 0 is a value the rest of the tracker already handles.
    [HarmonyPatch(typeof(Pawn_PsychicEntropyTracker), "PsyfocusFallPerDay", MethodType.Getter)]
    public static class Patch_NoPsyfocusDecay
    {
        static void Postfix(ref float __result)
        {
            if (PsycastSynergiesMod.Settings != null && PsycastSynergiesMod.Settings.noPsyfocusDecay)
                __result = 0f;
        }
    }
}

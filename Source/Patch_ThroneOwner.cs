#nullable disable
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PsycastSynergies
{
    // Remove the royal-title requirement on THRONE OWNERSHIP. Vanilla CompAssignableToPawn_Throne lists only
    // titled colonists (royalty != null && has/can-update a title) as assignment candidates, so the "Set owner"
    // gizmo is disabled with "Throne ownership requires a person with a royal title" when you have no nobles.
    // We widen the candidate list to every free colonist. Pawn_Ownership.ClaimThrone has no title check, so
    // this is safe; titled nobles remain in the list (still sorted assignable-first), and Reigning/throne-room
    // mechanics are untouched - this only changes who the assignment gizmo will offer.
    [HarmonyPatch(typeof(CompAssignableToPawn_Throne), "get_AssigningCandidates")]
    public static class Patch_ThroneAnyOwner
    {
        static void Postfix(CompAssignableToPawn_Throne __instance, ref IEnumerable<Pawn> __result)
        {
            if (__instance?.parent == null || !__instance.parent.Spawned) return;
            __result = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists
                .Where(p => p.IsColonist)
                .OrderByDescending(p => __instance.CanAssignTo(p).Accepted);
        }
    }
}

#nullable disable
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PsycastSynergies
{
    // Adds a per-pawn toggle to enable/disable the cosmetic ascension aura. Shown only on player
    // colonists who currently carry one of the ascension hediffs.
    [StaticConstructorOnStartup]   // has a static Texture2D field; must load on the main thread
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_AuraToggleGizmo
    {
        private static readonly Texture2D Icon = ContentFinder<Texture2D>.Get("UI/Gizmos/aura_toggle", false);

        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (var g in __result) yield return g;

            if (__instance == null || !__instance.IsColonistPlayerControlled) yield break;
            var hs = __instance.health?.hediffSet;
            if (hs == null) yield break;
            if (!hs.HasHediff(PsycastDefOf.PS_TranquilMind)
                && !hs.HasHediff(PsycastDefOf.PS_ArchotechAscendant)
                && !hs.HasHediff(PsycastDefOf.PS_UmbralSovereign)) yield break;

            var d = GameComponent_PsycastSynergies.Instance?.GetSpec(__instance, create: true);
            if (d == null) yield break;

            yield return new Command_Toggle
            {
                defaultLabel = "PS_AuraLabel".Translate(),
                defaultDesc = "PS_AuraDesc".Translate(),
                icon = Icon ?? BaseContent.BadTex,
                isActive = () => !d.aurasDisabled,
                toggleAction = () => d.aurasDisabled = !d.aurasDisabled
            };
        }
    }
}

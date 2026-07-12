#nullable disable
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;
using VEF.Abilities;
using AbilityDef = VEF.Abilities.AbilityDef;
using Ability = VEF.Abilities.Ability;

namespace PsycastSynergies
{
    // Targets synergy type: +1 target per level (for multi-select abilities, targetCount > 1).
    // We set def.targetCount = authored + extra every time targeting starts - idempotent, so it
    // never compounds, and it's recomputed for whichever pawn is currently casting. The parallel
    // targetModes / targetingParametersList are padded (repeating the last entry) so the extra
    // targets are valid and never index out of range.
    [HarmonyPatch(typeof(Ability), nameof(Ability.DoAction))]
    public static class Patch_TargetsBump
    {
        private static readonly Dictionary<AbilityDef, int> authoredCount = new Dictionary<AbilityDef, int>();

        static void Prefix(Ability __instance)
        {
            try
            {
                var def = __instance.def;
                var pawn = __instance.pawn;
                if (def == null || pawn == null) return;
                // Right-click toggles autocast (not a targeting start) - leave targetCount alone.
                if (Event.current != null && Event.current.button == 1) return;
                if (!PsycastInfo.HasStat(def, SynStat.Targets)) return;   // multi-target only (own OR synergy)

                if (!authoredCount.TryGetValue(def, out int orig)) { orig = def.targetCount; authoredCount[def] = orig; }

                float m = SkillSystem.StatMultiplier(pawn, def, SynStat.Targets);
                int extra = Mathf.Clamp(Mathf.RoundToInt((m - 1f) / PsycastSynergiesMod.Settings.perLevelPct), 0, 10);
                int newCount = orig + extra;
                // Paired-target abilities (Skip-style, iterated i+=2) need an even targetCount or the cast
                // loop indexes out of range; preserve the authored parity.
                if (orig % 2 == 0 && newCount % 2 != 0) newCount--;
                def.targetCount = newCount;

                if (def.targetModes != null)
                    while (def.targetModes.Count < newCount && def.targetModes.Count > 0)
                        def.targetModes.Add(def.targetModes[def.targetModes.Count - 1]);
                if (def.targetingParametersList != null)
                    while (def.targetingParametersList.Count < newCount && def.targetingParametersList.Count > 0)
                        def.targetingParametersList.Add(def.targetingParametersList[def.targetingParametersList.Count - 1]);
            }
            catch { }
        }
    }
}

#nullable disable
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using Verse;
using Ability = VEF.Abilities.Ability;
using AbilityDef = VEF.Abilities.AbilityDef;
using AbilityTargetingMode = VEF.Abilities.AbilityTargetingMode;

namespace PsycastSynergies
{
    // Multi-target: an ability with the Targets ("multi-strike") synergy lets the player pick EXTRA
    // targets in the targeting UI; VEF then casts at all of them. We pad def.targetCount + its
    // targetModes / targetingParametersList right before targeting, and restore after the cast (or the
    // next targeting attempt). Only one ability is targeted at a time, so one pending slot is enough.
    [HarmonyPatch(typeof(Ability), "DoTargeting")]
    public static class Patch_MultiTarget
    {
        private static AbilityDef padded;
        private static int origCount, origModes, origParams;

        // DoTargeting is the real targeting entry (the gizmo path doesn't always go through DoAction)
        // and it recurses per pick - only act on the FIRST call of a session (currentTargetingIndex < 0).
        static void Prefix(Ability __instance)
        {
            if (__instance == null || __instance.currentTargetingIndex >= 0) return;
            RestorePending();   // clean up a previous (possibly cancelled) targeting first
            var def = __instance.def;
            var pawn = __instance.pawn;
            if (def == null || pawn == null) return;
            if (def.worldTargeting) return;
            var modes = def.targetModes;
            if (modes == null || modes.Count == 0 || modes[0] == AbilityTargetingMode.Self) return;
            // VEF treats an AoE with (2 targets, 2nd = Random) specially - don't disturb that.
            if (def.hasAoE && def.targetCount == 2 && modes.Count > 1 && modes[1] == AbilityTargetingMode.Random) return;
            int extra = SkillSystem.ExtraTargets(pawn, def);
            if (extra <= 0) return;
            int n = def.targetCount + extra;
            // Paired-target abilities (VPE Skip-style: currentlyCastingTargets is [thing, destination]
            // pairs iterated i+=2) MUST keep an even targetCount or their cast loop indexes out of bounds.
            // Preserve the authored parity - drop a lone odd extra rather than crash.
            if (def.targetCount % 2 == 0 && n % 2 != 0) n--;
            if (n <= def.targetCount) return;
            Pad(def, n);
            __instance.currentTargets = new GlobalTargetInfo[n];   // resize to the padded count
        }

        private static void Pad(AbilityDef def, int n)
        {
            if (def.targetModes == null || def.targetingParametersList == null || def.targetModes.Count == 0) return;
            padded = def;
            origCount = def.targetCount;
            origModes = def.targetModes.Count;
            origParams = def.targetingParametersList.Count;
            var mode0 = def.targetModes[0];
            var par0 = def.targetingParametersList.Count > 0 ? def.targetingParametersList[0] : null;
            def.targetCount = n;
            while (def.targetModes.Count < n) def.targetModes.Add(mode0);
            while (def.targetingParametersList.Count < n) def.targetingParametersList.Add(par0);
        }

        public static void RestorePending()
        {
            var def = padded;
            if (def == null) return;
            padded = null;
            def.targetCount = origCount;
            if (def.targetModes != null)
                while (def.targetModes.Count > origModes) def.targetModes.RemoveAt(def.targetModes.Count - 1);
            if (def.targetingParametersList != null)
                while (def.targetingParametersList.Count > origParams) def.targetingParametersList.RemoveAt(def.targetingParametersList.Count - 1);
        }
    }

    // AoE multi-target fix: VEF's ModifyTargets expands the AoE around targets[0] ONLY, so picking
    // extra centers does nothing. When an AoE ability has >1 picked center, expand the area around
    // EACH center and union them, so every pick gets the blast.
    [HarmonyPatch(typeof(Ability), "ModifyTargets")]
    public static class Patch_AoEMultiTarget
    {
        private static readonly MethodInfo miAround = AccessTools.Method(typeof(Ability), "GetTargetsAround");
        private static readonly FieldInfo fiAoE = AccessTools.Field(typeof(Ability), "currentAoETargeting");

        static bool Prefix(Ability __instance, ref GlobalTargetInfo[] targets)
        {
            var def = __instance?.def;
            if (def == null || !def.hasAoE) return true;              // non-AoE: VEF returns early anyway
            if (targets == null || targets.Length <= 1) return true;  // single center: let VEF handle it
            if (miAround == null) return true;
            try
            {
                bool prev = fiAoE != null && (bool)fiAoE.GetValue(__instance);
                fiAoE?.SetValue(__instance, true);
                var seen = new HashSet<GlobalTargetInfo>();
                var outList = new List<GlobalTargetInfo>();
                foreach (var t in targets)
                {
                    var around = (IEnumerable<GlobalTargetInfo>)miAround.Invoke(
                        __instance, new object[] { t.Cell, def.targetingParametersForAoE, false });
                    foreach (var g in around) if (seen.Add(g)) outList.Add(g);
                }
                fiAoE?.SetValue(__instance, prev);
                targets = outList.ToArray();
                return false;
            }
            catch { return true; }
        }
    }
}

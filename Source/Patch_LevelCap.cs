#nullable disable
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
using VanillaPsycastsExpanded;

namespace PsycastSynergies
{
    // The psycaster level cap is a FLAT value: PsycastSynergiesMod writes vpeLevelCap (default 400) to VPE's
    // maxLevel via ApplyVpeLevelCap, and VPE's GainExperience enforces it. Enlightenment / Transcendence tiers
    // do NOT change it - EffectiveMax == BaseMax, so these patches collapse to VPE's own native cap.
    [HarmonyPatch(typeof(Hediff_PsycastAbilities), nameof(Hediff_PsycastAbilities.GainExperience))]
    public static class Patch_LevelCapByTier
    {
        private static readonly AccessTools.FieldRef<Hediff_PsycastAbilities, float> Exp =
            AccessTools.FieldRefAccess<Hediff_PsycastAbilities, float>("experience");

        // While the psycast tab draws we temporarily raise VPE's global maxLevel (so Modern UI shows the XP bar
        // + dev Level-up button past 30). This holds the REAL base so the cap math never double-extends.
        internal static int drawRealMax = -1;

        public static int BaseMax() => drawRealMax >= 0 ? drawRealMax : PsycastsMod.Settings.maxLevel;

        // Flat cap: tiers do NOT extend it (the cap lives in VPE's maxLevel, set to vpeLevelCap).
        public static int EffectiveMax(Hediff_PsycastAbilities h) => BaseMax();

        static bool Prefix(Hediff_PsycastAbilities __instance, float experienceGain, bool sendLetter)
        {
            int baseMax = BaseMax();
            if (__instance.level < baseMax) return true;          // below the base cap: vanilla handles it
            int eff = EffectiveMax(__instance);
            if (__instance.level >= eff) return false;             // at the tier-extended ceiling: no leveling
            // Extended band [baseMax, eff): replicate VPE's level-up loop with the higher ceiling.
            Exp(__instance) += experienceGain;
            bool sent = false;
            while (__instance.level < eff &&
                   Exp(__instance) >= Hediff_PsycastAbilities.ExperienceRequiredForLevel(__instance.level + 1))
            {
                __instance.ChangeLevel(1, sendLetter && !sent); sent = true;
                Exp(__instance) -= Hediff_PsycastAbilities.ExperienceRequiredForLevel(__instance.level);
            }
            return false;
        }
    }

    // Make Modern Psycasts UI reflect the extended cap: for the duration of the tab draw (panel-height calc AND
    // the left panel), raise VPE's maxLevel to the selected pawn's tier-extended cap, then restore. Without this
    // the XP bar + dev Level-up button vanish at 30 even though leveling past 30 now works - which read as a bug.
    [HarmonyPatch]
    public static class Patch_LevelCapUI
    {
        static bool Prepare() => AccessTools.TypeByName("ModernPsycastsUI.ModernPsycastsDrawer") != null;

        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("ModernPsycastsUI.ModernPsycastsDrawer");
            return t == null ? null : AccessTools.Method(t, "TryDraw");
        }

        static void Prefix()
        {
            if (PsycastSynergiesMod.Settings == null) return;
            var p = Find.Selector?.SingleSelectedThing as Pawn;
            var h = p == null ? null : p.Psycasts();
            if (h == null) return;
            Patch_LevelCapByTier.drawRealMax = PsycastsMod.Settings.maxLevel;
            PsycastsMod.Settings.maxLevel = Patch_LevelCapByTier.EffectiveMax(h);
        }

        static void Finalizer()
        {
            if (Patch_LevelCapByTier.drawRealMax >= 0)
            {
                PsycastsMod.Settings.maxLevel = Patch_LevelCapByTier.drawRealMax;
                Patch_LevelCapByTier.drawRealMax = -1;
            }
        }
    }
}

#nullable disable
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using VanillaPsycastsExpanded;

namespace PsycastSynergies
{
    // VPE lets a psycaster spend levels (points) on "stat upgrades": ImproveStats raises a private statPoints,
    // and RecacheCurStage turns statPoints into stat offsets - neural-heat limit, neural-heat recovery, psychic
    // sensitivity, AND a psyfocus-cost reduction (VPE_PsyfocusCostFactor = statPoints * -0.01).
    //
    // We rework this (setting "Auto psycaster stats", default on): the three useful stats grow AUTOMATICALLY
    // per psycaster level (the per-point amounts, granted once per level, on top of VPE's level base), the
    // psyfocus cost is left UNAFFECTED (offset forced to 0), and statPoints is cleared so there is no manual
    // spending and the points stay free for this mod's skill-level investment. A POSTFIX overrides the offset
    // values, so the result is identical regardless of any old statPoints a save carried.
    [HarmonyPatch(typeof(Hediff_PsycastAbilities), "RecacheCurStage")]
    public static class Patch_AutoStatsPerLevel
    {
        private static readonly AccessTools.FieldRef<Hediff_PsycastAbilities, int> StatPoints =
            AccessTools.FieldRefAccess<Hediff_PsycastAbilities, int>("statPoints");

        private static StatDef costFactor;
        private static StatDef CostFactor => costFactor != null
            ? costFactor : (costFactor = DefDatabase<StatDef>.GetNamedSilentFail("VPE_PsyfocusCostFactor"));

        static void Postfix(Hediff_PsycastAbilities __instance)
        {
            if (PsycastSynergiesMod.Settings == null || !PsycastSynergiesMod.Settings.autoPsycasterStats) return;
            StatPoints(__instance) = 0;   // no manual stat spending; saves clean
            var offs = __instance.CurStage?.statOffsets;
            if (offs == null) return;
            int lvl = __instance.level;
            Pawn p = __instance.pawn;
            var gc = GameComponent_PsycastSynergies.Instance;
            // Tracked pawns (awakened colonists) use the non-retroactive, tier-scaled accumulator. Untracked
            // psycasters (enemies, pre-awakening colonists) use the flat tier-0 rate = the original behavior.
            var data = gc?.GetMed(p, false);
            float heat, rec, sens;
            if (data != null)
            {
                AutoStats.Accumulate(data, lvl, EnlightenmentTier.TierOf(p));
                heat = data.autoHeat; rec = data.autoRecovery; sens = data.autoSensitivity;
            }
            else { heat = lvl * AutoStats.HeatBase; rec = lvl * AutoStats.RecBase; sens = lvl * AutoStats.SensBase; }
            for (int i = 0; i < offs.Count; i++)
            {
                var so = offs[i];
                if (so.stat == StatDefOf.PsychicEntropyMax) so.value = heat;                       // base 5 + auto per level
                else if (so.stat == StatDefOf.PsychicEntropyRecoveryRate) so.value = rec;            // base .0125 + auto per level
                else if (so.stat == StatDefOf.PsychicSensitivity) so.value = sens;                  // auto per level
                else if (CostFactor != null && so.stat == CostFactor) so.value = 0f;                // psyfocus cost UNAFFECTED
            }
        }
    }

    // Modern Psycasts UI draws an "Upgrade - Psycaster Stats" button (UIStyle.Button) wired to manual stat
    // spending, which no longer does anything. Replace JUST that one button (matched by its exact label) with
    // an explanatory note; every other UIStyle.Button is untouched. No-ops cleanly if Modern Psycasts UI is absent.
    [HarmonyPatch]
    public static class Patch_HideStatUpgradeButton
    {
        static bool Prepare() => AccessTools.TypeByName("ModernPsycastsUI.UIStyle") != null;

        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("ModernPsycastsUI.UIStyle");
            return t == null ? null : AccessTools.Method(t, "Button", new[] { typeof(Rect), typeof(string), typeof(bool) });
        }

        static bool Prefix(Rect r, string label, ref bool __result)
        {
            if (PsycastSynergiesMod.Settings == null || !PsycastSynergiesMod.Settings.autoPsycasterStats) return true;
            // Match on the "Psycaster Stats" portion only - the separator (em-dash vs hyphen) varies, and only
            // this one button's label contains it, so Contains is both robust and unambiguous.
            string ps = "VPE.PsycasterStats".Translate();
            if (string.IsNullOrEmpty(label) || !label.Contains(ps)) return true;
            var pf = Text.Font; var pa = Text.Anchor; var pc = GUI.color;
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = new Color(0.62f, 0.65f, 0.7f);
            Widgets.Label(r, "PS_AutoStatsNote".Translate());
            Text.Font = pf; Text.Anchor = pa; GUI.color = pc;
            __result = false;   // not clicked, not drawn as a button
            return false;
        }
    }
}

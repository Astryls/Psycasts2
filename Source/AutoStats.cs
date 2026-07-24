#nullable disable
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using VanillaPsycastsExpanded.UI;

namespace PsycastSynergies
{
    // Per-level auto-stat amounts and the non-retroactive, enlightenment-scaled accumulator.
    // Base per-level gains are the tier-0 rate. Each enlightenment tier multiplies the rate by
    // (1 + 0.15*tier), applied ONLY to levels gained while at that tier (non-retroactive), so a level
    // earned at a higher tier is permanently worth more than one earned earlier at a lower tier.
    public static class AutoStats
    {
        public const float HeatBase = 15f;       // PsychicEntropyMax per level
        public const float RecBase  = 0.0625f;   // PsychicEntropyRecoveryRate per level
        public const float SensBase = 0.0025f;   // PsychicSensitivity per level (tuned down from 0.05)

        public static float RateMult(int tier) => 1f + 0.15f * Mathf.Max(0, tier);

        // Advance a pawn's accumulators to their current level, charging each NEW level at the tier they
        // are at right now. First sighting seeds existing levels at the tier-0 rate (no retroactive bonus).
        public static void Accumulate(MeditationData d, int curLevel, int tier)
        {
            if (d == null) return;
            if (!d.autoInit)
            {
                d.autoHeat = curLevel * HeatBase;
                d.autoRecovery = curLevel * RecBase;
                d.autoSensitivity = curLevel * SensBase;
                d.autoLevel = curLevel;
                d.autoInit = true;
                return;
            }
            if (curLevel < d.autoLevel) d.autoLevel = curLevel;   // level dropped (respec/reset) - clamp the marker
            float mult = RateMult(tier);
            while (d.autoLevel < curLevel)
            {
                d.autoLevel++;
                d.autoHeat += HeatBase * mult;
                d.autoRecovery += RecBase * mult;
                d.autoSensitivity += SensBase * mult;
            }
        }

        // Per-level BASE for a stat (tier-0 rate). 0 = not auto-scaled.
        public static float BaseFor(StatDef stat)
        {
            if (stat == StatDefOf.PsychicEntropyMax) return HeatBase;
            if (stat == StatDefOf.PsychicEntropyRecoveryRate) return RecBase;
            if (stat == StatDefOf.PsychicSensitivity) return SensBase;
            return 0f;
        }

        // Signed, stat-aware value string (percent for sensitivity, raw otherwise).
        public static string ValStr(StatDef stat, float v)
        {
            if (stat == StatDefOf.PsychicSensitivity) return "+" + (v * 100f).ToString("0.##") + "%";
            if (stat == StatDefOf.PsychicEntropyRecoveryRate) return "+" + v.ToString("0.0##");
            return "+" + v.ToString("0.#");
        }

        // The TOTAL our auto-growth has contributed to a stat: the live accumulator for tracked pawns,
        // or the flat level*base for untracked ones (enemies / pre-awakening).
        public static float Total(Pawn p, StatDef stat, int lvl)
        {
            var d = GameComponent_PsycastSynergies.Instance?.GetMed(p, false);
            if (d != null && d.autoInit)
            {
                if (stat == StatDefOf.PsychicEntropyMax) return d.autoHeat;
                if (stat == StatDefOf.PsychicEntropyRecoveryRate) return d.autoRecovery;
                if (stat == StatDefOf.PsychicSensitivity) return d.autoSensitivity;
            }
            return lvl * BaseFor(stat);
        }

        // Mouseover breakdown: the per-level gain at each enlightenment tier (current marked) + the running total.
        public static string Tooltip(Pawn p, StatDef stat, int lvl)
        {
            float b = BaseFor(stat); if (b <= 0f) return null;
            int tier = EnlightenmentTier.TierOf(p);
            var sb = new StringBuilder();
            sb.AppendLine("Auto-growth per psycaster level (Psycasts\u00b2)").AppendLine();
            sb.AppendLine("Gain per level, by enlightenment tier:");
            int maxT = Mathf.Max(3, tier);
            for (int t = 0; t <= maxT; t++)
            {
                string nm = EnlightenmentTier.Name(t);
                sb.AppendLine("  Tier " + t + (nm.Length > 0 ? " " + nm : "") + ":  " + ValStr(stat, b * RateMult(t)) + (t == tier ? "   (current)" : ""));
            }
            sb.AppendLine().AppendLine("Total gained over " + lvl + " levels:  " + ValStr(stat, Total(p, stat, lvl)));
            sb.Append("Each tier raises the per-level rate by 15%, applied to new levels only - past levels keep their tier's rate.");
            return sb.ToString();
        }
    }

    // #1: append the per-level growth "(+N/lvl)" to each auto-scaled stat row in Modern Psycasts UI's
    // left panel, right-aligned in the row. Postfix the private DrawStatRow; no-ops if the UI is absent.
    [HarmonyPatch]
    public static class Patch_StatRowPerLevel
    {
        static bool Prepare() => AccessTools.TypeByName("ModernPsycastsUI.ModernPsycastsDrawer") != null;

        static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("ModernPsycastsUI.ModernPsycastsDrawer");
            return t == null ? null : AccessTools.Method(t, "DrawStatRow");
        }

        static void Postfix(Rect c, float y, StatDef stat)
        {
            if (PsycastSynergiesMod.Settings == null || !PsycastSynergiesMod.Settings.autoPsycasterStats) return;
            var hediff = PsycastsUIUtility.Hediff;
            Pawn p = hediff?.pawn;
            if (p == null || AutoStats.BaseFor(stat) <= 0f) return;
            int lvl = hediff.level;
            float total = AutoStats.Total(p, stat, lvl);
            var pf = Text.Font; var pa = Text.Anchor; var pc = GUI.color;
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleRight; GUI.color = new Color(0.55f, 0.78f, 1f, 0.9f);
            Widgets.Label(new Rect(c.x, y, c.width - 2f, 26f), "(" + AutoStats.ValStr(stat, total) + ")");
            Text.Font = pf; Text.Anchor = pa; GUI.color = pc;
            // Mouseover breakdown of the per-level gains and the running total (stacks under the stat's own tip).
            // DEFERRED: the StringBuilder body runs only when the tip actually shows, not per row per frame.
            // (BaseFor > 0 was checked above, so Tooltip can only be null defensively.)
            TooltipHandler.TipRegion(new Rect(c.x, y, c.width, 26f),
                () => AutoStats.Tooltip(p, stat, lvl) ?? "", (int)stat.shortHash + 0x5132A);
        }
    }
}

#nullable disable
using System;
using RimWorld;
using UnityEngine;
using VanillaPsycastsExpanded;
using Verse;

namespace PsycastSynergies
{
    // Compact meditation progress bars for the psycast tab, shown under the focus-type icons in BOTH
    // UIs (VPE native via the FillTab transpiler, Modern Psycasts UI via the DrawFoci postfix - see
    // Patch_MeditationBars). EBD-style rows: a tiny caption + value line over a thin track/fill bar.
    // Rows (adaptive):
    //   1. Coma risk        - meditation today vs the daily safe window (hidden when enlightenment is off)
    //   2. Next level       - VPE psycaster XP toward the next level
    //   3. Pilgrimage       - tier 1-2: pity climb toward the guaranteed pilgrimage offer
    //      or Transcendence - tier 3+: climb toward the next Transcendent tier
    // Perf: bar fractions are computed live each pass (no allocs); all strings are cached on a ~1s
    // quantum (and per language). A draw exception disables the whole block once, with a warning.
    public static class MeditationBars
    {
        private const float BarH = 7f, RowGap = 6f;
        private static readonly Color Violet = new Color(0.72f, 0.5f, 0.95f);

        // Tiny text is silently coerced to Small by Text.Font's setter (accessibility "disable tiny
        // text", Steam Deck, languages without a tiny face). The caption line - and every height that
        // reserves space for it - MUST size off the font that will actually render, or Small glyphs
        // clip in a Tiny-sized row.
        private static GameFont BarFont => Text.TinyFontSupported ? GameFont.Tiny : GameFont.Small;
        private static float CaptionH => Mathf.Ceil(Text.LineHeightOf(Text.TinyFontSupported ? GameFont.Tiny : GameFont.Small));
        private static float RowH => CaptionH + 1f + BarH + RowGap;

        private static readonly PerfCache.LangCache LblComa = new PerfCache.LangCache("PS_BarComa");
        private static readonly PerfCache.LangCache LblLevel = new PerfCache.LangCache("PS_BarLevel");
        private static readonly PerfCache.LangCache LblPilgrim = new PerfCache.LangCache("PS_BarPilgrim");
        private static readonly PerfCache.LangCache LblTranscend = new PerfCache.LangCache("PS_BarTranscend");

        private enum Third { None, Pilgrim, Transcend }

        private static bool broken;   // one-time fuse: a draw exception hides the block instead of breaking the tab

        // ---- string model, rebuilt at most ~once per second (fractions stay live) ----
        private static Pawn mPawn; private static int mQ = -1; private static LoadedLanguage mLang;
        private static bool mQuestOngoing, mPityOff, mMaxed;
        private static string mComaVal, mLevelVal, mThirdVal;

        public static float HeightFor(Pawn p)
        {
            if (broken) return 0f;
            var s = PsycastSynergiesMod.Settings;
            if (s == null || !s.medBars) return 0f;
            if (p == null || p.Psycasts() == null) return 0f;
            int rows = 1;                                          // level row always
            if (s.enlightenmentEnabled) rows++;                    // coma row (risk only exists with the system on)
            if (s.enlightenmentEnabled && ThirdRow(p, s) != Third.None) rows++;
            return 2f + rows * RowH;
        }

        private static Third ThirdRow(Pawn p, PsycastSynergiesSettings s)
        {
            int tier = EnlightenmentTier.TierOf(p);
            if (tier >= 3) return s.transcendEnabled && !TieringControl.TranscendenceDisabled ? Third.Transcend : Third.None;
            if (tier >= 1) return TieringControl.PilgrimagesDisabled ? Third.None : Third.Pilgrim;
            return Third.None;
        }

        public static void DrawInListing(Listing_Standard l, Pawn p)
        {
            float h = HeightFor(p);
            if (h <= 0f) return;
            Draw(l.GetRect(h), p);
        }

        public static void Draw(Rect r, Pawn p)
        {
            if (broken || p == null) return;
            var font = Text.Font; var anchor = Text.Anchor; var color = GUI.color;
            try { DrawInner(r, p); }
            catch (Exception e)
            {
                broken = true;
                Log.Warning("[Psycasts²] Meditation bars disabled after a draw error: " + e);
            }
            finally { Text.Font = font; Text.Anchor = anchor; GUI.color = color; }
        }

        private static void DrawInner(Rect r, Pawn p)
        {
            var s = PsycastSynergiesMod.Settings;
            var psy = p.Psycasts();
            if (s == null || psy == null) return;
            var med = GameComponent_PsycastSynergies.Instance?.GetMed(p, false);
            EnsureModel(p, s, med, psy);

            float y = r.y + 2f;
            Text.Font = BarFont;

            if (s.enlightenmentEnabled)
            {
                float safeTicks = Mathf.Max(0.1f, s.comaSafeHours) * 2500f;
                float comaFrac = (med?.todayTicks ?? 0) / safeTicks;
                Color comaCol = comaFrac >= 1f ? Palette.Bad : comaFrac >= 0.7f ? Palette.Gold : Palette.Good;
                Row(r, ref y, LblComa.Value, mComaVal, comaFrac, comaCol, 1, () => ComaTip(s, med));
            }

            int req = Hediff_PsycastAbilities.ExperienceRequiredForLevel(psy.level + 1);
            float lvlFrac = mMaxed ? 1f : psy.experience / Mathf.Max(1, req);
            Row(r, ref y, LblLevel.Value, mLevelVal, lvlFrac, Palette.Accent, 2, () => "PS_BarLevelTip".Translate());

            if (s.enlightenmentEnabled)
            {
                var third = ThirdRow(p, s);
                if (third == Third.Pilgrim)
                {
                    float need = MeditationSystem.PilgrimPityThresholdTicks(med?.tier ?? 1, s);
                    float frac = mQuestOngoing ? 1f : mPityOff ? 0f : (med?.pilgrimTicks ?? 0) / Mathf.Max(1f, need);
                    Row(r, ref y, LblPilgrim.Value, mThirdVal, frac, Palette.Gold, 3,
                        () => mQuestOngoing ? "PS_BarPilgrimUnderwayTip".Translate()
                            : mPityOff ? "PS_BarPilgrimStorytellerTip".Translate()
                            : "PS_BarPilgrimTip".Translate());
                }
                else if (third == Third.Transcend)
                {
                    float need = MeditationSystem.TranscendThreshold((med?.tier ?? 3) + 1, s);
                    float frac = (med?.transcendTicks ?? 0) / Mathf.Max(1f, need);
                    Row(r, ref y, LblTranscend.Value, mThirdVal, frac, Violet, 4, () => "PS_BarTranscendTip".Translate());
                }
            }
        }

        private static void Row(Rect outer, ref float y, string caption, string val, float frac, Color fill, int id, Func<string> tip)
        {
            var cap = new Rect(outer.x, y, outer.width, CaptionH);
            GUI.color = Palette.TextDim;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(cap, caption);
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(cap, val);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            var bar = new Rect(outer.x, y + CaptionH + 1f, outer.width, BarH);
            Widgets.DrawBoxSolid(bar, Palette.BGD);
            Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, Mathf.Max(3f, bar.width * Mathf.Clamp01(frac)), bar.height), fill);

            var hover = new Rect(outer.x, y, outer.width, CaptionH + BarH + 2f);
            if (Mouse.IsOver(hover))
            {
                Widgets.DrawHighlight(hover);
                TooltipHandler.TipRegion(hover, new TipSignal(tip, 0x50BA2 + id));
            }
            y += RowH;
        }

        private static void EnsureModel(Pawn p, PsycastSynergiesSettings s, MeditationData med, Hediff_PsycastAbilities psy)
        {
            int q = Find.TickManager.TicksGame / 60;
            var lang = LanguageDatabase.activeLanguage;
            if (ReferenceEquals(p, mPawn) && q == mQ && ReferenceEquals(lang, mLang)) return;
            mPawn = p; mQ = q; mLang = lang;

            float today = (med?.todayTicks ?? 0) / 2500f;
            mComaVal = "PS_BarHoursFmt".Translate(today.ToString("F1"), Mathf.Max(0.1f, s.comaSafeHours).ToString("F0"));

            int maxLevel = PsycastsMod.Settings?.maxLevel ?? 30;
            mMaxed = psy.level >= maxLevel;
            mLevelVal = mMaxed
                ? "PS_BarMax".Translate().ToString()
                : "PS_BarXpFmt".Translate(psy.experience.ToString("F0"),
                    Hediff_PsycastAbilities.ExperienceRequiredForLevel(psy.level + 1).ToString());

            int tier = med?.tier ?? 0;
            mQuestOngoing = false; mPityOff = false; mThirdVal = "";
            if (tier >= 3)
            {
                mThirdVal = "PS_BarHoursFmt".Translate(((med?.transcendTicks ?? 0) / 2500f).ToString("F1"),
                    (MeditationSystem.TranscendThreshold(tier + 1, s) / 2500f).ToString("F0"));
            }
            else if (tier >= 1)
            {
                mQuestOngoing = MeditationSystem.PilgrimQuestOngoing(tier + 1);
                float need = MeditationSystem.PilgrimPityThresholdTicks(tier, s);
                mPityOff = need <= 0f;
                mThirdVal = mQuestOngoing ? "PS_BarPilgrimUnderway".Translate().ToString()
                    : mPityOff ? "PS_BarPilgrimStoryteller".Translate().ToString()
                    : "PS_BarHoursFmt".Translate(((med?.pilgrimTicks ?? 0) / 2500f).ToString("F1"), (need / 2500f).ToString("F0")).ToString();
            }
        }

        // Mirrors RollHourly's coma formula so the tooltip states the CURRENT hourly risk.
        private static string ComaTip(PsycastSynergiesSettings s, MeditationData med)
        {
            float todayH = (med?.todayTicks ?? 0) / 2500f;
            float streakH = (med?.streakTicks ?? 0) / 2500f;
            float risk = Mathf.Max(0f, (todayH - s.comaSafeHours) * s.comaRiskPerHour)
                       + Mathf.Max(0f, (streakH - s.comaSafeHours) * s.comaRiskPerHour * 0.5f)
                       + (med?.rerollCount ?? 0) * 0.06f;
            risk = Mathf.Min(risk, 0.75f);
            return "PS_BarComaTip".Translate(todayH.ToString("F1"), s.comaSafeHours.ToString("F0"),
                (risk * 100f).ToString("F0") + "%", streakH.ToString("F1"));
        }
    }
}

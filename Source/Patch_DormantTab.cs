#nullable disable
using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using VanillaPsycastsExpanded;
using VanillaPsycastsExpanded.UI;
using Verse;

namespace PsycastSynergies
{
    // ============================ ALWAYS-ON PSYCAST TAB ============================
    // VPE gates its psycast tab on the psycast IMPLANT hediff (not on psylink level), so a colonist
    // who has not awakened yet has no tab at all - there is nowhere to see how close they are to
    // Awakening, and the pawn reads as "psycasts are not a thing for me". With alwaysShowPsycastTab
    // on, every player humanlike gets the tab; pawns who are not psycasters yet see a compact
    // "dormant" card instead of VPE's (empty) layout.
    //
    // Two patches:
    //   1. IsVisible postfix - force the tab open for our own humanlikes.
    //   2. FillTab prefix (Priority.First, returns false) - draw the dormant card and skip BOTH
    //      VPE's own FillTab and Modern Psycasts UI's prefix (which bails on a null hediff anyway,
    //      leaving a blank panel). Postfixes still run; our tool-block postfix already no-ops
    //      without a psycast hediff.
    // Plus an UpdateSize postfix (Priority.Last so it wins over Modern Psycasts UI's own sizing)
    // that shrinks the tab to the card while the pawn is dormant.
    internal static class DormantTab
    {
        internal const float CardW = 460f, CardH = 268f;

        internal static bool Enabled => PsycastSynergiesMod.Settings?.alwaysShowPsycastTab == true;

        // The selected pawn, or null when the selection is not a single player humanlike old enough to
        // awaken (the age gate only ever suppresses the DORMANT card - a real psycaster's tab is VPE's
        // own IsVisible result and is never taken away).
        internal static Pawn OurPawn()
        {
            var p = Find.Selector?.SingleSelectedThing as Pawn;
            if (p == null || p.Dead || p.RaceProps == null || !p.RaceProps.Humanlike) return null;
            if (p.Faction == null || !p.Faction.IsPlayer) return null;
            if (p.ageTracker != null && p.ageTracker.AgeBiologicalYears < 13) return null;
            return p;
        }

        // True when the tab should show the dormant card instead of the psycast layout.
        internal static bool DormantNow()
        {
            if (!Enabled) return false;
            var p = OurPawn();
            return p != null && p.Psycasts() == null;
        }
    }

    [HarmonyPatch(typeof(ITab_Pawn_Psycasts), nameof(ITab_Pawn_Psycasts.IsVisible), MethodType.Getter)]
    public static class Patch_DormantTabVisible
    {
        public static void Postfix(ref bool __result)
        {
            if (__result || !DormantTab.Enabled) return;
            if (DormantTab.OurPawn() != null) __result = true;
        }
    }

    // VPE's UpdateSize makes the tab as wide as the screen (Modern Psycasts UI clamps it to ~868).
    // A dormant pawn has nothing to fill that with, so size the tab to the card itself.
    [HarmonyPatch(typeof(ITab_Pawn_Psycasts), "UpdateSize")]
    public static class Patch_DormantTabSize
    {
        private static readonly AccessTools.FieldRef<InspectTabBase, Vector2> SizeF =
            AccessTools.FieldRefAccess<InspectTabBase, Vector2>("size");

        [HarmonyPriority(Priority.Last)]   // runs after Modern Psycasts UI's own size postfix
        public static void Postfix(ITab_Pawn_Psycasts __instance)
        {
            if (!DormantTab.DormantNow()) return;
            try { SizeF(__instance) = new Vector2(DormantTab.CardW, DormantTab.CardH); } catch { }
        }
    }

    [HarmonyPatch(typeof(ITab_Pawn_Psycasts), "FillTab")]
    public static class Patch_DormantTabFill
    {
        private static readonly PerfCache.LangCache LblTitle = new PerfCache.LangCache("PS_DormantTitle");
        private static readonly PerfCache.LangCache LblDesc = new PerfCache.LangCache("PS_DormantDesc");
        private static readonly PerfCache.LangCache LblNoMed = new PerfCache.LangCache("PS_DormantNoMeditation");
        private static readonly PerfCache.LangCache LblBar = new PerfCache.LangCache("PS_DormantAwakening");
        private static readonly PerfCache.LangCache LblBarTip = new PerfCache.LangCache("PS_DormantAwakeningTip");
        private static readonly PerfCache.LangCache LblChanceOnly = new PerfCache.LangCache("PS_DormantChanceOnly");

        // Tiny text is silently coerced to Small when tiny is unsupported (accessibility pref /
        // language), so the caption row must size off the font that will actually render.
        private static GameFont BarFont => Text.TinyFontSupported ? GameFont.Tiny : GameFont.Small;

        [HarmonyPriority(Priority.First)]
        public static bool Prefix(ITab_Pawn_Psycasts __instance)
        {
            if (!DormantTab.Enabled) return true;
            Pawn p = DormantTab.OurPawn();
            if (p == null || p.Psycasts() != null) return true;   // real psycaster: hand off to VPE / Modern UI
            try
            {
                Draw(new Rect(Vector2.zero, __instance.Size), p);
                return false;                                     // skip VPE's own (empty) layout
            }
            catch (Exception e)
            {
                Log.Warning("[Psycasts²] dormant psycast tab draw failed: " + e);
                return true;                                       // fall back to VPE's tab rather than a black hole
            }
        }

        private static void Draw(Rect full, Pawn p)
        {
            var prevF = Text.Font; var prevA = Text.Anchor; var prevC = GUI.color;
            MXStyle.Fill(full, MXStyle.Backdrop);
            Rect card = full.ContractedBy(12f);
            MXStyle.Fill(card, MXStyle.Panel);
            MXStyle.Border(card);
            Rect c = card.ContractedBy(14f);
            float y = c.y;

            // Header: pawn name + the dormant state.
            Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.UpperLeft; GUI.color = MXStyle.Fg;
            Widgets.Label(new Rect(c.x, y, c.width, 32f), p.Name.ToStringShort);
            y += 32f;
            Text.Font = GameFont.Small; GUI.color = Palette.Gold;
            Widgets.Label(new Rect(c.x, y, c.width, 24f), LblTitle.Value);
            y += 26f;

            var s = PsycastSynergiesMod.Settings;
            bool medRoute = s != null && s.enlightenmentEnabled && !TieringControl.MeditationAwakeningDisabled;

            // Body: how this pawn can awaken at all.
            GUI.color = MXStyle.TextDim;
            string body = medRoute ? LblDesc.Value : LblNoMed.Value;
            float bodyH = Text.CalcHeight(body, c.width);
            Widgets.Label(new Rect(c.x, y, c.width, bodyH), body);
            y += bodyH + 10f;
            GUI.color = Color.white;

            // Progress: cumulative meditation toward the guaranteed Awakening (the insight threshold
            // itself stays hidden by design - only the guarantee window is shown).
            if (medRoute)
            {
                var med = GameComponent_PsycastSynergies.Instance?.GetMed(p, false);
                float hours = (med?.awakenMeditationTicks ?? 0) / 2500f;
                float need = s.awakenGuaranteeHours;
                bool guaranteed = need > 0f;
                string val = guaranteed
                    ? "PS_BarHoursFmt".Translate(hours.ToString("F1"), need.ToString("F0")).ToString()
                    : LblChanceOnly.Value;
                float frac = guaranteed ? Mathf.Clamp01(hours / need) : 0f;

                Text.Font = BarFont;
                float capH = Mathf.Ceil(Text.LineHeightOf(BarFont));
                var cap = new Rect(c.x, y, c.width, capH);
                GUI.color = Palette.TextDim;
                Widgets.Label(cap, LblBar.Value);
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(cap, val);
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;

                var bar = new Rect(c.x, y + capH + 1f, c.width, 7f);
                Widgets.DrawBoxSolid(bar, Palette.BGD);
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, Mathf.Max(3f, bar.width * frac), bar.height), Palette.Accent);
                var hover = new Rect(c.x, y, c.width, capH + 9f);
                if (Mouse.IsOver(hover))
                {
                    Widgets.DrawHighlight(hover);
                    TooltipHandler.TipRegion(hover, (string)LblBarTip);
                }
                y += capH + 12f;
                Text.Font = GameFont.Small;
            }

            // Dev shortcut: awaken on the spot (same funnel as the XML trigger API).
            if (Prefs.DevMode)
            {
                var r = new Rect(c.x, c.yMax - 28f, Mathf.Min(200f, c.width), 28f);
                if (MXStyle.Button(r, "Dev: awaken now (Tier I)"))
                    AwakeningTrigger.Fire(p, 1);
            }

            Text.Font = prevF; Text.Anchor = prevA; GUI.color = prevC;
        }
    }
}

#nullable disable
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using VEF.Abilities;
using VanillaPsycastsExpanded;
using VanillaPsycastsExpanded.UI;
using AbilityDef = VEF.Abilities.AbilityDef;

namespace PsycastSynergies
{
    // Single universal hook: both VPE's native ITab_Pawn_Psycasts AND Modern Psycasts UI
    // draw every ability-tree icon through PsycastsUIUtility.DrawAbility, and both set
    // PsycastsUIUtility.Hediff / .CompAbilities to the current pawn first. So this one
    // postfix adds level overlays + click-to-invest to both UIs.
    [HarmonyPatch(typeof(PsycastsUIUtility), nameof(PsycastsUIUtility.DrawAbility))]
    public static class Patch_DrawAbility
    {
        static void Postfix(Rect inRect, AbilityDef ability)
        {
            var comp = PsycastsUIUtility.CompAbilities;
            var hediff = PsycastsUIUtility.Hediff;
            if (comp == null || hediff == null || ability == null) return;

            Pawn pawn = hediff.pawn;
            if (pawn == null) return;

            var gc = GameComponent_PsycastSynergies.Instance;
            if (gc == null) return;

            EnsureFrame(pawn, comp, hediff);

            // Synergy highlight: hovering a skill pulses the skills it GAINS power from (its synergy
            // sources); holding Left Ctrl pulses the skills it EMPOWERS instead.
            if (Event.current.type == EventType.Repaint && HoverHighlight.Active && HoverHighlight.def != ability)
            {
                bool related = HoverHighlight.ctrl
                    ? PsycastInfo.SynergySources(ability).Contains(HoverHighlight.def)      // this is empowered BY the hovered skill
                    : PsycastInfo.SynergySources(HoverHighlight.def).Contains(ability);     // this is a SOURCE of the hovered skill
                if (related)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 3.2f));
                    Color col = HoverHighlight.ctrl ? new Color(1f, 0.78f, 0.25f) : new Color(0.3f, 1f, 0.45f);   // empowers=gold, sources=emerald
                    GUI.color = new Color(col.r, col.g, col.b, pulse);
                    Widgets.DrawBox(inRect.ExpandedBy(4f), 3);
                    Widgets.DrawBox(inRect.ExpandedBy(2f), 2);
                    GUI.color = new Color(col.r, col.g, col.b, pulse * 0.25f);
                    Widgets.DrawBox(inRect.ContractedBy(1f), 2);
                    GUI.color = Color.white;
                }
            }

            // Un-learned skills still get the styled card (for tree pre-planning) but no level
            // badge / + / invest-click - VPE keeps its own unlock-click for those.
            bool owned = fLearned.Contains(ability);
            int cap = SkillSystem.MaxLevel(pawn, ability, fPsy);
            // Learning a psycast IS its first level: the single point spent to unlock it both learns the
            // skill AND moves it 0 -> 1. So a learned skill always reads (at least) 1/x and is castable;
            // an unlearned skill stays 0/x and disabled. Baseline owned skills to level 1 lazily on draw.
            if (owned && gc.GetLevel(pawn, ability) <= 0) gc.SetLevel(pawn, ability, 1);
            int lvl = owned ? gc.GetLevel(pawn, ability) : 0;
            bool atCap = lvl >= cap;
            bool hasPoints = hediff.points >= 1;

            if (owned && Event.current.type == EventType.Repaint)
            {
                GameFont pf = Text.Font;
                TextAnchor pa = Text.Anchor;
                Color pc = GUI.color;

                // Roman-numeral level overlay, anchored to the bottom-right corner.
                if (lvl > 0)
                {
                    Text.Font = GameFont.Tiny;
                    string roman = RomanNumerals.ToRoman(lvl);
                    Vector2 sz = RomanSize(lvl, roman);
                    float w = Mathf.Max(14f, sz.x + 6f);
                    float h = Mathf.Max(13f, Mathf.Min(inRect.height, sz.y + 2f));
                    Rect numRect = new Rect(inRect.xMax - w, inRect.yMax - h, w, h);
                    Color lc = atCap ? new Color(1f, 0.85f, 0.3f) : new Color(0.6f, 0.88f, 1f);
                    GUI.color = new Color(0f, 0f, 0f, 0.88f);
                    GUI.DrawTexture(numRect, BaseContent.WhiteTex);
                    GUI.color = new Color(lc.r, lc.g, lc.b, 0.9f);
                    Widgets.DrawBox(numRect, 1);   // bordered chip so level I is clearly visible
                    Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = lc;
                    Widgets.Label(numRect, roman);
                }

                // Green "+" hint in the top-right corner when a point can be spent.
                if (!atCap && hasPoints)
                {
                    Rect plus = new Rect(inRect.xMax - 12f, inRect.y, 12f, 12f);
                    GUI.color = new Color(0.2f, 0.78f, 0.32f, 0.95f);
                    GUI.DrawTexture(plus, BaseContent.WhiteTex);
                    GUI.color = Color.black;
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(plus, "+");
                }

                Text.Font = pf;
                Text.Anchor = pa;
                GUI.color = pc;

                // Level-up / mastery celebration burst (SkillFx) - drawn last so sparks sit on top.
                SkillFx.Draw(inRect, SkillFx.KeySkill(pawn, ability));
            }

            // Styled floating breakdown card (True RPG Inventory look) on hover; we also
            // suppress VPE's own plain tooltip for this exact icon (see Patch_SuppressVpeAbilityTip).
            if (Mouse.IsOver(inRect))
            {
                HoverHighlight.def = ability;
                HoverHighlight.frame = Time.frameCount;
                HoverHighlight.ctrl = Event.current.control;
                SkillTooltip.NotifyHover(pawn, ability, inRect, owned);
            }

            // Click an owned, below-cap icon to invest ONE level. Plain click → styled confirm
            // popup; Shift-click → skip the popup (still just one level).
            if (owned && !atCap && Widgets.ButtonInvisible(inRect, false))
            {
                if (hediff.points < 1)
                {
                    Messages.Message("VPE.NotEnoughPoints".Translate(), MessageTypeDefOf.RejectInput, false);
                }
                else if (Event.current.shift)
                {
                    hediff.SpentPoints(1);
                    int nl = gc.AddLevel(pawn, ability, 1);
                    SkillFx.OnSkillInvest(pawn, ability, nl);
                }
                else
                {
                    Find.WindowStack.Add(new Dialog_ConfirmInvest(pawn, hediff, gc, ability, 1));
                }
            }
        }

        // Per-frame cache: the tab routes EVERY visible icon through this postfix, so doing an
        // O(LearnedAbilities) HasAbility scan + a Psycasts() lookup PER ICON added up to a hitch on
        // high-ability pawns. Build the learned-set + read psy level once per (pawn, frame) instead.
        private static Pawn fPawn; private static int fFrame = -1, fPsy;
        private static HashSet<AbilityDef> fLearned;

        // Cache the level-numeral text size per level (Text.CalcSize per owned icon per frame otherwise).
        private static readonly Dictionary<int, Vector2> romanSizeCache = new Dictionary<int, Vector2>();
        private static Vector2 RomanSize(int lvl, string roman)
        {
            if (romanSizeCache.TryGetValue(lvl, out var s)) return s;
            s = Text.CalcSize(roman);   // Text.Font is Tiny at the call site
            romanSizeCache[lvl] = s;
            return s;
        }

        private static void EnsureFrame(Pawn pawn, CompAbilities comp, Hediff_PsycastAbilities hediff)
        {
            if (fPawn == pawn && fFrame == Time.frameCount && fLearned != null) return;
            fPawn = pawn; fFrame = Time.frameCount; fPsy = hediff?.level ?? 0;
            if (fLearned == null) fLearned = new HashSet<AbilityDef>(); else fLearned.Clear();
            if (comp?.LearnedAbilities != null)
                foreach (var a in comp.LearnedAbilities) if (a?.def != null) fLearned.Add(a.def);
        }

    }

    // Tracks the currently-hovered skill (and whether Left Ctrl is held) so every other icon can
    // decide whether to pulse. Edge-persistent for 1 frame so the highlight doesn't flicker.
    public static class HoverHighlight
    {
        public static AbilityDef def;
        public static int frame = -1000;
        public static bool ctrl;
        public static bool Active => def != null && Time.frameCount - frame <= 1;
    }

    public static class RomanNumerals
    {
        private static readonly int[] Values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        private static readonly string[] Symbols = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };

        public static string ToRoman(int n)
        {
            if (n <= 0) return n.ToString();
            var sb = new StringBuilder();
            for (int i = 0; i < Values.Length && n > 0; i++)
                while (n >= Values[i]) { sb.Append(Symbols[i]); n -= Values[i]; }
            return sb.ToString();
        }
    }

    // If a pawn resets their psycasts (VPE's Reset wipes learned abilities/paths),
    // wipe our invested levels too so they don't linger on re-learned abilities.
    [HarmonyPatch(typeof(Hediff_PsycastAbilities), nameof(Hediff_PsycastAbilities.Reset))]
    public static class Patch_Reset
    {
        static void Postfix(Hediff_PsycastAbilities __instance)
        {
            GameComponent_PsycastSynergies.Instance?.ClearPawn(__instance.pawn);
        }
    }
}

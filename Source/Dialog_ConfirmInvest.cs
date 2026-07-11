#nullable disable
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using VanillaPsycastsExpanded;
using AbilityDef = VEF.Abilities.AbilityDef;

namespace PsycastSynergies
{
    // Signature-style (Palette card) confirmation popup shown when investing a point into a
    // skill via a plain click. Shift-click bypasses this entirely (see Patch_DrawAbility).
    public class Dialog_ConfirmInvest : Window
    {
        private readonly Pawn pawn;
        private readonly Hediff_PsycastAbilities hediff;
        private readonly GameComponent_PsycastSynergies gc;
        private readonly AbilityDef ability;
        private readonly int amount;

        public override Vector2 InitialSize => new Vector2(400f, 224f);

        public Dialog_ConfirmInvest(Pawn pawn, Hediff_PsycastAbilities hediff,
            GameComponent_PsycastSynergies gc, AbilityDef ability, int amount)
        {
            this.pawn = pawn; this.hediff = hediff; this.gc = gc; this.ability = ability; this.amount = amount;
            doWindowBackground = false;       // we paint our own card chrome
            drawShadow = false;               // no vanilla drop shadow (clashes with the flat card)
            doCloseX = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            preventCameraMotion = false;
            draggable = false;
            soundAppear = SoundDefOf.FloatMenu_Open;
        }

        private static string Pct(float f) => (f * 100f).ToString("0.#") + "%";

        public override void DoWindowContents(Rect col)
        {
            Palette.DrawCard(col);
            Widgets.DrawBoxSolid(new Rect(col.x, col.y, 3f, col.height), Palette.Accent);

            Rect hr = new Rect(col.x + 3f, col.y, col.width - 3f, 30f);
            Widgets.DrawBoxSolid(hr, Palette.BGD);
            if (ability.icon != null)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(hr.x + 6f, hr.y + 6f, 18f, 18f), ability.icon);
            }
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = Palette.Accent;
            Widgets.Label(new Rect(hr.x + 28f, hr.y, hr.width - 32f, hr.height), "PS_InvestTitle".Translate());
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;

            int lvl = gc.GetLevel(pawn, ability);
            int newLvl = lvl + amount;
            var s = PsycastSynergiesMod.Settings;

            float y = hr.yMax + 10f;
            GUI.color = Palette.Stat;
            string body = amount > 1
                ? "PS_InvestBodyMulti".Translate(ability.LabelCap, lvl, newLvl, amount).ToString()
                : "PS_InvestBodyOne".Translate(ability.LabelCap, newLvl).ToString();
            Widgets.Label(new Rect(col.x + 12f, y, col.width - 24f, 44f), body);
            y += 48f;

            Text.Font = GameFont.Tiny; GUI.color = Palette.TextDim;
            Widgets.Label(new Rect(col.x + 12f, y, col.width - 24f, 34f),
                "PS_InvestBonus".Translate(Pct(lvl * s.perLevelPct), Pct(newLvl * s.perLevelPct)).ToString());
            Text.Font = GameFont.Small; GUI.color = Color.white;

            float bw = (col.width - 36f) / 2f;
            Rect cancel = new Rect(col.x + 12f, col.yMax - 40f, bw, 30f);
            Rect ok = new Rect(cancel.xMax + 12f, col.yMax - 40f, bw, 30f);
            if (Button(cancel, "PS_Cancel".Translate(), Palette.BGL, Color.white)) Close();
            if (Button(ok, "PS_Invest".Translate(), Palette.Accent, Color.black))
            {
                if (hediff.points >= amount)
                {
                    hediff.SpentPoints(amount);
                    gc.AddLevel(pawn, ability, amount);
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
                else
                {
                    Messages.Message("VPE.NotEnoughPoints".Translate(), MessageTypeDefOf.RejectInput, false);
                }
                Close();
            }
        }

        private static bool Button(Rect r, string label, Color bg, Color fg)
        {
            Widgets.DrawBoxSolid(r, Mouse.IsOver(r) ? Color.Lerp(bg, Color.white, 0.15f) : bg);
            GameFont pf = Text.Font; TextAnchor pa = Text.Anchor; Color pc = GUI.color;
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = fg;
            Widgets.Label(r, label);
            Text.Font = pf; Text.Anchor = pa; GUI.color = pc;
            return Widgets.ButtonInvisible(r);
        }
    }
}

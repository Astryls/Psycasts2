#nullable disable
using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace PsycastSynergies
{
    // Palette-card styled yes/no confirmation, matching the rest of the mod's UI (no vanilla dialog).
    public class Dialog_Confirm : Window
    {
        private readonly string title, body, confirmLabel;
        private readonly Action onConfirm;

        public override Vector2 InitialSize => new Vector2(450f, 210f);

        public Dialog_Confirm(string title, string body, Action onConfirm, string confirmLabel = null)
        {
            this.title = title; this.body = body; this.onConfirm = onConfirm;
            this.confirmLabel = confirmLabel ?? "PS_Confirm".Translate().ToString();
            doWindowBackground = false;
            drawShadow = false;
            doCloseX = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            soundAppear = SoundDefOf.FloatMenu_Open;
        }

        public override void DoWindowContents(Rect col)
        {
            Palette.DrawCard(col);
            Widgets.DrawBoxSolid(new Rect(col.x, col.y, 3f, col.height), Palette.Accent);

            Rect hr = new Rect(col.x + 3f, col.y, col.width - 3f, 30f);
            Widgets.DrawBoxSolid(hr, Palette.BGD);
            var pf = Text.Font; var pa = Text.Anchor;
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = Palette.Accent;
            Widgets.Label(new Rect(hr.x + 10f, hr.y, hr.width - 14f, hr.height), title);
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = Palette.Stat;

            Widgets.Label(new Rect(col.x + 14f, hr.yMax + 10f, col.width - 28f, col.height - 90f), body);
            GUI.color = Color.white;

            float bw = (col.width - 36f) / 2f;
            Rect cancel = new Rect(col.x + 12f, col.yMax - 40f, bw, 30f);
            Rect ok = new Rect(cancel.xMax + 12f, col.yMax - 40f, bw, 30f);
            if (Btn(cancel, "PS_Cancel".Translate(), Palette.BGL, Color.white)) Close();
            if (Btn(ok, confirmLabel, Palette.Accent, Color.black)) { onConfirm?.Invoke(); Close(); }

            Text.Font = pf; Text.Anchor = pa;
        }

        private static bool Btn(Rect r, string label, Color bg, Color fg)
        {
            Widgets.DrawBoxSolid(r, Mouse.IsOver(r) ? Color.Lerp(bg, Color.white, 0.15f) : bg);
            var pf = Text.Font; var pa = Text.Anchor; var pc = GUI.color;
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = fg;
            Widgets.Label(r, label);
            Text.Font = pf; Text.Anchor = pa; GUI.color = pc;
            return Widgets.ButtonInvisible(r);
        }
    }
}

#nullable disable
using UnityEngine;
using Verse;
using Verse.Sound;

namespace PsycastSynergies
{
    // Shared "Modern X Suite" UI style, replicated from Modern Psycasts UI's UIStyle so the awakening
    // window matches the suite look (dark backdrop, blue accent, flat bordered buttons, near-white text)
    // without taking a hard dependency on that mod.
    internal static class MXStyle
    {
        private static Color Hex(int h) => new Color(((h >> 16) & 0xFF) / 255f, ((h >> 8) & 0xFF) / 255f, (h & 0xFF) / 255f);
        private static readonly Color BG  = Hex(0x15191D);
        private static readonly Color BGL = Hex(0x2F3337);

        public static readonly Color Backdrop = Hex(0x0E1013);
        public static readonly Color Panel    = Color.Lerp(BG, BGL, 0.22f);
        public static readonly Color Accent   = new Color(0.45f, 0.75f, 1f, 1f);
        public static readonly Color Fg       = new Color(0.89f, 0.89f, 0.89f, 1f);
        public static readonly Color TextDim  = new Color(0.62f, 0.65f, 0.70f, 1f);
        public static readonly Color BtnNormal = BGL;
        public static readonly Color BtnHover  = Color.Lerp(BGL, Color.white, 0.10f);

        public static void Fill(Rect r, Color c)
        {
            var prev = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, BaseContent.WhiteTex); GUI.color = prev;
        }

        // Black 2px outer + faint 1px inset - the suite's card/panel border.
        public static void Border(Rect r)
        {
            GUI.color = Color.black; Widgets.DrawBox(r, 2);
            GUI.color = new Color(1f, 1f, 1f, 0.055f); Widgets.DrawBox(r.ContractedBy(2f), 1);
            GUI.color = Color.white;
        }

        public static bool Button(Rect r, string label, Texture2D icon = null)
        {
            bool over = Mouse.IsOver(r);
            Fill(r, over ? BtnHover : BtnNormal);
            GUI.color = new Color(Accent.r, Accent.g, Accent.b, over ? 0.9f : 0.5f);
            Widgets.DrawBox(r, 1);
            GUI.color = Color.white;

            var prevA = Text.Anchor; var prevF = Text.Font;
            Text.Font = GameFont.Small;
            const float iconSize = 22f, padIcon = 8f;
            float textW = Text.CalcSize(label).x;
            float total = textW + (icon != null ? iconSize + padIcon : 0f);
            float x = r.center.x - total / 2f;
            if (icon != null)
            {
                GUI.color = over ? Color.white : Accent;
                GUI.DrawTexture(new Rect(x, r.center.y - iconSize / 2f, iconSize, iconSize), icon);
                GUI.color = Color.white;
                x += iconSize + padIcon;
            }
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = over ? Color.white : Fg;
            Widgets.Label(new Rect(x, r.y, textW + 4f, r.height), label);
            GUI.color = Color.white;
            Text.Anchor = prevA; Text.Font = prevF;

            if (over) MouseoverSounds.DoRegion(r);
            return Widgets.ButtonInvisible(r, false);
        }
    }
}

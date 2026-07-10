#nullable disable
using UnityEngine;
using Verse;

namespace PsycastSynergies
{
    // Color palette + card chrome mirrored from the "Modern X" / True RPG Inventory suite,
    // so our skill tooltips match that visual language. No dependency on those DLLs.
    public static class Palette
    {
        public static readonly Color BG = FromHex(0x15191D);   // card background
        public static readonly Color BGL = FromHex(0x2F3337);  // light variant (hover, borders)
        public static readonly Color BGD = FromHex(0x0E1013);  // dark variant (backdrops, headers)
        public static readonly Color Stat = FromHex(0xE3E3E3);  // primary text
        public static readonly Color TextDim = new Color(0.62f, 0.65f, 0.70f);
        public static readonly Color Accent = new Color(0.45f, 0.75f, 1f); // suite azure
        public static readonly Color Border = BGL;
        public static readonly Color PanelBG = Color.Lerp(BG, BGL, 0.22f);

        public static readonly Color Good = new Color(0.4f, 0.85f, 0.4f);
        public static readonly Color Bad = new Color(0.9f, 0.35f, 0.35f);
        public static readonly Color Gold = new Color(0.85f, 0.72f, 0.35f);

        public static Color FromHex(int hex) =>
            new Color(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f);

        public static void DrawCard(Rect r)
        {
            Widgets.DrawBoxSolid(r, PanelBG);
            GUI.color = Border;
            Widgets.DrawBox(r);
            GUI.color = Color.white;
        }
    }
}

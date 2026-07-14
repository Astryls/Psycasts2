#nullable disable
using System;
using System.Reflection;
using RimWorld;
using VanillaPsycastsExpanded;
using Verse;

namespace PsycastSynergies
{
    // Soft bridge into Modern Psycasts UI: its focus-type tiles in the psycast tab become real
    // buttons that read/write our per-pawn default meditation focus (ModernPsycastsUI.PawnFocusHooks).
    // Wired by reflection so neither mod hard-depends on the other - when Modern Psycasts UI is
    // absent, Patch_PsycastTabRespec shows a "Default focus" dropdown fallback instead.
    internal static class ModernUIBridge
    {
        internal static bool Wired;

        // Soft read of Modern Psycasts UI's left-panel mode. When its "Psysets" editor is open
        // (psysetMode != 0) the left panel is REPLACED by its psyset list/editor (Back button +
        // "Create psyset"), so our footer tool block must NOT draw there or it covers those buttons.
        private static FieldInfo psysetModeField;
        private static bool psysetModeResolved;
        internal static bool PsysetPanelOpen
        {
            get
            {
                if (!psysetModeResolved)
                {
                    psysetModeResolved = true;
                    var t = GenTypes.GetTypeInAnyAssembly("ModernPsycastsUI.ModernPsycastsDrawer");
                    psysetModeField = t?.GetField("psysetMode", BindingFlags.NonPublic | BindingFlags.Static);
                }
                if (psysetModeField == null) return false;   // Modern Psycasts UI absent / field renamed
                try { return (int)psysetModeField.GetValue(null) != 0; }
                catch { return false; }
            }
        }

        // Width of the psycast tab's left pawn-info card. Modern Psycasts UI draws it at a FIXED
        // 340px (x=14, 14px inner padding) regardless of tab width; VPE's own tab scales it with
        // the tab. Everything we draw into that card (spec/tier buttons, the footer tool block)
        // must anchor off THIS, not size.x, or it drifts at narrow tab widths.
        internal static float LeftCardWidth(float tabWidth) =>
            Wired ? 340f : UnityEngine.Mathf.Min(tabWidth * 0.3f, 340f);

        internal static void TryWire()
        {
            try
            {
                var t = GenTypes.GetTypeInAnyAssembly("ModernPsycastsUI.PawnFocusHooks");
                if (t == null) return;   // Modern Psycasts UI not loaded (or an older build without hooks)
                var fActive = t.GetField("Active", BindingFlags.Public | BindingFlags.Static);
                var fGet = t.GetField("Get", BindingFlags.Public | BindingFlags.Static);
                var fSet = t.GetField("Set", BindingFlags.Public | BindingFlags.Static);
                if (fActive == null || fGet == null || fSet == null) return;
                fActive.SetValue(null, (Func<Pawn, bool>)Eligible);
                fGet.SetValue(null, (Func<Pawn, MeditationFocusDef>)GetFocus);
                fSet.SetValue(null, (Action<Pawn, MeditationFocusDef>)SetFocus);
                Wired = true;
            }
            catch (Exception e)
            {
                Log.Warning("[Psycasts²] Could not wire Modern Psycasts UI focus hooks: " + e.Message);
            }
        }

        // Mirrors the old default-focus gizmo's visibility: player-faction humanlike psycasters.
        private static bool Eligible(Pawn p) =>
            p != null && p.Faction != null && p.Faction.IsPlayer
            && p.RaceProps != null && p.RaceProps.Humanlike
            && p.GetMainPsylinkSource() != null;

        private static MeditationFocusDef GetFocus(Pawn p)
        {
            var med = GameComponent_PsycastSynergies.Instance?.GetMed(p, false);
            return med == null || string.IsNullOrEmpty(med.defaultFocus) ? null
                : DefDatabase<MeditationFocusDef>.GetNamedSilentFail(med.defaultFocus);
        }

        private static void SetFocus(Pawn p, MeditationFocusDef f)
        {
            var med = GameComponent_PsycastSynergies.Instance?.GetMed(p, true);
            if (med != null) med.defaultFocus = f?.defName;
        }
    }
}

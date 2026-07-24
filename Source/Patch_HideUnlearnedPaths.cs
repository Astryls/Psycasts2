#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using VanillaPsycastsExpanded;
using VanillaPsycastsExpanded.UI;
using Verse;

namespace PsycastSynergies
{
    // "Hide unlearned paths": the VPE-native tab draws every path, locked ones as darkened
    // tiles - under card-based unlocks those tiles are pure noise. While hideUnlearnedPaths
    // AND lockPathsToEnlightenment are both on, swap the tab's pathsByTab[curTab] list for an
    // unlocked-only view around DoPaths. Deliberately inactive when:
    //   - the tab's own dev mode is on (dev path unlocking stays possible), or
    //   - lockPathsToEnlightenment is off (the Unlock buttons are functional then and hiding
    //     them would brick point-based unlocking).
    // Modern Psycasts UI never calls DoPaths and ships its own tree filter, so this patch
    // only governs VPE's native tab.
    [HarmonyPatch(typeof(ITab_Pawn_Psycasts), "DoPaths")]
    public static class Patch_HideUnlearnedPaths
    {
        private static readonly AccessTools.FieldRef<ITab_Pawn_Psycasts, Dictionary<string, List<PsycasterPathDef>>> PathsByTabF =
            AccessTools.FieldRefAccess<ITab_Pawn_Psycasts, Dictionary<string, List<PsycasterPathDef>>>("pathsByTab");
        private static readonly AccessTools.FieldRef<ITab_Pawn_Psycasts, string> CurTabF =
            AccessTools.FieldRefAccess<ITab_Pawn_Psycasts, string>("curTab");
        private static readonly AccessTools.FieldRef<ITab_Pawn_Psycasts, Hediff_PsycastAbilities> HediffF =
            AccessTools.FieldRefAccess<ITab_Pawn_Psycasts, Hediff_PsycastAbilities>("hediff");
        private static readonly AccessTools.FieldRef<ITab_Pawn_Psycasts, bool> DevModeF =
            AccessTools.FieldRefAccess<ITab_Pawn_Psycasts, bool>("devMode");

        // Filtered-list cache: DoPaths runs on every IMGUI pass; rebuild only when the pawn's
        // hediff, the tab, the source list identity or the unlocked-path count changes.
        private static List<PsycasterPathDef> cacheList;
        private static object cacheHediff; private static string cacheTab;
        private static List<PsycasterPathDef> cacheSrc; private static int cacheUnlocked = -1;

        // Swap state in statics rather than __state: the restore must reach the finalizer even
        // when the original throws, and DoPaths never reenters itself (main-thread IMGUI).
        private static Dictionary<string, List<PsycasterPathDef>> swapDict;
        private static string swapTab; private static List<PsycasterPathDef> swapOrig;
        private static bool swapAllHidden;

        private static readonly PerfCache.LangCache LblNoPaths = new PerfCache.LangCache("PS_NoUnlockedPaths");

        public static void Prefix(ITab_Pawn_Psycasts __instance)
        {
            swapDict = null; swapTab = null; swapOrig = null; swapAllHidden = false;
            var s = PsycastSynergiesMod.Settings;
            if (s == null || !s.hideUnlearnedPaths || !s.lockPathsToEnlightenment) return;
            if (DevModeF(__instance)) return;

            var hediff = HediffF(__instance);
            var dict = PathsByTabF(__instance);
            var tab = CurTabF(__instance);
            if (hediff == null || dict == null || tab == null) return;
            if (!dict.TryGetValue(tab, out var src) || src == null) return;

            var unlocked = hediff.unlockedPaths;
            int un = unlocked?.Count ?? 0;
            if (!ReferenceEquals(cacheHediff, hediff) || cacheTab != tab ||
                !ReferenceEquals(cacheSrc, src) || cacheUnlocked != un)
            {
                cacheHediff = hediff; cacheTab = tab; cacheSrc = src; cacheUnlocked = un;
                cacheList = un == 0 ? new List<PsycasterPathDef>() : src.FindAll(p => unlocked.Contains(p));
            }
            if (cacheList.Count == src.Count) return;   // nothing hidden, no swap needed

            dict[tab] = cacheList;
            swapDict = dict; swapTab = tab; swapOrig = src;
            swapAllHidden = cacheList.Count == 0;
        }

        // Finalizer, not postfix: if DoPaths throws mid-draw a postfix would be skipped and the
        // instance dict would keep the filtered list forever (the full list is only built in the
        // tab's constructor). The finalizer leaves __exception untouched so errors surface as
        // before.
        public static void Finalizer(Rect inRect, Exception __exception)
        {
            if (swapDict != null && swapTab != null && swapOrig != null)
                swapDict[swapTab] = swapOrig;
            bool hint = swapAllHidden && __exception == null;
            swapDict = null; swapTab = null; swapOrig = null; swapAllHidden = false;
            if (!hint) return;

            // Every path in this tab is hidden - say why the area is empty.
            var anchor = Text.Anchor; var font = Text.Font;
            Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Small;
            GUI.color = new Color(1f, 1f, 1f, 0.45f);
            Widgets.Label(new Rect(inRect.x, inRect.y + 10f, inRect.width, 42f), LblNoPaths.Value);
            GUI.color = Color.white;
            Text.Anchor = anchor; Text.Font = font;
        }

        // ---- Modern Psycasts UI (soft, reflection only) ----------------------------------------
        // Modern UI's tree list is built from DefDatabase inside DrawTreesPanel (no field to swap),
        // but it already has its own visibility filter: S.treeFilter == 2 draws invested trees only.
        // While hideUnlearnedPaths (+ lockPathsToEnlightenment, same gates as the VPE tab) is on, we
        // force that filter to 2 for the duration of DrawTreesPanel and restore it in a finalizer -
        // the user's own saved filter value is untouched (the funnel button writes outside our
        // window, and any click it registers while forced is simply overridden next pass).

        private static Func<object> muiSettings;                    // () => ModernPsycastsUIMod.Settings
        private static AccessTools.FieldRef<object, int> muiTreeFilter;
        private static AccessTools.FieldRef<bool> muiDevMode;       // drawer's static devMode
        private static int muiPrevFilter; private static bool muiForced;

        public static void TryWireModernUI(Harmony harmony)
        {
            try
            {
                var drawer = AccessTools.TypeByName("ModernPsycastsUI.ModernPsycastsDrawer");
                if (drawer == null) return;
                var modT = AccessTools.TypeByName("ModernPsycastsUI.ModernPsycastsUIMod");
                var drawTrees = AccessTools.Method(drawer, "DrawTreesPanel");
                var devF = AccessTools.Field(drawer, "devMode");
                var settingsGetter = modT == null ? null : AccessTools.PropertyGetter(modT, "Settings");
                var settingsField = modT == null ? null : AccessTools.Field(modT, "Settings");
                var settingsType = AccessTools.TypeByName("ModernPsycastsUI.ModernPsycastsUISettings");
                var filterF = settingsType == null ? null : AccessTools.Field(settingsType, "treeFilter");
                if (drawTrees == null || filterF == null || (settingsGetter == null && settingsField == null))
                {
                    Log.Warning("[Psycasts²] Hide unlearned paths: Modern Psycasts UI shape unexpected; its tree list is not filtered.");
                    return;
                }
                muiSettings = settingsGetter != null
                    ? (Func<object>)(() => settingsGetter.Invoke(null, null))
                    : () => settingsField.GetValue(null);
                muiTreeFilter = AccessTools.FieldRefAccess<object, int>(filterF);
                muiDevMode = devF != null && devF.IsStatic ? AccessTools.StaticFieldRefAccess<bool>(devF) : null;
                harmony.Patch(drawTrees,
                    prefix: new HarmonyMethod(typeof(Patch_HideUnlearnedPaths), nameof(ModernTreesPrefix)),
                    finalizer: new HarmonyMethod(typeof(Patch_HideUnlearnedPaths), nameof(ModernTreesFinalizer)));
            }
            catch (Exception e)
            {
                Log.Warning("[Psycasts²] Hide unlearned paths: could not wire Modern Psycasts UI: " + e.Message);
            }
        }

        public static void ModernTreesPrefix()
        {
            muiForced = false;
            var s = PsycastSynergiesMod.Settings;
            if (s == null || !s.hideUnlearnedPaths || !s.lockPathsToEnlightenment) return;
            if (muiDevMode != null && muiDevMode()) return;   // dev mode shows everything, as on the VPE tab
            var cfg = muiSettings();
            if (cfg == null) return;
            muiPrevFilter = muiTreeFilter(cfg);
            if (muiPrevFilter == 2) return;                    // already invested-only
            muiTreeFilter(cfg) = 2;
            muiForced = true;
        }

        public static void ModernTreesFinalizer()
        {
            if (!muiForced) return;
            muiForced = false;
            var cfg = muiSettings();
            if (cfg != null) muiTreeFilter(cfg) = muiPrevFilter;
        }
    }
}

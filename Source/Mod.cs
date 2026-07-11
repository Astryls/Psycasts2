#nullable disable
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using VanillaPsycastsExpanded;

namespace PsycastSynergies
{
    public class PsycastSynergiesSettings : ModSettings
    {
        // Per hard level invested in an ability: bonus to its scaled stats.
        public float perLevelPct = 0.05f;
        // Per level invested in OTHER abilities of the same path (Diablo-2 synergy).
        public float synergyPct = 0.015f;
        // Hard cap on how many levels a single ability can hold (gated by psycaster level below).
        public int maxSkillLevel = 10;
        // Temporarily boost the caster's Psychic Sensitivity during a cast so effects that derive
        // from it (very common in addons that hardcode radius/damage as X*sensitivity) also scale.
        public bool scaleViaSensitivity = true;

        public bool scalePower = true;
        public bool scaleRadius = true;
        public bool scaleDuration = true;

        // Tradeoff: each level also raises the cast's psyfocus cost + entropy (heat).
        // Scales with this skill's OWN level only (synergy bonuses stay free).
        public bool scaleCost = true;
        public float costPerLevelPct = 0.05f;

        // Boons/buffs: scale the magnitude (severity) of hediffs the cast applies, when the
        // cast specifies an explicit severity. (Duration of buffs always scales via scaleDuration.)
        public bool scaleBuffStrength = true;

        // Flat psycaster level cap written to VPE's maxLevel. Enlightenment / Transcendence tiers do NOT affect it.
        public bool overrideVpeLevelCap = true;
        public int vpeLevelCap = 400;
        // When ISEKAI RPG Leveling is active, strip its StatParts from the psycaster stats so it can't inflate them.
        public bool suppressIsekaiPsycastStats = true;

        // How many psycaster levels grant one specialization point.
        public int specLevelsPerPoint = 4;
        public float specXpPerPoint = 26f;               // cast/kill spec-XP needed per specialization point
        // Psycaster levels required per allowed skill level (gates dumping levels early).
        public int psyLevelsPerSkillLevel = 3;

        // (B) Frozen synergy graph: abilityDefName -> its primary stat + fixed sources/edges.
        // Computed once (PsycastInfo.EnsureFrozen) and persisted so synergies never reshuffle.
        public Dictionary<string, FrozenSyn> frozenSyn = new Dictionary<string, FrozenSyn>();
        public int graphVersion = 0;   // PsycastInfo.GraphVersion stamp - mismatch triggers a one-time rebuild

        // In-game balance editor (PlayerTuning): the player's personal re-picks, layered on top of
        // ManualBalance.json and the frozen graph. Keys are defNames (prim/primStr/srcs) or
        // "src|tgt" pairs (edge/edgeStr); srcs values are semicolon-joined FULL source lists.
        public Dictionary<string, int> tunePrimStat = new Dictionary<string, int>();
        public Dictionary<string, float> tunePrimStr = new Dictionary<string, float>();
        public Dictionary<string, int> tuneEdgeStat = new Dictionary<string, int>();
        public Dictionary<string, float> tuneEdgeStr = new Dictionary<string, float>();
        public Dictionary<string, string> tuneSrcs = new Dictionary<string, string>();

        // VPE path-access tweaks (both default ON).
        public bool disableGeneRequirements = true;
        public bool enableLockedMechTrees = true;
        public bool lockPathsToEnlightenment = true;   // paths unlock only via the awakening cards (or dev mode)

        // XP system. Casting (tier-scaled) is the primary source; meditation is a reduced trickle;
        // meditating pawns can randomly break through ("Enlightenment") for a big burst.
        public float meditationXpMult = 0.35f;
        public float castXpPerTier = 20f;
        public bool noPsyfocusDecay = true;              // QoL: psyfocus never drains on its own (gain via meditation, spend via casting)
        public bool autoPsycasterStats = true;           // VPE heat/recovery/sensitivity auto per level; psyfocus cost unaffected; no manual stat spend
        public bool enlightenmentEnabled = true;
        public bool empirePsylinkIntegrate = true;       // external psylink (Empire/anima) triggers our Awakening instead
        public bool gateUntieredPsylinks = true;         // strip a generated psylink from any pawn without an Awakened+ tier
        public float awakenedSpawnChance = 0.05f;        // chance ANY eligible pawn spawns as a fresh Awakened+ psycaster
        public bool noAwakenedStartingPawns = true;      // starting colonists (new-game character creation) never roll the random Awakened+ spawn
        public bool cardRevealAll = false;               // awakening cards: the other cards can be turned by hand and any revealed card re-picked
        public int cardPickCount = 0;                    // cards dealt per tier-up pick: 0 = auto (3, or 5 at Tier II), 1-8 = fixed
        public float enlightenmentChance = 0.02f;        // base hourly Enlightenment chance while meditating
        public float enlightenmentStreakBonus = 0.015f;  // +chance per consecutive hour meditated
        public float transcendBreakthroughCurve = 0.15f; // each Transcendent tier (>3) multiplies breakthrough chance by +this (still hard-capped at 0.6)
        public float enlightenmentFrac = 1.0f;           // psycaster XP burst = this × next-level XP (1.0 = a full level)
        public float enlightenmentSaturationFactor = 0.5f; // daily-meditation falloff: breakthrough chance × 1/(1+saturation×this)
        public float awakenGuaranteeHours = 36f;          // cumulative meditation hours that GUARANTEE a non-psycaster Awakens (~1 week dedicated)
        public int balanceVersion = 0;                   // one-time stamp so changed balance defaults override a stale saved config
        public float comaSafeHours = 6f;                 // hours/day of meditation before coma risk starts
        public float comaRiskPerHour = 0.05f;            // psychic-coma chance per hour over the safe window
        public int tier2SpecPoints = 4;                  // bonus specialization points granted on reaching Tier II
        public int tier3SpecPoints = 6;                  // bonus specialization points granted on reaching Tier III
        public bool transcendEnabled = true;             // allow Illuminated pawns to keep climbing into open-ended Transcendent tiers
        public float transcendBaseHours = 48f;           // meditation hours for the FIRST Transcendent tier (Tier IV)
        public float transcendGrowth = 1.6f;             // geometric cost multiplier per Transcendent tier (diminishing returns)
        public int transcendForgoPoints = 4;             // spec points granted when forgoing a Transcendent path card

        // Enemy psycaster tiers (raiders, esp. Empire, can spawn Enlightened).
        public bool enemyTiersEnabled = true;
        public bool enemyTier1 = true;
        public bool enemyTier2 = true;
        public bool enemyTier3 = true;
        public float enemyTierFreq = 0.5f;               // chance a hostile psycaster receives any tier
        public bool enemyAscension = false;              // Tier III enemies can spawn with an unlocked Apotheosis path (OFF)

        // Tier II pilgrimage quest.
        public int pilgrimMeditationTicks = 50000;       // total actual meditation needed (~20h, default ~2.5 days at the daily cap)
        public int pilgrimDailyMaxTicks = 15000;         // per-day meditation cap (8h) so the pilgrimage spans real elapsed time
        public int pilgrimWaveIntervalTicks = 30000;     // 12h between Ancient Psycaster waves at the site
        public float pilgrimWavePointsScale = 1.0f;      // multiplier on wave threat points
        public string pilgrimFocusDef = "PS_PilgrimThrone";   // which focus building spawns at the altar-chain site (T2)

        // Anima pilgrimage chain (pacifist, multi-site).
        public int animaPilgrimTicksPerSite = 50000;     // ~20h of meditation per site
        public int animaPilgrimT2Sites = 3;              // number of anima sites at T2
        public int animaPilgrimT3Sites = 4;              // number of anima sites at T3 (last is the giant tree)

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref perLevelPct, "perLevelPct", 0.05f);
            Scribe_Values.Look(ref synergyPct, "synergyPct", 0.015f);
            Scribe_Values.Look(ref maxSkillLevel, "maxSkillLevel", 10);
            Scribe_Values.Look(ref scaleViaSensitivity, "scaleViaSensitivity", true);
            Scribe_Values.Look(ref scalePower, "scalePower", true);
            Scribe_Values.Look(ref scaleRadius, "scaleRadius", true);
            Scribe_Values.Look(ref scaleDuration, "scaleDuration", true);
            Scribe_Values.Look(ref scaleCost, "scaleCost", true);
            Scribe_Values.Look(ref costPerLevelPct, "costPerLevelPct", 0.05f);
            Scribe_Values.Look(ref scaleBuffStrength, "scaleBuffStrength", true);
            Scribe_Values.Look(ref overrideVpeLevelCap, "overrideVpeLevelCap", true);
            Scribe_Values.Look(ref vpeLevelCap, "vpeLevelCap", 400);
            Scribe_Values.Look(ref suppressIsekaiPsycastStats, "suppressIsekaiPsycastStats", true);
            Scribe_Values.Look(ref specLevelsPerPoint, "specLevelsPerPoint", 4);
            Scribe_Values.Look(ref specXpPerPoint, "specXpPerPoint", 26f);
            Scribe_Values.Look(ref psyLevelsPerSkillLevel, "psyLevelsPerSkillLevel", 3);
            Scribe_Collections.Look(ref frozenSyn, "frozenSyn", LookMode.Value, LookMode.Deep);
            if (frozenSyn == null) frozenSyn = new Dictionary<string, FrozenSyn>();
            Scribe_Values.Look(ref graphVersion, "graphVersion", 0);
            Scribe_Collections.Look(ref tunePrimStat, "tunePrimStat", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref tunePrimStr, "tunePrimStr", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref tuneEdgeStat, "tuneEdgeStat", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref tuneEdgeStr, "tuneEdgeStr", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref tuneSrcs, "tuneSrcs", LookMode.Value, LookMode.Value);
            if (tunePrimStat == null) tunePrimStat = new Dictionary<string, int>();
            if (tunePrimStr == null) tunePrimStr = new Dictionary<string, float>();
            if (tuneEdgeStat == null) tuneEdgeStat = new Dictionary<string, int>();
            if (tuneEdgeStr == null) tuneEdgeStr = new Dictionary<string, float>();
            if (tuneSrcs == null) tuneSrcs = new Dictionary<string, string>();
            Scribe_Values.Look(ref disableGeneRequirements, "disableGeneRequirements", true);
            Scribe_Values.Look(ref enableLockedMechTrees, "enableLockedMechTrees", true);
            Scribe_Values.Look(ref lockPathsToEnlightenment, "lockPathsToEnlightenment", true);
            Scribe_Values.Look(ref meditationXpMult, "meditationXpMult", 0.35f);
            Scribe_Values.Look(ref castXpPerTier, "castXpPerTier", 20f);
            Scribe_Values.Look(ref noPsyfocusDecay, "noPsyfocusDecay", true);
            Scribe_Values.Look(ref autoPsycasterStats, "autoPsycasterStats", true);
            Scribe_Values.Look(ref enlightenmentEnabled, "enlightenmentEnabled", true);
            Scribe_Values.Look(ref empirePsylinkIntegrate, "empirePsylinkIntegrate", true);
            Scribe_Values.Look(ref gateUntieredPsylinks, "gateUntieredPsylinks", true);
            Scribe_Values.Look(ref awakenedSpawnChance, "awakenedSpawnChance", 0.05f);
            Scribe_Values.Look(ref noAwakenedStartingPawns, "noAwakenedStartingPawns", true);
            Scribe_Values.Look(ref cardRevealAll, "cardRevealAll", false);
            Scribe_Values.Look(ref cardPickCount, "cardPickCount", 0);
            Scribe_Values.Look(ref enlightenmentChance, "enlightenmentChance", 0.02f);
            Scribe_Values.Look(ref enlightenmentStreakBonus, "enlightenmentStreakBonus", 0.015f);
            Scribe_Values.Look(ref transcendBreakthroughCurve, "transcendBreakthroughCurve", 0.15f);
            Scribe_Values.Look(ref enlightenmentFrac, "enlightenmentFrac", 1.0f);
            Scribe_Values.Look(ref enlightenmentSaturationFactor, "enlightenmentSaturationFactor", 0.5f);
            Scribe_Values.Look(ref awakenGuaranteeHours, "awakenGuaranteeHours", 36f);
            Scribe_Values.Look(ref comaSafeHours, "comaSafeHours", 6f);
            Scribe_Values.Look(ref comaRiskPerHour, "comaRiskPerHour", 0.05f);
            Scribe_Values.Look(ref tier2SpecPoints, "tier2SpecPoints", 4);
            Scribe_Values.Look(ref tier3SpecPoints, "tier3SpecPoints", 6);
            Scribe_Values.Look(ref transcendEnabled, "transcendEnabled", true);
            Scribe_Values.Look(ref transcendBaseHours, "transcendBaseHours", 48f);
            Scribe_Values.Look(ref transcendGrowth, "transcendGrowth", 1.6f);
            Scribe_Values.Look(ref transcendForgoPoints, "transcendForgoPoints", 4);
            Scribe_Values.Look(ref enemyTiersEnabled, "enemyTiersEnabled", true);
            Scribe_Values.Look(ref enemyTier1, "enemyTier1", true);
            Scribe_Values.Look(ref enemyTier2, "enemyTier2", true);
            Scribe_Values.Look(ref enemyTier3, "enemyTier3", true);
            Scribe_Values.Look(ref enemyTierFreq, "enemyTierFreq", 0.5f);
            Scribe_Values.Look(ref enemyAscension, "enemyAscension", false);
            Scribe_Values.Look(ref pilgrimMeditationTicks, "pilgrimMeditationTicks", 50000);
            Scribe_Values.Look(ref pilgrimDailyMaxTicks, "pilgrimDailyMaxTicks", 15000);
            Scribe_Values.Look(ref pilgrimWaveIntervalTicks, "pilgrimWaveIntervalTicks", 30000);
            Scribe_Values.Look(ref pilgrimWavePointsScale, "pilgrimWavePointsScale", 1.0f);
            Scribe_Values.Look(ref pilgrimFocusDef, "pilgrimFocusDef", "PS_PilgrimThrone");
            Scribe_Values.Look(ref animaPilgrimTicksPerSite, "animaPilgrimTicksPerSite", 50000);
            Scribe_Values.Look(ref animaPilgrimT2Sites, "animaPilgrimT2Sites", 3);
            Scribe_Values.Look(ref animaPilgrimT3Sites, "animaPilgrimT3Sites", 4);
            Scribe_Values.Look(ref balanceVersion, "balanceVersion", 0);

            // One-time migration: when the saved config predates a balance-default change, push the new
            // values in (otherwise the old saved knobs would mask the new defaults forever).
            if (Scribe.mode == LoadSaveMode.LoadingVars && balanceVersion < 5)
            {
                if (balanceVersion < 2) { castXpPerTier = 20f; enlightenmentFrac = 1.0f; enlightenmentSaturationFactor = 0.5f; }
                if (balanceVersion < 3) pilgrimDailyMaxTicks = 15000;   // 6h/day, matching the safe meditation window
                if (balanceVersion < 5) { vpeLevelCap = 400; overrideVpeLevelCap = true; }   // flat 400 cap, tier-independent
                balanceVersion = 5;
            }
        }
    }

    public class PsycastSynergiesMod : Mod
    {
        public static PsycastSynergiesSettings Settings;
        public static PsycastSynergiesMod Instance;
        private Vector2 settingsScroll;          // settings panel scroll position
        private int settingsTab;                 // active settings tab

        public PsycastSynergiesMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<PsycastSynergiesSettings>();
        }

        public override string SettingsCategory() => "Psycasts²";

        // Push our configured cap into VPE's live settings. Called at startup and
        // whenever our settings change. No-op (and non-destructive) when the override
        // is off - we never lower VPE's own configured value.
        public static void ApplyVpeLevelCap()
        {
            var vpe = PsycastsMod.Settings;
            if (vpe == null || Settings == null || !Settings.overrideVpeLevelCap) return;
            vpe.maxLevel = Mathf.Max(1, Settings.vpeLevelCap);
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            ApplyVpeLevelCap();
        }

        private static readonly Color TabGold = new Color(0.96f, 0.81f, 0.36f);
        private static readonly string[] SettingsTabs = { "Skills", "Specializations", "Meditation", "Pilgrimage", "Enemies" };
        private float[] settingsTabH;            // per-tab remembered scroll height (avoids stale-height clipping)

        public override void DoSettingsWindowContents(Rect inRect)
        {
            if (settingsTabH == null || settingsTabH.Length != SettingsTabs.Length)
            {
                settingsTabH = new float[SettingsTabs.Length];
                for (int k = 0; k < settingsTabH.Length; k++) settingsTabH[k] = 1200f;
            }
            // Stylized tab bar. We DETECT a tab click here but apply it only AFTER drawing the content, so the
            // active tab is constant across this frame's IMGUI event passes - switching mid-frame swaps the
            // control set between passes and leaves the new tab's sliders/checkboxes dead.
            int clicked = -1;
            float tabW = inRect.width / SettingsTabs.Length;
            for (int i = 0; i < SettingsTabs.Length; i++)
            {
                var tr = new Rect(inRect.x + i * tabW, inRect.y, tabW, 30f);
                bool active = settingsTab == i;
                if (active) Widgets.DrawBoxSolid(tr, new Color(0.17f, 0.155f, 0.10f));
                else if (Mouse.IsOver(tr)) Widgets.DrawHighlight(tr);
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = active ? TabGold : new Color(0.82f, 0.86f, 0.94f);
                Widgets.Label(tr, ("PS_SetTab_" + SettingsTabs[i]).Translate());
                GUI.color = Color.white;
                if (active) Widgets.DrawBoxSolid(new Rect(tr.x, tr.yMax - 3f, tr.width, 3f), TabGold);
                if (Widgets.ButtonInvisible(tr)) clicked = i;
            }
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.DrawLineHorizontal(inRect.x, inRect.y + 31f, inRect.width);

            int tab = settingsTab;
            var body = new Rect(inRect.x, inRect.y + 38f, inRect.width, inRect.height - 38f);
            var viewRect = new Rect(0f, 0f, body.width - 20f, settingsTabH[tab]);
            Widgets.BeginScrollView(body, ref settingsScroll, viewRect);
            var l = new Listing_Standard();
            l.Begin(viewRect);
            switch (tab)
            {
                case 0: TabSkills(l); break;
                case 1: TabSpec(l); break;
                case 2: TabMeditation(l); break;
                case 3: TabPilgrimage(l); break;
                default: TabEnemies(l); break;
            }
            settingsTabH[tab] = l.CurHeight + 24f;
            l.End();
            Widgets.EndScrollView();

            if (clicked >= 0 && clicked != settingsTab) { settingsTab = clicked; settingsScroll = Vector2.zero; }
        }

        // ---- tooltip-aware setting widgets ----
        static void Head(Listing_Standard l, string text)
        {
            l.Gap(8f);
            Text.Font = GameFont.Medium;
            GUI.color = TabGold;
            Widgets.Label(l.GetRect(28f), text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            l.GapLine(4f);
        }
        static void FS(Listing_Standard l, string label, ref float val, float min, float max, string tip, bool integer = false)
        {
            Rect r = l.GetRect(Text.LineHeight);
            if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(r, tip);
            Widgets.Label(r, label);
            float v = l.Slider(val, min, max);
            val = integer ? Mathf.Round(v) : v;
        }
        static void IS(Listing_Standard l, string label, ref int val, int min, int max, string tip)
        {
            float f = val; FS(l, label, ref f, min, max, tip, true); val = Mathf.RoundToInt(f);
        }
        static void CB(Listing_Standard l, string label, ref bool val, string tip) => l.CheckboxLabeled(label, ref val, tip);

        void TabSkills(Listing_Standard l)
        {
            var s = Settings;
            Head(l, "PS_SetH_SkillLeveling".Translate());
            FS(l, "PS_SetPerLevel".Translate((s.perLevelPct * 100f).ToString("F0")), ref s.perLevelPct, 0f, 0.25f,
                "PS_SetPerLevelTip".Translate());
            FS(l, "PS_SetSynergyPct".Translate((s.synergyPct * 100f).ToString("F0")), ref s.synergyPct, 0f, 0.10f,
                "PS_SetSynergyPctTip".Translate());
            IS(l, "PS_SetMaxSkillLevel".Translate(s.maxSkillLevel), ref s.maxSkillLevel, 1, 30,
                "PS_SetMaxSkillLevelTip".Translate());
            IS(l, "PS_SetPsyLevelsPerSkill".Translate(s.psyLevelsPerSkillLevel), ref s.psyLevelsPerSkillLevel, 1, 10,
                "PS_SetPsyLevelsPerSkillTip".Translate());

            Head(l, "PS_SetH_WhatScales".Translate());
            CB(l, "PS_SetScalePower".Translate(), ref s.scalePower, "PS_SetScalePowerTip".Translate());
            CB(l, "PS_SetScaleRadius".Translate(), ref s.scaleRadius, "PS_SetScaleRadiusTip".Translate());
            CB(l, "PS_SetScaleDuration".Translate(), ref s.scaleDuration, "PS_SetScaleDurationTip".Translate());
            CB(l, "PS_SetScaleBuff".Translate(), ref s.scaleBuffStrength, "PS_SetScaleBuffTip".Translate());
            CB(l, "PS_SetScaleSens".Translate(), ref s.scaleViaSensitivity, "PS_SetScaleSensTip".Translate());

            Head(l, "PS_SetH_CostCaps".Translate());
            CB(l, "PS_SetScaleCost".Translate(), ref s.scaleCost, "PS_SetScaleCostTip".Translate());
            if (s.scaleCost)
                FS(l, "PS_SetCostPerLevel".Translate((s.costPerLevelPct * 100f).ToString("F0")), ref s.costPerLevelPct, 0f, 0.25f, "PS_SetCostPerLevelTip".Translate());
            CB(l, "PS_SetLevelCap".Translate(), ref s.overrideVpeLevelCap, "PS_SetLevelCapTip".Translate());
            if (s.overrideVpeLevelCap)
                IS(l, "PS_SetLevelCapVal".Translate(s.vpeLevelCap), ref s.vpeLevelCap, 30, 500, "PS_SetLevelCapValTip".Translate());
            CB(l, "PS_SetIsekai".Translate(), ref s.suppressIsekaiPsycastStats, "PS_SetIsekaiTip".Translate());
            CB(l, "PS_SetAutoStats".Translate(), ref s.autoPsycasterStats,
                "PS_SetAutoStatsTip".Translate());
            ApplyVpeLevelCap();
        }

        void TabSpec(Listing_Standard l)
        {
            var s = Settings;
            Head(l, "PS_SetH_SpecPoints".Translate());
            IS(l, "PS_SetSpecLevels".Translate(s.specLevelsPerPoint), ref s.specLevelsPerPoint, 1, 20,
                "PS_SetSpecLevelsTip".Translate());
            FS(l, "PS_SetSpecXp".Translate(s.specXpPerPoint.ToString("F0")), ref s.specXpPerPoint, 10f, 80f,
                "PS_SetSpecXpTip".Translate(), true);
            IS(l, "PS_SetTier2Points".Translate(s.tier2SpecPoints), ref s.tier2SpecPoints, 0, 12,
                "PS_SetTier2PointsTip".Translate());
            IS(l, "PS_SetTier3Points".Translate(s.tier3SpecPoints), ref s.tier3SpecPoints, 0, 16,
                "PS_SetTier3PointsTip".Translate());

            Head(l, "PS_SetH_PathAccess".Translate());
            CB(l, "PS_SetNoGeneReq".Translate(), ref s.disableGeneRequirements, "PS_SetNoGeneReqTip".Translate());
            CB(l, "PS_SetMechTrees".Translate(), ref s.enableLockedMechTrees, "PS_SetMechTreesTip".Translate());
            CB(l, "PS_SetLockPaths".Translate(), ref s.lockPathsToEnlightenment, "PS_SetLockPathsTip".Translate());

            Head(l, "PS_SetH_SynergyGraph".Translate());
            l.Label("PS_SetSynFrozen".Translate());
            int tuned = PlayerTuning.Count;
            l.Label("PS_SetSynRetune".Translate()
                + (tuned > 0 ? " " + "PS_SetSynEditsActive".Translate(tuned) : new TaggedString("")));
            if (l.ButtonText("PS_SetSynRebuild".Translate()))
            {
                s.frozenSyn?.Clear();
                PsycastInfo.EnsureFrozen();
                Messages.Message("PS_MsgSynRebuilt".Translate(), MessageTypeDefOf.TaskCompletion, false);
            }

            Head(l, "PS_SetH_BalanceEdits".Translate());
            l.Label("PS_SetBalanceInfo".Translate());
            if (l.ButtonText(tuned > 0 ? "PS_SetBakeN".Translate(tuned).ToString() : "PS_SetBake".Translate().ToString()))
            {
                if (tuned == 0)
                    Messages.Message("PS_MsgNoBake".Translate(), MessageTypeDefOf.RejectInput, false);
                else
                    Find.WindowStack.Add(new Dialog_Confirm("PS_SetBakeTitle".Translate(),
                        "PS_SetBakeBody".Translate(tuned),
                        () =>
                        {
                            if (ManualBalance.BakePlayerTuning(out string err, out int baked))
                                Messages.Message("PS_MsgBaked".Translate(baked), MessageTypeDefOf.TaskCompletion, false);
                            else
                                Messages.Message("PS_MsgBakeFailed".Translate(err), MessageTypeDefOf.RejectInput, false);
                        }));
            }
            if (l.ButtonText("PS_SetDiscard".Translate()))
            {
                if (tuned == 0)
                    Messages.Message("PS_MsgNoDiscard".Translate(), MessageTypeDefOf.RejectInput, false);
                else
                    Find.WindowStack.Add(new Dialog_Confirm("PS_SetDiscardTitle".Translate(),
                        "PS_SetDiscardBody".Translate(tuned),
                        () => { PlayerTuning.ResetAll(); PsycastSynergiesMod.Instance?.WriteSettings(); }));
            }
        }

        void TabMeditation(Listing_Standard l)
        {
            var s = Settings;
            Head(l, "PS_SetH_PsycastXp".Translate());
            FS(l, "PS_SetCastXp".Translate(s.castXpPerTier.ToString("F0"), (s.castXpPerTier * 3f).ToString("F0")), ref s.castXpPerTier, 0f, 60f,
                "PS_SetCastXpTip".Translate(), true);
            FS(l, "PS_SetMedXp".Translate((s.meditationXpMult * 100f).ToString("F0")), ref s.meditationXpMult, 0f, 1f,
                "PS_SetMedXpTip".Translate());
            CB(l, "PS_SetNoDecay".Translate(), ref s.noPsyfocusDecay,
                "PS_SetNoDecayTip".Translate());

            Head(l, "PS_SetH_Flow".Translate());
            CB(l, "PS_SetBreakthroughs".Translate(), ref s.enlightenmentEnabled,
                "PS_SetBreakthroughsTip".Translate());
            if (TieringControl.MeditationAwakeningDisabled)
                ModOverrideNote(l, "PS_Ovr_MedAwaken".Translate());
            if (s.enlightenmentEnabled)
            {
                FS(l, "PS_SetBreakSize".Translate((s.enlightenmentFrac * 100f).ToString("F0")), ref s.enlightenmentFrac, 0.2f, 1.5f,
                    "PS_SetBreakSizeTip".Translate());
                FS(l, "PS_SetFalloff".Translate(s.enlightenmentSaturationFactor.ToString("F2")), ref s.enlightenmentSaturationFactor, 0f, 1.5f,
                    "PS_SetFalloffTip".Translate());
                FS(l, "PS_SetGuarantee".Translate(s.awakenGuaranteeHours.ToString("F0")), ref s.awakenGuaranteeHours, 6f, 120f,
                    "PS_SetGuaranteeTip".Translate(), true);
                FS(l, "PS_SetTransCurve".Translate((1f + s.transcendBreakthroughCurve).ToString("F2")), ref s.transcendBreakthroughCurve, 0f, 0.4f,
                    "PS_SetTransCurveTip".Translate());
            }

            Head(l, "PS_SetH_Comas".Translate());
            FS(l, "PS_SetSafeWindow".Translate(s.comaSafeHours.ToString("F1")), ref s.comaSafeHours, 0f, 16f,
                "PS_SetSafeWindowTip".Translate());
            FS(l, "PS_SetComaRisk".Translate((s.comaRiskPerHour * 100f).ToString("F0")), ref s.comaRiskPerHour, 0f, 0.25f,
                "PS_SetComaRiskTip".Translate());

            Head(l, "PS_SetH_Cards".Translate());
            CB(l, "PS_SetEmpirePsylink".Translate(), ref s.empirePsylinkIntegrate,
                "PS_SetEmpirePsylinkTip".Translate());
            if (TieringControl.ExternalPsylinkAwakeningDisabled)
                ModOverrideNote(l, "PS_Ovr_ExtPsylink".Translate());
            CB(l, "PS_SetPsylinkGate".Translate(), ref s.gateUntieredPsylinks,
                "PS_SetPsylinkGateTip".Translate());
            if (TieringControl.PsylinkGateDisabled)
                ModOverrideNote(l, "PS_Ovr_PsylinkGate".Translate());
            else if (TieringControl.RandomAwakenedSpawnsDisabled)
                ModOverrideNote(l, "PS_Ovr_RandomSpawns".Translate());
            if (s.gateUntieredPsylinks)
                FS(l, "PS_SetSpawnChance".Translate((s.awakenedSpawnChance * 100f).ToString("F0")), ref s.awakenedSpawnChance, 0f, 1f,
                    "PS_SetSpawnChanceTip".Translate());
            if (s.gateUntieredPsylinks)
                CB(l, "PS_SetNoStartAwakened".Translate(), ref s.noAwakenedStartingPawns,
                    "PS_SetNoStartAwakenedTip".Translate());
            CB(l, "PS_SetRevealAll".Translate(), ref s.cardRevealAll,
                "PS_SetRevealAllTip".Translate());
            IS(l, "PS_SetCardCount".Translate(s.cardPickCount <= 0 ? "PS_SetCardCountAuto".Translate().ToString() : s.cardPickCount.ToString()), ref s.cardPickCount, 0, 8,
                "PS_SetCardCountTip".Translate());

            Head(l, "PS_SetH_Transcendence".Translate());
            CB(l, "PS_SetTranscend".Translate(), ref s.transcendEnabled,
                "PS_SetTranscendTip".Translate());
            if (TieringControl.TranscendenceDisabled)
                ModOverrideNote(l, "PS_Ovr_Transcend".Translate());
            if (s.transcendEnabled)
            {
                FS(l, "PS_SetTransBase".Translate(s.transcendBaseHours.ToString("F0")), ref s.transcendBaseHours, 12f, 200f,
                    "PS_SetTransBaseTip".Translate(), true);
                FS(l, "PS_SetTransGrowth".Translate(s.transcendGrowth.ToString("F2")), ref s.transcendGrowth, 1.1f, 3f,
                    "PS_SetTransGrowthTip".Translate());
                IS(l, "PS_SetForgoPoints".Translate(s.transcendForgoPoints), ref s.transcendForgoPoints, 0, 12,
                    "PS_SetForgoPointsTip".Translate());
            }
        }

        // Amber note under a setting row when a loaded mod's TieringOverrideDef has taken a path over.
        private static void ModOverrideNote(Listing_Standard l, string what)
        {
            GUI.color = new Color(1f, 0.72f, 0.35f);
            l.Label("PS_SetOverrideNote".Translate(what, TieringControl.OwnerLabel));
            GUI.color = Color.white;
        }

        void TabPilgrimage(Listing_Standard l)
        {
            if (TieringControl.PilgrimagesDisabled)
                ModOverrideNote(l, "PS_Ovr_Pilgrimages".Translate());
            var s = Settings;
            Head(l, "PS_SetH_Altar".Translate());
            float days = s.pilgrimDailyMaxTicks > 0 ? (float)s.pilgrimMeditationTicks / s.pilgrimDailyMaxTicks : 0f;
            IS(l, "PS_SetAltarTotal".Translate((s.pilgrimMeditationTicks / 2500f).ToString("F1"), days.ToString("F1")), ref s.pilgrimMeditationTicks, 10000, 150000,
                "PS_SetAltarTotalTip".Translate());
            IS(l, "PS_SetAltarDaily".Translate((s.pilgrimDailyMaxTicks / 2500f).ToString("F1")), ref s.pilgrimDailyMaxTicks, 0, 60000,
                "PS_SetAltarDailyTip".Translate());
            IS(l, "PS_SetWaveInterval".Translate((s.pilgrimWaveIntervalTicks / 2500f).ToString("F1")), ref s.pilgrimWaveIntervalTicks, 5000, 120000,
                "PS_SetWaveIntervalTip".Translate());
            FS(l, "PS_SetWaveScale".Translate(s.pilgrimWavePointsScale.ToString("F2")), ref s.pilgrimWavePointsScale, 0.1f, 3f,
                "PS_SetWaveScaleTip".Translate());

            Head(l, "PS_SetH_AltarFocus".Translate());
            l.Label("PS_SetT3Throne".Translate());
            string[][] focusOpts = {
                new[]{"PS_PilgrimThrone","PS_SetFocus_PilgrimThrone".Translate().ToString()},
                new[]{"PS_PilgrimAltar","PS_SetFocus_PilgrimAltar".Translate().ToString()},
                new[]{"MeditationSpot","PS_SetFocus_MeditationSpot".Translate().ToString()},
                new[]{"Throne","PS_SetFocus_Throne".Translate().ToString()},
                new[]{"GrandThrone","PS_SetFocus_GrandThrone".Translate().ToString()},
            };
            foreach (var opt in focusOpts)
                if (l.RadioButton(opt[1], s.pilgrimFocusDef == opt[0])) s.pilgrimFocusDef = opt[0];

            Head(l, "PS_SetH_Anima".Translate());
            IS(l, "PS_SetAnimaPerSite".Translate((s.animaPilgrimTicksPerSite / 2500f).ToString("F1")), ref s.animaPilgrimTicksPerSite, 10000, 150000,
                "PS_SetAnimaPerSiteTip".Translate());
            IS(l, "PS_SetAnimaT2".Translate(s.animaPilgrimT2Sites), ref s.animaPilgrimT2Sites, 1, 6, "PS_SetAnimaT2Tip".Translate());
            IS(l, "PS_SetAnimaT3".Translate(s.animaPilgrimT3Sites), ref s.animaPilgrimT3Sites, 1, 8,
                "PS_SetAnimaT3Tip".Translate());
        }

        void TabEnemies(Listing_Standard l)
        {
            var s = Settings;
            Head(l, "PS_SetH_Enemies".Translate());
            CB(l, "PS_SetEnemyTiers".Translate(), ref s.enemyTiersEnabled,
                "PS_SetEnemyTiersTip".Translate());
            if (TieringControl.EnemyTiersDisabled)
                ModOverrideNote(l, "PS_Ovr_EnemyTiers".Translate());
            if (s.enemyTiersEnabled)
            {
                CB(l, "PS_SetEnemyT1".Translate(), ref s.enemyTier1, "PS_SetEnemyT1Tip".Translate());
                CB(l, "PS_SetEnemyT2".Translate(), ref s.enemyTier2, "PS_SetEnemyT2Tip".Translate());
                CB(l, "PS_SetEnemyT3".Translate(), ref s.enemyTier3, "PS_SetEnemyT3Tip".Translate());
                FS(l, "PS_SetEnemyFreq".Translate((s.enemyTierFreq * 100f).ToString("F0")), ref s.enemyTierFreq, 0f, 1f,
                    "PS_SetEnemyFreqTip".Translate());
                CB(l, "PS_SetEnemyAscension".Translate(), ref s.enemyAscension,
                    "PS_SetEnemyAscensionTip".Translate());
            }
        }

    }
}

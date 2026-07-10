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
                Widgets.Label(tr, SettingsTabs[i]);
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
            Head(l, "Skill leveling");
            FS(l, $"Bonus per level invested: +{s.perLevelPct * 100f:F0}% power / radius / duration", ref s.perLevelPct, 0f, 0.25f,
                "How much each point you invest in a psycast raises its primary stat, plus radius and duration. This is the skill's OWN per-level gain.");
            FS(l, $"Synergy bonus per level in same-path psycasts: +{s.synergyPct * 100f:F0}%", ref s.synergyPct, 0f, 0.10f,
                "Leveling OTHER psycasts of the same path feeds this one through directed, role-based synergies. This is the bonus contributed per level of those related skills.");
            IS(l, $"Maximum level per ability: {s.maxSkillLevel}", ref s.maxSkillLevel, 1, 30,
                "The hard level cap for any single psycast. The Convergence specialization raises a pawn's effective cap by a further +5.");
            IS(l, $"Psycaster levels required per allowed skill level: {s.psyLevelsPerSkillLevel}", ref s.psyLevelsPerSkillLevel, 1, 10,
                "Gating: a skill can only be raised as high as your psycaster level supports. Higher values mean leveling skills demands a higher psycaster level first.");

            Head(l, "What scales");
            CB(l, "Scale power / damage", ref s.scalePower, "Scale an ability's power, such as melee and explosion damage, with its level and synergies.");
            CB(l, "Scale radius", ref s.scaleRadius, "Scale an ability's area-of-effect radius with its level and synergies.");
            CB(l, "Scale duration", ref s.scaleDuration, "Scale an ability's effect duration with its level and synergies, for longer buffs and debuffs.");
            CB(l, "Scale buff / boon strength", ref s.scaleBuffStrength, "Scale the severity of hediffs a cast applies, both boons and debuffs, with level and synergies.");
            CB(l, "Scale sensitivity-derived effects", ref s.scaleViaSensitivity, "Briefly raise the caster's psychic sensitivity during a cast so addon effects hardcoded as a multiple of sensitivity, such as Geomancer's, also scale.");

            Head(l, "Cost and caps");
            CB(l, "Higher levels cost more (heat and psyfocus)", ref s.scaleCost, "Each invested level also raises the skill's psyfocus cost and neural heat. Scales with the skill's OWN level only; synergies never add cost.");
            if (s.scaleCost)
                FS(l, $"Cost increase per level: +{s.costPerLevelPct * 100f:F0}% heat and psyfocus", ref s.costPerLevelPct, 0f, 0.25f, "How steeply a skill's cost rises for each invested level.");
            CB(l, "Set psycaster level cap", ref s.overrideVpeLevelCap, "Set Vanilla Psycasts Expanded's maximum psycaster level to the value below. Enlightenment tiers do not affect this cap. Off leaves VPE's own setting untouched.");
            if (s.overrideVpeLevelCap)
                IS(l, $"Max psycaster level: {s.vpeLevelCap}", ref s.vpeLevelCap, 30, 500, "The flat psycaster level ceiling. Default 400.");
            CB(l, "Suppress ISEKAI RPG psycaster-stat boosts", ref s.suppressIsekaiPsycastStats, "When ISEKAI RPG Leveling is installed, stop it from raising psychic sensitivity, neural heat limit and recovery, meditation focus and psyfocus, so this mod's own scaling stays authoritative. Takes effect on restart.");
            CB(l, "Auto psycaster stats (no manual upgrades; cost unaffected)", ref s.autoPsycasterStats,
                "When on, VPE's neural-heat limit, neural-heat recovery and psychic sensitivity grow AUTOMATICALLY with each psycaster level, you no longer spend points on them, and the psyfocus cost is left unaffected by leveling. Your points stay free for raising skill levels (this mod's own system); the 'Upgrade Psycaster Stats' button is replaced with a note.");
            ApplyVpeLevelCap();
        }

        void TabSpec(Listing_Standard l)
        {
            var s = Settings;
            Head(l, "Specialization points");
            IS(l, $"Psycaster levels per specialization point: {s.specLevelsPerPoint}", ref s.specLevelsPerPoint, 1, 20,
                "How many psycaster levels grant one point to spend on the constellation tree. Lower means points flow faster.");
            FS(l, $"Casting XP per specialization point: {s.specXpPerPoint:F0}", ref s.specXpPerPoint, 10f, 80f,
                "Casting also earns specialization points. This is the XP needed per point; lower makes the Apotheosis tree reachable sooner.", true);
            IS(l, $"Tier II bonus points: {s.tier2SpecPoints}", ref s.tier2SpecPoints, 0, 12,
                "Specialization points granted the moment a pawn reaches Enlightenment Tier II.");
            IS(l, $"Tier III bonus points: {s.tier3SpecPoints}", ref s.tier3SpecPoints, 0, 16,
                "Specialization points granted the moment a pawn reaches Enlightenment Tier III.");

            Head(l, "Path access");
            CB(l, "Disable gene requirements", ref s.disableGeneRequirements, "Paths that require a specific gene, such as Archon, unlock without needing the gene.");
            CB(l, "Unlock removed Mech-Mind trees", ref s.enableLockedMechTrees, "Re-enable the Mechanitor Mech-Mind paths the addon ships but locks away as removed content.");
            CB(l, "Lock paths to Enlightenment", ref s.lockPathsToEnlightenment, "Paths unlock ONLY through the awakening cards; the tab's normal Unlock buttons are disabled. Dev mode still bypasses this.");

            Head(l, "Synergy graph");
            l.Label("Synergies are frozen the first time they are generated and never change afterward, even if you add psycast addons later.");
            int tuned = PlayerTuning.Count;
            l.Label("To retune skill effects, synergies and empowers, enable dev mode and open the Balance editor from the psycast tab."
                + (tuned > 0 ? " You have " + tuned + " personal edit(s) active." : ""));
            if (l.ButtonText("Rebuild synergy graph from installed psycasts"))
            {
                s.frozenSyn?.Clear();
                PsycastInfo.EnsureFrozen();
                Messages.Message("Synergy graph rebuilt from currently-installed psycasts.", MessageTypeDefOf.TaskCompletion, false);
            }

            Head(l, "Balance edits");
            l.Label("Edits made in the in-game Balance editor are stored in this config and override the baked defaults. Baking writes them into the mod's ManualBalance.json as the NEW defaults and clears the live overlay - a custom balance that survives resetting your edits, and the file the mod ships with.");
            if (l.ButtonText(tuned > 0 ? $"Bake {tuned} edit(s) into mod defaults" : "Bake edits into mod defaults"))
            {
                if (tuned == 0)
                    Messages.Message("No balance edits to bake.", MessageTypeDefOf.RejectInput, false);
                else
                    Find.WindowStack.Add(new Dialog_Confirm("Bake balance edits",
                        $"Write your {tuned} edit(s) into ManualBalance.json as the mod's new default balance, then clear the live overlay? (A Workshop update can overwrite the baked file - re-bake afterwards if needed.)",
                        () =>
                        {
                            if (ManualBalance.BakePlayerTuning(out string err, out int baked))
                                Messages.Message($"Baked - ManualBalance.json now holds {baked} default override(s).", MessageTypeDefOf.TaskCompletion, false);
                            else
                                Messages.Message("Bake failed: " + err, MessageTypeDefOf.RejectInput, false);
                        }));
            }
            if (l.ButtonText("Discard my balance edits"))
            {
                if (tuned == 0)
                    Messages.Message("No balance edits to discard.", MessageTypeDefOf.RejectInput, false);
                else
                    Find.WindowStack.Add(new Dialog_Confirm("Discard balance edits",
                        $"Discard all {tuned} of your balance edit(s) and return to the baked defaults?",
                        () => { PlayerTuning.ResetAll(); PsycastSynergiesMod.Instance?.WriteSettings(); }));
            }
        }

        void TabMeditation(Listing_Standard l)
        {
            var s = Settings;
            Head(l, "Psycast XP");
            FS(l, $"Casting XP per skill tier: {s.castXpPerTier:F0}  (tier-3 cast = {s.castXpPerTier * 3f:F0} XP)", ref s.castXpPerTier, 0f, 60f,
                "Casting is the main XP source. Each cast grants this much XP per the skill's tier, so deeper psycasts level you faster.", true);
            FS(l, $"Meditation psycast XP: {s.meditationXpMult * 100f:F0}% of VPE's rate", ref s.meditationXpMult, 0f, 1f,
                "Meditation also trickles psycast XP, scaled to this fraction of VPE's normal rate. A steady background source rather than the main one.");
            CB(l, "Remove psyfocus decay", ref s.noPsyfocusDecay,
                "On (default): psyfocus never drains on its own. It only rises from meditation and falls when you spend it casting. Off: vanilla decay, where idle psyfocus slowly bleeds away and must be topped up.");

            Head(l, "A flow of ancient knowledge");
            CB(l, "Enable meditation breakthroughs", ref s.enlightenmentEnabled,
                "A meditating pawn can be struck by a flow of ancient knowledge: a full level for a psycaster, or progress toward Awakening for a non-psycaster.");
            if (TieringControl.MeditationAwakeningDisabled)
                ModOverrideNote(l, "Awakening from meditation is disabled - breakthroughs still grant psycaster levels");
            if (s.enlightenmentEnabled)
            {
                FS(l, $"Breakthrough size: {s.enlightenmentFrac * 100f:F0}% of a level", ref s.enlightenmentFrac, 0.2f, 1.5f,
                    "How much psycaster XP one breakthrough grants. 1.0 is exactly a full level.");
                FS(l, $"Daily-meditation falloff: x{s.enlightenmentSaturationFactor:F2}", ref s.enlightenmentSaturationFactor, 0f, 1.5f,
                    "Anti-farming. Meditating past the safe window builds saturation that makes a psycaster's breakthroughs rarer. Higher rarefies faster; 0 disables the falloff.");
                FS(l, $"Awakening guaranteed after: {s.awakenGuaranteeHours:F0} cumulative meditation hours", ref s.awakenGuaranteeHours, 6f, 120f,
                    "A non-psycaster's odds of Awakening to Tier I ramp with total meditation and are GUARANTEED once they reach this many cumulative hours. Around 36 is about a week of dedicated 6-hour days.", true);
                FS(l, $"Transcendent breakthrough curve: x{1f + s.transcendBreakthroughCurve:F2} per tier", ref s.transcendBreakthroughCurve, 0f, 0.4f,
                    "Above Illuminated, each Transcendent tier multiplies a psycaster's hourly breakthrough chance by +this, so deep Transcendents earn free levels faster as leveling slows. The chance stays hard-capped at 60% - it never becomes guaranteed.");
            }

            Head(l, "Over-meditation and comas");
            FS(l, $"Safe meditation window: {s.comaSafeHours:F1} hours / day", ref s.comaSafeHours, 0f, 16f,
                "Hours a pawn can meditate per day with NO risk. Meditating PAST this window starts adding psychic-coma risk each hour, AND builds saturation so each successive coma grows LONGER, up to 8 days. Keep daily meditation at or under this for safe, sustainable progress; push past it only when you accept the escalating, compounding risk. Pilgrimage meditation is exempt from comas entirely.");
            FS(l, $"Coma risk per excess hour: {s.comaRiskPerHour * 100f:F0}%", ref s.comaRiskPerHour, 0f, 0.25f,
                "Added psychic-coma chance for EACH hour meditated beyond the safe window, rolled every hour. So the 9th hour of a day carries roughly three times the risk of the 7th. Coma length scales with the pawn's level, how far past the window they went, and accumulated saturation: a minimum of 4 hours up to a maximum of 8 days.");

            Head(l, "Awakening and cards");
            CB(l, "Empire psylink awakens", ref s.empirePsylinkIntegrate,
                "Gaining a psylink from OUTSIDE meditation, the Empire's bestowing ceremony, the blinding ritual, or anima-tree linking, triggers this mod's Awakening, so the pawn enters the system and picks a path instead of becoming a path-less psycaster.");
            if (TieringControl.ExternalPsylinkAwakeningDisabled)
                ModOverrideNote(l, "External-psylink awakening is disabled");
            CB(l, "Suppress untiered psylinks at generation", ref s.gateUntieredPsylinks,
                "A pawn that would generate already holding a psylink loses it unless it also has an Awakened (or higher) enlightenment tier, so psycasters are earned through Awakening rather than handed out at random. The Ancient Psycaster Order (pilgrimage enemies) is exempt, and enemy-tier upgrades still apply first.");
            if (TieringControl.PsylinkGateDisabled)
                ModOverrideNote(l, "The psylink gate is disabled");
            else if (TieringControl.RandomAwakenedSpawnsDisabled)
                ModOverrideNote(l, "Random Awakened+ spawns are disabled");
            if (s.gateUntieredPsylinks)
                FS(l, $"   Chance a random pawn spawns Awakened+: {s.awakenedSpawnChance * 100f:F0}%", ref s.awakenedSpawnChance, 0f, 1f,
                    "Each eligible pawn (age 13+) of any faction has this chance to generate as a proper Awakened psycaster - a psylink, a path and a tier (mostly Tier I, occasionally higher), created from scratch if needed. Everyone else who would have generated with a psylink loses it. 0% = no random psycasters spawn.");
            if (s.gateUntieredPsylinks)
                CB(l, "   Starting colonists never spawn Awakened", ref s.noAwakenedStartingPawns,
                    "On (default): the colonists you begin the game with are exempt from the random Awakened+ roll, so everyone starts the journey with an ordinary, mundane mind and must earn their Awakening. Off: starting colonists roll the same chance as everyone else.");
            CB(l, "Reveal all cards and allow re-picking", ref s.cardRevealAll,
                "On: after turning your first awakening card you may turn the remaining cards by hand, one at a time, and re-pick any revealed card before embracing. Off (default): only the card you turn is revealed, and that path is committed.");
            IS(l, "Cards per tier-up: " + (s.cardPickCount <= 0 ? "auto (3; Tier II deals 5)" : s.cardPickCount.ToString()), ref s.cardPickCount, 0, 8,
                "How many face-down cards every tier-up event deals. Auto (0) keeps the classic spread: 3 cards, or 5 at a Tier II pilgrimage. 1-8 forces that many cards at every tier-up. The deal can never exceed the paths the pawn hasn't unlocked yet.");

            Head(l, "Transcendence (beyond Illuminated)");
            CB(l, "Enable Transcendent tiers", ref s.transcendEnabled,
                "On: an Illuminated (Tier III) psycaster who keeps meditating climbs into open-ended Transcendent tiers (IV, V, ...). Each tier grants bonus specialization points and an animated psycast-card pick for a free path. Off: Illuminated is the ceiling.");
            if (TieringControl.TranscendenceDisabled)
                ModOverrideNote(l, "Transcendence from meditation is disabled");
            if (s.transcendEnabled)
            {
                FS(l, $"First Transcendent tier after: {s.transcendBaseHours:F0} meditation hours", ref s.transcendBaseHours, 12f, 200f,
                    "Cumulative post-Illuminated meditation hours to reach Tier IV. Higher makes the endless climb slower.", true);
                FS(l, $"Per-tier cost growth: x{s.transcendGrowth:F2}", ref s.transcendGrowth, 1.1f, 3f,
                    "Each Transcendent tier costs this multiple of the previous tier's meditation (geometric diminishing returns). At x1.6, Tier V costs 1.6x Tier IV, Tier VI 2.56x, and so on.");
                IS(l, $"Forgo-card specialization points: {s.transcendForgoPoints}", ref s.transcendForgoPoints, 0, 12,
                    "At a Transcendent tier-up you may forgo the free psycast-path card for this many bonus specialization points instead (on top of the tier's own +3).");
            }
        }

        // Amber note under a setting row when a loaded mod's TieringOverrideDef has taken a path over.
        private static void ModOverrideNote(Listing_Standard l, string what)
        {
            GUI.color = new Color(1f, 0.72f, 0.35f);
            l.Label("   \u26a0 " + what + " by a tiering override from: " + TieringControl.OwnerLabel + ". This row has no effect.");
            GUI.color = Color.white;
        }

        void TabPilgrimage(Listing_Standard l)
        {
            if (TieringControl.PilgrimagesDisabled)
                ModOverrideNote(l, "Pilgrimage quests are disabled");
            var s = Settings;
            Head(l, "Trial of the Altar (combat, single site)");
            float days = s.pilgrimDailyMaxTicks > 0 ? (float)s.pilgrimMeditationTicks / s.pilgrimDailyMaxTicks : 0f;
            IS(l, $"Total meditation required: {s.pilgrimMeditationTicks / 2500f:F1}h  (~{days:F1} days at the cap)", ref s.pilgrimMeditationTicks, 10000, 150000,
                "Hours of meditation the pilgrim must complete at the altar site to advance a tier.");
            IS(l, $"Daily meditation cap: {s.pilgrimDailyMaxTicks / 2500f:F1}h / day  (0 = no cap)", ref s.pilgrimDailyMaxTicks, 0, 60000,
                "Caps the pilgrim's meditation per day, forcing the pilgrimage to span real elapsed time. Pilgrimage meditation is exempt from coma risk, so a higher cap simply finishes the quest sooner.");
            IS(l, $"Ancient Psycaster wave interval: every {s.pilgrimWaveIntervalTicks / 2500f:F1}h", ref s.pilgrimWaveIntervalTicks, 5000, 120000,
                "How often a wave of the Ancient Psycaster Order attacks the altar site. Only the altar chain has waves.");
            FS(l, $"Wave threat scale: {s.pilgrimWavePointsScale:F2}x", ref s.pilgrimWavePointsScale, 0.1f, 3f,
                "Multiplier on each wave's combat strength. Higher makes the waves deadlier.");

            Head(l, "Altar focus building (Tier II)");
            l.Label("Tier III always uses the grand throne.");
            string[][] focusOpts = {
                new[]{"PS_PilgrimThrone","Meditation throne (default; in reliquary)"},
                new[]{"PS_PilgrimAltar","Pilgrim's altar (obelisk style)"},
                new[]{"MeditationSpot","Meditation spot (basic, no reliquary)"},
                new[]{"Throne","Vanilla throne (Dignified focus, royal title)"},
                new[]{"GrandThrone","Vanilla grand throne (Dignified, royal title)"},
            };
            foreach (var opt in focusOpts)
                if (l.RadioButton(opt[1], s.pilgrimFocusDef == opt[0])) s.pilgrimFocusDef = opt[0];

            Head(l, "Way of the Anima (pacifist, multi-site)");
            IS(l, $"Meditation per site: {s.animaPilgrimTicksPerSite / 2500f:F1}h", ref s.animaPilgrimTicksPerSite, 10000, 150000,
                "Hours of meditation required at EACH anima tree site. The anima chain is bloodless: no enemies attack, and pilgrimage meditation never causes a coma.");
            IS(l, $"Tier II sites: {s.animaPilgrimT2Sites}", ref s.animaPilgrimT2Sites, 1, 6, "Number of anima-tree sites the Tier II journey visits.");
            IS(l, $"Tier III sites: {s.animaPilgrimT3Sites}  (last spawns the ancient anima tree)", ref s.animaPilgrimT3Sites, 1, 8,
                "Number of sites the Tier III journey visits; the final one grows the giant ancient anima tree.");
        }

        void TabEnemies(Listing_Standard l)
        {
            var s = Settings;
            Head(l, "Enlightened enemy psycasters");
            CB(l, "Enemy psycasters can spawn Enlightened", ref s.enemyTiersEnabled,
                "Hostile psycasters, especially the Empire, can spawn with an Enlightenment tier: boosted psylink, extra schools and abilities, full psyfocus and faster casting.");
            if (TieringControl.EnemyTiersDisabled)
                ModOverrideNote(l, "Enemy enlightenment tiers are disabled");
            if (s.enemyTiersEnabled)
            {
                CB(l, "   Allow Tier I", ref s.enemyTier1, "Permit Tier I (Awakened) hostile psycasters.");
                CB(l, "   Allow Tier II", ref s.enemyTier2, "Permit Tier II (Enlightened) hostile psycasters.");
                CB(l, "   Allow Tier III", ref s.enemyTier3, "Permit Tier III (Illuminated) hostile psycasters, the strongest.");
                FS(l, $"   Frequency: {s.enemyTierFreq * 100f:F0}% of hostile psycasters", ref s.enemyTierFreq, 0f, 1f,
                    "The chance that any given hostile psycaster rolls an Enlightenment tier.");
                CB(l, "   Tier III can spawn with an unlocked Apotheosis path", ref s.enemyAscension,
                    "Off by default. Gives elite Tier III enemy psycasters a full Apotheosis constellation's power, a serious endgame threat.");
            }
        }

    }
}

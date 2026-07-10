#nullable disable
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using VanillaPsycastsExpanded;
using VEF.Abilities;
using Verse;

namespace PsycastSynergies
{
    // ============================== XML AWAKENING TRIGGERS ==============================
    // One shared vocabulary (this DefModExtension) + one core entry point (AwakeningTrigger.Fire)
    // exposed through every XML surface RimWorld offers:
    //   - HediffDef  + HediffCompProperties_TriggerAwakening  (drugs, abilities, surgery, rituals, genes...)
    //   - ThingDef   + CompProperties_UseEffectTriggerAwakening (usable artifacts)
    //   - IncidentDef + IncidentWorker_TriggerAwakening         (storyteller / quests / scenarios / Do incident)
    //   - GameConditionDef (conditionClass GameCondition_AwakeningSurge) - boosted odds + spontaneous rolls
    //   - ThoughtDef  carrying the extension - pawns holding the thought roll chancePerDay
    //   - PreceptDef  carrying the extension - believers roll chancePerDay
    //   - QuestScriptDef via QuestNode_TriggerAwakening / QuestPart_TriggerAwakening (signal-driven)
    // Attach the extension to YOUR def:  <modExtensions><li Class="PsycastSynergies.AwakeningTriggerExtension">...
    public class AwakeningTriggerExtension : DefModExtension
    {
        // --- shared ---
        public int tier = 1;              // target enlightenment tier (1 = Awakened ... 3 = Illuminated, 4+ Transcendent). Pawns already AT/ABOVE are unaffected.
        public int minAge = 13;           // minimum biological age
        public bool silent = false;       // true: grant tier + paths directly, no card-pick window/letter flow (always the case for non-player pawns)
        public string letterLabel;        // optional custom letter; {PAWN} is replaced with the pawn's short name
        public string letterText;

        // --- one-shot surfaces (hediff / use-effect) ---
        public float chance = 1f;         // probability the trigger takes when consumed

        // --- persistent surfaces (thought / precept / surge condition) ---
        public float chancePerDay = 0.05f;      // daily probability while the thought/precept/condition holds
        public bool onlyWhileMeditating = false; // restrict the daily roll to pawns actively meditating
        public bool scaleWithCertainty = false;  // precepts only: multiply the roll by ideo certainty

        // --- surge condition only ---
        public float breakthroughMult = 3f;      // multiplies this mod's meditation awakening/breakthrough odds map-wide

        // --- incident only ---
        public string incidentTarget = "meditator"; // "meditator" (most cumulative awakening meditation) or "random"
    }

    public static class AwakeningTrigger
    {
        private static AwakeningTriggerExtension defaultExt;
        internal static AwakeningTriggerExtension Default => defaultExt ?? (defaultExt = new AwakeningTriggerExtension());

        // ---------------------------------------------------------------- eligibility
        public static bool CanFire(Pawn p, AwakeningTriggerExtension ext)
        {
            ext = ext ?? Default;
            if (p == null || p.Dead || p.Destroyed || p.RaceProps == null || !p.RaceProps.Humanlike) return false;
            if (p.health?.hediffSet == null) return false;
            if ((p.ageTracker?.AgeBiologicalYears ?? 0) < ext.minAge) return false;
            return EnlightenmentTier.TierOf(p) < Mathf.Clamp(ext.tier, 1, 99);
        }

        // ---------------------------------------------------------------- core entry
        // The single funnel every surface (and any third-party C#) calls. Player-faction pawns get the
        // full interactive flow: letter + the tiered card-pick window (which applies the tier on embrace).
        // Everyone else (or ext.silent) gets the quiet grant: tier + levels + auto-picked paths, mirroring
        // enemy-tier generation. Returns true when the pawn actually advanced (or a pick was queued).
        public static bool Fire(Pawn p, AwakeningTriggerExtension ext, string source = null)
        {
            ext = ext ?? Default;
            if (!CanFire(p, ext)) return false;
            int tier = Mathf.Clamp(ext.tier, 1, 99);

            // Mark awakened BEFORE any psylink appears so Patch_ExternalPsylinkAwaken can't double-fire.
            var med = GameComponent_PsycastSynergies.Instance?.GetMed(p, true);
            if (med != null) med.awakened = true;

            bool interactive = !ext.silent && p.Faction != null && p.Faction.IsPlayer;
            if (Prefs.DevMode)
                Log.Message("[Psycasts²] awakening trigger (" + (source ?? "code") + ") -> " + p.LabelShortCap
                    + " tier " + tier + (interactive ? " (card pick)" : " (silent)"));

            if (!interactive) return SilentGrant(p, tier);

            var psy = MeditationSystem.EnsurePsycaster(p);
            if (psy == null) return false;
            SendLetter(p, ext, tier);
            MeditationSystem.OpenPick(p, tier);   // card pick applies the tier when embraced
            return true;
        }

        // Convenience overload for third-party C# callers.
        public static bool Fire(Pawn p, int tier, bool silent = false)
            => Fire(p, new AwakeningTriggerExtension { tier = tier, silent = silent }, "api");

        // Quiet grant, mirroring enemy-tier generation: tier hediff (+spec points for player pawns),
        // psylink levels and auto-picked paths per tier gained, full psyfocus, ascension chance at III+.
        private static bool SilentGrant(Pawn p, int tier)
        {
            var psy = MeditationSystem.EnsurePsycaster(p);
            if (psy == null) return false;
            int cur = EnlightenmentTier.TierOf(p);
            bool player = p.Faction != null && p.Faction.IsPlayer;
            EnlightenmentTier.SetTier(p, tier, player);   // spec-point rewards only for the player's pawns

            int levels = 0, paths = 0;
            for (int t = cur + 1; t <= tier; t++) { levels += t >= 3 ? 5 : t == 2 ? 4 : 3; paths++; }   // 3/7/12 cumulative
            try { psy.ChangeLevel(levels); } catch { }
            Patch_EnemyTiers.GrantPaths(p, psy, p.GetComp<CompAbilities>(), paths);
            try { p.psychicEntropy?.OffsetPsyfocusDirectly(1f); } catch { }
            if (tier >= 3 && !player && (PsycastSynergiesMod.Settings?.enemyAscension ?? false))
                Patch_EnemyTiers.GrantAscension(p);

            if (player && PawnUtility.ShouldSendNotificationAbout(p))
                Find.LetterStack.ReceiveLetter("Enlightenment: " + EnlightenmentTier.Name(tier),
                    p.LabelShortCap + " has been elevated to " + EnlightenmentTier.Name(tier)
                    + ". New psychic paths and power settle into them unbidden.", LetterDefOf.PositiveEvent, p);
            return true;
        }

        private static void SendLetter(Pawn p, AwakeningTriggerExtension ext, int tier)
        {
            if (!PawnUtility.ShouldSendNotificationAbout(p)) return;
            string label = !ext.letterLabel.NullOrEmpty() ? ext.letterLabel.Replace("{PAWN}", p.LabelShortCap)
                : tier <= 1 ? "Psychic awakening" : "Enlightenment: " + EnlightenmentTier.Name(tier);
            string text = !ext.letterText.NullOrEmpty() ? ext.letterText.Replace("{PAWN}", p.LabelShortCap)
                : tier <= 1
                    ? p.LabelShortCap + " has awakened as a psycaster - a surge of psychic power stirred a latent path to the surface. Choose the first path to walk."
                    : p.LabelShortCap + " has been elevated toward " + EnlightenmentTier.Name(tier) + ". A new psycast path manifests for them to claim.";
            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.PositiveEvent, p);
        }

        // ---------------------------------------------------------------- surge condition
        // Product of every active awakening-surge condition affecting the map (world conditions included).
        private static readonly List<GameCondition> tmpConditions = new List<GameCondition>();
        public static float SurgeMult(Map map)
        {
            if (map == null) return 1f;
            float mult = 1f;
            tmpConditions.Clear();
            map.gameConditionManager.GetAllGameConditionsAffectingMap(map, tmpConditions);
            for (int i = 0; i < tmpConditions.Count; i++)
                if (tmpConditions[i] is GameCondition_AwakeningSurge)
                    mult *= Mathf.Max(0f, tmpConditions[i].def.GetModExtension<AwakeningTriggerExtension>()?.breakthroughMult ?? 3f);
            return mult;
        }

        // ---------------------------------------------------------------- hourly passive scan
        // Drives the persistent surfaces: surge spontaneous rolls, thought-carried and precept-carried
        // triggers. Called every 2500 ticks from MeditationSystem.Tick. Costs nothing when no def in the
        // game carries the extension (cached lists are empty).
        private static bool cacheBuilt;
        private static readonly List<ThoughtDef> thoughtTriggers = new List<ThoughtDef>();
        private static readonly List<PreceptDef> preceptTriggers = new List<PreceptDef>();
        private static readonly List<Thought> tmpThoughts = new List<Thought>();

        private static void BuildCache()
        {
            cacheBuilt = true;
            foreach (var d in DefDatabase<ThoughtDef>.AllDefsListForReading)
                if (d.HasModExtension<AwakeningTriggerExtension>()) thoughtTriggers.Add(d);
            foreach (var d in DefDatabase<PreceptDef>.AllDefsListForReading)
                if (d.HasModExtension<AwakeningTriggerExtension>()) preceptTriggers.Add(d);
            if (Prefs.DevMode && (thoughtTriggers.Count > 0 || preceptTriggers.Count > 0))
                Log.Message("[Psycasts²] awakening triggers registered: " + thoughtTriggers.Count + " thought(s), " + preceptTriggers.Count + " precept(s).");
        }

        private static float PerHour(float perDay) => 1f - Mathf.Pow(1f - Mathf.Clamp01(perDay), 1f / 24f);

        internal static void HourlyScan(GameComponent_PsycastSynergies gc)
        {
            if (!cacheBuilt) BuildCache();
            var maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                var surgeExt = ActiveSurgeExt(maps[m]);
                var pawns = maps[m].mapPawns.FreeColonistsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    var p = pawns[i];
                    // Surge condition: spontaneous awakening rolls (meditators only, unless configured otherwise).
                    if (surgeExt != null
                        && (!surgeExt.onlyWhileMeditating || MeditationSystem.IsActivelyMeditating(p))
                        && Rand.Chance(PerHour(surgeExt.chancePerDay)))
                        Fire(p, surgeExt, "surge condition");
                    ScanPersistent(p);
                }
            }
            // Thought/precept triggers also follow colonists off-map (caravans, transporters).
            var travelers = PawnsFinder.AllCaravansAndTravellingTransporters_Alive;
            for (int i = 0; i < travelers.Count; i++)
                if (travelers[i].IsFreeColonist) ScanPersistent(travelers[i]);
        }

        private static AwakeningTriggerExtension ActiveSurgeExt(Map map)
        {
            tmpConditions.Clear();
            map.gameConditionManager.GetAllGameConditionsAffectingMap(map, tmpConditions);
            for (int i = 0; i < tmpConditions.Count; i++)
                if (tmpConditions[i] is GameCondition_AwakeningSurge)
                {
                    var ext = tmpConditions[i].def.GetModExtension<AwakeningTriggerExtension>();
                    if (ext != null && ext.chancePerDay > 0f) return ext;
                }
            return null;
        }

        private static void ScanPersistent(Pawn p)
        {
            // Thought-carried: any currently-active mood thought (memory OR situational) whose def carries the extension.
            if (thoughtTriggers.Count > 0 && p.needs?.mood?.thoughts != null)
            {
                tmpThoughts.Clear();
                p.needs.mood.thoughts.GetAllMoodThoughts(tmpThoughts);
                for (int i = 0; i < tmpThoughts.Count; i++)
                {
                    var ext = tmpThoughts[i]?.def?.GetModExtension<AwakeningTriggerExtension>();
                    if (ext == null) continue;
                    if (ext.onlyWhileMeditating && !MeditationSystem.IsActivelyMeditating(p)) continue;
                    if (Rand.Chance(PerHour(ext.chancePerDay))) { Fire(p, ext, "thought " + tmpThoughts[i].def.defName); return; }
                }
            }
            // Precept-carried: the pawn's ideo contains a precept whose def carries the extension.
            if (preceptTriggers.Count > 0 && p.Ideo != null)
            {
                var precepts = p.Ideo.PreceptsListForReading;
                for (int i = 0; i < precepts.Count; i++)
                {
                    var ext = precepts[i]?.def?.GetModExtension<AwakeningTriggerExtension>();
                    if (ext == null) continue;
                    if (ext.onlyWhileMeditating && !MeditationSystem.IsActivelyMeditating(p)) continue;
                    float mult = ext.scaleWithCertainty ? Mathf.Clamp01(p.ideo?.Certainty ?? 1f) : 1f;
                    if (Rand.Chance(PerHour(ext.chancePerDay) * mult)) { Fire(p, ext, "precept " + precepts[i].def.defName); return; }
                }
            }
        }
    }
}

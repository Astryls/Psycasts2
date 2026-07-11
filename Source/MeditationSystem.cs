#nullable disable
using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using HarmonyLib;
using VanillaPsycastsExpanded;

namespace PsycastSynergies
{
    [DefOf]
    public static class MeditationDefOf
    {
        public static HediffDef PsychicComa;
        public static ThoughtDef PS_InsightMemory;
        static MeditationDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(MeditationDefOf));
    }

    // Per-pawn meditation / awakening tracking (lives in the GameComponent save).
    public class MeditationData : IExposable
    {
        public int streakTicks;          // consecutive meditation (resets after a gap)
        public int todayTicks;           // meditation this day (drives the coma risk)
        public int lastMedTick = -99999;
        public int enlightenments;       // progress toward awakening
        public int awakenThreshold;      // HIDDEN, rolled 3-8 on first meditation
        public bool awakened;            // set when the awakening fires (Stage 2)
        public int tier;                 // enlightenment tier reached (mirrors the PS_Enlightenment hediff)
        public int pendingPick;          // a tier whose card pick was deferred to reroll via meditation (0 = none)
        public int rerollCount;          // deferred rerolls this pick - each raises psychic-coma risk
        public System.Collections.Generic.List<PsycasterPathDef> cardPaths;   // paths chosen via cards, in tier order (for path respec)
        public string focusType;         // VPE meditation-focus TYPE defName (or building defName) of the last focus meditated at
        public string defaultFocus;      // pawn's personal default focus type - used when meditating at an unattuned building
        public bool forcedMeditation;    // "meditate your ass off": keep meditating continuously (forces time assignment)
        public int pilgrimStyle;         // tier-up pilgrimage routing: 0 = Either, 1 = Combat (altar), 2 = Pacifist (anima)
        public float medSaturation;      // anti-farming: builds with daily meditation, shrinks breakthrough chance
        public int awakenMeditationTicks; // cumulative meditation toward Awakening (non-psycasters) - feeds the ramp + pity
        public int transcendTicks;        // cumulative post-Illuminated meditation toward the next Transcendent tier (resets on tier-up)

        // Non-retroactive, tier-scaled auto-stat accumulators (see AutoStats). Each psycaster level adds the
        // base per-level amount scaled by the enlightenment tier at THAT moment, so newer-tier levels are worth more.
        public float autoHeat, autoRecovery, autoSensitivity;
        public int autoLevel;   // highest psycaster level already folded into the accumulators
        public bool autoInit;   // seeded existing levels (at the tier-0 rate) yet?

        public void ExposeData()
        {
            Scribe_Values.Look(ref streakTicks, "streak", 0);
            Scribe_Values.Look(ref todayTicks, "today", 0);
            Scribe_Values.Look(ref lastMedTick, "lastMed", -99999);
            Scribe_Values.Look(ref enlightenments, "enl", 0);
            Scribe_Values.Look(ref awakenThreshold, "thresh", 0);
            Scribe_Values.Look(ref awakened, "awakened", false);
            Scribe_Values.Look(ref tier, "tier", 0);
            Scribe_Values.Look(ref pendingPick, "pendingPick", 0);
            Scribe_Values.Look(ref rerollCount, "rerollCount", 0);
            Scribe_Collections.Look(ref cardPaths, "cardPaths", LookMode.Def);
            Scribe_Values.Look(ref focusType, "focus");
            Scribe_Values.Look(ref defaultFocus, "defaultFocus");
            Scribe_Values.Look(ref forcedMeditation, "forcedMeditation", false);
            Scribe_Values.Look(ref pilgrimStyle, "pilgrimStyle", 0);
            Scribe_Values.Look(ref medSaturation, "medSaturation", 0f);
            Scribe_Values.Look(ref awakenMeditationTicks, "awakenMeditationTicks", 0);
            Scribe_Values.Look(ref transcendTicks, "transcendTicks", 0);
            Scribe_Values.Look(ref autoHeat, "autoHeat", 0f);
            Scribe_Values.Look(ref autoRecovery, "autoRecovery", 0f);
            Scribe_Values.Look(ref autoSensitivity, "autoSensitivity", 0f);
            Scribe_Values.Look(ref autoLevel, "autoLevel", 0);
            Scribe_Values.Look(ref autoInit, "autoInit", false);
        }
    }

    // Meditation drives "a flow of ancient knowledge" breakthrough events toward Awakening (the awakening
    // chance ramps with cumulative meditation and is guaranteed within ~a week of dedication), while the
    // psychic-coma RISK rises with how much a pawn meditates in a day - so round-the-clock meditation
    // backfires. Psycasters instead get a full-level XP burst from each flow of ancient knowledge.
    public static class MeditationSystem
    {
        // Re-entrancy guard: true while OUR awakening creates the psylink, so the external-psylink Harmony
        // postfix doesn't mistake our own meditation awakening for an Empire/anima grant.
        internal static bool internalPsylinkChange;

        public static void Tick(int t)
        {
            var s = PsycastSynergiesMod.Settings;
            var gc = GameComponent_PsycastSynergies.Instance;
            if (s == null || gc == null) return;

            if (t % 60000 == 0)
            {
                float satThreshold = (s.comaSafeHours > 0f ? s.comaSafeHours : 6f) * 2500f;   // only OVER-meditation counts
                foreach (var d in gc.MedDataValues)
                {
                    // Anti-farming saturation: only meditating PAST the daily safe window (6h by default) builds it
                    // up; any lighter day sheds it faster. So grinding many hours every day makes breakthroughs
                    // progressively rarer AND comas progressively longer, while normal meditation never accrues it.
                    d.medSaturation = d.todayTicks >= satThreshold
                        ? Mathf.Min(d.medSaturation + 1f, 6f)
                        : Mathf.Max(d.medSaturation - 1.5f, 0f);
                    d.todayTicks = 0;   // daily reset
                }
            }
            if (t % 60 == 0) Accumulate(t, gc);
            if (s.enlightenmentEnabled && t % 2500 == 0) RollHourly(t, gc, s);
            if (t % 2500 == 0) AwakeningTrigger.HourlyScan(gc);   // XML trigger surfaces (thought/precept/surge), independent of the enlightenment toggle
        }

        [DebugAction("Psycasts²", "Force awakening (Tier I)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Debug_ForceTier1(Pawn p)
        {
            var gc = GameComponent_PsycastSynergies.Instance;
            if (gc == null || p == null) return;
            var med = gc.GetMed(p, true);
            med.awakened = true;   // set BEFORE Awaken so the external-psylink postfix doesn't double-fire
            Awaken(p, med);
        }

        [DebugAction("Psycasts²", "Grant psycaster level (+1)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Debug_GrantLevel(Pawn p)
        {
            if (p == null) return;
            EnsurePsycaster(p);
            var h = p.Psycasts();
            if (h != null) h.GainExperience(Hediff_PsycastAbilities.ExperienceRequiredForLevel(h.level + 1), false);
        }

        // One-click mastery: every learned psycast jumps to the ABSOLUTE cap (10 / 15 with
        // Convergence) so the mastered-tree gold glow + sparkles can be verified without
        // hundreds of psycaster levels. State-only (no invest bursts - those are player-path FX).
        [DebugAction("Psycasts²", "Max all psycast skills (absolute cap)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Debug_MaxAllSkills(Pawn p)
        {
            var gc = GameComponent_PsycastSynergies.Instance;
            var comp = p?.GetComp<VEF.Abilities.CompAbilities>();
            if (gc == null || comp?.LearnedAbilities == null) return;
            var s = PsycastSynergiesMod.Settings;
            int absCap = (s?.maxSkillLevel ?? 10) + SpecEffects.LevelCapBonus(p);
            int n = 0;
            foreach (var ab in comp.LearnedAbilities)
            {
                if (ab?.def == null || ab.def.GetModExtension<AbilityExtension_Psycast>() == null) continue;
                gc.SetLevel(p, ab.def, absCap);
                n++;
            }
            Messages.Message(p.LabelShortCap + ": " + n + " psycast(s) set to level " + absCap + ".", p, MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction("Psycasts²", "Force Enlightened (Tier II)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Debug_ForceTier2(Pawn p)
        {
            if (p == null) return;
            EnsurePsycaster(p);
            OpenPick(p, 2);
        }

        [DebugAction("Psycasts²", "Force Illuminated (Tier III)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Debug_ForceTier3(Pawn p)
        {
            if (p == null) return;
            EnsurePsycaster(p);
            OpenPick(p, 3);
        }

        [DebugAction("Psycasts²", "Transcend (next tier IV+)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Debug_Transcend(Pawn p)
        {
            if (p == null) return;
            EnsurePsycaster(p);
            var med = GameComponent_PsycastSynergies.Instance?.GetMed(p, true);
            int cur = EnlightenmentTier.TierOf(p);
            if (cur < 3) { EnlightenmentTier.SetTier(p, 3, false); cur = 3; }   // jump to Illuminated first
            if (med != null) med.transcendTicks = 0;
            Transcend(p, med, cur + 1);
        }

        [DebugAction("Psycasts²", "Reset to non-psycaster (wipe awakening)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Debug_ResetPsycaster(Pawn p)
        {
            if (p?.health?.hediffSet == null) return;
            // Sweep off every status this mod adds (PS_*), the VPE psycast implant, and the psylink itself.
            var toRemove = new List<Hediff>();
            foreach (var h in p.health.hediffSet.hediffs)
            {
                var dn = h.def?.defName;
                if ((dn != null && dn.StartsWith("PS_")) || dn == "VPE_PsycastAbilityImplant" || h is Hediff_Psylink)
                    toRemove.Add(h);
            }
            internalPsylinkChange = true;   // suppress the external-psylink re-awaken postfix during removal
            try { foreach (var h in toRemove) p.health.RemoveHediff(h); }
            finally { internalPsylinkChange = false; }
            // Wipe our per-pawn bookkeeping: skill levels, specialization points/nodes, meditation + tier data.
            GameComponent_PsycastSynergies.Instance?.ResetPawn(p);
            Messages.Message(p.LabelShortCap + " reset to a non-psycaster, awakening wiped.", p, MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction("Psycasts²", "Open 2 picks at once (queue test)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Debug_TwoPicks()
        {
            var colonists = Find.CurrentMap?.mapPawns?.FreeColonists;
            if (colonists == null) return;
            for (int i = 0, n = 0; i < colonists.Count && n < 2; i++, n++)
                OpenPick(colonists[i], 1);   // two in a row: first opens, second queues, then chains on close
        }

        // Only count a pawn as meditating while they're actually in the meditation act - NOT while walking to
        // the spot (the Meditate job's goto toil still reports CurJobDef == Meditate). Without this, a pawn
        // whose schedule sends them to meditate accrues progress during the walk, before they ever sit down.
        internal static bool IsActivelyMeditating(Pawn p)
            => p?.CurJobDef == JobDefOf.Meditate && p.pather != null && !p.pather.MovingNow;

        private static void Accumulate(int t, GameComponent_PsycastSynergies gc)
        {
            var maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                var pawns = maps[m].mapPawns.FreeColonistsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    var p = pawns[i];
                    if (!IsActivelyMeditating(p)) continue;
                    var med = gc.GetMed(p, true);
                    med.streakTicks += 60;
                    med.todayTicks += 60;
                    med.lastMedTick = t;
                    if (p.Psycasts() == null) med.awakenMeditationTicks += 60;   // cumulative meditation toward Awakening
                    else if (med.tier >= 3) med.transcendTicks += 60;             // Illuminated+ psycasters climb toward Transcendence
                    if (med.awakenThreshold == 0) med.awakenThreshold = Rand.RangeInclusive(5, 10);
                    // The meditate job stores the FOCUS object in targetC and the SPOT/seat in targetA
                    // (targetB is null). Priority chain: building's gizmo > pawn's personal default >
                    // building's NATIVE focus type (e.g. brazier -> Flame) > null (trait-weighted).
                    var focusThing = p.CurJob?.targetC.Thing;
                    var spotThing = p.CurJob?.targetA.Thing;
                    MeditationFocusDef pawnDefault = string.IsNullOrEmpty(med.defaultFocus) ? null
                        : DefDatabase<MeditationFocusDef>.GetNamedSilentFail(med.defaultFocus);
                    var ft = focusThing?.TryGetComp<CompSchoolFocus>()?.selectedFocus
                             ?? spotThing?.TryGetComp<CompSchoolFocus>()?.selectedFocus
                             ?? pawnDefault
                             ?? NativeFocus(focusThing) ?? NativeFocus(spotThing);
                    med.focusType = ft?.defName;
                }
            }
            foreach (var kv in gc.MedDataPairs)
                if (kv.Value.streakTicks > 0 && t - kv.Value.lastMedTick > 1250) kv.Value.streakTicks = 0;   // gap breaks streak
        }

        private static void RollHourly(int t, GameComponent_PsycastSynergies gc, PsycastSynergiesSettings s)
        {
            var maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                var pawns = maps[m].mapPawns.FreeColonistsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    var p = pawns[i];
                    if (!IsActivelyMeditating(p)) continue;
                    var med = gc.GetMed(p, true);
                    float streakHours = med.streakTicks / 2500f;
                    float todayHours = med.todayTicks / 2500f;

                    // Over-meditation: psychic-coma risk past the daily safe window (+ a streak factor),
                    // raised further for each deferred card reroll. EXEMPT on pilgrimage site maps - the
                    // quest forces long daily meditation (well past the safe window), and a coma there would
                    // sabotage the pilgrimage (especially the pacifist anima chain, which must stay safe).
                    if (!PilgrimRouting.IsPilgrimageMap(p.Map))
                    {
                        float comaRisk = Mathf.Max(0f, (todayHours - s.comaSafeHours) * s.comaRiskPerHour)
                                       + Mathf.Max(0f, (streakHours - s.comaSafeHours) * s.comaRiskPerHour * 0.5f)
                                       + med.rerollCount * 0.06f;
                        if (comaRisk > 0f && Rand.Chance(Mathf.Min(comaRisk, 0.75f)))
                        {
                            ApplyComa(p, med);
                            med.streakTicks = 0;
                            // The pending card pick SURVIVES a coma - forfeiting it here could permanently
                            // lose a tier-up (soft lock). Alert_PendingCardPick keeps it recoverable; the
                            // coma's downtime and saturation are punishment enough.
                            continue;
                        }
                    }

                    // Deferred reroll: re-open the card pick after the pawn meditated on it.
                    if (med.pendingPick > 0)
                    {
                        int tier = med.pendingPick;
                        med.pendingPick = 0;
                        OpenPick(p, tier);   // rerollCount feeds the pool seed for a fresh draw
                        continue;
                    }

                    // Transcendence: an Illuminated (tier 3+) psycaster who keeps meditating climbs into the
                    // open-ended Transcendent tiers. Each tier costs geometrically more, so the climb slows hard.
                    if (med.tier >= 3 && s.transcendEnabled && !TieringControl.TranscendenceDisabled
                        && med.transcendTicks >= TranscendThreshold(med.tier + 1, s))
                    {
                        med.transcendTicks = 0;
                        Transcend(p, med, med.tier + 1);
                        continue;
                    }

                    // Non-psycasters are on the AWAKENING ramp: the chance climbs with CUMULATIVE meditation so a
                    // dedicated colonist reaches Tier I within ~a week, with a hard guarantee at the end of the
                    // window. Psycasters use the saturation-scaled streak chance for their full-level breakthroughs.
                    if (p.Psycasts() == null)
                    {
                        if (TieringControl.MeditationAwakeningDisabled) continue;   // a mod owns awakening now
                        float cumHours = med.awakenMeditationTicks / 2500f;
                        if (s.awakenGuaranteeHours > 0f && cumHours >= s.awakenGuaranteeHours)
                        {
                            med.enlightenments = med.awakenThreshold;   // pity: force the final insight -> Awakening
                            Enlighten(p, med, s);
                        }
                        else if (Rand.Chance(Mathf.Min((0.08f + cumHours * 0.02f) * AwakeningTrigger.SurgeMult(p.Map), 0.6f)))
                            Enlighten(p, med, s);
                        continue;
                    }
                    // Saturation from habitual daily meditation scales the whole chance down (anti-farming).
                    float satMult = 1f / (1f + med.medSaturation * s.enlightenmentSaturationFactor);
                    // Above Illuminated, each Transcendent tier speeds breakthroughs (leveling slows hard past 30) -
                    // but the 0.6 hard cap holds, so a breakthrough is never guaranteed.
                    float tierMult = med.tier > 3 ? 1f + (med.tier - 3) * s.transcendBreakthroughCurve : 1f;
                    float chance = Mathf.Min((s.enlightenmentChance + streakHours * s.enlightenmentStreakBonus) * satMult * tierMult * AwakeningTrigger.SurgeMult(p.Map), 0.6f);
                    if (Rand.Chance(chance)) Enlighten(p, med, s);
                }
            }
        }

        private static void Enlighten(Pawn p, MeditationData med, PsycastSynergiesSettings s)
        {
            var psy = p.Psycasts();
            if (psy != null)
            {
                if (psy.level < (PsycastsMod.Settings?.maxLevel ?? 30))
                {
                    float need = Hediff_PsycastAbilities.ExperienceRequiredForLevel(psy.level + 1);
                    psy.GainExperience(need * s.enlightenmentFrac, false);
                    med.medSaturation += 2f;   // a full-level breakthrough sates the mind; the next one is much rarer
                    Fx(p);
                    Notify(p, "PS_LetterFlowLabel".Translate(), "PS_LetterFlowText".Translate(p.LabelShortCap));
                }
                return;
            }

            // Non-psycaster: progress toward awakening (threshold is hidden, so no count is shown).
            med.enlightenments++;
            var thought = MeditationDefOf.PS_InsightMemory;
            if (thought != null) p.needs?.mood?.thoughts?.memories?.TryGainMemory(thought);
            Fx(p);
            if (med.enlightenments >= med.awakenThreshold && !med.awakened)
            {
                med.awakened = true;
                Awaken(p, med);
            }
            else if (!med.awakened)
            {
                Messages.Message("PS_MsgStirring".Translate(p.LabelShortCap), p, MessageTypeDefOf.PositiveEvent, false);
            }
        }

        // Ensure the pawn is a VPE psycaster (creating the psylink + hediff if needed); returns the hediff.
        internal static Hediff_PsycastAbilities EnsurePsycaster(Pawn p)
        {
            var psy = p.Psycasts();
            if (psy != null) return psy;
            internalPsylinkChange = true;
            try { p.ChangePsylinkLevel(1, false); }    // creates the psylink; VPE attaches its psycast hediff
            finally { internalPsylinkChange = false; }
            psy = p.Psycasts();
            if (psy == null)
            {
                // Safety net: VPE didn't auto-attach - build its hediff straight from the psylink.
                var def = DefDatabase<HediffDef>.GetNamedSilentFail("VPE_PsycastAbilityImplant");
                var psylink = p.GetMainPsylinkSource();
                if (def != null && psylink != null)
                {
                    var h = (Hediff_PsycastAbilities)HediffMaker.MakeHediff(def, p);
                    h.InitializeFromPsylink(psylink);
                    p.health.AddHediff(h, p.health.hediffSet.GetBrain());
                    psy = h;
                }
            }
            return psy;
        }

        // Tier I: meditation turns a non-psycaster into a psycaster and offers the first path pick.
        private static void Awaken(Pawn p, MeditationData med)
        {
            var psy = EnsurePsycaster(p);
            Notify(p, "PS_LetterAwakeningLabel".Translate(), "PS_LetterAwakenMeditationText".Translate(p.LabelShortCap));
            if (psy == null) return;
            OpenPick(p, 1);
        }

        // Awaken a pawn who gained a psylink from OUTSIDE meditation (Empire bestowing/blinding ritual, or
        // anima-tree linking). They already HAVE the psylink, so attach the VPE implant from it WITHOUT
        // adding another level, then route them into our system with the first path pick.
        public static void AwakenExternal(Pawn p, MeditationData med)
        {
            if (p == null || med == null || med.awakened) return;
            med.awakened = true;
            var psy = p.Psycasts();
            if (psy == null)
            {
                var def = DefDatabase<HediffDef>.GetNamedSilentFail("VPE_PsycastAbilityImplant");
                var psylink = p.GetMainPsylinkSource();
                if (def != null && psylink != null)
                {
                    var h = (Hediff_PsycastAbilities)HediffMaker.MakeHediff(def, p);
                    h.InitializeFromPsylink(psylink);
                    p.health.AddHediff(h, p.health.hediffSet.GetBrain());
                    psy = h;
                }
            }
            if (psy == null) return;
            Notify(p, "PS_LetterAwakeningLabel".Translate(), "PS_LetterAwakenExternalText".Translate(p.LabelShortCap));
            OpenPick(p, 1);
        }

        // Opens the tiered card pick: Tier I = 3 themed, Tier II = 5 themed, Tier III = 3 any-roll
        // (the cards-per-tier-up setting, when >0, overrides those counts for every tier).
        // Pending card picks. Multiple pawns can awaken/ascend on the SAME tick (e.g. two non-psycasters hit
        // their threshold in one hourly roll, or a mass psylink grant). Opening every Window_Awakening at once
        // stacks them modally and one gets lost, so we QUEUE picks and show exactly one at a time - the next
        // opens when the current one closes (Window_Awakening.PostClose chains here).
        private struct PickRequest { public Pawn pawn; public int tier; }
        private static readonly Queue<PickRequest> pickQueue = new Queue<PickRequest>();

        // Geometric meditation cost (in ticks) to reach a given Transcendent tier (>=4). Diminishing returns:
        // each tier costs growth-x more than the last, so the climb beyond Illuminated slows dramatically.
        private static float TranscendThreshold(int nextTier, PsycastSynergiesSettings s)
        {
            float baseTicks = Mathf.Max(1f, s?.transcendBaseHours ?? 48f) * 2500f;
            float growth = Mathf.Max(1.05f, s?.transcendGrowth ?? 1.6f);
            return baseTicks * Mathf.Pow(growth, Mathf.Max(0, nextTier - 4));
        }

        // Advance a pawn into the next Transcendent tier: grant the tier (+spec points) and open the animated
        // psycast-card pick for a bonus free path (its embrace unlocks the path; the SetTier there is a no-op).
        private static void Transcend(Pawn p, MeditationData med, int nextTier)
        {
            EnlightenmentTier.SetTier(p, nextTier, true);
            OpenPick(p, nextTier);
            if (PawnUtility.ShouldSendNotificationAbout(p))
                Find.LetterStack.ReceiveLetter("PS_LetterTranscendence".Translate(EnlightenmentTier.Name(nextTier)),
                    "PS_LetterTranscendText".Translate(p.LabelShortCap, EnlightenmentTier.Name(nextTier)),
                    LetterDefOf.PositiveEvent, p);
        }

        public static void OpenPick(Pawn p, int tier)
        {
            if (p == null) return;
            pickQueue.Enqueue(new PickRequest { pawn = p, tier = tier });
            ShowNextPick();
        }

        // Opens the next queued pick, but only if no awakening window is currently up (one at a time).
        internal static void ShowNextPick()
        {
            if (Find.WindowStack == null || Find.WindowStack.IsOpen(typeof(Window_Awakening))) return;
            while (pickQueue.Count > 0)
            {
                var req = pickQueue.Dequeue();
                if (req.pawn == null || req.pawn.Dead) continue;
                var med = GameComponent_PsycastSynergies.Instance?.GetMed(req.pawn, true);
                var st = PsycastSynergiesMod.Settings;
                int count = st != null && st.cardPickCount > 0 ? st.cardPickCount : (req.tier == 2 ? 5 : 3);
                bool anyRoll = req.tier >= 3;
                var pool = BuildPool(req.pawn, med, count, !anyRoll, anyRoll);
                if (pool.Count == 0) continue;
                try { Find.WindowStack.Add(new Window_Awakening(req.pawn, pool, req.tier)); return; }
                catch (System.Exception e) { Log.Warning("[PsycastSynergies] tier-" + req.tier + " pick window failed: " + e); }
            }
        }

        // Candidate paths for a pick: `count` unlocked-eligible paths, biased toward the meditation type
        // unless anyRoll (Tier III draws from EVERY path, rare ones included).
        private static List<PsycasterPathDef> BuildPool(Pawn p, MeditationData med, int count, bool theme, bool anyRoll)
        {
            var psy = p.Psycasts();
            var unlocked = psy?.unlockedPaths;
            var all = new List<PsycasterPathDef>();
            foreach (var path in DefDatabase<PsycasterPathDef>.AllDefs)
                if (path.HasAbilities && (unlocked == null || !unlocked.Contains(path))) all.Add(path);

            var pick = new List<PsycasterPathDef>();
            var rng = new System.Random(p.thingIDNumber * 31 + (med?.enlightenments ?? 0) + count * 7 + (anyRoll ? 101 : 0) + (med?.rerollCount ?? 0) * 17);

            if (theme && !anyRoll)
            {
                List<PsycasterPathDef> themed = null;
                // A focus TYPE (gizmo, or the focus building's native type) selects an EXCLUSIVE bucket.
                if (!string.IsNullOrEmpty(med?.focusType)
                    && DefDatabase<MeditationFocusDef>.GetNamedSilentFail(med.focusType) != null)
                {
                    themed = new List<PsycasterPathDef>();
                    foreach (var path in all) if (FocusForSchool(path) == med.focusType) themed.Add(path);
                }
                if (themed == null || themed.Count == 0)
                {
                    var kw = TraitKeywords(p);   // no focus type -> weight by the meditator's personality traits
                    if (kw != null)
                    {
                        themed = new List<PsycasterPathDef>();
                        foreach (var path in all)
                        {
                            string nm = ((path.defName ?? "") + " " + (path.label ?? "")).ToLowerInvariant();
                            for (int k = 0; k < kw.Length; k++) if (nm.Contains(kw[k])) { themed.Add(path); break; }
                        }
                    }
                }
                if (themed != null && themed.Count > 0) TakeRandom(themed, count - 1, pick, rng);   // mostly themed, leave room for variety
            }
            TakeRandom(all, count - pick.Count, pick, rng);   // fill to count
            return pick;
        }

        private static void TakeRandom(List<PsycasterPathDef> from, int count, List<PsycasterPathDef> into, System.Random rng)
        {
            var avail = new List<PsycasterPathDef>();
            foreach (var x in from) if (!into.Contains(x)) avail.Add(x);
            while (count-- > 0 && avail.Count > 0)
            {
                int idx = rng.Next(avail.Count);
                into.Add(avail[idx]);
                avail.RemoveAt(idx);
            }
        }

        // EXCLUSIVE focus-type -> school assignment. Each school belongs to exactly ONE focus type
        // (first keyword match wins), so meditating at a focus type rolls ONLY that thematic group --
        // no duplicate rolls across types. Curated for the common VPE + addon schools; anything unmapped
        // lands in a deterministic even bucket. Edit a row to re-theme a school.
        private static readonly string[][] FocusThemes = new string[][]
        {
            new[]{ "Morbid",        "necropath","deadlife","hemosage","veincaster","deathmarch","destined death" },
            new[]{ "Flame",         "conflagrator","flameheart","luminis","civilight","shining","meteor" },
            new[]{ "Void",          "voidweaver","umbra","nightstalker","horaxian","lunacy" },
            new[]{ "Natural",       "druid","empath","animancer","wildhunter","wildspeaker","bugmancer" },
            new[]{ "Artistic",      "harmonist","chronopath","fateweaver","tieweaver","skipmaster","phase shift" },
            new[]{ "VPE_Science",   "technomancer","neurophage","oripathy","biohazard","glitch","staticlord" },
            new[]{ "VPE_Archotech", "archotechist","archon","enlightened","knowledge","gravcaster","saileach" },
            new[]{ "VPE_Group",     "puppeteer","protector","warlord","amiya","kal'tsit","silence" },
            new[]{ "VPE_Wealth",    "geomancer","mudrock","frostshaper","hydromancer","aeromancer","ascalon" },
            new[]{ "Dignified",     "crownslayer","ines","wis'adel","blader","ranger","biosoother" },
            new[]{ "Minimal",       "mechanitor" },
        };
        private static readonly string[] FocusKeys =
            { "Flame","Morbid","Void","Natural","Artistic","VPE_Science","VPE_Archotech","VPE_Group","VPE_Wealth","Dignified","Minimal" };

        // The one focus type (its defName) a school belongs to. First keyword match wins -> exclusive.
        public static string FocusForSchool(PsycasterPathDef path)
        {
            if (path?.defName == null) return null;
            string nm = ((path.defName ?? "") + " " + (path.label ?? "")).ToLowerInvariant();
            foreach (var t in FocusThemes)
                for (int i = 1; i < t.Length; i++)
                    if (nm.Contains(t[i])) return t[0];
            return FocusKeys[(int)(Hash(path.defName) % (uint)FocusKeys.Length)];   // even fallback for unmapped
        }

        private static uint Hash(string s)
        {
            uint h = 2166136261u;
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; }
            return h & 0x7fffffffu;
        }

        // Public for the field manual + awakening: the schools steered toward by a focus TYPE (exclusive).
        public static System.Collections.Generic.List<PsycasterPathDef> FocusBiasedPaths(string focusType)
        {
            var result = new System.Collections.Generic.List<PsycasterPathDef>();
            if (string.IsNullOrEmpty(focusType)) return result;
            foreach (var path in DefDatabase<PsycasterPathDef>.AllDefs)
                if (path.HasAbilities && FocusForSchool(path) == focusType) result.Add(path);
            return result;
        }

        // The focus building's OWN native meditation-focus type (anima tree -> Natural, brazier -> Flame...).
        private static MeditationFocusDef NativeFocus(Thing t)
        {
            var props = t?.def?.GetCompProperties<CompProperties_MeditationFocus>();
            return (props?.focusTypes != null && props.focusTypes.Count > 0) ? props.focusTypes[0] : null;
        }

        // Rough meditation-type → theme keyword mapping (Stage 3 buildings will define exact pools).
        private static string[] FocusKeywords(string focus)
        {
            if (string.IsNullOrEmpty(focus)) return null;
            string f = focus.ToLowerInvariant();
            // VPE / vanilla meditation-focus TYPES picked via the focus gizmo. ("Flame"/"Morbid"/
            // "Artistic" are already caught by the building-name checks below; these add the rest.)
            if (f.Contains("natural") || f.Contains("nature"))
                return new[] { "wild", "nature", "druid", "gauranlen", "anima", "empath", "ranger", "wood", "harmon" };
            if (f.Contains("science") || f.Contains("techno"))
                return new[] { "techno", "mech", "machine", "nano", "bot", "circuit", "neuro", "oripathy", "scribe" };
            if (f.Contains("archotech"))
                return new[] { "archo", "ascend", "void", "celestial", "star", "empyrean", "chrono" };
            if (f.Contains("wealth"))
                return new[] { "greed", "wealth", "gold", "chrono", "harmon", "skip" };
            if (f.Contains("group"))
                return new[] { "consonance", "puppet", "group", "empath", "harmon", "ally", "anima" };

            if (f.Contains("anima") || f.Contains("tree") || f.Contains("plant") || f.Contains("gauranlen") || f.Contains("flower"))
                return new[] { "wild", "nature", "druid", "gauranlen", "anima", "empath", "ranger", "wood", "harmon" };
            if (f.Contains("brazier") || f.Contains("torch") || f.Contains("campfire") || f.Contains("fire") || f.Contains("flame") || f.Contains("lamp"))
                return new[] { "flame", "conflag", "luminis", "pyro", "fire", "ember", "sun", "warden" };
            if (f.Contains("grave") || f.Contains("sarcoph") || f.Contains("skull") || f.Contains("morbid") || f.Contains("tomb"))
                return new[] { "necro", "death", "deadlife", "hemo", "umbra", "blood", "void", "morbid", "bone" };
            if (f.Contains("sculpt") || f.Contains("art"))
                return new[] { "harmon", "chrono", "skip", "static", "arknight", "glitch", "time", "phase" };
            if (f.Contains("snow") || f.Contains("ice") || f.Contains("water"))
                return new[] { "hydro", "frost", "ice", "water", "aero", "storm" };
            return null;
        }

        // Non-specific meditation: bias the card pool by the pawn's personality traits.
        private static string[] TraitKeywords(Pawn p)
        {
            var traits = p?.story?.traits;
            if (traits == null) return null;
            var set = new System.Collections.Generic.HashSet<string>();
            foreach (var tr in traits.allTraits)
            {
                string d = (tr?.def?.defName ?? "").ToLowerInvariant();
                if (d.Contains("bloodlust") || d.Contains("cannibal") || d.Contains("abrasive"))
                    AddAll(set, "necro", "death", "blood", "hemo", "deadlife");
                if (d.Contains("pyromaniac"))
                    AddAll(set, "flame", "fire", "conflag", "pyro", "ember");
                if (d.Contains("psychopath"))
                    AddAll(set, "void", "umbra", "necro", "shadow", "night");
                if (d.Contains("nightowl") || d.Contains("night_owl"))
                    AddAll(set, "umbra", "void", "shadow", "night", "phase");
                if (d.Contains("kind"))
                    AddAll(set, "empath", "harmon", "wild", "nature", "anima");
                if (d.Contains("ascetic"))
                    AddAll(set, "wild", "nature", "empath", "anima");
                if (d.Contains("greedy") || d.Contains("jealous"))
                    AddAll(set, "harmon", "chrono", "skip", "glitch");
                if (d.Contains("brawler") || d.Contains("tough") || d.Contains("nimble"))
                    AddAll(set, "blader", "guard", "skip", "warden");
                if (d.Contains("transhumanist") || d.Contains("toosmart") || d.Contains("greatmemory"))
                    AddAll(set, "techno", "mechan", "chrono", "static", "arknight");
            }
            return set.Count > 0 ? System.Linq.Enumerable.ToArray(set) : null;
        }

        private static void AddAll(System.Collections.Generic.HashSet<string> set, params string[] vals)
        {
            foreach (var v in vals) set.Add(v);
        }

        private static void ApplyComa(Pawn p, MeditationData med)
        {
            var def = MeditationDefOf.PsychicComa;
            if (def == null || p.health?.hediffSet == null) return;
            if (p.health.hediffSet.GetFirstHediffOfDef(def) != null) return;
            var s = PsycastSynergiesMod.Settings;
            // Minimum 4 hours (10000 ticks), scaling with the pawn's level, how far past the safe window they
            // meditated today, AND accumulated over-meditation saturation (~0.5 day per point) - so a pawn pushed
            // past the safe window day after day faces escalating comas. Cap 8 days.
            int level = p.Psycasts()?.level ?? med.enlightenments;
            float excessHours = Mathf.Max(0f, med.todayTicks / 2500f - (s?.comaSafeHours ?? 6f));
            int dur = Mathf.Clamp(
                10000 + level * 2500 + Mathf.RoundToInt(excessHours * 2500f) + Mathf.RoundToInt(med.medSaturation * 30000f),
                10000, 480000);
            var h = HediffMaker.MakeHediff(def, p);
            p.health.AddHediff(h);
            var disappears = (h as HediffWithComps)?.TryGetComp<HediffComp_Disappears>();
            if (disappears != null) disappears.ticksToDisappear = dur;
            Notify(p, "PS_LetterOverloadLabel".Translate(), "PS_LetterOverloadText".Translate(p.LabelShortCap, dur.ToStringTicksToPeriod()));
        }

        private static void Fx(Pawn p)
        {
            if (p.Spawned && p.Map != null)
                FleckMaker.Static(p.DrawPos, p.Map, FleckDefOf.PsycastAreaEffect, 1.4f);
        }

        private static void Notify(Pawn p, string label, string text)
        {
            if (PawnUtility.ShouldSendNotificationAbout(p))
                Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.PositiveEvent, new LookTargets(p));
        }
    }

    // Integrate external psylink grants into our Awakening. Hediff_Psylink.PostAdd is the universal
    // chokepoint: the PsychicAmplifier hediff is created by EVERY psylink source - the Empire bestowing
    // ceremony, the blinding ritual, anima-tree linking, and the dev "add psylink" tool. The first time a
    // spawned player colonist gains a psylink outside our own meditation flow, route them into the
    // Enlightenment system (awaken + first path pick) instead of leaving them a path-less psycaster.
    [HarmonyPatch(typeof(Hediff_Psylink), "PostAdd")]
    public static class Patch_ExternalPsylinkAwaken
    {
        public static void Postfix(Hediff_Psylink __instance)
        {
            if (MeditationSystem.internalPsylinkChange) return;        // our own awakening created it
            var pawn = __instance?.pawn;
            if (pawn?.Faction == null || !pawn.Faction.IsPlayer) return;
            if (!pawn.Spawned || pawn.RaceProps == null || !pawn.RaceProps.Humanlike) return;   // not at gen time
            if (PsycastSynergiesMod.Settings?.empirePsylinkIntegrate != true) return;
            if (TieringControl.ExternalPsylinkAwakeningDisabled) return;   // a mod owns awakening now
            var med = GameComponent_PsycastSynergies.Instance?.GetMed(pawn, true);
            if (med == null || med.awakened) return;
            MeditationSystem.AwakenExternal(pawn, med);
        }
    }
}

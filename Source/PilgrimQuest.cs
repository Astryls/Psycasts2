#nullable disable
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using HarmonyLib;

namespace PsycastSynergies
{
    [DefOf]
    public static class PSPilgrimDefOf
    {
        public static ThingDef PS_PilgrimAltar;
        public static SitePartDef PS_PilgrimSite;
        public static QuestScriptDef PS_TierIIPilgrimage;
        static PSPilgrimDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(PSPilgrimDefOf)); }
    }

    // The single QuestPart that drives the whole pilgrimage:
    //  - lazy-spawns the altar the first frame the site's map exists (after the caravan arrives),
    //  - ticks while the pilgrim is meditating AT that altar to accumulate progress,
    //  - on threshold: opens the Tier II awakening pick window, ends the quest with Success, despawns site,
    //  - on pilgrim death / tier-mismatch / site lost: ends with Fail.
    public class QuestPart_PilgrimMeditation : QuestPartActivable
    {
        public Pawn pilgrim;
        public Site site;
        public int requiredTicks = 50000;             // ~20h of actual meditation
        public int targetTier = 2;
        public string focusOverride;                  // per-quest override of the global focus-def setting
        public float wavePointsScale = 1f;            // per-quest multiplier on top of the global scale
        public bool wrapInReliquary;                  // build a stone enclosure around the focus on spawn (altar chain)
        public int progress;
        public int dailyProgress;
        public int dailyProgressDay = -1;
        public int nextWaveTick = -1;
        public int wavesFired;
        public bool altarSpawned;
        public bool firedEnd;

        public override IEnumerable<GlobalTargetInfo> QuestLookTargets
        {
            get
            {
                if (pilgrim != null && !pilgrim.Dead) yield return pilgrim;
                if (site != null) yield return site;
            }
        }

        public override string DescriptionPart
        {
            get
            {
                float h = progress / 2500f, req = requiredTicks / 2500f;
                int capTicks = PsycastSynergiesMod.Settings?.pilgrimDailyMaxTicks ?? 20000;
                if (capTicks > 0)
                    return "PS_PilgrimProgressCap".Translate(h.ToString("F1"), req.ToString("F1"), (dailyProgress / 2500f).ToString("F1"), (capTicks / 2500f).ToString("F1"), wavesFired);
                return "PS_PilgrimProgress".Translate(h.ToString("F1"), req.ToString("F1"), wavesFired);
            }
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (firedEnd) return;
            if (pilgrim == null || pilgrim.Dead || EnlightenmentTier.TierOf(pilgrim) != targetTier - 1)
            { FireFail(); return; }
            if (site == null || site.Destroyed)
            { FireFail(); return; }

            // Fallback focus spawn (the SitePartWorker normally already did this at map-gen).
            var siteMap = site.Map;
            if (siteMap != null && !altarSpawned)
            {
                SpawnFocus(siteMap);
                altarSpawned = true;
            }

            // Start the wave timer the first time the site map exists. Decoupled from altarSpawned
            // because the SitePartWorker now sets that flag at map-gen BEFORE this part ever ticks -
            // gating the timer on altarSpawned meant nextWaveTick was never initialized and no wave
            // ever fired. First wave a few hours after the map exists.
            if (siteMap != null && nextWaveTick < 0)
                nextWaveTick = Find.TickManager.TicksGame + 6000;

            // Waves keep firing as long as the site map exists (skipping when no pawn is on it would
            // let the player abandon-and-return to skip threat; this design wants real pressure).
            if (siteMap != null && nextWaveTick > 0 && Find.TickManager.TicksGame >= nextWaveTick)
            {
                FireWave(siteMap);
                int interval = PsycastSynergiesMod.Settings?.pilgrimWaveIntervalTicks ?? 30000;
                nextWaveTick = Find.TickManager.TicksGame + Mathf.Max(2500, interval);
            }

            // Keep the raiders pressing the assault. The AssaultColony duty proved unreliable for a lone
            // target on a site map (no player-owned buildings to march toward), so directly order any idle
            // Ancient Psycaster to attack the pilgrim. AttackMelee handles the charge + melee.
            if (siteMap != null && pilgrim != null && pilgrim.Spawned && Find.TickManager.TicksGame % 120 == 0)
            {
                var foeFac = Find.FactionManager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamedSilentFail("PS_AncientPsycasters"));
                if (foeFac != null)
                    foreach (var foe in siteMap.mapPawns.SpawnedPawnsInFaction(foeFac))
                    {
                        if (foe.Dead || foe.Downed) continue;
                        var jd = foe.CurJobDef;
                        if (jd == JobDefOf.AttackMelee || jd == JobDefOf.AttackStatic) continue;
                        // Don't interrupt a psycast in progress — let them finish the cast, then re-nudge.
                        if (foe.TryGetComp<VEF.Abilities.CompAbilities>()?.currentlyCasting != null) continue;
                        if (foe.CanReach(pilgrim, PathEndMode.Touch, Danger.Deadly))
                            foe.jobs?.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.AttackMelee, pilgrim), JobTag.Misc);
                    }
            }

            // Meditation progress (with daily cap so the pilgrimage spans real elapsed time).
            if (!pilgrim.Spawned || pilgrim.Map?.Parent != site) return;
            if (pilgrim.CurJobDef != JobDefOf.Meditate) return;

            int today = Find.TickManager.TicksGame / 60000;
            if (today != dailyProgressDay) { dailyProgressDay = today; dailyProgress = 0; }
            int cap = PsycastSynergiesMod.Settings?.pilgrimDailyMaxTicks ?? 20000;
            if (cap > 0 && dailyProgress >= cap) return;

            progress++;
            dailyProgress++;
            if (progress >= requiredTicks) FireSuccess();
        }

        public void SpawnFocus(Map siteMap)
        {
            // Per-quest override > mod setting > built-in fallback.
            string defName = !string.IsNullOrEmpty(focusOverride)
                ? focusOverride
                : (PsycastSynergiesMod.Settings?.pilgrimFocusDef ?? "PS_PilgrimAltar");
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName) ?? PSPilgrimDefOf.PS_PilgrimAltar;
            if (siteMap.listerThings.ThingsOfDef(def).Count > 0) return;
            // Trees need a wider clearing; buildings just need a standable cell with no edifice.
            bool isPlant = typeof(Plant).IsAssignableFrom(def.thingClass);
            IntVec3 cell;
            int radius = isPlant ? 18 : 12;
            if (!CellFinder.TryFindRandomCellNear(siteMap.Center, siteMap, radius,
                    c => c.Standable(siteMap) && c.GetEdifice(siteMap) == null
                         && (!isPlant || c.GetPlant(siteMap) == null), out cell))
                cell = siteMap.Center;
            ThingDef stuff = def.MadeFromStuff ? (GenStuff.RandomStuffByCommonalityFor(def) ?? ThingDefOf.WoodLog) : null;
            var focus = ThingMaker.MakeThing(def, stuff);
            try
            {
                // Build the temple first (clears + floors the area), then place the focus on the dais.
                if (wrapInReliquary && !isPlant) BuildTemple(siteMap, cell, def);
                GenSpawn.Spawn(focus, cell, siteMap, Rot4.North);
                if (focus is Plant plant) plant.Growth = 1f;   // mature instantly (anima/giant anima trees)
            }
            catch (System.Exception e) { Log.Warning("[Psycasts²] failed to spawn pilgrim focus '" + defName + "': " + e); }
        }

        // Build an open-air grand temple around the focus: stone-block perimeter walls with a 3-wide
        // south entrance flanked by columns, a tiled floor, a raised dais under the focus, freestanding
        // columns, sculptures behind it, and braziers for light. Scaled up + marble for the T3 grand
        // throne. Left UNROOFED on purpose - site maps don't auto-roof and the throne's Natural
        // meditation focus wants open sky (roofing it would zero the focus strength and fail the job).
        private static void BuildTemple(Map map, IntVec3 center, ThingDef focusDef)
        {
            bool grand = focusDef?.defName == "PS_PilgrimGrandThrone";
            int half = grand ? 7 : 5;                       // 15x15 outer (grand) / 11x11 (T2)
            var wallStuff  = (grand ? DefDatabase<ThingDef>.GetNamedSilentFail("BlocksMarble") : null) ?? ThingDefOf.BlocksGranite;
            var floorDef = DefDatabase<TerrainDef>.GetNamedSilentFail(grand ? "TileMarble" : "TileSandstone");
            var daisDef  = DefDatabase<TerrainDef>.GetNamedSilentFail(grand ? "FlagstoneMarble" : "TileGranite");
            var columnDef  = DefDatabase<ThingDef>.GetNamedSilentFail("Column");
            var brazierDef = DefDatabase<ThingDef>.GetNamedSilentFail("Brazier");
            var sculptDef  = DefDatabase<ThingDef>.GetNamedSilentFail(grand ? "SculptureGrand" : "SculptureLarge");

            // Keep a 1-cell ring around the focus footprint clear (throne + its interaction cell).
            CellRect focusRect = GenAdj.OccupiedRect(center, Rot4.North, focusDef?.size ?? IntVec2.One).ExpandedBy(1);

            // 1. Clear + floor the whole interior.
            for (int dx = -half; dx <= half; dx++)
                for (int dz = -half; dz <= half; dz++)
                {
                    IntVec3 c = center + new IntVec3(dx, 0, dz);
                    if (!c.InBounds(map)) continue;
                    ClearCell(c, map);
                    if (floorDef != null) map.terrainGrid.SetTerrain(c, floorDef);
                }

            // 2. Raised dais (contrasting floor) under and around the focus.
            int daisHalf = grand ? 2 : 1;
            if (daisDef != null)
                for (int dx = -daisHalf; dx <= daisHalf; dx++)
                    for (int dz = -daisHalf; dz <= daisHalf; dz++)
                    {
                        IntVec3 c = center + new IntVec3(dx, 0, dz);
                        if (c.InBounds(map)) map.terrainGrid.SetTerrain(c, daisDef);
                    }

            // 3. Perimeter walls, with a 3-wide entrance gap centered on the south face.
            for (int dx = -half; dx <= half; dx++)
                for (int dz = -half; dz <= half; dz++)
                {
                    bool edge = (dx == -half) || (dx == half) || (dz == -half) || (dz == half);
                    if (!edge) continue;
                    if (dz == -half && dx >= -1 && dx <= 1) continue;   // 3-wide open archway
                    IntVec3 c = center + new IntVec3(dx, 0, dz);
                    if (!c.InBounds(map)) continue;
                    ClearCell(c, map);
                    GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, wallStuff), c, map);
                }

            // 4. Columns: inner corners + entrance flanks (+ wall midpoints for the grand temple).
            if (columnDef != null)
            {
                int ci = half - 2;
                var cols = new List<IntVec3>
                {
                    center + new IntVec3(-ci, 0, -ci), center + new IntVec3(ci, 0, -ci),
                    center + new IntVec3(-ci, 0,  ci), center + new IntVec3(ci, 0,  ci),
                    center + new IntVec3(-2, 0, -half + 1), center + new IntVec3(2, 0, -half + 1), // flank the archway
                };
                if (grand)
                {
                    cols.Add(center + new IntVec3(-ci, 0, 0)); cols.Add(center + new IntVec3(ci, 0, 0));
                    cols.Add(center + new IntVec3(0, 0, ci));
                }
                foreach (var c in cols) TrySpawnDecor(map, c, columnDef, wallStuff, focusRect);
            }

            // 5. Sculptures flanking the focus (behind it, to the north).
            if (sculptDef != null)
            {
                int sx = grand ? 3 : 2;
                int sz = grand ? 3 : 2;
                TrySpawnDecor(map, center + new IntVec3(-sx, 0, sz), sculptDef, wallStuff, focusRect);
                TrySpawnDecor(map, center + new IntVec3( sx, 0, sz), sculptDef, wallStuff, focusRect);
            }

            // 6. Braziers for light, just inside the corners (refuel so they're lit on arrival).
            if (brazierDef != null)
            {
                int bi = half - 1;
                var bcells = new List<IntVec3>
                {
                    center + new IntVec3(-bi, 0, -bi), center + new IntVec3(bi, 0, -bi),
                    center + new IntVec3(-bi, 0,  bi), center + new IntVec3(bi, 0,  bi),
                };
                foreach (var c in bcells)
                {
                    var br = TrySpawnDecor(map, c, brazierDef, null, focusRect);
                    br?.TryGetComp<CompRefuelable>()?.Refuel(999f);
                }
            }
        }

        // Spawn a decoration if the cell is in bounds, free of edifices, and outside the focus footprint.
        // Picks a valid stuff (the supplied one if the def allows stuff, else a commonality roll).
        private static Thing TrySpawnDecor(Map map, IntVec3 c, ThingDef def, ThingDef preferredStuff, CellRect avoid)
        {
            if (def == null || !c.InBounds(map) || avoid.Contains(c)) return null;
            if (c.GetEdifice(map) != null) return null;
            ClearCell(c, map);
            ThingDef stuff = null;
            if (def.MadeFromStuff)
                stuff = (preferredStuff != null && preferredStuff.IsStuff && def.stuffCategories != null
                         && def.stuffCategories.Exists(cat => preferredStuff.stuffProps.categories.Contains(cat)))
                        ? preferredStuff
                        : (GenStuff.RandomStuffByCommonalityFor(def) ?? ThingDefOf.Steel);
            var t = ThingMaker.MakeThing(def, stuff);
            GenSpawn.Spawn(t, c, map, Rot4.North);
            return t;
        }

        private static void ClearCell(IntVec3 c, Map map)
        {
            var list = c.GetThingList(map);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var t = list[i];
                if (t is Pawn) continue;
                if (t.def.destroyable && t.def.useHitPoints) t.Destroy();
            }
        }

        private static Faction EnsureFaction()
        {
            var facDef = DefDatabase<FactionDef>.GetNamedSilentFail("PS_AncientPsycasters");
            if (facDef == null) return null;
            var fac = Find.FactionManager.FirstFactionOfDef(facDef);
            if (fac == null)
            {
                try
                {
                    fac = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(facDef, default(IdeoGenerationParms), true));
                    Find.FactionManager.Add(fac);
                }
                catch (System.Exception e) { Log.Warning("[Psycasts²] failed to spawn Ancient Psycasters faction: " + e); }
            }
            return fac;
        }

        private void FireWave(Map map)
        {
            var fac = EnsureFaction();
            if (fac == null) return;
            try
            {
                float scale = (PsycastSynergiesMod.Settings?.pilgrimWavePointsScale ?? 1f)
                              * (wavePointsScale > 0f ? wavePointsScale : 1f);
                float points = Mathf.Max(600f, (600f + wavesFired * 250f) * scale);

                // Generate the Ancient Psycaster combat group directly from OUR faction, then drop them at
                // a map edge under an explicit assault lord. Going through IncidentWorker_RaidEnemy on a
                // quest/site map was unreliable (single idle pawn); this guarantees several attackers.
                var gp = new PawnGroupMakerParms
                {
                    groupKind = PawnGroupKindDefOf.Combat,
                    tile = map.Tile,
                    faction = fac,
                    points = points,
                    raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                };
                var pawns = PawnGroupMakerUtility.GeneratePawns(gp).ToList();

                // Guarantee a minimum wave size. The points-based group is small early on (and the cost
                // curve trades count for quality), so top up with extra Basic psycasters until we hit 8.
                const int minPawns = 8;
                var basicKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("PS_AncientPsycaster_Basic");
                int guard = 0;
                while (pawns.Count < minPawns && basicKind != null && guard++ < 32)
                    pawns.Add(PawnGenerator.GeneratePawn(new PawnGenerationRequest(basicKind, fac, forceGenerateNewPawn: true)));
                if (pawns.Count == 0) return;

                // Spawn within AssaultColony's target-acquire radius (65) of the pilgrim so the raiders
                // ENGAGE immediately. On a site map there are no player-owned buildings to march toward,
                // and a far map-edge spawn leaves the pilgrim out of acquire range, so the assault AI just
                // idles until he wanders close. We spawn them ~25-50 tiles out (a perimeter charge),
                // reachable, and also seed each pawn's enemyTarget = pilgrim so they actively hunt him.
                IntVec3 reachTarget = (pilgrim != null && pilgrim.Spawned) ? pilgrim.Position : map.Center;
                IntVec3 entry;
                if (!CellFinder.TryFindRandomCellNear(reachTarget, map, 50,
                        c => c.Standable(map) && !c.Fogged(map) && (c - reachTarget).LengthHorizontal > 24f
                             && map.reachability.CanReach(c, reachTarget, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors)),
                        out entry))
                    entry = CellFinder.RandomEdgeCell(map);
                foreach (var p in pawns)
                {
                    IntVec3 loc = CellFinder.RandomClosewalkCellNear(entry, map, 8);
                    GenSpawn.Spawn(p, loc, map, Rot4.Random);
                    // Full psyfocus so they can open with psycasts the moment they have a line of sight.
                    // (Safe now that the per-tick AttackMelee nudge guarantees the charge — it only fires
                    // for IDLE raiders and skips anyone mid-cast, so it won't stall them or interrupt a
                    // psycast. VEF only auto-casts when CanHitTarget passes, so no-LoS never stalls them.)
                    p.psychicEntropy?.OffsetPsyfocusDirectly(1f);
                }
                LordMaker.MakeNewLord(fac, new LordJob_AssaultColony(fac, canKidnap: false, canTimeoutOrFlee: false, canSteal: false), map, pawns);
                wavesFired++;
                Find.LetterStack.ReceiveLetter("PS_LetterWaveLabel".Translate(),
                    "PS_LetterWaveText".Translate(pawns.Count),
                    LetterDefOf.ThreatBig, new LookTargets(pawns[0]), fac);
                if (Prefs.DevMode)
                {
                    var p0 = pawns[0];
                    bool reach = pilgrim != null && p0.CanReach(pilgrim, PathEndMode.Touch, Danger.Deadly);
                    Log.Message("[Psycasts\u00b2] wave " + wavesFired + ": " + pawns.Count + " pawns"
                        + ", lord=" + (p0.GetLord() != null)
                        + ", duty=" + (p0.mindState?.duty?.def?.defName ?? "null")
                        + ", hostileToPilgrim=" + (pilgrim != null && p0.HostileTo(pilgrim))
                        + ", pilgrimHostileToThem=" + (pilgrim != null && pilgrim.HostileTo(p0))
                        + ", facHostile=" + fac.HostileTo(Faction.OfPlayer)
                        + ", downed=" + p0.Downed
                        + ", violentDisabled=" + p0.WorkTagIsDisabled(WorkTags.Violent)
                        + ", dist=" + (pilgrim != null ? (p0.Position - pilgrim.Position).LengthHorizontal.ToString("F0") : "?")
                        + ", reach=" + reach);
                }
            }
            catch (System.Exception e) { Log.Warning("[Psycasts²] pilgrim wave fire failed: " + e); }
        }

        private void FireSuccess()
        {
            if (firedEnd) return; firedEnd = true;
            try { MeditationSystem.OpenPick(pilgrim, targetTier); }
            catch (System.Exception e) { Log.Warning("[Psycasts²] pilgrim awakening open failed: " + e); }
            // Do NOT destroy the site here: tearing down the map while the pilgrim is standing on it
            // loses them. Leave the map so the player can reform a caravan and travel home - the site
            // auto-removes once the caravan leaves (Site.ShouldRemoveMapNow). Refresh the timeout so a
            // stale one can't yank the map before they get a chance to reform.
            if (site != null && !site.Destroyed && site.HasMap)
            {
                site.GetComponent<TimeoutComp>()?.StartTimeout(15 * 60000);
                Messages.Message("PS_MsgPilgrimReform".Translate(pilgrim?.LabelShortCap ?? "PS_ThePilgrim".Translate()),
                    pilgrim, MessageTypeDefOf.PositiveEvent, false);
            }
            else if (site != null && !site.Destroyed) site.Destroy();   // no map loaded -> safe to clean up now
            quest.End(QuestEndOutcome.Success, sendStandardLetter: true);
        }

        private void FireFail()
        {
            if (firedEnd) return; firedEnd = true;
            // Spare the site if the pilgrim is still standing on its map (tier-mismatch edge case), so a
            // fail can't strand them either - let them reform a caravan out.
            bool pilgrimOnSite = site != null && site.HasMap && pilgrim != null && !pilgrim.Dead && pilgrim.MapHeld == site.Map;
            if (site != null && !site.Destroyed && !pilgrimOnSite) site.Destroy();
            quest.End(QuestEndOutcome.Fail, sendStandardLetter: true);
        }


        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref pilgrim, "pilgrim");
            Scribe_References.Look(ref site, "site");
            Scribe_Values.Look(ref requiredTicks, "requiredTicks", 50000);
            Scribe_Values.Look(ref targetTier, "targetTier", 2);
            Scribe_Values.Look(ref focusOverride, "focusOverride");
            Scribe_Values.Look(ref wavePointsScale, "wavePointsScale", 1f);
            Scribe_Values.Look(ref wrapInReliquary, "wrapInReliquary", false);
            Scribe_Values.Look(ref progress, "progress");
            Scribe_Values.Look(ref dailyProgress, "dailyProgress");
            Scribe_Values.Look(ref dailyProgressDay, "dailyProgressDay", -1);
            Scribe_Values.Look(ref nextWaveTick, "nextWaveTick", -1);
            Scribe_Values.Look(ref wavesFired, "wavesFired");
            Scribe_Values.Look(ref altarSpawned, "altarSpawned");
            Scribe_Values.Look(ref firedEnd, "firedEnd");
        }
    }

    // Routes a pawn to the combat (altar) or pacifist (anima) pilgrimage. A pawn's effective style is its
    // chosen pilgrimStyle (0 Either / 1 Combat / 2 Pacifist), EXCEPT a colonist incapable of Violence is
    // always forced to Pacifist (they'd die in the altar trial). The storyteller only offers a chain when
    // an eligible-styled pawn exists, and the quest picks that pawn (not just the first awakened one).
    public static class PilgrimRouting
    {
        public static int EffectiveStyle(Pawn p)
        {
            if (p == null) return 0;
            if (p.WorkTagIsDisabled(WorkTags.Violent)) return 2;   // pacifists -> anima only
            return GameComponent_PsycastSynergies.Instance?.GetMed(p, false)?.pilgrimStyle ?? 0;
        }
        public static bool AllowsCombat(Pawn p) { int s = EffectiveStyle(p); return s == 0 || s == 1; }
        public static bool AllowsAnima(Pawn p)  { int s = EffectiveStyle(p); return s == 0 || s == 2; }

        // First awakened colonist at `tier` whose style allows this pilgrimage kind.
        public static Pawn FindPilgrim(int tier, bool combat)
        {
            foreach (var p in PawnsFinder.AllMaps_FreeColonistsSpawned)
                if (EnlightenmentTier.TierOf(p) == tier && (combat ? AllowsCombat(p) : AllowsAnima(p)))
                    return p;
            return null;
        }

        // True when this map belongs to one of our pilgrimage sites (altar OR anima). Used to exempt the
        // pilgrim's meditation from coma risk and to suppress random storyteller threats on the site map.
        public static bool IsPilgrimageMap(Map map)
        {
            return map?.Parent is RimWorld.Planet.Site site && site.parts != null
                && site.parts.Any(sp => sp.def == PSPilgrimDefOf.PS_PilgrimSite);
        }
    }

    // Root QuestNode for PS_TierIIPilgrimage: picks an awakened (Tier I) pilgrim, generates a site on the
    // world map, wires the tracker. Future T3 quest can reuse all of this with a different targetTier.
    public class QuestNode_PilgrimRoot : QuestNode
    {
        public int targetTier = 2;
        public string focusOverride;                  // overrides the global pilgrimFocusDef setting for this quest
        public float wavePointsScale = 1f;            // per-quest wave threat multiplier (stacks with the setting)
        public int requiredTicksOverride;             // 0 = use setting
        public bool wrapInReliquary;                  // if true, the focus spawns inside a stone reliquary

        protected override bool TestRunInt(Slate slate)
        {
            if (TieringControl.PilgrimagesDisabled) return false;   // a mod owns tier progression now
            int neededTier = targetTier - 1;
            return PilgrimRouting.FindPilgrim(neededTier, true) != null
                && TileFinder.TryFindNewSiteTile(out _);
        }

        protected override void RunInt()
        {
            var quest = QuestGen.quest;
            var slate = QuestGen.slate;
            int neededTier = targetTier - 1;

            var pilgrim = PilgrimRouting.FindPilgrim(neededTier, true);
            if (pilgrim == null) return;
            if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile)) return;

            Site site = SiteMaker.MakeSite(PSPilgrimDefOf.PS_PilgrimSite, tile, null);
            site.GetComponent<TimeoutComp>()?.StartTimeout(30 * 60000);   // 30 days to complete
            Find.WorldObjects.Add(site);

            slate.Set("pilgrim", pilgrim);
            slate.Set("site", site);
            slate.Set("map", pilgrim.Map);

            int reqTicks = requiredTicksOverride > 0
                ? requiredTicksOverride
                : (PsycastSynergiesMod.Settings?.pilgrimMeditationTicks ?? 50000);
            if (reqTicks < 2500) reqTicks = 2500;

            quest.AddPart(new QuestPart_PilgrimMeditation
            {
                pilgrim = pilgrim,
                site = site,
                requiredTicks = reqTicks,
                targetTier = targetTier,
                focusOverride = focusOverride,
                wavePointsScale = wavePointsScale,
                wrapInReliquary = wrapInReliquary,
                inSignalEnable = slate.Get<string>("inSignal"),
            });
        }

        static Pawn FindEligiblePilgrim(int tier)
        {
            foreach (var p in PawnsFinder.AllMaps_FreeColonistsSpawned)
                if (EnlightenmentTier.TierOf(p) == tier) return p;
            return null;
        }
    }

    // =====================================================================================
    // ANIMA CHAIN (pacifist): pilgrim travels to MULTIPLE sites, each spawning an anima tree,
    // and meditates the required time at EACH. No waves. Longer total commitment. The LAST
    // site can optionally use a different focus (the giant anima tree for T3).
    // =====================================================================================
    public class QuestPart_PilgrimJourney : QuestPartActivable
    {
        public Pawn pilgrim;
        public List<Site> sites = new List<Site>();
        public List<int> siteProgress = new List<int>();
        public List<bool> siteFocusSpawned = new List<bool>();
        public int requiredTicksPerSite = 50000;
        public int targetTier = 2;
        public string focusOverride;                  // default focus def for every site
        public string lastSiteFocusOverride;          // override for the final site only (giant tree for T3)
        public int dailyProgress;
        public int dailyProgressDay = -1;
        public bool firedEnd;

        public override IEnumerable<GlobalTargetInfo> QuestLookTargets
        {
            get
            {
                if (pilgrim != null && !pilgrim.Dead) yield return pilgrim;
                if (sites != null) for (int i = 0; i < sites.Count; i++)
                    if (sites[i] != null && !sites[i].Destroyed) yield return sites[i];
            }
        }

        public override string DescriptionPart
        {
            get
            {
                if (sites == null || sites.Count == 0) return null;
                int completed = 0;
                for (int i = 0; i < sites.Count && i < siteProgress.Count; i++)
                    if (siteProgress[i] >= requiredTicksPerSite) completed++;
                float reqH = requiredTicksPerSite / 2500f;
                int capTicks = PsycastSynergiesMod.Settings?.pilgrimDailyMaxTicks ?? 20000;
                string today = capTicks > 0 ? "PS_AnimaToday".Translate((dailyProgress / 2500f).ToString("F1"), (capTicks / 2500f).ToString("F1")).ToString() : "";
                string curSite = "";
                if (pilgrim?.Map?.Parent is Site cs)
                {
                    int idx = sites.IndexOf(cs);
                    if (idx >= 0 && idx < siteProgress.Count)
                        curSite = "PS_AnimaHere".Translate((siteProgress[idx] / 2500f).ToString("F1"), reqH.ToString("F1")).ToString();
                }
                return "PS_AnimaSites".Translate(completed, sites.Count) + curSite + today;
            }
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (firedEnd) return;
            if (pilgrim == null || pilgrim.Dead || EnlightenmentTier.TierOf(pilgrim) != targetTier - 1)
            { FireFail(); return; }
            if (sites == null || sites.Count == 0)
            { FireFail(); return; }

            while (siteProgress.Count < sites.Count) siteProgress.Add(0);
            while (siteFocusSpawned.Count < sites.Count) siteFocusSpawned.Add(false);

            for (int i = 0; i < sites.Count; i++)
            {
                var s = sites[i];
                if (s == null || s.Destroyed || s.Map == null || siteFocusSpawned[i]) continue;
                SpawnFocusAt(i);
                siteFocusSpawned[i] = true;
            }

            if (!pilgrim.Spawned) return;
            var pSite = pilgrim.Map?.Parent as Site;
            if (pSite == null) return;
            int siteIdx = sites.IndexOf(pSite);
            if (siteIdx < 0) return;
            if (siteProgress[siteIdx] >= requiredTicksPerSite) return;
            if (pilgrim.CurJobDef != JobDefOf.Meditate) return;

            int today = Find.TickManager.TicksGame / 60000;
            if (today != dailyProgressDay) { dailyProgressDay = today; dailyProgress = 0; }
            int cap = PsycastSynergiesMod.Settings?.pilgrimDailyMaxTicks ?? 20000;
            if (cap > 0 && dailyProgress >= cap) return;

            siteProgress[siteIdx]++;
            dailyProgress++;

            bool allDone = true;
            for (int i = 0; i < sites.Count; i++)
                if (siteProgress[i] < requiredTicksPerSite) { allDone = false; break; }
            if (allDone) FireSuccess();
        }

        public void SpawnFocusAt(int idx)
        {
            var siteMap = sites[idx]?.Map;
            if (siteMap == null) return;
            bool isLast = idx == sites.Count - 1;
            string defName = (isLast && !string.IsNullOrEmpty(lastSiteFocusOverride))
                ? lastSiteFocusOverride
                : (!string.IsNullOrEmpty(focusOverride) ? focusOverride : "Plant_TreeAnima");
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null) return;
            if (siteMap.listerThings.ThingsOfDef(def).Count > 0) return;
            bool isPlant = typeof(Plant).IsAssignableFrom(def.thingClass);
            int radius = isPlant ? 18 : 12;
            bool Valid(IntVec3 c)
            {
                if (!c.Standable(siteMap) || c.GetEdifice(siteMap) != null) return false;
                var terr = siteMap.terrainGrid.TerrainAt(c);
                if (terr.IsWater) return false;                         // never on water (Standable allows shallow water)
                if (isPlant)
                {
                    if (c.GetPlant(siteMap) != null) return false;
                    if (terr.fertility <= 0f) return false;              // a tree needs growable soil, not rock/sand
                }
                return true;
            }
            // Search near the center first, then anywhere on the map, before giving up on the center cell.
            if (!CellFinder.TryFindRandomCellNear(siteMap.Center, siteMap, radius, Valid, out IntVec3 cell, 600)
                && !CellFinderLoose.TryGetRandomCellWith(Valid, siteMap, 1000, out cell))
                cell = siteMap.Center;
            ThingDef stuff = def.MadeFromStuff ? (GenStuff.RandomStuffByCommonalityFor(def) ?? ThingDefOf.WoodLog) : null;
            var focus = ThingMaker.MakeThing(def, stuff);
            try
            {
                GenSpawn.Spawn(focus, cell, siteMap, Rot4.North);
                if (focus is Plant plant) plant.Growth = 1f;
            }
            catch (System.Exception e) { Log.Warning("[Psycasts²] failed to spawn anima focus '" + defName + "': " + e); }
        }

        private void FireSuccess()
        {
            if (firedEnd) return; firedEnd = true;
            try { MeditationSystem.OpenPick(pilgrim, targetTier); }
            catch (System.Exception e) { Log.Warning("[Psycasts²] anima awakening open failed: " + e); }
            // Keep the site the pilgrim is standing on so the player can reform a caravan home; the
            // other (already-vacated) sites are cleaned up. The kept site auto-removes when they leave.
            Site keep = pilgrim?.MapHeld?.Parent as Site;
            DestroyAllSitesExcept(keep);
            if (keep != null && !keep.Destroyed && keep.HasMap)
            {
                keep.GetComponent<TimeoutComp>()?.StartTimeout(15 * 60000);
                Messages.Message("PS_MsgPilgrimReform".Translate(pilgrim?.LabelShortCap ?? "PS_ThePilgrim".Translate()),
                    pilgrim, MessageTypeDefOf.PositiveEvent, false);
            }
            quest.End(QuestEndOutcome.Success, sendStandardLetter: true);
        }

        private void FireFail()
        {
            if (firedEnd) return; firedEnd = true;
            // Spare the site the pilgrim is standing on (if alive) so a fail can't strand them.
            Site keep = (pilgrim != null && !pilgrim.Dead) ? pilgrim.MapHeld?.Parent as Site : null;
            DestroyAllSitesExcept(keep);
            quest.End(QuestEndOutcome.Fail, sendStandardLetter: true);
        }

        private void DestroyAllSitesExcept(Site keep)
        {
            if (sites == null) return;
            foreach (var s in sites) if (s != null && !s.Destroyed && s != keep) s.Destroy();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref pilgrim, "pilgrim");
            Scribe_Collections.Look(ref sites, "sites", LookMode.Reference);
            Scribe_Collections.Look(ref siteProgress, "siteProgress", LookMode.Value);
            Scribe_Collections.Look(ref siteFocusSpawned, "siteFocusSpawned", LookMode.Value);
            Scribe_Values.Look(ref requiredTicksPerSite, "requiredTicksPerSite", 50000);
            Scribe_Values.Look(ref targetTier, "targetTier", 2);
            Scribe_Values.Look(ref focusOverride, "focusOverride");
            Scribe_Values.Look(ref lastSiteFocusOverride, "lastSiteFocusOverride");
            Scribe_Values.Look(ref dailyProgress, "dailyProgress");
            Scribe_Values.Look(ref dailyProgressDay, "dailyProgressDay", -1);
            Scribe_Values.Look(ref firedEnd, "firedEnd");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (sites == null) sites = new List<Site>();
                if (siteProgress == null) siteProgress = new List<int>();
                if (siteFocusSpawned == null) siteFocusSpawned = new List<bool>();
            }
        }
    }

    public class QuestNode_AnimaPilgrimRoot : QuestNode
    {
        public int targetTier = 2;
        public int siteCount = 3;
        public string focusOverride = "Plant_TreeAnima";
        public string lastSiteFocusOverride;

        protected override bool TestRunInt(Slate slate)
        {
            if (TieringControl.PilgrimagesDisabled) return false;   // a mod owns tier progression now
            return PilgrimRouting.FindPilgrim(targetTier - 1, false) != null
                && TileFinder.TryFindNewSiteTile(out _);
        }

        protected override void RunInt()
        {
            var quest = QuestGen.quest;
            var slate = QuestGen.slate;
            var pilgrim = PilgrimRouting.FindPilgrim(targetTier - 1, false);
            if (pilgrim == null) return;

            int count = Mathf.Max(1, siteCount);
            var sites = new List<Site>();
            for (int i = 0; i < count; i++)
            {
                if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile, 7, 60)) break;
                var s = SiteMaker.MakeSite(PSPilgrimDefOf.PS_PilgrimSite, tile, null);
                s.GetComponent<TimeoutComp>()?.StartTimeout(60 * 60000);   // 60-day timeout (long journey)
                Find.WorldObjects.Add(s);
                sites.Add(s);
            }
            if (sites.Count == 0) return;

            int reqTicks = PsycastSynergiesMod.Settings?.animaPilgrimTicksPerSite ?? 50000;
            if (reqTicks < 2500) reqTicks = 2500;

            slate.Set("pilgrim", pilgrim);
            slate.Set("map", pilgrim.Map);

            quest.AddPart(new QuestPart_PilgrimJourney
            {
                pilgrim = pilgrim,
                sites = sites,
                siteProgress = Enumerable.Repeat(0, sites.Count).ToList(),
                siteFocusSpawned = Enumerable.Repeat(false, sites.Count).ToList(),
                requiredTicksPerSite = reqTicks,
                targetTier = targetTier,
                focusOverride = focusOverride,
                lastSiteFocusOverride = lastSiteFocusOverride,
                inSignalEnable = slate.Get<string>("inSignal"),
            });
        }

        static Pawn FindEligiblePilgrim(int tier)
        {
            foreach (var p in PawnsFinder.AllMaps_FreeColonistsSpawned)
                if (EnlightenmentTier.TierOf(p) == tier) return p;
            return null;
        }
    }

    // Spawns the pilgrimage focus (altar throne + reliquary, or anima tree) during SITE MAP GENERATION,
    // so it appears the moment the player enters the site - independent of whether the quest has been
    // accepted yet or whether QuestPart ticking has started. Site.PostMapGenerate calls
    // part.def.Worker.PostMapGenerate, which runs after all GenSteps. The matching quest part supplies the
    // focus def + reliquary flag. QuestPartTick keeps an idempotent fallback spawn for edge cases.
    public class SitePartWorker_PilgrimAltar : SitePartWorker
    {
        public override void PostMapGenerate(Map map)
        {
            base.PostMapGenerate(map);
            var site = map?.Parent as Site;
            if (site == null || Find.QuestManager == null) return;
            foreach (var q in Find.QuestManager.QuestsListForReading)
            {
                var parts = q.PartsListForReading;
                for (int i = 0; i < parts.Count; i++)
                {
                    if (parts[i] is QuestPart_PilgrimMeditation pm && pm.site == site)
                    {
                        pm.SpawnFocus(map);
                        pm.altarSpawned = true;
                        return;
                    }
                    if (parts[i] is QuestPart_PilgrimJourney pj && pj.sites != null)
                    {
                        int idx = pj.sites.IndexOf(site);
                        if (idx >= 0)
                        {
                            pj.SpawnFocusAt(idx);
                            while (pj.siteFocusSpawned.Count <= idx) pj.siteFocusSpawned.Add(false);
                            pj.siteFocusSpawned[idx] = true;
                            return;
                        }
                    }
                }
            }
        }
    }

    // "Meditate your ass off": a per-pawn forced-meditation toggle. While active, Patch_ForcedMeditation
    // makes the pawn's current time assignment read as Meditate, so vanilla's JobGiver_Meditate keeps
    // re-issuing a meditation job AND JobDriver_Meditate never ends it at full psyfocus or wanders off.
    // Drafting suspends it (the pawn fights, then resumes when undrafted). Lets the player one-click the
    // pilgrim into the continuous meditation the quest needs.
    public static class ForcedMeditation
    {
        public static readonly HashSet<Pawn> Active = new HashSet<Pawn>();

        public static bool On(Pawn p) => p != null && Active.Contains(p);

        public static void Start(Pawn p)
        {
            if (p == null) return;
            Active.Add(p);
            var med = GameComponent_PsycastSynergies.Instance?.GetMed(p, true);
            if (med != null) med.forcedMeditation = true;
        }

        public static void Stop(Pawn p)
        {
            if (p == null) return;
            Active.Remove(p);
            var med = GameComponent_PsycastSynergies.Instance?.GetMed(p, false);
            if (med != null) med.forcedMeditation = false;
            if (p.CurJobDef == JobDefOf.Meditate) p.jobs?.EndCurrentJob(JobCondition.InterruptForced);
        }

        // Rebuild the runtime set from saved per-pawn flags on game load.
        public static void Rebuild(GameComponent_PsycastSynergies gc)
        {
            Active.Clear();
            if (gc == null) return;
            foreach (var kv in gc.MedDataPairs)
                if (kv.Key != null && kv.Value != null && kv.Value.forcedMeditation) Active.Add(kv.Key);
        }
    }

    // Force the current time assignment to Meditate for pawns under forced meditation (unless drafted).
    // GetTimeAssignment() -> timetable.CurrentAssignment, which JobDriver_Meditate reads to decide whether
    // to keep meditating past full psyfocus; forcing it = continuous meditation.
    [HarmonyPatch(typeof(Pawn_TimetableTracker), "CurrentAssignment", MethodType.Getter)]
    public static class Patch_ForcedMeditationAssignment
    {
        private static readonly AccessTools.FieldRef<Pawn_TimetableTracker, Pawn> PawnRef =
            AccessTools.FieldRefAccess<Pawn_TimetableTracker, Pawn>("pawn");

        public static void Postfix(Pawn_TimetableTracker __instance, ref TimeAssignmentDef __result)
        {
            if (ForcedMeditation.Active.Count == 0 || __result == TimeAssignmentDefOf.Meditate) return;
            var pawn = PawnRef(__instance);
            if (pawn == null || pawn.Drafted || !ForcedMeditation.Active.Contains(pawn)) return;
            __result = TimeAssignmentDefOf.Meditate;
        }
    }

    // Right-click "Meditate here" option on the pilgrim thrones. Vanilla has NO manual meditate order
    // (it's schedule/recreation-driven via JobGiver_Meditate), so for the quest UX the player needs an
    // explicit way to send the pilgrim to the focus. Issues JobDefOf.Meditate with the throne as
    // TargetC (focus) and an adjacent standable cell as TargetA - exactly what QuestPart_PilgrimMeditation
    // watches for (pilgrim.CurJobDef == JobDefOf.Meditate on the site map).
    public class CompProperties_PilgrimMeditation : CompProperties
    {
        public CompProperties_PilgrimMeditation() { compClass = typeof(CompPilgrimMeditation); }
    }

    public class CompPilgrimMeditation : ThingComp
    {
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            if (selPawn == null || !selPawn.HasPsylink || parent.Map == null) yield break;
            // Already meditating continuously? Offer to stop.
            if (ForcedMeditation.On(selPawn))
            {
                yield return new FloatMenuOption("PS_StopMeditatingAt".Translate(selPawn.LabelShort), () => ForcedMeditation.Stop(selPawn));
                yield break;
            }
            string label = "PS_MeditateAt".Translate(parent.LabelShort);
            if (!selPawn.CanReach(parent, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption(label + "PS_CannotReachSuffix".Translate(), null);
                yield break;
            }
            IntVec3 spot = FindMeditationCell(selPawn);
            if (!spot.IsValid)
            {
                yield return new FloatMenuOption(label + "PS_NoFreeSpotSuffix".Translate(), null);
                yield break;
            }
            yield return new FloatMenuOption(label, () =>
            {
                // Forced (continuous) meditation: the time-assignment patch keeps vanilla re-issuing
                // meditation at the best focus (this throne) and stops it ending at full psyfocus.
                ForcedMeditation.Start(selPawn);
                Job job = JobMaker.MakeJob(JobDefOf.Meditate, spot, null, parent);
                job.ignoreJoyTimeAssignment = true;
                selPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            });
        }

        // Prefer the throne's interaction cell (sits right in front); fall back to any standable cell
        // within a few tiles so it still works if the interaction cell is blocked by the reliquary.
        private IntVec3 FindMeditationCell(Pawn pawn)
        {
            Map map = parent.Map;
            if (parent.def.hasInteractionCell)
            {
                IntVec3 ic = parent.InteractionCell;
                if (ic.InBounds(map) && ic.Standable(map) && !ic.IsForbidden(pawn)
                    && pawn.CanReserveAndReach(ic, PathEndMode.OnCell, Danger.Deadly))
                    return ic;
            }
            foreach (IntVec3 c in GenRadial.RadialCellsAround(parent.Position, 3.5f, false))
            {
                if (c.InBounds(map) && c.Standable(map) && !c.IsForbidden(pawn)
                    && pawn.CanReserveAndReach(c, PathEndMode.OnCell, Danger.Deadly))
                    return c;
            }
            return IntVec3.Invalid;
        }
    }
}

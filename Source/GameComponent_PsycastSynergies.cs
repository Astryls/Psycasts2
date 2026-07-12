#nullable disable
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using VEF.Abilities;
using VanillaPsycastsExpanded;
using AbilityDef = VEF.Abilities.AbilityDef;

namespace PsycastSynergies
{
    // Per-pawn specialization state: owned specs, the earned point pool, accrued XP, and the
    // chosen targets for the mod-aware picks (Mastery / Discipline / Attunement).
    public class SpecData : IExposable
    {
        public HashSet<string> owned = new HashSet<string>();
        public int points;
        public float xp;
        public int levelProgress;          // accumulated psycaster levels toward the next point
        public int lastCastTick = -99999;
        public float lastEntropy, lastFocus;   // transient: last cast's paid cost (for Tempest refund)
        public AbilityDef masteryDef;
        public PsycasterPathDef disciplinePath;
        public DamageDef attuneDamage;
        public bool aurasDisabled;          // per-pawn toggle for the cosmetic ascension aura

        private List<string> _ownedScribe;

        public bool Owns(string id) => owned.Contains(id);

        public void ExposeData()
        {
            Scribe_Values.Look(ref points, "points", 0);
            Scribe_Values.Look(ref xp, "xp", 0f);
            Scribe_Values.Look(ref levelProgress, "levelProgress", 0);
            Scribe_Values.Look(ref lastCastTick, "lastCastTick", -99999);
            Scribe_Defs.Look(ref masteryDef, "masteryDef");
            Scribe_Defs.Look(ref disciplinePath, "disciplinePath");
            Scribe_Defs.Look(ref attuneDamage, "attuneDamage");
            Scribe_Values.Look(ref aurasDisabled, "aurasDisabled", false);
            if (Scribe.mode == LoadSaveMode.Saving) _ownedScribe = owned.ToList();
            Scribe_Collections.Look(ref _ownedScribe, "owned", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                owned = _ownedScribe != null ? new HashSet<string>(_ownedScribe) : new HashSet<string>();
        }
    }
    // One serialized entry per (pawn, ability) that has at least one invested level.
    public class SkillEntry : IExposable
    {
        public Pawn pawn;
        public AbilityDef def;
        public int level;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref level, "level", 0);
        }
    }

    // Stores per-pawn, per-ability skill levels. Auto-instantiated by the engine for
    // every GameComponent subclass; lives in the save graph.
    public class GameComponent_PsycastSynergies : GameComponent
    {
        // Runtime store: pawn -> (abilityDef -> level).
        private Dictionary<Pawn, Dictionary<AbilityDef, int>> levels =
            new Dictionary<Pawn, Dictionary<AbilityDef, int>>();

        // Scribe buffer (flattened).
        private List<SkillEntry> entries;
        // Working lists for the Reference-keyed dictionary scribe below. Pawn keys are saved as cross-references
        // and resolve in ResolvingCrossRefs, so these MUST be persistent instance fields AND the working-list
        // Scribe overload MUST be used - the plain overload throws "provide working lists for the keys and
        // values" on load and silently wipes all spec/meditation data on every save+reload.
        private List<Pawn> specKeysWork; private List<SpecData> specValsWork;
        private List<Pawn> medKeysWork;  private List<MeditationData> medValsWork;

        // Per-pawn specialization state.
        private Dictionary<Pawn, SpecData> specs = new Dictionary<Pawn, SpecData>();

        // Per-pawn meditation / awakening tracking.
        private Dictionary<Pawn, MeditationData> medData = new Dictionary<Pawn, MeditationData>();

        public GameComponent_PsycastSynergies(Game game) { }

        public SpecData GetSpec(Pawn pawn, bool create = false)
        {
            if (pawn == null) return null;
            if (specs.TryGetValue(pawn, out var d)) return d;
            if (!create) return null;
            d = new SpecData();
            specs[pawn] = d;
            return d;
        }

        public MeditationData GetMed(Pawn pawn, bool create = false)
        {
            if (pawn == null) return null;
            if (medData.TryGetValue(pawn, out var d)) return d;
            if (!create) return null;
            d = new MeditationData();
            medData[pawn] = d;
            return d;
        }

        // Dev: fully wipe a pawn's mod state - skill levels, specialization points/nodes, meditation + tier data.
        public void ResetPawn(Pawn pawn)
        {
            if (pawn == null) return;
            levels.Remove(pawn);
            specs.Remove(pawn);
            medData.Remove(pawn);
        }

        public IEnumerable<MeditationData> MedDataValues => medData.Values;
        public IEnumerable<KeyValuePair<Pawn, MeditationData>> MedDataPairs => medData;

        public bool Owns(Pawn pawn, string id)
        {
            var d = GetSpec(pawn);
            return d != null && d.owned.Contains(id);
        }

        // Leveled abilities in OTHER paths than the target's (for the Conduit cross-path synergy).
        public List<KeyValuePair<AbilityDef, int>> CrossPathContributors(Pawn pawn, PsycasterPathDef excludePath, AbilityDef excludeDef)
        {
            var result = new List<KeyValuePair<AbilityDef, int>>();
            if (pawn == null) return result;
            if (!levels.TryGetValue(pawn, out var d)) return result;
            foreach (var kv in d)
            {
                if (kv.Value <= 0 || kv.Key == excludeDef) continue;
                var ext = kv.Key.GetModExtension<AbilityExtension_Psycast>();
                if (ext?.path != null && ext.path != excludePath) result.Add(kv);
            }
            return result;
        }

        public override void FinalizeInit()
        {
            // Re-apply the PS_Enlightenment hediff from the mirrored MeditationData.tier so existing colonists
            // keep their enlightenment tier across save/load. Enemies re-roll on spawn, so no migration there.
            foreach (var kv in medData)
            {
                var p = kv.Key; var d = kv.Value;
                if (p == null || p.Dead || d == null || d.tier <= 0) continue;
                if (EnlightenmentTier.TierOf(p) != d.tier) EnlightenmentTier.SetTier(p, d.tier, false);
                else EnlightenmentTier.EnsureOnBrain(p);   // older saves seated it whole-body; move it to the brain
            }
            // Migration: the apex specialization id "ascendance" was renamed to "convergence".
            foreach (var sp in specs.Values)
                if (sp != null && sp.owned.Remove("ascendance")) sp.owned.Add("convergence");
        }

        public override void GameComponentTick()
        {
            int t = Find.TickManager.TicksGame;
            if (t % 120 == 0)
            {
                ChargeStore.Tick();
                foreach (var kv in specs)
                {
                    var pawn = kv.Key;
                    if (pawn == null || pawn.Dead || !pawn.Spawned) continue;
                    if (kv.Value.owned.Contains("wellspring"))
                        pawn.psychicEntropy?.OffsetPsyfocusDirectly(0.006f);
                }
            }

            MeditationSystem.Tick(t);   // meditation tracking + Enlightenment + coma risk (gates internally)
            if (t % 250 == 0) SyncPsycastHediffs();
        }

        // Informational counts for the Psychic Resonance hediff readout.
        public void SummarizeLevels(Pawn pawn, out int leveled, out int total)
        {
            leveled = 0; total = 0;
            if (pawn == null || !levels.TryGetValue(pawn, out var d)) return;
            foreach (var kv in d) if (kv.Value > 0) { leveled++; total += kv.Value; }
        }

        // Keep the informational + ascension hediffs in sync with each pawn's specs/levels.
        private void SyncPsycastHediffs()
        {
            var done = new HashSet<Pawn>();
            foreach (var p in specs.Keys) HandleHediffs(p, done);
            foreach (var p in levels.Keys) HandleHediffs(p, done);
        }

        private void HandleHediffs(Pawn p, HashSet<Pawn> done)
        {
            if (p == null || !done.Add(p) || p.Dead || p.health == null) return;
            var sp = GetSpec(p);
            bool hasLevels = levels.TryGetValue(p, out var d) && d.Count > 0;
            bool qualifies = (sp != null && sp.owned.Count > 0) || hasLevels;
            EnsureHediff(p, PsycastDefOf.PS_PsychicResonance, qualifies);
            AscensionSystem.Sync(p, sp);
        }

        private static void EnsureHediff(Pawn p, HediffDef def, bool want)
        {
            if (def == null || p.health?.hediffSet == null) return;
            var brain = p.health.hediffSet.GetBrain();   // seat psychic hediffs in the brain, like a psylink
            var hd = p.health.hediffSet.GetFirstHediffOfDef(def);
            if (!want) { if (hd != null) p.health.RemoveHediff(hd); return; }
            if (hd != null && hd.Part != brain) { p.health.RemoveHediff(hd); hd = null; }   // migrate whole-body -> brain
            if (hd == null) p.health.AddHediff(def, brain);
        }

        public static GameComponent_PsycastSynergies Instance =>
            Current.Game?.GetComponent<GameComponent_PsycastSynergies>();

        public int GetLevel(Pawn pawn, AbilityDef def)
        {
            if (pawn == null || def == null) return 0;
            if (levels.TryGetValue(pawn, out var d) && d.TryGetValue(def, out int lvl)) return lvl;
            return 0;
        }

        public void SetLevel(Pawn pawn, AbilityDef def, int level)
        {
            if (pawn == null || def == null) return;
            if (!levels.TryGetValue(pawn, out var d))
            {
                if (level <= 0) return;
                d = new Dictionary<AbilityDef, int>();
                levels[pawn] = d;
            }
            if (level <= 0) d.Remove(def);
            else d[def] = level;
        }

        public int AddLevel(Pawn pawn, AbilityDef def, int delta)
        {
            int n = GetLevel(pawn, def) + delta;
            SetLevel(pawn, def, n);
            return n;
        }

        // Has this pawn invested any real (spent-point) skill levels? Used by the oskill reconciler to
        // decide whether it's safe to tear down an implant it granted (never strip a real psycaster).
        public bool HasAnyInvestedLevels(Pawn pawn)
        {
            return pawn != null && levels.TryGetValue(pawn, out var d) && d != null && d.Count > 0;
        }

        // Total levels invested across every ability that shares this path.
        public int PathInvested(Pawn pawn, PsycasterPathDef path)
        {
            if (pawn == null || path == null) return 0;
            if (!levels.TryGetValue(pawn, out var d)) return 0;
            int sum = 0;
            foreach (var kv in d)
            {
                var ext = kv.Key.GetModExtension<AbilityExtension_Psycast>();
                if (ext != null && ext.path == path) sum += kv.Value;
            }
            return sum;
        }

        // Every OTHER ability in this path that has levels invested (the synergy sources for a
        // given ability), sorted by level descending.
        public List<KeyValuePair<AbilityDef, int>> PathContributors(Pawn pawn, PsycasterPathDef path, AbilityDef exclude)
        {
            var result = new List<KeyValuePair<AbilityDef, int>>();
            if (pawn == null || path == null) return result;
            if (!levels.TryGetValue(pawn, out var d)) return result;
            foreach (var kv in d)
            {
                if (kv.Value <= 0 || kv.Key == exclude) continue;
                var ext = kv.Key.GetModExtension<AbilityExtension_Psycast>();
                if (ext != null && ext.path == path) result.Add(kv);
            }
            result.Sort((a, b) => b.Value.CompareTo(a.Value));
            return result;
        }

        public void ClearPawn(Pawn pawn)
        {
            if (pawn != null) levels.Remove(pawn);
        }

        public override void ExposeData()
        {
            base.ExposeData();

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                entries = new List<SkillEntry>();
                foreach (var kv in levels)
                {
                    if (kv.Key == null || kv.Key.Destroyed) continue;
                    foreach (var inner in kv.Value)
                    {
                        if (inner.Key == null || inner.Value <= 0) continue;
                        entries.Add(new SkillEntry { pawn = kv.Key, def = inner.Key, level = inner.Value });
                    }
                }
            }

            Scribe_Collections.Look(ref entries, "skillEntries", LookMode.Deep);
            Scribe_Collections.Look(ref specs, "specs", LookMode.Reference, LookMode.Deep, ref specKeysWork, ref specValsWork);
            Scribe_Collections.Look(ref medData, "medData", LookMode.Reference, LookMode.Deep, ref medKeysWork, ref medValsWork);
            if (medData == null) medData = new Dictionary<Pawn, MeditationData>();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                levels = new Dictionary<Pawn, Dictionary<AbilityDef, int>>();
                if (entries != null)
                {
                    foreach (var e in entries)
                    {
                        if (e == null || e.pawn == null || e.def == null || e.level <= 0) continue;
                        SetLevel(e.pawn, e.def, e.level);
                    }
                }
                entries = null;

                if (specs == null) specs = new Dictionary<Pawn, SpecData>();
                foreach (var key in specs.Keys.Where(k => k == null).ToList()) specs.Remove(key);

                // Rebuild the runtime forced-meditation set from saved per-pawn flags.
                ForcedMeditation.Rebuild(this);
            }
        }
    }
}

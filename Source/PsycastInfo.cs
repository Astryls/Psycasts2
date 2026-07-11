#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using VEF.Abilities;
using VanillaPsycastsExpanded;
using AbilityDef = VEF.Abilities.AbilityDef;
using Ability = VEF.Abilities.Ability;

namespace PsycastSynergies
{
    // Reads what a psycast actually DOES - its role (Offensive/Control/Boon), which stats it has,
    // its primary stat, and concrete per-effect values - so synergies and the tooltip are accurate.
    public static class PsycastInfo
    {
        public struct Effect
        {
            public string label;
            public string value;   // current (level-adjusted) magnitude
            public SynStat? stat;  // which scaled stat this is (null = info-only / unscaled)
            public bool scaled;
            public Effect(string l, string v, SynStat? st, bool sc = true) { label = l; value = v; stat = st; scaled = sc; }
        }

        // ── Role classification ───────────────────────────────────────────────
        public static Role RoleOf(AbilityDef def)
        {
            var hed = def.GetModExtension<AbilityExtension_Hediff>();
            if (hed?.hediff != null)
                return (hed.targetOnlyEnemies || hed.hediff.isBad) ? Role.Control : Role.Boon;
            if (HasDamage(def)) return Role.Offensive;
            if (def.isPositive == true) return Role.Boon;
            if (def.isPositive == false) return Role.Control;
            // No explicit signal - infer from the stats it actually has so stat-bearing utility
            // casts (e.g. Aeromancer's Cyclone: a radius AoE with no damage/hediff/isPositive)
            // still get a primary stat (own-scaling) and participate in synergies.
            if (HasStat(def, SynStat.Radius)) return Role.Control;          // area battlefield effect
            if (HasStat(def, SynStat.Duration) || HasStat(def, SynStat.Strength)) return Role.Boon;
            return Role.Neutral;                                            // pure mobility / teleport
        }

        private static bool HasDamage(AbilityDef def)
            => def.power != 0f
               || def.GetModExtension<AbilityExtension_Explosion>() != null
               || def.GetModExtension<AbilityExtension_Projectile>()?.projectile != null;

        // ── Archetype detection (drives guaranteed synergy edges) ─────────────
        // Addon ability class names are thematic, not mechanical, so these lean on def fields with a
        // tiny curated set + a couple of name hints only for beams/volleys (which have no def tell).
        private static readonly HashSet<string> BeamClasses = new HashSet<string>
        {
            "VPE_Luminis.Ability_SunBeam",
            "MakaiTechPsycast.CorruptedProphet.Ability_NightmarePillar",
        };
        private static readonly HashSet<string> VolleyClasses = new HashSet<string>
        {
            "VPE_Gravmancer.Ability_SummonMeteorites",
            "VPE_Gravmancer.Ability_SpawnMeteorite",
            "VPE_Gravmancer.Ability_SpawnVolcanicDebris",
            "VPE_Gravmancer.Ability_SpawnShipChunk",
        };

        public static bool IsBeam(AbilityDef def)
        {
            if (def?.abilityClass == null) return false;
            if (BeamClasses.Contains(def.abilityClass.FullName)) return true;
            return NameHas(def, "Beam", "Laser");
        }

        public static bool IsProjectileVolley(AbilityDef def)
        {
            if (def?.abilityClass == null) return false;
            if (VolleyClasses.Contains(def.abilityClass.FullName)) return true;
            return NameHas(def, "Meteor", "Barrage", "Bombard", "Volley", "Skyfall", "Missile", "Comet");
        }

        // Match a keyword against the ability's defName, label OR class name. The defName/label are
        // usually the most descriptive of the mechanic, even when the class name is thematic.
        private static bool NameHas(AbilityDef def, params string[] subs)
        {
            string dn = def.defName ?? "", lb = def.label ?? "", cn = def.abilityClass?.Name ?? "";
            foreach (var s in subs)
                if (dn.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                    || lb.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                    || cn.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // Spawns friendly pawns/minions (count-scaling makes sense).
        // Creates NEW creatures/constructs from nothing (golems, fleshbeasts, undead, animals…).
        // Excludes Puppeteer-style abilities, which subjugate EXISTING pawns rather than summon, and
        // projectile volleys (meteorite/skyfaller spawns), which are their own archetype.
        public static bool IsSummon(AbilityDef def)
        {
            if (def?.abilityClass == null) return false;
            if (NameHas(def, "Puppet", "Subjugat", "Marionette")) return false;
            if (IsProjectileVolley(def)) return false;
            return NameHas(def, "Summon", "Conjure", "Raise", "Animate", "Golem", "Fleshbeast",
                "Skeleton", "Shambler", "Zombie", "Undead", "Familiar", "Elemental", "Hive");
        }

        // Strength tier of a synergy type, used to balance each skill's received mix (S=0 … C=3).
        public static int Tier(SynStat s)
        {
            switch (s)
            {
                case SynStat.Charges: case SynStat.Power: case SynStat.SummonCount: case SynStat.ProjectileCount: return 0;
                case SynStat.Radius: case SynStat.Targets: case SynStat.Strength: return 1;
                case SynStat.Cooldown: case SynStat.Efficiency: case SynStat.Range: return 2;
                default: return 3;   // Haste, Yield, Insight, Duration
            }
        }

        // Single-target offensive strike (eligible for the "pick extra targets" multi-strike synergy).
        public static bool IsSingleStrike(AbilityDef def)
        {
            if (def == null || def.targetCount > 1 || def.hasAoE) return false;
            if (RoleOf(def) != Role.Offensive) return false;
            var modes = def.targetModes;
            if (modes != null && modes.Count > 0 && modes[0] == AbilityTargetingMode.Self) return false;
            return true;
        }

        // True if this ability has any multi-target ("multi-strike") benefit - Targets primary or a
        // received Targets edge - so it should always allow selecting at least 2 targets.
        public static bool GrantsMultiTarget(AbilityDef def)
        {
            if (PrimaryStat(def) == SynStat.Targets) return true;
            foreach (var src in SynergySources(def))
                if (EdgeStat(src, def) == SynStat.Targets) return true;
            return false;
        }

        public static bool HasStat(AbilityDef def, SynStat stat)
        {
            var expl = def.GetModExtension<AbilityExtension_Explosion>();
            switch (stat)
            {
                case SynStat.Power:
                    return def.power != 0f || (expl != null && (expl.explosionDamageAmount > 0 || expl.explosionDamageDef != null));
                case SynStat.Radius:
                    return def.radius > 0f || (expl != null && expl.explosionRadius > 0f) || IsBeam(def);
                case SynStat.Duration:
                    // Skill-aware: a few percent of a multi-hour buff is meaningless, so only treat duration as
                    // a scalable type when it is short enough to matter (<= ~4 in-game hours; 2500 ticks = 1 hour).
                    return def.durationTime > 0 && def.durationTime <= 10000;
                case SynStat.Strength:
                    var hed = def.GetModExtension<AbilityExtension_Hediff>();
                    return hed != null && hed.severity > 0f;
                case SynStat.Range:
                    // Skill-aware: skip "global"/whole-map ranges (self casts, Mend, etc.) where +range does nothing.
                    return def.range > 0f && def.range <= 60f;
                case SynStat.Cooldown:
                    return def.cooldownTime > 0;
                case SynStat.Targets:
                    return def.targetCount > 1 || IsSingleStrike(def);
                case SynStat.ProjectileCount:
                    return IsProjectileVolley(def);
                case SynStat.SummonCount:
                    return IsSummon(def);
                // Universal types - any cast can take them (so neutral/utility skills scale too).
                case SynStat.Charges:
                case SynStat.Efficiency:
                case SynStat.Haste:
                case SynStat.Yield:
                    return true;
                case SynStat.Insight:
                    return false;   // retired: psycast XP is per-cast tier scaling, not a synergy
            }
            return false;
        }

        // The ONE synergy type this ability's own levels scale. Rarity-weighted and biased by role
        // (offense leans Power, control Radius, boon Strength) but mixed with the universal Range /
        // Cooldown types, so two skills in the same path rarely share a type - multi-skilling matters.
        // Deterministic per defName + cached.
        private static readonly Dictionary<AbilityDef, SynStat?> primCache = new Dictionary<AbilityDef, SynStat?>();

        public static SynStat? PrimaryStat(AbilityDef def)
        {
            if (def == null) return null;
            // Player tuning (in-game balance editor) wins over everything.
            if (PlayerTuning.TryPrim(def.defName, out int pv)) return pv < 0 ? (SynStat?)null : (SynStat)pv;
            return BasePrimary(def);
        }

        // Baseline (pre-player-tuning) primary: baked ManualBalance overlay -> frozen graph -> computed.
        // The balance editor diffs the player's pick against THIS to decide what counts as an edit.
        public static SynStat? BasePrimary(AbilityDef def)
        {
            if (def == null) return null;
            if (ManualBalance.TryPrim(def.defName, out int ovp)) return ovp < 0 ? (SynStat?)null : (SynStat)ovp;
            var fs = PsycastSynergiesMod.Settings?.frozenSyn;
            if (fs != null && fs.TryGetValue(def.defName, out var fz))
                return fz.prim < 0 ? (SynStat?)null : (SynStat)fz.prim;
            if (primCache.TryGetValue(def, out var cached)) return cached;
            SynStat? result = ComputePrimary(def);
            primCache[def] = result;
            return result;
        }

        private static SynStat? ComputePrimary(AbilityDef def)
            => ComputeWeighted(def, StableHash(def.defName), allowCharges: false);

        // Rarity-weighted pick of a stat the def can actually use, selected deterministically by a
        // caller-supplied seed. PrimaryStat seeds on the def; EdgeStat seeds on the source|target
        // pair so each synergy LINK rolls its own (useful) type.
        private static SynStat? ComputeWeighted(AbilityDef def, uint seed, bool allowCharges = true, float sTierPenalty = 1f)
        {
            Role role = RoleOf(def);
            var weights = new List<KeyValuePair<SynStat, float>>();
            void TryAdd(SynStat s, float w)
            {
                if (w <= 0f || !HasStat(def, s)) return;
                if (Tier(s) == 0) w *= sTierPenalty;   // balance: dampen S-tier once one is already taken
                if (w > 0f) weights.Add(new KeyValuePair<SynStat, float>(s, w));
            }
            TryAdd(SynStat.Power, role == Role.Offensive ? 6f : 2f);
            TryAdd(SynStat.Radius, IsBeam(def) ? 7f : (role == Role.Control ? 6f : 2f));
            TryAdd(SynStat.Duration, (role == Role.Boon || role == Role.Control) ? 4f : 2f);
            TryAdd(SynStat.Strength, role == Role.Boon ? 6f : 2f);
            TryAdd(SynStat.Range, 2.5f);
            TryAdd(SynStat.Cooldown, 2.5f);
            TryAdd(SynStat.Targets, IsSingleStrike(def) ? 4f : 2f);
            TryAdd(SynStat.ProjectileCount, IsProjectileVolley(def) ? 7f : 0f);
            TryAdd(SynStat.SummonCount, IsSummon(def) ? 7f : 0f);
            if (allowCharges) TryAdd(SynStat.Charges, 1.2f);   // Charges is a SYNERGY-only type, never a primary
            TryAdd(SynStat.Efficiency, 0.8f);
            TryAdd(SynStat.Haste, 0.8f);
            TryAdd(SynStat.Yield, 0.8f);
            // Insight (psycast XP) retired as a synergy - XP now comes from casting, scaled by tier.
            if (weights.Count == 0) return null;

            float total = 0f;
            foreach (var w in weights) total += w.Value;
            float r = (seed % 1000000u) / 1000000f * total;
            foreach (var w in weights) { if (r < w.Value) return w.Key; r -= w.Value; }
            return weights[weights.Count - 1].Key;
        }

        // The synergy type a SOURCE skill feeds a TARGET skill. Pair-seeded and weighted toward the
        // TARGET's usable stats, so one source empowers different path-mates in DIFFERENT ways.
        private static readonly Dictionary<(AbilityDef, AbilityDef), SynStat?> edgeCache
            = new Dictionary<(AbilityDef, AbilityDef), SynStat?>();

        // Per-skill primary scaling strength (hand-tuned multiplier; 1 = auto). Scales a skill's OWN
        // per-level contribution to its primary stat. Player tuning > baked ManualBalance.
        public static float PrimaryStrength(AbilityDef def)
            => def == null ? 1f
             : PlayerTuning.TryPrimStr(def.defName, out float pf) ? pf
             : ManualBalance.PrimStrength(def.defName);

        public static float BasePrimaryStrength(AbilityDef def)
            => def == null ? 1f : ManualBalance.PrimStrength(def.defName);

        // Per-edge synergy strength (hand-tuned multiplier; 1 = auto). Scales how much a source feeds
        // a target. Player tuning > baked ManualBalance.
        public static float EdgeStrength(AbilityDef source, AbilityDef target)
            => (source == null || target == null) ? 1f
             : PlayerTuning.TryEdgeStr(source.defName, target.defName, out float ef) ? ef
             : ManualBalance.EdgeStrength(source.defName, target.defName);

        public static float BaseEdgeStrength(AbilityDef source, AbilityDef target)
            => (source == null || target == null) ? 1f : ManualBalance.EdgeStrength(source.defName, target.defName);

        public static SynStat? EdgeStat(AbilityDef source, AbilityDef target)
        {
            if (source == null || target == null) return null;
            // Player tuning (in-game balance editor) wins over everything.
            if (PlayerTuning.TryEdge(source.defName, target.defName, out int pv)) return pv < 0 ? (SynStat?)null : (SynStat)pv;
            return BaseEdgeStat(source, target);
        }

        // Baseline (pre-player-tuning) edge type: baked ManualBalance overlay -> frozen graph -> computed.
        public static SynStat? BaseEdgeStat(AbilityDef source, AbilityDef target)
        {
            if (source == null || target == null) return null;
            if (ManualBalance.TryEdge(source.defName, target.defName, out int ove)) return ove < 0 ? (SynStat?)null : (SynStat)ove;
            var fs = PsycastSynergiesMod.Settings?.frozenSyn;
            if (fs != null && fs.TryGetValue(target.defName, out var fz))
            {
                int idx = fz.srcs.IndexOf(source.defName);
                if (idx >= 0 && idx < fz.edges.Count)
                    return fz.edges[idx] < 0 ? (SynStat?)null : (SynStat)fz.edges[idx];
            }
            var key = (source, target);
            if (edgeCache.TryGetValue(key, out var cached)) return cached;
            SynStat? result = ComputeEdge(source, target);
            edgeCache[key] = result;
            return result;
        }

        private static SynStat? ComputeEdge(AbilityDef source, AbilityDef target)
            => ComputeWeighted(target, StableHash((source.defName ?? "") + "\u2192" + (target.defName ?? "")));

        // Each skill has a FIXED, deterministic set of 3-5 synergy sources (avg 3) drawn from
        // anywhere in its path (any tier - a bottom skill can feed a capstone), not all path-mates.
        private static readonly Dictionary<AbilityDef, List<AbilityDef>> sourceCache = new Dictionary<AbilityDef, List<AbilityDef>>();
        private static readonly List<AbilityDef> EmptySources = new List<AbilityDef>();

        public static List<AbilityDef> SynergySources(AbilityDef target)
        {
            if (target == null) return EmptySources;
            if (sourceCache.TryGetValue(target, out var c)) return c;
            // Player tuning (a full replacement list) wins. The cache holds the EFFECTIVE list and
            // is invalidated on every editor mutation (PlayerTuning -> Dirty -> ClearCaches).
            List<AbilityDef> result = PlayerTuning.TrySrcs(target.defName, out var names)
                ? ResolveNames(names)
                : BaseSourcesUncached(target);
            sourceCache[target] = result;
            return result;
        }

        // Baseline (pre-player-tuning) source list: baked srcs overlay -> frozen graph -> computed.
        public static List<AbilityDef> BaseSources(AbilityDef target)
            => target == null ? EmptySources : BaseSourcesUncached(target);

        private static List<AbilityDef> BaseSourcesUncached(AbilityDef target)
        {
            if (ManualBalance.TrySrcs(target.defName, out var mnames)) return ResolveNames(mnames);
            var fs = PsycastSynergiesMod.Settings?.frozenSyn;
            if (fs != null && fs.TryGetValue(target.defName, out var fz)) return ResolveNames(fz.srcs);
            return ComputeSources(target);
        }

        private static List<AbilityDef> ResolveNames(List<string> names)
        {
            var list = new List<AbilityDef>();
            if (names != null)
                foreach (var name in names)
                {
                    var d = DefDatabase<AbilityDef>.GetNamedSilentFail(name);
                    if (d != null) list.Add(d);
                }
            return list;
        }

        private static List<AbilityDef> ComputeSources(AbilityDef target)
        {
            var result = new List<AbilityDef>();
            var ext = target.GetModExtension<AbilityExtension_Psycast>();
            if (ext?.path?.abilities == null) return result;
            var pool = ext.path.abilities
                .Where(a => a != target && a.GetModExtension<AbilityExtension_Psycast>() != null).ToList();
            if (pool.Count == 0) return result;

            uint h = StableHash(target.defName);
            int count = 3;
            if (h % 5u == 0u) count++;
            if (h % 11u == 0u) count++;
            if (h % 3u == 0u) count--;
            count = Mathf.Clamp(count, 2, 5);
            if (count > pool.Count) count = pool.Count;

            pool.Sort((a, b) => SrcKey(target, a).CompareTo(SrcKey(target, b)));
            for (int i = 0; i < count; i++) result.Add(pool[i]);
            return result;
        }

        private static uint SrcKey(AbilityDef t, AbilityDef a)
            => StableHash((t.defName ?? "") + "|" + (a.defName ?? ""));

        // Deterministic string hash (FNV-1a). String.GetHashCode is randomized per-process on some
        // runtimes, which would reshuffle the whole synergy graph every launch - this never changes.
        private static uint StableHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0u;
            uint h = 2166136261u;
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; }
            return h & 0x7fffffffu;
        }

        // (B) Freeze the whole synergy graph once into mod settings so it never reshuffles across
        // launches, versions, or path changes. Safe to call every startup: only fills entries that
        // don't exist yet (e.g. newly-added abilities/addons); existing entries are never rewritten.
        public static void EnsureFrozen()
        {
            var s = PsycastSynergiesMod.Settings;
            if (s == null) return;
            if (s.frozenSyn == null) s.frozenSyn = new Dictionary<string, FrozenSyn>();
            bool changed = false;
            if (s.graphVersion != GraphVersion) { s.frozenSyn.Clear(); s.graphVersion = GraphVersion; changed = true; }

            // Group psycast abilities by path; build each path's graph as a whole so each skill's
            // empowers (out-degree) and synergies-received (in-degree) can both be capped at 3.
            var byPath = new Dictionary<string, List<AbilityDef>>();
            var pathless = new List<AbilityDef>();
            foreach (var def in DefDatabase<AbilityDef>.AllDefs)
            {
                var ext = def?.GetModExtension<AbilityExtension_Psycast>();
                if (ext == null || def.defName == null) continue;
                if (ext.path == null) { pathless.Add(def); continue; }
                string pk = ext.path.defName;
                if (!byPath.TryGetValue(pk, out var list)) { list = new List<AbilityDef>(); byPath[pk] = list; }
                list.Add(def);
            }
            foreach (var kv in byPath)
            {
                if (kv.Value.All(d => s.frozenSyn.ContainsKey(d.defName))) continue;   // fully frozen - keep stable
                BuildPathGraph(kv.Value, s.frozenSyn);
                changed = true;
            }
            foreach (var def in pathless)
            {
                if (s.frozenSyn.ContainsKey(def.defName)) continue;
                var f = new FrozenSyn();
                var p = ComputePrimary(def);
                f.prim = p.HasValue ? (int)p.Value : -1;
                s.frozenSyn[def.defName] = f;
                changed = true;
            }
            ClearCaches();   // frozen data is authoritative from now on
            if (changed) PsycastSynergiesMod.Instance?.WriteSettings();
        }

        // ── Global per-path graph builder ───────────────────────────
        private static readonly Dictionary<AbilityDef, HashSet<string>> tokenCache = new Dictionary<AbilityDef, HashSet<string>>();

        // Significant (length >= 4) name tokens of an ability, used to detect thematic kinship.
        private static HashSet<string> Tokens(AbilityDef def)
        {
            if (tokenCache.TryGetValue(def, out var c)) return c;
            var set = new HashSet<string>();
            string n = def.defName ?? "";
            int us = n.IndexOf('_'); if (us >= 1 && us <= 6) n = n.Substring(us + 1);   // drop mod prefix (VPE_, VPEP_…)
            var sb = new StringBuilder();
            void Flush() { if (sb.Length >= 4) set.Add(sb.ToString().ToLowerInvariant()); sb.Clear(); }
            foreach (char ch in n)
            {
                if (ch == '_') Flush();
                else if (char.IsUpper(ch) && sb.Length > 0) { Flush(); sb.Append(ch); }
                else sb.Append(ch);
            }
            Flush();
            tokenCache[def] = set;
            return set;
        }

        // Thematic (D2 skill-tab kin): same archetype or a shared name stem (chunk→chunk, bolt→bolt).
        // Reciprocal edges are allowed ONLY between thematic skills.
        private static bool Thematic(AbilityDef a, AbilityDef b)
        {
            if (IsBeam(a) && IsBeam(b)) return true;
            if (IsProjectileVolley(a) && IsProjectileVolley(b)) return true;
            if (IsSummon(a) && IsSummon(b)) return true;
            var ta = Tokens(a);
            foreach (var t in Tokens(b)) if (ta.Contains(t)) return true;
            return false;
        }

        private static long Pack(int a, int b) => ((long)a << 20) | (uint)b;

        // Build a whole path's directed synergy graph. Each skill receives <=3 and gives <=3 edges;
        // thematic kin are strongly preferred (and may be reciprocal); edge types weighted to the
        // target's usable stats and tier-balanced (<=1 S-tier per received set).
        private static void BuildPathGraph(List<AbilityDef> nodesIn, Dictionary<string, FrozenSyn> outp)
        {
            var nodes = nodesIn.Where(d => d?.defName != null).ToList();
            nodes.Sort((a, b) => StableHash(a.defName).CompareTo(StableHash(b.defName)));
            int n = nodes.Count;
            var inDeg = new int[n];
            var outDeg = new int[n];
            var have = new HashSet<long>();
            var srcsOf = new List<int>[n];
            for (int i = 0; i < n; i++) srcsOf[i] = new List<int>();

            for (int ti = 0; ti < n; ti++)
            {
                var target = nodes[ti];
                var cands = new List<KeyValuePair<int, ulong>>();
                for (int si = 0; si < n; si++)
                {
                    if (si == ti) continue;
                    bool them = Thematic(nodes[si], target);
                    ulong score = (them ? 4000000000UL : 0UL)
                                + (StableHash(nodes[si].defName + "\u2192" + target.defName) % 1000000000UL);
                    cands.Add(new KeyValuePair<int, ulong>(si, score));
                }
                cands.Sort((x, y) => y.Value.CompareTo(x.Value));
                foreach (var kv in cands)
                {
                    if (inDeg[ti] >= 3) break;
                    int si = kv.Key;
                    if (outDeg[si] >= 3) continue;
                    if (have.Contains(Pack(si, ti))) continue;
                    if (have.Contains(Pack(ti, si)) && !Thematic(nodes[si], target)) continue;   // reciprocal only if thematic
                    have.Add(Pack(si, ti));
                    srcsOf[ti].Add(si);
                    inDeg[ti]++; outDeg[si]++;
                }
            }

            for (int ti = 0; ti < n; ti++)
            {
                var target = nodes[ti];
                var f = new FrozenSyn();
                var p = ComputePrimary(target);
                f.prim = p.HasValue ? (int)p.Value : -1;
                int sUsed = 0;
                foreach (int si in srcsOf[ti])
                {
                    var src = nodes[si];
                    uint seed = StableHash(src.defName + "\u2192" + target.defName);
                    var et = ComputeWeighted(target, seed, true, sUsed >= 1 ? 0.05f : 1f);
                    if (et.HasValue && Tier(et.Value) == 0) sUsed++;
                    f.srcs.Add(src.defName);
                    f.edges.Add(et.HasValue ? (int)et.Value : -1);
                }
                EnforceArchetypeEdge(target, f);
                outp[target.defName] = f;
            }
        }

        // Bumped whenever the generation rules change so saved graphs auto-rebuild once.
        public const int GraphVersion = 7;   // bumped: skill-aware HasStat (drop Duration on long buffs, Range on global casts)

        // Guarantee an archetype-appropriate RECEIVED edge: beams gain Radius, projectile volleys gain
        // Projectile Count, single-strike offensives sometimes gain Multi-target.
        private static void EnforceArchetypeEdge(AbilityDef target, FrozenSyn f)
        {
            if (f.srcs.Count == 0) return;
            int required; bool optional = false;
            if (IsBeam(target)) required = (int)SynStat.Radius;
            else if (IsProjectileVolley(target)) required = (int)SynStat.ProjectileCount;
            else if (IsSummon(target)) { required = (int)SynStat.SummonCount; optional = true; }
            else if (IsSingleStrike(target)) { required = (int)SynStat.Targets; optional = true; }
            else return;
            if (f.edges.Contains(required)) return;
            if (optional && StableHash(target.defName + "#mt") % 100u >= 45u) return;
            f.edges[0] = required;
        }

        public static void ClearCaches()
        {
            primCache.Clear(); edgeCache.Clear(); sourceCache.Clear();
        }

        // Pre-populate the runtime caches (primary stat, synergy sources, edge types) at startup so the
        // first psycast-tab open / first tooltip hover doesn't pay the lazy warm-up cost as a frame
        // hitch. EnsureFrozen() ends with ClearCaches(), so this MUST run after it.
        public static void WarmCaches()
        {
            foreach (var def in DefDatabase<AbilityDef>.AllDefs)
            {
                if (def?.GetModExtension<AbilityExtension_Psycast>() == null) continue;
                PrimaryStat(def);
                var srcs = SynergySources(def);
                for (int i = 0; i < srcs.Count; i++) EdgeStat(srcs[i], def);
            }
        }

        public static Color RoleColor(Role r)
        {
            switch (r)
            {
                case Role.Offensive: return Palette.Bad;
                case Role.Control: return new Color(1f, 0.62f, 0.24f);
                case Role.Boon: return Palette.Good;
                default: return Palette.TextDim;
            }
        }

        // ── Concrete scalable effects with current values ─────────────────────
        private static string Num(float f) => f.ToString("0.#");

        public static List<Effect> Scalables(Pawn pawn, AbilityDef def, Ability inst)
        {
            var s = PsycastSynergiesMod.Settings;
            var list = new List<Effect>();

            var expl = def.GetModExtension<AbilityExtension_Explosion>();
            if (expl != null)
            {
                if (s.scaleRadius && expl.explosionRadius > 0f)
                {
                    float r = expl.explosionRadius * SkillSystem.StatMultiplier(pawn, def, SynStat.Radius);
                    list.Add(new Effect("PS_FxBlastRadius".Translate(), "PS_FxTiles".Translate(Num(r)), SynStat.Radius));
                }
                if (s.scalePower)
                {
                    int baseAmt = expl.explosionDamageAmount >= 0
                        ? expl.explosionDamageAmount
                        : (expl.explosionDamageDef != null ? expl.explosionDamageDef.defaultDamage : -1);
                    if (baseAmt > 0)
                    {
                        float dmg = baseAmt * SkillSystem.StatMultiplier(pawn, def, SynStat.Power);
                        list.Add(new Effect("PS_FxBlastDamage".Translate(), Mathf.RoundToInt(dmg).ToString(), SynStat.Power));
                    }
                }
            }

            if (s.scalePower && expl == null && def.power != 0f)
            {
                float p = inst != null ? inst.GetPowerForPawn() : def.power * SkillSystem.StatMultiplier(pawn, def, SynStat.Power);
                list.Add(new Effect("PS_Stat_Power".Translate(), Num(p), SynStat.Power));
            }

            if (s.scaleRadius && expl == null && def.radius > 0f)
            {
                float r = inst != null ? inst.GetRadiusForPawn() : def.radius * SkillSystem.StatMultiplier(pawn, def, SynStat.Radius);
                list.Add(new Effect("PS_FxRadius".Translate(), "PS_FxTiles".Translate(Num(r)), SynStat.Radius));
            }

            if (s.scaleDuration && def.durationTime > 0)
            {
                int d = inst != null ? inst.GetDurationForPawn() : Mathf.RoundToInt(def.durationTime * SkillSystem.StatMultiplier(pawn, def, SynStat.Duration));
                if (d > 0) list.Add(new Effect("PS_Stat_Duration".Translate(), d.ToStringTicksToPeriod(), SynStat.Duration));
            }

            if (def.range > 0f)
            {
                float rng = inst != null ? inst.GetRangeForPawn() : def.range * SkillSystem.StatMultiplier(pawn, def, SynStat.Range);
                if (rng > 0f && rng < 500f) list.Add(new Effect("PS_Stat_Range".Translate(), "PS_FxTiles".Translate(Num(rng)), SynStat.Range));
            }
            if (def.cooldownTime > 0)
            {
                int cd = inst != null ? inst.GetCooldownForPawn() : def.cooldownTime;
                if (cd > 0) list.Add(new Effect("PS_Stat_Cooldown".Translate(), cd.ToStringTicksToPeriod(), SynStat.Cooldown));
            }

            // Meta / universal types active on this skill (from its own type OR fed by neighbours).
            string PctS(float f) => (f * 100f).ToString("0.#") + "%";
            int chMax = ChargeStore.Max(pawn, def);
            if (chMax > 0) list.Add(new Effect("PS_FxFreeCharges".Translate(), chMax + " / " + ChargeStore.Cap, null));
            if (HasStat(def, SynStat.Targets) || def.targetCount > 1)
            {
                int total = def.targetCount + SkillSystem.ExtraTargets(pawn, def);
                if (total > 1) list.Add(new Effect("PS_Stat_Targets".Translate(), "PS_FxPerCast".Translate(total), null));
            }
            if (HasStat(def, SynStat.ProjectileCount))
            {
                float pc = SkillSystem.StatMultiplier(pawn, def, SynStat.ProjectileCount);
                if (pc > 1.001f) list.Add(new Effect("PS_Stat_ProjectileCount".Translate(), "\u00d7" + Num(pc), SynStat.ProjectileCount));
            }
            if (HasStat(def, SynStat.SummonCount))
            {
                float sc = SkillSystem.StatMultiplier(pawn, def, SynStat.SummonCount);
                if (sc > 1.001f) list.Add(new Effect("PS_Stat_SummonCount".Translate(), "\u00d7" + Num(sc), SynStat.SummonCount));
            }
            float eff = SkillSystem.StatMultiplier(pawn, def, SynStat.Efficiency) - 1f;
            if (eff > 0f) list.Add(new Effect("PS_Stat_Efficiency".Translate(), "-" + PctS(Mathf.Min(eff, 0.7f)), null));
            float hst = SkillSystem.StatMultiplier(pawn, def, SynStat.Haste) - 1f;
            if (hst > 0f) list.Add(new Effect("PS_Stat_Haste".Translate(), "PS_FxTimeReduction".Translate(PctS(Mathf.Min(hst, 0.7f))), null));
            float yld = SkillSystem.StatMultiplier(pawn, def, SynStat.Yield) - 1f;
            if (yld > 0f) list.Add(new Effect("PS_Stat_Yield".Translate(), PctS(Mathf.Min(yld, 0.9f)), null));
            float ins = SkillSystem.StatMultiplier(pawn, def, SynStat.Insight) - 1f;
            if (ins > 0f) list.Add(new Effect("PS_FxBonusXp".Translate(), "+" + PctS(Mathf.Min(ins, 2f)), null));

            var proj = def.GetModExtension<AbilityExtension_Projectile>();
            if (proj?.projectile?.projectile != null)
            {
                int pd = proj.projectile.projectile.GetDamageAmount((Thing)null);
                if (pd > 0) list.Add(new Effect("PS_FxProjectileDamage".Translate(), "PS_FxUnscaled".Translate(pd), null, false));
            }

            var hed = def.GetModExtension<AbilityExtension_Hediff>();
            if (hed != null && s.scaleBuffStrength && hed.severity > 0f)
                list.Add(new Effect("PS_Stat_Strength".Translate(), "×" + Num(SkillSystem.StatMultiplier(pawn, def, SynStat.Strength)), SynStat.Strength));

            return list;
        }

        // Plain-language description of the boon/debuff a cast applies.
        public static string EffectSummary(AbilityDef def)
        {
            var hed = def.GetModExtension<AbilityExtension_Hediff>();
            if (hed?.hediff == null) return null;
            string body = DescribeHediff(hed.hediff);
            if (body == null) return null;
            string verb = (hed.targetOnlyEnemies || hed.hediff.isBad) ? "PS_FxTargetSuffers".Translate().ToString()
                        : (hed.applyToCaster ? "PS_FxCasterGains".Translate().ToString() : "PS_FxTargetGains".Translate().ToString());
            return verb + ": " + body;
        }

        private static string DescribeHediff(HediffDef hd)
        {
            if (hd.stages == null || hd.stages.Count == 0) return hd.LabelCap;
            var st = hd.stages[hd.stages.Count - 1];
            var parts = new List<string>();
            try
            {
                if (st.statOffsets != null)
                    foreach (var m in st.statOffsets)
                        parts.Add(m.stat.LabelCap + " " + m.stat.Worker.ValueToString(m.value, false, ToStringNumberSense.Offset));
                if (st.statFactors != null)
                    foreach (var m in st.statFactors)
                        parts.Add(m.stat.LabelCap + " ×" + m.value.ToString("0.##"));
                if (st.capMods != null)
                    foreach (var c in st.capMods)
                    {
                        if (c.offset != 0f) parts.Add(c.capacity.LabelCap + " " + (c.offset > 0f ? "+" : "") + c.offset.ToStringPercent());
                        else if (c.setMax < 998f) parts.Add(c.capacity.LabelCap + " max " + c.setMax.ToStringPercent());
                    }
                if (st.painFactor != 1f) parts.Add("Pain ×" + st.painFactor.ToString("0.##"));
                if (st.painOffset != 0f) parts.Add("Pain " + (st.painOffset > 0f ? "+" : "") + st.painOffset.ToStringPercent());
            }
            catch { }

            if (parts.Count == 0) return hd.LabelCap;
            if (parts.Count > 4) { parts = parts.Take(4).ToList(); parts.Add("…"); }
            return string.Join(", ", parts);
        }
    }

    // One ability's frozen synergy identity (B): its primary stat + its fixed synergy sources and
    // the edge type each source feeds. Persisted in mod settings so synergies never change.
    public class FrozenSyn : IExposable
    {
        public int prim = -1;                              // primary SynStat (int), -1 = none
        public List<string> srcs = new List<string>();     // synergy source ability defNames
        public List<int> edges = new List<int>();           // parallel: edge SynStat per source (-1 none)

        public void ExposeData()
        {
            Scribe_Values.Look(ref prim, "prim", -1);
            Scribe_Collections.Look(ref srcs, "srcs", LookMode.Value);
            Scribe_Collections.Look(ref edges, "edges", LookMode.Value);
            if (srcs == null) srcs = new List<string>();
            if (edges == null) edges = new List<int>();
        }
    }
}

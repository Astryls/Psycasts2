#nullable disable
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using VEF.Abilities;
using VanillaPsycastsExpanded;

namespace PsycastSynergies
{
    public static class SkillSystem
    {
        public static int GetLevel(Pawn pawn, AbilityDef def)
        {
            var gc = GameComponent_PsycastSynergies.Instance;
            return gc == null ? 0 : gc.GetLevel(pawn, def);
        }

        // ---- External level-bonus extension point (for mods like Itemization Overhaul "oskills") ----
        // Providers take (pawn, abilityDefName) and return EXTRA effective levels contributed from
        // outside the skill tree (e.g. an equipped item that grants "+3 Skip"). These add ONLY to the
        // power/scaling multiplier below - they deliberately do NOT touch invested level (GetLevel),
        // the per-skill cap (MaxLevel), point refunds, casting cost, or "mastered" FX. That keeps a
        // maxed 15/15 skill able to reach an effective 18 for scaling while staying 15 invested.
        private static readonly List<Func<Pawn, string, int>> externalBonusProviders = new List<Func<Pawn, string, int>>();

        public static void RegisterExternalLevelBonus(Func<Pawn, string, int> provider)
        {
            if (provider != null && !externalBonusProviders.Contains(provider))
                externalBonusProviders.Add(provider);
        }

        public static int ExternalBonus(Pawn pawn, AbilityDef def)
        {
            if (pawn == null || def == null || externalBonusProviders.Count == 0) return 0;
            int sum = 0;
            for (int i = 0; i < externalBonusProviders.Count; i++)
            {
                try { sum += externalBonusProviders[i](pawn, def.defName); }
                catch { }
            }
            return sum;
        }

        // Does this pawn actually KNOW (have learned) the given ability? External mods use this to decide
        // whether to boost the real leveled psycast (known -> overlevel via ExternalBonus) or grant their
        // own fallback ability instead (unknown / not a psycaster).
        public static bool PawnHasLearnedAbility(Pawn pawn, string abilityDefName)
        {
            if (pawn == null || string.IsNullOrEmpty(abilityDefName)) return false;
            var comp = pawn.GetComp<CompAbilities>();
            var learned = comp?.LearnedAbilities;
            if (learned == null) return false;
            for (int i = 0; i < learned.Count; i++)
                if (learned[i]?.def != null && learned[i].def.defName == abilityDefName) return true;
            return false;
        }

        // Per-skill max level: base setting + Ascendance bonus, but gated by psycaster level so you
        // can't dump all levels into one skill immediately. A fresh psycaster can only invest a
        // little; the cap rises as they level up.
        public static int MaxLevel(Pawn pawn, AbilityDef def)
            => MaxLevel(pawn, def, pawn?.Psycasts()?.level ?? 0);

        // Overload taking a pre-fetched psy level so a per-icon UI loop can read the level once per
        // frame instead of doing a Psycasts() hediff lookup for every icon.
        public static int MaxLevel(Pawn pawn, AbilityDef def, int psy)
        {
            var s = PsycastSynergiesMod.Settings;
            int cap = s.maxSkillLevel + SpecEffects.LevelCapBonus(pawn);
            int per = Mathf.Max(1, s.psyLevelsPerSkillLevel);
            int tierBase = TierBase(def);   // hoisted: compute the (cached) tier base ONCE, not per loop step
            int n = 0;
            while (n < cap)
            {
                int m = n + 1;
                // Level 1 is the learned floor (req = tier floor only); the per-level psycaster ramp
                // applies to level 2+, so a learned tier-1 skill sits at its 1/x until psy rises.
                if (tierBase + (m - 1) * per + ((m - 1) * (m - 2) / 2) > psy) break;
                n++;
            }
            return n;
        }

        // Psycaster level required to reach a given skill level. Deeper psycasts (higher VPE tier,
        // 1-6) start higher; a triangular ramp makes each successive level cost progressively more
        // (Diablo-2 style), so the last few levels are a real end-game investment.
        public static int LevelReq(AbilityDef def, int skillLevel)
        {
            int per = Mathf.Max(1, PsycastSynergiesMod.Settings.psyLevelsPerSkillLevel);
            int n = Mathf.Max(1, skillLevel);
            // Level 1 is the learned floor (req = the ability's tier floor); the per-level ramp starts at 2.
            // Deeper psycasts (higher VPE tier) floor higher via TierBase; triangular ramp = end-game top levels.
            return TierBase(def) + (n - 1) * per + ((n - 1) * (n - 2) / 2);
        }

        // Ability VPE tier (1-6), cached. GetModExtension was being called ~cap times PER ICON per frame
        // via MaxLevel's loop (a real psycast-tab hot-path cost); memoize it.
        private static readonly Dictionary<AbilityDef, int> tierCache = new Dictionary<AbilityDef, int>();

        private static int AbilityTier(AbilityDef def)
        {
            if (def == null) return 1;
            if (tierCache.TryGetValue(def, out int v)) return v;
            v = def.GetModExtension<AbilityExtension_Psycast>()?.level ?? 1;
            tierCache[def] = v;
            return v;
        }

        private static int TierBase(AbilityDef def)
            => Mathf.Max(0, AbilityTier(def) - 1) * Mathf.Max(1, PsycastSynergiesMod.Settings.psyLevelsPerSkillLevel);

        // Per-stat multiplier (>= 1): own primary scaling + directed synergies + specialization bonuses.
        // A skill scales only its ONE primary type. Leveled same-path mates feed that primary type
        // (so to empower a skill you level its neighbours). Plus specialization bonuses.
        public static float StatMultiplier(Pawn pawn, AbilityDef target, SynStat stat)
        {
            if (pawn == null || target == null) return 1f;
            var gc = GameComponent_PsycastSynergies.Instance;
            var s = PsycastSynergiesMod.Settings;
            if (gc == null || s == null) return 1f;

            SynStat? prim = PsycastInfo.PrimaryStat(target);
            Role role = PsycastInfo.RoleOf(target);
            float bonus = 0f;

            // Own scaling: a skill's own levels scale only its OWN type.
            if (prim.HasValue && prim.Value == stat)
            {
                float perLevelMult = 1f, extraLevels = 0f;
                SpecEffects.OwnAdjust(pawn, target, ref perLevelMult, ref extraLevels);
                bonus += (gc.GetLevel(pawn, target) + extraLevels + ExternalBonus(pawn, target)) * s.perLevelPct * perLevelMult * CountBoost(stat)
                         * PsycastInfo.PrimaryStrength(target);   // hand-tuned primary strength
            }

            // Synergy: this skill's FIXED 3-5 synergy sources each contribute a bonus in their OWN
            // type. So a skill receives a small, curated MIX (not the whole path), and a source can
            // be any tier in the path (a bottom skill can feed a capstone). Skipped entirely when the
            // player disables the synergy system (own-level scaling above still applies).
            if (!s.disableSynergies && s.synergyPct > 0f)
            {
                float synFactor = SpecEffects.SynergyFactor(pawn);
                bool offDouble = SpecEffects.OffensiveSourceDoubled(pawn);
                foreach (var src in PsycastInfo.SynergySources(target))
                {
                    if (PsycastInfo.EdgeStat(src, target) != stat) continue;
                    int lvl = gc.GetLevel(pawn, src);
                    if (lvl <= 0) continue;
                    float term = lvl * s.synergyPct * CountBoost(stat) * PsycastInfo.EdgeStrength(src, target);   // hand-tuned edge strength
                    if (offDouble && PsycastInfo.RoleOf(src) == Role.Offensive) term *= 2f;
                    bonus += term * synFactor;
                }
            }

            bonus += SpecEffects.StatBonus(pawn, target, stat, role, prim);
            return 1f + bonus;
        }

        // A single "signature" multiplier for an ability, used to scale effects that aren't a
        // typed stat (meteorite/chunk count, beam damage, etc.): its primary-stat multiplier, or
        // just its own-level scaling when it has no primary stat.
        public static float SelfMultiplier(Pawn pawn, AbilityDef def)
        {
            if (pawn == null || def == null) return 1f;
            var prim = PsycastInfo.PrimaryStat(def);
            if (prim.HasValue) return StatMultiplier(pawn, def, prim.Value);
            var gc = GameComponent_PsycastSynergies.Instance;
            var s = PsycastSynergiesMod.Settings;
            if (gc == null || s == null) return 1f;
            float perLevelMult = 1f, extraLevels = 0f;
            SpecEffects.OwnAdjust(pawn, def, ref perLevelMult, ref extraLevels);
            return 1f + (gc.GetLevel(pawn, def) + extraLevels + ExternalBonus(pawn, def)) * s.perLevelPct * perLevelMult;
        }

        // Extra targets a single-strike can pick in the targeting UI, from its Targets ("multi-strike")
        // multiplier. ~0.20 per extra target, capped at 3. Used by both the tooltip and the targeting patch.
        // Count-type stats add DISCRETE units. Summons are integers, so under floor(base×mult) the 2nd
        // summon needs +100% (2×) - unreachable. Give SummonCount a steeper curve so investment lands
        // extra minions. (ProjectileCount stays 1×: meteorite/volley output scales continuously.)
        public static float CountBoost(SynStat stat) => stat == SynStat.SummonCount ? 2.5f : 1f;
        // Effective per-level synergy rate for a stat (used by tooltips + the compendium so the shown
        // number matches the actual multiplier).
        public static float SynergyRate(SynStat stat) => (PsycastSynergiesMod.Settings?.synergyPct ?? 0f) * CountBoost(stat);

        public static int ExtraTargets(Pawn pawn, AbilityDef def)
        {
            bool grants = PsycastInfo.GrantsMultiTarget(def);
            if (!grants && def.targetCount <= 1) return 0;
            float m = StatMultiplier(pawn, def, SynStat.Targets) - 1f;
            int leveled = Mathf.FloorToInt(m / 0.2f);
            int min = grants ? 1 : 0;   // any multi-strike synergy guarantees ≥ 2 total targets
            return Mathf.Clamp(Mathf.Max(min, leveled), 0, 3);
        }

        // Cost multiplier (>= depends on specs) for psyfocus + entropy.
        public static float CostMultiplier(Pawn pawn, AbilityDef def)
        {
            if (pawn == null || def == null) return 1f;
            var gc = GameComponent_PsycastSynergies.Instance;
            var s = PsycastSynergiesMod.Settings;
            if (gc == null || s == null) return 1f;
            float penalty = s.scaleCost ? gc.GetLevel(pawn, def) * s.costPerLevelPct : 0f;
            float factor = SpecEffects.CostFactor(pawn, def, penalty);
            // Efficiency synergy type: reduce psyfocus + heat cost.
            float effRed = StatMultiplier(pawn, def, SynStat.Efficiency) - 1f;
            if (effRed > 0f) factor *= 1f - Mathf.Min(effRed, 0.7f);
            return factor;
        }
    }
}

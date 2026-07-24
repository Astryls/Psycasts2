#nullable disable
using System.Collections.Generic;
using UnityEngine;
using Verse;
using VEF.Abilities;
using VanillaPsycastsExpanded;
using AbilityDef = VEF.Abilities.AbilityDef;

namespace PsycastSynergies
{
    // Central application of specialization effects. SkillSystem / cost / cast hooks call into here.
    // Every effect has TWO entry points: the Pawn overload (resolves SpecData itself - convenient
    // for one-off callers) and a SpecData overload for hot paths that already resolved it, so a
    // single StatMultiplier call doesn't re-probe the per-pawn dictionary four times.
    public static class SpecEffects
    {
        // Additive stat bonus (multiplier-space) from owned passive specs for (pawn, def, stat).
        public static float StatBonus(Pawn pawn, AbilityDef def, SynStat stat, Role role, SynStat? prim)
            => StatBonus(GameComponent_PsycastSynergies.Instance?.GetSpec(pawn), pawn, def, stat, role, prim);

        public static float StatBonus(SpecData d, Pawn pawn, AbilityDef def, SynStat stat, Role role, SynStat? prim)
        {
            if (d == null || d.owned.Count == 0) return 0f;
            float b = 0f;

            if (prim.HasValue && prim.Value == stat && d.Owns("kindling")) b += 0.03f;
            if (stat == SynStat.Power && d.Owns("surge")) b += 0.15f;
            if (stat == SynStat.Radius && d.Owns("resonance")) b += 0.15f;
            if (stat == SynStat.Duration && d.Owns("lingering")) b += 0.15f;
            if (stat == SynStat.Strength && d.Owns("potency")) b += 0.15f;

            if (role == Role.Offensive && stat == SynStat.Power)
            {
                if (d.Owns("onslaught")) b += 0.20f;
                if (d.Owns("abandon")) b += 0.40f;
                if (d.Owns("fervor")) b += 0.25f * EntropyFrac(pawn);
            }
            if (role == Role.Control && d.Owns("dominion") && (stat == SynStat.Radius || stat == SynStat.Duration)) b += 0.20f;
            if (role == Role.Boon && d.Owns("bulwark") && (stat == SynStat.Strength || stat == SynStat.Duration)) b += 0.20f;

            if (d.Owns("maelstrom") && PsycastInfo.HasStat(def, SynStat.Radius))
            {
                if (stat == SynStat.Radius) b += 0.50f;
                if (stat == SynStat.Duration) b += 0.25f;
            }
            if (d.Owns("sanctuary") && role == Role.Boon && (stat == SynStat.Strength || stat == SynStat.Duration)) b += 0.50f;

            if (d.Owns("overflow")) b += 0.08f;
            if (d.Owns("convergence")) b += 0.15f;

            if (d.Owns("mastery") && d.masteryDef == def && prim.HasValue && prim.Value == stat) b += 1.0f;

            if (d.Owns("attunement") && d.attuneDamage != null && ElementOf(def) == d.attuneDamage
                && (stat == SynStat.Power || stat == SynStat.Radius)) b += 0.25f;

            return b;
        }

        // Own-scaling adjustment from Discipline (chosen path).
        public static void OwnAdjust(Pawn pawn, AbilityDef def, ref float perLevelMult, ref float extraLevels)
            => OwnAdjust(GameComponent_PsycastSynergies.Instance?.GetSpec(pawn), def, ref perLevelMult, ref extraLevels);

        public static void OwnAdjust(SpecData d, AbilityDef def, ref float perLevelMult, ref float extraLevels)
        {
            if (d == null || !d.Owns("discipline") || d.disciplinePath == null) return;
            if (PsycastInfo.PathOf(def) == d.disciplinePath) { perLevelMult *= 1.5f; extraLevels += 1f; }
        }

        public static float SynergyFactor(Pawn pawn)
            => SynergyFactor(GameComponent_PsycastSynergies.Instance?.GetSpec(pawn));

        public static float SynergyFactor(SpecData d)
        {
            float f = 1f;
            if (d != null)
            {
                if (d.Owns("confluence")) f *= 2f;
                if (d.Owns("conduit")) f *= 1.5f;
                if (d.Owns("convergence")) f *= 1.15f;
            }
            return f;
        }

        public static bool OffensiveSourceDoubled(Pawn pawn)
            => GameComponent_PsycastSynergies.Instance?.Owns(pawn, "onslaught") ?? false;

        public static bool ConduitActive(Pawn pawn)
            => GameComponent_PsycastSynergies.Instance?.Owns(pawn, "conduit") ?? false;

        // Cost multiplier folding Flow / Abandon / Mastery into the level-based penalty.
        public static float CostFactor(Pawn pawn, AbilityDef def, float levelPenalty)
            => CostFactor(GameComponent_PsycastSynergies.Instance?.GetSpec(pawn), def, levelPenalty);

        public static float CostFactor(SpecData d, AbilityDef def, float levelPenalty)
        {
            if (d == null) return 1f + levelPenalty;
            if (d.Owns("mastery") && d.masteryDef == def) levelPenalty = 0f;
            if (d.Owns("flow")) levelPenalty *= 0.5f;
            float factor = 1f + levelPenalty;
            if (d.Owns("abandon")) factor += 0.40f;
            return factor;
        }

        public static int LevelCapBonus(Pawn pawn)
            => (GameComponent_PsycastSynergies.Instance?.Owns(pawn, "convergence") ?? false) ? 5 : 0;

        private static float EntropyFrac(Pawn pawn)
        {
            var e = pawn?.psychicEntropy;
            if (e == null) return 0f;
            float max = e.MaxEntropy;
            return max <= 0f ? 0f : Mathf.Clamp01(e.EntropyValue / max);
        }

        // The damage-type "element" of a cast, for Attunement matching. Def-stable, cached - this
        // rides StatBonus (per StatMultiplier call) whenever Attunement is owned.
        private static readonly Dictionary<AbilityDef, DamageDef> elementCache = new Dictionary<AbilityDef, DamageDef>();

        public static DamageDef ElementOf(AbilityDef def)
        {
            if (def == null) return null;
            if (elementCache.TryGetValue(def, out var cached)) return cached;
            DamageDef v = null;
            var expl = def.GetModExtension<AbilityExtension_Explosion>();
            if (expl?.explosionDamageDef != null) v = expl.explosionDamageDef;
            else
            {
                var proj = def.GetModExtension<AbilityExtension_Projectile>();
                if (proj?.projectile?.projectile != null) v = proj.projectile.projectile.damageDef;
            }
            elementCache[def] = v;
            return v;
        }
    }

    public static class SpecPoints
    {
        public static float XpPerPoint => PsycastSynergiesMod.Settings?.specXpPerPoint ?? 26f;

        public static void AddXp(SpecData d, float amount)
        {
            if (d == null || amount <= 0f) return;
            d.xp += amount;
            while (d.xp >= XpPerPoint) { d.xp -= XpPerPoint; d.points++; }
        }
    }
}

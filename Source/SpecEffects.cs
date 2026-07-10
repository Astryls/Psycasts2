#nullable disable
using UnityEngine;
using Verse;
using VEF.Abilities;
using VanillaPsycastsExpanded;
using AbilityDef = VEF.Abilities.AbilityDef;

namespace PsycastSynergies
{
    // Central application of specialization effects. SkillSystem / cost / cast hooks call into here.
    public static class SpecEffects
    {
        // Additive stat bonus (multiplier-space) from owned passive specs for (pawn, def, stat).
        public static float StatBonus(Pawn pawn, AbilityDef def, SynStat stat, Role role, SynStat? prim)
        {
            var d = GameComponent_PsycastSynergies.Instance?.GetSpec(pawn);
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
        {
            var d = GameComponent_PsycastSynergies.Instance?.GetSpec(pawn);
            if (d == null || !d.Owns("discipline") || d.disciplinePath == null) return;
            var ext = def.GetModExtension<AbilityExtension_Psycast>();
            if (ext?.path == d.disciplinePath) { perLevelMult *= 1.5f; extraLevels += 1f; }
        }

        public static float SynergyFactor(Pawn pawn)
        {
            var d = GameComponent_PsycastSynergies.Instance?.GetSpec(pawn);
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
        {
            var d = GameComponent_PsycastSynergies.Instance?.GetSpec(pawn);
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

        // The damage-type "element" of a cast, for Attunement matching.
        public static DamageDef ElementOf(AbilityDef def)
        {
            var expl = def.GetModExtension<AbilityExtension_Explosion>();
            if (expl?.explosionDamageDef != null) return expl.explosionDamageDef;
            var proj = def.GetModExtension<AbilityExtension_Projectile>();
            if (proj?.projectile?.projectile != null) return proj.projectile.projectile.damageDef;
            return null;
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

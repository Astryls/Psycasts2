#nullable disable
namespace PsycastSynergies
{
    // The synergy types a skill can scale. Each skill is assigned exactly ONE (its primary),
    // rarity-weighted, so builds must spread across several skills. (Targets/Charges are reserved
    // for an upcoming pass and not yet assignable.)
    public enum SynStat
    {
        // Combat / effect types
        Power, Radius, Duration, Strength, Range, Cooldown, Targets, Charges,
        // Universal "neutral" types (apply to any cast, so utility/mobility skills scale too)
        Efficiency, Haste, Yield, Insight,
        // Count of projectiles/meteorites a volley spawns (appended last to keep saved int values stable)
        ProjectileCount,
        // Count of minions a summon spawns
        SummonCount
    }

    // Combat archetype of a psycast, derived from what it does (PsycastInfo.RoleOf).
    public enum Role { Neutral, Offensive, Control, Boon }

    // Global, automatic "how role X amplifies role Y, and on which stat" table. This is what
    // makes offensive skills feed defensive ones (and vice-versa) without per-skill authoring.
    // Returns the TARGET stat a source role boosts on a target role, or null for no synergy.
    public static class SynergyRules
    {
        public static SynStat? Rule(Role src, Role tgt)
        {
            switch (src)
            {
                case Role.Offensive:
                    if (tgt == Role.Control) return SynStat.Duration;   // dmg → CC lasts longer
                    if (tgt == Role.Boon) return SynStat.Strength;      // dmg → tougher shields
                    if (tgt == Role.Offensive) return SynStat.Power;    // same-role reinforce
                    break;
                case Role.Control:
                    if (tgt == Role.Boon) return SynStat.Strength;      // CC → more absorption
                    if (tgt == Role.Offensive) return SynStat.Radius;   // CC → wider blasts
                    if (tgt == Role.Control) return SynStat.Radius;
                    break;
                case Role.Boon:
                    if (tgt == Role.Offensive) return SynStat.Power;    // defense → emboldened dmg
                    if (tgt == Role.Control) return SynStat.Radius;
                    if (tgt == Role.Boon) return SynStat.Strength;
                    break;
            }
            return null; // anything involving Neutral → no directed synergy
        }

        public static string StatLabel(SynStat st)
        {
            switch (st)
            {
                case SynStat.Power: return "Damage";
                case SynStat.Radius: return "Radius";
                case SynStat.Duration: return "Duration";
                case SynStat.Strength: return "Strength";
                case SynStat.Range: return "Range";
                case SynStat.Cooldown: return "Cooldown";
                case SynStat.Targets: return "Targets";
                case SynStat.Charges: return "Charges";
                case SynStat.Efficiency: return "Efficiency";
                case SynStat.Haste: return "Cast speed";
                case SynStat.Yield: return "Psyfocus yield";
                case SynStat.Insight: return "Insight";
                case SynStat.ProjectileCount: return "Projectiles";
                case SynStat.SummonCount: return "Summons";
                default: return "";
            }
        }

        public static string RoleLabel(Role r)
        {
            switch (r)
            {
                case Role.Offensive: return "OFFENSIVE";
                case Role.Control: return "CONTROL";
                case Role.Boon: return "BOON";
                default: return "UTILITY";
            }
        }
    }
}

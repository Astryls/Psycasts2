#nullable disable
using HarmonyLib;
using RimWorld.Planet;
using Verse;
using Ability = VEF.Abilities.Ability;

namespace PsycastSynergies
{
    // Targeted compat for addon abilities whose effect MAGNITUDE bypasses our cast-time def-field
    // scaling. Every patch is guarded by type existence (AccessTools.TypeByName), so it silently
    // no-ops when that addon isn't installed. __instance is typed as the VEF base Ability, so we
    // never need a compile-time reference to the addon assemblies.
    public static class AddonCompat
    {
        public static void Install(Harmony h)
        {
            // ── Gravcaster #2 (projectile count) ──────────────────────────────────────────────
            // Summon Meteorites' drop-chance is clamped (<=0.2) so meteorite count saturates. Scale
            // the clamped getter by the caster's Projectile Count multiplier so count grows with the
            // skill's level + its guaranteed Projectile-Count synergy.
            ScaleGetter(h, "VPE_Gravmancer.Ability_SummonMeteorites", "dropChance", nameof(ScaleByProjectileCount));

            // ── Luminis #1 (beam) ─────────────────────────────────────────────────────────────
            // Sun Beam deals flame damage = casterSensitivity*100 every tick after Cast returns.
            // Open a post-cast sensitivity-amplification window on the caster for the beam's duration.
            AmplifyOnCast(h, "VPE_Luminis.Ability_SunBeam");
        }

        private static void ScaleGetter(Harmony h, string typeName, string prop, string postfixName)
        {
            var t = AccessTools.TypeByName(typeName);
            var g = t == null ? null : AccessTools.PropertyGetter(t, prop);
            if (g == null) return;
            h.Patch(g, postfix: new HarmonyMethod(typeof(AddonCompat), postfixName));
        }

        public static void ScaleByProjectileCount(Ability __instance, ref float __result)
        {
            var pawn = __instance?.pawn;
            if (pawn == null || __instance.def == null) return;
            __result *= SkillSystem.StatMultiplier(pawn, __instance.def, SynStat.ProjectileCount);
        }

        // Patch an ability's Cast to open a sensitivity-amplification window on the caster lasting
        // (generously) the cast's duration, so its post-cast sensitivity-derived damage scales.
        private static void AmplifyOnCast(Harmony h, string typeName)
        {
            var t = AccessTools.TypeByName(typeName);
            var m = t == null ? null : AccessTools.Method(t, "Cast", new[] { typeof(GlobalTargetInfo[]) });
            if (m == null) return;
            h.Patch(m, postfix: new HarmonyMethod(typeof(AddonCompat), nameof(OpenAmpAfterCast)));
        }

        public static void OpenAmpAfterCast(Ability __instance)
        {
            var pawn = __instance?.pawn;
            if (pawn == null || __instance.def == null) return;
            float f = SkillSystem.SelfMultiplier(pawn, __instance.def);
            if (f <= 1.0001f) return;
            int baseDur = __instance.def.durationTime > 0 ? __instance.def.durationTime : 300;
            CastScaling.OpenAmplify(pawn, f, baseDur * 2 + 300);
        }
    }
}

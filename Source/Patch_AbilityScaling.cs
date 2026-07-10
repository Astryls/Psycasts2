#nullable disable
using HarmonyLib;
using UnityEngine;
using Verse;
using VEF.Abilities;
using VanillaPsycastsExpanded;

namespace PsycastSynergies
{
    // VEF's Ability funnels every cast's numbers through these virtual getters, so a
    // postfix on the base class scales power/radius/duration for ALL VPE psycasts and
    // any mod built on the VPE/VEF ability framework - no per-ability XML required.

    [HarmonyPatch(typeof(Ability), nameof(Ability.GetPowerForPawn))]
    public static class Patch_GetPowerForPawn
    {
        static void Postfix(Ability __instance, ref float __result)
        {
            if (CastScaling.Active || !PsycastSynergiesMod.Settings.scalePower || __result == 0f) return;
            __result *= SkillSystem.StatMultiplier(__instance.pawn, __instance.def, SynStat.Power);
        }
    }

    [HarmonyPatch(typeof(Ability), nameof(Ability.GetRadiusForPawn))]
    public static class Patch_GetRadiusForPawn
    {
        static void Postfix(Ability __instance, ref float __result)
        {
            if (CastScaling.Active || !PsycastSynergiesMod.Settings.scaleRadius || __result <= 0f) return;
            __result *= SkillSystem.StatMultiplier(__instance.pawn, __instance.def, SynStat.Radius);
        }
    }

    // Range synergy type: +range per level (for skills that rolled Range).
    [HarmonyPatch(typeof(Ability), nameof(Ability.GetRangeForPawn))]
    public static class Patch_GetRangeForPawn
    {
        static void Postfix(Ability __instance, ref float __result)
        {
            if (CastScaling.Active || __result <= 0f) return;
            __result *= SkillSystem.StatMultiplier(__instance.pawn, __instance.def, SynStat.Range);
        }
    }

    // Cooldown synergy type: -cooldown per level. (Not gated on CastScaling.Active because the
    // cooldown is computed during the cast; def.cooldownTime is NOT mutated to avoid double-dipping.)
    [HarmonyPatch(typeof(Ability), nameof(Ability.GetCooldownForPawn))]
    public static class Patch_GetCooldownForPawn
    {
        // Hard clamp: a psycast's cooldown can never be driven below 40% of its base (so it's never
        // effectively zero). Skip/blink-type mobility casts are exempt and may stay near-instant.
        public static bool IsSkipBlink(AbilityDef def)
        {
            string n = def?.defName;
            if (string.IsNullOrEmpty(n)) return false;
            return n.IndexOf("skip", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("blink", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void Postfix(Ability __instance, ref int __result)
        {
            if (__result <= 0) return;
            var pawn = __instance.pawn;
            bool exempt = IsSkipBlink(__instance.def);
            float m = SkillSystem.StatMultiplier(pawn, __instance.def, SynStat.Cooldown);
            if (m > 1.0001f)
            {
                int baseCd = __result;
                float reduce = Mathf.Min(m - 1f, exempt ? 0.95f : 0.6f);
                int scaled = Mathf.RoundToInt(baseCd * (1f - reduce));
                int floor = exempt ? 1 : Mathf.Max(1, Mathf.RoundToInt(baseCd * 0.4f));
                __result = Mathf.Max(floor, scaled);
            }
            // Enemy psycaster aggression: tiered hostile psycasters re-cast faster (-12% cooldown per tier).
            if (pawn?.Faction != null && !pawn.Faction.IsPlayer)
            {
                int tier = EnlightenmentTier.TierOf(pawn);
                if (tier > 0)
                {
                    int fl = exempt ? 1 : Mathf.Max(1, Mathf.RoundToInt(__result * 0.3f));
                    __result = Mathf.Max(fl, Mathf.RoundToInt(__result * (1f - 0.12f * tier)));
                }
            }
        }
    }

    // Cast time: Catalyst spec (25% faster) + Haste synergy type (-cast time per level).
    [HarmonyPatch(typeof(Ability), nameof(Ability.GetCastTimeForPawn))]
    public static class Patch_GetCastTimeForPawn
    {
        static void Postfix(Ability __instance, ref int __result)
        {
            if (__result <= 0 || __instance.pawn == null) return;
            float mult = 1f;
            if (GameComponent_PsycastSynergies.Instance?.Owns(__instance.pawn, "catalyst") == true) mult *= 0.75f;
            float hasteRed = SkillSystem.StatMultiplier(__instance.pawn, __instance.def, SynStat.Haste) - 1f;
            if (hasteRed > 0f) mult *= 1f - Mathf.Min(hasteRed, 0.7f);
            if (mult < 0.9999f) __result = Mathf.Max(1, Mathf.RoundToInt(__result * mult));
        }
    }

    [HarmonyPatch(typeof(Ability), nameof(Ability.GetDurationForPawn))]
    public static class Patch_GetDurationForPawn
    {
        static void Postfix(Ability __instance, ref int __result)
        {
            if (CastScaling.Active || !PsycastSynergiesMod.Settings.scaleDuration || __result <= 0) return;
            __result = Mathf.RoundToInt(__result * SkillSystem.StatMultiplier(__instance.pawn, __instance.def, SynStat.Duration));
        }
    }

    // Cost side of the tradeoff: leveling a skill raises its psyfocus cost and entropy
    // (neural heat). VPE computes both on AbilityExtension_Psycast; __instance.abilityDef
    // (set in AbilityDef.ResolveReferences) gives the def to look the level up by.
    [HarmonyPatch(typeof(AbilityExtension_Psycast), nameof(AbilityExtension_Psycast.GetPsyfocusUsedByPawn))]
    public static class Patch_PsyfocusCost
    {
        static void Postfix(AbilityExtension_Psycast __instance, Pawn pawn, ref float __result)
        {
            if (__result <= 0f) return;
            float baseCost = __result;
            __result *= SkillSystem.CostMultiplier(pawn, __instance.abilityDef);
            __result = Mathf.Max(__result, baseCost * 0.2f);                    // never ~0 (charges still refund post-cast)
            // Never let our scaling raise psyfocus cost past what a full pool can pay, so leveling
            // can't render a castable psycast uncastable (the gizmo gates on this).
            __result = Mathf.Min(__result, Mathf.Max(baseCost, 0.9f));
        }
    }

    [HarmonyPatch(typeof(AbilityExtension_Psycast), nameof(AbilityExtension_Psycast.GetEntropyUsedByPawn))]
    public static class Patch_EntropyGain
    {
        static void Postfix(AbilityExtension_Psycast __instance, Pawn pawn, ref float __result)
        {
            if (__result <= 0f) return;
            float baseCost = __result;
            __result *= SkillSystem.CostMultiplier(pawn, __instance.abilityDef);
            __result = Mathf.Max(__result, baseCost * 0.2f);
            // If the BASE neural-heat cost fit under the pawn's limit, keep the scaled value castable
            // too (don't let leveling push it into permanent "would exceed neural heat" lockout).
            float limit = pawn?.psychicEntropy?.MaxEntropy ?? 0f;
            if (limit > 0f && baseCost <= limit) __result = Mathf.Min(__result, limit * 0.95f);
        }
    }

    // Explosion casts read radius + damage straight from AbilityExtension_Explosion (NOT the
    // GetRadiusForPawn/GetPowerForPawn getters), so we scale those fields around the cast by
    // temporarily mutating the (shared, but cast is synchronous + main-thread) extension.
    public struct ExplodeState { public float radius; public int amount; public bool active; }

    [HarmonyPatch(typeof(Ability_Explode), nameof(Ability_Explode.Cast))]
    public static class Patch_ExplodeScaling
    {
        static void Prefix(Ability_Explode __instance, out ExplodeState __state)
        {
            __state = default;
            var s = PsycastSynergiesMod.Settings;
            var ext = ((Verse.Def)__instance.def).GetModExtension<AbilityExtension_Explosion>();
            if (ext == null) return;
            float radMult = SkillSystem.StatMultiplier(__instance.pawn, __instance.def, SynStat.Radius);
            float dmgMult = SkillSystem.StatMultiplier(__instance.pawn, __instance.def, SynStat.Power);
            if (radMult <= 1.0001f && dmgMult <= 1.0001f) return;

            __state = new ExplodeState { radius = ext.explosionRadius, amount = ext.explosionDamageAmount, active = true };
            if (s.scaleRadius && ext.explosionRadius > 0f)
                ext.explosionRadius *= radMult;
            if (s.scalePower)
            {
                int baseAmt = ext.explosionDamageAmount >= 0
                    ? ext.explosionDamageAmount
                    : (ext.explosionDamageDef != null ? ext.explosionDamageDef.defaultDamage : -1);
                if (baseAmt > 0)
                    ext.explosionDamageAmount = Mathf.RoundToInt(baseAmt * dmgMult);
            }
        }

        static void Postfix(Ability_Explode __instance, ExplodeState __state)
        {
            if (!__state.active) return;
            var ext = ((Verse.Def)__instance.def).GetModExtension<AbilityExtension_Explosion>();
            if (ext == null) return;
            ext.explosionRadius = __state.radius;
            ext.explosionDamageAmount = __state.amount;
        }
    }

    // Buff/boon magnitude: scale the severity of hediffs a cast applies (when an explicit
    // severity is given). Duration of those hediffs already scales via GetDurationForPawn.
    [HarmonyPatch(typeof(Ability), nameof(Ability.ApplyHediff),
        new[] { typeof(Pawn), typeof(HediffDef), typeof(BodyPartRecord), typeof(int), typeof(float) })]
    public static class Patch_BuffSeverity
    {
        static void Prefix(Ability __instance, ref float severity)
        {
            if (!PsycastSynergiesMod.Settings.scaleBuffStrength || severity <= 0.0001f) return;
            severity *= SkillSystem.StatMultiplier(__instance.pawn, __instance.def, SynStat.Strength);
        }
    }
}

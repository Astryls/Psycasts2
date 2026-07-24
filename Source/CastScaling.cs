#nullable disable
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using VEF.Abilities;
using VanillaPsycastsExpanded;
using AbilityDef = VEF.Abilities.AbilityDef;
using Ability = VEF.Abilities.Ability;

namespace PsycastSynergies
{
    // Many ability classes (vanilla VPE and addons like Aeromancer's VPE_Cyclone.Ability_AOEWindPush)
    // read def.radius / def.power / def.durationTime DIRECTLY at cast time instead of going through
    // GetRadiusForPawn / GetPowerForPawn / GetDurationForPawn - so our getter postfixes never fire.
    //
    // Fix: temporarily scale those def fields in-place for the duration of the cast (def is shared,
    // but casts are synchronous + main-thread), restoring afterward. We patch EVERY Ability.Cast
    // override (and the base) so the scaled values are live no matter how the class reads them.
    // The getters skip their own scaling while this window is active so nothing double-applies.
    public static class CastScaling
    {
        private static int depth;
        private static AbilityDef scaledDef;
        private static Ability scaledAbility;
        private static float origRadius, origPower, origRange;
        private static int origDuration;
        private static float preEntropy, prePsyfocus;   // pre-cast cost snapshot (for free charge casts)

        public static bool Active => depth > 0;

        // While a cast is active, the caster's Psychic Sensitivity is boosted by SensFactor so
        // sensitivity-derived effects (addons that hardcode magnitude as X*sensitivity) scale too.
        public static Pawn SensPawn;
        public static float SensFactor = 1f;

        // Post-cast amplification windows: some addon effects (beams) read the caster's Psychic
        // Sensitivity every tick AFTER the cast returns. Opening a window keeps the boost alive for
        // the effect's lifetime so its per-tick magnitude scales too.
        private struct AmpWin { public float factor; public int expireTick; }
        private static readonly Dictionary<Pawn, AmpWin> ampWindows = new Dictionary<Pawn, AmpWin>();

        public static void OpenAmplify(Pawn p, float factor, int ticks)
        {
            if (p == null || factor <= 1.0001f) return;
            int exp = Find.TickManager.TicksGame + ticks;
            if (ampWindows.TryGetValue(p, out var cur) && cur.expireTick >= exp && cur.factor >= factor) return;
            ampWindows[p] = new AmpWin { factor = factor, expireTick = exp };
        }

        public static float AmplifyFactor(Pawn p)
        {
            if (p == null || ampWindows.Count == 0) return 1f;
            if (ampWindows.TryGetValue(p, out var w))
            {
                if (Find.TickManager.TicksGame <= w.expireTick) return w.factor;
                ampWindows.Remove(p);
            }
            return 1f;
        }

        // Periodic cleanup (GameComponentTick, every 120 ticks): AmplifyFactor only evicts an
        // expired entry when that pawn's sensitivity is read again - a pawn that dies (or never
        // gets probed) would park its entry forever.
        // Cross-game hygiene (FinalizeInit): static state must not carry pawns between sessions.
        public static void ClearAmplifyWindows() => ampWindows.Clear();

        private static readonly List<Pawn> ampSweep = new List<Pawn>();
        public static void SweepExpired(int now)
        {
            if (ampWindows.Count == 0) return;
            ampSweep.Clear();
            foreach (var kv in ampWindows) if (now > kv.Value.expireTick) ampSweep.Add(kv.Key);
            for (int i = 0; i < ampSweep.Count; i++) ampWindows.Remove(ampSweep[i]);
        }

        private static void Begin(Ability ab)
        {
            depth++;
            if (depth > 1) return; // nested cast - already scaled (or a different def we leave alone)
            scaledDef = null;
            scaledAbility = ab;
            try
            {
                var def = ab?.def;
                var pawn = ab?.pawn;
                if (def == null || pawn == null) return;

                var s = PsycastSynergiesMod.Settings;
                if (s == null) return;

                preEntropy = pawn.psychicEntropy?.EntropyValue ?? 0f;
                prePsyfocus = pawn.psychicEntropy?.CurrentPsyfocus ?? 0f;

                scaledDef = def;
                origRadius = def.radius; origPower = def.power; origDuration = def.durationTime; origRange = def.range;

                if (s.scaleRadius && def.radius > 0f)
                    def.radius = origRadius * SkillSystem.StatMultiplier(pawn, def, SynStat.Radius);
                if (s.scalePower && def.power != 0f)
                    def.power = origPower * SkillSystem.StatMultiplier(pawn, def, SynStat.Power);
                if (def.range > 0f)
                    def.range = origRange * SkillSystem.StatMultiplier(pawn, def, SynStat.Range);
                if (s.scaleDuration && def.durationTime > 0)
                    def.durationTime = Mathf.RoundToInt(origDuration * SkillSystem.StatMultiplier(pawn, def, SynStat.Duration));

                if (s.scaleViaSensitivity)
                {
                    var prim = PsycastInfo.PrimaryStat(def);
                    SensFactor = prim.HasValue ? SkillSystem.StatMultiplier(pawn, def, prim.Value) : 1f;
                    SensPawn = SensFactor > 1.0001f ? pawn : null;
                }
            }
            catch { scaledDef = null; SensPawn = null; SensFactor = 1f; }
        }

        private static void End()
        {
            depth--;
            if (depth > 0) return;
            if (depth < 0) depth = 0;
            if (scaledDef != null)
            {
                scaledDef.radius = origRadius;
                scaledDef.power = origPower;
                scaledDef.durationTime = origDuration;
                scaledDef.range = origRange;
                scaledDef = null;
            }
            SensPawn = null;
            SensFactor = 1f;
            try { PostCast(scaledAbility); } catch { }
            try { Patch_MultiTarget.RestorePending(); } catch { }
            scaledAbility = null;
        }

        // Per-cast specialization bookkeeping: accrue spec XP, stamp the cast time + cost (for kill
        // refund), and roll Echo's cooldown refresh.
        private static void PostCast(Ability ab)
        {
            if (ab?.pawn == null || ab.def == null) return;
            var gc = GameComponent_PsycastSynergies.Instance;
            if (gc == null) return;
            var d = gc.GetSpec(ab.pawn, create: true);

            d.lastCastTick = Find.TickManager.TicksGame;
            var psy = ((Verse.Def)ab.def).GetModExtension<AbilityExtension_Psycast>();
            float entropy = 0f, focus = 0f, weight = 1f;
            if (psy != null)
            {
                entropy = psy.GetEntropyUsedByPawn(ab.pawn);
                focus = psy.GetPsyfocusUsedByPawn(ab.pawn);
                weight = 1f + entropy * 0.04f + focus * 15f;
            }
            d.lastEntropy = entropy;
            d.lastFocus = focus;
            SpecPoints.AddXp(d, weight);

            if (d.Owns("echo") && Rand.Chance(0.20f))
                ab.cooldown = Find.TickManager.TicksGame;

            // Charges: a cast while >1 charge remains is FREE - no neural heat, no psyfocus, no
            // cooldown (4→3, 3→2, 2→1). The 1→0 cast pays full cost and starts the normal cooldown;
            // at 0 charges it's a normal cast until charges regenerate.
            bool freeCast = false;
            int curCharges = ChargeStore.Current(ab.pawn, ab.def);
            if (curCharges >= 1)
            {
                ChargeStore.Consume(ab.pawn, ab.def);
                if (curCharges >= 2)
                {
                    freeCast = true;
                    ab.cooldown = Find.TickManager.TicksGame;
                    var pe = ab.pawn.psychicEntropy;
                    if (pe != null)
                    {
                        float dF = prePsyfocus - pe.CurrentPsyfocus;   // refund spent psyfocus
                        if (dF > 0f) pe.OffsetPsyfocusDirectly(dF);
                        float dE = pe.EntropyValue - preEntropy;        // remove gained neural heat
                        if (dE > 0f) pe.TryAddEntropy(-dE, null, false, true);
                    }
                }
            }

            // Yield: refund psyfocus (skipped on a free cast - already fully refunded above).
            float yieldFrac = Mathf.Min(SkillSystem.StatMultiplier(ab.pawn, ab.def, SynStat.Yield) - 1f, 0.9f);
            if (!freeCast && yieldFrac > 0f && focus > 0f) ab.pawn.psychicEntropy?.OffsetPsyfocusDirectly(focus * yieldFrac);

            // Casting grants psycast XP scaled by the skill's tier (the primary XP source).
            XpSystem.GrantCastXp(ab.pawn, psy?.level ?? 1);
        }

        // Harmony prefix/postfix bodies (manually attached to every Cast override).
        public static void CastPrefix(Ability __instance) => Begin(__instance);
        public static void CastPostfix() => End();

        public static void Install(Harmony harmony)
        {
            var prefix = new HarmonyMethod(typeof(CastScaling), nameof(CastPrefix));
            var postfix = new HarmonyMethod(typeof(CastScaling), nameof(CastPostfix));
            var pt = new[] { typeof(GlobalTargetInfo[]) };

            // Base Ability.Cast (covers subclasses that don't override it).
            TryPatch(harmony, AccessTools.Method(typeof(Ability), "Cast", pt), prefix, postfix);

            // Every concrete subclass that DECLARES its own Cast override.
            foreach (var t in typeof(Ability).AllSubclassesNonAbstract())
            {
                var m = AccessTools.Method(t, "Cast", pt);
                if (m == null || m.DeclaringType != t) continue;
                TryPatch(harmony, m, prefix, postfix);
            }
        }

        private static void TryPatch(Harmony h, System.Reflection.MethodInfo m, HarmonyMethod pre, HarmonyMethod post)
        {
            if (m == null) return;
            try { h.Patch(m, pre, post); }
            catch (Exception e) { Log.Warning("[Psycasts²] Could not patch Cast on " + m.DeclaringType + ": " + e.Message); }
        }
    }

    // Boost the caster's Psychic Sensitivity during their own cast so sensitivity-derived effects
    // (Geomancer's stun = 120*sensitivity, ChunkBurst damage = 10*sensitivity, etc.) scale too.
    [HarmonyPatch(typeof(StatExtension), nameof(StatExtension.GetStatValue),
        new[] { typeof(Thing), typeof(StatDef), typeof(bool), typeof(int) })]
    public static class Patch_SensitivityDuringCast
    {
        static void Postfix(Thing thing, StatDef stat, ref float __result)
        {
            if (stat != StatDefOf.PsychicSensitivity) return;
            // During the caster's own cast.
            if (CastScaling.Active && thing == CastScaling.SensPawn) { __result *= CastScaling.SensFactor; return; }
            // Post-cast amplification window (e.g. a channeled beam that ticks damage = sensitivity*K).
            if (thing is Pawn p)
            {
                float f = CastScaling.AmplifyFactor(p);
                if (f > 1.0001f) __result *= f;
            }
        }
    }
}

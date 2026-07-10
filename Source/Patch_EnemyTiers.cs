#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using VanillaPsycastsExpanded;
using VEF.Abilities;

namespace PsycastSynergies
{
    // Hostile psycasters (especially the Empire) can spawn Enlightened: a tier + boosted psylink,
    // extra schools/abilities, full psyfocus (and faster psycasting via Patch_GetCooldownForPawn). Runs
    // AFTER VEF/VPE's own psycaster generation (Priority.Last) so the pawn already has its base implant.
    [HarmonyPatch(typeof(PawnGenerator), "GenerateNewPawnInternal")]
    public static class Patch_EnemyTiers
    {
        private static HediffDef implantDef;
        private static readonly string[] AscensionHediffs =
            { "PS_Halcyon", "PS_Ascendant", "PS_Penumbra", "PS_Empyrean", "PS_Pandemonium", "PS_Chronos", "PS_Consonance" };

        [HarmonyPriority(Priority.Last)]
        static void Postfix(Pawn __result, PawnGenerationRequest request)
        {
            try
            {
                var p = __result;
                if (p == null || p.Dead || p.RaceProps == null || !p.RaceProps.Humanlike) return;
                var s = PsycastSynergiesMod.Settings;
                if (s == null) return;
                if (s.enemyTiersEnabled) TryInjectTier(p, request, s);   // hostile psycaster -> chance of an Awakened+ tier
                if (s.gateUntieredPsylinks) GatePsylink(p);              // strip a psylink from any pawn left untiered
            }
            catch (Exception e) { Log.Warning("[Psycasts²] pawn-gen psycast handling failed: " + e); }
        }

        // Hostile psycaster -> chance of a tier upgrade (extra schools, levels, full psyfocus).
        private static void TryInjectTier(Pawn p, PawnGenerationRequest request, PsycastSynergiesSettings s)
        {
            var fac = p.Faction ?? request.Faction;
            if (fac == null || fac.IsPlayer) return;
            if (EnlightenmentTier.TierOf(p) > 0) return;   // already tiered

            if (implantDef == null) implantDef = DefDatabase<HediffDef>.GetNamedSilentFail("VPE_PsycastAbilityImplant");
            if (implantDef == null) return;
            var implant = p.health?.hediffSet?.GetFirstHediffOfDef(implantDef) as Hediff_PsycastAbilities;
            if (implant == null) return;   // not a psycaster

            bool empire = fac.def == FactionDefOf.Empire;
            float chance = s.enemyTierFreq * (empire ? 1.4f : 0.6f);
            if (!Rand.Chance(chance)) return;

            int tier = RollTier(s);
            if (tier == 0) return;
            ApplyTier(p, implant, tier, s);
        }

        // Psycasters are EARNED: a pawn generated with a psylink but no Awakened+ tier loses it. Tiered pawns
        // (including injected enemies) keep theirs, and the scripted Ancient Psycaster Order is exempt.
        private static void GatePsylink(Pawn p)
        {
            if (EnlightenmentTier.TierOf(p) >= 1) return;                                       // already Awakened+ (incl. injected enemies)
            if (p.Faction != null && p.Faction.def?.defName == "PS_AncientPsycasters") return;  // scripted psycaster order
            var hs = p.health?.hediffSet;
            if (hs == null) return;
            var s = PsycastSynergiesMod.Settings;

            // Any eligible pawn (age 13+) has a chance to spawn as a fresh Awakened psycaster - created from
            // scratch if it has no psylink yet (a psylink + a path + a tier), mostly Tier I, occasionally higher.
            if (s != null && s.awakenedSpawnChance > 0f
                && (p.ageTracker == null || p.ageTracker.AgeBiologicalYears >= 13f)
                && Rand.Chance(s.awakenedSpawnChance))
            {
                var impl = MeditationSystem.EnsurePsycaster(p);
                if (impl != null) { ApplyTier(p, impl, RollSpawnTier(), s); return; }
            }

            // Otherwise, suppress any psylink the pawn generated with but did not earn.
            if (implantDef == null) implantDef = DefDatabase<HediffDef>.GetNamedSilentFail("VPE_PsycastAbilityImplant");
            var implant = implantDef != null ? hs.GetFirstHediffOfDef(implantDef) as Hediff_PsycastAbilities : null;
            var psylink = p.GetMainPsylinkSource();
            if (implant == null && psylink == null) return;   // nothing to suppress
            MeditationSystem.internalPsylinkChange = true;
            try
            {
                if (implant != null) p.health.RemoveHediff(implant);
                foreach (var h in hs.hediffs.Where(x => x is Hediff_Psylink).ToList())
                    p.health.RemoveHediff(h);
            }
            finally { MeditationSystem.internalPsylinkChange = false; }
        }

        // A randomly-spawned Awakened psycaster: mostly Tier I, occasionally higher.
        private static int RollSpawnTier()
        {
            float r = Rand.Value;
            return r < 0.7f ? 1 : (r < 0.92f ? 2 : 3);
        }

        // Weighted: Tier I common, II uncommon, III rare. Only enabled tiers are eligible.
        private static int RollTier(PsycastSynergiesSettings s)
        {
            var opts = new List<KeyValuePair<int, float>>();
            if (s.enemyTier1) opts.Add(new KeyValuePair<int, float>(1, 6f));
            if (s.enemyTier2) opts.Add(new KeyValuePair<int, float>(2, 3f));
            if (s.enemyTier3) opts.Add(new KeyValuePair<int, float>(3, 1f));
            if (opts.Count == 0) return 0;
            float total = 0f; foreach (var o in opts) total += o.Value;
            float r = Rand.Value * total;
            foreach (var o in opts) { if (r < o.Value) return o.Key; r -= o.Value; }
            return opts[opts.Count - 1].Key;
        }

        private static void ApplyTier(Pawn p, Hediff_PsycastAbilities implant, int tier, PsycastSynergiesSettings s)
        {
            EnlightenmentTier.SetTier(p, tier, false);   // no player spec-point rewards

            int extraLevels = tier == 1 ? 3 : tier == 2 ? 7 : 12;
            try { implant.ChangeLevel(extraLevels); } catch { }

            GrantPaths(p, implant, p.GetComp<CompAbilities>(), tier);   // 1 / 2 / 3 extra schools

            // Open with a full psyfocus pool so they actually cast right away.
            try { p.psychicEntropy?.OffsetPsyfocusDirectly(1f); } catch { }

            if (tier == 3 && s.enemyAscension) GrantAscension(p);
        }

        internal static void GrantPaths(Pawn p, Hediff_PsycastAbilities implant, CompAbilities comp, int count)
        {
            if (comp == null) { Log.Warning("[Psycasts²] enemy tier: no CompAbilities on " + p?.LabelShortCap); return; }
            var avail = DefDatabase<PsycasterPathDef>.AllDefs
                .Where(pp => pp.HasAbilities && (implant.unlockedPaths == null || !implant.unlockedPaths.Contains(pp)))
                .ToList();
            int granted = 0;
            for (int i = 0; i < count && avail.Count > 0; i++)
            {
                var path = avail.RandomElement(); avail.Remove(path);
                try { implant.UnlockPath(path); } catch { continue; }
                int budget = Rand.RangeInclusive(3, 5);
                // Grant in tier order (low first) so each ability's prerequisites are already met.
                if (path.abilityLevelsInOrder != null)
                    foreach (var arr in path.abilityLevelsInOrder)
                    {
                        if (budget <= 0) break;
                        if (arr == null) continue;
                        foreach (var ab in arr)
                        {
                            if (budget <= 0) break;
                            if (ab == null || ab == PsycasterPathDef.Blank) continue;
                            if (comp.LearnedAbilities.Any(x => x.def == ab)) continue;
                            try { comp.GiveAbility(ab); budget--; granted++; } catch { }
                        }
                    }
            }
            if (Prefs.DevMode)
                Log.Message("[Psycasts²] enemy tier: +" + count + " path(s), +" + granted
                    + " abilities \u2192 " + p?.LabelShortCap + " now has " + comp.LearnedAbilities.Count + " psycasts.");
        }

        internal static void GrantAscension(Pawn p)
        {
            if (p.health == null) return;
            var hd = DefDatabase<HediffDef>.GetNamedSilentFail(AscensionHediffs.RandomElement());
            if (hd == null || p.health.hediffSet.HasHediff(hd)) return;
            try { p.health.AddHediff(hd, p.health.hediffSet.GetBrain()); } catch { }
        }
    }
}

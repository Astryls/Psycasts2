#nullable disable
using System.Reflection;
using HarmonyLib;
using Verse;
using VanillaPsycastsExpanded;

namespace PsycastSynergies
{
    // Patch from our OWN [StaticConstructorOnStartup] static ctor (NOT the Mod ctor):
    // VPE's PsycastsUIUtility is [StaticConstructorOnStartup], and patching it from the
    // earlier Mod/CreateModClasses phase forces its cctor to run too early, which makes
    // its meditationIcons cache incomplete and crashes the psycast tab. The static-ctor
    // phase is the same late phase VPE/Modern Psycasts UI patch in, so timing lines up.
    [StaticConstructorOnStartup]
    public static class HarmonyInit
    {
        static HarmonyInit()
        {
            var harmony = new Harmony("astryl.psycastsynergies");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            // Soft-wire Modern Psycasts UI's focus-tile hooks (no-op when that mod is absent):
            // its focus-type tiles become the per-pawn default-focus picker.
            ModernUIBridge.TryWire();

            // Meditation bars in Modern Psycasts UI's left panel (reflection-only; no-op when absent).
            Patch_MeditationBars.TryWireModernUI(harmony);

            // Hide-unlearned-paths filtering of Modern Psycasts UI's tree list (reflection-only).
            Patch_HideUnlearnedPaths.TryWireModernUI(harmony);

            // "Undiscovered" instead of "Locked" on Modern Psycasts UI's locked tree tiles (reflection-only).
            Patch_LockedNoColon.TryWireModernUI(harmony);

            // Psyset-editor click scope for Modern Psycasts UI's embedded editor (reflection-only), so
            // clicking a psycast there adds it to the set instead of hitting our invest button.
            Patch_PsysetScope.TryWireModernUI(harmony);

            // Dynamically wrap every Ability.Cast override so def-field scaling reaches custom
            // ability classes that read def.radius/power/durationTime directly (addon paths).
            CastScaling.Install(harmony);

            // Targeted scaling for addon effects that bypass def-field scaling (beam damage,
            // clamped meteorite count, etc.). Guarded per-type, so uninstalled addons no-op.
            AddonCompat.Install(harmony);

            // VPE's Mod ctor has already run (CreateModClasses precedes static ctors),
            // so PsycastsMod.Settings is populated - apply our level-cap override now.
            PsycastSynergiesMod.ApplyVpeLevelCap();

            // (B) Defs are loaded at static-ctor time - freeze the synergy graph so it never
            // reshuffles across launches/versions/path changes.
            PsycastInfo.EnsureFrozen();

            // Hand-tuned balance overlay (primary + per-edge stat/strength overrides) layered on top
            // of the auto graph. Loaded from <modRoot>/ManualBalance.json if present.
            ManualBalance.Load();

            // Pre-warm the per-def synergy caches now (EnsureFrozen clears them) so the first
            // psycast-tab open / tooltip hover doesn't pay the warm-up cost as a frame hitch.
            PsycastInfo.WarmCaches();

            // "Disable gene requirements": strip requiredGene from every psycaster path at the SOURCE,
            // so the gate is gone from the unlock check. ALSO clear the gene-flavored lockedReason - VPE's
            // tab draws "Locked: " + lockedReason whenever CanPawnUnlock is false (which lockPathsToEnlightenment
            // forces for every path), so Hemosage's "Hemogenic only" text would otherwise still show.
            if (PsycastSynergiesMod.Settings?.disableGeneRequirements == true)
            {
                int cleared = 0;
                foreach (var path in DefDatabase<PsycasterPathDef>.AllDefs)
                    if (path.requiredGene != null)
                    {
                        path.requiredGene = null;
                        path.lockedReason = null;
                        path.ensureLockRequirement = false;
                        cleared++;
                    }
                Log.Message("[Psycasts²] disableGeneRequirements: cleared the gene gate on " + cleared + " path(s).");
            }

            // Compat with our lockPathsToEnlightenment scheme: that forces PsycasterPathDef.CanPawnUnlock
            // to return false for player pawns (so paths unlock ONLY via the awakening cards). VPE's
            // AbilityExtension_Psycast.IsEnabledForPawn ALSO gates ability USE on CanPawnUnlock - but only
            // for paths whose ignoreLockRestrictionsForNeurotrainers is false. Almost every VPE path leaves
            // that true (so their learned abilities stay castable), but a few (e.g. Hemosage) set it false,
            // which made their LEARNED abilities show "Disabled:" with an empty reason (we'd also nulled
            // lockedReason). Force it true on every path so a learned ability is never disabled by our
            // card-based unlock gate. The tab's Unlock buttons stay blocked via CanPawnUnlock itself.
            if (PsycastSynergiesMod.Settings?.lockPathsToEnlightenment == true)
            {
                int freed = 0;
                foreach (var path in DefDatabase<PsycasterPathDef>.AllDefs)
                    if (!path.ignoreLockRestrictionsForNeurotrainers) { path.ignoreLockRestrictionsForNeurotrainers = true; freed++; }
                if (freed > 0)
                    Log.Message("[Psycasts²] lockPathsToEnlightenment: freed ability-use on " + freed + " lock-restricted path(s) (e.g. Hemosage).");
            }

            // Dev: write the HTML skill compendium + the icon-rich field manual. The manual needs
            // ability ICONS, which load in a deferred LongEvent after PostLoad - generate it after
            // that finishes (and after Specs' own static ctor) so every icon is actually loaded.
            // (The old HTML/JSON balance-editor export is gone - tuning now lives in-game:
            // psycast tab -> dev mode -> Balance editor.)
            try { SkillReport.Generate(); } catch { }
            LongEventHandler.ExecuteWhenFinished(delegate { try { ManualReport.Generate(); } catch { } });
        }
    }
}

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

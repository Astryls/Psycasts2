#nullable disable
using System;
using RimWorld;
using Verse;

namespace PsycastSynergies
{
    // ISEKAI RPG Leveling (JellyCreative.IsekaiLeveling) adds StatParts (namespace IsekaiLeveling.*) to the
    // psycaster stats - PsychicSensitivity, PsychicEntropyMax, PsychicEntropyRecoveryRate, MeditationFocusGain,
    // and the VPE_* psyfocus stats - scaling them with its RPG attributes. When our setting is on (default),
    // strip those StatParts at startup so this mod's own per-level scaling stays authoritative.
    // Note: applied once at startup (after defs + XML patches load); toggle requires a restart.
    [StaticConstructorOnStartup]
    public static class Patch_IsekaiSuppress
    {
        static Patch_IsekaiSuppress()
        {
            try
            {
                if (PsycastSynergiesMod.Settings == null || !PsycastSynergiesMod.Settings.suppressIsekaiPsycastStats) return;
                if (!ModsConfig.IsActive("JellyCreative.IsekaiLeveling")) return;

                int removed = 0;
                foreach (var stat in DefDatabase<StatDef>.AllDefsListForReading)
                {
                    if (stat?.parts == null) continue;
                    string dn = stat.defName ?? "";
                    bool psy = dn.Contains("Psychic") || dn.Contains("Psyfocus") || dn.Contains("Meditation")
                            || dn.Contains("Neural") || dn.StartsWith("VPE_");
                    if (!psy) continue;
                    removed += stat.parts.RemoveAll(p => p != null && (p.GetType().Namespace ?? "").StartsWith("IsekaiLeveling"));
                }
                if (Prefs.DevMode)
                    Log.Message("[Psycasts\u00b2] ISEKAI compat: suppressed " + removed + " psycaster-stat StatPart(s).");
            }
            catch (Exception e) { Log.Warning("[Psycasts\u00b2] ISEKAI suppression failed: " + e); }
        }
    }
}

#nullable disable
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PsycastSynergies
{
    // ============================== TIERING OVERRIDE (for mod authors) ==============================
    // Ship ONE small def and Psycasts² hands the enlightenment ladder over to your mod:
    //
    //   <PsycastSynergies.TieringOverrideDef>
    //     <defName>MyMod_Tiering</defName>
    //     <disableBuiltInTiering>true</disableBuiltInTiering>
    //     <tierNames><li>Initiate</li><li>Adept</li><li>Master</li></tierNames>
    //     <tierNameBeyondFormat>Master {ROMAN}</tierNameBeyondFormat>
    //     <tierSpecPoints><li>0</li><li>2</li><li>3</li></tierSpecPoints>
    //   </PsycastSynergies.TieringOverrideDef>
    //
    // The disable flags kill this mod's built-in tier PROGRESSION (how tiers are earned) while the tier
    // system itself - the PS_Enlightenment hediff, card picks, spec points, and the whole XML trigger API
    // (AwakeningTrigger.Fire, trigger hediffs, incidents, quest signals...) - stays fully live. That API
    // is your replacement progression. The reskin fields rename the ladder everywhere it appears (letters,
    // health tab, card picks, settings text is unaffected) and re-cost its spec-point rewards.
    public class TieringOverrideDef : Def
    {
        // --- earn-path disables (umbrella first; granular flags for partial handoffs) ---
        public bool disableBuiltInTiering;             // everything below at once: total handoff
        public bool disableMeditationAwakening;        // non-psycasters can no longer Awaken (Tier I) from meditation
        public bool disableTranscendence;              // Illuminated pawns no longer climb Tier 4+ from meditation
        public bool disablePilgrimages;                // the Tier II/III pilgrimage quest chains are never offered
        public bool disableExternalPsylinkAwakening;   // Empire bestowing / anima linking no longer trigger Awakening
        public bool disableEnemyTiers;                 // hostile psycasters no longer spawn tiered
        public bool disableRandomAwakenedSpawns;       // no random fresh-Awakened pawn generation
        public bool disablePsylinkGate;                // stop stripping generated psylinks from untiered pawns

        // --- ladder reskin ---
        public List<string> tierNames;                 // display names for tiers 1..N (replaces Awakened/Enlightened/Illuminated)
        public string tierNameBeyondFormat;            // tiers past the list: "{TIER}" = number, "{ROMAN}" = numeral. Unset = built-in "Transcendent {ROMAN}".
        public List<int> tierSpecPoints;               // spec points granted on REACHING tier 1..N; tiers past the list reuse the last entry
    }

    // Aggregated view over every loaded TieringOverrideDef: any def's disable wins (OR), the first def
    // supplying a reskin field wins it (a warning names any loser). Built lazily on first query.
    public static class TieringControl
    {
        private static bool built;
        private static bool dMed, dTranscend, dPilgrim, dExternal, dEnemy, dSpawn, dGate;
        private static List<string> names;
        private static string beyondFormat;
        private static List<int> specPoints;
        private static string ownerLabel = "";

        public static bool MeditationAwakeningDisabled { get { Build(); return dMed; } }
        public static bool TranscendenceDisabled { get { Build(); return dTranscend; } }
        public static bool PilgrimagesDisabled { get { Build(); return dPilgrim; } }
        public static bool ExternalPsylinkAwakeningDisabled { get { Build(); return dExternal; } }
        public static bool EnemyTiersDisabled { get { Build(); return dEnemy; } }
        public static bool RandomAwakenedSpawnsDisabled { get { Build(); return dSpawn; } }
        public static bool PsylinkGateDisabled { get { Build(); return dGate; } }
        public static string OwnerLabel { get { Build(); return ownerLabel; } }

        // Custom display name for a tier, or null to use the built-in ladder.
        public static string TierName(int tier)
        {
            Build();
            if (names == null || names.Count == 0 || tier <= 0) return null;
            if (tier <= names.Count) return names[tier - 1];
            if (!beyondFormat.NullOrEmpty())
                return beyondFormat.Replace("{TIER}", tier.ToString()).Replace("{ROMAN}", RomanNumerals.ToRoman(tier));
            return null;   // fall through to built-in "Transcendent X"
        }

        // Custom spec points for REACHING a tier, or null for the built-in rewards.
        public static int? SpecPoints(int tier)
        {
            Build();
            if (specPoints == null || specPoints.Count == 0 || tier <= 0) return null;
            return Mathf.Max(0, specPoints[Mathf.Clamp(tier, 1, specPoints.Count) - 1]);
        }

        private static void Build()
        {
            if (built) return;
            built = true;
            var owners = new List<string>();
            foreach (var d in DefDatabase<TieringOverrideDef>.AllDefsListForReading)
            {
                bool all = d.disableBuiltInTiering;
                dMed |= all || d.disableMeditationAwakening;
                dTranscend |= all || d.disableTranscendence;
                dPilgrim |= all || d.disablePilgrimages;
                dExternal |= all || d.disableExternalPsylinkAwakening;
                dEnemy |= all || d.disableEnemyTiers;
                dSpawn |= all || d.disableRandomAwakenedSpawns;
                dGate |= all || d.disablePsylinkGate;

                if (d.tierNames != null && d.tierNames.Count > 0)
                {
                    if (names == null) { names = d.tierNames; if (beyondFormat == null) beyondFormat = d.tierNameBeyondFormat; }
                    else Log.Warning("[Psycasts²] multiple TieringOverrideDefs supply tierNames; ignoring " + d.defName + "'s.");
                }
                else if (beyondFormat == null && !d.tierNameBeyondFormat.NullOrEmpty()) beyondFormat = d.tierNameBeyondFormat;

                if (d.tierSpecPoints != null && d.tierSpecPoints.Count > 0)
                {
                    if (specPoints == null) specPoints = d.tierSpecPoints;
                    else Log.Warning("[Psycasts²] multiple TieringOverrideDefs supply tierSpecPoints; ignoring " + d.defName + "'s.");
                }

                string owner = d.modContentPack?.Name ?? d.defName;
                if (!owners.Contains(owner)) owners.Add(owner);
            }
            ownerLabel = owners.Count == 0 ? "" : string.Join(", ", owners);
            if (owners.Count > 0)
                Log.Message("[Psycasts²] tiering override active (" + ownerLabel + "): "
                    + (dMed ? "meditation-awakening " : "") + (dTranscend ? "transcendence " : "")
                    + (dPilgrim ? "pilgrimages " : "") + (dExternal ? "external-psylink " : "")
                    + (dEnemy ? "enemy-tiers " : "") + (dSpawn ? "random-spawns " : "") + (dGate ? "psylink-gate " : "")
                    + (names != null ? "| custom tier names" : "") + (specPoints != null ? " | custom spec points" : ""));
        }
    }
}

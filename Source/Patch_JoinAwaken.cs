#nullable disable
using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using VanillaPsycastsExpanded;
using Verse;

namespace PsycastSynergies
{
    // ======================= JOINING PSYCASTERS WITH NO PATH =======================
    // Under our scheme a psycaster's trees come from the awakening cards, so a pawn who arrives
    // ALREADY carrying a psylink but with zero unlocked paths is a dead end: the tab's Unlock
    // buttons are disabled (lockPathsToEnlightenment), meditation only ramps NON-psycasters toward
    // Awakening, and no card is ever offered. That is exactly what happens when such a pawn joins
    // the colony - a recruited prisoner, a quest lodger who signs on, a wanderer, a freed slave,
    // or a psycaster granted a psylink by another mod while outside the player faction.
    //
    // Reconcile it: the first time a path-less psycaster is under player control, mark them awakened
    // and open the card pick for their tier (Tier I if they have none, which the pick's embrace
    // applies). Two entry points - the faction change itself (instant) and the hourly scan (safety
    // net for existing saves, dev-spawned pawns and any join route that bypasses SetFaction).
    internal static class JoinAwaken
    {
        internal static void TryOffer(Pawn p, string source)
        {
            try
            {
                if (Current.ProgramState != ProgramState.Playing) return;   // never during world/map gen
                if (p == null || p.Dead || p.Destroyed) return;
                if (p.RaceProps == null || !p.RaceProps.Humanlike) return;
                if (p.Faction == null || !p.Faction.IsPlayer) return;
                if (p.IsQuestLodger() || p.IsSlave || p.IsPrisoner) return;   // not fully ours yet

                var s = PsycastSynergiesMod.Settings;
                if (s == null || !s.empirePsylinkIntegrate) return;           // same toggle as external psylink grants
                if (TieringControl.ExternalPsylinkAwakeningDisabled) return;  // a mod owns awakening now

                var psy = p.Psycasts();
                if (psy == null) return;                                     // not a psycaster: the meditation ramp handles them
                if (psy.unlockedPaths != null && psy.unlockedPaths.Count > 0) return;   // already walking a path

                var gc = GameComponent_PsycastSynergies.Instance;
                var med = gc?.GetMed(p, true);
                if (med == null) return;
                if (med.pendingPick > 0) return;                             // already offered; the alert keeps it one click away
                // Belt and braces against a free reroll: a pawn who was given cards before (and later
                // surrendered the path) is not re-offered one for nothing.
                if (med.cardPaths != null && med.cardPaths.Count > 0) return;

                med.awakened = true;
                int tier = Mathf.Max(1, EnlightenmentTier.TierOf(p));
                if (Prefs.DevMode)
                    Log.Message("[Psycasts²] path-less psycaster reconciled (" + source + ") -> "
                        + p.LabelShortCap + ", card pick at tier " + tier + ".");
                if (PawnUtility.ShouldSendNotificationAbout(p))
                    Find.LetterStack.ReceiveLetter("PS_LetterJoinPsycasterLabel".Translate(),
                        "PS_LetterJoinPsycasterText".Translate(p.LabelShortCap),
                        LetterDefOf.PositiveEvent, p);
                MeditationSystem.OpenPick(p, tier);
            }
            catch (Exception e)
            {
                Log.Warning("[Psycasts²] join awakening reconcile failed: " + e);
            }
        }
    }

    // Pawn.SetFaction is the universal join chokepoint: recruitment, quest joiners, wanderers,
    // freed slaves and dev "make colonist" all route through it.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
    public static class Patch_JoinAwaken
    {
        public static void Postfix(Pawn __instance, Faction newFaction)
        {
            if (newFaction == null || !newFaction.IsPlayer) return;
            JoinAwaken.TryOffer(__instance, "faction change");
        }
    }
}

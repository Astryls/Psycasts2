#nullable disable
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PsycastSynergies
{
    // Auto-accept the four pilgrimage quests (Tier II / III, altar AND anima). The storyteller still decides
    // WHEN to offer one, but instead of the normal "quest available" choice letter we accept it immediately
    // and announce it, so the player never has to click Accept. Hooking SendLetterQuestAvailable is the single
    // choke point where a freshly-generated root-random quest is offered; we Accept(null) it (which fires the
    // Initiate signal so the QuestPartActivable trackers enable) and suppress the offer letter.
    [HarmonyPatch(typeof(QuestUtility), nameof(QuestUtility.SendLetterQuestAvailable))]
    public static class Patch_AutoAcceptPilgrimage
    {
        private static readonly HashSet<string> AutoAccept = new HashSet<string>
        {
            "PS_TierIIPilgrimage", "PS_TierIIIPilgrimage", "PS_T2AnimaPilgrimage", "PS_T3AnimaPilgrimage"
        };

        static bool Prefix(Quest quest)
        {
            if (quest?.root == null || !AutoAccept.Contains(quest.root.defName)) return true;

            if (quest.State == QuestState.NotYetAccepted) quest.Accept(null);

            Pawn pilgrim = FindPilgrim(quest);
            string label = quest.name.NullOrEmpty() ? "Pilgrimage begins" : "Pilgrimage begins: " + quest.name;
            string text = (pilgrim != null ? pilgrim.LabelShortCap + " has been called to a pilgrimage" : "A pilgrimage has been set in motion")
                + ", and it was accepted automatically. Send the pilgrim to the marked site to meditate and earn their next tier of Enlightenment.";
            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.PositiveEvent,
                pilgrim != null ? new LookTargets(pilgrim) : LookTargets.Invalid, null, quest);
            return false;   // do not also send the normal offer/choice letter
        }

        private static Pawn FindPilgrim(Quest quest)
        {
            var parts = quest.PartsListForReading;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] is QuestPart_PilgrimMeditation m && m.pilgrim != null) return m.pilgrim;
                if (parts[i] is QuestPart_PilgrimJourney j && j.pilgrim != null) return j.pilgrim;
            }
            return null;
        }
    }
}

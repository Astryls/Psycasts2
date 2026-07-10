#nullable disable
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using VanillaPsycastsExpanded;
using VanillaPsycastsExpanded.UI;
using VEF.Abilities;

namespace PsycastSynergies
{
    // Adds a free "Respec skills" button and a tier-costing "Respec path" button to the psycast tab.
    // A postfix on ITab_Pawn_Psycasts.FillTab runs whether VPE's own tab or Modern Psycasts UI's drawer
    // rendered the panel, so the buttons appear in both.
    [HarmonyPatch(typeof(ITab_Pawn_Psycasts), "FillTab")]
    public static class Patch_PsycastTabRespec
    {
        static void Postfix(ITab_Pawn_Psycasts __instance)
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            var psy = pawn?.Psycasts();
            if (psy == null) return;

            int learnedPsycasts = 0;
            var comp0 = pawn.GetComp<CompAbilities>();
            if (comp0 != null)
                foreach (var a in comp0.LearnedAbilities)
                    if (a?.def?.GetModExtension<AbilityExtension_Psycast>() != null) learnedPsycasts++;
            int tier = EnlightenmentTier.TierOf(pawn);

            Vector2 size = __instance.Size;
            var prevF = Text.Font; var prevA = Text.Anchor; var prevC = GUI.color;

            float panelW = Mathf.Min(size.x * 0.3f, 340f);   // fit inside the left (pawn-info) panel
            const float pad = 14f, h = 28f, gap = 8f;
            float bw = (panelW - pad * 2f - gap) / 2f;
            float y = size.y - h - 10f;
            var r1 = new Rect(pad, y, bw, h);
            var r2 = new Rect(r1.xMax + gap, y, bw, h);
            bool dev = Prefs.DevMode;
            float bgTop = dev ? y - h - 6f : y;   // dev mode adds a second tool row above the respecs
            MXStyle.Fill(new Rect(pad - 6f, bgTop - 6f, panelW - pad * 2f + 12f, size.y - 4f - bgTop + 6f), new Color(0.04f, 0.05f, 0.07f, 0.9f));

            // The tab's SECOND dev-mode tool (VPE's dev checkbox being the first): the in-game
            // synergy balance editor. Dev-gated, but its edits persist for normal play.
            if (dev)
            {
                var rDev = new Rect(pad, y - h - 6f, panelW - pad * 2f, h);
                TooltipHandler.TipRegion(rDev, "Open the synergy balance editor: re-pick any skill's primary effect, its synergies and empowers, and which skills feed which. Your picks override the built-in balance, apply instantly, and persist in mod settings.");
                if (MXStyle.Button(rDev, "Balance editor (dev)"))
                    Find.WindowStack.Add(new Window_BalanceEditor());
            }

            if (MXStyle.Button(r1, learnedPsycasts > 0 ? "Respec skills (" + learnedPsycasts + ")" : "Respec skills"))
            {
                if (learnedPsycasts > 0)
                    Find.WindowStack.Add(new Dialog_Confirm("Respec skills",
                        "Unlearn every psycast and refund all points spent learning and leveling them? Unlocked paths are kept. (Free.)",
                        () => SkillRespec(pawn, psy)));
                else Messages.Message("No learned psycasts to respec.", MessageTypeDefOf.RejectInput, false);
            }
            if (MXStyle.Button(r2, "Respec path"))
            {
                if (tier >= 1)
                    Find.WindowStack.Add(new Dialog_Confirm("Respec path - drop a tier",
                        "Surrender your highest enlightenment tier? Its path - and the abilities bought within it - are removed, and you must re-earn that tier to choose anew.",
                        () => CardRespec(pawn, psy)));
                else Messages.Message("No enlightenment tier to surrender.", MessageTypeDefOf.RejectInput, false);
            }

            Text.Font = prevF; Text.Anchor = prevA; GUI.color = prevC;
        }

        private static void SkillRespec(Pawn pawn, Hediff_PsycastAbilities psy)
        {
            var gc = GameComponent_PsycastSynergies.Instance;
            if (gc == null) return;
            var comp = pawn.GetComp<CompAbilities>();
            // Each learned psycast cost 1 point to learn (level 1) plus its invested levels; refund the lot.
            int refund = 0;
            if (comp != null)
                foreach (var a in comp.LearnedAbilities)
                    if (a?.def?.GetModExtension<AbilityExtension_Psycast>() != null)
                        refund += Mathf.Max(1, gc.GetLevel(pawn, a.def));
            if (refund <= 0) { Messages.Message("No learned psycasts to respec.", MessageTypeDefOf.RejectInput, false); return; }
            psy.points += refund;
            gc.ClearPawn(pawn);
            comp?.LearnedAbilities.RemoveAll(a => a?.def?.GetModExtension<AbilityExtension_Psycast>() != null);   // un-learn psycasts (paths kept) -> 0/10
            SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
            Messages.Message(pawn.LabelShortCap + " respecced their psycasts - " + refund + " point" + (refund == 1 ? "" : "s") + " returned.",
                pawn, MessageTypeDefOf.PositiveEvent, false);
        }

        private static void CardRespec(Pawn pawn, Hediff_PsycastAbilities psy)
        {
            int tier = EnlightenmentTier.TierOf(pawn);
            if (tier < 1) return;
            var gc = GameComponent_PsycastSynergies.Instance;
            var med = gc?.GetMed(pawn, false);

            // Dropping the LAST tier un-awakens the pawn completely: the psylink fades, they cease to be
            // a psycaster, and must re-awaken through meditation. (Paths/abilities/skills go with it.)
            if (tier <= 1)
            {
                EnlightenmentTier.SetTier(pawn, 0, false);
                if (med != null) { med.awakened = false; med.enlightenments = 0; med.cardPaths?.Clear(); }
                gc?.ClearPawn(pawn);
                // Remove the learned psycast abilities too - otherwise their gizmos keep calling
                // ShowGizmoOnPawn with no Psycasts hediff (spams "called on a pawn that does not have Psycasts").
                pawn.GetComp<CompAbilities>()?.LearnedAbilities.RemoveAll(a => a?.def?.GetModExtension<AbilityExtension_Psycast>() != null);
                if (pawn.health != null)
                {
                    if (psy != null) pawn.health.RemoveHediff(psy);                       // VPE psycast hediff
                    var psylink = pawn.GetMainPsylinkSource();
                    if (psylink != null) pawn.health.RemoveHediff(psylink);               // the psylink itself
                }
                SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
                Messages.Message(pawn.LabelShortCap + "'s psylink has faded - no longer a psycaster. They must re-awaken through meditation.",
                    pawn, MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            // Higher tiers: drop one tier - remove the highest card path + its abilities (refund the
            // points spent learning/leveling them), keep the psylink.
            PsycasterPathDef drop = null;
            if (med?.cardPaths != null && med.cardPaths.Count > 0)
            {
                drop = med.cardPaths[med.cardPaths.Count - 1];
                med.cardPaths.RemoveAt(med.cardPaths.Count - 1);
            }
            if (drop != null)
            {
                psy.unlockedPaths.Remove(drop);
                var comp = pawn.GetComp<CompAbilities>();
                if (comp != null)
                {
                    var rem = new System.Collections.Generic.List<VEF.Abilities.Ability>();
                    foreach (var a in comp.LearnedAbilities)
                        if (a?.def?.GetModExtension<AbilityExtension_Psycast>()?.path == drop) rem.Add(a);
                    foreach (var a in rem)
                    {
                        if (gc != null) { psy.points += Mathf.Max(1, gc.GetLevel(pawn, a.def)); gc.SetLevel(pawn, a.def, 0); }
                        comp.LearnedAbilities.Remove(a);
                    }
                }
            }
            EnlightenmentTier.SetTier(pawn, tier - 1, false);
            SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
            Messages.Message(pawn.LabelShortCap + " surrendered their highest tier"
                + (drop != null ? " (the " + drop.LabelCap + " path)" : "") + " - re-earn it to choose anew.",
                pawn, MessageTypeDefOf.NeutralEvent, false);
        }
    }
}

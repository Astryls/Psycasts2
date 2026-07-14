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
    // Adds the bottom tool block to the psycast tab: a free "Reset skills" button, a tier-costing
    // "Reset path" button, the "Pilgrim's path" routing dropdown (moved here from the old pawn
    // gizmo) and - when Modern Psycasts UI's clickable focus tiles aren't available - a "Default
    // focus" dropdown fallback. A postfix on ITab_Pawn_Psycasts.FillTab runs whether VPE's own tab
    // or Modern Psycasts UI's drawer rendered the panel, so the block appears in both.
    [HarmonyPatch(typeof(ITab_Pawn_Psycasts), "FillTab")]
    public static class Patch_PsycastTabRespec
    {
        static void Postfix(ITab_Pawn_Psycasts __instance)
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn;
            var psy = pawn?.Psycasts();
            if (psy == null) return;

            // Modern Psycasts UI's "Psysets" view replaces the whole left panel with its own editor
            // (Back button + Create psyset). Our footer tool block would draw right over those, so
            // yield the panel to it while that view is open.
            if (ModernUIBridge.PsysetPanelOpen) return;

            int learnedPsycasts = 0;
            var comp0 = pawn.GetComp<CompAbilities>();
            if (comp0 != null)
                foreach (var a in comp0.LearnedAbilities)
                    if (a?.def?.GetModExtension<AbilityExtension_Psycast>() != null) learnedPsycasts++;
            int tier = EnlightenmentTier.TierOf(pawn);

            Vector2 size = __instance.Size;
            var prevF = Text.Font; var prevA = Text.Anchor; var prevC = GUI.color;

            // Align with the tab's left (pawn-info) card: both VPE's tab and Modern Psycasts UI
            // inset that card 14px from the tab edge. Rows are centered within the card's width
            // (Modern UI's card is a fixed 340px — see ModernUIBridge.LeftCardWidth).
            const float x0 = 14f, pad = 14f, h = 28f, gap = 8f;
            float panelW = ModernUIBridge.LeftCardWidth(size.x);
            float rowW = panelW - pad * 2f;
            float rowX = x0 + (panelW - rowW) / 2f;   // centered on the card
            float bw = (rowW - gap) / 2f;
            float y = size.y - h - 10f;
            bool showSkillReset = PsycastSynergiesMod.Settings == null || !PsycastSynergiesMod.Settings.disableSkillReset;
            var r1 = new Rect(rowX, y, bw, h);
            // When the skill-reset button is hidden, the path-reset button spans the full row.
            var r2 = showSkillReset ? new Rect(r1.xMax + gap, y, bw, h) : new Rect(rowX, y, rowW, h);

            bool dev = Prefs.DevMode;
            bool ours = pawn.Faction != null && pawn.Faction.IsPlayer;
            bool pilgrimRow = ours && tier >= 1;              // pilgrimage routing dropdown (awakened only)
            bool focusRow = ours && !ModernUIBridge.Wired;    // fallback when the focus tiles aren't clickable
            float cursor = y;
            if (pilgrimRow) cursor -= h + 6f;
            float yPilgrim = cursor;
            if (focusRow) cursor -= h + 6f;
            float yFocus = cursor;
            if (dev) cursor -= h + 6f;
            float yDev = cursor;
            MXStyle.Fill(new Rect(rowX - 6f, cursor - 6f, rowW + 12f, size.y - 4f - cursor + 6f), new Color(0.04f, 0.05f, 0.07f, 0.9f));

            // The tab's SECOND dev-mode tool (VPE's dev checkbox being the first): the in-game
            // synergy balance editor. Dev-gated, but its edits persist for normal play.
            if (dev)
            {
                var rDev = new Rect(rowX, yDev, rowW, h);
                TooltipHandler.TipRegion(rDev, "Open the synergy balance editor: re-pick any skill's primary effect, its synergies and empowers, and which skills feed which. Your picks override the built-in balance, apply instantly, and persist in mod settings.");
                if (MXStyle.Button(rDev, "Balance editor (dev)"))
                    Find.WindowStack.Add(new Window_BalanceEditor());
            }

            // Pilgrimage routing (was a pawn gizmo): which tier-up quest the storyteller offers this
            // colonist. Colonists incapable of violence always walk the anima (pacifist) path.
            if (pilgrimRow)
            {
                bool canFight = !pawn.WorkTagIsDisabled(WorkTags.Violent);
                var med = GameComponent_PsycastSynergies.Instance?.GetMed(pawn, true);
                int pstyle = canFight ? (med != null ? med.pilgrimStyle : 0) : 2;
                string[] pl = { "PS_PilgrimUnbound".Translate(), "PS_PilgrimTrialAltar".Translate(), "PS_PilgrimWayAnima".Translate() };
                var rp = new Rect(rowX, yPilgrim, rowW, h);
                TooltipHandler.TipRegion(rp, "PS_PilgrimPathTip".Translate());
                if (MXStyle.Button(rp, "PS_PilgrimPathBtn".Translate(pl[pstyle])))
                {
                    if (!canFight)
                        Messages.Message("PS_MsgNoViolencePilgrim".Translate(pawn.LabelShortCap),
                            MessageTypeDefOf.RejectInput, false);
                    else if (med != null)
                    {
                        var opts = new System.Collections.Generic.List<FloatMenuOption>();
                        for (int i = 0; i < pl.Length; i++)
                        {
                            int style = i;
                            opts.Add(new FloatMenuOption(pl[style], () => med.pilgrimStyle = style,
                                CompSchoolFocus.PilgrimIcon(style), Color.white));
                        }
                        Find.WindowStack.Add(new FloatMenu(opts));
                    }
                }
            }

            // Default meditation focus (was a pawn gizmo). With Modern Psycasts UI the focus-type
            // tiles above are the picker (via PawnFocusHooks); this dropdown is the fallback.
            if (focusRow)
            {
                var medF = GameComponent_PsycastSynergies.Instance?.GetMed(pawn, true);
                if (medF != null)
                {
                    MeditationFocusDef cur = string.IsNullOrEmpty(medF.defaultFocus) ? null
                        : DefDatabase<MeditationFocusDef>.GetNamedSilentFail(medF.defaultFocus);
                    var rf = new Rect(rowX, yFocus, rowW, h);
                    TooltipHandler.TipRegion(rf, "PS_DefaultFocusTip".Translate());
                    if (MXStyle.Button(rf, "PS_DefaultFocusBtn".Translate(cur != null ? cur.LabelCap.ToString() : "PS_FocusAny".Translate().ToString())))
                        CompSchoolFocus.OpenMenuFor(f => medF.defaultFocus = f?.defName, "PS_FocusAnyDefer".Translate());
                }
            }

            if (showSkillReset && MXStyle.Button(r1, learnedPsycasts > 0 ? "PS_ResetSkillsN".Translate(learnedPsycasts).ToString() : "PS_ResetSkills".Translate().ToString()))
            {
                if (learnedPsycasts > 0)
                    Find.WindowStack.Add(new Dialog_Confirm("PS_ResetSkills".Translate(),
                        "PS_ResetSkillsBody".Translate(),
                        () => SkillRespec(pawn, psy)));
                else Messages.Message("PS_MsgNoPsycastsToReset".Translate(), MessageTypeDefOf.RejectInput, false);
            }
            if (MXStyle.Button(r2, "PS_ResetPath".Translate()))
            {
                if (tier >= 1)
                    Find.WindowStack.Add(new Dialog_Confirm("PS_ResetPathTitle".Translate(),
                        "PS_ResetPathBody".Translate(),
                        () => CardRespec(pawn, psy)));
                else Messages.Message("PS_MsgNoTierToSurrender".Translate(), MessageTypeDefOf.RejectInput, false);
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
            if (refund <= 0) { Messages.Message("PS_MsgNoPsycastsToReset".Translate(), MessageTypeDefOf.RejectInput, false); return; }
            psy.points += refund;
            gc.ClearPawn(pawn);
            comp?.LearnedAbilities.RemoveAll(a => a?.def?.GetModExtension<AbilityExtension_Psycast>() != null);   // un-learn psycasts (paths kept) -> 0/10
            SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
            Messages.Message("PS_MsgSkillsReset".Translate(pawn.LabelShortCap, refund),
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
                Messages.Message("PS_MsgPsylinkFaded".Translate(pawn.LabelShortCap),
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
            Messages.Message("PS_MsgTierSurrendered".Translate(pawn.LabelShortCap,
                drop != null ? "PS_TierSurrenderedPath".Translate(drop.LabelCap).ToString() : ""),
                pawn, MessageTypeDefOf.NeutralEvent, false);
        }
    }
}

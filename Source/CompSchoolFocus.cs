#nullable disable
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using VanillaPsycastsExpanded;

namespace PsycastSynergies
{
    public class CompProperties_SchoolFocus : CompProperties
    {
        public CompProperties_SchoolFocus() { compClass = typeof(CompSchoolFocus); }
    }

    // Added (via Patches/SchoolFocus.xml) to every vanilla/modded meditation-focus building. Lets the
    // player attune that focus to a VPE/vanilla meditation-focus TYPE (Flame, Morbid, Natural, Science,
    // Wealth, Group, Archotech, ...). A non-psycaster who meditates FACING it is then steered toward
    // that type's psycast schools among the Enlightenment awakening cards (MeditationSystem.FocusKeywords).
    // Storing the VPE MeditationFocusDef keeps the naming interoperable as VPE/addons add focus types.
    public class CompSchoolFocus : ThingComp
    {
        public MeditationFocusDef selectedFocus;

        private static Texture2D iconTex;
        internal static Texture2D GizmoIcon => iconTex != null ? iconTex : (iconTex = ContentFinder<Texture2D>.Get("UI/SchoolFocus", false));

        private static Texture2D[] pilgrimIcons;
        internal static Texture2D PilgrimIcon(int style)
        {
            if (pilgrimIcons == null)
                pilgrimIcons = new[]
                {
                    ContentFinder<Texture2D>.Get("UI/Pilgrim_Unbound", false),
                    ContentFinder<Texture2D>.Get("UI/Pilgrim_Altar", false),
                    ContentFinder<Texture2D>.Get("UI/Pilgrim_Anima", false),
                };
            int i = (style >= 0 && style < 3) ? style : 0;
            return pilgrimIcons[i] ?? GizmoIcon;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look(ref selectedFocus, "ps_focusType");
        }

        // Persistent meditation-focus readout. Vanilla CompMeditationFocus only prints the strength line
        // while a pawn is ACTIVELY meditating (its LastUser returns null 5 ticks after the last use), so the
        // number flickers in and out and is hard to read. We mirror it persistently: cache the live per-user
        // value while it's shown, and keep displaying it (or the intrinsic strength) once vanilla hides it.
        private string cachedFocusStr;

        public override string CompInspectStringExtra()
        {
            string typeLine = "PS_FocusTypeLabel".Translate(selectedFocus != null ? selectedFocus.LabelCap.ToString() : "PS_FocusAny".Translate().ToString());
            var med = parent.TryGetComp<CompMeditationFocus>();
            if (med == null) return typeLine;
            var user = med.LastUser;
            if (user != null)
                cachedFocusStr = "PS_FocusStrengthFor".Translate(user.LabelShort,
                    parent.GetStatValueForPawn(StatDefOf.MeditationFocusStrength, user).ToStringPercent());
            else if (cachedFocusStr.NullOrEmpty())
            {
                float v = parent.GetStatValue(StatDefOf.MeditationFocusStrength);
                if (v > 0f) cachedFocusStr = "PS_FocusStrength".Translate(v.ToStringPercent());
            }
            // While vanilla is showing its live line (user != null) don't duplicate the strength.
            return user == null && !cachedFocusStr.NullOrEmpty() ? typeLine + "\n" + cachedFocusStr : typeLine;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra()) yield return g;
            if (parent.Faction != null && parent.Faction != Faction.OfPlayer) yield break;
            // Don't offer the focus-type selector on walls. Some mods tag walls / natural rock as a
            // "Natural" meditation focus, which my comp-add patch would otherwise pick up — putting the
            // attune gizmo on every wall segment. Meditation spots, thrones and real foci aren't wall-linked.
            if (parent.def.graphicData != null && parent.def.graphicData.linkFlags.HasFlag(LinkFlags.Wall)) yield break;

            yield return new Command_Action
            {
                defaultLabel = "PS_FocusTypeLabel".Translate(selectedFocus != null ? selectedFocus.LabelCap.ToString() : "PS_FocusAny".Translate().ToString()),
                defaultDesc = "PS_FocusTypeDesc".Translate(),
                icon = FocusIcon(selectedFocus) ?? GizmoIcon,
                action = OpenMenu
            };
        }

        internal static Texture2D FocusIcon(MeditationFocusDef def)
        {
            string p = def?.GetModExtension<MeditationFocusExtension>()?.icon;
            return string.IsNullOrEmpty(p) ? null : ContentFinder<Texture2D>.Get(p, false);
        }

        private void OpenMenu() => OpenMenuFor(f => selectedFocus = f, "PS_FocusAnyTrait".Translate());

        internal static void OpenMenuFor(System.Action<MeditationFocusDef> setter, string nullLabel)
        {
            var opts = new List<FloatMenuOption>();
            opts.Add(new FloatMenuOption(nullLabel, () => setter(null)));
            foreach (var def in DefDatabase<MeditationFocusDef>.AllDefs.OrderBy(d => d.LabelCap.ToString()))
            {
                var fd = def;
                opts.Add(new FloatMenuOption(fd.LabelCap, () => setter(fd), FocusIcon(fd), Color.white));
            }
            if (opts.Count > 1) Find.WindowStack.Add(new FloatMenu(opts));
        }
    }

    // Pawn gizmos. The old per-pawn "Default focus" and "Pilgrim's Path" gizmos moved into the
    // psycast tab (focus = clickable focus-type tiles via Modern Psycasts UI's PawnFocusHooks,
    // with a dropdown fallback; path = a dropdown row above the respec buttons - see
    // Patch_PsycastTabRespec). Only the "Stop meditating" button remains on the pawn itself.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_PawnDefaultFocusGizmo
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Pawn __instance)
        {
            foreach (var v in values) yield return v;
            if (__instance?.Faction == null || !__instance.Faction.IsPlayer) yield break;
            if (__instance.RaceProps == null || !__instance.RaceProps.Humanlike) yield break;
            if (__instance.GetMainPsylinkSource() == null) yield break;   // psycasters only

            // Stop button for "meditate your ass off" forced meditation (always reachable on the pawn).
            if (ForcedMeditation.On(__instance))
            {
                yield return new Command_Action
                {
                    defaultLabel = "PS_StopMeditating".Translate(),
                    defaultDesc = "PS_StopMeditatingDesc".Translate(),
                    icon = CompSchoolFocus.GizmoIcon,
                    action = () => ForcedMeditation.Stop(__instance)
                };
            }
        }
    }
}

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
            string typeLine = "Focus type: " + (selectedFocus != null ? selectedFocus.LabelCap.ToString() : "any");
            var med = parent.TryGetComp<CompMeditationFocus>();
            if (med == null) return typeLine;
            var user = med.LastUser;
            if (user != null)
                cachedFocusStr = "Meditation focus strength for " + user.LabelShort + ": "
                    + parent.GetStatValueForPawn(StatDefOf.MeditationFocusStrength, user).ToStringPercent() + " / day";
            else if (cachedFocusStr.NullOrEmpty())
            {
                float v = parent.GetStatValue(StatDefOf.MeditationFocusStrength);
                if (v > 0f) cachedFocusStr = "Meditation focus strength: " + v.ToStringPercent() + " / day";
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
                defaultLabel = selectedFocus != null ? "Focus type: " + selectedFocus.LabelCap : "Focus type: any",
                defaultDesc = "Attune this meditation focus to a psychic focus type.\n\nA non-psycaster who meditates facing this focus is steered toward that type's psycast schools among their awakening cards when Enlightenment strikes. Leave it on \u201cany\u201d to weight by the meditator's traits instead.",
                icon = FocusIcon(selectedFocus) ?? GizmoIcon,
                action = OpenMenu
            };
        }

        internal static Texture2D FocusIcon(MeditationFocusDef def)
        {
            string p = def?.GetModExtension<MeditationFocusExtension>()?.icon;
            return string.IsNullOrEmpty(p) ? null : ContentFinder<Texture2D>.Get(p, false);
        }

        private void OpenMenu() => OpenMenuFor(f => selectedFocus = f, "Any (trait-weighted)");

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

    // Per-pawn default-focus gizmo. Shows on player-faction humanlike PSYCASTERS (anyone with a
    // psylink - awakened pawns included since awakening grants one). Used by MeditationSystem's
    // priority chain (building gizmo > pawn default > building's native > null/trait-weighted).
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_PawnDefaultFocusGizmo
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Pawn __instance)
        {
            foreach (var v in values) yield return v;
            if (__instance?.Faction == null || !__instance.Faction.IsPlayer) yield break;
            if (__instance.RaceProps == null || !__instance.RaceProps.Humanlike) yield break;
            if (__instance.GetMainPsylinkSource() == null) yield break;   // psycasters only

            var med = GameComponent_PsycastSynergies.Instance?.GetMed(__instance, true);
            if (med == null) yield break;

            // Pilgrimage routing: which tier-up quest the storyteller offers this colonist. Only meaningful
            // once awakened (Tier I+). Colonists incapable of violence are locked to the anima (pacifist) path.
            if (EnlightenmentTier.TierOf(__instance) >= 1)
            {
                bool canFight = !__instance.WorkTagIsDisabled(WorkTags.Violent);
                int pstyle = canFight ? med.pilgrimStyle : 2;
                string[] pl = { "Pilgrim's Path: Unbound", "Pilgrim's Path: Trial of the Altar", "Pilgrim's Path: Way of the Anima" };
                yield return new Command_Action
                {
                    defaultLabel = pl[pstyle],
                    defaultDesc = "The road this seeker will be called to walk toward their next tier of Enlightenment:\n\n"
                        + "• Trial of the Altar (combat): a single hallowed site, held against waves of the Ancient Psycaster Order.\n"
                        + "• Way of the Anima (pacifist): a longer pilgrimage among the anima trees, with no bloodshed.\n"
                        + "• Unbound: let fate (the storyteller) choose either road.\n\n"
                        + "Those who cannot bring themselves to violence always walk the Way of the Anima.",
                    icon = CompSchoolFocus.PilgrimIcon(pstyle),
                    action = () =>
                    {
                        if (!canFight)
                        {
                            Messages.Message(__instance.LabelShortCap + " is incapable of violence — always the anima (pacifist) pilgrimage.",
                                MessageTypeDefOf.RejectInput, false);
                            return;
                        }
                        med.pilgrimStyle = (med.pilgrimStyle + 1) % 3;
                    }
                };
            }

            // Stop button for "meditate your ass off" forced meditation (always reachable on the pawn).
            if (ForcedMeditation.On(__instance))
            {
                yield return new Command_Action
                {
                    defaultLabel = "Stop meditating",
                    defaultDesc = "This colonist is meditating continuously (\"meditate your ass off\"). Click to stop and return to their normal schedule.",
                    icon = CompSchoolFocus.GizmoIcon,
                    action = () => ForcedMeditation.Stop(__instance)
                };
            }

            MeditationFocusDef cur = string.IsNullOrEmpty(med.defaultFocus) ? null
                : DefDatabase<MeditationFocusDef>.GetNamedSilentFail(med.defaultFocus);

            yield return new Command_Action
            {
                defaultLabel = cur != null ? "Default focus: " + cur.LabelCap : "Default focus: any",
                defaultDesc = "Personal meditation focus preference. Used when this colonist meditates at a focus building that doesn't have its own attunement set. \n\nPriority: building's gizmo \u2192 pawn's default \u2192 building's native focus \u2192 unfocused. So if you set the altar/sculpture/tree itself, that wins; otherwise this preference biases the awakening cards toward this focus type's schools.",
                icon = CompSchoolFocus.FocusIcon(cur) ?? CompSchoolFocus.GizmoIcon,
                action = () => CompSchoolFocus.OpenMenuFor(f => med.defaultFocus = f?.defName, "Any (defer to building / traits)")
            };
        }
    }
}

#nullable disable
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PsycastSynergies
{
    // A single specialization node. Effects are applied centrally in SpecEffects; this is just
    // the metadata + tree placement + icon.
    public class Spec
    {
        public string id;
        public string label;
        public string desc;
        public int cost;
        public string branch;            // Offense / Control / Ward / Flow / Apex / Mod / Root
        public float col, row;           // tree grid position (row 0 = top = most powerful)
        public string[] prereqs;         // ALL required
        public string[] anyPrereq;       // ANY one required (e.g. Ascendance needs any capstone)
        public bool pick;                // mod-aware: needs a chosen target (ability/path/element)
        public string exclusiveGroup;    // only ONE node sharing this tag may be owned
        public Texture2D icon;

        public Spec(string id, string label, int cost, string branch, float col, float row, string desc,
            string[] prereqs = null, string[] anyPrereq = null, bool pick = false, string exclusive = null)
        {
            this.id = id; this.label = label; this.cost = cost; this.branch = branch;
            this.col = col; this.row = row; this.desc = desc;
            this.prereqs = prereqs ?? new string[0];
            this.anyPrereq = anyPrereq ?? new string[0];
            this.pick = pick;
            this.exclusiveGroup = exclusive;
        }
    }

    [StaticConstructorOnStartup]
    public static class Specs
    {
        public static readonly List<Spec> All = new List<Spec>();
        public static readonly Dictionary<string, Spec> ById = new Dictionary<string, Spec>();

        public static readonly string[] Capstones = { "tempest", "maelstrom", "sanctuary", "conduit" };

        static Specs()
        {
            // Root
            Add(new Spec("kindling", "Kindling", 8, "Root", 3.5f, 4f,
                "Opens the way. +3% to every psycast's primary stat."));

            // Tier 1 - stat foundations
            Add(new Spec("surge", "Surge", 10, "Offense", 2.5f, 3.3f,
                "+15% to all damage / power scaling.", new[] { "kindling" }));
            Add(new Spec("resonance", "Resonance", 10, "Control", 4.5f, 3.3f,
                "+15% radius on every cast.", new[] { "kindling" }));
            Add(new Spec("lingering", "Lingering", 10, "Control", 2.5f, 4.7f,
                "+15% duration on every timed effect.", new[] { "kindling" }));
            Add(new Spec("potency", "Potency", 10, "Ward", 4.5f, 4.7f,
                "+15% buff / boon magnitude.", new[] { "kindling" }));

            // Tier 2 - role / meta
            Add(new Spec("onslaught", "Onslaught", 15, "Offense", 1.7f, 2.6f,
                "Offensive casts +20% power; synergies from offensive skills count double.", new[] { "surge" }));
            Add(new Spec("dominion", "Dominion", 15, "Control", 5.3f, 2.6f,
                "Control casts +20% radius & duration.", new[] { "resonance" }));
            Add(new Spec("bulwark", "Bulwark", 15, "Ward", 5.3f, 5.4f,
                "Boons & shields +20% strength & duration.", new[] { "potency" }));
            Add(new Spec("flow", "Flow", 12, "Flow", 1.7f, 5.4f,
                "Halves the level-based heat & psyfocus cost penalty.", new[] { "kindling" }));

            // Tier 3 - power
            Add(new Spec("fervor", "Fervor", 18, "Offense", 0.9f, 1.9f,
                "Offensive power rises with your current neural heat (up to +25% at maximum).", new[] { "onslaught" }));
            Add(new Spec("abandon", "Abandon", 18, "Offense", 0.6f, 3f,
                "Reckless: +40% offensive power, but +40% psyfocus & heat cost.", new[] { "onslaught" }));
            Add(new Spec("echo", "Echo", 18, "Control", 6.1f, 1.9f,
                "20% chance a cast immediately refreshes its cooldown.", new[] { "dominion" }));
            Add(new Spec("wellspring", "Wellspring", 18, "Ward", 6.1f, 6.1f,
                "Slowly regenerate psyfocus over time.", new[] { "bulwark" }));
            Add(new Spec("confluence", "Confluence", 15, "Flow", 0.9f, 6.1f,
                "Synergy bonuses you receive are doubled.", new[] { "flow" }));

            // Tier 4 - capstones
            Add(new Spec("tempest", "Tempest", 20, "Offense", 0.2f, 1.2f,
                "When a psycast kills, refund half of that cast's heat & psyfocus.", new[] { "fervor" }));
            Add(new Spec("maelstrom", "Maelstrom", 20, "Control", 6.8f, 1.2f,
                "Area casts surge outward: +50% radius and +25% duration.", new[] { "echo" }));
            Add(new Spec("sanctuary", "Sanctuary", 20, "Ward", 6.8f, 6.8f,
                "Bastion of will: boons +50% strength & duration.", new[] { "wellspring" }));
            Add(new Spec("conduit", "Conduit", 20, "Flow", 0.2f, 6.8f,
                "Synergy bonuses you receive are increased by 50%.", new[] { "confluence" }));

            // Apex
            Add(new Spec("convergence", "Convergence", 30, "Apex", 3.5f, 0.3f,
                "Convergence. +15% to ALL stats & synergies, and +5 to the per-skill level cap. Opens the seven Apotheosis constellations drifting above.",
                null, Capstones));

            // Mod-aware picks
            Add(new Spec("mastery", "Mastery", 20, "Mod", 2.6f, 7.3f,
                "Choose one learned psycast - it gains +100% to its primary stat and ignores cost scaling.\n\nExclusive: only one of Overflow / Mastery / Discipline / Attunement.",
                new[] { "kindling" }, null, pick: true, exclusive: "specialization"));
            Add(new Spec("discipline", "Discipline", 20, "Mod", 3.5f, 7.7f,
                "Choose a path - its skills gain +50% per-level scaling and start one effective level higher.\n\nExclusive: only one of Overflow / Mastery / Discipline / Attunement.",
                new[] { "kindling" }, null, pick: true, exclusive: "specialization"));
            Add(new Spec("attunement", "Attunement", 20, "Mod", 4.4f, 7.3f,
                "Choose an element - casts of that damage type gain +25% power & radius.\n\nExclusive: only one of Overflow / Mastery / Discipline / Attunement.",
                new[] { "kindling" }, null, pick: true, exclusive: "specialization"));

            // Extra nodes.
            Add(new Spec("overflow", "Overflow", 12, "Root", 3.5f, 2.3f,
                "+8% to ALL of a skill's stats (not just its primary).\n\nExclusive: only one of Overflow / Mastery / Discipline / Attunement.",
                new[] { "kindling" }, exclusive: "specialization"));
            Add(new Spec("catalyst", "Catalyst", 15, "Flow", 0.6f, 5f,
                "Your psycasts are cast 25% faster.", new[] { "flow" }));
            Add(new Spec("siphon", "Siphon", 16, "Offense", 1.4f, 0.9f,
                "Psycast kills restore 25% of that cast's psyfocus.", new[] { "onslaught" }));

            // ── Apotheosis constellations: SEVEN floating star-signs drifting high above Convergence.
            //    Pick ONE path - every gateway shares the mutually-exclusive "apotheosis" group. Effects
            //    are psycaster/lore flavour delivered by hediffs, NOT the synergy/skill stat system.
            //    Each gate requires "convergence" but draws NO connector to it (floating).

            // Halcyon (green arc) - upper-left.
            Add(new Spec("tm_gate", "Stillness", 20, "Halcyon", 0.8f, -6.6f,
                "Begin the Halcyon path.\n\nNeural heat builds 20% slower and recovers 25% faster.",
                new[] { "convergence" }, null, false, "apotheosis"));
            Add(new Spec("tm_mid", "Serenity", 20, "Halcyon", 1.7f, -7.2f,
                "Neural heat builds 35% slower and recovers 50% faster.", new[] { "tm_gate" }));
            Add(new Spec("tm_cap", "Halcyon", 30, "Halcyon", 2.6f, -6.7f,
                "Perfect serenity.\n\n\u2022 Neural heat builds 55% slower, recovers 90% faster.\n\u2022 Immune to ALL mental breaks.",
                new[] { "tm_mid" }));

            // Singularity (gold diamond) - top-center, highest.
            Add(new Spec("ar_gate", "Event Horizon", 25, "Singularity", 4.3f, -6.4f,
                "Begin the Singularity path.\n\n\u22125% psyfocus cost, +10% psychic sensitivity.",
                new[] { "convergence" }, null, false, "apotheosis"));
            Add(new Spec("ar_l", "Logic Engine", 20, "Singularity", 3.6f, -7.2f,
                "\u221210% psyfocus cost, +18% psychic sensitivity.", new[] { "ar_gate" }));
            Add(new Spec("ar_r", "Datum", 20, "Singularity", 5.0f, -7.2f,
                "\u221215% psyfocus cost, +25% psychic sensitivity, slow passive psyfocus regen.", new[] { "ar_gate" }));
            Add(new Spec("ar_cap", "Singularity", 35, "Singularity", 4.3f, -8.0f,
                "Verge on the archotech.\n\n\u2022 \u221220% psyfocus cost.\n\u2022 +35% psychic sensitivity.\n\u2022 Steady passive psyfocus regeneration.",
                new[] { "ar_l", "ar_r" }));

            // Penumbra (violet crown/W) - upper-right.
            Add(new Spec("um_gate", "Umbral Veil", 30, "Penumbra", 6.2f, -6.4f,
                "Begin the Penumbra path.\n\nPsychic shadow steels the mind. +8% psychic sensitivity.",
                new[] { "convergence" }, null, false, "apotheosis"));
            Add(new Spec("um_a", "Shade", 20, "Penumbra", 7.0f, -7.1f,
                "The shadow deepens. +14% psychic sensitivity.", new[] { "um_gate" }));
            Add(new Spec("um_b", "Gloaming", 20, "Penumbra", 7.9f, -6.5f,
                "The shadow spreads. +20% psychic sensitivity.", new[] { "um_a" }));
            Add(new Spec("um_c", "Eclipse", 20, "Penumbra", 8.8f, -7.1f,
                "The shadow consumes. +27% psychic sensitivity.", new[] { "um_b" }));
            Add(new Spec("um_cap", "Penumbra", 40, "Penumbra", 7.9f, -7.9f,
                "Sovereign of shadow.\n\n\u2022 +34% psychic sensitivity.\n\u2022 Immune to mental breaks, berserk and mind control.\n\u2022 Aura: hostile psycasters within 9 tiles lose 50% psychic sensitivity.",
                new[] { "um_c" }));

            // Empyrean (blue eye) - mid-right.
            Add(new Spec("em_gate", "Stargazer", 25, "Empyrean", 6.4f, -3.7f,
                "Begin the Empyrean path.\n\nThe mind reaches into the void between the stars. +10% psychic sensitivity; immune to the psychic drone.",
                new[] { "convergence" }, null, false, "apotheosis"));
            Add(new Spec("em_a", "Farsight", 20, "Empyrean", 7.2f, -4.4f,
                "The mind's eye opens onto far horizons. +18% psychic sensitivity.", new[] { "em_gate" }));
            Add(new Spec("em_b", "Constellate", 20, "Empyrean", 8.2f, -4.4f,
                "Scattered thoughts align like distant stars. +24% psychic sensitivity.", new[] { "em_a" }));
            Add(new Spec("em_c", "Starlit", 20, "Empyrean", 9.0f, -3.7f,
                "Lit from within by far-off suns. +30% psychic sensitivity.", new[] { "em_b" }));
            Add(new Spec("em_cap", "Empyrean", 35, "Empyrean", 7.7f, -3.0f,
                "The mind joins the stars.\n\n\u2022 +30% psychic sensitivity.\n\u2022 Starlight aura: every nearby ally is bathed in borrowed starlight, their psychic sense sharpened.",
                new[] { "em_c" }));

            // Chronos (teal hourglass) - mid-left.
            Add(new Spec("cr_gate", "Hourglass", 25, "Chronos", 0.4f, -3.6f,
                "Begin the Chronos path.\n\n+10% meditation focus; a slow passive psyfocus regen.",
                new[] { "convergence" }, null, false, "apotheosis"));
            Add(new Spec("cr_waist", "Dilation", 20, "Chronos", 1.4f, -4.4f,
                "+20% meditation focus; stronger psyfocus regen.", new[] { "cr_gate" }));
            Add(new Spec("cr_a", "Eddy", 20, "Chronos", 2.4f, -3.6f,
                "+8% psychic sensitivity.", new[] { "cr_waist" }));
            Add(new Spec("cr_b", "Rewind", 20, "Chronos", 0.4f, -5.2f,
                "+14% psychic sensitivity.", new[] { "cr_waist" }));
            Add(new Spec("cr_cap", "Chronos", 35, "Chronos", 2.4f, -5.2f,
                "Master of moments.\n\n\u2022 +30% meditation focus.\n\u2022 Strong steady passive psyfocus regeneration.",
                new[] { "cr_b" }));

            // Pandemonium (red burst) - lower-left-center.
            Add(new Spec("pn_gate", "Spark", 30, "Pandemonium", 3.0f, -1.5f,
                "Begin the Pandemonium path.\n\nThe mind runs wild. +8% psychic sensitivity.",
                new[] { "convergence" }, null, false, "apotheosis"));
            Add(new Spec("pn_a", "Fracture", 20, "Pandemonium", 2.1f, -2.2f,
                "+15% psychic sensitivity.", new[] { "pn_gate" }));
            Add(new Spec("pn_b", "Frenzy", 20, "Pandemonium", 4.0f, -2.2f,
                "+15% psychic sensitivity.", new[] { "pn_gate" }));
            Add(new Spec("pn_c", "Riot", 20, "Pandemonium", 2.7f, -3.0f,
                "+21% psychic sensitivity.", new[] { "pn_a" }));
            Add(new Spec("pn_cap", "Pandemonium", 40, "Pandemonium", 3.6f, -3.2f,
                "Unbound chaos.\n\n\u2022 +26% psychic sensitivity.\n\u2022 Immune to mind control - no will commands the storm.",
                new[] { "pn_b", "pn_c" }));

            // Consonance (pink chorus-ring) - lower-right-center.
            Add(new Spec("cn_gate", "Resonate", 25, "Consonance", 5.4f, -1.5f,
                "Begin the Consonance path.\n\nYour mind hums in sympathy with every psyche around it. +8% psychic sensitivity.",
                new[] { "convergence" }, null, false, "apotheosis"));
            Add(new Spec("cn_a", "Attune", 20, "Consonance", 4.6f, -2.2f,
                "Your mind tunes itself to those nearby. +14% psychic sensitivity.", new[] { "cn_gate" }));
            Add(new Spec("cn_b", "Chord", 20, "Consonance", 4.8f, -3.1f,
                "Many minds, struck as one note. +20% psychic sensitivity.", new[] { "cn_a" }));
            Add(new Spec("cn_d", "Accord", 20, "Consonance", 6.2f, -2.2f,
                "Wills settle into agreement. +14% psychic sensitivity.", new[] { "cn_gate" }));
            Add(new Spec("cn_c", "Harmony", 20, "Consonance", 6.0f, -3.1f,
                "The chorus swells. +20% psychic sensitivity.", new[] { "cn_d" }));
            Add(new Spec("cn_cap", "Consonance", 35, "Consonance", 5.4f, -3.7f,
                "One voice in many.\n\n\u2022 +24% psychic sensitivity.\n\u2022 Resonant chord: nearby allied psycasters fall into harmony with you, their psychic sense amplified.",
                new[] { "cn_b", "cn_c" }));
        }

        private static void Add(Spec s)
        {
            All.Add(s);
            ById[s.id] = s;
            s.icon = ContentFinder<Texture2D>.Get("UI/Specs/" + s.id, false);
        }

        public static Spec Get(string id) => ById.TryGetValue(id, out var s) ? s : null;

        // Capstones get a distinct frame: the Apex (Convergence), the four main-tree capstones, and every
        // ascension capstone (ids ending in "_cap").
        public static bool IsCapstone(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return id == "convergence" || id.EndsWith("_cap") || System.Array.IndexOf(Capstones, id) >= 0;
        }
    }
}

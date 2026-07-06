#nullable disable
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PsycastSynergies
{
    [DefOf]
    public static class PsycastDefOf
    {
        public static HediffDef PS_PsychicResonance;
        public static HediffDef PS_TranquilMind;
        public static HediffDef PS_ArchotechAscendant;
        public static HediffDef PS_UmbralSovereign;
        public static HediffDef PS_UmbralSuppression;
        public static HediffDef PS_Empyrean;
        public static HediffDef PS_Pandemonium;
        public static HediffDef PS_Chronos;
        public static HediffDef PS_Consonance;
        public static HediffDef PS_AscensionBlessing;

        public static ThingDef PS_AscensionGlow_Tranquil;
        public static ThingDef PS_AscensionGlow_Transcendence;
        public static ThingDef PS_AscensionGlow_Umbral;
        public static ThingDef PS_AscensionGlow_Empyrean;
        public static ThingDef PS_AscensionGlow_Pandemonium;
        public static ThingDef PS_AscensionGlow_Chronos;
        public static ThingDef PS_AscensionGlow_Consonance;
        public static FleckDef PS_Fleck_Leaf;
        public static FleckDef PS_Fleck_Wisp;
        public static FleckDef PS_Fleck_Dot;
        public static FleckDef PS_Fleck_Glyph0;
        public static FleckDef PS_Fleck_Glyph1;

        static PsycastDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(PsycastDefOf)); }
    }

    // The three mutually-exclusive ascension "constellations" beyond Ascendance. Each is a small
    // cluster of spec nodes; owning N of a cluster's nodes drives its hediff to severity N, and the
    // capstone unlocks the full effect (immunity / aura / visual).
    public static class AscensionSystem
    {
        public class Cluster
        {
            public string key;
            public HediffDef hediff;
            public string[] nodes;
            public string capstone;
        }

        private static List<Cluster> clusters;
        public static List<Cluster> Clusters => clusters ?? (clusters = new List<Cluster>
        {
            new Cluster { key = "tranquil",    hediff = PsycastDefOf.PS_TranquilMind,      nodes = new[] { "tm_gate", "tm_mid", "tm_cap" },               capstone = "tm_cap" },
            new Cluster { key = "archotech",   hediff = PsycastDefOf.PS_ArchotechAscendant, nodes = new[] { "ar_gate", "ar_l", "ar_r", "ar_cap" },         capstone = "ar_cap" },
            new Cluster { key = "umbral",      hediff = PsycastDefOf.PS_UmbralSovereign,    nodes = new[] { "um_gate", "um_a", "um_b", "um_c", "um_cap" },  capstone = "um_cap" },
            new Cluster { key = "empyrean",    hediff = PsycastDefOf.PS_Empyrean,           nodes = new[] { "em_gate", "em_a", "em_b", "em_c", "em_cap" },  capstone = "em_cap" },
            new Cluster { key = "chronos",     hediff = PsycastDefOf.PS_Chronos,            nodes = new[] { "cr_gate", "cr_waist", "cr_a", "cr_b", "cr_cap" }, capstone = "cr_cap" },
            new Cluster { key = "pandemonium", hediff = PsycastDefOf.PS_Pandemonium,        nodes = new[] { "pn_gate", "pn_a", "pn_b", "pn_c", "pn_cap" },  capstone = "pn_cap" },
            new Cluster { key = "consonance",  hediff = PsycastDefOf.PS_Consonance,         nodes = new[] { "cn_gate", "cn_a", "cn_b", "cn_d", "cn_c", "cn_cap" }, capstone = "cn_cap" },
        });

        public static bool HasCapstone(Pawn pawn, string key)
        {
            var sp = GameComponent_PsycastSynergies.Instance?.GetSpec(pawn);
            if (sp == null) return false;
            foreach (var c in Clusters) if (c.key == key) return sp.Owns(c.capstone);
            return false;
        }

        // Reconcile each ascension hediff with how many of its cluster nodes the pawn owns.
        public static void Sync(Pawn pawn, SpecData sp)
        {
            if (pawn?.health == null) return;
            foreach (var c in Clusters)
            {
                int owned = 0;
                if (sp != null) foreach (var n in c.nodes) if (sp.Owns(n)) owned++;

                var hd = pawn.health.hediffSet.GetFirstHediffOfDef(c.hediff);
                if (owned <= 0)
                {
                    if (hd != null) pawn.health.RemoveHediff(hd);
                    continue;
                }
                var brain = pawn.health.hediffSet.GetBrain();
                if (hd != null && hd.Part != brain) { pawn.health.RemoveHediff(hd); hd = null; }   // migrate whole-body -> brain
                if (hd == null)
                {
                    hd = HediffMaker.MakeHediff(c.hediff, pawn, brain);   // seat in the brain, like a psylink
                    pawn.health.AddHediff(hd, brain);
                }
                hd.Severity = owned;
            }
        }
    }
}

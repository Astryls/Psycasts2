#nullable disable
using System.Collections.Generic;
using Verse;

namespace PsycastSynergies
{
    // ============================== BALANCE OVERRIDE (for modpack / addon authors) ==============================
    // Ship ONE small def in YOUR OWN mod's Defs and Psycasts² folds your balance edits into its synergy
    // overlay for every player of the pack. It layers ABOVE the mod's own baked ManualBalance.json and the
    // auto graph, but UNDER a player's personal in-game Balance-editor edits, so players can still retune.
    //
    //   <PsycastSynergies.BalanceOverrideDef>
    //     <defName>MyPack_PsycastBalance</defName>
    //
    //     <!-- Per-ability PRIMARY: which stat the ability's own levels scale, and a strength multiplier. -->
    //     <primaries>
    //       <li><ability>VPE_Firestorm</ability><stat>Power</stat><strength>1.5</strength></li>
    //       <li><ability>VPE_BladeFocus</ability><stat>none</stat></li>          <!-- no primary scaling -->
    //     </primaries>
    //
    //     <!-- Per-EDGE empower: how a source ability feeds a target ability (stat + strength). -->
    //     <empowers>
    //       <li><source>VPE_Flame</source><target>VPE_Firestorm</target><stat>Radius</stat><strength>0.5</strength></li>
    //     </empowers>
    //
    //     <!-- Full REPLACEMENT of a target ability's synergy source list. -->
    //     <sources>
    //       <li><ability>VPE_Firestorm</ability><from><li>VPE_Flame</li><li>VPE_Ignite</li></from></li>
    //     </sources>
    //   </PsycastSynergies.BalanceOverrideDef>
    //
    // <stat> accepts any SynStat name (Power, Radius, Duration, Strength, Range, Cooldown, Targets, Charges,
    // Efficiency, Haste, Yield, ProjectileCount, SummonCount) case-insensitively, or "none" for no scaling.
    // Omit <strength> to leave the auto/baked strength; omit <stat> to leave the auto/baked stat.
    // Every field is optional - supply only what you want to change.
    public class BalanceOverrideDef : Def
    {
        public List<PrimaryOverride> primaries;
        public List<EmpowerOverride> empowers;
        public List<SourceOverride> sources;
    }

    public class PrimaryOverride
    {
        public string ability;
        public string stat;               // SynStat name / "none" / raw int; null = leave unchanged
        public float strength = float.NaN; // NaN = leave unchanged
    }

    public class EmpowerOverride
    {
        public string source;
        public string target;
        public string stat;               // SynStat name / "none" / raw int; null = leave unchanged
        public float strength = float.NaN; // NaN = leave unchanged
    }

    public class SourceOverride
    {
        public string ability;            // the TARGET ability whose source list is being replaced
        public List<string> from;         // the FULL replacement list of synergy-source ability defNames
    }
}

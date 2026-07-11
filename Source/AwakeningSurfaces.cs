#nullable disable
using System.Collections.Generic;
using RimWorld;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;

namespace PsycastSynergies
{
    // ---------------------------------------------------------------------------------
    // 1) HEDIFF SURFACE - the universal hook. Anything that can apply a hediff triggers
    // awakening: IngestionOutcomeDoer_GiveHediff (drugs/food), CompAbilityEffect_GiveHediff
    // (abilities), surgery outcomes, ritual outcome effecters, genes, scenario parts...
    // The hediff fires once on add (rolling ext.chance) and removes itself next tick.
    // Ready-made defs: PS_AwakeningTrigger / PS_AwakeningTriggerTierII / PS_AwakeningTriggerTierIII.
    public class HediffCompProperties_TriggerAwakening : HediffCompProperties
    {
        public HediffCompProperties_TriggerAwakening() => compClass = typeof(HediffComp_TriggerAwakening);
    }

    public class HediffComp_TriggerAwakening : HediffComp
    {
        private bool fired;
        public override bool CompShouldRemove => fired;

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            if (fired) return;
            fired = true;   // one-shot: consumed whether or not the roll takes
            var ext = parent.def.GetModExtension<AwakeningTriggerExtension>();
            if (Rand.Chance(ext?.chance ?? 1f))
                AwakeningTrigger.Fire(Pawn, ext, "hediff " + parent.def.defName);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref fired, "fired");
        }
    }

    // ---------------------------------------------------------------------------------
    // 2) USE-EFFECT SURFACE - for usable items (CompUsable + this comp): awakening shards,
    // ancient relics, neurotrainer-style consumables. Right-click -> use -> awakening.
    public class CompProperties_UseEffectTriggerAwakening : CompProperties_UseEffect
    {
        public CompProperties_UseEffectTriggerAwakening() => compClass = typeof(CompUseEffect_TriggerAwakening);
    }

    public class CompUseEffect_TriggerAwakening : CompUseEffect
    {
        private AwakeningTriggerExtension Ext => parent.def.GetModExtension<AwakeningTriggerExtension>();

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            if (!AwakeningTrigger.CanFire(p, Ext))
                return "PS_CannotAwaken".Translate().ToString();
            return base.CanBeUsedBy(p);
        }

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);
            var ext = Ext;
            if (Rand.Chance(ext?.chance ?? 1f))
                AwakeningTrigger.Fire(usedBy, ext, "use " + parent.def.defName);
            else if (usedBy.Faction != null && usedBy.Faction.IsPlayer)
                Messages.Message("PS_MsgPsychicStirring".Translate(usedBy.LabelShortCap), usedBy, MessageTypeDefOf.NeutralEvent, false);
        }
    }

    // ---------------------------------------------------------------------------------
    // 3) INCIDENT SURFACE - fire from storytellers, quests, scenario parts, or debug
    // "Do incident". Ships as PS_Awakening (baseChance 0 = never natural). Other mods can
    // define their own IncidentDef with this worker + their own extension numbers.
    // ext.incidentTarget picks the pawn: "meditator" (most cumulative awakening meditation,
    // falling back to random eligible) or "random".
    public class IncidentWorker_TriggerAwakening : IncidentWorker
    {
        private AwakeningTriggerExtension Ext => def.GetModExtension<AwakeningTriggerExtension>();

        protected override bool CanFireNowSub(IncidentParms parms)
            => base.CanFireNowSub(parms) && FindPawn(parms) != null;

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            var p = FindPawn(parms);
            return p != null && AwakeningTrigger.Fire(p, Ext, "incident " + def.defName);
        }

        private Pawn FindPawn(IncidentParms parms)
        {
            var map = parms.target as Map;
            if (map == null) return null;
            var ext = Ext ?? AwakeningTrigger.Default;
            var cands = new List<Pawn>();
            var pawns = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < pawns.Count; i++)
                if (AwakeningTrigger.CanFire(pawns[i], ext)) cands.Add(pawns[i]);
            if (cands.Count == 0) return null;
            if (ext.incidentTarget == "random") return cands.RandomElement();

            // "meditator": deepest cumulative meditation toward awakening wins; ties/zero fall back to random.
            var gc = GameComponent_PsycastSynergies.Instance;
            Pawn best = null; int bestTicks = 0;
            for (int i = 0; i < cands.Count; i++)
            {
                var med = gc?.GetMed(cands[i], false);
                int ticks = med?.awakenMeditationTicks ?? 0;
                if (ticks > bestTicks) { bestTicks = ticks; best = cands[i]; }
            }
            return best ?? cands.RandomElement();
        }
    }

    // ---------------------------------------------------------------------------------
    // 4) GAME-CONDITION SURFACE - while a condition with this class is active on (or above)
    // a map, ext.breakthroughMult multiplies this mod's meditation awakening/breakthrough
    // odds, and ext.chancePerDay rolls spontaneous awakenings (meditators by default).
    // Ships as PS_AwakeningSurge + inert wrapper incident PS_AwakeningSurgeIncident.
    public class GameCondition_AwakeningSurge : GameCondition
    {
    }

    // ---------------------------------------------------------------------------------
    // 5) QUEST-SIGNAL SURFACE - QuestScriptDef authors awaken quest pawns when a signal
    // fires. Pawns come from slate refs and/or the signal's SUBJECT arg.
    //
    //   <li Class="PsycastSynergies.QuestNode_TriggerAwakening">
    //     <inSignal>pilgrim.Arrived</inSignal>
    //     <pawn>$pilgrim</pawn>          <!-- or <pawns>$lodgers</pawns> -->
    //     <tier>1</tier>                 <!-- optional, default 1 -->
    //     <silent>false</silent>         <!-- optional -->
    //   </li>
    public class QuestNode_TriggerAwakening : QuestNode
    {
        [NoTranslate] public SlateRef<string> inSignal;
        public SlateRef<Pawn> pawn;
        public SlateRef<IEnumerable<Pawn>> pawns;
        public SlateRef<int> tier;
        public SlateRef<bool> silent;
        public SlateRef<float> chance;

        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            var slate = QuestGen.slate;
            var part = new QuestPart_TriggerAwakening
            {
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? slate.Get<string>("inSignal"),
                tier = Mathf.Max(1, tier.GetValue(slate)),
                silent = silent.GetValue(slate),
                chance = chance.GetValue(slate) <= 0f ? 1f : chance.GetValue(slate)
            };
            var single = pawn.GetValue(slate);
            if (single != null) part.pawns.Add(single);
            var many = pawns.GetValue(slate);
            if (many != null) part.pawns.AddRange(many);
            QuestGen.quest.AddPart(part);
        }
    }

    public class QuestPart_TriggerAwakening : QuestPart
    {
        public string inSignal;
        public List<Pawn> pawns = new List<Pawn>();
        public int tier = 1;
        public bool silent;
        public float chance = 1f;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag != inSignal) return;

            var targets = new List<Pawn>(pawns);
            if (targets.Count == 0)   // no explicit pawns: take the signal's SUBJECT
            {
                if (signal.args.TryGetArg("SUBJECT", out Pawn subject) && subject != null) targets.Add(subject);
                else if (signal.args.TryGetArg("SUBJECT", out IEnumerable<Pawn> subjects) && subjects != null) targets.AddRange(subjects);
            }
            var ext = new AwakeningTriggerExtension { tier = tier, silent = silent };
            for (int i = 0; i < targets.Count; i++)
                if (targets[i] != null && Rand.Chance(chance))
                    AwakeningTrigger.Fire(targets[i], ext, "quest signal " + inSignal);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Values.Look(ref tier, "tier", 1);
            Scribe_Values.Look(ref silent, "silent");
            Scribe_Values.Look(ref chance, "chance", 1f);
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (pawns == null) pawns = new List<Pawn>();
                pawns.RemoveAll(x => x == null);
            }
        }

        public override void ReplacePawnReferences(Pawn replace, Pawn with)
        {
            base.ReplacePawnReferences(replace, with);
            for (int i = 0; i < pawns.Count; i++)
                if (pawns[i] == replace) pawns[i] = with;
        }
    }
}

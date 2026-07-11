#nullable disable
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using VEF.Abilities;
using AbilityDef = VEF.Abilities.AbilityDef;
using Command_Ability = VEF.Abilities.Command_Ability;

namespace PsycastSynergies
{
    // Free-cast charges for skills whose assigned type is Charges. Transient (not saved - charges
    // refill over time anyway; on load they start full). Regen ticked from the GameComponent.
    public static class ChargeStore
    {
        public const int Cap = 4;   // hard cap on free-cast charges, however many the synergy implies

        private class Entry { public int charges; public int regenTick; }
        private static readonly Dictionary<Pawn, Dictionary<AbilityDef, Entry>> store =
            new Dictionary<Pawn, Dictionary<AbilityDef, Entry>>();

        // Charges contributed by ONE Charges-type synergy source at a given level (0.5 / level).
        public static float SourceCharges(int level) => level * 0.5f;

        // Charges come ONLY from synergy (never from a skill's own level allocation): each
        // Charges-type synergy source gives 0.5 charge/level (×synergy factor), floored, capped.
        public static int Max(Pawn pawn, AbilityDef def)
        {
            var gc = GameComponent_PsycastSynergies.Instance;
            if (pawn == null || def == null || gc == null) return 0;
            float c = 0f;
            float synF = SpecEffects.SynergyFactor(pawn);
            foreach (var src in PsycastInfo.SynergySources(def))
            {
                if (PsycastInfo.EdgeStat(src, def) != SynStat.Charges) continue;
                int lvl = gc.GetLevel(pawn, src);
                if (lvl > 0) c += SourceCharges(lvl) * synF * PsycastInfo.EdgeStrength(src, def);   // hand-tuned edge strength
            }
            return Mathf.Clamp(Mathf.FloorToInt(c), 0, Cap);
        }

        private static Entry GetOrInit(Pawn pawn, AbilityDef def, int max)
        {
            if (!store.TryGetValue(pawn, out var d)) { d = new Dictionary<AbilityDef, Entry>(); store[pawn] = d; }
            if (!d.TryGetValue(def, out var e)) { e = new Entry { charges = max, regenTick = Find.TickManager.TicksGame }; d[def] = e; }
            return e;
        }

        public static int Current(Pawn pawn, AbilityDef def)
        {
            int max = Max(pawn, def);
            if (max <= 0) return 0;
            return Mathf.Min(GetOrInit(pawn, def, max).charges, max);
        }

        public static bool Consume(Pawn pawn, AbilityDef def)
        {
            int max = Max(pawn, def);
            if (max <= 0) return false;
            var e = GetOrInit(pawn, def, max);
            if (e.charges > 0)
            {
                // Start the regen clock the moment we drop below full, so a spent charge actually
                // stays spent for a cooldown instead of being instantly topped up next tick.
                if (e.charges >= max) e.regenTick = Find.TickManager.TicksGame;
                e.charges--;
                return true;
            }
            return false;
        }

        public static void Tick()
        {
            if (store.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            foreach (var kv in store)
            {
                if (kv.Key == null) continue;
                foreach (var inner in kv.Value)
                {
                    var def = inner.Key; var e = inner.Value;
                    int max = Max(kv.Key, def);
                    if (e.charges >= max) { e.regenTick = now; continue; }
                    int cd = Mathf.Max(600, def.cooldownTime * 2);   // ~10s+ per charge so it reads as a resource
                    if (now - e.regenTick >= cd) { e.charges = Mathf.Min(e.charges + 1, max); e.regenTick = now; }
                }
            }
        }
    }

    // Draw the charge count (current/max) in the top-left of the ability gizmo.
    [HarmonyPatch(typeof(Command_Ability), "GizmoOnGUIInt")]
    public static class Patch_ChargeGizmo
    {
        static void Postfix(Command_Ability __instance, Rect butRect)
        {
            try
            {
                var pawn = __instance.pawn;
                var def = __instance.ability?.def;
                if (pawn == null || def == null) return;
                int max = ChargeStore.Max(pawn, def);
                if (max <= 0) return;
                int cur = ChargeStore.Current(pawn, def);

                Rect chip = new Rect(butRect.x + 3f, butRect.yMax - 23f, 26f, 20f);
                GUI.color = new Color(0.04f, 0.06f, 0.09f, 0.92f);
                GUI.DrawTexture(chip, BaseContent.WhiteTex);
                GUI.color = cur > 0 ? new Color(0.4f, 0.8f, 1f, 0.95f) : new Color(0.5f, 0.5f, 0.55f, 0.9f);
                Widgets.DrawBox(chip, 1);
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = cur > 0 ? new Color(0.6f, 0.9f, 1f) : new Color(0.6f, 0.6f, 0.6f);
                Widgets.Label(chip, cur.ToString());
                TooltipHandler.TipRegion(chip, "PS_ChargesTip".Translate(cur, max));
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            }
            catch { }
        }
    }
}

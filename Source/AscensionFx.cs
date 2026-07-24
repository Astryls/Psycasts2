#nullable disable
using RimWorld;
using UnityEngine;
using Verse;

namespace PsycastSynergies
{
    // Drives an Apotheosis path's ambient effects: vanilla flecks (leaves / glints / wisps) emitted
    // each interval, plus a very slight colored CompGlower carried by an invisible follower thing.
    // Both respect the per-pawn "Psychic aura" toggle (SpecData.aurasDisabled).
    public class HediffCompProperties_AscensionFx : HediffCompProperties
    {
        public string path;   // tranquil | archotech | umbral | empyrean | pandemonium | chronos | consonance
        public HediffCompProperties_AscensionFx() { compClass = typeof(HediffComp_AscensionFx); }
    }

    public class HediffComp_AscensionFx : HediffComp
    {
        private HediffCompProperties_AscensionFx Props => (HediffCompProperties_AscensionFx)props;

        private Thing glower;
        private IntVec3 glowCell = IntVec3.Invalid;
        private int lastTick = -1;
        // The per-pawn "Psychic aura" toggle is read on a 15-tick cadence instead of every tick
        // (a dictionary probe per aura pawn per tick adds up); a 0.25s latency on a cosmetic
        // toggle is imperceptible.
        private bool aurasOff;
        private int aurasCheckAt = -1;

        // 1.6 ticks comps through CompPostTickInterval; CompPostTick is legacy and not forwarded.
        // Override both, guarded so the body runs exactly once per game tick.
        public override void CompPostTick(ref float severityAdjustment) => Run();
        public override void CompPostTickInterval(ref float severityAdjustment, int delta) => Run();

        private void Run()
        {
            int now = Find.TickManager.TicksGame;
            if (now == lastTick) return;
            lastTick = now;

            var pawn = parent.pawn;
            if (pawn == null || !pawn.Spawned || pawn.Map == null) { ClearGlower(); return; }

            if (now >= aurasCheckAt)
            {
                aurasCheckAt = now + 15;
                aurasOff = GameComponent_PsycastSynergies.Instance?.GetSpec(pawn)?.aurasDisabled == true;
            }
            if (aurasOff) { ClearGlower(); return; }

            float max = parent.def.maxSeverity > 0f ? parent.def.maxSeverity : 1f;
            float frac = Mathf.Clamp01(parent.Severity / max);

            UpdateGlower(pawn);
            if (pawn.IsHashIntervalTick(30)) EmitFlecks(pawn, frac);
        }

        public override void CompPostPostRemoved() { ClearGlower(); }

        // ---- glower follower (one ThingDef per path; color baked in) -------------------------
        private ThingDef GlowDef()
        {
            switch (Props.path)
            {
                case "archotech":   return PsycastDefOf.PS_AscensionGlow_Transcendence;
                case "umbral":      return PsycastDefOf.PS_AscensionGlow_Umbral;
                case "empyrean":    return PsycastDefOf.PS_AscensionGlow_Empyrean;
                case "pandemonium": return PsycastDefOf.PS_AscensionGlow_Pandemonium;
                case "chronos":     return PsycastDefOf.PS_AscensionGlow_Chronos;
                case "consonance":  return PsycastDefOf.PS_AscensionGlow_Consonance;
                default:            return PsycastDefOf.PS_AscensionGlow_Tranquil;
            }
        }

        private void UpdateGlower(Pawn pawn)
        {
            var def = GlowDef();
            if (glower != null && (glower.Destroyed || !glower.Spawned || glower.def != def)) ClearGlower();
            if (glower == null)
            {
                glower = ThingMaker.MakeThing(def);
                GenSpawn.Spawn(glower, pawn.Position, pawn.Map);
                glowCell = pawn.Position;
            }
            else if (glowCell != pawn.Position)
            {
                glower.DeSpawn();
                GenSpawn.Spawn(glower, pawn.Position, pawn.Map);
                glowCell = pawn.Position;
            }
        }

        private void ClearGlower()
        {
            if (glower != null && glower.Spawned) glower.DeSpawn();
            glower = null;
            glowCell = IntVec3.Invalid;
        }

        // ---- flecks -------------------------------------------------------------------------
        private void EmitFlecks(Pawn pawn, float frac)
        {
            var map = pawn.Map;
            // Snowfall feel: usually one flake, a second only occasionally at higher completion.
            int n = 1 + (Rand.Chance(0.2f + 0.3f * frac) ? 1 : 0);

            for (int i = 0; i < n; i++)
            {
                // Spawn above & around the pawn; drift straight down and fade out before the ground.
                Vector3 pos = pawn.DrawPos
                    + Polar(Rand.Range(0f, 360f), Rand.Range(0.15f, 0.8f))
                    + new Vector3(0f, 0f, Rand.Range(0.45f, 1.05f));
                float fall = Rand.Range(165f, 195f);   // ~straight down (180 = down)
                switch (Props.path)
                {
                    case "archotech":
                        // Falling binary glyphs (0/1): small + slow, kept near-upright so the digit reads.
                        Emit(map, Rand.Bool ? PsycastDefOf.PS_Fleck_Glyph1 : PsycastDefOf.PS_Fleck_Glyph0, pos,
                            Rand.Range(0.15f, 0.24f), new Color(1f, 0.84f, 0.42f), fall, Rand.Range(0.12f, 0.22f), 0f, Rand.Range(-7f, 7f));
                        break;
                    case "umbral":
                        Emit(map, PsycastDefOf.PS_Fleck_Wisp, pos, Rand.Range(0.24f, 0.38f),
                            new Color(0.75f, 0.55f, 1f), fall, Rand.Range(0.25f, 0.45f), Rand.Range(-18f, 18f), Rand.Range(0f, 360f));
                        break;
                    case "empyrean":
                        Emit(map, PsycastDefOf.PS_Fleck_Dot, pos, Rand.Range(0.14f, 0.24f),
                            new Color(0.6f, 0.78f, 1f), fall, Rand.Range(0.18f, 0.34f), Rand.Range(-8f, 8f), Rand.Range(0f, 360f));
                        break;
                    case "pandemonium":
                        Emit(map, PsycastDefOf.PS_Fleck_Dot, pos, Rand.Range(0.16f, 0.28f),
                            new Color(1f, 0.42f, 0.45f), fall, Rand.Range(0.3f, 0.55f), Rand.Range(-30f, 30f), Rand.Range(0f, 360f));
                        break;
                    case "chronos":
                        Emit(map, PsycastDefOf.PS_Fleck_Dot, pos, Rand.Range(0.16f, 0.26f),
                            new Color(0.4f, 0.92f, 0.86f), fall, Rand.Range(0.16f, 0.3f), Rand.Range(-6f, 6f), Rand.Range(0f, 360f));
                        break;
                    case "consonance":
                        Emit(map, PsycastDefOf.PS_Fleck_Dot, pos, Rand.Range(0.16f, 0.28f),
                            new Color(1f, 0.66f, 0.86f), fall, Rand.Range(0.2f, 0.4f), Rand.Range(-12f, 12f), Rand.Range(0f, 360f));
                        break;
                    default: // tranquil
                        Emit(map, PsycastDefOf.PS_Fleck_Leaf, pos, Rand.Range(0.24f, 0.38f),
                            new Color(0.62f, 1f, 0.66f), fall, Rand.Range(0.22f, 0.4f), Rand.Range(-25f, 25f), Rand.Range(0f, 360f));
                        break;
                }
            }
        }

        // Vanilla emission pattern (GetDataStatic sets ageTicksOverride=-1; angle 0 = up).
        private static void Emit(Map map, FleckDef def, Vector3 pos, float scale, Color color, float velAngle, float velSpeed, float rotRate, float rot)
        {
            if (def == null) return;
            var fc = FleckMaker.GetDataStatic(pos, map, def, scale);
            fc.instanceColor = color;
            fc.rotation = rot;
            fc.velocityAngle = velAngle;
            fc.velocitySpeed = velSpeed;
            fc.rotationRate = rotRate;
            map.flecks.CreateFleck(fc);
        }

        private static Vector3 Polar(float angleDeg, float r)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(rad) * r, 0f, Mathf.Sin(rad) * r);
        }
        private static Vector3 Ring(float r) => Polar(Rand.Range(0f, 360f), Rand.Range(0f, r));
    }
}

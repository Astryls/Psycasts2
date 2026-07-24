#nullable disable
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using VanillaPsycastsExpanded;
using VEF.Abilities;
using AbilityDef = VEF.Abilities.AbilityDef;

namespace PsycastSynergies
{
    // Celebration bursts for skill investment, modeled on the Window_Awakening card-reveal burst
    // (glow flash + expanding ring + golden-ratio spark spray). Three moments, three grades:
    //   Invest       - every level applied to a psycast (small cyan pop on the ability icon)
    //   Mastery      - the invest that reaches the ABSOLUTE cap (maxSkillLevel + Convergence bonus,
    //                  i.e. 10 or 15): grandiose gold, double ring, lingering border, map-side
    //                  shimmer + fleck at the pawn, and a mastery message
    //   SpecNode     - each specialization node committed in the constellation window (blue pop)
    //   SpecCapstone - committed capstones/Convergence/ascension capstones: gold, grandiose
    // Draw sites poll Draw(rect, key) every repaint; the registry holds only live bursts.
    // All visuals + the extra psychic sounds are gated by the skillFx setting (legacy Tick_High
    // click feedback is used when it is off). Textures reuse UI/Sparkle + UI/Glow from the cards.
    public static class SkillFx
    {
        public enum Grade { Invest, Mastery, SpecNode, SpecCapstone }

        private struct Burst { public float start; public Grade grade; }

        private static readonly Dictionary<string, Burst> active = new Dictionary<string, Burst>();
        private static readonly List<string> sweep = new List<string>();

        private static Texture2D sparkTex, glowTex, ringTex;
        private static Texture2D Spark => sparkTex != null ? sparkTex : (sparkTex = ContentFinder<Texture2D>.Get("UI/Sparkle", false));
        private static Texture2D Glow => glowTex != null ? glowTex : (glowTex = ContentFinder<Texture2D>.Get("UI/Glow", false));
        private static Texture2D Ring => ringTex != null ? ringTex : (ringTex = ContentFinder<Texture2D>.Get("UI/Specs/node_ring", false));

        private static SoundDef sndSmall, sndPulse, sndShimmer;
        private static bool sndInit;
        private static void EnsureSounds()
        {
            if (sndInit) return;
            sndInit = true;
            sndSmall = DefDatabase<SoundDef>.GetNamedSilentFail("Psycast_Skip_Entry");
            sndPulse = DefDatabase<SoundDef>.GetNamedSilentFail("Psycast_Skip_Pulse");
            sndShimmer = DefDatabase<SoundDef>.GetNamedSilentFail("PsycastPsychicEffect");
        }

        public static bool Enabled => PsycastSynergiesMod.Settings == null || PsycastSynergiesMod.Settings.skillFx;
        public static bool AnyActive => active.Count > 0;

        public static string KeySkill(Pawn p, AbilityDef d) => (p?.thingIDNumber ?? 0) + ":" + (d?.defName ?? "?");
        public static string KeySpec(Pawn p, string id) => "spec:" + (p?.thingIDNumber ?? 0) + ":" + id;

        public static void Trigger(string key, Grade g)
        {
            if (!Enabled || key == null) return;
            float now = Time.realtimeSinceStartup;
            // Sweep stale entries (burst drawn never/partially because its icon scrolled away).
            sweep.Clear();
            foreach (var kv in active) if (now - kv.Value.start > 5f) sweep.Add(kv.Key);
            for (int i = 0; i < sweep.Count; i++) active.Remove(sweep[i]);
            active[key] = new Burst { start = now, grade = g };
        }

        // Central invest handler: decides small pop vs mastery flare. newLvl is the level just
        // reached. Mastery keys off the ABSOLUTE cap (settings cap + Convergence bonus), NOT the
        // psycaster-level-gated interim cap from SkillSystem.MaxLevel - hitting the interim
        // ceiling is routine; hitting 10 (or 15) is the celebration.
        public static void OnSkillInvest(Pawn pawn, AbilityDef ability, int newLvl)
        {
            var s = PsycastSynergiesMod.Settings;
            int absCap = (s?.maxSkillLevel ?? 10) + SpecEffects.LevelCapBonus(pawn);
            if (newLvl >= absCap)
            {
                Trigger(KeySkill(pawn, ability), Grade.Mastery);
                MasteryFanfare(pawn);
                if (PawnUtility.ShouldSendNotificationAbout(pawn))
                    Messages.Message("PS_MsgSkillMastered".Translate(pawn.LabelShortCap, ability.LabelCap, newLvl),
                        pawn, MessageTypeDefOf.PositiveEvent, false);
            }
            else
            {
                Trigger(KeySkill(pawn, ability), Grade.Invest);
                SmallNoise(pawn);
            }
        }

        // Small invest noise: psychic skip-whoosh at the pawn (the tab floats over the map).
        // Falls back to the legacy Tick_High when FX are off or the pawn is not on a map.
        public static void SmallNoise(Pawn pawn)
        {
            EnsureSounds();
            if (!Enabled || !PlayAt(pawn, sndSmall)) SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        // Grandiose map-side moment: shimmer + pulse at the pawn and a psychic fleck, so the
        // mastery reads even outside the tab.
        public static void MasteryFanfare(Pawn pawn)
        {
            EnsureSounds();
            if (!Enabled) { SoundDefOf.Tick_High.PlayOneShotOnCamera(); return; }
            bool played = PlayAt(pawn, sndShimmer);
            played |= PlayAt(pawn, sndPulse);
            if (!played) SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
            if (pawn != null && pawn.Spawned && pawn.Map != null && FleckDefOf.PsycastAreaEffect != null)
                FleckMaker.Static(pawn.Position, pawn.Map, FleckDefOf.PsycastAreaEffect, 1.6f);
        }

        // Lesser map pulse for committed spec capstones (no fleck, no shimmer tail).
        public static void MinorPulse(Pawn pawn)
        {
            EnsureSounds();
            if (!Enabled) return;
            PlayAt(pawn, sndPulse);
        }

        // These psychic SoundDefs are context=MapOnly, so they must play at the pawn's map cell.
        private static bool PlayAt(Pawn pawn, SoundDef snd)
        {
            if (snd == null || pawn == null || !pawn.Spawned || pawn.Map == null) return false;
            snd.PlayOneShot(SoundInfo.InMap(new TargetInfo(pawn.Position, pawn.Map)));
            return true;
        }

        // ---- drawing ----

        // Zero-allocation fast path for per-icon draw loops: the string key is only built when a
        // burst is actually live somewhere (rare) - the common case exits on a count check before
        // any concatenation happens.
        public static void Draw(Rect r, Pawn p, AbilityDef d)
        {
            if (active.Count == 0) return;
            Draw(r, KeySkill(p, d));
        }

        public static void Draw(Rect r, string key)
        {
            if (active.Count == 0 || key == null) return;
            if (Event.current != null && Event.current.type != EventType.Repaint) return;
            if (!active.TryGetValue(key, out var b)) return;

            Dur(b.grade, out float dur, out int sparks, out int rings, out float glowMax,
                out float radMul, out bool linger, out bool round, out Color col);
            float bt = (Time.realtimeSinceStartup - b.start) / dur;
            if (bt >= 1f) { active.Remove(key); return; }

            var c = r.center;
            float baseDim = Mathf.Min(r.width, r.height);
            float maxRad = baseDim * 0.44f * radMul;
            // Sparks were authored against ~230px cards; scale them down for small tab icons.
            float sizeScale = Mathf.Clamp(baseDim / 180f, 0.4f, 1.3f);
            Color flashCol = Color.Lerp(col, Color.white, 0.35f);
            Color sparkCol = Color.Lerp(col, Color.white, 0.5f);

            if (Glow != null)
            {
                float flash = Mathf.Clamp01(1f - bt * 1.4f);
                float gs = Mathf.Lerp(0.25f, glowMax, bt) * baseDim;
                GUI.color = new Color(flashCol.r, flashCol.g, flashCol.b, flash * 0.8f);
                GUI.DrawTexture(new Rect(c.x - gs / 2f, c.y - gs / 2f, gs, gs), Glow);
            }

            // Expanding shockwave ring(s); second ring trails the first for the grand grades.
            // Constellation nodes are ROUND - their shockwave uses the node ring texture, never a
            // rectangular box (square icons keep the box ring, matching the card window).
            for (int i = 0; i < rings; i++)
            {
                float rt = Mathf.Clamp01((bt - 0.14f * i) / (1f - 0.14f * i));
                if (rt <= 0f) continue;
                float rr = rt * maxRad * 1.05f;
                float ra = Mathf.Clamp01(1f - rt);
                if (round)
                {
                    if (Ring != null)
                    {
                        GUI.color = new Color(col.r, col.g, col.b, ra * 0.75f);
                        GUI.DrawTexture(new Rect(c.x - rr, c.y - rr, rr * 2f, rr * 2f), Ring);
                    }
                }
                else
                {
                    GUI.color = new Color(col.r, col.g, col.b, ra * 0.45f);
                    Widgets.DrawBox(new Rect(c.x - rr, c.y - rr, rr * 2f, rr * 2f), 2);
                }
            }
            GUI.color = Color.white;

            if (Spark != null)
            {
                for (int k = 0; k < sparks; k++)
                {
                    float f = Frac(k * 0.61803399f);
                    float ang = (k * (360f / sparks) + 20f * f) * Mathf.Deg2Rad;
                    float speed = 0.55f + 0.45f * Frac(f * 7.3f);
                    float delay = 0.18f * Frac(f * 3.7f);
                    float lt = Mathf.Clamp01((bt - delay) / (1f - delay));
                    if (lt <= 0f) continue;
                    float rad = lt * maxRad * speed;
                    var pos = new Vector2(c.x + Mathf.Cos(ang) * rad, c.y + Mathf.Sin(ang) * rad);
                    float sz = Mathf.Lerp(8f, 22f, lt) * (0.55f + 0.75f * Frac(f * 3.1f)) * sizeScale;
                    float rot = lt * 220f * (k % 2 == 0 ? 1f : -1f);
                    DrawTexRot(Spark, pos, sz, rot, new Color(sparkCol.r, sparkCol.g, sparkCol.b, Mathf.Clamp01(1f - lt)));
                }
            }

            // Mastery grades: lingering pulse over the tail of the burst. Square icons get the
            // pulsing gold border; round constellation nodes get a pulsing gold ring instead
            // (no rectangular box around a round node).
            if (linger && bt > 0.3f)
            {
                float la = Mathf.Clamp01((1f - bt) / 0.7f) * (0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 7f)));
                if (round)
                {
                    if (Ring != null)
                    {
                        float g = baseDim * (1.14f + 0.05f * Mathf.Sin(Time.realtimeSinceStartup * 7f));
                        GUI.color = new Color(col.r, col.g, col.b, la);
                        GUI.DrawTexture(new Rect(c.x - g / 2f, c.y - g / 2f, g, g), Ring);
                    }
                }
                else
                {
                    GUI.color = new Color(col.r, col.g, col.b, la * 0.35f);
                    Widgets.DrawBox(r.ExpandedBy(4f), 2);
                    GUI.color = new Color(col.r, col.g, col.b, la);
                    Widgets.DrawBox(r.ExpandedBy(1f), 2);
                }
            }
            GUI.color = Color.white;
        }

        private static void Dur(Grade g, out float dur, out int sparks, out int rings,
            out float glowMax, out float radMul, out bool linger, out bool round, out Color col)
        {
            switch (g)
            {
                case Grade.Mastery:
                    dur = 1.4f; sparks = 26; rings = 2; glowMax = 2.1f; radMul = 1.6f; linger = true; round = false;
                    col = new Color(1f, 0.85f, 0.3f);   // matches the at-cap numeral gold
                    return;
                case Grade.SpecCapstone:
                    dur = 1.25f; sparks = 24; rings = 2; glowMax = 1.9f; radMul = 1.45f; linger = true; round = true;
                    col = new Color(0.96f, 0.81f, 0.36f);   // card-window gold
                    return;
                case Grade.SpecNode:
                    dur = 0.6f; sparks = 12; rings = 1; glowMax = 1.15f; radMul = 1.05f; linger = false; round = true;
                    col = new Color(0.5f, 0.8f, 1f);   // tree lit-line blue
                    return;
                default:
                    dur = 0.55f; sparks = 10; rings = 1; glowMax = 1.1f; radMul = 1f; linger = false; round = false;
                    col = new Color(0.6f, 0.88f, 1f);   // level-chip cyan
                    return;
            }
        }

        // ---- mastered trees (every ability in the path at the absolute max level) ----

        private static int mtFrame = -1;
        private static readonly Dictionary<long, bool> mtCache = new Dictionary<long, bool>();
        private static Pawn mtPawn;
        private static readonly HashSet<AbilityDef> mtLearned = new HashSet<AbilityDef>();

        // True when the pawn has EVERY ability of this path learned and at the absolute cap
        // (maxSkillLevel + Convergence bonus). Cached per (pawn, path) per frame - tree cards
        // poll this every repaint.
        public static bool TreeMastered(Pawn pawn, CompAbilities comp, PsycasterPathDef path)
        {
            if (!Enabled || pawn == null || comp == null || path == null) return false;
            var gc = GameComponent_PsycastSynergies.Instance;
            if (gc == null) return false;
            if (Time.frameCount != mtFrame) { mtFrame = Time.frameCount; mtCache.Clear(); mtPawn = null; }
            long key = ((long)pawn.thingIDNumber << 16) | path.shortHash;
            if (mtCache.TryGetValue(key, out bool m)) return m;
            if (mtPawn != pawn)
            {
                mtPawn = pawn; mtLearned.Clear();
                if (comp.LearnedAbilities != null)
                    foreach (var a in comp.LearnedAbilities) if (a?.def != null) mtLearned.Add(a.def);
            }
            m = ComputeMastered(pawn, gc, path);
            mtCache[key] = m;
            return m;
        }

        private static bool ComputeMastered(Pawn pawn, GameComponent_PsycastSynergies gc, PsycasterPathDef path)
        {
            var abs = path.abilities;
            if (abs == null) return false;
            var s = PsycastSynergiesMod.Settings;
            int absCap = (s?.maxSkillLevel ?? 10) + SpecEffects.LevelCapBonus(pawn);
            int real = 0;
            foreach (var ab in abs)
            {
                if (ab == null || ab == PsycasterPathDef.Blank) continue;
                real++;
                if (!mtLearned.Contains(ab)) return false;
                if (gc.GetLevel(pawn, ab) < absCap) return false;
            }
            return real > 0;
        }

        // Persistent celebration on a mastered tree card: pulsing gold card glow (the card-window
        // chosen-card gold) + ambient sparkle flecks looping over the card, deterministic per tree.
        public static void DrawMasteredTree(Rect tile, PsycasterPathDef def)
        {
            if (Event.current != null && Event.current.type != EventType.Repaint) return;
            float now = Time.realtimeSinceStartup;
            int seed = def != null ? def.defName.GetHashCode() : 0;
            float pulse = 0.62f + 0.38f * Mathf.Sin(now * 2.6f + (seed & 0xFF) * 0.13f);
            var gold = new Color(0.96f, 0.81f, 0.36f);
            GUI.color = new Color(gold.r, gold.g, gold.b, 0.14f * pulse); Widgets.DrawBox(tile.ExpandedBy(5f), 2);
            GUI.color = new Color(gold.r, gold.g, gold.b, 0.34f * pulse); Widgets.DrawBox(tile.ExpandedBy(3f), 2);
            GUI.color = new Color(gold.r, gold.g, gold.b, 0.50f + 0.50f * pulse); Widgets.DrawBox(tile.ExpandedBy(1f), 2);
            GUI.color = Color.white;

            if (Spark != null)
            {
                const int N = 7;
                const float cycle = 2.8f;
                for (int k = 0; k < N; k++)
                {
                    float f0 = Frac(Mathf.Abs(seed % 1000) * 0.001618f + k * 0.61803399f);
                    float p = Frac(now / cycle + f0);                  // staggered life phase 0..1
                    float a = Mathf.Sin(p * Mathf.PI);                 // fade in, peak, fade out
                    float px = tile.x + Frac(f0 * 7.31f + k * 0.173f) * tile.width;
                    float py = tile.y + 6f + Frac(f0 * 3.77f) * (tile.height - 30f) - p * 10f;   // slow rise
                    float sz = (7f + 9f * Frac(f0 * 5.19f)) * (0.8f + 0.4f * a);
                    float rot = (now * 40f + k * 51f) * ((k & 1) == 0 ? 1f : -1f);
                    DrawTexRot(Spark, new Vector2(px, py), sz, rot, new Color(1f, 0.93f, 0.62f, 0.85f * a));
                }
            }
            GUI.color = Color.white;
        }

        private static float Frac(float x) => x - Mathf.Floor(x);

        private static void DrawTexRot(Texture2D tex, Vector2 center, float size, float angle, Color col)
        {
            var rect = new Rect(center.x - size / 2f, center.y - size / 2f, size, size);
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, center);
            GUI.color = col;
            GUI.DrawTexture(rect, tex);
            GUI.color = Color.white;
            GUI.matrix = m;
        }
    }
}

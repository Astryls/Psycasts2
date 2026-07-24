#nullable disable
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PsycastSynergies
{
    // Tick/frame-scoped memoization for the mod's hot math (StatMultiplier, MaxLevel, charge
    // maxima, enlightenment tier). Values are cached only within the CURRENT game tick + render
    // frame + mutation version, so cross-tick staleness is impossible by construction: any change
    // (level invest, spec commit, tier change, balance edit, settings write) either bumps the
    // version explicitly or is visible on the next tick/frame anyway. What this kills is the
    // REPETITION - the psycast tab asks for the same multiplier dozens of times per frame (per
    // icon, per IMGUI pass), gizmos re-ask every frame, and a single cast reads the same numbers
    // through several getter patches.
    //
    // Gated by the "High performance caching" setting (default ON) as a diagnostic escape hatch:
    // when off, every Try* misses and every Put* no-ops, restoring the fully-recomputed behavior.
    public static class PerfCache
    {
        // ---- mutation version ------------------------------------------------------------
        private static int version;
        public static int Version => version;

        // Call after ANY mutation that changes multiplier/cap/tier inputs mid-frame:
        // skill level writes, spec commits/resets, tier changes, balance-editor edits,
        // settings writes. Cheap (one int increment + next-access clear).
        public static void Bump() => version++;

        private static bool Enabled => PsycastSynergiesMod.Settings == null || PsycastSynergiesMod.Settings.perfCaching;

        // ---- stamp + stores --------------------------------------------------------------
        private static int stampFrame = -1, stampTick = -1, stampVersion = -1;

        private static readonly Dictionary<long, float> mult = new Dictionary<long, float>();     // (pawn,def,stat) -> multiplier
        private static readonly Dictionary<long, long> maxLevel = new Dictionary<long, long>();   // (pawn,def) -> packed (psy, cap)
        private static readonly Dictionary<long, int> chargeMax = new Dictionary<long, int>();    // (pawn,def) -> max charges
        private static readonly Dictionary<int, int> tier = new Dictionary<int, int>();           // pawnId -> enlightenment tier

        private static void EnsureFresh()
        {
            int f = Time.frameCount;
            int t = Current.Game != null ? Find.TickManager.TicksGame : 0;
            if (f == stampFrame && t == stampTick && version == stampVersion) return;
            stampFrame = f; stampTick = t; stampVersion = version;
            mult.Clear(); maxLevel.Clear(); chargeMax.Clear(); tier.Clear();
        }

        // Key layout: pawn id in the high 32 bits; def shortHash (unique per def type) in bits
        // 8..23; a small stat/slot code in bits 0..7.
        private static long Key(Pawn p, Def d, int slot)
            => ((long)p.thingIDNumber << 32) | ((uint)d.shortHash << 8) | (uint)(slot & 0xFF);

        // ---- typed accessors ---------------------------------------------------------------
        public static bool TryMult(Pawn p, Def d, int stat, out float v)
        {
            v = 1f;
            if (!Enabled || p == null || d == null) return false;
            EnsureFresh();
            return mult.TryGetValue(Key(p, d, stat), out v);
        }

        public static void PutMult(Pawn p, Def d, int stat, float v)
        {
            if (!Enabled || p == null || d == null) return;
            mult[Key(p, d, stat)] = v;
        }

        // MaxLevel depends on the caller-supplied psycaster level, so the stored value packs the
        // psy it was computed for - a hit requires the same psy (different psy = recompute).
        public static bool TryMaxLevel(Pawn p, Def d, int psy, out int cap)
        {
            cap = 0;
            if (!Enabled || p == null || d == null) return false;
            EnsureFresh();
            if (!maxLevel.TryGetValue(Key(p, d, 0), out long packed)) return false;
            if ((int)(packed >> 32) != psy) return false;
            cap = (int)packed;
            return true;
        }

        public static void PutMaxLevel(Pawn p, Def d, int psy, int cap)
        {
            if (!Enabled || p == null || d == null) return;
            maxLevel[Key(p, d, 0)] = ((long)psy << 32) | (uint)cap;
        }

        public static bool TryChargeMax(Pawn p, Def d, out int v)
        {
            v = 0;
            if (!Enabled || p == null || d == null) return false;
            EnsureFresh();
            return chargeMax.TryGetValue(Key(p, d, 0), out v);
        }

        public static void PutChargeMax(Pawn p, Def d, int v)
        {
            if (!Enabled || p == null || d == null) return;
            chargeMax[Key(p, d, 0)] = v;
        }

        public static bool TryTier(Pawn p, out int t)
        {
            t = 0;
            if (!Enabled || p == null) return false;
            EnsureFresh();
            return tier.TryGetValue(p.thingIDNumber, out t);
        }

        public static void PutTier(Pawn p, int t)
        {
            if (!Enabled || p == null) return;
            tier[p.thingIDNumber] = t;
        }

        // ---- language-safe translated-string cache -----------------------------------------
        // .Translate() does a dictionary lookup + TaggedString allocation per call; per-frame UI
        // labels should resolve once and reuse. Re-resolves automatically if the active language
        // changes (reference compare per access, so it can never serve the wrong language).
        public sealed class LangCache
        {
            private readonly string key;
            private string val;
            private LoadedLanguage lang;

            public LangCache(string key) { this.key = key; }

            public string Value
            {
                get
                {
                    var l = LanguageDatabase.activeLanguage;
                    if (!ReferenceEquals(l, lang)) { lang = l; val = key.Translate(); }
                    return val;
                }
            }

            public static implicit operator string(LangCache c) => c.Value;
        }
    }
}

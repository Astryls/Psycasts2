#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PsycastSynergies
{
    // The PLAYER'S in-game balance edits (the psycast-tab dev tool), stored in mod settings so
    // they persist across saves/launches and apply to every game. Highest layer of the synergy
    // stack:  PlayerTuning  >  ManualBalance.json (baked defaults)  >  frozen auto graph.
    //   prim / primStr : ability defName        -> primary stat int (-1 = none) / strength mult
    //   edge / edgeStr : "src|tgt" defName pair -> edge stat int (-1 = none) / strength mult
    //   srcs           : target ability defName -> FULL replacement source list ("A;B;C")
    // The editor writes an override only when the pick differs from the baseline (PsycastInfo.Base*),
    // so Count doubles as the "N edits" badge, and a later bake can read these dicts 1:1 into
    // ManualBalance.json records.
    public static class PlayerTuning
    {
        private static PsycastSynergiesSettings S => PsycastSynergiesMod.Settings;

        public static string Key(string src, string tgt) => src + "|" + tgt;

        public static int Count => S == null ? 0
            : S.tunePrimStat.Count + S.tunePrimStr.Count + S.tuneEdgeStat.Count + S.tuneEdgeStr.Count + S.tuneSrcs.Count;

        // ── reads (PsycastInfo consults these FIRST) ─────────────────────────
        public static bool TryPrim(string def, out int stat)
        { stat = -1; return S != null && def != null && S.tunePrimStat.TryGetValue(def, out stat); }

        public static bool TryPrimStr(string def, out float str)
        { str = 1f; return S != null && def != null && S.tunePrimStr.TryGetValue(def, out str); }

        public static bool TryEdge(string src, string tgt, out int stat)
        { stat = -1; return S != null && src != null && tgt != null && S.tuneEdgeStat.TryGetValue(Key(src, tgt), out stat); }

        public static bool TryEdgeStr(string src, string tgt, out float str)
        { str = 1f; return S != null && src != null && tgt != null && S.tuneEdgeStr.TryGetValue(Key(src, tgt), out str); }

        public static bool TrySrcs(string def, out List<string> srcs)
        {
            srcs = null;
            if (S == null || def == null || !S.tuneSrcs.TryGetValue(def, out string joined)) return false;
            srcs = SplitList(joined);
            return true;
        }

        // Shared with ManualBalance's "srcs" records: semicolon-joined defName list.
        public static List<string> SplitList(string joined)
            => string.IsNullOrEmpty(joined)
                ? new List<string>()
                : joined.Split(';').Where(x => !string.IsNullOrEmpty(x)).ToList();

        // ── editor state queries (chip "modified" markers) ───────────────────
        public static bool HasPrim(string def) => S != null && S.tunePrimStat.ContainsKey(def);
        public static bool HasPrimStr(string def) => S != null && S.tunePrimStr.ContainsKey(def);
        public static bool HasEdge(string src, string tgt) => S != null && S.tuneEdgeStat.ContainsKey(Key(src, tgt));
        public static bool HasEdgeStr(string src, string tgt) => S != null && S.tuneEdgeStr.ContainsKey(Key(src, tgt));
        public static bool HasSrcs(string def) => S != null && S.tuneSrcs.ContainsKey(def);

        // ── writes (balance editor only). Every mutation invalidates the live caches. ──
        public static void SetPrim(string def, int stat) { S.tunePrimStat[def] = stat; Dirty(); }
        public static void ClearPrim(string def) { S.tunePrimStat.Remove(def); Dirty(); }
        public static void SetPrimStr(string def, float str) { S.tunePrimStr[def] = str; Dirty(); }
        public static void ClearPrimStr(string def) { S.tunePrimStr.Remove(def); Dirty(); }
        public static void SetEdge(string src, string tgt, int stat) { S.tuneEdgeStat[Key(src, tgt)] = stat; Dirty(); }
        public static void ClearEdge(string src, string tgt) { S.tuneEdgeStat.Remove(Key(src, tgt)); Dirty(); }
        public static void SetEdgeStr(string src, string tgt, float str) { S.tuneEdgeStr[Key(src, tgt)] = str; Dirty(); }
        public static void ClearEdgeStr(string src, string tgt) { S.tuneEdgeStr.Remove(Key(src, tgt)); Dirty(); }
        public static void SetSrcs(string def, List<string> srcs) { S.tuneSrcs[def] = string.Join(";", srcs); Dirty(); }
        public static void ClearSrcs(string def) { S.tuneSrcs.Remove(def); Dirty(); }

        // Drop every per-edge override touching the src->tgt pair (used when the pair leaves the graph).
        public static void PurgeEdge(string src, string tgt)
        { S.tuneEdgeStat.Remove(Key(src, tgt)); S.tuneEdgeStr.Remove(Key(src, tgt)); Dirty(); }

        // Reset every edit that touches any of the given abilities (either side of an edge).
        public static void ResetDefs(ICollection<string> defs)
        {
            if (S == null) return;
            RemoveKeys(S.tunePrimStat, defs.Contains);
            RemoveKeys(S.tunePrimStr, defs.Contains);
            RemoveKeys(S.tuneSrcs, defs.Contains);
            Func<string, bool> touches = k =>
            {
                int i = k.IndexOf('|');
                return i > 0 && (defs.Contains(k.Substring(0, i)) || defs.Contains(k.Substring(i + 1)));
            };
            RemoveKeys(S.tuneEdgeStat, touches);
            RemoveKeys(S.tuneEdgeStr, touches);
            Dirty();
        }

        public static void ResetAll()
        {
            if (S == null) return;
            S.tunePrimStat.Clear(); S.tunePrimStr.Clear();
            S.tuneEdgeStat.Clear(); S.tuneEdgeStr.Clear(); S.tuneSrcs.Clear();
            Dirty();
        }

        // Count of edits touching any of the given abilities (path-rail badge).
        public static int CountFor(ICollection<string> defs)
        {
            if (S == null) return 0;
            int n = 0;
            foreach (var k in S.tunePrimStat.Keys) if (defs.Contains(k)) n++;
            foreach (var k in S.tunePrimStr.Keys) if (defs.Contains(k)) n++;
            foreach (var k in S.tuneSrcs.Keys) if (defs.Contains(k)) n++;
            foreach (var k in S.tuneEdgeStat.Keys) if (EdgeTouches(k, defs)) n++;
            foreach (var k in S.tuneEdgeStr.Keys) if (EdgeTouches(k, defs)) n++;
            return n;
        }

        private static bool EdgeTouches(string key, ICollection<string> defs)
        {
            int i = key.IndexOf('|');
            return i > 0 && (defs.Contains(key.Substring(0, i)) || defs.Contains(key.Substring(i + 1)));
        }

        private static void RemoveKeys<T>(Dictionary<string, T> d, Func<string, bool> pred)
        {
            List<string> kill = null;
            foreach (var k in d.Keys) if (pred(k)) (kill ?? (kill = new List<string>())).Add(k);
            if (kill != null) foreach (var k in kill) d.Remove(k);
        }

        private static void Dirty() => PsycastInfo.ClearCaches();
    }
}

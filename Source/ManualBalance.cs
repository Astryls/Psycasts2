#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Verse;

namespace PsycastSynergies
{
    // Hand-tuned balance OVERLAY. The auto synergy graph (frozenSyn in ModSettings) stays pristine;
    // this layers on top of it: per-ability primary-stat + primary-strength overrides, per-edge
    // stat + strength overrides, and full source-list replacements. Loaded once at startup from
    // <modRoot>/ManualBalance.json - a flat array of records (baked from the in-game balance
    // editor's PlayerTuning overlay):
    //   [ {"k":"prim","def":"X","stat":3,"str":1.5},
    //     {"k":"edge","src":"A","tgt":"B","stat":2,"str":0.5},
    //     {"k":"srcs","def":"X","list":"A;B;C"} ]
    // PrimaryStat / EdgeStat consult this FIRST; strengths default to 1 when unset.
    public static class ManualBalance
    {
        private static readonly Dictionary<string, int> primStat = new Dictionary<string, int>();   // def -> stat (-1 = none)
        private static readonly Dictionary<string, float> primStr = new Dictionary<string, float>(); // def -> strength
        private static readonly Dictionary<string, int> edgeStat = new Dictionary<string, int>();    // src|tgt -> stat
        private static readonly Dictionary<string, float> edgeStr = new Dictionary<string, float>();  // src|tgt -> strength
        private static readonly Dictionary<string, List<string>> srcsOf = new Dictionary<string, List<string>>(); // tgt -> full source list

        public static bool Loaded { get; private set; }
        public static int OverrideCount => primStat.Count + edgeStat.Count + srcsOf.Count;

        private static string Key(string src, string tgt) => src + "\u2192" + tgt;

        public static void Load()
        {
            primStat.Clear(); primStr.Clear(); edgeStat.Clear(); edgeStr.Clear(); srcsOf.Clear();
            Loaded = false;
            try
            {
                string root = PsycastSynergiesMod.Instance?.Content?.RootDir;
                if (string.IsNullOrEmpty(root)) return;
                string path = Path.Combine(root, "ManualBalance.json");
                if (!File.Exists(path)) return;

                string text = File.ReadAllText(path);
                foreach (Match m in Regex.Matches(text, "\\{[^{}]*\\}"))
                {
                    var f = ParseObj(m.Value);
                    f.TryGetValue("k", out string kind);
                    if (kind == "prim")
                    {
                        if (!f.TryGetValue("def", out string def) || string.IsNullOrEmpty(def)) continue;
                        if (f.TryGetValue("stat", out string st) && PInt(st, out int si)) primStat[def] = si;
                        if (f.TryGetValue("str", out string sr) && PFloat(sr, out float sf)) primStr[def] = sf;
                    }
                    else if (kind == "edge")
                    {
                        if (!f.TryGetValue("src", out string src) || !f.TryGetValue("tgt", out string tgt)) continue;
                        if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(tgt)) continue;
                        string key = Key(src, tgt);
                        if (f.TryGetValue("stat", out string st) && PInt(st, out int si)) edgeStat[key] = si;
                        if (f.TryGetValue("str", out string sr) && PFloat(sr, out float sf)) edgeStr[key] = sf;
                    }
                    else if (kind == "srcs")
                    {
                        if (!f.TryGetValue("def", out string def) || string.IsNullOrEmpty(def)) continue;
                        if (f.TryGetValue("list", out string lst)) srcsOf[def] = PlayerTuning.SplitList(lst);
                    }
                }
                Loaded = true;
                Log.Message($"[Psycasts²] Manual balance overlay: {primStat.Count} primary + {edgeStat.Count} edge + {srcsOf.Count} source-list override(s).");
            }
            catch (Exception e) { Log.Warning("[Psycasts²] Failed to load ManualBalance.json: " + e); }
        }

        // Flat objects only (no nested braces): pull every "key": value  (string OR number) pair.
        private static Dictionary<string, string> ParseObj(string obj)
        {
            var d = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(obj, "\"(\\w+)\"\\s*:\\s*(?:\"([^\"]*)\"|(-?[0-9.]+))"))
                d[m.Groups[1].Value] = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            return d;
        }

        private static bool PInt(string s, out int v)
            => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        private static bool PFloat(string s, out float v)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        public static bool TryPrim(string defName, out int stat) => primStat.TryGetValue(defName, out stat);
        public static float PrimStrength(string defName) => primStr.TryGetValue(defName, out float f) ? f : 1f;
        public static bool TryEdge(string src, string tgt, out int stat) => edgeStat.TryGetValue(Key(src, tgt), out stat);
        public static float EdgeStrength(string src, string tgt) => edgeStr.TryGetValue(Key(src, tgt), out float f) ? f : 1f;
        public static bool TrySrcs(string defName, out List<string> srcs) => srcsOf.TryGetValue(defName, out srcs);

        // Bake the player's live PlayerTuning overlay into <modRoot>/ManualBalance.json as the NEW
        // baked defaults (player values win over existing baked records), then reload the file and
        // clear the overlay - the baked values become the baseline the editor diffs against.
        public static bool BakePlayerTuning(out string error, out int baked)
        {
            error = null; baked = 0;
            var s = PsycastSynergiesMod.Settings;
            string root = PsycastSynergiesMod.Instance?.Content?.RootDir;
            if (s == null || string.IsNullOrEmpty(root)) { error = "mod root unavailable"; return false; }
            try
            {
                // Merged view: existing baked overlay first, player edits win.
                var pStat = new Dictionary<string, int>(primStat);
                var pStr = new Dictionary<string, float>(primStr);
                foreach (var kv in s.tunePrimStat) pStat[kv.Key] = kv.Value;
                foreach (var kv in s.tunePrimStr) pStr[kv.Key] = kv.Value;
                var eStat = new Dictionary<string, int>(edgeStat);
                var eStr = new Dictionary<string, float>(edgeStr);
                foreach (var kv in s.tuneEdgeStat) eStat[PipeToArrow(kv.Key)] = kv.Value;
                foreach (var kv in s.tuneEdgeStr) eStr[PipeToArrow(kv.Key)] = kv.Value;
                var lists = new Dictionary<string, List<string>>(srcsOf);
                foreach (var kv in s.tuneSrcs) lists[kv.Key] = PlayerTuning.SplitList(kv.Value);

                var sb = new StringBuilder("[\n");
                bool first = true;
                foreach (var def in pStat.Keys.Union(pStr.Keys).OrderBy(x => x, StringComparer.Ordinal))
                {
                    sb.Append(first ? " " : ",\n "); first = false;
                    sb.Append("{\"k\":\"prim\",\"def\":\"").Append(Esc(def)).Append('"');
                    if (pStat.TryGetValue(def, out int st)) sb.Append(",\"stat\":").Append(st);
                    if (pStr.TryGetValue(def, out float k)) sb.Append(",\"str\":").Append(k.ToString("0.###", CultureInfo.InvariantCulture));
                    sb.Append('}'); baked++;
                }
                foreach (var key in eStat.Keys.Union(eStr.Keys).OrderBy(x => x, StringComparer.Ordinal))
                {
                    int i = key.IndexOf('\u2192');
                    if (i <= 0) continue;
                    sb.Append(first ? " " : ",\n "); first = false;
                    sb.Append("{\"k\":\"edge\",\"src\":\"").Append(Esc(key.Substring(0, i)))
                      .Append("\",\"tgt\":\"").Append(Esc(key.Substring(i + 1))).Append('"');
                    if (eStat.TryGetValue(key, out int st)) sb.Append(",\"stat\":").Append(st);
                    if (eStr.TryGetValue(key, out float k)) sb.Append(",\"str\":").Append(k.ToString("0.###", CultureInfo.InvariantCulture));
                    sb.Append('}'); baked++;
                }
                foreach (var kv in lists.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    sb.Append(first ? " " : ",\n "); first = false;
                    sb.Append("{\"k\":\"srcs\",\"def\":\"").Append(Esc(kv.Key))
                      .Append("\",\"list\":\"").Append(Esc(string.Join(";", kv.Value))).Append("\"}");
                    baked++;
                }
                sb.Append("\n]\n");
                File.WriteAllText(Path.Combine(root, "ManualBalance.json"), sb.ToString());

                PlayerTuning.ResetAll();          // baked file is the baseline now
                Load();                           // reload the overlay from disk
                PsycastInfo.ClearCaches();
                PsycastSynergiesMod.Instance?.WriteSettings();
                Log.Message($"[Psycasts²] Baked balance edits: ManualBalance.json now holds {baked} record(s).");
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        // OPEN-BETA feedback: export the player's live balance edits as a shareable JSON file,
        // grouped per psycast tree (with each tree's source mod named) so balancing ideas for
        // every tree and addon tree are readable at a glance. Pure export: nothing is baked,
        // cleared, or written into the mod folder. The file path is copied to the clipboard.
        public static bool ExportPlayerTuning(out string error, out string path, out int count)
        {
            error = null; path = null; count = 0;
            var s = PsycastSynergiesMod.Settings;
            if (s == null) { error = "settings unavailable"; return false; }
            try
            {
                var prims = new Dictionary<string, List<string>>();
                var edges = new Dictionary<string, List<string>>();
                var lists = new Dictionary<string, List<string>>();
                var meta = new Dictionary<string, VanillaPsycastsExpanded.PsycasterPathDef>();

                string TreeOf(string defName)
                {
                    var ab = DefDatabase<VEF.Abilities.AbilityDef>.GetNamedSilentFail(defName);
                    var p = ab?.GetModExtension<VanillaPsycastsExpanded.AbilityExtension_Psycast>()?.path;
                    string key = p?.defName ?? "(unknown tree)";
                    if (p != null && !meta.ContainsKey(key)) meta[key] = p;
                    return key;
                }
                void AddRow(Dictionary<string, List<string>> bag, string tree, string row)
                { if (!bag.TryGetValue(tree, out var rows)) bag[tree] = rows = new List<string>(); rows.Add(row); }
                string StatName(int st) => st < 0 ? "none" : ((SynStat)st).ToString();

                foreach (var def in s.tunePrimStat.Keys.Union(s.tunePrimStr.Keys).OrderBy(x => x, StringComparer.Ordinal))
                {
                    var row = new StringBuilder("        {\"def\":\"").Append(Esc(def)).Append('"');
                    if (s.tunePrimStat.TryGetValue(def, out int st)) row.Append(",\"stat\":\"").Append(StatName(st)).Append("\",\"statId\":").Append(st);
                    if (s.tunePrimStr.TryGetValue(def, out float k)) row.Append(",\"str\":").Append(k.ToString("0.###", CultureInfo.InvariantCulture));
                    row.Append('}');
                    AddRow(prims, TreeOf(def), row.ToString()); count++;
                }
                foreach (var key in s.tuneEdgeStat.Keys.Union(s.tuneEdgeStr.Keys).OrderBy(x => x, StringComparer.Ordinal))
                {
                    int i = key.IndexOf('|');
                    if (i <= 0) continue;
                    string src = key.Substring(0, i), tgt = key.Substring(i + 1);
                    var row = new StringBuilder("        {\"src\":\"").Append(Esc(src)).Append("\",\"tgt\":\"").Append(Esc(tgt)).Append('"');
                    if (s.tuneEdgeStat.TryGetValue(key, out int st)) row.Append(",\"stat\":\"").Append(StatName(st)).Append("\",\"statId\":").Append(st);
                    if (s.tuneEdgeStr.TryGetValue(key, out float k)) row.Append(",\"str\":").Append(k.ToString("0.###", CultureInfo.InvariantCulture));
                    row.Append('}');
                    AddRow(edges, TreeOf(tgt), row.ToString()); count++;   // edge edits belong to the TARGET's tree
                }
                foreach (var kv in s.tuneSrcs.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    AddRow(lists, TreeOf(kv.Key), "        {\"def\":\"" + Esc(kv.Key) + "\",\"sources\":\"" + Esc(kv.Value) + "\"}");
                    count++;
                }
                if (count == 0) { error = "no edits"; return false; }

                var trees = prims.Keys.Union(edges.Keys).Union(lists.Keys).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var sb = new StringBuilder();
                sb.Append("{\n  \"mod\": \"Psycasts\u00b2\",\n  \"exported\": \"")
                  .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
                  .Append("\",\n  \"edits\": ").Append(count).Append(",\n  \"trees\": [\n");
                for (int t = 0; t < trees.Count; t++)
                {
                    string tree = trees[t];
                    meta.TryGetValue(tree, out var p);
                    sb.Append("    {\n      \"tree\": \"").Append(Esc(tree))
                      .Append("\",\n      \"label\": \"").Append(Esc(p?.label ?? ""))
                      .Append("\",\n      \"sourceMod\": \"").Append(Esc(p?.modContentPack?.Name ?? "unknown")).Append('"');
                    void Section(string name, Dictionary<string, List<string>> bag)
                    {
                        if (!bag.TryGetValue(tree, out var rows)) return;
                        sb.Append(",\n      \"").Append(name).Append("\": [\n").Append(string.Join(",\n", rows)).Append("\n      ]");
                    }
                    Section("primaries", prims);
                    Section("empowerEdges", edges);
                    Section("sourceLists", lists);
                    sb.Append("\n    }").Append(t < trees.Count - 1 ? "," : "").Append('\n');
                }
                sb.Append("  ]\n}\n");

                string dir = Path.Combine(GenFilePaths.SaveDataFolderPath, "Psycasts2");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, "BalanceIdeas_" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json");
                File.WriteAllText(path, sb.ToString());
                UnityEngine.GUIUtility.systemCopyBuffer = path;   // one paste away from an upload box
                Log.Message("[Psycasts\u00b2] Exported " + count + " balance edit(s) to " + path);
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        private static string PipeToArrow(string key)
        {
            int i = key.IndexOf('|');
            return i <= 0 ? key : key.Substring(0, i) + "\u2192" + key.Substring(i + 1);
        }

        private static string Esc(string v)
            => v == null ? "" : v.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

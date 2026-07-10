#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using VanillaPsycastsExpanded;
using Verse;

namespace PsycastSynergies
{
    // Loads a psycast path's background straight from disk (searching every running mod's Textures
    // folders) instead of via ContentFinder. This mirrors Modern Psycasts UI's PathBackgrounds so the
    // awakening cards use the SAME art the tab does, including retextures, and NEVER show the magenta/
    // red-X BadTex that ContentFinder returns for a missing alt-background file. Returns null when no
    // file resolves (caller draws a tinted fallback, exactly like the tab).
    [StaticConstructorOnStartup]
    public static class PathArt
    {
        private static readonly Dictionary<PsycasterPathDef, Texture2D> cacheMain = new Dictionary<PsycasterPathDef, Texture2D>();
        private static readonly Dictionary<PsycasterPathDef, Texture2D> cacheAlt = new Dictionary<PsycasterPathDef, Texture2D>();

        public static Texture2D Get(PsycasterPathDef def, bool alt)
        {
            var cache = alt ? cacheAlt : cacheMain;
            if (cache.TryGetValue(def, out var tex)) return tex;
            try { tex = Load(def, alt); }
            catch (Exception e)
            {
                Log.WarningOnce("[PsycastSynergies] path art load failed for " + def.defName + ": " + e.Message,
                    def.defName.GetHashCode() ^ (alt ? 778941 : 778940));
                tex = null;
            }
            cache[def] = tex;
            return tex;
        }

        private static Texture2D Load(PsycasterPathDef def, bool alt)
        {
            // VPE's naming is inverted: the default tab view (alt=false) shows the "altBackground" string.
            string primary = alt ? def.background : def.altBackground;
            string secondary = alt ? def.altBackground : def.background;
            foreach (var rel in new[] { primary, secondary })
            {
                if (string.IsNullOrEmpty(rel)) continue;
                foreach (var file in ResolveCandidates(rel))
                {
                    var t = LoadFile(file);
                    if (t != null) return t;
                }
            }
            return null;
        }

        private static IEnumerable<string> ResolveCandidates(string rel)
        {
            rel = rel.Replace('\\', '/');
            string[] exts = { ".png", ".dds", ".jpg" };
            var mods = LoadedModManager.RunningModsListForReading;
            var seen = new HashSet<string>();
            for (int i = mods.Count - 1; i >= 0; i--)
            {
                var m = mods[i];
                if (m == null) continue;
                var folders = new List<string>();
                if (m.foldersToLoadDescendingOrder != null) folders.AddRange(m.foldersToLoadDescendingOrder);
                if (m.RootDir != null) folders.Add(m.RootDir);
                foreach (var folder in folders)
                {
                    if (folder == null || !seen.Add(folder)) continue;
                    foreach (var ext in exts)
                    {
                        string p = Path.Combine(folder, "Textures", rel + ext);
                        if (File.Exists(p)) yield return p;
                    }
                }
            }
        }

        private static Texture2D LoadFile(string file)
        {
            byte[] bytes = File.ReadAllBytes(file);
            if (file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, bytes)) return null;
                tex.filterMode = FilterMode.Bilinear;
                return tex;
            }
            return LoadDds(bytes);
        }

        private static Texture2D LoadDds(byte[] b)
        {
            if (b.Length < 128 || b[0] != 'D' || b[1] != 'D' || b[2] != 'S' || b[3] != ' ') return null;
            int height = BitConverter.ToInt32(b, 12);
            int width = BitConverter.ToInt32(b, 16);
            string fourcc = Encoding.ASCII.GetString(b, 84, 4);
            TextureFormat fmt; int blockBytes;
            if (fourcc == "DXT1") { fmt = TextureFormat.DXT1; blockBytes = 8; }
            else if (fourcc == "DXT5") { fmt = TextureFormat.DXT5; blockBytes = 16; }
            else return null;
            if (width <= 0 || height <= 0 || width > 8192 || height > 8192) return null;
            int w4 = (width + 3) / 4 * 4, h4 = (height + 3) / 4 * 4;
            int dataLen = w4 / 4 * (h4 / 4) * blockBytes;
            if (128 + dataLen > b.Length) return null;
            var tex = new Texture2D(w4, h4, fmt, false);
            var data = new byte[dataLen];
            Array.Copy(b, 128, data, 0, dataLen);
            tex.LoadRawTextureData(data);
            tex.Apply(false, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }
    }
}

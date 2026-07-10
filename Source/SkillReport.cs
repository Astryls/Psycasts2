#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using VEF.Abilities;
using VanillaPsycastsExpanded;
using AbilityDef = VEF.Abilities.AbilityDef;

namespace PsycastSynergies
{
    // Generates a styled HTML compendium of every installed psycast path: each skill's tier, role,
    // description, per-level scaling, base effects, the synergies it RECEIVES and the skills it
    // EMPOWERS (with types + tiers). Reuses the live PsycastInfo/SkillSystem logic so it always
    // matches in-game. Written to the savedata Config folder; an agent converts it to PDF.
    public static class SkillReport
    {
        public static string LastPath;

        public static void Generate()
        {
            var s = PsycastSynergiesMod.Settings;
            if (s == null) return;

            var byPath = new Dictionary<Def, List<AbilityDef>>();
            foreach (var def in DefDatabase<AbilityDef>.AllDefs)
            {
                var ext = def?.GetModExtension<AbilityExtension_Psycast>();
                if (ext?.path == null || def.defName == null) continue;
                if (!byPath.TryGetValue(ext.path, out var list)) { list = new List<AbilityDef>(); byPath[ext.path] = list; }
                list.Add(def);
            }
            var paths = byPath.Keys.OrderBy(p => p.LabelCap.ToString()).ToList();
            int skillCount = byPath.Values.Sum(v => v.Count);

            var sb = new StringBuilder();
            sb.Append(Head(paths.Count, skillCount));

            sb.Append("<div class='toc'><h2>Contents</h2><ul>");
            foreach (var p in paths)
                sb.Append("<li><a href='#p" + Id(p) + "'>" + Esc(p.LabelCap) + "</a> <span class='dim'>(" + byPath[p].Count + ")</span></li>");
            sb.Append("</ul></div>");

            foreach (var p in paths)
            {
                sb.Append("<section class='path' id='p" + Id(p) + "'><h1>" + Esc(p.LabelCap) + "</h1>");
                if (!string.IsNullOrEmpty(p.description)) sb.Append("<p class='pdesc'>" + Esc(p.description) + "</p>");
                foreach (var a in byPath[p].OrderBy(Lvl).ThenBy(a => a.LabelCap.ToString()))
                    sb.Append(SkillCard(a, byPath[p], s));
                sb.Append("</section>");
            }
            sb.Append("</body></html>");

            try
            {
                string path = Path.Combine(GenFilePaths.ConfigFolderPath, "PS_SkillReport.html");
                File.WriteAllText(path, sb.ToString());
                LastPath = path;
                Log.Message("[Psycasts²] Skill compendium written: " + path);
            }
            catch (Exception e) { Log.Warning("[Psycasts²] report write failed: " + e); }
        }

        private static int Lvl(AbilityDef a) => a.GetModExtension<AbilityExtension_Psycast>()?.level ?? 0;

        private static string SkillCard(AbilityDef a, List<AbilityDef> pathMates, PsycastSynergiesSettings s)
        {
            var sb = new StringBuilder();
            sb.Append("<div class='skill'>");
            sb.Append("<h3>" + Esc(a.LabelCap) + " <span class='tier'>Tier " + Lvl(a) + "</span> <span class='role'>"
                + Esc(SynergyRules.RoleLabel(PsycastInfo.RoleOf(a))) + "</span></h3>");
            if (!string.IsNullOrEmpty(a.description)) sb.Append("<p class='desc'>" + Esc(a.description) + "</p>");

            var prim = PsycastInfo.PrimaryStat(a);
            sb.Append("<div class='block'><b>Per level:</b> ");
            if (prim.HasValue)
                sb.Append("+" + Pct(s.perLevelPct * PsycastInfo.PrimaryStrength(a)) + " " + Esc(SynergyRules.StatLabel(prim.Value)) + " " + TierTag(prim.Value));
            else
                sb.Append("<span class='dim'>utility - no scaled stat</span>");
            if (s.scaleCost) sb.Append(" &nbsp;&middot;&nbsp; +" + Pct(s.costPerLevelPct) + " psyfocus and heat cost");
            sb.Append("</div>");

            var fx = BaseEffects(a);
            if (fx.Count > 0)
            {
                sb.Append("<div class='block'><b>Effects:</b><ul>");
                foreach (var e in fx) sb.Append("<li>" + Esc(e) + "</li>");
                sb.Append("</ul></div>");
            }

            var srcs = PsycastInfo.SynergySources(a);
            sb.Append("<div class='block'><b>Gains power from:</b>");
            if (srcs.Count == 0) sb.Append(" <span class='dim'>none</span>");
            else
            {
                sb.Append("<ul>");
                foreach (var src in srcs)
                {
                    var t = PsycastInfo.EdgeStat(src, a);
                    sb.Append("<li>" + Esc(src.LabelCap) + " <span class='dim'>\u2192</span> +" + Pct((t.HasValue ? SkillSystem.SynergyRate(t.Value) : s.synergyPct) * PsycastInfo.EdgeStrength(src, a)) + "/lvl "
                        + (t.HasValue ? Esc(SynergyRules.StatLabel(t.Value)) + " " + TierTag(t.Value) : "") + "</li>");
                }
                sb.Append("</ul>");
            }
            sb.Append("</div>");

            var emps = new List<string>();
            foreach (var z in pathMates)
            {
                if (z == a) continue;
                if (!PsycastInfo.SynergySources(z).Contains(a)) continue;
                var t = PsycastInfo.EdgeStat(a, z);
                emps.Add(Esc(z.LabelCap) + " <span class='dim'>\u2192</span> +" + Pct((t.HasValue ? SkillSystem.SynergyRate(t.Value) : s.synergyPct) * PsycastInfo.EdgeStrength(a, z)) + "/lvl "
                    + (t.HasValue ? Esc(SynergyRules.StatLabel(t.Value)) + " " + TierTag(t.Value) : ""));
            }
            sb.Append("<div class='block'><b>Empowers:</b>");
            if (emps.Count == 0) sb.Append(" <span class='dim'>none</span>");
            else { sb.Append("<ul>"); foreach (var e in emps) sb.Append("<li>" + e + "</li>"); sb.Append("</ul>"); }
            sb.Append("</div></div>");

            return sb.ToString();
        }

        private static List<string> BaseEffects(AbilityDef a)
        {
            var list = new List<string>();
            var expl = a.GetModExtension<AbilityExtension_Explosion>();
            if (expl != null)
            {
                if (expl.explosionRadius > 0f) list.Add("Blast radius: " + expl.explosionRadius.ToString("0.#") + " tiles");
                int amt = expl.explosionDamageAmount >= 0 ? expl.explosionDamageAmount : (expl.explosionDamageDef?.defaultDamage ?? -1);
                if (amt > 0) list.Add("Blast damage: " + amt);
            }
            if (a.power != 0f && expl == null) list.Add("Damage / power: " + a.power.ToString("0.#"));
            if (a.radius > 0f && expl == null) list.Add("Radius: " + a.radius.ToString("0.#") + " tiles");
            if (a.range > 0f && a.range < 500f) list.Add("Range: " + a.range.ToString("0.#") + " tiles");
            if (a.durationTime > 0) list.Add("Duration: " + a.durationTime.ToStringTicksToPeriod());
            if (a.cooldownTime > 0) list.Add("Cooldown: " + a.cooldownTime.ToStringTicksToPeriod());
            if (a.targetCount > 1) list.Add("Targets: " + a.targetCount);
            try { string d = PsycastInfo.EffectSummary(a); if (!string.IsNullOrEmpty(d)) list.Add(d); } catch { }
            return list;
        }

        private static string TierName(SynStat s)
        {
            switch (PsycastInfo.Tier(s)) { case 0: return "S"; case 1: return "A"; case 2: return "B"; default: return "C"; }
        }
        private static string TierTag(SynStat t) => "<span class='tt t" + PsycastInfo.Tier(t) + "'>" + TierName(t) + "</span>";
        private static string Pct(float f) => (f * 100f).ToString("0.#") + "%";
        private static string Id(Def d) => (d.defName ?? "x");
        private static string Esc(string s) => s == null ? "" : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private static string Head(int pathCount, int skillCount)
        {
            return "<!DOCTYPE html><html><head><meta charset='utf-8'><title>Psycasts² - Skill Compendium</title><style>"
                + "body{font-family:'Segoe UI',Arial,sans-serif;color:#1a1a1a;margin:22px;font-size:12px;line-height:1.35;}"
                + "h1{font-size:22px;border-bottom:3px solid #2aa66a;padding-bottom:4px;color:#16633e;margin:0 0 6px;}"
                + "h2{font-size:16px;color:#16633e;} h3{font-size:13.5px;margin:0 0 3px;color:#1c3a5e;}"
                + ".title{text-align:center;margin:40px 0 50px;} .title h1{border:none;font-size:32px;color:#16633e;}"
                + ".title p{color:#666;font-size:14px;}"
                + ".toc{page-break-after:always;} .toc ul{columns:2;list-style:none;padding:0;} .toc li{margin:2px 0;}"
                + ".toc a{text-decoration:none;color:#1c3a5e;}"
                + ".path{page-break-before:always;} .pdesc{color:#444;margin:0 0 10px;}"
                + ".skill{border:1px solid #d2d2d2;border-radius:6px;padding:7px 11px;margin:7px 0;break-inside:avoid;background:#fafafa;}"
                + ".desc{color:#555;font-style:italic;margin:1px 0 5px;} .block{margin:3px 0;} .block ul{margin:2px 0 2px 16px;padding:0;}"
                + ".tier{font-size:10px;background:#ececec;border-radius:3px;padding:1px 5px;color:#555;font-weight:normal;}"
                + ".role{font-size:10px;color:#a4501c;font-weight:normal;} .dim{color:#999;}"
                + ".tt{font-size:9px;border-radius:3px;padding:0 4px;color:#fff;font-weight:bold;}"
                + ".t0{background:#c0392b;} .t1{background:#e67e22;} .t2{background:#2980b9;} .t3{background:#7f8c8d;}"
                + "@page{margin:1.4cm;}"
                + "</style></head><body>"
                + "<div class='title'><h1>Psycasts²</h1><p>Skill Compendium &mdash; " + pathCount + " paths &middot; " + skillCount + " skills</p>"
                + "<p class='dim'>Tier legend: <span class='tt t0'>S</span> top &middot; <span class='tt t1'>A</span> strong &middot; <span class='tt t2'>B</span> moderate &middot; <span class='tt t3'>C</span> minor</p></div>";
        }
    }
}

#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using VEF.Abilities;
using VanillaPsycastsExpanded;
using AbilityDef = VEF.Abilities.AbilityDef;

namespace PsycastSynergies
{
    // Stylized dark-themed HTML field manual: a fixed sidebar lists every tree; the main area shows
    // one section at a time. Overview + Specializations explainers (with a rendered tree map) lead,
    // then each path shows its skills as large cards - art on the left, bonuses + "effects at max
    // level" on the right. Icons are the ACTUAL loaded textures (RenderTexture→PNG base64), so any
    // retexture is captured and the file is self-contained.
    public static class ManualReport
    {
        private static readonly Dictionary<Texture, string> uriCache = new Dictionary<Texture, string>();

        private static string IconUri(Texture tex, int max, bool jpg = false)
        {
            if (tex == null || tex == BaseContent.BadTex) return null;
            if (uriCache.TryGetValue(tex, out var c)) return c;
            string uri = null;
            try
            {
                int w = tex.width, h = tex.height;
                if (w <= 0 || h <= 0) { uriCache[tex] = null; return null; }
                float scale = Mathf.Min(1f, (float)max / Mathf.Max(w, h));
                int tw = Mathf.Max(1, Mathf.RoundToInt(w * scale)), th = Mathf.Max(1, Mathf.RoundToInt(h * scale));
                var rt = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32);
                var prev = RenderTexture.active;
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var readable = new Texture2D(tw, th, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
                readable.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                byte[] data = jpg ? readable.EncodeToJPG(72) : readable.EncodeToPNG();
                UnityEngine.Object.Destroy(readable);
                if (data != null && data.Length > 0)
                    uri = "data:" + (jpg ? "image/jpeg" : "image/png") + ";base64," + Convert.ToBase64String(data);
            }
            catch { uri = null; }
            uriCache[tex] = uri;
            return uri;
        }

        private static string FileUri(string rel)
        {
            try
            {
                var content = PsycastSynergiesMod.Instance?.Content;
                if (content == null) return null;
                string p = Path.Combine(content.RootDir, rel);
                if (!File.Exists(p)) return null;
                return "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(p));
            }
            catch { return null; }
        }

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

            var sb = new StringBuilder();
            sb.Append(Head());

            // Sidebar.
            sb.Append("<nav class='sidebar'><div class='brand'>PSYCAST<br>SYNERGIES<span>Field Manual</span></div>");
            sb.Append("<a onclick=\"show('overview')\">&#9733; Overview</a>");
            sb.Append("<a onclick=\"show('specs')\">&#9670; Specializations</a>");
            sb.Append("<a onclick=\"show('awaken')\">&#9728; Awakening &amp; Focus</a>");
            sb.Append("<a onclick=\"show('pilgrim')\">&#9874; Pilgrimage Quests</a>");
            sb.Append("<div class='navhdr'>Paths (" + paths.Count + ")</div>");
            for (int i = 0; i < paths.Count; i++)
                sb.Append("<a onclick=\"show('p" + i + "')\">" + Esc(paths[i].LabelCap) + " <span class='ct'>" + byPath[paths[i]].Count + "</span></a>");
            sb.Append("</nav><main class='content'>");

            // Overview.
            sb.Append("<section id='overview' class='view'>" + OverviewHtml(paths.Count, byPath.Values.Sum(v => v.Count), s) + "</section>");

            // Specializations.
            sb.Append("<section id='specs' class='view' style='display:none'>" + SpecsHtml(s) + "</section>");

            // Awakening & focused meditation.
            sb.Append("<section id='awaken' class='view' style='display:none'>" + AwakeningHtml(s) + "</section>");

            // Pilgrimage quest chains.
            sb.Append("<section id='pilgrim' class='view' style='display:none'>" + PilgrimageHtml(s) + "</section>");

            // Paths.
            for (int i = 0; i < paths.Count; i++)
            {
                var p = paths[i];
                sb.Append("<section id='p" + i + "' class='view' style='display:none'><h1>" + Esc(p.LabelCap) + "</h1>");
                if (!string.IsNullOrEmpty(p.description)) sb.Append("<p class='lead'>" + Esc(p.description) + "</p>");
                foreach (var a in byPath[p].OrderBy(x => x.GetModExtension<AbilityExtension_Psycast>()?.level ?? 0).ThenBy(x => x.LabelCap.ToString()))
                    sb.Append(SkillCard(a, byPath[p], s));
                sb.Append("</section>");
            }

            sb.Append("</main>"
                + "<script>function show(id){document.querySelectorAll('.view').forEach(function(v){v.style.display='none';});"
                + "var e=document.getElementById(id);if(e)e.style.display='block';window.scrollTo(0,0);}</script>"
                + "</body></html>");

            try
            {
                string path = Path.Combine(GenFilePaths.ConfigFolderPath, "PS_FieldManual.html");
                File.WriteAllText(path, sb.ToString());
                Log.Message("[Psycasts²] Field manual written: " + path + " (" + (sb.Length / 1024) + " KB)");
            }
            catch (Exception e) { Log.Warning("[Psycasts²] field manual write failed: " + e); }
        }

        // ── Large skill card ──────────────────────────────────────────────────
        private struct Fx { public string label; public float num; public int kind; public SynStat? stat; }

        private static string SkillCard(AbilityDef a, List<AbilityDef> mates, PsycastSynergiesSettings s)
        {
            int tier = a.GetModExtension<AbilityExtension_Psycast>()?.level ?? 0;
            Role role = PsycastInfo.RoleOf(a);
            var prim = PsycastInfo.PrimaryStat(a);
            int maxLvl = s.maxSkillLevel;
            float boost = prim.HasValue ? SkillSystem.CountBoost(prim.Value) : 1f;
            float pstr = PsycastInfo.PrimaryStrength(a);   // hand-tuned primary strength
            float ownPct = maxLvl * s.perLevelPct * boost * pstr;
            bool reduction = prim.HasValue && (prim.Value == SynStat.Cooldown || prim.Value == SynStat.Efficiency || prim.Value == SynStat.Haste);
            float mult = 1f + ownPct;
            string ic = IconUri(a.icon, 120);

            var sb = new StringBuilder();
            sb.Append("<div class='lcard'>");
            sb.Append("<div class='art'>" + (ic != null ? "<img src='" + ic + "'>" : "") + "</div>");
            sb.Append("<div class='info'>");
            sb.Append("<div class='hd'><span class='nm'>" + Esc(a.LabelCap) + "</span><span class='tier'>Tier " + tier + "</span>"
                + "<span class='role r" + (int)role + "'>" + Esc(SynergyRules.RoleLabel(role)) + "</span></div>");
            if (!string.IsNullOrEmpty(a.description)) sb.Append("<div class='desc'>" + Esc(a.description) + "</div>");

            // Scaling + at-max headline.
            if (prim.HasValue)
            {
                sb.Append("<div class='sec'><b>Scaling</b> &mdash; primary: +" + Pct(s.perLevelPct * boost * pstr) + "/level "
                    + Esc(SynergyRules.StatLabel(prim.Value)) + ".<br>");
                sb.Append("<span class='atmax'>At max (Lv" + maxLvl + "): " + (reduction ? "&minus;" : "+") + Pct(ownPct) + " "
                    + Esc(SynergyRules.StatLabel(prim.Value)) + "</span> from your own levels &mdash; synergies &amp; specializations stack on top.</div>");
            }

            // Effects (base + at-max for the primary).
            var fx = BaseEffects(a);
            if (fx.Count > 0)
            {
                sb.Append("<div class='sec'><b>Effects</b><ul>");
                foreach (var e in fx)
                {
                    string li = Esc(e.label) + ": " + Esc(FmtVal(e.num, e.kind));
                    if (!reduction && prim.HasValue && e.stat == prim && mult > 1.001f)
                        li += " <span class='gx'>\u2192 " + Esc(FmtVal(e.num * mult, e.kind)) + " at Lv" + maxLvl + "</span>";
                    sb.Append("<li>" + li + "</li>");
                }
                sb.Append("</ul></div>");
            }

            // Synergies received.
            var srcs = PsycastInfo.SynergySources(a);
            if (srcs.Count > 0)
            {
                sb.Append("<div class='sec'><b>Gains power from</b><ul>");
                foreach (var src in srcs)
                {
                    var t = PsycastInfo.EdgeStat(src, a);
                    sb.Append("<li class='g'>" + Esc(src.LabelCap) + " <span class='ar'>\u25C2</span> +"
                        + Pct((t.HasValue ? SkillSystem.SynergyRate(t.Value) : s.synergyPct) * PsycastInfo.EdgeStrength(src, a)) + "/lvl "
                        + (t.HasValue ? Esc(SynergyRules.StatLabel(t.Value)) : "") + "</li>");
                }
                sb.Append("</ul></div>");
            }

            // Empowers.
            var emps = mates.Where(z => z != a && PsycastInfo.SynergySources(z).Contains(a)).ToList();
            if (emps.Count > 0)
            {
                sb.Append("<div class='sec'><b>Empowers</b><ul>");
                foreach (var z in emps)
                {
                    var t = PsycastInfo.EdgeStat(a, z);
                    sb.Append("<li class='e'>" + Esc(z.LabelCap) + " <span class='ar'>\u25B8</span> +"
                        + Pct((t.HasValue ? SkillSystem.SynergyRate(t.Value) : s.synergyPct) * PsycastInfo.EdgeStrength(a, z)) + "/lvl "
                        + (t.HasValue ? Esc(SynergyRules.StatLabel(t.Value)) : "") + "</li>");
                }
                sb.Append("</ul></div>");
            }

            sb.Append("</div></div>");
            return sb.ToString();
        }

        private static List<Fx> BaseEffects(AbilityDef a)
        {
            var list = new List<Fx>();
            var expl = a.GetModExtension<AbilityExtension_Explosion>();
            if (expl != null)
            {
                if (expl.explosionRadius > 0f) list.Add(new Fx { label = "Blast radius", num = expl.explosionRadius, kind = 0, stat = SynStat.Radius });
                int amt = expl.explosionDamageAmount >= 0 ? expl.explosionDamageAmount : (expl.explosionDamageDef?.defaultDamage ?? -1);
                if (amt > 0) list.Add(new Fx { label = "Blast damage", num = amt, kind = 2, stat = SynStat.Power });
            }
            if (a.power != 0f && expl == null) list.Add(new Fx { label = "Damage / power", num = a.power, kind = 0, stat = SynStat.Power });
            if (a.radius > 0f && expl == null) list.Add(new Fx { label = "Radius", num = a.radius, kind = 0, stat = SynStat.Radius });
            if (a.range > 0f && a.range < 500f) list.Add(new Fx { label = "Range", num = a.range, kind = 0, stat = SynStat.Range });
            if (a.durationTime > 0) list.Add(new Fx { label = "Duration", num = a.durationTime, kind = 1, stat = SynStat.Duration });
            if (a.cooldownTime > 0) list.Add(new Fx { label = "Cooldown", num = a.cooldownTime, kind = 1, stat = SynStat.Cooldown });
            if (a.targetCount > 1) list.Add(new Fx { label = "Targets", num = a.targetCount, kind = 2, stat = SynStat.Targets });
            return list;
        }

        private static string FmtVal(float v, int kind)
        {
            switch (kind)
            {
                case 0: return v.ToString("0.#") + " tiles";
                case 1: return Mathf.RoundToInt(v).ToStringTicksToPeriod();
                default: return Mathf.RoundToInt(v).ToString();
            }
        }

        // ── Specialization view ───────────────────────────────────────────────
        private static string SpecsHtml(PsycastSynergiesSettings s)
        {
            var sb = new StringBuilder();
            sb.Append("<h1>Specializations</h1>");
            sb.Append("<p class='lead'>Beyond per-skill levels, you earn <b>specialization points</b> (1 per " + s.specLevelsPerPoint
                + " psycaster levels) and spend them in the constellation tree &mdash; the <b>&#9733;</b> button on the Psycasts tab. "
                + "Nodes unlock along prerequisite chains; picks are <b>staged</b> then <b>Applied</b> together. Some nodes are "
                + "<b>exclusive</b> (only one of a group). Each branch ends in a <b>capstone</b>; any capstone opens <b>Convergence</b>, "
                + "the apex, which in turn unlocks the seven floating <b>Apotheosis constellations</b> &mdash; Halcyon, Singularity, Penumbra, Empyrean, Pandemonium, Chronos and Consonance &mdash; one per pawn.</p>");

            string tree = FileUri("Textures/UI/SpecTreeMap.png");
            if (tree != null) sb.Append("<div class='treewrap'><img class='tree' src='" + tree + "'></div>");

            foreach (var branch in new[] { "Root", "Offense", "Control", "Ward", "Flow", "Mod", "Apex", "Halcyon", "Singularity", "Penumbra", "Empyrean", "Pandemonium", "Chronos", "Consonance" })
            {
                var specs = Specs.All.Where(x => x.branch == branch).ToList();
                if (specs.Count == 0) continue;
                sb.Append("<h2>" + Esc(BranchName(branch)) + "</h2>");
                foreach (var sp in specs) sb.Append(SpecCard(sp));
            }
            return sb.ToString();
        }

        private static string SpecCard(Spec sp)
        {
            string ic = IconUri(sp.icon, 96);
            string req = sp.prereqs != null && sp.prereqs.Length > 0
                ? string.Join(", ", sp.prereqs.Select(p => Specs.Get(p)?.label ?? p))
                : (sp.anyPrereq != null && sp.anyPrereq.Length > 0
                    ? "any of " + string.Join(" / ", sp.anyPrereq.Select(p => Specs.Get(p)?.label ?? p))
                    : "-");
            var sb = new StringBuilder();
            sb.Append("<div class='lcard spec'>");
            sb.Append("<div class='art sm'>" + (ic != null ? "<img src='" + ic + "'>" : "") + "</div>");
            sb.Append("<div class='info'>");
            sb.Append("<div class='hd'><span class='nm'>" + Esc(sp.label) + "</span><span class='tier cost'>\u2605 " + sp.cost + "</span>"
                + (sp.pick ? "<span class='role r3'>PICK</span>" : "")
                + (sp.exclusiveGroup != null ? "<span class='excl'>EXCLUSIVE</span>" : "") + "</div>");
            sb.Append("<div class='desc'>" + Esc((sp.desc ?? "").Replace("\n", "  ")) + "</div>");
            sb.Append("<div class='sec'><b>Requires:</b> " + Esc(req) + "</div>");
            sb.Append("</div></div>");
            return sb.ToString();
        }

        private static string AwakeningHtml(PsycastSynergiesSettings s)
        {
            var sb = new StringBuilder();
            sb.Append("<style>.focus{width:100%;border-collapse:collapse;margin:14px 0;}"
                + ".focus th{text-align:left;padding:8px 10px;border-bottom:2px solid #333;color:#9aa;font-size:12px;text-transform:uppercase;letter-spacing:.05em;}"
                + ".focus td{padding:9px 10px;border-bottom:1px solid #262b33;vertical-align:top;}"
                + ".focus .ft{font-weight:700;white-space:nowrap;color:#e6c878;}"
                + ".schl{display:inline-flex;align-items:center;gap:3px;background:#1b2029;border:1px solid #2c333f;border-radius:6px;padding:2px 8px 2px 4px;margin:3px 4px 3px 0;font-size:13px;}"
                + ".ti{width:18px;height:18px;vertical-align:middle;object-fit:contain;}</style>");
            sb.Append("<h1>Awakening &amp; Focused Meditation</h1>");
            sb.Append("<p class='lead'>Any colonist - psycaster or not - can be set to meditate toward <b>Enlightenment</b>. A non-psycaster who breaks through enough times <b>awakens</b> as a psycaster, choosing a first path from face-down cards; psycasters instead climb three Enlightenment <b>tiers</b> (Awakened, Enlightened, Illuminated), each a fresh card pick plus bonus specialization points.</p>");
            sb.Append("<h2>Focused rolls - steering your cards</h2>");
            sb.Append("<div class='panel'>Every meditation focus - sculptures, braziers, the anima tree, even the bare <b>meditation spot</b> - carries a <b>Focus type</b> gizmo. Attune it to a psychic focus type and a non-psycaster who meditates <i>facing it</i> is steered toward that type's schools: the awakening cards are weighted toward the matching schools, and one is effectively <b>guaranteed</b>. Leave it on <b>Any</b> to weight by the meditator's personality traits instead.</div>");
            sb.Append("<table class='focus'><thead><tr><th>Focus type</th><th>Steers the awakening toward&hellip;</th></tr></thead><tbody>");
            foreach (var fd in DefDatabase<MeditationFocusDef>.AllDefs.OrderBy(d => d.LabelCap.ToString()))
            {
                var biased = MeditationSystem.FocusBiasedPaths(fd.defName);
                if (biased.Count == 0) continue;
                sb.Append("<tr><td class='ft'>" + FocusIconImg(fd) + " " + Esc(fd.LabelCap) + "</td><td>");
                foreach (var p in biased.OrderBy(p => p.LabelCap.ToString()).Take(24))
                    sb.Append("<span class='schl'>" + SchoolIconImg(p) + Esc(p.LabelCap) + "</span>");
                sb.Append("</td></tr>");
            }
            sb.Append("</tbody></table>");
            sb.Append("<p class='dim'>Focus types with no thematic match (e.g. Minimal) fall back to trait weighting. This table reflects the psycast mods you currently have installed.</p>");
            sb.Append("<h2>Progression to Apotheosis</h2>");
            sb.Append("<div class='panel'><b>Specialization points</b> accrue from psycaster levels (1 per " + s.specLevelsPerPoint + "), from <b>casting</b> (more for costlier casts), from <b>psycast kills</b>, and from Enlightenment tiers (Tier II +" + s.tier2SpecPoints + ", Tier III +" + s.tier3SpecPoints + "). Spend them up the constellation: any branch capstone opens <b>Convergence</b>, the apex, which unlocks the seven floating <b>Apotheosis constellations</b> drifting above - one per pawn. Touching a first Apotheosis gate is a genuine end-game goal (about 121 points along the cheapest path), reachable by a dedicated psycaster over a full game.</div>");
            return sb.ToString();
        }

        private static string FocusIconImg(MeditationFocusDef fd)
        {
            string ic = fd?.GetModExtension<MeditationFocusExtension>()?.icon;
            var tex = string.IsNullOrEmpty(ic) ? null : ContentFinder<Texture2D>.Get(ic, false);
            string uri = IconUri(tex, 32);
            return uri != null ? "<img class='ti' src='" + uri + "'>" : "";
        }

        private static string SchoolIconImg(PsycasterPathDef path)
        {
            var tex = (path?.abilities != null && path.abilities.Count > 0) ? path.abilities[0].icon : null;
            string uri = IconUri(tex, 28);
            return uri != null ? "<img class='ti' src='" + uri + "'>" : "";
        }

        private static string PilgrimageHtml(PsycastSynergiesSettings s)
        {
            var sb = new StringBuilder();
            sb.Append("<style>.qtbl{width:100%;border-collapse:collapse;margin:14px 0;}"
                + ".qtbl th{text-align:left;padding:8px 10px;border-bottom:2px solid #333;color:#9aa;font-size:12px;text-transform:uppercase;letter-spacing:.05em;}"
                + ".qtbl td{padding:9px 10px;border-bottom:1px solid #262b33;vertical-align:top;font-size:13px;}"
                + ".qtbl tbody tr:last-child td{border-bottom:none;}"
                + ".qtbl .tier{font-weight:700;color:#e6c878;white-space:nowrap;}"
                + ".qtbl .ch{color:#9bdcff;font-weight:600;}"
                + ".pchain{background:#161a22;border:1px solid #2c333f;border-radius:9px;padding:12px 14px;margin:0 0 14px;}"
                + ".pchain h2{margin:0 0 6px;color:#cdd4e0;font-size:15px;}"
                + ".pchain .tag{display:inline-block;font-size:10.5px;text-transform:uppercase;letter-spacing:.06em;padding:2px 8px;border-radius:10px;margin-right:6px;}"
                + ".tag.trial{background:#3a1e1e;color:#ff9a8a;border:1px solid #6a3030;}"
                + ".tag.pacifist{background:#1c3220;color:#9bdc9b;border:1px solid #335a3a;}</style>");

            sb.Append("<h1>Pilgrimage Quests</h1>");
            sb.Append("<p class='lead'>Awakened colonists can advance through Enlightenment via two distinct quest chains, each offered by the storyteller when an eligible pilgrim exists. Both end by opening the awakening pick window on the pilgrim. They differ sharply in how they're paid for.</p>");

            int reqAltar = (s.pilgrimMeditationTicks > 0 ? s.pilgrimMeditationTicks : 50000);
            int cap = (s.pilgrimDailyMaxTicks > 0 ? s.pilgrimDailyMaxTicks : 20000);
            float altarH = reqAltar / 2500f;
            float capH = cap / 2500f;
            float altarDays = capH > 0 ? altarH / capH : altarH / 24f;
            int reqAnima = (s.animaPilgrimTicksPerSite > 0 ? s.animaPilgrimTicksPerSite : 50000);
            int animaT2 = s.animaPilgrimT2Sites > 0 ? s.animaPilgrimT2Sites : 3;
            int animaT3 = s.animaPilgrimT3Sites > 0 ? s.animaPilgrimT3Sites : 4;
            float animaPerSiteH = reqAnima / 2500f;
            float animaT2Days = capH > 0 ? animaT2 * animaPerSiteH / capH : 0;
            float animaT3Days = capH > 0 ? animaT3 * animaPerSiteH / capH : 0;

            sb.Append("<div class='pchain'><span class='tag trial'>Trial</span><h2>The Altar Chain &mdash; short &amp; brutal</h2>");
            sb.Append("<p class='dim'>A single site spawns a focus structure (Pilgrim's Altar by default; tunable to Meditation Spot, Throne, or Grand Throne). The pilgrim meditates the required hours, <b>capped at "
                + capH.ToString("F0") + "h/day</b>, while the <b>Ancient Psycaster Order</b> sends escalating raid waves every " + (s.pilgrimWaveIntervalTicks / 2500f).ToString("F0") + "h.</p>");
            sb.Append("<table class='qtbl'><thead><tr><th>Tier</th><th>Quest</th><th>Focus</th><th>Meditation</th><th>Waves</th></tr></thead><tbody>");
            sb.Append("<tr><td class='tier'>II</td><td class='ch'>A Pilgrim's Path</td><td>" + Esc(s.pilgrimFocusDef ?? "PS_PilgrimAltar") + "</td><td>" + altarH.ToString("F0") + "h \u00b7 \u2248 " + altarDays.ToString("F1") + " days</td><td>standard</td></tr>");
            sb.Append("<tr><td class='tier'>III</td><td class='ch'>Trial of Illumination</td><td>" + Esc(s.pilgrimFocusDef ?? "PS_PilgrimAltar") + "</td><td>" + altarH.ToString("F0") + "h \u00b7 \u2248 " + altarDays.ToString("F1") + " days</td><td>\u00d71.6 harder</td></tr>");
            sb.Append("</tbody></table></div>");

            sb.Append("<div class='pchain'><span class='tag pacifist'>Pacifist</span><h2>The Anima Chain &mdash; long &amp; peaceful</h2>");
            sb.Append("<p class='dim'>Multiple anima trees manifest across the world. The pilgrim must travel to <i>each</i> and meditate beneath its boughs. <b>No enemies attack the sites.</b> The total commitment is roughly 3&ndash;4&times; the altar chain &mdash; the right path for a single beloved colonist you don't want to risk.</p>");
            sb.Append("<table class='qtbl'><thead><tr><th>Tier</th><th>Quest</th><th>Sites</th><th>Per-site / total meditation</th><th>Special</th></tr></thead><tbody>");
            sb.Append("<tr><td class='tier'>II</td><td class='ch'>Song of the Anima</td><td>" + animaT2 + " anima trees</td><td>" + animaPerSiteH.ToString("F0") + "h / " + (animaT2 * animaPerSiteH).ToString("F0") + "h \u00b7 \u2248 " + animaT2Days.ToString("F1") + " days</td><td>-</td></tr>");
            sb.Append("<tr><td class='tier'>III</td><td class='ch'>The Ancient World-Tree</td><td>" + animaT3 + " trees (last is Ancient)</td><td>" + animaPerSiteH.ToString("F0") + "h / " + (animaT3 * animaPerSiteH).ToString("F0") + "h \u00b7 \u2248 " + animaT3Days.ToString("F1") + " days</td><td>last site spawns a <b>giant anima tree</b></td></tr>");
            sb.Append("</tbody></table></div>");

            sb.Append("<h2>Shared rules</h2>");
            sb.Append("<div class='panel'>An <b>" + capH.ToString("F0") + " hours / day cap</b> on meditation applies to <b>both</b> chains (forces real elapsed time). On success, the awakening card window opens on the pilgrim. On pilgrim death or tier mismatch, the quest fails. Every parameter - meditation hours, daily cap, focus type, wave interval, wave threat scale, anima site counts - is tunable in mod settings.</div>");
            sb.Append("<div class='panel'><b>Eligibility:</b> a colonist must already be at <b>Tier I (Awakened)</b> for a T2 quest, and <b>Tier II (Enlightened)</b> for a T3 quest. Awakening to Tier I still comes from normal meditation breakthroughs.</div>");
            return sb.ToString();
        }

        private static string OverviewHtml(int pathCount, int skillCount, PsycastSynergiesSettings s)
        {
            return "<div class='hero'><h1>Psycasts²</h1><p>A Diablo / Path-of-Exile-style progression layer for Vanilla Psycasts Expanded.</p>"
                + "<p class='dim'>" + pathCount + " paths &middot; " + skillCount + " skills documented</p></div>"
                + "<h2>How it works</h2>"
                + "<div class='panel'><b>1 &middot; Level your psycasts.</b> Click an unlocked psycast's icon (in the VPE tab or Modern Psycasts UI) to invest a point and raise its level &mdash; up to " + s.maxSkillLevel + " (Shift-click skips the confirm). Each level boosts that skill's effects.</div>"
                + "<div class='panel'><b>2 &middot; One primary stat.</b> Every skill scales exactly ONE rarity-assigned stat &mdash; its <i>primary</i> &mdash; by +" + Pct(s.perLevelPct) + " per level (damage, radius, duration, projectiles, summons, &hellip;).</div>"
                + "<div class='panel'><b>3 &middot; Synergies &amp; empowers.</b> Every skill is linked to a few path-mates. Leveling a skill <i>empowers</i> the ones it points to (\u25B8), and a skill <i>gains power from</i> its sources (\u25C2) &mdash; each link in its own stat. The graph is fixed per path: every skill receives at most 3 and gives at most 3, so spreading investment across a path matters. Each card lists both directions.</div>"
                + "<div class='panel'><b>4 &middot; Tiers.</b> Synergy types are tiered by strength &mdash; <span class='tt t0'>S</span> top, <span class='tt t1'>A</span> strong, <span class='tt t2'>B</span> moderate, <span class='tt t3'>C</span> minor &mdash; and each skill receives a balanced mix.</div>"
                + "<div class='panel'><b>5 &middot; Specializations.</b> Psycaster levels also grant specialization points for the constellation tree (see the Specializations page) &mdash; global multipliers, capstones, and three Apotheosis paths.</div>"
                + "<h2>Leveling, XP &amp; caps</h2>"
                + "<div class='panel'><b>Per-skill levels.</b> Each ability levels 0&ndash;" + s.maxSkillLevel + " (Convergence raises the cap by +5). A higher skill level needs a higher <i>psycaster</i> level on a rising triangular curve, and deeper-tier abilities start that curve higher &mdash; so maxing a tier-6 skill is a real end-game investment, not an instant dump.</div>"
                + "<div class='panel'><b>XP comes from casting</b>, scaled by the skill's tier (\u2248 " + s.castXpPerTier.ToString("0") + " XP &times; tier per cast). Casting your higher-tier psycasts levels your psycaster fastest.</div>"
                + "<div class='panel'><b>Meditation</b> still grants psycast XP, but as a reduced trickle (" + Pct(s.meditationXpMult) + " of normal) &mdash; a steady background source rather than the main one.</div>"
                + (s.enlightenmentEnabled ? "<div class='panel'><b>Meditation, Enlightenment, Awakening &amp; Pilgrimage.</b> Awakening from meditation reaches <b>Tier I</b>. Tier II and Tier III are earned through <b>pilgrimage quests</b> offered by the storyteller - see the <b>Pilgrimage Quests</b> page for the two chains (altar trial vs. anima journey).</div><div class='panel'><b> <i>Any</i> colonist can be set to meditate. Sustained meditation can trigger <b>a flow of ancient knowledge</b> - a psychic breakthrough. For a psycaster it is worth a <b>full psycaster level</b>; for a non-psycaster it is insight toward <b>awakening as a psycaster</b>, a path that ramps with total meditation and is guaranteed within about a week of dedication. The newly awakened pick a first path from a center-screen spread of face-down cards, biased toward the focus type they meditated at (see the <b>Awakening &amp; Focus</b> page for which focus steers which schools). A psycaster's breakthroughs get <b>progressively rarer the more days in a row</b> they over-meditate (recovering after a few days off), so they can't be farmed for a free level every day. And over-meditating within a day is dangerous: too many hours risks a <b>psychic coma</b>, so a sustainable rhythm beats round-the-clock grinding.</div>" : "")
                + "<h2>Reading a card</h2>"
                + "<div class='panel'>Each skill card shows its art, tier &amp; role, base effects with their value <span class='gx'>&rarr; at max level</span>, the <span style='color:#7fe0a0'>\u25C2 sources that empower it</span>, and the <span style='color:#9fb6ff'>\u25B8 skills it empowers</span>. Use the sidebar to jump between paths.</div>";
        }

        private static string BranchName(string b)
        {
            switch (b)
            {
                case "Root": return "Foundation";
                case "Mod": return "Specialization picks (choose one)";
                case "Apex": return "Apex";
                default: return b;
            }
        }

        private static string Pct(float f) => (f * 100f).ToString("0.#") + "%";
        private static string Esc(string s) => s == null ? "" : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private static string Head()
        {
            return "<!DOCTYPE html><html><head><meta charset='utf-8'><title>Psycasts² - Field Manual</title><style>"
                + "*{box-sizing:border-box;} body{background:#11131a;color:#d7dce6;font-family:'Segoe UI',Arial,sans-serif;margin:0;font-size:13px;line-height:1.45;}"
                + ".sidebar{position:fixed;top:0;left:0;width:236px;height:100vh;overflow-y:auto;background:#0c0e14;border-right:1px solid #232a38;padding:14px 10px;}"
                + ".brand{font-weight:bold;font-size:17px;color:#7fd7a6;letter-spacing:1px;line-height:1.1;margin:4px 6px 14px;} .brand span{display:block;font-size:10px;color:#8b93a4;font-weight:normal;letter-spacing:2px;margin-top:4px;}"
                + ".sidebar a{display:flex;justify-content:space-between;gap:6px;color:#aeb6c6;text-decoration:none;padding:5px 9px;border-radius:5px;cursor:pointer;font-size:12.5px;}"
                + ".sidebar a:hover{background:#171c27;color:#fff;} .navhdr{color:#647088;font-size:10px;letter-spacing:1px;margin:14px 8px 4px;text-transform:uppercase;} .ct{color:#5e6678;font-size:10px;}"
                + ".content{margin-left:262px;padding:24px 34px;max-width:920px;}"
                + "h1{font-size:25px;color:#7fd7a6;margin:0 0 10px;} h2{font-size:17px;color:#9fb6ff;margin:24px 0 8px;border-bottom:1px solid #262d3c;padding-bottom:4px;}"
                + ".lead{color:#aeb6c6;margin:0 0 14px;} .dim{color:#8b93a4;}"
                + ".hero{text-align:center;padding:30px 0 16px;border-bottom:1px solid #262d3c;margin-bottom:18px;} .hero h1{font-size:34px;} .hero p{color:#aeb6c6;}"
                + ".panel{background:#171b25;border-left:3px solid #2aa66a;border-radius:0 6px 6px 0;padding:9px 14px;margin:8px 0;}"
                + ".treewrap{text-align:center;margin:14px 0 8px;} .tree{max-width:100%;border:1px solid #262d3c;border-radius:8px;background:#13151b;}"
                + ".lcard{display:flex;gap:16px;background:#181c26;border:1px solid #29303f;border-radius:10px;padding:13px;margin:11px 0;break-inside:avoid;}"
                + ".lcard.spec{background:#1a1726;border-color:#352b48;}"
                + ".art{flex:0 0 118px;width:118px;height:118px;background:rgba(255,255,255,.05);border-radius:9px;display:flex;align-items:center;justify-content:center;}"
                + ".art.sm{flex-basis:92px;width:92px;height:92px;} .art img{max-width:104px;max-height:104px;} .art.sm img{max-width:74px;max-height:74px;}"
                + ".info{flex:1;min-width:0;} .hd{display:flex;align-items:center;gap:9px;flex-wrap:wrap;} .nm{font-weight:bold;font-size:16px;color:#eef2f8;}"
                + ".tier{font-size:10px;background:#283041;color:#9fb0c8;border-radius:3px;padding:1px 6px;} .cost{background:#3a2f12;color:#e8c474;}"
                + ".excl{font-size:9px;background:#7d3c98;color:#fff;border-radius:3px;padding:1px 5px;}"
                + ".role{font-size:10px;letter-spacing:.5px;} .r1{color:#ff7a6b;} .r2{color:#ffae54;} .r3{color:#7fe0a0;} .r0{color:#8b93a4;}"
                + ".desc{color:#aeb6c6;font-style:italic;margin:5px 0 7px;} .sec{margin:5px 0;font-size:12px;} .sec b{color:#cdd4e0;} .sec ul{margin:3px 0 3px 18px;padding:0;}"
                + ".atmax{color:#ffd98a;font-weight:bold;} .gx{color:#7fe0a0;} .sec li.g{color:#cfe9d6;} .sec li.e{color:#cdd8f4;} .ar{opacity:.6;}"
                + ".tt{font-size:9px;border-radius:3px;padding:0 4px;color:#fff;font-weight:bold;} .t0{background:#c0392b;} .t1{background:#e67e22;} .t2{background:#2980b9;} .t3{background:#7f8c8d;}"
                + "@media print{.sidebar{position:static;width:auto;height:auto;border:none;} .content{margin-left:0;} .view{display:block!important;page-break-before:always;}}"
                + "</style></head><body>";
        }
    }
}

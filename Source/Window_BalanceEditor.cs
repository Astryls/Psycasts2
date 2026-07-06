#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using VanillaPsycastsExpanded;
using VanillaPsycastsExpanded.UI;
using VEF.Abilities;
using AbilityDef = VEF.Abilities.AbilityDef;
using Ability = VEF.Abilities.Ability;

namespace PsycastSynergies
{
    // In-game synergy balance editor (Modern-suite / Luminary-Cube style), opened from the psycast
    // tab's dev row. ONE path (tree) is popped out at a time via the left rail; each skill of that
    // path is a card whose PRIMARY effect ("skill effect"), received synergies ("gains power from")
    // and outgoing empowers can all be re-picked from dropdown chips - including WHICH skills feed
    // which (graph edits). Edits live in mod settings (PlayerTuning) - the highest layer of the
    // synergy stack - and apply instantly to the live game. A later "bake" converts the overlay
    // 1:1 into ManualBalance.json records shipped with the mod; the tool itself stays for players.
    public class Window_BalanceEditor : Window
    {
        // ── layout ──
        private const float RowH = 26f, SkillW = 210f, StatW = 136f, StrW = 54f;
        private const int MaxSources = 6;

        // ── cube-style palette (flat colors; no texture deps) ──
        private static readonly Color Darkest = Palette.BGD;
        private static readonly Color PanelBG = Palette.BG;
        private static readonly Color Inset = Palette.BGL;
        private static readonly Color InsetHi = Palette.FromHex(0x3C4147);
        private static readonly Color RowSel = Palette.FromHex(0x2A3A52);
        private static readonly Color Hairline = new Color(1f, 1f, 1f, 0.06f);

        private class PathGroup
        {
            public PsycasterPathDef path;              // null = unpathed group
            public string label;
            public List<AbilityDef> skills = new List<AbilityDef>();
            public HashSet<string> defNames = new HashSet<string>();
        }

        private readonly List<PathGroup> groups = new List<PathGroup>();
        private static int selGroup;                   // persists across opens
        private Vector2 railScroll, bodyScroll, previewScroll;
        private AbilityDef selSkill;                   // the skill the live preview tracks
        private bool scrollToSel;                      // jump the card list to selSkill's card

        // Captured from the psycast tab at open: renders the live tree + live values.
        private readonly Pawn pawn;
        private readonly Hediff_PsycastAbilities psy;
        private readonly CompAbilities comp;

        public override Vector2 InitialSize
            => new Vector2(Mathf.Min(1560f, UI.screenWidth - 24f), Mathf.Min(800f, UI.screenHeight - 60f));
        protected override float Margin => 0f;

        public Window_BalanceEditor()
        {
            forcePause = true;
            doCloseX = true;
            draggable = true;
            absorbInputAroundWindow = true;
            closeOnCancel = true;
            doWindowBackground = false;
            drawShadow = false;

            pawn = Find.Selector.SingleSelectedThing as Pawn;
            psy = pawn?.Psycasts();
            comp = pawn?.GetComp<CompAbilities>();

            var byPath = new Dictionary<PsycasterPathDef, PathGroup>();
            var unpathed = new PathGroup { label = "(No path)" };
            foreach (var def in DefDatabase<AbilityDef>.AllDefs)
            {
                var ext = def?.GetModExtension<AbilityExtension_Psycast>();
                if (ext == null || def.defName == null) continue;
                if (ext.path == null) { unpathed.skills.Add(def); continue; }
                if (!byPath.TryGetValue(ext.path, out var pg))
                {
                    pg = new PathGroup { label = ext.path.LabelCap.ToString(), path = ext.path };
                    byPath[ext.path] = pg;
                }
                pg.skills.Add(def);
            }
            groups.AddRange(byPath.Values.OrderBy(x => x.label));
            if (unpathed.skills.Count > 0) groups.Add(unpathed);
            foreach (var pg in groups)
            {
                pg.skills = pg.skills.OrderBy(Tier).ThenBy(a => a.LabelCap.ToString()).ToList();
                foreach (var a in pg.skills) pg.defNames.Add(a.defName);
            }
            if (selGroup < 0 || selGroup >= groups.Count) selGroup = 0;
            selSkill = Cur != null && Cur.skills.Count > 0 ? Cur.skills[0] : null;
        }

        private PathGroup Cur => groups.Count > 0 ? groups[selGroup] : null;
        private static int Tier(AbilityDef a) => a.GetModExtension<AbilityExtension_Psycast>()?.level ?? 0;

        public override void PostClose()
        {
            base.PostClose();
            PsycastSynergiesMod.Instance?.WriteSettings();   // persist the tuning overlay
        }

        // ─────────────────────────────────────────────────────────────────────
        public override void DoWindowContents(Rect rect)
        {
            var pf = Text.Font; var pa = Text.Anchor; var pc = GUI.color;

            MXStyle.Fill(rect, Darkest);
            MXStyle.Border(rect);

            // ── header ──
            Rect head = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, 46f);
            MXStyle.Fill(head, PanelBG);
            Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = Palette.Accent;
            Widgets.Label(new Rect(head.x + 14f, head.y + 2f, 330f, 26f), "Synergy balance editor");
            Text.Font = GameFont.Tiny; GUI.color = Palette.TextDim;
            Widgets.Label(new Rect(head.x + 14f, head.y + 27f, head.width - 28f, 16f),
                "One tree at a time - pick a path on the left. Changes apply instantly and persist in mod settings.");
            GUI.color = Color.white;

            float bx = head.xMax - 44f;   // leave room for the close X
            Rect raAll = new Rect(bx - 84f, head.y + 10f, 84f, 26f); bx = raAll.x - 8f;
            Rect raPath = new Rect(bx - 94f, head.y + 10f, 94f, 26f); bx = raPath.x - 12f;
            int edits = PlayerTuning.Count;
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = edits > 0 ? Palette.Gold : Palette.TextDim;
            Widgets.Label(new Rect(bx - 160f, head.y + 10f, 160f, 26f),
                edits > 0 ? edits + " edit" + (edits == 1 ? "" : "s") : "No edits");
            GUI.color = Color.white;
            if (MXStyle.Button(raPath, "Reset path")) ConfirmResetPath();
            if (MXStyle.Button(raAll, "Reset all")) ConfirmResetAll();

            // ── body: path rail + one popped-out tree + live preview ──
            Rect body = new Rect(rect.x + 10f, head.yMax + 8f, rect.width - 20f, rect.yMax - head.yMax - 18f);
            const float railW = 224f, prevW = 460f;
            DrawRail(new Rect(body.x, body.y, railW, body.height));
            DrawContent(new Rect(body.x + railW + 10f, body.y, body.width - railW - prevW - 20f, body.height));
            DrawPreview(new Rect(body.xMax - prevW, body.y, prevW, body.height));

            Text.Font = pf; Text.Anchor = pa; GUI.color = pc;
        }

        // ── left rail: one tree selected at a time ───────────────────────────
        private void DrawRail(Rect rect)
        {
            MXStyle.Fill(rect, PanelBG);
            GUI.color = Hairline; Widgets.DrawBox(rect, 1); GUI.color = Color.white;
            Rect inner = rect.ContractedBy(6f);
            const float rowH = 40f;
            float contentH = 26f + groups.Count * (rowH + 4f);
            Rect view = new Rect(0f, 0f, FlatScroll.ScrollViewWidth(inner, contentH), contentH);
            FlatScroll.Begin(inner, ref railScroll, view);
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = Palette.TextDim;
            Widgets.Label(new Rect(4f, 2f, view.width - 8f, 20f), "Psycast trees");
            GUI.color = Color.white;
            float y = 26f;
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                Rect r = new Rect(0f, y, view.width, rowH);
                bool sel = i == selGroup;
                Widgets.DrawBoxSolid(r, sel ? RowSel : (Mouse.IsOver(r) ? InsetHi : Inset));
                GUI.color = Hairline; Widgets.DrawBox(r, 1); GUI.color = Color.white;
                if (sel) Widgets.DrawBoxSolid(new Rect(r.x, r.y, 3f, r.height), Palette.Accent);
                var icon = g.skills.Count > 0 ? g.skills[0].icon : null;
                if (icon != null) GUI.DrawTexture(new Rect(r.x + 8f, r.y + 8f, 24f, 24f), icon);
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = sel ? Color.white : Palette.Stat;
                Widgets.Label(new Rect(r.x + 40f, r.y + 3f, r.width - 44f, 19f), g.label);
                int ed = PlayerTuning.CountFor(g.defNames);
                Text.Font = GameFont.Tiny; GUI.color = ed > 0 ? Palette.Gold : Palette.TextDim;
                Widgets.Label(new Rect(r.x + 40f, r.y + 21f, r.width - 44f, 16f),
                    g.skills.Count + " skills" + (ed > 0 ? "  \u00b7  " + ed + " edited" : ""));
                GUI.color = Color.white;
                if (Widgets.ButtonInvisible(r) && !sel)
                {
                    selGroup = i; bodyScroll = Vector2.zero;
                    selSkill = g.skills.Count > 0 ? g.skills[0] : null;
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
                y += rowH + 4f;
            }
            FlatScroll.End(inner, ref railScroll, view, 0xBE01);
        }

        // ── right pane: the selected tree's skill cards ──────────────────────
        private void DrawContent(Rect rect)
        {
            MXStyle.Fill(rect, PanelBG);
            GUI.color = Hairline; Widgets.DrawBox(rect, 1); GUI.color = Color.white;
            var g = Cur;
            if (g == null)
            {
                Text.Anchor = TextAnchor.MiddleCenter; GUI.color = Palette.TextDim;
                Widgets.Label(rect, "No psycast paths found.");
                GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
                return;
            }
            Rect inner = rect.ContractedBy(10f);
            float total = 0f, selY = -1f;
            foreach (var a in g.skills)
            {
                if (a == selSkill) selY = total;
                total += CardHeight(a, g) + 10f;
            }
            // A fresh selection (card click / preview-node click) jumps the list so the selected
            // skill's editing card sits at the top.
            if (scrollToSel)
            {
                if (selY >= 0f) bodyScroll.y = Mathf.Clamp(selY, 0f, Mathf.Max(0f, total - inner.height));
                scrollToSel = false;
            }
            float viewH = Mathf.Max(total, inner.height);
            Rect view = new Rect(0f, 0f, FlatScroll.ScrollViewWidth(inner, viewH), viewH);
            FlatScroll.Begin(inner, ref bodyScroll, view);
            float y = 0f;
            foreach (var a in g.skills)
            {
                float h = CardHeight(a, g);
                if (y + h >= bodyScroll.y - 60f && y <= bodyScroll.y + inner.height + 60f)
                    DrawCard(new Rect(0f, y, view.width, h), a, g);
                y += h + 10f;
            }
            FlatScroll.End(inner, ref bodyScroll, view, 0xBE02);
        }

        // Card = 9px padding + 28 header + 30 primary + two sections of (22 title + 30/row + 24 add-row).
        private static float CardHeight(AbilityDef a, PathGroup g)
        {
            int nSrc = PsycastInfo.SynergySources(a).Count;
            int nEmp = EmpowerTargets(a, g).Count;
            return 168f + 30f * (nSrc + nEmp);
        }

        private static List<AbilityDef> EmpowerTargets(AbilityDef a, PathGroup g)
        {
            var list = new List<AbilityDef>();
            foreach (var z in g.skills)
                if (z != a && PsycastInfo.SynergySources(z).Contains(a)) list.Add(z);
            return list;
        }

        private void DrawCard(Rect r, AbilityDef a, PathGroup g)
        {
            Widgets.DrawBoxSolid(r, Darkest);
            Widgets.DrawBoxSolid(r.ContractedBy(1f), PanelBG);
            GUI.color = Hairline; Widgets.DrawBox(r, 1); GUI.color = Color.white;

            Rect c = r.ContractedBy(9f);
            float y = c.y;

            // ── header: icon + name + tier/role ──
            if (a.icon != null) GUI.DrawTexture(new Rect(c.x, y + 2f, 24f, 24f), a.icon);
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = Color.white;
            Widgets.Label(new Rect(c.x + 30f, y, c.width - 170f, 28f), a.LabelCap);
            var role = PsycastInfo.RoleOf(a);
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleRight; GUI.color = PsycastInfo.RoleColor(role);
            Widgets.Label(new Rect(c.x, y, c.width, 28f), "T" + Tier(a) + "  \u00b7  " + RoleCase(role));
            GUI.color = Color.white;
            y += 28f;

            // ── primary ("skill effect") ──
            var primOpts = PrimOpts(a);
            int primCur = ToInt(PsycastInfo.PrimaryStat(a));
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = Palette.Gold;
            Widgets.Label(new Rect(c.x + 2f, y, SkillW - 8f, RowH), "Skill effect");
            GUI.color = Color.white;
            if (primOpts.Count > 1 || primCur >= 0)
            {
                Rect statR = new Rect(c.x + SkillW + 6f, y, StatW, RowH);
                if (Chip(statR, StatName(primCur), PlayerTuning.HasPrim(a.defName), StatTip(primCur)))
                {
                    var a2 = a;
                    OpenStatMenu(WithCur(primOpts, primCur), primCur, v => PickPrim(a2, v));
                }
                Rect strR = new Rect(statR.xMax + 6f, y, StrW, RowH);
                float strCur = PsycastInfo.PrimaryStrength(a);
                if (Chip(strR, "\u00d7" + Trim(strCur), PlayerTuning.HasPrimStr(a.defName),
                        "Hand-tuned multiplier on this skill's own per-level gain."))
                {
                    var a2 = a;
                    OpenStrMenu(strCur, v => PickPrimStr(a2, v));
                }
                InfoLabel(new Rect(strR.xMax + 8f, y, c.xMax - strR.xMax - 12f, RowH), PrimInfo(a, primCur));
            }
            else
            {
                GUI.color = Palette.TextDim; Text.Anchor = TextAnchor.MiddleLeft; Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(c.x + SkillW + 6f, y, c.width - SkillW - 6f, RowH), "Utility - no scaled stat");
                GUI.color = Color.white;
            }
            y += RowH + 4f;

            // ── gains power from (received synergies; skill + stat + strength all editable) ──
            var srcs = PsycastInfo.SynergySources(a);
            bool srcsEdited = PlayerTuning.HasSrcs(a.defName);
            y = SectionTitle(c, y, "Gains power from", srcs.Count);
            for (int i = 0; i < srcs.Count; i++)
            {
                var src = srcs[i];
                Rect skR = new Rect(c.x, y, SkillW, RowH);
                if (Chip(skR, src.LabelCap, srcsEdited,
                        "The skill whose levels feed this one. Click to swap it for another skill in this tree."))
                {
                    var a2 = a; int idx = i;
                    OpenSkillMenu(SourceCandidates(g, a, srcs), z => ReplaceSource(a2, idx, z));
                }
                int cur = ToInt(PsycastInfo.EdgeStat(src, a));
                Rect stR = new Rect(skR.xMax + 6f, y, StatW, RowH);
                if (Chip(stR, StatName(cur), PlayerTuning.HasEdge(src.defName, a.defName), StatTip(cur)))
                {
                    var s2 = src; var a2 = a;
                    OpenStatMenu(WithCur(EdgeOpts(a), cur), cur, v => PickEdge(s2, a2, v));
                }
                float es = PsycastInfo.EdgeStrength(src, a);
                Rect srR = new Rect(stR.xMax + 6f, y, StrW, RowH);
                if (Chip(srR, "\u00d7" + Trim(es), PlayerTuning.HasEdgeStr(src.defName, a.defName),
                        "Hand-tuned multiplier on how strongly this source feeds the skill."))
                {
                    var s2 = src; var a2 = a;
                    OpenStrMenu(es, v => PickEdgeStr(s2, a2, v));
                }
                Rect rmR = new Rect(c.xMax - 22f, y + 2f, 22f, RowH - 4f);
                if (MiniButton(rmR, "\u00d7", "Remove this synergy source."))
                    RemoveSource(a, src);
                InfoLabel(new Rect(srR.xMax + 8f, y, rmR.x - srR.xMax - 12f, RowH), EdgeInfo(src, a, cur));
                y += RowH + 4f;
            }
            {
                var cands = SourceCandidates(g, a, srcs);
                bool can = cands.Count > 0 && srcs.Count < MaxSources;
                Rect addR = new Rect(c.x, y, SkillW, 22f);
                if (AddChip(addR, "+  Add source", can,
                        srcs.Count >= MaxSources ? "Source cap reached (" + MaxSources + ")."
                                                 : "Add another skill from this tree that feeds this one."))
                {
                    var a2 = a;
                    OpenSkillMenu(cands, z => AddSource(a2, z));
                }
                y += 24f;
            }

            // ── empowers (outgoing edges; edits the TARGET's source list) ──
            var emps = EmpowerTargets(a, g);
            y = SectionTitle(c, y, "Empowers", emps.Count);
            for (int i = 0; i < emps.Count; i++)
            {
                var tgt = emps[i];
                Rect skR = new Rect(c.x, y, SkillW, RowH);
                if (Chip(skR, tgt.LabelCap, PlayerTuning.HasSrcs(tgt.defName),
                        "The skill this one feeds. Click to redirect the empower to another skill in this tree."))
                {
                    var a2 = a; var old = tgt;
                    OpenSkillMenu(TargetCandidates(g, a), w => { RemoveSource(old, a2); AddSource(w, a2); });
                }
                int cur = ToInt(PsycastInfo.EdgeStat(a, tgt));
                Rect stR = new Rect(skR.xMax + 6f, y, StatW, RowH);
                if (Chip(stR, StatName(cur), PlayerTuning.HasEdge(a.defName, tgt.defName), StatTip(cur)))
                {
                    var a2 = a; var t2 = tgt;
                    OpenStatMenu(WithCur(EdgeOpts(tgt), cur), cur, v => PickEdge(a2, t2, v));
                }
                float es = PsycastInfo.EdgeStrength(a, tgt);
                Rect srR = new Rect(stR.xMax + 6f, y, StrW, RowH);
                if (Chip(srR, "\u00d7" + Trim(es), PlayerTuning.HasEdgeStr(a.defName, tgt.defName),
                        "Hand-tuned multiplier on how strongly this skill feeds the target."))
                {
                    var a2 = a; var t2 = tgt;
                    OpenStrMenu(es, v => PickEdgeStr(a2, t2, v));
                }
                Rect rmR = new Rect(c.xMax - 22f, y + 2f, 22f, RowH - 4f);
                if (MiniButton(rmR, "\u00d7", "Stop empowering this skill."))
                    RemoveSource(tgt, a);
                InfoLabel(new Rect(srR.xMax + 8f, y, rmR.x - srR.xMax - 12f, RowH), EdgeInfo(a, tgt, cur));
                y += RowH + 4f;
            }
            {
                var cands = TargetCandidates(g, a);
                Rect addR = new Rect(c.x, y, SkillW, 22f);
                if (AddChip(addR, "+  Add target", cands.Count > 0,
                        "Make this skill feed another skill in this tree (adds it to that skill's sources)."))
                {
                    var a2 = a;
                    OpenSkillMenu(cands, w => AddSource(w, a2));
                }
            }

            // Card selection LAST - the first-drawn IMGUI button under the cursor wins, so the
            // chips keep click priority and this only fires on empty card area. The selected
            // card feeds the live preview panel on the right.
            if (Widgets.ButtonInvisible(r, false) && selSkill != a)
            { selSkill = a; scrollToSel = true; SoundDefOf.Click.PlayOneShotOnCamera(); }
            if (selSkill == a)
            {
                GUI.color = new Color(Palette.Accent.r, Palette.Accent.g, Palette.Accent.b, 0.8f);
                Widgets.DrawBox(r, 2);
                GUI.color = Color.white;
            }
        }

        private static float SectionTitle(Rect c, float y, string label, int count)
        {
            y += 6f;
            var pf = Text.Font; var pa = Text.Anchor;
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = Palette.TextDim;
            Widgets.Label(new Rect(c.x + 2f, y, c.width - 4f, 16f), label + (count > 0 ? "  (" + count + ")" : ""));
            GUI.color = Color.white; Text.Font = pf; Text.Anchor = pa;
            return y + 16f;
        }

        // ── chips & small widgets ─────────────────────────────────────────────
        private static bool Chip(Rect r, string label, bool modified, string tip)
        {
            bool over = Mouse.IsOver(r);
            Widgets.DrawBoxSolid(r, over ? InsetHi : Inset);
            GUI.color = modified ? new Color(Palette.Accent.r, Palette.Accent.g, Palette.Accent.b, 0.85f) : Hairline;
            Widgets.DrawBox(r, 1);
            GUI.color = Color.white;
            var pf = Text.Font; var pa = Text.Anchor;
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Palette.Stat;
            Widgets.Label(new Rect(r.x + 7f, r.y, r.width - 22f, r.height), label);
            Text.Anchor = TextAnchor.MiddleRight; GUI.color = Palette.TextDim;
            Widgets.Label(new Rect(r.x, r.y, r.width - 6f, r.height), "\u25BC");
            GUI.color = Color.white; Text.Font = pf; Text.Anchor = pa;
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(r, tip);
            if (over) MouseoverSounds.DoRegion(r);
            return Widgets.ButtonInvisible(r);
        }

        private static bool AddChip(Rect r, string label, bool enabled, string tip)
        {
            bool over = enabled && Mouse.IsOver(r);
            Widgets.DrawBoxSolid(r, over ? InsetHi : new Color(Inset.r, Inset.g, Inset.b, enabled ? 0.7f : 0.3f));
            GUI.color = Hairline; Widgets.DrawBox(r, 1); GUI.color = Color.white;
            var pf = Text.Font; var pa = Text.Anchor;
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = enabled ? Palette.Accent : Palette.TextDim;
            Widgets.Label(r, label);
            GUI.color = Color.white; Text.Font = pf; Text.Anchor = pa;
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(r, tip);
            return enabled && Widgets.ButtonInvisible(r);
        }

        private static bool MiniButton(Rect r, string glyph, string tip)
        {
            bool over = Mouse.IsOver(r);
            Widgets.DrawBoxSolid(r, over ? new Color(0.45f, 0.18f, 0.18f) : Inset);
            GUI.color = Hairline; Widgets.DrawBox(r, 1); GUI.color = Color.white;
            var pf = Text.Font; var pa = Text.Anchor;
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = over ? Color.white : Palette.TextDim;
            Widgets.Label(r, glyph);
            GUI.color = Color.white; Text.Font = pf; Text.Anchor = pa;
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(r, tip);
            return Widgets.ButtonInvisible(r);
        }

        private static void InfoLabel(Rect r, string text)
        {
            if (text.NullOrEmpty()) return;
            var pf = Text.Font; var pa = Text.Anchor;
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = Palette.TextDim;
            Widgets.Label(r, text);
            GUI.color = Color.white; Text.Font = pf; Text.Anchor = pa;
        }

        // ── dropdown menus ────────────────────────────────────────────────────
        private static void OpenStatMenu(List<int> opts, int current, Action<int> pick)
        {
            var menu = new List<FloatMenuOption>();
            foreach (var o in opts)
            {
                int v = o;
                menu.Add(new FloatMenuOption((v == current ? "\u2713 " : "     ") + StatName(v), () => pick(v)));
            }
            Find.WindowStack.Add(new FloatMenu(menu));
        }

        private static readonly float[] Strengths = { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f, 2.5f, 3f };

        private static void OpenStrMenu(float current, Action<float> pick)
        {
            var menu = new List<FloatMenuOption>();
            if (!Strengths.Any(s => Approx(s, current)))
                menu.Add(new FloatMenuOption("\u2713 \u00d7" + Trim(current) + "  (current)", () => pick(current)));
            foreach (var s in Strengths)
            {
                float v = s;
                menu.Add(new FloatMenuOption((Approx(v, current) ? "\u2713 " : "     ") + "\u00d7" + Trim(v), () => pick(v)));
            }
            Find.WindowStack.Add(new FloatMenu(menu));
        }

        private static void OpenSkillMenu(List<AbilityDef> cands, Action<AbilityDef> pick)
        {
            var menu = new List<FloatMenuOption>();
            foreach (var z in cands)
            {
                var v = z;
                menu.Add(new FloatMenuOption(v.LabelCap + "   (T" + Tier(v) + ")", () => pick(v)));
            }
            if (menu.Count == 0) menu.Add(new FloatMenuOption("No eligible skills", null));
            Find.WindowStack.Add(new FloatMenu(menu));
        }

        // ── pick handlers: only a DIFFERENCE from the baseline is stored ─────
        private static void PickPrim(AbilityDef a, int v)
        {
            if (v == ToInt(PsycastInfo.BasePrimary(a))) PlayerTuning.ClearPrim(a.defName);
            else PlayerTuning.SetPrim(a.defName, v);
        }
        private static void PickPrimStr(AbilityDef a, float v)
        {
            if (Approx(v, PsycastInfo.BasePrimaryStrength(a))) PlayerTuning.ClearPrimStr(a.defName);
            else PlayerTuning.SetPrimStr(a.defName, v);
        }
        private static void PickEdge(AbilityDef src, AbilityDef tgt, int v)
        {
            if (v == ToInt(PsycastInfo.BaseEdgeStat(src, tgt))) PlayerTuning.ClearEdge(src.defName, tgt.defName);
            else PlayerTuning.SetEdge(src.defName, tgt.defName, v);
        }
        private static void PickEdgeStr(AbilityDef src, AbilityDef tgt, float v)
        {
            if (Approx(v, PsycastInfo.BaseEdgeStrength(src, tgt))) PlayerTuning.ClearEdgeStr(src.defName, tgt.defName);
            else PlayerTuning.SetEdgeStr(src.defName, tgt.defName, v);
        }

        // ── graph (source-list) mutations ─────────────────────────────────────
        private static void SetSources(AbilityDef tgt, List<string> names)
        {
            var baseNames = PsycastInfo.BaseSources(tgt).Select(d => d.defName).ToList();
            if (names.SequenceEqual(baseNames)) PlayerTuning.ClearSrcs(tgt.defName);
            else PlayerTuning.SetSrcs(tgt.defName, names);
        }
        private static void AddSource(AbilityDef tgt, AbilityDef src)
        {
            var names = PsycastInfo.SynergySources(tgt).Select(d => d.defName).ToList();
            if (names.Contains(src.defName) || names.Count >= MaxSources) return;
            names.Add(src.defName);
            SetSources(tgt, names);
        }
        private static void RemoveSource(AbilityDef tgt, AbilityDef src)
        {
            var names = PsycastInfo.SynergySources(tgt).Select(d => d.defName).ToList();
            if (!names.Remove(src.defName)) return;
            PlayerTuning.PurgeEdge(src.defName, tgt.defName);   // orphaned per-edge overrides go with it
            SetSources(tgt, names);
        }
        private static void ReplaceSource(AbilityDef tgt, int index, AbilityDef nw)
        {
            var names = PsycastInfo.SynergySources(tgt).Select(d => d.defName).ToList();
            if (index < 0 || index >= names.Count || names.Contains(nw.defName)) return;
            PlayerTuning.PurgeEdge(names[index], tgt.defName);
            names[index] = nw.defName;
            SetSources(tgt, names);
        }

        private static List<AbilityDef> SourceCandidates(PathGroup g, AbilityDef tgt, List<AbilityDef> cur)
            => g.skills.Where(z => z != tgt && !cur.Contains(z)).ToList();

        private static List<AbilityDef> TargetCandidates(PathGroup g, AbilityDef src)
            => g.skills.Where(z => z != src && !PsycastInfo.SynergySources(z).Contains(src)
                                && PsycastInfo.SynergySources(z).Count < MaxSources).ToList();

        // ── resets ────────────────────────────────────────────────────────────
        private void ConfirmResetPath()
        {
            var g = Cur; if (g == null) return;
            int n = PlayerTuning.CountFor(g.defNames);
            if (n == 0) { Messages.Message("No edits in this tree.", MessageTypeDefOf.RejectInput, false); return; }
            Find.WindowStack.Add(new Dialog_Confirm("Reset " + g.label,
                "Discard your " + n + " balance edit(s) touching the " + g.label + " tree? Baked-in defaults are kept.",
                () => { PlayerTuning.ResetDefs(g.defNames); PsycastSynergiesMod.Instance?.WriteSettings(); }));
        }
        private void ConfirmResetAll()
        {
            int n = PlayerTuning.Count;
            if (n == 0) { Messages.Message("No edits to reset.", MessageTypeDefOf.RejectInput, false); return; }
            Find.WindowStack.Add(new Dialog_Confirm("Reset all",
                "Discard ALL " + n + " of your balance edits and return to the mod's defaults?",
                () => { PlayerTuning.ResetAll(); PsycastSynergiesMod.Instance?.WriteSettings(); }));
        }

        // ── live preview: the selected skill on its tree, labels updating live ──
        private static readonly Dictionary<AbilityDef, Vector2> previewPos = new Dictionary<AbilityDef, Vector2>();
        private static readonly Color SrcGreen = new Color(0.3f, 1f, 0.45f);   // matches the tab's source pulse
        private static readonly Color EmpGold = new Color(1f, 0.78f, 0.25f);   // matches the tab's empower pulse

        private void DrawPreview(Rect rect)
        {
            MXStyle.Fill(rect, PanelBG);
            GUI.color = Hairline; Widgets.DrawBox(rect, 1); GUI.color = Color.white;
            var g = Cur;
            if (g == null) return;
            if (selSkill == null || !g.defNames.Contains(selSkill.defName))
                selSkill = g.skills.Count > 0 ? g.skills[0] : null;
            var sel = selSkill;
            Rect inner = rect.ContractedBy(8f);
            if (sel == null)
            {
                InfoLabel(inner, "Select a skill card to preview it here.");
                return;
            }

            var srcs = PsycastInfo.SynergySources(sel);
            var emps = EmpowerTargets(sel, g);
            List<PsycastInfo.Effect> fx = null;
            if (pawn != null && psy != null)
                try { fx = PsycastInfo.Scalables(pawn, sel, comp?.LearnedAbilities?.FirstOrDefault(x => x.def == sel)); }
                catch { }

            // Reserve the scrollbar gutter unconditionally so layout math is stable (suite-style).
            float vw = inner.width - FlatScroll.BarW;
            // Real path art - PathArt honors retextures (last active mod wins) and decodes VPE's
            // odd-sized .dds safely. Its aspect drives the card height so nodes land exactly like
            // the psycast tab; flat TileTint only when no art resolves.
            Texture2D bg = g.path != null ? PathArt.Get(g.path, false) : null;
            float aspect = (bg != null && bg.width > 0) ? (float)bg.height / bg.width : 1.32f;
            float artH = g.path != null ? Mathf.Clamp(vw * aspect, 320f, 720f) : 0f;
            float treeBlock = g.path != null ? artH + 22f + 8f : 22f;
            int fxRows = fx != null ? Mathf.Max(1, fx.Count) : 1;
            float contentH = 30f + treeBlock
                + 22f + fxRows * 17f + 4f
                + 22f + (1 + srcs.Count + emps.Count) * 17f + 10f;

            Rect view = new Rect(0f, 0f, vw, Mathf.Max(contentH, inner.height));
            FlatScroll.Begin(inner, ref previewScroll, view);
            Rect vc = new Rect(0f, 0f, vw, 0f);
            float y = 0f;

            // header: the skill being edited
            if (sel.icon != null) GUI.DrawTexture(new Rect(0f, y + 2f, 24f, 24f), sel.icon);
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = Color.white;
            Widgets.Label(new Rect(30f, y, vw - 120f, 28f), sel.LabelCap);
            var role = PsycastInfo.RoleOf(sel);
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleRight; GUI.color = PsycastInfo.RoleColor(role);
            Widgets.Label(new Rect(0f, y, vw, 28f), "T" + Tier(sel) + " \u00b7 " + RoleCase(role));
            GUI.color = Color.white; Text.Anchor = TextAnchor.MiddleLeft;
            y += 30f;

            // the tree card (Modern-UI style, flat tint - VPE's .dds backgrounds fail on 1.6),
            // every node rendered UNLOCKED; the edited skill + its live graph highlighted.
            if (g.path != null)
            {
                Rect art = new Rect(0f, y, vw, artH);
                if (bg != null) GUI.DrawTexture(art, bg, ScaleMode.StretchToFill);
                else Widgets.DrawBoxSolid(art, TileTint(g.path));
                GUI.color = Hairline; Widgets.DrawBox(art, 1); GUI.color = Color.white;
                if (psy != null && comp != null)
                {
                    PsycastsUIUtility.Hediff = psy;
                    PsycastsUIUtility.CompAbilities = comp;
                    previewPos.Clear();
                    PsycastsUIUtility.DoPathAbilities(art.ContractedBy(8f), g.path, previewPos, DoPreviewNode);
                    DrawSynergyOverlay(art, sel, srcs, emps);
                }
                else
                {
                    Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = Palette.TextDim;
                    Widgets.Label(art, "Open from a psycaster's tab\nto render the live tree.");
                    GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
                }
                Rect labelBar = new Rect(0f, art.yMax, vw, 22f);
                MXStyle.Fill(labelBar, new Color(0.043f, 0.05f, 0.06f, 0.97f));
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = Palette.Stat;
                Widgets.Label(labelBar, g.label);
                GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
                y = labelBar.yMax + 8f;
            }
            else
            {
                InfoLabel(new Rect(2f, y, vw - 4f, 18f), "No tree - unpathed psycasts.");
                y += 22f;
            }

            // live values: the opened pawn's REAL numbers; they move as you edit
            y = SectionTitle(vc, y, "Live values" + (pawn != null ? " \u2014 " + pawn.LabelShortCap.ToString() : ""), 0);
            if (fx != null && fx.Count > 0)
            {
                var prim = PsycastInfo.PrimaryStat(sel);
                foreach (var e in fx)
                {
                    Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = Palette.TextDim;
                    Widgets.Label(new Rect(2f, y, vw * 0.52f, 16f), e.label);
                    Text.Anchor = TextAnchor.MiddleRight;
                    GUI.color = (e.stat != null && prim != null && e.stat.Value == prim.Value) ? Palette.Gold : Palette.Stat;
                    Widgets.Label(new Rect(vw * 0.42f, y, vw * 0.58f - 2f, 16f), e.value);
                    GUI.color = Color.white;
                    y += 17f;
                }
            }
            else
            {
                InfoLabel(new Rect(2f, y, vw - 4f, 16f), pawn == null
                    ? "No pawn selected - live values unavailable."
                    : "No scalable effects detected.");
                y += 17f;
            }
            Text.Anchor = TextAnchor.UpperLeft;

            // scaling graph for this skill: primary + received + given, live rates
            y = SectionTitle(vc, y, "Scaling \u2014 edits apply live", 0);
            int prim2 = ToInt(PsycastInfo.PrimaryStat(sel));
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = Palette.Gold;
            Widgets.Label(new Rect(2f, y, vw - 4f, 16f),
                "Skill effect: " + StatName(prim2) + (prim2 >= 0 ? "  " + PrimInfo(sel, prim2) : ""));
            y += 17f;
            foreach (var s in srcs)
            {
                int es = ToInt(PsycastInfo.EdgeStat(s, sel));
                GUI.color = SrcGreen;
                Widgets.Label(new Rect(2f, y, vw - 4f, 16f),
                    "\u25C2 " + s.LabelCap + ": " + StatName(es) + (es >= 0 ? "  " + EdgeInfo(s, sel, es) : ""));
                y += 17f;
            }
            foreach (var t in emps)
            {
                int es = ToInt(PsycastInfo.EdgeStat(sel, t));
                GUI.color = EmpGold;
                Widgets.Label(new Rect(2f, y, vw - 4f, 16f),
                    "\u25B8 " + t.LabelCap + ": " + StatName(es) + (es >= 0 ? "  " + EdgeInfo(sel, t, es) : ""));
                y += 17f;
            }
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;

            FlatScroll.End(inner, ref previewScroll, view, 0xBE03);
        }

        private void DoPreviewNode(Rect r, AbilityDef ability)
        {
            PsycastsUIUtility.DrawAbility(r, ability);   // full-color node; the level-pips postfix rides along
            TooltipHandler.TipRegion(r, () => ability.LabelCap + "\n\n" + ability.description,
                ability.GetHashCode() ^ 0x5EED);
            // Observe (don't consume) the click so level-invest clicks still work: any left-click
            // on a node also focuses it in the editor.
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0
                && Mouse.IsOver(r) && selSkill != ability)
            { selSkill = ability; scrollToSel = true; }
        }

        private void DrawSynergyOverlay(Rect art, AbilityDef sel, List<AbilityDef> srcs, List<AbilityDef> emps)
        {
            if (!previewPos.TryGetValue(sel, out var pc)) return;
            foreach (var s in srcs)
                if (previewPos.TryGetValue(s, out var ps))
                    Widgets.DrawLine(ps, pc, new Color(SrcGreen.r, SrcGreen.g, SrcGreen.b, 0.85f), 2f);
            foreach (var t in emps)
                if (previewPos.TryGetValue(t, out var pt))
                    Widgets.DrawLine(pc, pt, new Color(EmpGold.r, EmpGold.g, EmpGold.b, 0.85f), 2f);
            foreach (var s in srcs)
                if (previewPos.TryGetValue(s, out var ps))
                {
                    NodeBox(ps, SrcGreen, 22f);
                    NodeTag(art, ps, StatName(ToInt(PsycastInfo.EdgeStat(s, sel))), SrcGreen, false);
                }
            foreach (var t in emps)
                if (previewPos.TryGetValue(t, out var pt))
                {
                    NodeBox(pt, EmpGold, 26f);
                    NodeTag(art, pt, StatName(ToInt(PsycastInfo.EdgeStat(sel, t))), EmpGold, true);
                }
            NodeBox(pc, Palette.Accent, 24f);
            NodeTag(art, pc, "Editing \u00b7 " + StatName(ToInt(PsycastInfo.PrimaryStat(sel))), Palette.Accent, false);
        }

        private static void NodeBox(Vector2 c, Color col, float half)
        {
            GUI.color = col;
            Widgets.DrawBox(new Rect(c.x - half, c.y - half, half * 2f, half * 2f), 2);
            GUI.color = Color.white;
        }

        // Small dark label chip near a tree node (sources below, empower targets above),
        // clamped inside the art rect.
        private static void NodeTag(Rect art, Vector2 center, string text, Color col, bool above)
        {
            if (text.NullOrEmpty()) return;
            var pf = Text.Font; var pa = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Vector2 sz = Text.CalcSize(text);
            sz.x += 8f;
            float x = Mathf.Clamp(center.x - sz.x / 2f, art.x + 2f, art.xMax - sz.x - 2f);
            float yy = above ? center.y - 41f : center.y + 24f;
            yy = Mathf.Clamp(yy, art.y + 2f, art.yMax - 17f);
            Rect r = new Rect(x, yy, sz.x, 15f);
            Widgets.DrawBoxSolid(r, new Color(0f, 0f, 0f, 0.72f));
            GUI.color = col; Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, text);
            GUI.color = Color.white; Text.Font = pf; Text.Anchor = pa;
        }

        // Muted per-path tile colour (mirrors Modern Psycasts UI's TileTint, unlocked variant).
        private static Color TileTint(PsycasterPathDef def)
        {
            Color c;
            if (def.backgroundColor.a > 0.05f)
                c = new Color(def.backgroundColor.r, def.backgroundColor.g, def.backgroundColor.b);
            else
            {
                float hue = (Mathf.Abs(def.defName.GetHashCode()) % 360) / 360f;
                c = Color.HSVToRGB(hue, 0.32f, 1f);
            }
            return new Color(c.r * 0.34f, c.g * 0.34f, c.b * 0.34f, 1f);
        }

        // ── option pools & labels ─────────────────────────────────────────────
        // Stats offered for a PRIMARY (skill effect): valid for the ability, never Charges/Insight.
        private static List<int> PrimOpts(AbilityDef a)
        {
            var list = new List<int> { -1 };
            for (int i = 0; i <= 13; i++)
            {
                var st = (SynStat)i;
                if (st == SynStat.Charges || st == SynStat.Insight) continue;
                if (PsycastInfo.HasStat(a, st)) list.Add(i);
            }
            return list;
        }

        // Stats offered for an EDGE feeding target t: valid for t, never Insight (Charges allowed).
        private static List<int> EdgeOpts(AbilityDef t)
        {
            var list = new List<int> { -1 };
            for (int i = 0; i <= 13; i++)
            {
                var st = (SynStat)i;
                if (st == SynStat.Insight) continue;
                if (PsycastInfo.HasStat(t, st)) list.Add(i);
            }
            return list;
        }

        private static List<int> WithCur(List<int> opts, int cur)
        {
            if (!opts.Contains(cur)) opts.Add(cur);
            return opts;
        }

        private static string StatName(int v) => v < 0 ? "None" : SynergyRules.StatLabel((SynStat)v);

        // Sentence-case role tag ("CONTROL" -> "Control") to match the game's casing.
        private static string RoleCase(Role r)
        {
            string s = SynergyRules.RoleLabel(r);
            return s.Length > 1 ? s.Substring(0, 1) + s.Substring(1).ToLowerInvariant() : s;
        }

        private static string StatTip(int v)
        {
            if (v < 0) return "No scaled stat.";
            switch ((SynStat)v)
            {
                case SynStat.Power: return "Damage: melee/explosion/projectile damage of the cast.";
                case SynStat.Radius: return "Radius: area-of-effect / blast radius (beams: beam size).";
                case SynStat.Duration: return "Duration: how long the effect lasts.";
                case SynStat.Strength: return "Strength: severity of the applied buff/debuff.";
                case SynStat.Range: return "Range: maximum cast range.";
                case SynStat.Cooldown: return "Cooldown: reduces the cast's cooldown.";
                case SynStat.Targets: return "Targets: extra targets pickable per cast (multi-strike).";
                case SynStat.Charges: return "Charges: banked free casts (no psyfocus or heat).";
                case SynStat.Efficiency: return "Efficiency: reduces psyfocus + neural heat cost.";
                case SynStat.Haste: return "Cast speed: reduces casting time.";
                case SynStat.Yield: return "Psyfocus yield: refunds psyfocus on cast.";
                case SynStat.Insight: return "Insight: bonus psycast XP (retired).";
                case SynStat.ProjectileCount: return "Projectiles: number of projectiles/meteorites a volley spawns.";
                case SynStat.SummonCount: return "Summons: number of minions a summon spawns.";
            }
            return "";
        }

        private static string PrimInfo(AbilityDef a, int cur)
        {
            if (cur < 0) return "";
            var s = PsycastSynergiesMod.Settings;
            if (s == null) return "";
            float pct = s.perLevelPct * SkillSystem.CountBoost((SynStat)cur) * PsycastInfo.PrimaryStrength(a) * 100f;
            return "+" + pct.ToString("0.##") + "%/lvl (own levels)";
        }

        private static string EdgeInfo(AbilityDef src, AbilityDef tgt, int cur)
        {
            if (cur < 0) return "";
            float pct = SkillSystem.SynergyRate((SynStat)cur) * PsycastInfo.EdgeStrength(src, tgt) * 100f;
            return "+" + pct.ToString("0.##") + "%/lvl";
        }

        private static int ToInt(SynStat? s) => s.HasValue ? (int)s.Value : -1;
        private static bool Approx(float a, float b) => Mathf.Abs(a - b) < 0.001f;
        private static string Trim(float f) => f.ToString("0.##");
    }
}

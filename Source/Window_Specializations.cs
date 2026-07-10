#nullable disable
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using VEF.Abilities;
using VanillaPsycastsExpanded;
using VanillaPsycastsExpanded.UI;
using AbilityDef = VEF.Abilities.AbilityDef;

namespace PsycastSynergies
{
    // Diablo-2 / PoE-style specialization constellation. Zoomable + pannable inside a clipped box,
    // with a right-side panel that previews bonuses/perks and confirms staged picks before applying.
    [StaticConstructorOnStartup]
    public class Window_Specializations : Window
    {
        private static readonly Texture2D NodeRingTex = ContentFinder<Texture2D>.Get("UI/Specs/node_ring", false);
        private static readonly Texture2D NodeGlowTex = ContentFinder<Texture2D>.Get("UI/Specs/node_glow", false);
        private static readonly Texture2D OrbitTex = ContentFinder<Texture2D>.Get("UI/Specs/orbit_ring", false);
        private static readonly Texture2D ApplyTex = ContentFinder<Texture2D>.Get("UI/Specs/apply", false);
        private static readonly Texture2D ClearTex = ContentFinder<Texture2D>.Get("UI/Specs/clear", false);
        private static readonly Texture2D DevPointsTex = ContentFinder<Texture2D>.Get("UI/Specs/dev_points", false);
        private static readonly Texture2D DevResetTex = ContentFinder<Texture2D>.Get("UI/Specs/dev_reset", false);
        private static readonly Texture2D DevEditTex = ContentFinder<Texture2D>.Get("UI/Specs/dev_edit", false);
        private static readonly Texture2D DevLogTex = ContentFinder<Texture2D>.Get("UI/Specs/dev_log", false);
        private static readonly Texture2D ZoomInTex = ContentFinder<Texture2D>.Get("UI/Specs/zoom_in", false);
        private static readonly Texture2D ZoomOutTex = ContentFinder<Texture2D>.Get("UI/Specs/zoom_out", false);
        private static readonly Texture2D ViewResetTex = ContentFinder<Texture2D>.Get("UI/Specs/view_reset", false);
        private static readonly Texture2D RespecTex = ContentFinder<Texture2D>.Get("UI/Specs/respec", false);

        private Pawn pawn;
        private const float BaseCellW = 138f, BaseCellH = 126f, BaseNode = 52f;
        private const float PanelW = 326f;

        private static bool editMode;        // dev: drag nodes to reposition
        private static Spec dragging;
        private static Vector2 panOffset;    // drag empty space to pan the constellation
        private static Vector2 panTarget;    // smooth auto-pan target (node-select nudge)
        private static float zoom = 1.45f;
        private static bool panning;

        // Staged (not-yet-applied) selections.
        private readonly HashSet<string> pending = new HashSet<string>();
        private AbilityDef pendMastery;
        private PsycasterPathDef pendDiscipline;
        private DamageDef pendAttune;

        // Frame-local layout (set each DoWindowContents).
        private Vector2 _origin;
        private Vector2 _treeSize;
        private float _cw, _ch, _node;
        private Vector2 _kc;        // kindling center (ring pivot)
        private Spec hoverSpec;
        private bool needsCenter = true;   // on open, center the view on Kindling
        private Vector2 perkScroll;

        public override Vector2 InitialSize => new Vector2(1120f, 860f);
        protected override float Margin => 0f;

        public Window_Specializations(Pawn pawn)
        {
            this.pawn = pawn;
            panTarget = panOffset;
            doWindowBackground = false;
            doCloseX = true;
            closeOnClickedOutside = true;   // click anywhere outside the constellation to close
            absorbInputAroundWindow = false;
            draggable = false;   // body drag pans the constellation instead of moving the window
            preventCameraMotion = false;
        }

        public override void PreClose()
        {
            base.PreClose();
            pending.Clear(); pendMastery = null; pendDiscipline = null; pendAttune = null;
        }

        private SpecData Data => GameComponent_PsycastSynergies.Instance?.GetSpec(pawn, create: true);

        // While the window is open, follow the player's map selection: clicking another psycaster
        // rebinds the whole tree to them (staged picks are per-pawn, so they're discarded).
        private void SyncToSelectedPawn()
        {
            var sel = Find.Selector.SingleSelectedThing as Pawn;
            if (sel == null || sel == pawn || sel.Psycasts() == null) return;
            pawn = sel;
            pending.Clear(); pendMastery = null; pendDiscipline = null; pendAttune = null;
            perkScroll = Vector2.zero;
        }

        private static Color Branch(string b)
        {
            switch (b)
            {
                case "Offense": return Palette.Bad;
                case "Control": return new Color(1f, 0.62f, 0.24f);
                case "Ward": return new Color(0.3f, 0.75f, 0.85f);
                case "Flow": return Palette.Accent;
                case "Apex": return Palette.Gold;
                case "Mod": return new Color(0.72f, 0.46f, 0.92f);
                case "Halcyon": return new Color(0.44f, 0.81f, 0.5f);
                case "Singularity": return new Color(0.9f, 0.78f, 0.43f);
                case "Penumbra": return new Color(0.66f, 0.55f, 0.91f);
                case "Empyrean": return new Color(0.54f, 0.69f, 1f);
                case "Pandemonium": return new Color(0.93f, 0.33f, 0.38f);
                case "Chronos": return new Color(0.27f, 0.78f, 0.75f);
                case "Consonance": return new Color(0.91f, 0.6f, 0.82f);
                default: return new Color(0.7f, 0.72f, 0.76f);
            }
        }

        // Un-rotated grid center.
        private Vector2 RawCenter(Spec sp)
            => new Vector2(_origin.x + (sp.col + 0.5f) * _cw + _node * 0.5f, _origin.y + sp.row * _ch + _node * 0.5f);

        // Live center: each orb gently floats around its home position (independent slow Lissajous
        // drift, deterministic phase per node). Frozen (raw) while editing the layout.
        private Vector2 NodeCenter(Spec sp)
        {
            Vector2 raw = RawCenter(sp);
            if (editMode) return raw;
            uint h = (uint)((sp.id ?? "").GetHashCode() & 0x7fffffff);
            float pa = (h % 628u) / 100f;
            float pb = ((h / 7u) % 628u) / 100f;
            float t = Time.realtimeSinceStartup;
            float amp = _node * 0.10f;
            return raw + new Vector2(Mathf.Sin(t * 0.45f + pa), Mathf.Sin(t * 0.37f + pb)) * amp;
        }

        private Rect NodeRect(Spec sp)
        {
            Vector2 c = NodeCenter(sp);
            return new Rect(c.x - _node * 0.5f, c.y - _node * 0.5f, _node, _node);
        }

        private bool Has(SpecData d, string id) => d.Owns(id) || pending.Contains(id);

        private bool PrereqMet(SpecData d, Spec sp, bool withPending)
        {
            if (GroupLocked(d, sp, withPending)) return false;
            foreach (var p in sp.prereqs) if (!(withPending ? Has(d, p) : d.Owns(p))) return false;
            if (sp.anyPrereq.Length > 0 && !sp.anyPrereq.Any(p => withPending ? Has(d, p) : d.Owns(p))) return false;
            return true;
        }

        // True if another node in this node's exclusive group is already owned (or staged).
        private bool GroupLocked(SpecData d, Spec sp, bool withPending = true)
        {
            if (string.IsNullOrEmpty(sp.exclusiveGroup)) return false;
            foreach (var o in Specs.All)
                if (o != sp && o.exclusiveGroup == sp.exclusiveGroup && (withPending ? Has(d, o.id) : d.Owns(o.id)))
                    return true;
            return false;
        }

        private int PendingCost() => pending.Sum(id => Specs.Get(id)?.cost ?? 0);

        private void DrawRing(Vector2 center, float dia, float alpha)
        {
            if (OrbitTex == null) return;
            GUI.color = new Color(0.55f, 0.72f, 1f, alpha);
            GUI.DrawTexture(new Rect(center.x - dia / 2f, center.y - dia / 2f, dia, dia), OrbitTex);
            GUI.color = Color.white;
        }

        public override void DoWindowContents(Rect inRect)
        {
            SyncToSelectedPawn();
            var d = Data;
            if (d == null) { Close(); return; }

            resizeable = editMode;
            Palette.DrawCard(inRect);

            // Header.
            Rect header = new Rect(inRect.x, inRect.y, inRect.width, 34f);
            Widgets.DrawBoxSolid(header, Palette.BGD);
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleRight; GUI.color = Palette.Gold;
            Widgets.Label(new Rect(header.xMax - 240f, header.y, 198f, header.height), "\u2605 " + d.points + " points");
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;

            // Split: tree box (clipped) on the left, preview/confirm panel on the right.
            Rect treeRect = new Rect(inRect.x, header.yMax, inRect.width - PanelW - 2f, inRect.height - header.height);
            Rect panelRect = new Rect(treeRect.xMax + 2f, header.yMax, PanelW - 2f, inRect.height - header.height);

            hoverSpec = null;
            DrawTree(d, treeRect);
            DrawPanel(d, panelRect);
        }

        private void DrawTree(SpecData d, Rect treeRect)
        {
            Widgets.DrawBoxSolid(treeRect, new Color(0.04f, 0.05f, 0.07f, 0.55f));

            GUI.BeginClip(treeRect);
            Rect local = new Rect(0f, 0f, treeRect.width, treeRect.height);
            _treeSize = new Vector2(local.width, local.height);
            if (Event.current.type == EventType.Repaint && !panning && !editMode && (panTarget - panOffset).sqrMagnitude > 0.01f)
                panOffset = Vector2.Lerp(panOffset, panTarget, 1f - Mathf.Exp(-Time.deltaTime * 9f));

            // Zoomed metrics + centered origin (clip-local), then pan.
            _cw = BaseCellW * zoom; _ch = BaseCellH * zoom; _node = BaseNode * zoom;
            float minC = 999f, maxC = -999f, minR = 999f, maxR = -999f;
            foreach (var sp in Specs.All)
            {
                if (sp.col < minC) minC = sp.col; if (sp.col > maxC) maxC = sp.col;
                if (sp.row < minR) minR = sp.row; if (sp.row > maxR) maxR = sp.row;
            }
            float contentW = (maxC - minC) * _cw + _node;
            float contentH = (maxR - minR) * _ch + _node;
            Vector2 originBase = new Vector2((local.width - contentW) / 2f - (minC + 0.5f) * _cw,
                                            (local.height - contentH) / 2f - minR * _ch);
            _origin = originBase + panOffset;
            _kc = RawCenter(Specs.Get("kindling"));

            // Open focused on Kindling (the root), regardless of where the view was left last time.
            if (needsCenter && Event.current.type == EventType.Repaint)
            {
                panOffset += _treeSize * 0.5f - _kc;
                panTarget = panOffset;
                needsCenter = false;
            }

            // Starfield (deterministic) with a faint parallax drift as the board pans.
            var rng = new System.Random(2024_0601);
            Vector2 starPar = panOffset * 0.12f;
            GUI.color = new Color(1f, 1f, 1f, 0.22f);
            for (int i = 0; i < 130; i++)
            {
                float sx = Mathf.Repeat((float)rng.NextDouble() * local.width + starPar.x, local.width);
                float sy = Mathf.Repeat((float)rng.NextDouble() * local.height + starPar.y, local.height);
                float sz = 1f + (float)rng.NextDouble() * 1.7f;
                GUI.DrawTexture(new Rect(sx, sy, sz, sz), BaseContent.WhiteTex);
            }
            GUI.color = Color.white;

            // Orbital rings aligned to the actual node shells, so the skills sit ON the star's
            // orbits (e.g. all four tier-1 nodes share the inner ring) and rotate along them.
            float r1 = (RawCenter(Specs.Get("surge")) - _kc).magnitude;
            float r2 = (RawCenter(Specs.Get("onslaught")) - _kc).magnitude;
            float r3 = (RawCenter(Specs.Get("tempest")) - _kc).magnitude;
            DrawRing(_kc, r1 * 2f, 0.18f);
            DrawRing(_kc, r2 * 2f, 0.12f);
            DrawRing(_kc, r3 * 2f, 0.08f);
            if (NodeGlowTex != null)
            {
                GUI.color = new Color(0.5f, 0.72f, 1f, 0.2f);
                float g = 170f * zoom;
                GUI.DrawTexture(new Rect(_kc.x - g / 2f, _kc.y - g / 2f, g, g), NodeGlowTex);
                GUI.color = Color.white;
            }

            // Prerequisite lines (lit when satisfied incl. staged).
            foreach (var sp in Specs.All)
            {
                Vector2 c = NodeRect(sp).center;
                foreach (var pre in sp.prereqs)
                {
                    var ps = Specs.Get(pre); if (ps == null) continue;
                    if (pre == "convergence")
                    {
                        // Faint thread from Convergence up to each ascension gate - the constellations still
                        // read as floating, but their origin is shown.
                        Widgets.DrawLine(c, NodeRect(ps).center, new Color(0.55f, 0.62f, 0.85f, 0.16f), 1f);
                        continue;
                    }
                    bool lit = Has(d, sp.id) || (Has(d, pre) && PrereqMet(d, sp, true));
                    Widgets.DrawLine(c, NodeRect(ps).center,
                        lit ? new Color(0.5f, 0.8f, 1f, 0.85f) : new Color(0.42f, 0.46f, 0.56f, 0.45f), lit ? 2f : 1f);
                }
                foreach (var pre in sp.anyPrereq)
                {
                    var ps = Specs.Get(pre); if (ps == null) continue;
                    Widgets.DrawLine(c, NodeRect(ps).center,
                        Has(d, pre) ? new Color(0.9f, 0.8f, 0.3f, 0.8f) : new Color(0.4f, 0.4f, 0.5f, 0.35f), 1f);
                }
            }

            foreach (var sp in Specs.All) DrawNode(d, sp);

            Rect viewCtrl = new Rect(8f, 8f, 186f, 60f);
            DrawViewControls(viewCtrl);
            HandlePanZoom(d, local, viewCtrl);

            if (editMode && dragging != null)
            {
                Event e = Event.current;
                Vector2 mp = e.mousePosition;
                dragging.col = Mathf.Round(((mp.x - _node / 2f - _origin.x) / _cw - 0.5f) * 4f) / 4f;
                dragging.row = Mathf.Round(((mp.y - _node / 2f - _origin.y) / _ch) * 4f) / 4f;
                if (e.rawType == EventType.MouseUp) dragging = null;
            }

            GUI.EndClip();
        }

        private void HandlePanZoom(SpecData d, Rect local, Rect exclude)
        {
            Event e = Event.current;
            if (e.type == EventType.ScrollWheel && local.Contains(e.mousePosition)) { ZoomBy(-e.delta.y * 0.06f); e.Use(); return; }
            if (editMode) return;
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (exclude.Contains(e.mousePosition)) return;
                bool onNode = Specs.All.Any(sp => NodeRect(sp).Contains(e.mousePosition));
                if (!onNode && local.Contains(e.mousePosition)) { panning = true; e.Use(); }
            }
            else if (panning && e.type == EventType.MouseDrag) { panOffset += e.delta; panTarget = panOffset; e.Use(); }
            if (e.rawType == EventType.MouseUp) panning = false;
        }

        private void ZoomBy(float dz)
        {
            float o = zoom;
            zoom = Mathf.Clamp(zoom + dz, 0.6f, 2.6f);
            if (!Mathf.Approximately(zoom, o)) panOffset *= zoom / o;
            panTarget = panOffset;
        }

        // Nudge the constellation slightly toward a node's tree when it's selected.
        private void PanToward(Spec sp)
        {
            if (sp == null || _treeSize.x <= 0f) return;
            Vector2 viewCenter = _treeSize * 0.5f;
            panTarget = panOffset + (viewCenter - RawCenter(sp)) * 0.35f;
        }

        // Smoothly center the view FULLY on a node (used when finishing a spoke opens the apex).
        private void PanTo(Spec sp)
        {
            if (sp == null || _treeSize.x <= 0f) return;
            panTarget = panOffset + (_treeSize * 0.5f - RawCenter(sp));
        }

        private void DrawNode(SpecData d, Spec sp)
        {
            Rect r = NodeRect(sp);
            bool owned = d.Owns(sp.id);
            bool staged = pending.Contains(sp.id);
            bool met = PrereqMet(d, sp, true);
            bool afford = d.points - PendingCost() >= sp.cost;
            bool buyable = !owned && !staged && met && afford;
            Color bc = Branch(sp.branch);

            if (NodeGlowTex != null && (owned || staged || buyable))
            {
                float pulse = 0.55f + 0.4f * Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 2f));
                GUI.color = owned ? new Color(bc.r, bc.g, bc.b, pulse)
                          : staged ? new Color(Palette.Accent.r, Palette.Accent.g, Palette.Accent.b, pulse)
                                   : new Color(Palette.Accent.r, Palette.Accent.g, Palette.Accent.b, pulse * 0.6f);
                GUI.DrawTexture(r.ExpandedBy(5f), NodeGlowTex);
                GUI.color = Color.white;
            }

            if (NodeRingTex != null) GUI.DrawTexture(r, NodeRingTex);
            else Widgets.DrawBoxSolid(r, new Color(bc.r * 0.2f, bc.g * 0.2f, bc.b * 0.2f, 0.96f));

            // Capstone frame: a layered gold border marks culminating nodes apart from normal skills.
            if (Specs.IsCapstone(sp.id))
            {
                GUI.color = new Color(Palette.Gold.r, Palette.Gold.g, Palette.Gold.b, owned ? 1f : 0.62f);
                Widgets.DrawBox(r.ExpandedBy(3f), 2);
                GUI.color = new Color(Palette.Gold.r, Palette.Gold.g, Palette.Gold.b, owned ? 0.55f : 0.30f);
                Widgets.DrawBox(r.ExpandedBy(6f), 1);
                GUI.color = Color.white;
            }

            if (sp.icon != null)
            {
                GUI.color = owned || staged || met ? Color.white : new Color(0.5f, 0.5f, 0.53f);
                GUI.DrawTexture(r.ContractedBy(_node * 0.22f), sp.icon);
                GUI.color = Color.white;
            }
            else
            {
                Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = bc;
                Widgets.Label(r, sp.label.Substring(0, 1));
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            }

            if (!owned && !staged && !met)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.5f);
                GUI.DrawTexture(r, NodeRingTex ?? BaseContent.WhiteTex);
                GUI.color = Color.white;
            }
            // Staged: bright accent ring.
            if (staged)
            {
                GUI.color = Palette.Accent;
                Widgets.DrawBox(r.ExpandedBy(1f), 2);
                GUI.color = Color.white;
            }

            bool over = Mouse.IsOver(r);
            if (over) { hoverSpec = sp; if (!owned) { GUI.color = new Color(1f, 1f, 1f, 0.12f); GUI.DrawTexture(r, NodeRingTex ?? BaseContent.WhiteTex); GUI.color = Color.white; } }

            // Cost / status chip (top-right).
            Rect chip = new Rect(r.xMax - 20f, r.y - 2f, 22f, 16f);
            Widgets.DrawBoxSolid(chip, owned ? Palette.Good : staged ? Palette.Accent : Palette.BGD);
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = owned ? Color.black : staged ? Color.black : (afford ? Palette.Gold : Palette.TextDim);
            Widgets.Label(chip, owned ? "\u2713" : staged ? "+" : sp.cost.ToString());
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;

            // Label backing + bright text (no wrap, full height).
            Text.Font = GameFont.Tiny;
            bool prevWrap = Text.WordWrap; Text.WordWrap = false;
            Vector2 lsz = Text.CalcSize(sp.label);
            float lw = lsz.x + 12f, lh = lsz.y + 2f;
            Rect lr = new Rect(r.center.x - lw / 2f, r.yMax + 3f, lw, lh);
            GUI.color = new Color(0f, 0f, 0f, 0.62f); GUI.DrawTexture(lr, BaseContent.WhiteTex);
            Text.Anchor = TextAnchor.MiddleCenter; GUI.color = owned ? new Color(1f, 0.95f, 0.72f) : staged ? Palette.Accent : new Color(0.87f, 0.89f, 0.93f);
            Widgets.Label(lr, sp.label);
            if (sp.pick && (owned || staged))
            {
                string pl = PickLabel(d, sp);
                Vector2 psz = Text.CalcSize(pl);
                float pw = psz.x + 12f;
                Rect pr = new Rect(r.center.x - pw / 2f, lr.yMax + 1f, pw, psz.y + 2f);
                GUI.color = new Color(0f, 0f, 0f, 0.62f); GUI.DrawTexture(pr, BaseContent.WhiteTex);
                Text.Anchor = TextAnchor.MiddleCenter; GUI.color = Palette.Accent;
                Widgets.Label(pr, pl);
            }
            Text.WordWrap = prevWrap;
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;

            if (editMode && Prefs.DevMode)
            {
                if (over && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                { dragging = sp; Event.current.Use(); }
            }
            else if (Widgets.ButtonInvisible(r)) OnClick(d, sp, owned, staged, met, afford);
        }

        // Drawn clip-local inside the tree box; its rect is excluded from pan so the buttons work.
        private void DrawViewControls(Rect box)
        {
            Widgets.DrawBoxSolid(box, new Color(0f, 0f, 0f, 0.5f));
            float x = box.x + 5f, y = box.y + 6f;
            if (IconButton(new Rect(x, y, 26f, 24f), ZoomInTex, null, true)) ZoomBy(0.15f);
            if (IconButton(new Rect(x + 30f, y, 26f, 24f), ZoomOutTex, null, true)) ZoomBy(-0.15f);
            if (IconButton(new Rect(x + 60f, y, 26f, 24f), ViewResetTex, null, true)) { zoom = 1.45f; panOffset = Vector2.zero; panTarget = Vector2.zero; }
            Text.Font = GameFont.Tiny; GUI.color = Palette.TextDim;
            bool pw = Text.WordWrap; Text.WordWrap = false;
            Widgets.Label(new Rect(x, y + 28f, box.width, Text.LineHeight), "Drag to pan \u2022 scroll to zoom");
            Text.WordWrap = pw;
            GUI.color = Color.white; Text.Font = GameFont.Small;
        }

        // Box button with an optional icon (icon-only when label is null) + enabled dimming.
        private bool IconButton(Rect r, Texture2D icon, string label, bool enabled)
        {
            bool over = Mouse.IsOver(r);
            Widgets.DrawBoxSolid(r, over && enabled ? Palette.BGL : Palette.PanelBG);
            GUI.color = new Color(1f, 1f, 1f, 0.22f); Widgets.DrawBox(r, 1); GUI.color = Color.white;
            if (icon != null)
            {
                float ih = Mathf.Min(r.height - 6f, 20f);
                float ix = string.IsNullOrEmpty(label) ? r.center.x - ih / 2f : r.x + 5f;
                GUI.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.4f);
                GUI.DrawTexture(new Rect(ix, r.center.y - ih / 2f, ih, ih), icon);
                GUI.color = Color.white;
            }
            if (!string.IsNullOrEmpty(label))
            {
                float lx = icon != null ? 22f : 0f;
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = enabled ? Palette.Stat : Palette.TextDim;
                Widgets.Label(new Rect(r.x + lx, r.y, r.width - lx, r.height), label);
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white; Text.Font = GameFont.Small;
            }
            return enabled && Widgets.ButtonInvisible(r);
        }

        private string PickLabel(SpecData d, Spec sp)
        {
            switch (sp.id)
            {
                case "mastery": return (pendMastery ?? d.masteryDef)?.LabelCap.ToString() ?? "-";
                case "discipline": return (pendDiscipline ?? d.disciplinePath)?.LabelCap.ToString() ?? "-";
                case "attunement": return (pendAttune ?? d.attuneDamage)?.LabelCap.ToString() ?? "-";
                default: return "";
            }
        }

        // ---- Right-side preview / confirm panel -------------------------------------------------

        private void DrawPanel(SpecData d, Rect rect)
        {
            Widgets.DrawBoxSolid(rect, Palette.BGD);
            Rect inner = rect.ContractedBy(10f);

            if (hoverSpec != null) { DrawNodeDetail(d, inner, hoverSpec); return; }

            float y = inner.y;
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Palette.Stat;
            float lhS = Text.LineHeight;
            Widgets.Label(new Rect(inner.x, y, inner.width, lhS), "Bonuses & Perks"); y += lhS + 3f;

            // Points block.
            int pend = PendingCost();
            Text.Font = GameFont.Tiny; float lh = Text.LineHeight; GUI.color = Palette.TextDim;
            Widgets.Label(new Rect(inner.x, y, inner.width, lh), "Available points: " + d.points); y += lh;
            if (pend > 0)
            {
                GUI.color = Palette.Accent;
                Widgets.Label(new Rect(inner.x, y, inner.width, lh),
                    pending.Count + (pending.Count == 1 ? " perk, " : " perks, ") + pend + " pts  \u2192  " + (d.points - pend) + " left"); y += lh;
            }
            GUI.color = new Color(1f, 1f, 1f, 0.12f); Widgets.DrawLineHorizontal(inner.x, y + 3f, inner.width); GUI.color = Color.white; y += 9f;

            // Aggregate bonuses.
            float synF = 1f;
            if (Has(d, "confluence")) synF *= 2f;
            if (Has(d, "conduit")) synF *= 1.5f;
            if (Has(d, "convergence")) synF *= 1.15f;
            int capB = Has(d, "convergence") ? 5 : 0;
            int glob = (Has(d, "overflow") ? 8 : 0) + (Has(d, "convergence") ? 15 : 0);
            Text.Font = GameFont.Tiny; GUI.color = Palette.Good;
            if (glob > 0) { Widgets.Label(new Rect(inner.x, y, inner.width, lh), "All stats:  +" + glob + "%"); y += lh; }
            if (synF > 1.001f) { Widgets.Label(new Rect(inner.x, y, inner.width, lh), "Synergy received:  \u00d7" + synF.ToString("0.00")); y += lh; }
            if (capB > 0) { Widgets.Label(new Rect(inner.x, y, inner.width, lh), "Skill level cap:  +" + capB); y += lh; }
            GUI.color = Color.white;
            y += 4f;

            // Owned + staged perk list (scrollable).
            var rows = new List<Spec>();
            foreach (var sp in Specs.All) if (d.Owns(sp.id) || pending.Contains(sp.id)) rows.Add(sp);
            Text.Font = GameFont.Tiny; GUI.color = Palette.TextDim;
            Widgets.Label(new Rect(inner.x, y, inner.width, lh), rows.Count == 0 ? "No specializations yet." : "Perks (" + rows.Count + "):"); y += lh + 2f;
            GUI.color = Color.white;

            float devH = Prefs.DevMode ? 32f : 0f;
            Rect btnRow = new Rect(inner.x, inner.yMax - 30f - devH, inner.width, 28f);
            Rect respecRow = new Rect(inner.x, btnRow.y - 30f, inner.width, 26f);
            float listBottom = respecRow.y - 6f;
            Rect listOut = new Rect(inner.x, y, inner.width, Mathf.Max(40f, listBottom - y));

            // Variable row heights so wrapped descriptions are never clipped.
            Text.Font = GameFont.Tiny;
            float lhR = Text.LineHeight;
            float textW = listOut.width - 16f - 12f;
            var heights = new List<float>(rows.Count);
            float total = 2f;
            foreach (var sp in rows) { float rh = lhR + 8f + Mathf.Max(lhR, Text.CalcHeight(sp.desc, textW)) + 6f; heights.Add(rh); total += rh; }
            Rect listView = new Rect(0f, 0f, listOut.width - 16f, total);
            Widgets.BeginScrollView(listOut, ref perkScroll, listView);
            float ry = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                var sp = rows[i]; float rh = heights[i];
                bool isPending = pending.Contains(sp.id);
                Rect rr = new Rect(0f, ry, listView.width, rh - 4f);
                Widgets.DrawBoxSolid(rr, isPending ? new Color(Palette.Accent.r, Palette.Accent.g, Palette.Accent.b, 0.14f) : new Color(1f, 1f, 1f, 0.04f));
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = isPending ? Palette.Accent : Palette.Good;
                string nm = (isPending ? "\u25C6 " : "\u2713 ") + sp.label + (sp.pick ? ": " + PickLabel(d, sp) : "");
                Widgets.Label(new Rect(rr.x + 6f, rr.y + 3f, rr.width - 10f, lhR), nm);
                GUI.color = Palette.TextDim;
                Widgets.Label(new Rect(rr.x + 6f, rr.y + 4f + lhR, rr.width - 14f, rh - lhR - 12f), sp.desc);
                ry += rh;
            }
            Widgets.EndScrollView();
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;

            // Player respec (free): refund every owned spec back to the point pool.
            bool hasOwned = d.owned.Count > 0;
            if (IconButton(respecRow, RespecTex, hasOwned ? "Respec all - refund " + d.owned.Sum(id => Specs.Get(id)?.cost ?? 0) + " ★" : "Respec all", hasOwned))
                OpenRespecConfirm(d);

            // Apply / Clear (icon buttons).
            float half = (btnRow.width - 6f) / 2f;
            bool any = pending.Count > 0;
            if (IconButton(new Rect(btnRow.x, btnRow.y, half, btnRow.height), ApplyTex, any ? "Apply " + pending.Count + " (" + pend + " pts)" : "Apply", any)) Apply(d);
            if (IconButton(new Rect(btnRow.x + half + 6f, btnRow.y, half, btnRow.height), ClearTex, "Clear", any)) ClearPending();

            // Dev tools (icon row), relocated here from the tree footer so clicks aren't eaten by pan.
            if (Prefs.DevMode)
            {
                Rect devRow = new Rect(inner.x, inner.yMax - 26f, inner.width, 24f);
                float bw = 26f, gap = 6f, dx = devRow.x;
                if (IconButton(new Rect(dx, devRow.y, bw, 24f), DevPointsTex, null, true)) d.points += 20; dx += bw + gap;
                if (IconButton(new Rect(dx, devRow.y, bw, 24f), DevResetTex, null, true))
                {
                    d.points += d.owned.Sum(id => Specs.Get(id)?.cost ?? 0);
                    d.owned.Clear(); d.masteryDef = null; d.disciplinePath = null; d.attuneDamage = null;
                    pending.Clear(); pendMastery = null; pendDiscipline = null; pendAttune = null;
                } dx += bw + gap;
                Rect er = new Rect(dx, devRow.y, bw, 24f);
                if (IconButton(er, DevEditTex, null, true)) editMode = !editMode;
                if (editMode) { GUI.color = Palette.Accent; Widgets.DrawBox(er, 2); GUI.color = Color.white; }
                dx += bw + gap;
                if (IconButton(new Rect(dx, devRow.y, bw, 24f), DevLogTex, null, true))
                    foreach (var sp in Specs.All) Log.Message($"SPEC {sp.id} {sp.col}f {sp.row}f");
                Text.Font = GameFont.Tiny; GUI.color = Palette.TextDim; Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(dx + bw + 4f, devRow.y, devRow.xMax - dx - bw - 4f, 24f), "dev");
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white; Text.Font = GameFont.Small;
            }
        }

        private void DrawNodeDetail(SpecData d, Rect inner, Spec sp)
        {
            bool owned = d.Owns(sp.id);
            bool staged = pending.Contains(sp.id);
            bool met = PrereqMet(d, sp, true);
            bool afford = d.points - PendingCost() >= sp.cost;
            float y = inner.y;

            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Branch(sp.branch);
            float lhS = Text.LineHeight;
            Widgets.Label(new Rect(inner.x, y, inner.width, lhS), sp.label); y += lhS + 2f;
            Text.Font = GameFont.Tiny; float lh = Text.LineHeight; GUI.color = Palette.Gold;
            Widgets.Label(new Rect(inner.x, y, inner.width, lh), sp.branch + "  \u2022  " + sp.cost + " pts"); y += lh + 2f;
            GUI.color = new Color(1f, 1f, 1f, 0.12f); Widgets.DrawLineHorizontal(inner.x, y + 2f, inner.width); GUI.color = Color.white; y += 8f;

            GUI.color = Palette.Stat; Text.Font = GameFont.Tiny;
            float dh = Text.CalcHeight(sp.desc, inner.width);
            Widgets.Label(new Rect(inner.x, y, inner.width, dh), sp.desc); y += dh + 8f;

            if (sp.prereqs.Length > 0)
            {
                GUI.color = Palette.TextDim;
                string req = "Requires: " + string.Join(", ", sp.prereqs.Select(p => Specs.Get(p)?.label ?? p));
                float reqH = Text.CalcHeight(req, inner.width);
                Widgets.Label(new Rect(inner.x, y, inner.width, reqH), req); y += reqH + 2f;
            }
            if (sp.anyPrereq.Length > 0)
            {
                GUI.color = Palette.TextDim;
                string reqA = "Requires any: " + string.Join(", ", sp.anyPrereq.Select(p => Specs.Get(p)?.label ?? p));
                float reqAH = Text.CalcHeight(reqA, inner.width);
                Widgets.Label(new Rect(inner.x, y, inner.width, reqAH), reqA); y += reqAH + 2f;
            }

            string status; Color sc;
            if (owned) { status = "\u2713 Owned"; sc = Palette.Good; }
            else if (staged) { status = "\u25C6 Staged - click node to unstage"; sc = Palette.Accent; }
            else if (GroupLocked(d, sp)) { status = sp.exclusiveGroup == "apotheosis" ? "Locked - you already chose another Apotheosis path" : "Locked - you already picked one of this exclusive set"; sc = Palette.Bad; }
            else if (!met) { status = "Locked - prerequisites not met"; sc = Palette.Bad; }
            else if (!afford) { status = "Need more points"; sc = Palette.Bad; }
            else { status = "Click node to stage"; sc = Palette.TextDim; }
            y += 4f; GUI.color = sc; Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inner.x, y, inner.width, Text.LineHeight), status);
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
        }

        // ---- Staging / commit -------------------------------------------------------------------

        private void OnClick(SpecData d, Spec sp, bool owned, bool staged, bool met, bool afford)
        {
            // Clicking a branch capstone flies the camera to the apex (Convergence) it feeds.
            if (Specs.Capstones.Contains(sp.id)) PanTo(Specs.Get("convergence"));
            else PanToward(sp);
            if (owned) return;
            if (staged) { Unstage(sp); return; }
            if (GroupLocked(d, sp))
            {
                Messages.Message(sp.exclusiveGroup == "apotheosis"
                    ? "You can pursue only one Apotheosis path (Tranquil Mind, Transcendence or Umbral Sovereign)."
                    : "You can pick only one of Overflow, Mastery, Discipline or Attunement.",
                    MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (!met) { Messages.Message("Prerequisites not met.", MessageTypeDefOf.RejectInput, false); return; }
            if (!afford) { Messages.Message("Not enough specialization points.", MessageTypeDefOf.RejectInput, false); return; }
            if (sp.pick) OpenPicker(d, sp);
            else Stage(sp);
        }

        private void Stage(Spec sp)
        {
            pending.Add(sp.id);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        private void Unstage(Spec sp)
        {
            if (!pending.Remove(sp.id)) return;
            if (sp.id == "mastery") pendMastery = null;
            if (sp.id == "discipline") pendDiscipline = null;
            if (sp.id == "attunement") pendAttune = null;
            // Cascade: drop staged dependents that now lack prerequisites.
            var d = Data;
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var id in pending.ToList())
                {
                    var s2 = Specs.Get(id);
                    if (s2 == null || PrereqMet(d, s2, true)) continue;
                    pending.Remove(id);
                    if (id == "mastery") pendMastery = null;
                    if (id == "discipline") pendDiscipline = null;
                    if (id == "attunement") pendAttune = null;
                    changed = true;
                }
            }
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void ClearPending()
        {
            pending.Clear(); pendMastery = null; pendDiscipline = null; pendAttune = null;
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        private void OpenRespecConfirm(SpecData d)
        {
            Find.WindowStack.Add(new Dialog_Confirm("Respec specializations",
                "Refund all specializations? Every spent point returns to your pool, all picks are cleared, and any Apotheosis path is undone. (Free.)",
                () => RespecAll(d)));
        }

        private void RespecAll(SpecData d)
        {
            d.points += d.owned.Sum(id => Specs.Get(id)?.cost ?? 0);
            d.owned.Clear(); d.masteryDef = null; d.disciplinePath = null; d.attuneDamage = null;
            pending.Clear(); pendMastery = null; pendDiscipline = null; pendAttune = null;
            if (pawn != null) AscensionSystem.Sync(pawn, d);   // drop ascension hediffs immediately
            SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
        }

        private void Apply(SpecData d)
        {
            if (pending.Count == 0) return;
            // Did this commit finish a spoke? (any of the four branch capstones, which open Convergence)
            bool capstoneDone = false;
            foreach (var id in pending) if (Specs.Capstones.Contains(id)) { capstoneDone = true; break; }
            d.points -= PendingCost();
            d.owned.UnionWith(pending);
            if (pendMastery != null) d.masteryDef = pendMastery;
            if (pendDiscipline != null) d.disciplinePath = pendDiscipline;
            if (pendAttune != null) d.attuneDamage = pendAttune;
            pending.Clear(); pendMastery = null; pendDiscipline = null; pendAttune = null;
            SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();   // satisfying confirm chime
            // Finishing a spoke opens Convergence (the synergized apex) - pan the camera to it.
            if (capstoneDone) PanTo(Specs.Get("convergence"));
        }

        private void OpenPicker(SpecData d, Spec sp)
        {
            var opts = new List<FloatMenuOption>();
            if (sp.id == "mastery")
            {
                var comp = pawn.GetComp<CompAbilities>();
                if (comp?.LearnedAbilities != null)
                    foreach (var ab in comp.LearnedAbilities.Where(a => a.def.GetModExtension<AbilityExtension_Psycast>() != null)
                                 .OrderBy(a => a.def.label))
                    {
                        var def = ab.def;
                        opts.Add(new FloatMenuOption(def.LabelCap, () => { pendMastery = def; Stage(sp); }));
                    }
            }
            else if (sp.id == "discipline")
            {
                var hediff = pawn.Psycasts();
                if (hediff?.unlockedPaths != null)
                    foreach (var path in hediff.unlockedPaths.OrderBy(p => p.label))
                    {
                        var pp = path;
                        opts.Add(new FloatMenuOption(pp.LabelCap, () => { pendDiscipline = pp; Stage(sp); }));
                    }
            }
            else if (sp.id == "attunement")
            {
                var comp = pawn.GetComp<CompAbilities>();
                var dmgs = new HashSet<DamageDef>();
                if (comp?.LearnedAbilities != null)
                    foreach (var ab in comp.LearnedAbilities)
                    {
                        var dd = SpecEffects.ElementOf(ab.def);
                        if (dd != null) dmgs.Add(dd);
                    }
                foreach (var dd in dmgs.OrderBy(x => x.label))
                {
                    var dmg = dd;
                    opts.Add(new FloatMenuOption(dmg.LabelCap, () => { pendAttune = dmg; Stage(sp); }));
                }
            }

            if (opts.Count == 0)
            {
                Messages.Message("No valid choices available yet (learn more psycasts first).", MessageTypeDefOf.RejectInput, false);
                return;
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }
    }

    // "Specializations" button drawn into the Psycasts tab's left panel.
    [HarmonyPatch(typeof(ITab_Pawn_Psycasts), "FillTab")]
    public static class Patch_SpecButtonInTab
    {
        static void Postfix(ITab_Pawn_Psycasts __instance)
        {
            try
            {
                var pawn = Find.Selector.SingleSelectedThing as Pawn;
                var hediff = pawn?.Psycasts();
                if (hediff == null) return;
                Vector2 size = __instance.Size;
                var d = GameComponent_PsycastSynergies.Instance?.GetSpec(pawn, create: true);

                float leftW = Mathf.Min(size.x * 0.3f, 320f);
                Rect r = new Rect(leftW - 98f, 18f, 92f, 26f);

                Palette.DrawCard(r);
                if ((d?.points ?? 0) > 0)
                {
                    float pulse = 0.45f + 0.4f * Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 2.2f));
                    GUI.color = new Color(1f, 0.85f, 0.2f, pulse);
                    Widgets.DrawBox(r.ExpandedBy(2f), 2);
                    GUI.color = Color.white;
                }
                if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = Palette.Gold;
                Widgets.Label(r, "\u2605 " + (d?.points ?? 0));
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                TooltipHandler.TipRegion(r, "Specializations - " + (d?.points ?? 0) + " points");
                if (Widgets.ButtonInvisible(r) && !Find.WindowStack.IsOpen(typeof(Window_Specializations)))
                    Find.WindowStack.Add(new Window_Specializations(pawn));

                // Enlightenment-tier badge: a code-drawn serif Roman numeral in tooltip-blue, just left of the
                // spec-point star. Drawn from rects/lines so it reads as a numeral at any font and any tier.
                int tier = EnlightenmentTier.TierOf(pawn);
                if (tier > 0)
                {
                    float bw = Mathf.Max(30f, RomanNumerals.ToRoman(tier).Length * 9f + 12f);   // grow for longer numerals (IV, XII...)
                    Rect tr = new Rect(r.x - 4f - bw, r.y, bw, 26f);
                    Palette.DrawCard(tr);
                    if (Mouse.IsOver(tr)) Widgets.DrawHighlight(tr);
                    DrawRoman(tr.ContractedBy(5f), tier, Palette.Accent);
                    TooltipHandler.TipRegion(tr, "Enlightenment: " + EnlightenmentTier.Name(tier) + " (Tier " + tier + ")");
                }
            }
            catch { }
        }

        // Draw a serif Roman numeral (any value) from rects (I) + lines (V, X), centered in `area`, in `col`.
        // Font-independent so it always renders as an actual numeral - and now continues past III (IV, V, ... ).
        private static void DrawRoman(Rect area, int n, Color col)
        {
            if (n < 1) return;
            string s = RomanNumerals.ToRoman(n);   // "IV", "XII", ...
            int cnt = s.Length;
            float cw = area.width / cnt;
            float bh = area.height * 0.82f;
            float top = area.y + (area.height - bh) / 2f;
            float th = Mathf.Max(2f, area.height * 0.13f);   // stroke thickness
            Color old = GUI.color; GUI.color = col;
            for (int i = 0; i < cnt; i++)
            {
                float gw = Mathf.Min(cw * 0.86f, bh * 0.8f);          // glyph box width
                float gx = area.x + i * cw + (cw - gw) / 2f;
                float cx = gx + gw / 2f;
                char ch = s[i];
                if (ch == 'I')
                {
                    float ibw = Mathf.Max(2f, gw * 0.34f), isw = gw * 0.92f;
                    GUI.color = col;
                    GUI.DrawTexture(new Rect(cx - ibw / 2f, top, ibw, bh), BaseContent.WhiteTex);          // stroke
                    GUI.DrawTexture(new Rect(cx - isw / 2f, top, isw, th), BaseContent.WhiteTex);          // top serif
                    GUI.DrawTexture(new Rect(cx - isw / 2f, top + bh - th, isw, th), BaseContent.WhiteTex);// bottom serif
                }
                else if (ch == 'V')
                {
                    Widgets.DrawLine(new Vector2(gx, top), new Vector2(cx, top + bh), col, th);
                    Widgets.DrawLine(new Vector2(gx + gw, top), new Vector2(cx, top + bh), col, th);
                }
                else if (ch == 'X')
                {
                    Widgets.DrawLine(new Vector2(gx, top), new Vector2(gx + gw, top + bh), col, th);
                    Widgets.DrawLine(new Vector2(gx + gw, top), new Vector2(gx, top + bh), col, th);
                }
            }
            GUI.color = old;
        }

        private static string TierWord(int t) => EnlightenmentTier.Name(UnityEngine.Mathf.Clamp(t, 0, 3));   // respects TieringOverrideDef reskins
    }
}

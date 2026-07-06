#nullable disable
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using VanillaPsycastsExpanded;

namespace PsycastSynergies
{
    // The "awakening" pick. Face-down tarot cards; the player turns ONE (their choice) and it flips with a
    // reveal burst. By DEFAULT only that card is revealed, and its path is the one they commit to. With the
    // "reveal all cards" setting ON, the remaining cards then auto-flip too and the player may center + click
    // any of them to re-pick a different path before embracing. Card fronts are drawn in the Modern Psycasts
    // UI tile style (disk-loaded path art via PathArt, near-black label bar, tile border, gold glow on the
    // pick) so they match the psycast tab. Embracing unlocks the path for free.
    [StaticConstructorOnStartup]
    public class Window_Awakening : Window
    {
        private const float FlipDur = 0.44f;
        private const float BurstDur = 0.85f;
        private const float OtherStagger = 0.22f;
        private const float MaxCardW = 230f;
        private const float LabelBarH = 28f;

        private static readonly Color BarColor = new Color(0.043f, 0.05f, 0.06f, 0.97f);
        private static readonly Color Gold = new Color(0.96f, 0.81f, 0.36f);

        private readonly Pawn pawn;
        private readonly List<PsycasterPathDef> options;
        private readonly int tier;

        private int chosen = -1;
        private readonly float[] flipT;
        private readonly bool[] flipping;
        private readonly float[] burstStart;
        private readonly float[] otherStart;
        private bool othersScheduled;
        private float lastTime;
        private float carCenter = -1f;   // animated carousel focus (index space)
        private int carTarget;           // carousel focus target index

        private static Texture2D backTex, sparkTex, glowTex;
        private static Texture2D Back => backTex != null ? backTex : (backTex = ContentFinder<Texture2D>.Get("UI/CardBack", false));
        private static Texture2D Spark => sparkTex != null ? sparkTex : (sparkTex = ContentFinder<Texture2D>.Get("UI/Sparkle", false));
        private static Texture2D Glow => glowTex != null ? glowTex : (glowTex = ContentFinder<Texture2D>.Get("UI/Glow", false));
        private static Texture2D acceptTex;
        private static Texture2D Accept => acceptTex != null ? acceptTex : (acceptTex = ContentFinder<Texture2D>.Get("UI/Accept", false));

        // Vanilla psychic / skip sounds: shimmer on open, skip-whoosh per flip, magical pulse per reveal.
        private static SoundDef sndOpen, sndFlip, sndPulse;
        private static bool sndInit;
        // When on, flipping one card reveals all of them and lets the player re-pick any; default commits the pick.
        private static bool RevealAll => PsycastSynergiesMod.Settings != null && PsycastSynergiesMod.Settings.cardRevealAll;
        private static void EnsureSounds()
        {
            if (sndInit) return;
            sndInit = true;
            sndOpen = DefDatabase<SoundDef>.GetNamedSilentFail("PsycastPsychicEffect");
            sndFlip = DefDatabase<SoundDef>.GetNamedSilentFail("Psycast_Skip_Entry");
            sndPulse = DefDatabase<SoundDef>.GetNamedSilentFail("Psycast_Skip_Pulse");
        }

        public override Vector2 InitialSize => new Vector2(920f, 620f);

        public Window_Awakening(Pawn pawn, List<PsycasterPathDef> options, int tier = 1)
        {
            this.pawn = pawn;
            this.options = options;
            this.tier = tier;
            int n = options.Count;
            flipT = new float[n];
            flipping = new bool[n];
            burstStart = new float[n];
            otherStart = new float[n];
            carTarget = n / 2;
            forcePause = true;
            closeOnClickedOutside = false;
            doCloseX = false;
            absorbInputAroundWindow = true;
            doWindowBackground = false;   // Modern-suite: draw our own backdrop, no vanilla border
            drawShadow = false;           // and no vanilla drop shadow (clashes with the flat backdrop)
            EnsureSounds();
        }

        public override void PostOpen()
        {
            base.PostOpen();
            PlayAt(sndOpen);   // magical-tech shimmer as the cards manifest
        }

        public override void PostClose()
        {
            base.PostClose();
            MeditationSystem.ShowNextPick();   // chain to the next queued awakening, if another pawn awoke at the same time
        }

        // These psychic SoundDefs are context=MapOnly (no on-camera subSounds), so they must be played
        // at the awakening pawn's map position rather than on camera.
        private void PlayAt(SoundDef snd)
        {
            if (snd == null || pawn == null || !pawn.Spawned || pawn.Map == null) return;
            snd.PlayOneShot(SoundInfo.InMap(new TargetInfo(pawn.Position, pawn.Map)));
        }

        // Cards are sized to the BACK image's aspect so the tarot back shows undistorted; fronts are
        // ScaleAndCrop'd into the art area (path textures are near the same aspect, so crop is minimal).
        private static float BackAspect()
        {
            var t = Back;
            if (t == null || t.width == 0) return 1.595f;
            return (float)t.height / t.width;
        }

        public override void DoWindowContents(Rect inRect)
        {
            float now = Time.realtimeSinceStartup;
            float dt = lastTime > 0f ? Mathf.Min(now - lastTime, 0.05f) : 0f;
            lastTime = now;
            Advance(now, dt);

            // Modern-suite backdrop (vanilla window background/border disabled).
            float mg = Margin;
            var full = new Rect(-mg, -mg, inRect.width + mg * 2f, inRect.height + mg * 2f);
            MXStyle.Fill(full, MXStyle.Backdrop);
            MXStyle.Border(full);

            var prevFont = Text.Font;
            var prevAnchor = Text.Anchor;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperCenter;
            GUI.color = MXStyle.Fg;
            Widgets.Label(new Rect(0f, 4f, inRect.width, 38f), TitleText());
            Text.Font = GameFont.Small;
            GUI.color = MXStyle.TextDim;
            Widgets.Label(new Rect(0f, 44f, inRect.width, 24f),
                chosen < 0 ? PromptText()
                    : (RevealAll ? "Scroll a card to the center to choose its path, then embrace it."
                                 : "The cards are cast. This path is now theirs."));
            GUI.color = Color.white;
            Text.Anchor = prevAnchor;

            const float headerH = 72f, footerH = 56f;
            DrawCards(inRect, headerH, footerH);

            float fy = inRect.height - footerH + 10f;
            bool canForgo = tier >= 4;   // Transcendent tiers may trade the path card for bonus specialization points
            if (chosen >= 0 && flipT[chosen] >= 0.999f)
            {
                if (canForgo)
                {
                    if (MXStyle.Button(new Rect(inRect.width / 2f - 250f, fy, 244f, 40f), "Embrace the " + options[chosen].label.CapitalizeFirst() + " path", Accept))
                        Confirm();
                    if (MXStyle.Button(new Rect(inRect.width / 2f + 6f, fy, 244f, 40f), "Forgo: +" + ForgoPoints() + " specialization points"))
                        ForgoForPoints();
                }
                else
                {
                    if (MXStyle.Button(new Rect(inRect.width / 2f - 175f, fy, 350f, 40f), "Embrace the " + options[chosen].label.CapitalizeFirst() + " path", Accept))
                        Confirm();
                }
            }
            else if (chosen < 0 && canForgo)
            {
                if (MXStyle.Button(new Rect(inRect.width / 2f - 200f, fy, 400f, 40f), "Forgo the cards: +" + ForgoPoints() + " specialization points"))
                    ForgoForPoints();
            }
            else if (chosen < 0 && tier >= 2)
            {
                // Tier II/III may defer the choice and meditate for a fresh draw - at rising coma risk.
                if (MXStyle.Button(new Rect(inRect.width / 2f - 195f, fy, 390f, 40f), "Keep meditating - reroll the cards (coma risk)"))
                    DeferReroll();
            }

            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
        }

        private void Advance(float now, float dt)
        {
            int n = options.Count;
            if (carCenter < 0f) carCenter = carTarget;
            carCenter = Mathf.Lerp(carCenter, carTarget, 1f - Mathf.Exp(-dt * 11f));
            if (Mathf.Abs(carCenter - carTarget) < 0.003f) carCenter = carTarget;
            for (int i = 0; i < n; i++)
                if (flipping[i] && flipT[i] < 1f)
                    flipT[i] = Mathf.Min(1f, flipT[i] + dt / FlipDur);

            // Fire each card's burst at its flip midpoint (the reveal moment).
            for (int i = 0; i < n; i++)
                if (flipping[i] && flipT[i] >= 0.5f && burstStart[i] == 0f)
                {
                    burstStart[i] = now;
                    PlayAt(sndPulse);   // magical pulse at the reveal
                }

            // Reveal-all mode only: once the chosen card lands, auto-flip the others (staggered) so the player
            // can see and re-pick any of them. Default mode leaves the other cards face-down (a committal pick).
            if (RevealAll && chosen >= 0 && flipT[chosen] >= 1f && !othersScheduled)
            {
                othersScheduled = true;
                int s = 0;
                for (int j = 0; j < n; j++)
                    if (j != chosen) { otherStart[j] = now + s * OtherStagger; s++; }
            }
            if (othersScheduled)
                for (int j = 0; j < n; j++)
                    if (j != chosen && !flipping[j] && now >= otherStart[j])
                    {
                        flipping[j] = true;
                        PlayAt(sndFlip);
                    }

            // Reveal-all, post-pick: the centered card IS the current pick. Selection follows the carousel,
            // so scrolling/chevron navigation updates the gold glow + Embrace button without a separate click.
            if (RevealAll && othersScheduled && chosen >= 0)
            {
                int c = ((Mathf.RoundToInt(carCenter) % n) + n) % n;
                if (c != chosen && flipT[c] >= 0.999f) chosen = c;
            }
        }

        // Centered row for <=3 cards; a spherical carousel for more (~3 visible, chevrons cycle the rest,
        // outer cards shaded toward their far edge).
        private void DrawCards(Rect inRect, float headerH, float footerH)
        {
            int n = options.Count;
            float availH = inRect.height - headerH - footerH;
            float refAspect = BackAspect();

            // All tiers use the same coverflow carousel (it cycles any card count circularly).
            const float chevW = 40f;
            float midY = headerH + availH / 2f;
            float ch = availH * 0.92f;
            float cw = ch / refAspect;
            float maxW = (inRect.width - 2f * (chevW + 14f)) / 2.5f;
            if (cw > maxW) { cw = maxW; ch = cw * refAspect; }
            float cx = inRect.width / 2f;

            var lr = new Rect(8f, midY - 30f, chevW, 60f);
            var rr = new Rect(inRect.width - 8f - chevW, midY - 30f, chevW, 60f);
            bool overChev = Mouse.IsOver(lr) || Mouse.IsOver(rr);

            var order = new List<int>();
            for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((a, b) => Mathf.Abs(CircularOffset(b)).CompareTo(Mathf.Abs(CircularOffset(a))));   // far -> near
            foreach (int i in order)
            {
                float off = CircularOffset(i), ad = Mathf.Abs(off);
                if (ad > 2.5f) continue;                                   // hidden behind the carousel
                var r = CarouselRect(i, cx, midY, cw, ch);
                if (r.width < 6f) continue;
                DrawCardVisual(r, i);
                if (ad < 0.5f && (chosen < 0 || (RevealAll && i != chosen && flipT[i] >= 0.999f))) { GUI.color = new Color(0.5f, 0.78f, 1f, 0.55f); Widgets.DrawBox(r, 2); GUI.color = Color.white; }
                float uni = Mathf.Lerp(0f, 0.42f, Mathf.Clamp01((ad - 0.45f) / 1.6f));   // depth dim
                if (uni > 0.01f) Fill(r, new Color(0f, 0f, 0f, uni));
                float dirS = Mathf.Clamp01((ad - 0.3f) / 1.4f) * 0.72f;                 // curving terminator
                if (dirS > 0.01f) DirectionalShade(r, dirS, off < 0f);
            }

            if (!overChev) HandleCarouselClicks(cx, midY, cw, ch);   // chevron clicks never double as card clicks

            if (Chevron(lr, true, true)) carTarget -= 1;    // circular: cycles past the ends
            if (Chevron(rr, false, true)) carTarget += 1;
        }

        // Nearest wrapped offset of card i from the carousel focus, so the strip cycles endlessly.
        private float CircularOffset(int i)
        {
            int n = options.Count;
            float rel = Mathf.Repeat(i - carCenter, n);
            return rel > n / 2f ? rel - n : rel;
        }

        // Coverflow cylinder placement: cards rotate away from center (horizontal foreshorten) and recede.
        private Rect CarouselRect(int i, float cx, float midY, float cardW, float cardH)
        {
            const float spread = 0.52f;
            float radius = cardW * 0.66f / Mathf.Sin(spread);
            float d = CircularOffset(i);
            float ang = Mathf.Clamp(d * spread, -1.45f, 1.45f);
            float fx = Mathf.Cos(ang);                                          // turns to a sliver at the edges
            float depth = Mathf.Lerp(1f, 0.58f, Mathf.Clamp01(Mathf.Abs(d) / 2f));
            float w = cardW * depth * fx;
            float h = cardH * depth;
            float x = cx + Mathf.Sin(ang) * radius;
            return new Rect(x - w / 2f, midY - h / 2f, w, h);
        }

        private void DrawCardVisual(Rect r, int i)
        {
            float t = flipT[i];
            bool front = t >= 0.5f;
            float sx = Mathf.Max(0.02f, Mathf.Abs(Mathf.Cos(t * Mathf.PI)));   // 1 -> 0 (edge) -> 1
            var dr = new Rect(r.center.x - r.width * sx / 2f, r.y, r.width * sx, r.height);
            if (front) DrawFront(dr, options[i], i == chosen);
            else DrawBack(dr);
            if (burstStart[i] > 0f)
            {
                float bt = (Time.realtimeSinceStartup - burstStart[i]) / BurstDur;
                if (bt < 1f) DrawBurst(r, bt);
            }
        }

        // Topmost (nearest-center) card eats the click: clicking the centered card flips it (the pick),
        // clicking a side card scrolls it to the center.
        private void HandleCarouselClicks(float cx, float midY, float cardW, float cardH)
        {
            int n = options.Count;
            var order = new List<int>();
            for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((a, b) => Mathf.Abs(CircularOffset(a)).CompareTo(Mathf.Abs(CircularOffset(b))));   // near -> far
            foreach (int i in order)
            {
                float off = CircularOffset(i);
                if (Mathf.Abs(off) > 2.5f) continue;
                var r = CarouselRect(i, cx, midY, cardW, cardH);
                if (r.width < 6f || !Mouse.IsOver(r)) continue;
                if (Widgets.ButtonInvisible(r))
                {
                    if (chosen < 0)
                    {
                        // First pick: only the centered card can be turned; side cards just scroll over.
                        if (Mathf.Abs(off) < 0.5f) { chosen = i; flipping[i] = true; carTarget = Mathf.RoundToInt(carCenter); PlayAt(sndFlip); }
                        else carTarget += (off > 0f ? 1 : -1);
                    }
                    else if (RevealAll && flipT[i] >= 0.999f)
                    {
                        // Reveal-all: clicking ANY revealed card picks it AND brings it to center, so the
                        // Embrace button always tracks the card the player last clicked.
                        if (i != chosen) { chosen = i; PlayAt(sndPulse); }
                        carTarget = Mathf.RoundToInt(carCenter + off);
                    }
                    else carTarget += (off > 0f ? 1 : -1);   // default mode after a pick: just scroll
                }
                break;   // cards occluded below don't receive the click
            }
        }

        private static bool Chevron(Rect r, bool left, bool enabled)
        {
            bool over = enabled && Mouse.IsOver(r);
            if (over) Fill(r, new Color(0.45f, 0.75f, 1f, 0.16f));
            Color c = !enabled ? new Color(0.5f, 0.5f, 0.55f, 0.4f) : (over ? Color.white : new Color(0.82f, 0.82f, 0.9f));
            Vector2 m = r.center; const float w = 11f, h = 18f;
            if (left)
            {
                Widgets.DrawLine(new Vector2(m.x + w / 2f, m.y - h / 2f), new Vector2(m.x - w / 2f, m.y), c, 3f);
                Widgets.DrawLine(new Vector2(m.x - w / 2f, m.y), new Vector2(m.x + w / 2f, m.y + h / 2f), c, 3f);
            }
            else
            {
                Widgets.DrawLine(new Vector2(m.x - w / 2f, m.y - h / 2f), new Vector2(m.x + w / 2f, m.y), c, 3f);
                Widgets.DrawLine(new Vector2(m.x + w / 2f, m.y), new Vector2(m.x - w / 2f, m.y + h / 2f), c, 3f);
            }
            return enabled && Widgets.ButtonInvisible(r);
        }

        // Darkens a card toward its outer edge for the spherical/coverflow feel.
        private static void DirectionalShade(Rect r, float strength, bool darkenLeft)
        {
            const int strips = 8;
            for (int s = 0; s < strips; s++)
            {
                float f = (s + 0.5f) / strips;
                float a = (darkenLeft ? 1f - f : f);
                a = a * a * strength;
                var sr = new Rect(r.x + r.width * s / strips, r.y, r.width / strips + 1f, r.height);
                Fill(sr, new Color(0f, 0f, 0f, a));
            }
        }

        private void DrawBack(Rect r)
        {
            if (Back != null) GUI.DrawTexture(r, Back, ScaleMode.StretchToFill);
            else { Fill(r, new Color(0.10f, 0.08f, 0.20f)); GUI.color = Color.black; Widgets.DrawBox(r, 2); GUI.color = Color.white; }
        }

        // Mirrors Modern Psycasts UI's DrawTreeTile look.
        private void DrawFront(Rect tile, PsycasterPathDef path, bool isChosen)
        {
            var art = new Rect(tile.x, tile.y, tile.width, tile.height - LabelBarH);
            var bar = new Rect(tile.x, art.yMax, tile.width, LabelBarH);

            var tex = PathArt.Get(path, false);
            if (tex != null) { GUI.color = Color.white; GUI.DrawTexture(art, tex, ScaleMode.ScaleAndCrop); }
            else Fill(art, TileTint(path));

            Fill(bar, BarColor);

            // tile border (black + faint inner edge)
            GUI.color = Color.black; Widgets.DrawBox(tile, 2);
            GUI.color = new Color(1f, 1f, 1f, 0.06f); Widgets.DrawBox(tile.ContractedBy(2f), 1);
            GUI.color = Color.white;

            var prevAnchor = Text.Anchor; var prevFont = Text.Font;
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, path.LabelCap);
            Text.Anchor = prevAnchor; Text.Font = prevFont;

            if (isChosen) GoldGlow(tile);
            if (!path.tooltip.NullOrEmpty()) TooltipHandler.TipRegion(tile, path.tooltip);
        }

        private string TitleText()
        {
            switch (tier)
            {
                case 2: return pawn.LabelShortCap + " ascends - Enlightenment II";
                case 3: return pawn.LabelShortCap + " transcends - Enlightenment III";
                default: return pawn.LabelShortCap + " awakens as a psycaster";
            }
        }

        private string PromptText()
        {
            switch (tier)
            {
                case 2: return "A pilgrimage fulfilled. Turn one of the five cards to reveal a new path.";
                case 3: return "The mind burns bright - turn one card; any path may answer the call.";
                default: return "Long meditation has unlocked a latent gift. Turn one card to reveal the path it calls them to.";
            }
        }

        private void DrawBurst(Rect r, float bt)
        {
            float a = Mathf.Clamp01(1f - bt);
            var c = r.center;
            float maxRad = Mathf.Min(r.width, r.height) * 0.44f;

            if (Glow != null)
            {
                float flash = Mathf.Clamp01(1f - bt * 1.4f);
                float gs = Mathf.Lerp(0.25f, 1.35f, bt) * Mathf.Min(r.width, r.height);
                GUI.color = new Color(1f, 0.94f, 0.70f, flash * 0.8f);
                GUI.DrawTexture(new Rect(c.x - gs / 2f, c.y - gs / 2f, gs, gs), Glow);
            }

            // expanding shockwave ring
            float rr = bt * maxRad * 1.05f;
            GUI.color = new Color(0.98f, 0.86f, 0.50f, a * 0.45f);
            Widgets.DrawBox(new Rect(c.x - rr, c.y - rr, rr * 2f, rr * 2f), 2);
            GUI.color = Color.white;

            if (Spark != null)
            {
                const int N = 18;
                for (int k = 0; k < N; k++)
                {
                    float f = Frac(k * 0.61803399f);
                    float ang = (k * (360f / N) + 20f * f) * Mathf.Deg2Rad;
                    float speed = 0.55f + 0.45f * Frac(f * 7.3f);
                    float delay = 0.18f * Frac(f * 3.7f);
                    float lt = Mathf.Clamp01((bt - delay) / (1f - delay));
                    if (lt <= 0f) continue;
                    float rad = lt * maxRad * speed;
                    var pos = new Vector2(c.x + Mathf.Cos(ang) * rad, c.y + Mathf.Sin(ang) * rad);
                    float sz = Mathf.Lerp(8f, 22f, lt) * (0.55f + 0.75f * Frac(f * 3.1f));
                    float rot = lt * 220f * (k % 2 == 0 ? 1f : -1f);
                    DrawTexRot(Spark, pos, sz, rot, new Color(1f, 0.96f, 0.80f, Mathf.Clamp01(1f - lt)));
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

        private static void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, BaseContent.WhiteTex);
            GUI.color = prev;
        }

        private static Color TileTint(PsycasterPathDef def)
        {
            Color baseCol;
            if (def.backgroundColor.a > 0.05f) baseCol = new Color(def.backgroundColor.r, def.backgroundColor.g, def.backgroundColor.b);
            else baseCol = Color.HSVToRGB((Mathf.Abs(def.defName.GetHashCode()) % 360) / 360f, 0.32f, 1f);
            const float m = 0.30f;
            return new Color(baseCol.r * m, baseCol.g * m, baseCol.b * m, 1f);
        }

        private static void GoldGlow(Rect r)
        {
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.16f); Widgets.DrawBox(r.ExpandedBy(5f), 2);
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.38f); Widgets.DrawBox(r.ExpandedBy(3f), 2);
            GUI.color = Gold; Widgets.DrawBox(r.ExpandedBy(1f), 2);
            GUI.color = Color.white;
        }

        private void DeferReroll()
        {
            var med = GameComponent_PsycastSynergies.Instance?.GetMed(pawn, true);
            if (med != null) { med.pendingPick = tier; med.rerollCount++; }
            if (PawnUtility.ShouldSendNotificationAbout(pawn))
                Messages.Message(pawn.LabelShortCap + " sets the cards aside to meditate further - the omens will shift.",
                    pawn, MessageTypeDefOf.NeutralEvent, false);
            Close();
        }

        private static int ForgoPoints() => PsycastSynergiesMod.Settings?.transcendForgoPoints ?? 4;

        // Transcendent tiers: skip the free path card and take bonus specialization points instead (on top of
        // the +3 the tier-up already granted). The tier itself is already set, so this just awards points + closes.
        private void ForgoForPoints()
        {
            var gc = GameComponent_PsycastSynergies.Instance;
            int pts = ForgoPoints();
            var spec = gc?.GetSpec(pawn, true);
            if (spec != null) spec.points += pts;
            var med = gc?.GetMed(pawn, true);
            if (med != null) { med.rerollCount = 0; med.pendingPick = 0; }
            SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
            if (PawnUtility.ShouldSendNotificationAbout(pawn))
                Messages.Message(pawn.LabelShortCap + " forwent the vision for deeper insight.  (+" + pts + " specialization points)",
                    pawn, MessageTypeDefOf.PositiveEvent, false);
            Close();
        }

        private void Confirm()
        {
            var psy = pawn.Psycasts();
            var path = options[chosen];
            if (psy != null && !psy.unlockedPaths.Contains(path)) psy.UnlockPath(path);
            EnlightenmentTier.SetTier(pawn, tier, true);   // set the tier (PS_Enlightenment hediff) + grant tier spec points
            var med = GameComponent_PsycastSynergies.Instance?.GetMed(pawn, true);
            if (med != null)
            {
                med.rerollCount = 0; med.pendingPick = 0;
                (med.cardPaths ?? (med.cardPaths = new List<PsycasterPathDef>())).Add(path);   // record for path respec
            }
            SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
            if (PawnUtility.ShouldSendNotificationAbout(pawn))
            {
                var s = PsycastSynergiesMod.Settings;
                int bonus = tier == 2 ? (s?.tier2SpecPoints ?? 0) : tier == 3 ? (s?.tier3SpecPoints ?? 0) : tier >= 4 ? 3 : 0;
                string msg = pawn.LabelShortCap + " embraced the " + path.label.CapitalizeFirst() + " path.";
                if (bonus > 0) msg += " (+" + bonus + " specialization points)";
                Messages.Message(msg, pawn, MessageTypeDefOf.PositiveEvent, false);
            }
            Close();
        }
    }
}

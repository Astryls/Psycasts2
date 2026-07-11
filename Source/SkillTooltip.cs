#nullable disable
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using VEF.Abilities;
using VanillaPsycastsExpanded;
using AbilityDef = VEF.Abilities.AbilityDef;
using Ability = VEF.Abilities.Ability;

namespace PsycastSynergies
{
    // Diablo-2-style floating skill card. Built per-ability: role tag, what it does, the concrete
    // effects with current value + bonus %, the synergy bonuses it RECEIVES (which path-mates feed
    // it and on which stat), and what it EMPOWERS as you level it. Drawn as a Super-layer
    // ImmediateWindow so it sits above the inspect-pane / Modern Psycasts UI tab.
    public static class SkillTooltip
    {
        private const float Pad = 8f;
        private const float Row = 20f;
        private const int MaxList = 12;
        private static string RecvSuffix => "PS_TipRecvSuffix".Translate();

        private static Pawn pawn;
        private static AbilityDef ability;
        private static bool ownedHover;
        private static int frame = -1;
        private static Rect suppressRect;
        private static int suppressFrame = -1;

        public static void NotifyHover(Pawn p, AbilityDef a, Rect rect, bool owned)
        {
            pawn = p; ability = a; ownedHover = owned; frame = Time.frameCount;
            suppressRect = rect; suppressFrame = Time.frameCount;
        }

        public static bool ShouldSuppress(Rect rect)
            => suppressFrame == Time.frameCount && rect == suppressRect;

        private static string Pct(float frac) => (frac * 100f).ToString("0.#") + "%";

        // Base (pre-our-scaling) value of a stat, used to express the per-level gain as a RAW amount.
        private static float PrimaryBaseValue(AbilityDef def, Ability inst, SynStat stat)
        {
            switch (stat)
            {
                case SynStat.Power: return inst != null ? inst.GetPowerForPawn() : def.power;
                case SynStat.Radius: return inst != null ? inst.GetRadiusForPawn() : def.radius;
                case SynStat.Range: return inst != null ? inst.GetRangeForPawn() : def.range;
                case SynStat.Duration: return inst != null ? inst.GetDurationForPawn() : def.durationTime;
                default: return 0f;
            }
        }

        // "+1.6 Damage", "+0.3 Area", "+2.5s Duration" - the raw stat gained per level.
        private static string RawPerLevelStr(SynStat stat, float raw)
        {
            string lab = EffectLabel(stat);
            if (stat == SynStat.Duration) return "+" + Mathf.Max(1, Mathf.RoundToInt(raw)).ToStringTicksToPeriod() + " " + lab;
            return "+" + raw.ToString("0.0") + " " + lab;
        }

        private struct ERow { public string label, value; public bool scaled; public bool isPrimary; }
        private struct Recv { public AbilityDef def; public int lvl; public SynStat stat; public float pct; public float rate; public float str; public float raw; }

        // Right-hand text for a synergy source: active mates show current contribution; unleveled
        // ones show the per-level rate they WOULD give. Type-aware (reductions show "-", count types
        // show a count) so it reads as what it actually does, not a raw percent.
        // Describe what a synergy type actually does (not its type name).
        private static string EffectLabel(SynStat stat)
        {
            switch (stat)
            {
                case SynStat.Power: return "PS_Stat_Power".Translate();
                case SynStat.Radius: return "PS_Stat_Radius".Translate();
                case SynStat.Duration: return "PS_Stat_Duration".Translate();
                case SynStat.Strength: return "PS_Stat_Strength".Translate();
                case SynStat.Range: return "PS_Stat_Range".Translate();
                case SynStat.Cooldown: return "PS_Stat_Cooldown".Translate();
                case SynStat.Targets: return "PS_Stat_Targets".Translate();
                case SynStat.Charges: return "PS_Stat_Charges".Translate();
                case SynStat.Efficiency: return "PS_Stat_Efficiency".Translate();
                case SynStat.Haste: return "PS_Stat_Haste".Translate();
                case SynStat.Yield: return "PS_Stat_Yield".Translate();
                case SynStat.Insight: return "PS_Stat_Insight".Translate();
                case SynStat.ProjectileCount: return "PS_Stat_ProjectileCount".Translate();
                case SynStat.SummonCount: return "PS_Stat_SummonCount".Translate();
                default: return "";
            }
        }

        private static string FormatSyn(SynStat stat, float frac, bool rate)
        {
            string suf = rate ? "/lvl" : "";
            string lab = EffectLabel(stat);
            switch (stat)
            {
                case SynStat.Cooldown:
                case SynStat.Efficiency:
                case SynStat.Haste:
                    return "-" + Pct(frac) + suf + " " + lab;
                case SynStat.Targets:
                case SynStat.Charges:
                case SynStat.ProjectileCount:
                case SynStat.SummonCount:
                    return "+" + (frac / PsycastSynergiesMod.Settings.perLevelPct).ToString("0.#") + suf + " " + lab;
                default:
                    return "+" + Pct(frac) + suf + " " + lab;
            }
        }

        private static string SignLabel(SynStat stat)
        {
            switch (stat)
            {
                case SynStat.Cooldown:
                case SynStat.Efficiency:
                case SynStat.Haste:
                    return "-" + EffectLabel(stat);
                default:
                    return "+" + EffectLabel(stat);
            }
        }

        // D2-style "+1% Damage per level" line for a synergy source. Reductions show "-", count types a count.
        private static string PerLevelSyn(SynStat stat, float rate)
        {
            string lab = EffectLabel(stat);
            switch (stat)
            {
                case SynStat.Cooldown: case SynStat.Efficiency: case SynStat.Haste:
                    return "PS_PerLevel".Translate("-" + Pct(rate) + " " + lab);
                case SynStat.Targets: case SynStat.Charges: case SynStat.ProjectileCount: case SynStat.SummonCount:
                    return "PS_PerLevel".Translate("+" + (rate / PsycastSynergiesMod.Settings.perLevelPct).ToString("0.#") + " " + lab);
                default:
                    return "PS_PerLevel".Translate("+" + Pct(rate) + " " + lab);
            }
        }

        // Signed value only (no stat label) - for the "(now +X%)" live-total parenthetical on Alt.
        private static string SynValueOnly(SynStat stat, float frac)
        {
            switch (stat)
            {
                case SynStat.Cooldown: case SynStat.Efficiency: case SynStat.Haste:
                    return "-" + Pct(frac);
                case SynStat.Targets: case SynStat.Charges: case SynStat.ProjectileCount: case SynStat.SummonCount:
                    return "+" + (frac / PsycastSynergiesMod.Settings.perLevelPct).ToString("0.#");
                default:
                    return "+" + Pct(frac);
            }
        }

        // "Receives bonuses from" row text: always the per-level rate; Alt adds the live total for leveled sources.
        private static string RecvLine(Recv r, bool alt)
        {
            string txt;
            if (r.stat == SynStat.Charges)
                txt = "PS_ChargesPerLevel".Translate((0.5f * r.str).ToString("0.#"));
            else if (r.raw > 0.0001f)
                txt = "PS_PerLevel".Translate(RawPerLevelStr(r.stat, r.raw));   // raw amount (Damage/Area/Range/Duration)
            else
                txt = PerLevelSyn(r.stat, r.rate);                    // % for multiplier stats with no raw base
            if (alt && r.lvl > 0)
            {
                string total;
                if (r.stat == SynStat.Charges) total = "+" + (ChargeStore.SourceCharges(r.lvl) * r.str).ToString("0.#");
                else if (r.raw > 0.0001f)       total = RawValueOnly(r.stat, r.raw * r.lvl);
                else                            total = SynValueOnly(r.stat, r.pct);
                txt += "PS_NowParen".Translate(total);
            }
            return txt;
        }

        // Signed raw value only (no stat label) - for the "(now +X)" live total on raw-amount stats.
        private static string RawValueOnly(SynStat stat, float raw)
            => stat == SynStat.Duration ? "+" + Mathf.Max(1, Mathf.RoundToInt(raw)).ToStringTicksToPeriod() : "+" + raw.ToString("0.0");

        private static string RecvRight(Recv r)
        {
            // Charges are counted from levels (0.5 / source level), so show that directly instead of
            // the %-derived value - keeps the synergy line consistent with the actual charge count.
            if (r.stat == SynStat.Charges)
                return r.lvl > 0
                    ? "PS_LvCharges".Translate(r.lvl, (ChargeStore.SourceCharges(r.lvl) * r.str).ToString("0.#")).ToString()
                    : "PS_Lv0Charges".Translate((0.5f * r.str).ToString("0.#")).ToString();
            return r.lvl > 0
                ? "PS_LvValue".Translate(r.lvl, FormatSyn(r.stat, r.pct, false)).ToString()
                : "PS_LvValue".Translate(0, FormatSyn(r.stat, r.rate, true)).ToString();
        }
        private struct Emp { public AbilityDef def; public SynStat stat; public float str; }

        // Right-hand text for an empowered skill when Alt is held: how much THIS skill (at its level)
        // feeds that target, in the link's type. Mirrors RecvRight but from the source's perspective.
        private static string EmpRight(Emp e, int thisLvl)
        {
            if (e.stat == SynStat.Charges)
                return thisLvl > 0
                    ? "PS_LvCharges".Translate(thisLvl, (ChargeStore.SourceCharges(thisLvl) * e.str).ToString("0.#")).ToString()
                    : "PS_Lv0Charges".Translate((0.5f * e.str).ToString("0.#")).ToString();
            float rate = SkillSystem.SynergyRate(e.stat) * e.str;
            return thisLvl > 0
                ? "PS_LvValue".Translate(thisLvl, FormatSyn(e.stat, thisLvl * rate, false)).ToString()
                : "PS_LvValue".Translate(0, FormatSyn(e.stat, rate, true)).ToString();
        }

        private class Model
        {
            public AbilityDef def;
            public bool owned;
            public bool full;     // Shift held - show all synergies both directions
            public int lvl, cap, absCap, psyLevel, nextReq; public bool atCap;
            public string roleLabel; public Color roleColor;
            public string description, effectSummary;
            public string empowersLine;
            public string primaryLabel; public float primaryPct; public SynStat? primaryStat;
            public float primaryRaw; public bool primaryHasRaw;
            public List<ERow> effects;
            public List<Recv> received;
            public List<Emp> empowers;
            public bool showCost; public float costPct;
            public float psyfocusCost, entropyCost;
            public bool alt;
        }

        private static Model Build(Pawn pawn, AbilityDef def, bool owned)
        {
            var gc = GameComponent_PsycastSynergies.Instance;
            var s = PsycastSynergiesMod.Settings;
            if (gc == null || s == null) return null;

            int lvl = owned ? gc.GetLevel(pawn, def) : 0;
            var ext = def.GetModExtension<AbilityExtension_Psycast>();
            Role role = PsycastInfo.RoleOf(def);

            Ability inst = null;
            var comp = pawn.GetComp<CompAbilities>();
            if (owned && comp?.LearnedAbilities != null)
                for (int i = 0; i < comp.LearnedAbilities.Count; i++)
                    if (comp.LearnedAbilities[i].def == def) { inst = comp.LearnedAbilities[i]; break; }

            // Effects with current value + per-stat total bonus. A skill's OWN levels scale only its OWN
            // primary stat (synergies feed OTHER skills); the per-level GAIN for that primary is shown as a
            // RAW amount (+N.N <stat>) on the "Each level" line, not a percentage.
            SynStat? primStat = PsycastInfo.PrimaryStat(def);
            float pLM = 1f, xL = 0f; SpecEffects.OwnAdjust(pawn, def, ref pLM, ref xL);
            float ownDelta = primStat.HasValue
                ? s.perLevelPct * pLM * SkillSystem.CountBoost(primStat.Value) * PsycastInfo.PrimaryStrength(def) : 0f;
            float primaryBase = primStat.HasValue ? PrimaryBaseValue(def, inst, primStat.Value) : 0f;
            float primaryRaw = primaryBase * ownDelta;
            bool primaryHasRaw = primaryRaw > 0.001f;
            var effects = new List<ERow>();
            foreach (var e in PsycastInfo.Scalables(pawn, def, inst))
            {
                string val = e.value;
                bool isPrim = e.stat.HasValue && primStat.HasValue && e.stat.Value == primStat.Value;
                if (e.stat.HasValue)
                {
                    float tot = SkillSystem.StatMultiplier(pawn, def, e.stat.Value) - 1f;
                    if (tot > 0.0001f)
                        val += e.stat.Value == SynStat.Cooldown ? "  (-" + Pct(Mathf.Min(tot, 0.85f)) + ")" : "  (+" + Pct(tot) + ")";
                }
                effects.Add(new ERow { label = e.label, value = val, scaled = e.scaled, isPrimary = isPrim });
            }

            // Received: this skill's FIXED 3-5 synergy sources, each feeding ITS OWN type (mixed).
            // Sources can be any tier in the path (a bottom skill can feed a capstone).
            SynStat? thisPrim = PsycastInfo.PrimaryStat(def);
            var received = new List<Recv>();
            foreach (var src in PsycastInfo.SynergySources(def))
            {
                SynStat? sp = PsycastInfo.EdgeStat(src, def);
                if (!sp.HasValue) continue;
                int sl = gc.GetLevel(pawn, src);
                float estr = PsycastInfo.EdgeStrength(src, def);
                float rate = SkillSystem.SynergyRate(sp.Value) * estr;
                received.Add(new Recv { def = src, lvl = sl, stat = sp.Value, pct = sl * rate, rate = rate, str = estr,
                                        raw = PrimaryBaseValue(def, inst, sp.Value) * rate });
            }
            received.Sort((a, b) => b.lvl.CompareTo(a.lvl));

            // Empowers: the skills that list THIS skill among their synergy sources - each in the
            // type THIS→that link rolled, so the same skill empowers different mates differently.
            var empowers = new List<Emp>();
            if (ext?.path?.abilities != null)
                foreach (var z in ext.path.abilities)
                {
                    if (z == def) continue;
                    if (!PsycastInfo.SynergySources(z).Contains(def)) continue;
                    SynStat? et = PsycastInfo.EdgeStat(def, z);
                    if (et.HasValue) empowers.Add(new Emp { def = z, stat = et.Value, str = PsycastInfo.EdgeStrength(def, z) });
                }

            // Empowers are shown as a simple name line - each is fully detailed on that target skill's OWN card.
            string empowersLine = null;
            if (empowers.Count > 0)
            {
                var names = new List<string>();
                foreach (var e in empowers) { string n = e.def.LabelCap; if (!names.Contains(n)) names.Add(n); }
                const int capN = 10;
                string joined = string.Join("  \u00b7  ", names.GetRange(0, Mathf.Min(capN, names.Count)).ToArray());
                if (names.Count > capN) joined += "  \u00b7  " + "PS_AndMore".Translate(names.Count - capN);
                empowersLine = "PS_AlsoEmpowers".Translate(def.LabelCap, joined);
            }

            SynStat? prim = PsycastInfo.PrimaryStat(def);

            return new Model
            {
                def = def,
                owned = owned,
                lvl = lvl, cap = SkillSystem.MaxLevel(pawn, def),
                absCap = s.maxSkillLevel + SpecEffects.LevelCapBonus(pawn),
                psyLevel = pawn.Psycasts()?.level ?? 0,
                nextReq = SkillSystem.LevelReq(def, lvl + 1),
                atCap = owned && lvl >= SkillSystem.MaxLevel(pawn, def),
                roleLabel = SynergyRules.RoleLabel(role),
                roleColor = PsycastInfo.RoleColor(role),
                description = def.description,
                effectSummary = PsycastInfo.EffectSummary(def),
                primaryLabel = prim.HasValue ? SynergyRules.StatLabel(prim.Value) : null,
                primaryStat = prim,
                primaryPct = s.perLevelPct * (prim.HasValue ? SkillSystem.CountBoost(prim.Value) : 1f) * PsycastInfo.PrimaryStrength(def),
                primaryRaw = primaryRaw, primaryHasRaw = primaryHasRaw,
                effects = effects,
                received = received,
                empowers = empowers,
                empowersLine = empowersLine,
                showCost = true,
                costPct = s.scaleCost ? s.costPerLevelPct : 0f,
                psyfocusCost = ext != null ? ext.GetPsyfocusUsedByPawn(pawn) : 0f,
                entropyCost = ext != null ? ext.GetEntropyUsedByPawn(pawn) : 0f,
            };
        }

        private static float TextH(string text, float w)
        {
            if (text.NullOrEmpty()) return 0f;
            GameFont pf = Text.Font; Text.Font = GameFont.Tiny;
            float h = Text.CalcHeight(text, w);
            Text.Font = pf;
            return h;
        }

        private static int Shown(int count) => Mathf.Min(count, MaxList) + (count > MaxList ? 1 : 0);

        // Auto-fit the card width to the widest line so nothing truncates (readability pass).
        private static float ContentWidth(Model m)
        {
            GameFont pf = Text.Font;
            float w = 320f;

            Text.Font = GameFont.Small;
            float role = m.roleLabel.NullOrEmpty() ? 0f : Text.CalcSize(m.roleLabel).x + 14f;
            w = Mathf.Max(w, 26f + Text.CalcSize(m.def.LabelCap).x + role + 20f);

            Text.Font = GameFont.Tiny;
            foreach (var e in m.effects)
                w = Mathf.Max(w, Mathf.Max(Text.CalcSize(e.label).x / 0.42f, Text.CalcSize(e.value).x / 0.58f) + 16f);
            if (m.full)
            {
                foreach (var r in m.received)
                {
                    string right = RecvLine(r, true);   // size for the Alt (live-total) view so width stays stable
                    w = Mathf.Max(w, Mathf.Max((Text.CalcSize(r.def.LabelCap).x + 22f) / 0.40f, Text.CalcSize(right).x / 0.60f) + 16f);
                }
            }
            if (m.primaryLabel != null)
            {
                string eachW = m.primaryHasRaw
                    ? "PS_EachLevelPrimary".Translate(RawPerLevelStr(m.primaryStat.Value, m.primaryRaw)).ToString()
                    : "PS_EachLevelPrimary".Translate("+" + Pct(m.primaryPct) + " " + m.primaryLabel).ToString();
                w = Mathf.Max(w, Text.CalcSize(eachW).x + 20f);
            }
            w = Mathf.Max(w, Text.CalcSize(m.def.LabelCap + RecvSuffix).x + 20f);
            w = Mathf.Max(w, Text.CalcSize(FooterText(m)).x + 20f);

            // Content-aware: a long description never widens the card on its own (it just wraps), so widen the
            // card here when the description would otherwise wrap into a tall, narrow sliver - fewer, wider lines.
            if (!m.description.NullOrEmpty())
            {
                if (TextH(m.description, w - 16f) > 96f)  w = Mathf.Max(w, 470f);
                if (TextH(m.description, w - 16f) > 200f) w = Mathf.Max(w, 560f);
            }

            Text.Font = pf;
            return Mathf.Clamp(w, 320f, 560f);
        }

        private static float Measure(Model m, float W)
        {
            float inner = W - 16f;
            float h = 28f + 4f;
            float dH = TextH(m.description, inner); if (dH > 0f) h += dH + 4f;   // full height - never clip the description
            float sH = Mathf.Min(54f, TextH(m.effectSummary, inner)); if (sH > 0f) h += sH + 4f;

            h += 7f;                                       // divider
            h += Row;                                      // skill level
            if (m.primaryStat.HasValue) h += 18f;          // "each level" line
            h += 30f;                                      // bottom required-level line + divider
            if (m.effects.Count > 0) h += 18f + m.effects.Count * Row;

            if (m.full)
            {
                h += 7f + 18f + (m.received.Count > 0 ? Shown(m.received.Count) : 1) * Row;
                if (!m.empowersLine.NullOrEmpty()) h += 7f + TextH(m.empowersLine, inner) + 4f;
            }
            else
            {
                h += 26f; // framed "hold Shift" tip
            }

            if (m.psyfocusCost > 0f || m.entropyCost > 0f) h += Row;
            h += Row;                                      // cost / level (always shown)
            h += 18f + Pad;                                // footer + pad
            return h;
        }

        // Cache the built model + its measured size so we rebuild only when the hovered skill (its
        // level, or the pawn's psy-level) changes - NOT every frame. Build + ContentWidth + Measure do
        // ~25 Text.CalcSize calls plus several list/string allocations; running them per-frame while
        // hovering churned the GC and stuttered the whole tab.
        private static Model cM; private static float cW, cH;
        private static Pawn cP; private static AbilityDef cA; private static bool cO; private static int cL = -1, cPsy = -1;

        public static void DrawFloating()
        {
            if (Time.frameCount - frame > 1 || pawn == null || ability == null) return;

            int lvl = ownedHover ? (GameComponent_PsycastSynergies.Instance?.GetLevel(pawn, ability) ?? 0) : 0;
            int psy = pawn.Psycasts()?.level ?? 0;
            if (cM == null || cP != pawn || cA != ability || cO != ownedHover || cL != lvl || cPsy != psy)
            {
                cM = Build(pawn, ability, ownedHover);
                if (cM != null)
                {
                    cM.full = true;   // always show the full breakdown (no Shift gating)
                    cW = ContentWidth(cM);
                    cH = Mathf.Min(Measure(cM, cW), UI.screenHeight * 0.94f);
                }
                cP = pawn; cA = ability; cO = ownedHover; cL = lvl; cPsy = psy;
            }
            Model m = cM;
            if (m == null) return;

            m.alt = Event.current.alt;   // Alt reveals exact +/- percentages (draw-only; no relayout)
            float W = cW;
            float h = cH;
            Vector2 mp = Event.current.mousePosition;
            float x = mp.x + 18f, y = mp.y;
            if (x + W > UI.screenWidth) x = mp.x - W - 18f;
            if (x < 2f) x = 2f;
            if (y + h > UI.screenHeight) y = UI.screenHeight - h - 2f;
            if (y < 2f) y = 2f;
            Rect rect = new Rect(x, y, W, h);

            Find.WindowStack.ImmediateWindow(74126002, rect, WindowLayer.Super, () =>
            {
                try { Draw(new Rect(0f, 0f, rect.width, rect.height), m); } catch { }
            }, doBackground: false, absorbInputAroundWindow: false, 0.5f);
        }

        private static void Draw(Rect col, Model m)
        {
            Palette.DrawCard(col);
            Widgets.DrawBoxSolid(new Rect(col.x, col.y, 3f, col.height), m.atCap ? Palette.Gold : Palette.Accent);

            // Header: icon + name + role chip.
            Rect hr = new Rect(col.x + 3f, col.y, col.width - 3f, 28f);
            Widgets.DrawBoxSolid(hr, Palette.BGD);
            if (m.def.icon != null) { GUI.color = Color.white; GUI.DrawTexture(new Rect(hr.x + 5f, hr.y + 5f, 18f, 18f), m.def.icon); }
            float nameRight = hr.xMax - 6f;
            if (!m.roleLabel.NullOrEmpty())
            {
                Text.Font = GameFont.Tiny;
                float tw = Text.CalcSize(m.roleLabel).x + 12f;
                Rect chip = new Rect(hr.xMax - tw - 6f, hr.y + 6f, tw, 16f);
                Widgets.DrawBoxSolid(chip, m.roleColor);
                Text.Anchor = TextAnchor.MiddleCenter; GUI.color = Color.black;
                Widgets.Label(chip, m.roleLabel);
                nameRight = chip.x - 4f;
            }
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = m.atCap ? Palette.Gold : Palette.Accent;
            bool pwN = Text.WordWrap; Text.WordWrap = false;
            Widgets.Label(new Rect(hr.x + 26f, hr.y, nameRight - (hr.x + 26f), hr.height), m.def.LabelCap);
            Text.WordWrap = pwN; GUI.color = Color.white;

            float y = hr.yMax + 4f;
            float innerW = col.width - 16f;

            if (!m.description.NullOrEmpty())
            {
                float dH = TextH(m.description, innerW);
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true; GUI.color = Palette.TextDim;
                Widgets.Label(new Rect(col.x + 8f, y, innerW, dH), m.description); y += dH + 4f;
            }
            if (!m.effectSummary.NullOrEmpty())
            {
                float sH = Mathf.Min(54f, TextH(m.effectSummary, innerW));
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.UpperLeft; GUI.color = m.roleColor;
                Widgets.Label(new Rect(col.x + 8f, y, innerW, sH), m.effectSummary); GUI.color = Color.white; y += sH + 4f;
            }

            Divider(col, ref y);
            KeyVal(col, ref y, "PS_TipSkillLevel".Translate(), m.lvl + " / " + m.absCap,
                !m.owned ? Palette.TextDim : (m.lvl >= m.absCap ? Palette.Gold : Palette.Stat));
            if (m.primaryStat.HasValue)
            {
                string each = m.primaryHasRaw
                    ? "PS_EachLevelPrimary".Translate(RawPerLevelStr(m.primaryStat.Value, m.primaryRaw)).ToString()
                    : "PS_EachLevelPrimary".Translate(FormatSyn(m.primaryStat.Value, m.primaryPct, false)).ToString();
                Label(col.x + 8f, ref y, innerW, each, Palette.Accent, 18f);
            }
            if (m.effects.Count > 0)
            {
                Label(col.x + 8f, ref y, innerW, "PS_TipCurrentEffects".Translate(), Palette.TextDim, 18f);
                foreach (var e in m.effects)
                {
                    Color vc = e.isPrimary ? Palette.Accent : (e.scaled ? Palette.Good : Palette.TextDim);
                    KeyVal(col, ref y, e.label, e.value, vc, e.isPrimary ? Palette.Accent : (Color?)null);
                }
            }

            if (m.full)
            {
                Divider(col, ref y);
                Label(col.x + 8f, ref y, innerW, m.def.LabelCap + RecvSuffix, Palette.TextDim, 18f);
                if (m.received.Count == 0)
                    Label(col.x + 8f, ref y, innerW, "PS_TipNoMates".Translate(), Palette.TextDim, Row);
                else
                {
                    int shown = Mathf.Min(m.received.Count, MaxList);
                    for (int i = 0; i < shown; i++)
                    {
                        var r = m.received[i];
                        SkillRow(col, ref y, r.def, RecvLine(r, m.alt), r.lvl > 0 ? Palette.Good : Palette.TextDim);
                    }
                    if (m.received.Count > shown)
                        Label(col.x + 8f, ref y, innerW, "PS_TipMoreRows".Translate(m.received.Count - shown), Palette.TextDim, Row);
                }

                if (!m.empowersLine.NullOrEmpty())
                {
                    Divider(col, ref y);
                    float eH = TextH(m.empowersLine, innerW);
                    Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true; GUI.color = Palette.Accent;
                    Widgets.Label(new Rect(col.x + 8f, y, innerW, eH), m.empowersLine); y += eH + 4f;
                    GUI.color = Color.white;
                }
            }
            else
            {
                Rect fr = new Rect(col.x + 8f, y + 2f, innerW, 22f);
                Widgets.DrawBoxSolid(fr, Palette.BGD);
                GUI.color = Palette.Accent; Widgets.DrawBox(fr, 1); GUI.color = Color.white;
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = Palette.Accent;
                Widgets.Label(fr, "PS_TipHoldShift".Translate());
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                y += 26f;
            }

            if (m.psyfocusCost > 0f || m.entropyCost > 0f)
            {
                string cc = "";
                if (m.psyfocusCost > 0f) cc += Pct(m.psyfocusCost) + " " + "PS_TipPsyfocus".Translate();
                if (m.entropyCost > 0f) cc += (cc.Length > 0 ? "  \u2022  " : "") + m.entropyCost.ToString("0.#") + " " + "PS_TipHeat".Translate();
                KeyVal(col, ref y, "PS_TipCastCost".Translate(), cc, Palette.Stat);
            }
            KeyVal(col, ref y, "PS_TipCostPerLevel".Translate(), "PS_TipCostPerLevelVal".Translate(Pct(m.costPct)), Palette.Bad);

            // Diablo-2-style required-level line, anchored at the bottom. Red when your psycaster
            // level is below it.
            Divider(col, ref y);
            if (m.lvl >= m.absCap)
                Label(col.x + 8f, ref y, innerW, "PS_TipMaxLevel".Translate(m.absCap), Palette.Gold, 20f);
            else
                Label(col.x + 8f, ref y, innerW, "PS_TipReqLevel".Translate(m.nextReq), m.psyLevel >= m.nextReq ? Palette.Stat : Palette.Bad, 20f);

            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = Palette.TextDim;
            bool pwF = Text.WordWrap; Text.WordWrap = false;
            Widgets.Label(new Rect(col.x + 8f, y + 2f, innerW, 18f), FooterText(m));
            Text.WordWrap = pwF;

            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
        }

        private static void SkillRow(Rect col, ref float y, AbilityDef def, string right, Color rightColor)
        {
            Rect r = new Rect(col.x + 8f, y, col.width - 16f, Row);
            if (def.icon != null) { GUI.color = Color.white; GUI.DrawTexture(new Rect(r.x, r.y + 1f, 18f, 18f), def.icon); }
            bool pw = Text.WordWrap; Text.WordWrap = false;
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = Palette.TextDim;
            Widgets.Label(new Rect(r.x + 22f, r.y, r.width * 0.40f - 22f, r.height), def.LabelCap);
            Text.Anchor = TextAnchor.MiddleRight; GUI.color = rightColor;
            Widgets.Label(new Rect(r.x + r.width * 0.40f, r.y, r.width * 0.60f, r.height), right);
            Text.WordWrap = pw; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            y += Row;
        }

        private static string FooterText(Model m)
        {
            if (!m.owned) return "PS_TipNotLearned".Translate();
            if (m.lvl < m.cap) return "PS_TipClickInvest".Translate();
            if (m.lvl < m.absCap) return "PS_TipLockedLevel".Translate();
            return "PS_TipMaxReached".Translate();
        }

        private static void Label(float x, ref float y, float w, string text, Color color, float h)
        {
            bool pw = Text.WordWrap; Text.WordWrap = false;
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = color;
            Widgets.Label(new Rect(x, y, w, h), text);
            Text.WordWrap = pw; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            y += h;
        }

        private static void KeyVal(Rect col, ref float y, string label, string val, Color valColor, Color? labelColor = null)
        {
            Rect r = new Rect(col.x + 8f, y, col.width - 16f, Row);
            bool pw = Text.WordWrap; Text.WordWrap = false;
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = labelColor ?? Palette.TextDim;
            Widgets.Label(new Rect(r.x, r.y, r.width * 0.42f, r.height), label);
            Text.Anchor = TextAnchor.MiddleRight; GUI.color = valColor;
            Widgets.Label(new Rect(r.x + r.width * 0.42f, r.y, r.width * 0.58f, r.height), val);
            Text.WordWrap = pw; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            y += Row;
        }

        private static void Divider(Rect col, ref float y)
        {
            GUI.color = Palette.BGL;
            Widgets.DrawLineHorizontal(col.x + 8f, y + 1f, col.width - 16f);
            GUI.color = Color.white;
            y += 7f;
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.WindowStackOnGUI))]
    public static class Patch_WindowStack_DrawSkillTooltip
    {
        static void Postfix()
        {
            try { if (Current.ProgramState == ProgramState.Playing) SkillTooltip.DrawFloating(); }
            catch { }
        }
    }

    // Suppress VPE / Modern Psycasts UI's plain tooltip for the exact icon we're showing our card over.
    [HarmonyPatch]
    public static class Patch_SuppressVpeAbilityTip
    {
        static System.Reflection.MethodBase TargetMethod()
            => AccessTools.Method(typeof(TooltipHandler), nameof(TooltipHandler.TipRegion),
                new[] { typeof(Rect), typeof(Func<string>), typeof(int) });

        static bool Prefix(Rect rect) => !SkillTooltip.ShouldSuppress(rect);
    }
}

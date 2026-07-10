#nullable disable
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace PsycastSynergies
{
    // Safety net for the awakening card pick. While any colonist has a pending pick (deferred with
    // "keep meditating", or the window was closed with Esc), this alert stays up: meditating re-opens
    // the cards on the hourly roll, and CLICKING the alert re-opens them immediately. Without it, an
    // Esc-close could soft-lock the tier-up (the letter is gone and nothing else re-offers the cards).
    // Vanilla auto-instantiates every non-abstract Alert subclass, so no registration is needed.
    public class Alert_PendingCardPick : Alert
    {
        private readonly List<Pawn> pending = new List<Pawn>();

        public Alert_PendingCardPick()
        {
            defaultLabel = "Psycast path awaits";
            defaultPriority = AlertPriority.High;
        }

        private List<Pawn> Pending()
        {
            pending.Clear();
            var gc = GameComponent_PsycastSynergies.Instance;
            if (gc == null) return pending;
            var list = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists;
            for (int i = 0; i < list.Count; i++)
            {
                var med = gc.GetMed(list[i], false);
                if (med != null && med.pendingPick > 0) pending.Add(list[i]);
            }
            return pending;
        }

        public override string GetLabel() =>
            pending.Count > 1 ? "Psycast paths await (" + pending.Count + ")" : "Psycast path awaits";

        public override TaggedString GetExplanation()
        {
            var sb = new StringBuilder();
            sb.AppendLine("These colonists have enlightenment cards waiting to be faced:");
            sb.AppendLine();
            for (int i = 0; i < pending.Count; i++) sb.AppendLine("  - " + pending[i].LabelShortCap);
            sb.AppendLine();
            sb.Append("The cards return on their own while the colonist meditates - or click this alert to open the pick right now.");
            return sb.ToString();
        }

        public override AlertReport GetReport() => AlertReport.CulpritsAre(Pending());

        // Click = re-open the pick immediately (first pending colonist). Falls back to the default
        // jump-to-culprit when a pick window is already up.
        protected override void OnClick()
        {
            if (Find.WindowStack != null && !Find.WindowStack.IsOpen(typeof(Window_Awakening)))
            {
                var gc = GameComponent_PsycastSynergies.Instance;
                for (int i = 0; i < pending.Count; i++)
                {
                    var med = gc?.GetMed(pending[i], false);
                    if (med != null && med.pendingPick > 0)
                    {
                        int tier = med.pendingPick;
                        med.pendingPick = 0;
                        MeditationSystem.OpenPick(pending[i], tier);
                        return;
                    }
                }
            }
            base.OnClick();
        }
    }
}

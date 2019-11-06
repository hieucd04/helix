namespace Helix.Bot.Abstractions
{
    public class WorkingProgressReportEvent : Event
    {
        public int BrokenUrlCount { get; set; }

        public int RemainingWorkload { get; set; }

        public int ValidUrlCount { get; set; }

        public int VerifiedUrlCount { get; set; }
    }
}
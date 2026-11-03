using System;

namespace FFTS.Core
{
    public enum TimeSegment { Morning, Afternoon, Evening, Night, LateNight }

    /// <summary>
    /// In-game time in whole minutes since the campaign started (Day 0, 08:00 by default).
    /// Real-time scaling lives in the presentation layer; this class only advances game minutes.
    /// </summary>
    public sealed class GameClock
    {
        public const int MinutesPerDay = 24 * 60;
        private readonly EventBus _bus;

        public long TotalMinutes { get; private set; }
        public int Day => (int)(TotalMinutes / MinutesPerDay);
        public int MinuteOfDay => (int)(TotalMinutes % MinutesPerDay);
        public int Hour => MinuteOfDay / 60;
        public int Minute => MinuteOfDay % 60;
        public TimeSegment Segment => SegmentFor(MinuteOfDay);

        public GameClock(EventBus bus, long startMinutes = 8 * 60)
        {
            _bus = bus;
            TotalMinutes = startMinutes;
        }

        /// <summary>Morning 06-12, Afternoon 12-17, Evening 17-21, Night 21-02, LateNight 02-06.</summary>
        public static TimeSegment SegmentFor(int minuteOfDay)
        {
            int h = minuteOfDay / 60;
            if (h >= 6 && h < 12) return TimeSegment.Morning;
            if (h >= 12 && h < 17) return TimeSegment.Afternoon;
            if (h >= 17 && h < 21) return TimeSegment.Evening;
            if (h >= 21 || h < 2) return TimeSegment.Night;
            return TimeSegment.LateNight;
        }

        public void Advance(int minutes)
        {
            if (minutes < 0) throw new ArgumentOutOfRangeException(nameof(minutes), "Time cannot run backwards.");
            for (int i = 0; i < minutes; i++)
            {
                var prevSeg = Segment; int prevDay = Day;
                TotalMinutes++;
                if (Day != prevDay) _bus?.Publish(new DayChanged(Day));
                if (Segment != prevSeg) _bus?.Publish(new TimeSegmentChanged(prevSeg, Segment, Day));
            }
        }

        public void Restore(long totalMinutes)
        {
            if (totalMinutes < 0) throw new ArgumentOutOfRangeException(nameof(totalMinutes));
            TotalMinutes = totalMinutes;
        }

        public override string ToString() => $"Day {Day + 1} {Hour:00}:{Minute:00} ({Segment})";
    }
}

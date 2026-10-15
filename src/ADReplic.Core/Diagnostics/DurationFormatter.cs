using System;

namespace ADReplic.Core.Diagnostics
{
    /// <summary>
    /// Formate une durée pour un lecteur humain : deux unités au plus, la plus
    /// grande d'abord, et "moins d'une minute" sous la minute. Au-delà de 10 jours
    /// les heures n'apportent rien : on ne garde que les jours.
    /// </summary>
    public static class DurationFormatter
    {
        public static string ToHumanText(TimeSpan? duration)
        {
            if (duration == null) return "";
            var span = duration.Value;
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;

            if (span.TotalMinutes < 1) return "moins d'une minute";

            if (span.TotalDays >= 10) return $"{(int)span.TotalDays} j";

            if (span.TotalDays >= 1)
            {
                var days = (int)span.TotalDays;
                return span.Hours > 0 ? $"{days} j {span.Hours} h" : $"{days} j";
            }

            if (span.TotalHours >= 1)
            {
                var hours = (int)span.TotalHours;
                return span.Minutes > 0 ? $"{hours} h {span.Minutes} min" : $"{hours} h";
            }

            return $"{(int)span.TotalMinutes} min";
        }
    }
}

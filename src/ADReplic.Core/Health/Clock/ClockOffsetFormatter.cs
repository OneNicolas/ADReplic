using System;
using System.Globalization;

namespace ADReplic.Core.Health.Clock
{
    /// <summary>
    /// Texte court d'un décalage d'horloge : « +0,4 s », « -10 min 3 s », « +1 h 5 min »,
    /// « — » si non mesuré. Le signe est toujours affiché : un décalage est une direction
    /// (en avance / en retard), pas seulement une grandeur.
    /// </summary>
    public static class ClockOffsetFormatter
    {
        public const string NotMeasured = "\u2014";

        private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

        public static string ToText(TimeSpan? offset)
        {
            if (!offset.HasValue) return NotMeasured;

            var sign = offset.Value < TimeSpan.Zero ? "-" : "+";
            var magnitude = offset.Value.Duration();

            if (magnitude < TimeSpan.FromMinutes(1))
                return sign + magnitude.TotalSeconds.ToString("0.0", French) + " s";

            var totalMinutes = (int)Math.Floor(magnitude.TotalMinutes);
            var seconds = (int)Math.Round(magnitude.TotalSeconds - totalMinutes * 60);
            if (totalMinutes < 60)
                return sign + totalMinutes + " min" + (seconds > 0 ? " " + seconds + " s" : "");

            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;
            return sign + hours + " h" + (minutes > 0 ? " " + minutes + " min" : "");
        }
    }
}

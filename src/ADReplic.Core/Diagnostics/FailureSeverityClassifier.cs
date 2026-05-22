using System;
using ADReplic.Core.Models;

namespace ADReplic.Core.Diagnostics
{
    /// <summary>
    /// Classification de la sévérité d'un échec de réplication selon sa durée.
    /// Isolé pour être testable et ajustable indépendamment de la sonde.
    /// </summary>
    public static class FailureSeverityClassifier
    {
        public static readonly TimeSpan RecentThreshold    = TimeSpan.FromHours(1);
        public static readonly TimeSpan SustainedThreshold = TimeSpan.FromHours(24);

        public static ReplicationFailureSeverity Classify(TimeSpan duration)
        {
            if (duration < RecentThreshold)    return ReplicationFailureSeverity.Recent;
            if (duration < SustainedThreshold) return ReplicationFailureSeverity.Sustained;
            return ReplicationFailureSeverity.Critical;
        }
    }
}

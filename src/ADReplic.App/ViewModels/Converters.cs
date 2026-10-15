using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ADReplic.Core.Diagnostics.Issues;
using ADReplic.Core.Models;

namespace ADReplic.App.ViewModels
{
    /// <summary>
    /// Converters MVVM réutilisables. Singletons pour éviter une StaticResource par vue.
    /// </summary>
    public sealed class BoolToVisibleConverter : IValueConverter
    {
        public static readonly BoolToVisibleConverter Instance = new BoolToVisibleConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility v && v == Visibility.Visible;
    }

    public sealed class BoolToCollapsedConverter : IValueConverter
    {
        public static readonly BoolToCollapsedConverter Instance = new BoolToCollapsedConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility v && v == Visibility.Collapsed;
    }

    /// <summary>Durée en français lisible ("3 j 4 h", "45 min") via le formateur Core.</summary>
    public sealed class DurationToHumanTextConverter : IValueConverter
    {
        public static readonly DurationToHumanTextConverter Instance = new DurationToHumanTextConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is TimeSpan ts ? ADReplic.Core.Diagnostics.DurationFormatter.ToHumanText(ts) : "";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Découpe une recommandation multi-lignes "1. ...\n2. ..." en étapes pour
    /// un ItemsControl, numéro retiré (la liste numérote elle-même).
    /// </summary>
    public sealed class StepsSplitConverter : IValueConverter
    {
        public static readonly StepsSplitConverter Instance = new StepsSplitConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is string text) || string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
            return text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(l => System.Text.RegularExpressions.Regex.Replace(l.Trim(), @"^\d+\.\s*", ""))
                       .Where(l => l.Length > 0)
                       .ToArray();
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>AlternationIndex (base 0) vers "1.", "2."... pour les listes numérotées.</summary>
    public sealed class AlternationIndexToNumberConverter : IValueConverter
    {
        public static readonly AlternationIndexToNumberConverter Instance = new AlternationIndexToNumberConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is int i ? (i + 1).ToString(CultureInfo.InvariantCulture) + "." : "";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public sealed class StringJoinConverter : IValueConverter
    {
        public static readonly StringJoinConverter Instance = new StringJoinConverter();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is IEnumerable items && !(value is string))
            {
                var separator = parameter as string ?? ", ";
                return string.Join(separator, items.Cast<object>().Select(o => o?.ToString()));
            }
            return value?.ToString();
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public sealed class ReplicationStatusToBrushConverter : IValueConverter
    {
        public static readonly ReplicationStatusToBrushConverter Instance = new ReplicationStatusToBrushConverter();

        private static readonly SolidColorBrush Healthy = Freeze(0x1A, 0x7F, 0x37);
        private static readonly SolidColorBrush Warning = Freeze(0x9A, 0x67, 0x00);
        private static readonly SolidColorBrush Failing = Freeze(0xD1, 0x24, 0x2F);
        private static readonly SolidColorBrush Unknown = Freeze(0x6E, 0x77, 0x81);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ReplicationLinkStatus s)
            {
                switch (s)
                {
                    case ReplicationLinkStatus.Healthy: return Healthy;
                    case ReplicationLinkStatus.Warning: return Warning;
                    case ReplicationLinkStatus.Failing:
                    case ReplicationLinkStatus.Unreachable: return Failing;
                }
            }
            return Unknown;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static SolidColorBrush Freeze(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>
    /// Pastille de joignabilité d'un DC : vert si joignable, rouge sinon.
    /// Mêmes teintes que les autres pastilles de statut pour rester cohérent.
    /// </summary>
    public sealed class ReachabilityToBrushConverter : IValueConverter
    {
        public static readonly ReachabilityToBrushConverter Instance = new ReachabilityToBrushConverter();

        private static readonly SolidColorBrush Reachable   = Freeze(0x1A, 0x7F, 0x37);
        private static readonly SolidColorBrush Unreachable = Freeze(0xD1, 0x24, 0x2F);
        private static readonly SolidColorBrush Unknown     = Freeze(0x6E, 0x77, 0x81);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool reachable) return reachable ? Reachable : Unreachable;
            return Unknown;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static SolidColorBrush Freeze(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>Libellé de tooltip pour la pastille de joignabilité.</summary>
    public sealed class ReachabilityToTextConverter : IValueConverter
    {
        public static readonly ReachabilityToTextConverter Instance = new ReachabilityToTextConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool reachable)
            {
                return reachable
                    ? "DC joignable"
                    : "DC injoignable : aucune interrogation réseau n'a abouti pendant l'audit.";
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public sealed class LatencyToTextConverter : IValueConverter
    {
        public static readonly LatencyToTextConverter Instance = new LatencyToTextConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || !(value is TimeSpan t)) return "—";
            if (t.TotalMinutes < 1) return $"{(int)t.TotalSeconds}s";
            if (t.TotalHours < 1) return $"{(int)t.TotalMinutes}min";
            if (t.TotalDays < 1) return $"{(int)t.TotalHours}h{t.Minutes:D2}";
            return $"{(int)t.TotalDays}j{t.Hours:D2}h";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public sealed class HealthLevelToBrushConverter : IValueConverter
    {
        public static readonly HealthLevelToBrushConverter Instance = new HealthLevelToBrushConverter();

        private static readonly SolidColorBrush Excellent = Freeze(0x1A, 0x7F, 0x37);
        private static readonly SolidColorBrush Warning   = Freeze(0x9A, 0x67, 0x00);
        private static readonly SolidColorBrush Critical  = Freeze(0xD1, 0x24, 0x2F);
        private static readonly SolidColorBrush Neutral   = Freeze(0x6E, 0x77, 0x81);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is HealthLevel level)
            {
                switch (level)
                {
                    case HealthLevel.Excellent: return Excellent;
                    case HealthLevel.Warning:   return Warning;
                    case HealthLevel.Critical:  return Critical;
                }
            }
            return Neutral;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static SolidColorBrush Freeze(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    public sealed class IssueSeverityToBrushConverter : IValueConverter
    {
        public static readonly IssueSeverityToBrushConverter Instance = new IssueSeverityToBrushConverter();

        private static readonly SolidColorBrush Info     = Freeze(0x1F, 0x6F, 0xEB);
        private static readonly SolidColorBrush Warning  = Freeze(0x9A, 0x67, 0x00);
        private static readonly SolidColorBrush Critical = Freeze(0xD1, 0x24, 0x2F);
        private static readonly SolidColorBrush Neutral  = Freeze(0x6E, 0x77, 0x81);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is IssueSeverity s)
            {
                switch (s)
                {
                    case IssueSeverity.Info:     return Info;
                    case IssueSeverity.Warning:  return Warning;
                    case IssueSeverity.Critical: return Critical;
                }
            }
            return Neutral;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static SolidColorBrush Freeze(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    public sealed class FailureSeverityToBrushConverter : IValueConverter
    {
        public static readonly FailureSeverityToBrushConverter Instance = new FailureSeverityToBrushConverter();

        private static readonly SolidColorBrush Warn = Freeze(0x9A, 0x67, 0x00);
        private static readonly SolidColorBrush Fail = Freeze(0xD1, 0x24, 0x2F);
        private static readonly SolidColorBrush Neutral = Freeze(0x6E, 0x77, 0x81);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ReplicationFailureSeverity s)
            {
                switch (s)
                {
                    case ReplicationFailureSeverity.Recent: return Warn;
                    case ReplicationFailureSeverity.Sustained: return Fail;
                    case ReplicationFailureSeverity.Critical: return Fail;
                }
            }
            return Neutral;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static SolidColorBrush Freeze(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    public sealed class DnsCheckStatusToBrushConverter : IValueConverter
    {
        public static readonly DnsCheckStatusToBrushConverter Instance = new DnsCheckStatusToBrushConverter();

        private static readonly SolidColorBrush Ok      = Freeze(0x1A, 0x7F, 0x37);
        private static readonly SolidColorBrush Warn    = Freeze(0x9A, 0x67, 0x00);
        private static readonly SolidColorBrush Fail    = Freeze(0xD1, 0x24, 0x2F);
        private static readonly SolidColorBrush Neutral = Freeze(0x6E, 0x77, 0x81);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DnsCheckStatus s)
            {
                switch (s)
                {
                    case DnsCheckStatus.Ok:      return Ok;
                    case DnsCheckStatus.Missing: return Warn;
                    case DnsCheckStatus.Error:   return Fail;
                }
            }
            return Neutral;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static SolidColorBrush Freeze(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    public sealed class PortCheckStatusToBrushConverter : IValueConverter
    {
        public static readonly PortCheckStatusToBrushConverter Instance = new PortCheckStatusToBrushConverter();

        private static readonly SolidColorBrush Open    = Freeze(0x1A, 0x7F, 0x37);
        private static readonly SolidColorBrush Warn    = Freeze(0x9A, 0x67, 0x00);
        private static readonly SolidColorBrush Closed  = Freeze(0xD1, 0x24, 0x2F);
        private static readonly SolidColorBrush Neutral = Freeze(0x6E, 0x77, 0x81);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PortCheckStatus s)
            {
                switch (s)
                {
                    case PortCheckStatus.Open:    return Open;
                    case PortCheckStatus.Closed:  return Closed;
                    case PortCheckStatus.Timeout: return Warn;
                    case PortCheckStatus.Error:   return Warn;
                }
            }
            return Neutral;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static SolidColorBrush Freeze(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>
    /// Traduit une catégorie de record DNS en libellé lisible : "SRV _ldap", "A", etc.
    /// Aligne l'affichage GUI avec celui du rapport HTML (DnsTypeLabel) pour cohérence.
    /// </summary>
    public sealed class DnsCheckedRecordTypeToTextConverter : IValueConverter
    {
        public static readonly DnsCheckedRecordTypeToTextConverter Instance = new DnsCheckedRecordTypeToTextConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DnsCheckedRecordType t)
            {
                switch (t)
                {
                    case DnsCheckedRecordType.SrvLdap:     return "SRV _ldap";
                    case DnsCheckedRecordType.SrvKerberos: return "SRV _kerberos";
                    case DnsCheckedRecordType.SrvGc:       return "SRV _gc";
                    case DnsCheckedRecordType.SrvKpasswd:  return "SRV _kpasswd";
                    case DnsCheckedRecordType.ARecord:     return "A";
                }
            }
            return value?.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Renvoie true si value est égal au ConverterParameter. Sert aux MenuItem
    /// checkables qui doivent se comporter comme un groupe de boutons radio,
    /// par exemple le choix de l'intervalle d'auto-refresh.
    /// </summary>
    /// <remarks>
    /// Le ConverterParameter est par défaut une string en XAML : on convertit donc
    /// vers le type runtime de value avant comparaison. Cas typique : value=int 15,
    /// parameter="15" -> on parse "15" en int et on compare.
    /// </remarks>
    public sealed class EqualsConverter : IValueConverter
    {
        public static readonly EqualsConverter Instance = new EqualsConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null && parameter == null) return true;
            if (value == null || parameter == null) return false;

            if (value.GetType() == parameter.GetType()) return value.Equals(parameter);

            try
            {
                var converted = System.Convert.ChangeType(parameter, value.GetType(), CultureInfo.InvariantCulture);
                return value.Equals(converted);
            }
            catch
            {
                return false;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Cache un élément quand la valeur source est null, ou vide pour une chaîne
    /// ou une collection (v0.7.0). Utile pour les bindings vers des sous-objets
    /// optionnels (HealthScoreDelta d'un diff) et les sections facultatives des
    /// panneaux de détail (Impact, Commands...).
    /// </summary>
    public sealed class NullToCollapsedConverter : IValueConverter
    {
        public static readonly NullToCollapsedConverter Instance = new NullToCollapsedConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return Visibility.Collapsed;
            if (value is string s) return string.IsNullOrWhiteSpace(s) ? Visibility.Collapsed : Visibility.Visible;
            if (value is ICollection c) return c.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}

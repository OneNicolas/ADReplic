using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
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
}

using Gallery2.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Data;

namespace Gallery2.Helpers;

public class DateGroupConverter : IValueConverter
{
    public GroupMode Mode { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime date)
            return "Unknown Date";

        return Mode switch
        {
            GroupMode.Month => date.ToString("MMMM yyyy", CultureInfo.CurrentCulture),
            GroupMode.Week => $"Week of {StartOfWeek(date):MMMM d, yyyy}",
            GroupMode.Day => date.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture),
            _ => string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static DateTime StartOfWeek(DateTime date)
    {
        int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff).Date;
    }
}
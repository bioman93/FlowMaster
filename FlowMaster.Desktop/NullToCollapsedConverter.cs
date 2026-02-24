using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FlowMaster.Desktop
{
    /// <summary>
    /// null → Collapsed, non-null → Visible
    /// </summary>
    public class NullToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value == null ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}

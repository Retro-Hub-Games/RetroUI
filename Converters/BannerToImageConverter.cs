using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

public class BannerToImageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path))
        {
            return new BitmapImage(new Uri(path, UriKind.RelativeOrAbsolute));
        }
        // Return a default image if the value is null or empty
        return new BitmapImage(new Uri("pack://application:,,,/Images/default-banner.png")); // Default image path
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
} 
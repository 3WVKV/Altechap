using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfColor = System.Windows.Media.Color;

namespace Altechap.Converters;

public class BoolVis : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class InvBoolVis : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class HasItemsVis : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class EmptyListVis : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is int n && n == 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class InvBool : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => v is bool b ? !b : v;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => v is bool b ? !b : v;
}

public class NullVis : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v != null && (v is not string s || s.Length > 0) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>Couleur hex "#RRGGBB" → SolidColorBrush.</summary>
public class HexBrush : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        try
        {
            var hex = (v as string ?? "#45475A").TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = System.Convert.ToByte(hex[..2], 16);
                byte g = System.Convert.ToByte(hex[2..4], 16);
                byte b = System.Convert.ToByte(hex[4..6], 16);
                return new SolidColorBrush(WpfColor.FromRgb(r, g, b));
            }
        }
        catch { }
        return new SolidColorBrush(WpfColor.FromRgb(0x45, 0x47, 0x5A));
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>
/// Chemin fichier PNG → BitmapImage.
/// Retourne null si fichier inexistant (fallback lettre dans XAML).
/// </summary>
public class PathToImage : IValueConverter
{
    public object? Convert(object v, Type t, object p, CultureInfo c)
    {
        var path = v as string;
        bool exists = !string.IsNullOrEmpty(path) && File.Exists(path);

        // ConverterParameter="vis"  → retourne Visibility
        // ConverterParameter="inv"  → retourne Visibility inversée
        var param = p as string;
        if (param == "vis") return exists ? Visibility.Visible : Visibility.Collapsed;
        if (param == "inv") return exists ? Visibility.Collapsed : Visibility.Visible;

        // Sans paramètre → retourne BitmapImage ou null
        if (!exists) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.UriSource = new Uri(path!, UriKind.Absolute);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch { return null; }
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>Enabled (bool) → opacité : true=1.0, false=0.45</summary>
public class EnabledOpacity : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => v is true ? 1.0 : 0.45;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>IsLinked → brush turquoise ou bois gris</summary>
public class LinkedBrush : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v is true
            ? new SolidColorBrush(WpfColor.FromRgb(0x19, 0xB8, 0x8A)) // Turquoise
            : new SolidColorBrush(WpfColor.FromRgb(0xB8, 0xA8, 0x88)); // Bois gris
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>IsLinked → texte</summary>
public class LinkedText : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => v is true ? "Lié" : "Non lié";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>Current == this → bordure turquoise, sinon transparente.</summary>
public class CurrentBorder : IMultiValueConverter
{
    public object Convert(object[] v, Type t, object p, CultureInfo c)
        => v[0] != null && v[0] == v[1]
            ? new SolidColorBrush(WpfColor.FromRgb(0x19, 0xB8, 0x8A)) // Turquoise actif
            : new SolidColorBrush(WpfColor.FromRgb(0xB8, 0xA8, 0x88)); // Bois gris discret
    public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>BitmapImage null → Collapsed (pour fallback lettre initiale)</summary>
public class ImageToVis : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v != null ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class ImageToInvVis : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
        => v == null ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

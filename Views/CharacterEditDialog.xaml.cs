using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Altechap.Models;

using WpfColor = System.Windows.Media.Color;

namespace Altechap.Views;

public partial class CharacterEditDialog : Window
{
    private readonly Character _ch;
    private int _selectedClassId = 0;

    public CharacterEditDialog(Character ch)
    {
        _ch = ch;
        InitializeComponent();

        TxtName.Text       = ch.Name;
        TxtPattern.Text    = ch.MatchPattern;
        ChkRegex.IsChecked = ch.UseRegex;

        BuildClassGrid();

        if (ch.ClassId > 0)
            SelectClassId(ch.ClassId);

        TxtName.Focus();
        TxtName.SelectAll();
    }

    private void BuildClassGrid()
    {
        ClassGrid.Children.Clear();

        // Bouton "Aucune classe"
        AddClassButton(0, "(Aucune)", "#6C7086", null);

        foreach (var (id, name, color) in ClassDefs.All)
            AddClassButton(id, name, color, ClassDefs.GetIconPath(id));
    }

    private void AddClassButton(int id, string name, string colorHex, string? iconPath)
    {
        var btn = new Button
        {
            Width    = 76,
            Height   = 76,
            Margin   = new Thickness(3),
            Cursor   = Cursors.Hand,
            ToolTip  = name,
            Tag      = id,
            Template = BuildBtnTemplate(colorHex),
        };

        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        // Icône : PNG si dispo, sinon lettre initiale
        if (iconPath != null && File.Exists(iconPath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(iconPath, UriKind.Absolute);
                bmp.EndInit(); bmp.Freeze();
                sp.Children.Add(new System.Windows.Controls.Image
                {
                    Source = bmp, Width = 32, Height = 32,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }
            catch { AddTextIcon(sp, name); }
        }
        else
        {
            AddTextIcon(sp, name);
        }

        sp.Children.Add(new TextBlock
        {
            Text = name.Length > 10 ? name[..10] : name,
            FontSize = 9, Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        btn.Content = sp;
        btn.Click  += (_, _) => SelectClassId(id);
        ClassGrid.Children.Add(btn);
    }

    private static void AddTextIcon(StackPanel sp, string name)
    {
        sp.Children.Add(new TextBlock
        {
            Text = name.Length > 0 ? name[..1].ToUpper() : "?",
            FontSize = 24, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
    }

    private static ControlTemplate BuildBtnTemplate(string colorHex)
    {
        var tpl  = new ControlTemplate(typeof(Button));
        var fac  = new FrameworkElementFactory(typeof(Border));
        fac.SetValue(Border.CornerRadiusProperty,    new CornerRadius(10));
        fac.SetValue(Border.PaddingProperty,          new Thickness(4));
        fac.SetValue(Border.BackgroundProperty,       ParseBrush(colorHex, 0.28));
        fac.SetValue(Border.BorderBrushProperty,      ParseBrush(colorHex, 0.65));
        fac.SetValue(Border.BorderThicknessProperty,  new Thickness(1.5));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty,   VerticalAlignment.Center);
        fac.AppendChild(cp);
        tpl.VisualTree = fac;
        return tpl;
    }

    private static SolidColorBrush ParseBrush(string hex, double alpha)
    {
        try
        {
            hex = hex.TrimStart('#');
            byte r = Convert.ToByte(hex[..2], 16), g = Convert.ToByte(hex[2..4], 16),
                 b = Convert.ToByte(hex[4..6], 16), a = (byte)(alpha * 255);
            return new SolidColorBrush(WpfColor.FromArgb(a, r, g, b));
        }
        catch { return new SolidColorBrush(Colors.Gray); }
    }

    private void SelectClassId(int id)
    {
        _selectedClassId = id;

        // Aperçu
        var name  = id > 0 ? (ClassDefs.NameFromId(id) ?? "?") : "Aucune classe";
        var color = ClassDefs.GetColor(id);
        var path  = id > 0 ? ClassDefs.GetIconPath(id) : null;

        PreviewName.Text = name;

        // Icône preview
        if (path != null && File.Exists(path))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit(); bmp.Freeze();
                PreviewImage.Source   = bmp;
                PreviewImage.Visibility = Visibility.Visible;
                PreviewIcon.Visibility  = Visibility.Collapsed;
            }
            catch { ShowLetterPreview(name, color); }
        }
        else ShowLetterPreview(name, color);

        // Colorier badge
        PreviewBadge.Background = ParseBrush(color, 1.0);

        // Highlight bouton sélectionné
        foreach (Button btn in ClassGrid.Children)
            btn.Opacity = ((int)btn.Tag) == id ? 1.0 : 0.52;
    }

    private void ShowLetterPreview(string name, string color)
    {
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewIcon.Visibility  = Visibility.Visible;
        PreviewIcon.Text = name.Length > 0 ? name[..1].ToUpper() : "?";
        PreviewBadge.Background = ParseBrush(color, 1.0);
    }

    private void OK_Click(object s, RoutedEventArgs e)
    {
        var name = TxtName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            TxtName.BorderBrush = new SolidColorBrush(Colors.OrangeRed);
            TxtName.Focus(); return;
        }
        _ch.Name         = name;
        _ch.ClassId      = _selectedClassId;
        _ch.MatchPattern = TxtPattern.Text.Trim();
        _ch.UseRegex     = ChkRegex.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object s, RoutedEventArgs e) => Close();
}

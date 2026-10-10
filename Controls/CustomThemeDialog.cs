using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chassis_Master_Test_Suite.Core;
using Chassis_Master_Test_Suite.Localization;
using Chassis_Master_Test_Suite.Themes;

namespace Chassis_Master_Test_Suite.Controls;

/// <summary>Simple hex-color editor for Custom theme mode.</summary>
public static class CustomThemeDialog
{
    public static bool? Show(Window? owner, CustomThemeColors seed, out CustomThemeColors result)
    {
        result = seed;

        var accentBox = MakeBox(seed.Accent);
        var appBgBox = MakeBox(seed.AppBackground);
        var panelBox = MakeBox(seed.PanelBackground);

        var root = new StackPanel { Margin = new Thickness(16), Width = 360 };
        root.Children.Add(new TextBlock
        {
            Text = Loc.T("Theme.CustomTitle"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 12)
        });
        root.Children.Add(Labeled(Loc.T("Theme.Accent"), accentBox));
        root.Children.Add(Labeled(Loc.T("Theme.AppBackground"), appBgBox));
        root.Children.Add(Labeled(Loc.T("Theme.PanelBackground"), panelBox));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var reset = new Button { Content = Loc.T("Theme.ResetDefaults"), Height = 28, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 0, 10, 0) };
        var apply = new Button { Content = Loc.T("Theme.Apply"), Width = 80, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = Loc.T("Theme.Cancel"), Width = 80, Height = 28, IsCancel = true };
        buttons.Children.Add(reset);
        buttons.Children.Add(apply);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        var dlg = new Window
        {
            Title = Loc.T("Theme.CustomTitle"),
            Content = root,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            Owner = owner,
            Background = TryBrush("PanelBackground", Brushes.WhiteSmoke)
        };

        CustomThemeColors? chosen = null;
        reset.Click += (_, _) =>
        {
            var d = new CustomThemeColors();
            accentBox.Text = d.Accent;
            appBgBox.Text = d.AppBackground;
            panelBox.Text = d.PanelBackground;
        };
        apply.Click += (_, _) =>
        {
            chosen = new CustomThemeColors
            {
                Accent = NormalizeHex(accentBox.Text, seed.Accent),
                AppBackground = NormalizeHex(appBgBox.Text, seed.AppBackground),
                PanelBackground = NormalizeHex(panelBox.Text, seed.PanelBackground),
                CardBackground = seed.CardBackground,
                Border = seed.Border,
                TextPrimary = seed.TextPrimary,
                TextSecondary = seed.TextSecondary,
                TextMuted = seed.TextMuted
            };
            dlg.DialogResult = true;
            dlg.Close();
        };
        cancel.Click += (_, _) =>
        {
            dlg.DialogResult = false;
            dlg.Close();
        };

        var ok = dlg.ShowDialog();
        if (ok == true && chosen is not null)
        {
            result = chosen;
            return true;
        }

        return ok;
    }

    private static TextBox MakeBox(string text) =>
        new()
        {
            Text = text,
            Height = 28,
            Margin = new Thickness(0, 0, 0, 10),
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 0, 6, 0)
        };

    private static UIElement Labeled(string label, UIElement field)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        sp.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4), Opacity = 0.8 });
        sp.Children.Add(field);
        return sp;
    }

    private static string NormalizeHex(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        var s = raw.Trim();
        if (!s.StartsWith('#'))
            s = "#" + s;
        try
        {
            _ = (Color)ColorConverter.ConvertFromString(s);
            return s.ToUpperInvariant();
        }
        catch
        {
            return fallback;
        }
    }

    private static Brush TryBrush(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;
}

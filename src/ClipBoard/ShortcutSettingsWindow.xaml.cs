using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using ClipBoard.Core;

namespace ClipBoard;

public partial class ShortcutSettingsWindow : Window
{
    private readonly Func<ShortcutSettings, string?> apply;

    public ShortcutSettingsWindow(ShortcutSettings settings, Func<ShortcutSettings, string?> apply)
    {
        this.apply = apply;
        InitializeComponent();
        KeySelector.ItemsSource = Enumerable.Range(0x41, 26).Concat(Enumerable.Range(0x30, 10))
            .Concat(Enumerable.Range(0x70, 11))
            .Select(key => new KeyValuePair<string, uint>(key >= 0x70 ? $"F{key - 0x6F}" : ((char)key).ToString(), (uint)key))
            .ToArray();
        Populate(settings);
    }

    private void Populate(ShortcutSettings settings)
    {
        OverrideToggle.IsChecked = settings.OverrideWinV;
        ControlToggle.IsChecked = (settings.Shortcut.Modifiers & 2) != 0;
        AltToggle.IsChecked = (settings.Shortcut.Modifiers & 1) != 0;
        ShiftToggle.IsChecked = (settings.Shortcut.Modifiers & 4) != 0;
        KeySelector.SelectedValue = settings.Shortcut.VirtualKey;
        ErrorMessage.Visibility = Visibility.Collapsed;
    }

    private void Reset_Click(object sender, RoutedEventArgs args) => Populate(new ShortcutSettings());

    private void Save_Click(object sender, RoutedEventArgs args)
    {
        var modifiers = (AltToggle.IsChecked == true ? 1u : 0u)
            | (ControlToggle.IsChecked == true ? 2u : 0u)
            | (ShiftToggle.IsChecked == true ? 4u : 0u);
        var requested = new ShortcutSettings
        {
            Shortcut = new ShortcutGesture(modifiers, KeySelector.SelectedValue is uint key ? key : 0),
            OverrideWinV = OverrideToggle.IsChecked == true
        };
        var error = requested.Shortcut.IsValid ? apply(requested) : "Select at least one modifier and a key.";
        if (error is not null)
        {
            ErrorMessage.Text = error;
            ErrorMessage.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }
}
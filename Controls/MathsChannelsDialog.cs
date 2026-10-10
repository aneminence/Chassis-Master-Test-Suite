using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Chassis_Master_Test_Suite.Analysis;
using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Controls;

/// <summary>
/// VBTS-style Maths Channels editor: left list (+/delete), right formula with
/// Channels / Functions insert, Channel Name + Unit, OK / Cancel.
/// </summary>
public sealed class MathsChannelsDialog : Window
{
    private readonly ListBox _list;
    private readonly TextBox _formulaBox;
    private readonly TextBox _nameBox;
    private readonly TextBox _unitBox;
    private readonly TextBlock _status;
    private readonly List<Draft> _drafts = new();
    private bool _suppressSync;
    private Draft? _selected;

    public MathsChannelsDialog()
    {
        Title = "Maths Channels";
        Width = 720;
        Height = 480;
        MinWidth = 560;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush(0x12, 0x17, 0x1E);
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ---- Left: toolbar + list ----
        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var leftBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var addBtn = MakeToolButton("+", "Add maths channel");
        addBtn.Foreground = Brush(0x4C, 0xAF, 0x50);
        addBtn.FontWeight = FontWeights.Bold;
        addBtn.FontSize = 16;
        addBtn.Click += (_, _) => AddDraft();
        var delBtn = MakeToolButton("×", "Delete selected");
        delBtn.Foreground = Brush(0xE5, 0x73, 0x73);
        delBtn.FontWeight = FontWeights.Bold;
        delBtn.FontSize = 16;
        delBtn.Click += (_, _) => DeleteSelected();
        leftBar.Children.Add(addBtn);
        leftBar.Children.Add(delBtn);
        Grid.SetRow(leftBar, 0);
        left.Children.Add(leftBar);

        _list = new ListBox
        {
            Background = Brush(0x0E, 0x13, 0x1A),
            BorderBrush = Brush(0x1E, 0x25, 0x30),
            Foreground = Brush(0xE6, 0xEA, 0xF0),
            Padding = new Thickness(2)
        };
        _list.SelectionChanged += (_, _) => OnListSelectionChanged();
        Grid.SetRow(_list, 1);
        left.Children.Add(_list);
        Grid.SetColumn(left, 0);
        body.Children.Add(left);

        // ---- Right: formula toolbar + fields ----
        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var formulaBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6)
        };

        var channelsBtn = new Button
        {
            Content = "Channels ▾",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Background = Brush(0x1A, 0x22, 0x2E),
            Foreground = Brush(0xE6, 0xEA, 0xF0),
            BorderBrush = Brush(0x2A, 0x34, 0x44),
            ToolTip = "Insert a live channel Id into the formula"
        };
        channelsBtn.Click += ChannelsButton_Click;

        var functionsBtn = new Button
        {
            Content = "fx Functions ▾",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Background = Brush(0x1A, 0x22, 0x2E),
            Foreground = Brush(0xE6, 0xEA, 0xF0),
            BorderBrush = Brush(0x2A, 0x34, 0x44),
            ToolTip = "Insert operators / stubs"
        };
        functionsBtn.Click += FunctionsButton_Click;

        var accBtn = MakeToolButton("Σ Acc", "Accumulate (not yet supported)");
        accBtn.IsEnabled = false;
        accBtn.Opacity = 0.45;
        accBtn.Margin = new Thickness(0, 0, 6, 0);

        var intBtn = MakeToolButton("∫ Int", "Integrate (not yet supported)");
        intBtn.IsEnabled = false;
        intBtn.Opacity = 0.45;

        formulaBar.Children.Add(channelsBtn);
        formulaBar.Children.Add(functionsBtn);
        formulaBar.Children.Add(accBtn);
        formulaBar.Children.Add(intBtn);
        Grid.SetRow(formulaBar, 0);
        right.Children.Add(formulaBar);

        _formulaBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            Background = Brush(0x0E, 0x13, 0x1A),
            Foreground = Brush(0xE6, 0xEA, 0xF0),
            BorderBrush = Brush(0xC8, 0xA3, 0x4A),
            CaretBrush = Brush(0xC8, 0xA3, 0x4A),
            Padding = new Thickness(8),
            ToolTip = "Example: velocity * 1.16"
        };
        _formulaBox.TextChanged += (_, _) => PushFieldsToDraft();
        Grid.SetRow(_formulaBox, 1);
        right.Children.Add(_formulaBox);

        var meta = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        meta.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var nameLabel = new TextBlock
        {
            Text = "Channel Name",
            Foreground = Brush(0x8A, 0x94, 0xA6),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _nameBox = MakeFieldBox();
        _nameBox.TextChanged += (_, _) =>
        {
            PushFieldsToDraft();
            RefreshListItemTexts();
        };
        var unitLabel = new TextBlock
        {
            Text = "Unit",
            Foreground = Brush(0x8A, 0x94, 0xA6),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 8, 0)
        };
        _unitBox = MakeFieldBox();
        _unitBox.TextChanged += (_, _) =>
        {
            PushFieldsToDraft();
            RefreshListItemTexts();
        };
        Grid.SetColumn(nameLabel, 0);
        Grid.SetColumn(_nameBox, 1);
        Grid.SetColumn(unitLabel, 2);
        Grid.SetColumn(_unitBox, 3);
        meta.Children.Add(nameLabel);
        meta.Children.Add(_nameBox);
        meta.Children.Add(unitLabel);
        meta.Children.Add(_unitBox);
        Grid.SetRow(meta, 2);
        right.Children.Add(meta);

        var hint = new TextBlock
        {
            Text = "Insert channel Ids via Channels. Operators: +  -  *  /  ( ). Accumulate / Integrate coming later.",
            Foreground = Brush(0x5A, 0x64, 0x74),
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(hint, 3);
        right.Children.Add(hint);

        Grid.SetColumn(right, 2);
        body.Children.Add(right);
        Grid.SetRow(body, 0);
        root.Children.Add(body);

        _status = new TextBlock
        {
            Foreground = Brush(0xC8, 0xA3, 0x4A),
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(_status, 1);
        root.Children.Add(_status);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var ok = new Button
        {
            Content = "OK",
            Width = 88,
            Height = 28,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Background = Brush(0x1A, 0x22, 0x2E),
            Foreground = Brush(0xE6, 0xEA, 0xF0),
            BorderBrush = Brush(0x2A, 0x34, 0x44)
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Width = 88,
            Height = 28,
            IsCancel = true,
            Background = Brush(0x1A, 0x22, 0x2E),
            Foreground = Brush(0xE6, 0xEA, 0xF0),
            BorderBrush = Brush(0x2A, 0x34, 0x44)
        };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;

        LoadFromStore();
        SetEditorEnabled(_drafts.Count > 0);
        if (_drafts.Count > 0)
            _list.SelectedIndex = 0;
    }

    public static bool? Show(Window? owner)
    {
        var dlg = new MathsChannelsDialog();
        if (owner is not null)
            dlg.Owner = owner;
        return dlg.ShowDialog();
    }

    private void LoadFromStore()
    {
        _drafts.Clear();
        foreach (var d in MathsChannelStore.Instance.Definitions)
        {
            _drafts.Add(new Draft
            {
                Id = d.Id,
                DisplayName = string.IsNullOrWhiteSpace(d.DisplayName) ? d.Id : d.DisplayName,
                Unit = d.Unit ?? "",
                Expression = d.Expression
            });
        }

        RebuildList();
    }

    private void RebuildList()
    {
        _suppressSync = true;
        try
        {
            var keepId = _selected?.Id;
            _list.Items.Clear();
            foreach (var d in _drafts)
                _list.Items.Add(CreateListItem(d));

            if (keepId is not null)
            {
                foreach (ListBoxItem item in _list.Items)
                {
                    if (item.Tag is Draft draft &&
                        string.Equals(draft.Id, keepId, StringComparison.OrdinalIgnoreCase))
                    {
                        _list.SelectedItem = item;
                        return;
                    }
                }
            }
        }
        finally
        {
            _suppressSync = false;
        }
    }

    private ListBoxItem CreateListItem(Draft d)
    {
        var row = new Grid { Margin = new Thickness(2, 1, 2, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(d.DisplayName) ? d.Id : d.DisplayName,
            Foreground = Brush(0xE6, 0xEA, 0xF0),
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var unit = new TextBlock
        {
            Text = d.Unit ?? "",
            Foreground = Brush(0x8A, 0x94, 0xA6),
            FontSize = 12,
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(name, 0);
        Grid.SetColumn(unit, 1);
        row.Children.Add(name);
        row.Children.Add(unit);

        return new ListBoxItem
        {
            Content = row,
            Tag = d,
            Padding = new Thickness(6, 5, 6, 5)
        };
    }

    private void RefreshListItemTexts()
    {
        foreach (ListBoxItem item in _list.Items)
        {
            if (item.Tag is not Draft d || item.Content is not Grid row)
                continue;
            if (row.Children[0] is TextBlock name)
                name.Text = string.IsNullOrWhiteSpace(d.DisplayName) ? d.Id : d.DisplayName;
            if (row.Children[1] is TextBlock unit)
                unit.Text = d.Unit ?? "";
        }
    }

    private void AddDraft()
    {
        PushFieldsToDraft();
        var id = MathsChannelStore.AllocateUniqueId(
            "Maths",
            _drafts.Select(d => d.Id));
        var draft = new Draft
        {
            Id = id,
            DisplayName = id,
            Unit = "",
            Expression = ""
        };
        _drafts.Add(draft);
        RebuildList();
        foreach (ListBoxItem item in _list.Items)
        {
            if (item.Tag is Draft d && ReferenceEquals(d, draft))
            {
                _list.SelectedItem = item;
                break;
            }
        }

        SetEditorEnabled(true);
        _formulaBox.Focus();
        _status.Text = "";
    }

    private void DeleteSelected()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: Draft draft })
            return;

        var idx = _drafts.IndexOf(draft);
        if (idx < 0)
            return;
        _drafts.RemoveAt(idx);
        _selected = null;
        RebuildList();
        if (_drafts.Count == 0)
        {
            SetEditorEnabled(false);
            _formulaBox.Text = "";
            _nameBox.Text = "";
            _unitBox.Text = "";
        }
        else
        {
            _list.SelectedIndex = Math.Min(idx, _drafts.Count - 1);
        }

        _status.Text = "";
    }

    private void OnListSelectionChanged()
    {
        if (_suppressSync)
            return;

        PushFieldsToDraft();

        if (_list.SelectedItem is not ListBoxItem { Tag: Draft draft })
        {
            _selected = null;
            SetEditorEnabled(false);
            return;
        }

        _selected = draft;
        SetEditorEnabled(true);
        _suppressSync = true;
        try
        {
            _nameBox.Text = draft.DisplayName;
            _unitBox.Text = draft.Unit;
            _formulaBox.Text = draft.Expression;
        }
        finally
        {
            _suppressSync = false;
        }
    }

    private void PushFieldsToDraft()
    {
        if (_suppressSync || _selected is null)
            return;

        _selected.DisplayName = _nameBox.Text.Trim();
        _selected.Unit = _unitBox.Text.Trim();
        _selected.Expression = _formulaBox.Text.Trim();
    }

    private void SetEditorEnabled(bool enabled)
    {
        _formulaBox.IsEnabled = enabled;
        _nameBox.IsEnabled = enabled;
        _unitBox.IsEnabled = enabled;
    }

    private void ChannelsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            _status.Text = "Add or select a maths channel first.";
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = sender as UIElement,
            Placement = PlacementMode.Bottom,
            Background = Brush(0x1A, 0x22, 0x2E),
            BorderBrush = Brush(0x2A, 0x34, 0x44)
        };

        foreach (var ch in ChannelRegistry.Instance.AvailablePlotChannels)
        {
            // Skip other maths channels in the insert list to avoid self-reference loops in UI;
            // they can still be typed manually if needed.
            if (_drafts.Any(d => string.Equals(d.Id, ch.Id, StringComparison.OrdinalIgnoreCase)))
                continue;

            var label = string.IsNullOrEmpty(ch.Unit)
                ? $"{ch.DisplayName}  [{ch.Id}]"
                : $"{ch.DisplayName} ({ch.Unit})  [{ch.Id}]";
            var item = new MenuItem
            {
                Header = label,
                Tag = ch.Id,
                Foreground = Brush(0xE6, 0xEA, 0xF0)
            };
            item.Click += (_, _) =>
            {
                if (item.Tag is string id)
                    InsertAtCaret(id);
            };
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
        {
            menu.Items.Add(new MenuItem
            {
                Header = "(no live channels)",
                IsEnabled = false,
                Foreground = Brush(0x5A, 0x64, 0x74)
            });
        }

        menu.IsOpen = true;
    }

    private void FunctionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
        {
            _status.Text = "Add or select a maths channel first.";
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = sender as UIElement,
            Placement = PlacementMode.Bottom,
            Background = Brush(0x1A, 0x22, 0x2E),
            BorderBrush = Brush(0x2A, 0x34, 0x44)
        };

        void AddInsert(string header, string insert)
        {
            var item = new MenuItem
            {
                Header = header,
                Foreground = Brush(0xE6, 0xEA, 0xF0)
            };
            item.Click += (_, _) => InsertAtCaret(insert);
            menu.Items.Add(item);
        }

        void AddDisabled(string header)
        {
            menu.Items.Add(new MenuItem
            {
                Header = header,
                IsEnabled = false,
                Foreground = Brush(0x5A, 0x64, 0x74)
            });
        }

        AddInsert("+  Add", " + ");
        AddInsert("-  Subtract", " - ");
        AddInsert("*  Multiply", " * ");
        AddInsert("/  Divide", " / ");
        AddInsert("( )  Parentheses", "()");
        menu.Items.Add(new Separator());
        AddDisabled("abs(x)  — coming later");
        AddDisabled("sqrt(x) — coming later");
        AddDisabled("Accumulate — coming later");
        AddDisabled("Integrate — coming later");

        menu.IsOpen = true;
    }

    private void InsertAtCaret(string text)
    {
        if (!_formulaBox.IsEnabled)
            return;

        var caret = _formulaBox.CaretIndex;
        if (caret < 0 || caret > _formulaBox.Text.Length)
            caret = _formulaBox.Text.Length;

        // If inserting "()", place caret between.
        if (text == "()")
        {
            _formulaBox.Text = _formulaBox.Text.Insert(caret, "()");
            _formulaBox.CaretIndex = caret + 1;
        }
        else
        {
            _formulaBox.Text = _formulaBox.Text.Insert(caret, text);
            _formulaBox.CaretIndex = caret + text.Length;
        }

        _formulaBox.Focus();
        PushFieldsToDraft();
    }

    private void Accept()
    {
        PushFieldsToDraft();

        // Refresh Ids from display names for brand-new drafts that still use placeholder Ids,
        // but keep existing Ids stable so plots/dashboard bindings survive renames.
        var existingStoreIds = new HashSet<string>(
            MathsChannelStore.Instance.Definitions.Select(d => d.Id),
            StringComparer.OrdinalIgnoreCase);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defs = new List<MathsChannelDefinition>();

        foreach (var draft in _drafts)
        {
            var name = string.IsNullOrWhiteSpace(draft.DisplayName) ? draft.Id : draft.DisplayName.Trim();
            string id;
            if (existingStoreIds.Contains(draft.Id) || used.Contains(draft.Id))
            {
                // Keep stable Id for channels that already existed in the store.
                id = draft.Id;
                if (!used.Add(id))
                {
                    id = MathsChannelStore.AllocateUniqueId(name, used);
                    used.Add(id);
                }
            }
            else
            {
                id = MathsChannelStore.AllocateUniqueId(name, used);
                used.Add(id);
            }

            if (string.IsNullOrWhiteSpace(draft.Expression))
            {
                _status.Text = $"'{name}' needs a formula.";
                SelectDraft(draft);
                return;
            }

            if (!MathsExpression.TryEvaluate(draft.Expression, _ => 0, out _, out var evalError))
            {
                _status.Text = $"'{name}': {evalError}";
                SelectDraft(draft);
                return;
            }

            defs.Add(new MathsChannelDefinition
            {
                Id = id,
                DisplayName = name,
                Unit = draft.Unit?.Trim() ?? "",
                Expression = draft.Expression.Trim()
            });
        }

        if (!MathsChannelStore.Instance.TryReplaceAll(defs, out var error))
        {
            _status.Text = error;
            return;
        }

        DialogResult = true;
        Close();
    }

    private void SelectDraft(Draft draft)
    {
        foreach (ListBoxItem item in _list.Items)
        {
            if (item.Tag is Draft d && ReferenceEquals(d, draft))
            {
                _list.SelectedItem = item;
                break;
            }
        }
    }

    private static Button MakeToolButton(string content, string tip) =>
        new()
        {
            Content = content,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Background = Brush(0x1A, 0x22, 0x2E),
            Foreground = Brush(0xE6, 0xEA, 0xF0),
            BorderBrush = Brush(0x2A, 0x34, 0x44),
            ToolTip = tip
        };

    private static TextBox MakeFieldBox() =>
        new()
        {
            Height = 28,
            Background = Brush(0x0E, 0x13, 0x1A),
            Foreground = Brush(0xE6, 0xEA, 0xF0),
            BorderBrush = Brush(0x1E, 0x25, 0x30),
            CaretBrush = Brush(0xC8, 0xA3, 0x4A),
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 0, 6, 0)
        };

    private static SolidColorBrush Brush(byte r, byte g, byte b) =>
        new(Color.FromRgb(r, g, b));

    private sealed class Draft
    {
        public required string Id { get; set; }
        public string DisplayName { get; set; } = "";
        public string Unit { get; set; } = "";
        public string Expression { get; set; } = "";
    }
}

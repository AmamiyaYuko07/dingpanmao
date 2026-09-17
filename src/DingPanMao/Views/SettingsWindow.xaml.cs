using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DingPanMao.Config;
using DingPanMao.Models;
using DingPanMao.Services;

namespace DingPanMao.Views;

/// <summary>设置窗口。所有改动先写在副本上，点保存才回写到真实配置。</summary>
public partial class SettingsWindow : Window
{
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x9A, 0xA5, 0xB4));

    private readonly AppSettings _original;

    private readonly AppSettings _draft;

    private readonly MarketDataService _market;

    private readonly StackPanel _slotsPanel = new();

    public SettingsWindow(AppSettings settings, MarketDataService market)
    {
        _original = settings;
        _market = market;
        _draft = Clone(settings);

        InitializeComponent();
        Title = Lang.T("settings.title");
        SaveButton.Content = Lang.T("common.save");
        CancelButton.Content = Lang.T("common.cancel");

        BuildContent();

        // 固定显示在主屏靠下的位置，避免跑到副屏或者挡住任务栏。
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2);
        Top = Math.Max(40, SystemParameters.PrimaryScreenHeight - Height - 120);
    }

    public event Action? Saved;

    private void BuildContent()
    {
        Body.Children.Clear();
        BuildSlotsSection();
        BuildLanguageSection();
        BuildAppearanceSection();
        BuildBehaviorSection();
        BuildAlertSection();
        BuildAiSection();
    }

    private void BuildSlotsSection()
    {
        Body.Children.Add(SectionHeader("settings.symbols"));
        Body.Children.Add(_slotsPanel);
        RenderSlots();

        var add = new Button
        {
            Content = "＋ " + Lang.T("search.placeholder"),
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(10, 5, 10, 5),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        add.Click += OnAddSlotClick;
        Body.Children.Add(add);
    }

    private void RenderSlots()
    {
        _slotsPanel.Children.Clear();

        for (var i = 0; i < _draft.Slots.Count; i++)
        {
            var index = i;
            var slot = _draft.Slots[i];

            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock
            {
                Text = slot.Symbol.NameFor(Lang.CurrentLanguage) + "   " + slot.Symbol.Code,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(name, 0);
            row.Children.Add(name);

            var sizeBox = new ComboBox { Width = 110, Margin = new Thickness(8, 0, 8, 0) };
            sizeBox.Items.Add(new ComboBoxItem { Content = Lang.T("settings.size.small"), Tag = TileSize.Small });
            sizeBox.Items.Add(new ComboBoxItem { Content = Lang.T("settings.size.medium"), Tag = TileSize.Medium });
            sizeBox.Items.Add(new ComboBoxItem { Content = Lang.T("settings.size.large"), Tag = TileSize.Large });
            sizeBox.SelectedIndex = slot.Size switch
            {
                TileSize.Small => 0,
                TileSize.Medium => 1,
                _ => 2,
            };
            sizeBox.SelectionChanged += (_, _) =>
            {
                if (sizeBox.SelectedItem is ComboBoxItem { Tag: TileSize size })
                {
                    _draft.Slots[index].Size = size;
                }
            };
            Grid.SetColumn(sizeBox, 1);
            row.Children.Add(sizeBox);

            var remove = new Button { Content = "✕", Width = 32, Padding = new Thickness(0) };
            remove.IsEnabled = _draft.Slots.Count > AppConfig.MinSlots;
            remove.Click += (_, _) =>
            {
                _draft.Slots.RemoveAt(index);
                RenderSlots();
            };
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);

            _slotsPanel.Children.Add(row);
        }
    }

    private void OnAddSlotClick(object sender, RoutedEventArgs e)
    {
        if (_draft.Slots.Count >= AppConfig.MaxSlots)
        {
            return;
        }

        var picker = new SymbolPickerWindow(_market) { Owner = this };
        if (picker.ShowDialog() == true && picker.Selected is not null)
        {
            _draft.Slots.Add(new SlotSettings { Symbol = picker.Selected, Size = TileSize.Large });
            RenderSlots();
        }
    }

    private void BuildLanguageSection()
    {
        Body.Children.Add(SectionHeader("settings.language"));

        var box = new ComboBox { Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var language in Lang.Languages)
        {
            box.Items.Add(new ComboBoxItem { Content = language.NativeName, Tag = language.Code });
        }

        box.SelectedIndex = Math.Max(0, Lang.Languages.ToList().FindIndex(l => l.Code == _draft.Language));
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem { Tag: string code })
            {
                _draft.Language = code;
                Lang.SetLanguage(code);
                Title = Lang.T("settings.title");
                SaveButton.Content = Lang.T("common.save");
                CancelButton.Content = Lang.T("common.cancel");
                BuildContent();
            }
        };

        Body.Children.Add(box);
    }

    private void BuildAppearanceSection()
    {
        Body.Children.Add(SectionHeader("settings.appearance"));

        var colorBox = new ComboBox { Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
        colorBox.Items.Add(new ComboBoxItem { Content = Lang.T("settings.color.redUp"), Tag = true });
        colorBox.Items.Add(new ComboBoxItem { Content = Lang.T("settings.color.greenUp"), Tag = false });
        colorBox.SelectedIndex = _draft.RedUpGreenDown ? 0 : 1;
        colorBox.SelectionChanged += (_, _) =>
        {
            if (colorBox.SelectedItem is ComboBoxItem { Tag: bool redUp })
            {
                _draft.RedUpGreenDown = redUp;
            }
        };
        Body.Children.Add(LabeledRow("settings.color", colorBox));

        Body.Children.Add(LabeledSlider("settings.opacity", 0.2, 1.0, _draft.Opacity, v => _draft.Opacity = v, "P0"));
        Body.Children.Add(LabeledSlider("settings.fontScale", 0.8, 1.4, _draft.FontScale, v => _draft.FontScale = v, "P0"));
    }

    private void BuildBehaviorSection()
    {
        Body.Children.Add(SectionHeader("settings.general"));

        Body.Children.Add(CheckBoxRow("settings.detailWindow", _draft.OpenDetailOnClick, v => _draft.OpenDetailOnClick = v));
        Body.Children.Add(CheckBoxRow("settings.autoStart", _draft.AutoStart, v => _draft.AutoStart = v));
        Body.Children.Add(LabeledSlider("settings.interval", 3, 60, _draft.RefreshSeconds, v => _draft.RefreshSeconds = (int)Math.Round(v), "F0"));
    }

    private void BuildAlertSection()
    {
        Body.Children.Add(SectionHeader("settings.alerts"));

        Body.Children.Add(CheckBoxRow("settings.reversal", _draft.ReversalAlerts, v => _draft.ReversalAlerts = v));
        Body.Children.Add(CheckBoxRow("settings.breakout", _draft.BreakoutAlerts, v => _draft.BreakoutAlerts = v));
        Body.Children.Add(CheckBoxRow("settings.levels", _draft.LevelAlerts, v => _draft.LevelAlerts = v));
        Body.Children.Add(LabeledSlider("settings.sensitivity", 0.5, 2.0, _draft.Sensitivity, v => _draft.Sensitivity = v, "F1"));
    }

    private void BuildAiSection()
    {
        Body.Children.Add(SectionHeader("settings.ai"));

        Body.Children.Add(CheckBoxRow("settings.ai", _draft.AiEnabled, v => _draft.AiEnabled = v));

        var keyBox = new PasswordBox
        {
            Password = _draft.AiApiKey,
            Padding = new Thickness(6, 4, 6, 4),
            Background = new SolidColorBrush(Color.FromRgb(0x23, 0x26, 0x2E)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE9, 0xEE, 0xF5)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        };
        keyBox.PasswordChanged += (_, _) => _draft.AiApiKey = keyBox.Password;
        Body.Children.Add(LabeledRow("ai.apiKey", keyBox));

        var warning = new TextBlock
        {
            Text = Lang.T("ai.keyWarning"),
            FontSize = 11,
            Foreground = Muted,
            Margin = new Thickness(180, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        };
        Body.Children.Add(warning);

        Body.Children.Add(LabeledRow("ai.baseUrl", TextRow(_draft.AiBaseUrl, v => _draft.AiBaseUrl = v)));
        Body.Children.Add(LabeledRow("ai.model", TextRow(_draft.AiModel, v => _draft.AiModel = v)));

        Body.Children.Add(CheckBoxRow("ai.webSearch", _draft.AiWebSearch, v => _draft.AiWebSearch = v));
        Body.Children.Add(new TextBlock
        {
            Text = Lang.T("ai.webSearchNote"),
            FontSize = 11,
            Foreground = Muted,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        });

        var promptBox = new TextBox
        {
            Text = _draft.AiCustomPrompt,
            MinHeight = 70,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(6, 4, 6, 4),
            Background = new SolidColorBrush(Color.FromRgb(0x23, 0x26, 0x2E)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE9, 0xEE, 0xF5)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        promptBox.TextChanged += (_, _) => _draft.AiCustomPrompt = promptBox.Text;
        Body.Children.Add(LabeledRow("ai.prompt", promptBox));
    }

    private static TextBox TextRow(string value, Action<string> apply)
    {
        var box = new TextBox
        {
            Text = value,
            Padding = new Thickness(6, 4, 6, 4),
            Background = new SolidColorBrush(Color.FromRgb(0x23, 0x26, 0x2E)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE9, 0xEE, 0xF5)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        };
        box.TextChanged += (_, _) => apply(box.Text);
        return box;
    }

    private TextBlock SectionHeader(string key) => new()
    {
        Text = Lang.T(key),
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 16, 0, 8),
    };

    private Grid LabeledRow(string labelKey, UIElement control)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = Lang.T(labelKey),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        Grid.SetColumn((UIElement)control, 1);
        row.Children.Add((UIElement)control);

        return row;
    }

    private UIElement LabeledSlider(
        string labelKey,
        double min,
        double max,
        double value,
        Action<double> apply,
        string format)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var readout = new TextBlock
        {
            Text = value.ToString(format),
            Width = 48,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = Muted,
            VerticalAlignment = VerticalAlignment.Center,
        };

        slider.ValueChanged += (_, args) =>
        {
            apply(args.NewValue);
            readout.Text = args.NewValue.ToString(format);
        };

        var host = new Grid();
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(slider, 0);
        Grid.SetColumn(readout, 1);
        host.Children.Add(slider);
        host.Children.Add(readout);

        return LabeledRow(labelKey, host);
    }

    private UIElement CheckBoxRow(string labelKey, bool value, Action<bool> apply)
    {
        var box = new CheckBox
        {
            Content = Lang.T(labelKey),
            IsChecked = value,
            Margin = new Thickness(0, 4, 0, 4),
        };
        box.Checked += (_, _) => apply(true);
        box.Unchecked += (_, _) => apply(false);
        return box;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _original.Slots = _draft.Slots;
        _original.Language = _draft.Language;
        _original.RedUpGreenDown = _draft.RedUpGreenDown;
        _original.Opacity = _draft.Opacity;
        _original.FontScale = _draft.FontScale;
        _original.AutoStart = _draft.AutoStart;
        _original.OpenDetailOnClick = _draft.OpenDetailOnClick;
        _original.RefreshSeconds = _draft.RefreshSeconds;
        _original.ReversalAlerts = _draft.ReversalAlerts;
        _original.BreakoutAlerts = _draft.BreakoutAlerts;
        _original.LevelAlerts = _draft.LevelAlerts;
        _original.Sensitivity = _draft.Sensitivity;
        _original.AiEnabled = _draft.AiEnabled;
        _original.AiApiKey = _draft.AiApiKey;
        _original.AiBaseUrl = _draft.AiBaseUrl;
        _original.AiModel = _draft.AiModel;
        _original.AiWebSearch = _draft.AiWebSearch;
        _original.AiCustomPrompt = _draft.AiCustomPrompt;

        Saved?.Invoke();
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Lang.SetLanguage(_original.Language);
        Close();
    }

    private static AppSettings Clone(AppSettings source)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(source);
        return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json) ?? SettingsStore.CreateDefault();
    }
}

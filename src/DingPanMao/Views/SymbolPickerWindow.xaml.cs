using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DingPanMao.Models;
using DingPanMao.Services;

namespace DingPanMao.Views;

/// <summary>品种搜索窗口。可以搜任意品种，也可以直接从内置列表里挑。</summary>
public partial class SymbolPickerWindow : Window
{
    private readonly MarketDataService _market;

    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(400) };

    private CancellationTokenSource? _searchCts;

    public SymbolPickerWindow(MarketDataService market)
    {
        _market = market;
        InitializeComponent();

        Title = Lang.T("search.placeholder");
        OkButton.Content = Lang.T("common.save");
        CancelButton.Content = Lang.T("common.cancel");
        SearchBox.ToolTip = Lang.T("search.placeholder");
        SearchBox.Text = string.Empty;

        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await SearchAsync();
        };

        SearchBox.TextChanged += (_, _) =>
        {
            _debounce.Stop();
            _debounce.Start();
        };

        ResultList.MouseDoubleClick += (_, _) => Confirm();

        Loaded += (_, _) =>
        {
            ShowBuiltIn();
            SearchBox.Focus();
        };
    }

    public SymbolDefinition? Selected { get; private set; }

    private void ShowBuiltIn()
    {
        ResultList.ItemsSource = SymbolCatalog.BuiltIn;
        StatusText.Text = string.Empty;
    }

    private async Task SearchAsync()
    {
        var keyword = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            ShowBuiltIn();
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        StatusText.Text = Lang.T("search.searching");

        try
        {
            var results = await _market.SearchAsync(keyword, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            ResultList.ItemsSource = results;
            StatusText.Text = results.Count == 0 ? Lang.T("search.empty") : string.Empty;
        }
        catch (OperationCanceledException)
        {
            // 输入变化导致这次搜索作废。
        }
        catch
        {
            StatusText.Text = Lang.T("search.empty");
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        if (ResultList.SelectedItem is SymbolDefinition symbol)
        {
            Selected = symbol;
            DialogResult = true;
            Close();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Enter)
        {
            Confirm();
        }
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ObservableCollections;
using STranslate.Core;
using STranslate.Helpers;
using STranslate.Plugin;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;

namespace STranslate.ViewModels.Pages;

public partial class HistoryViewModel : ObservableObject, IDisposable
{
    private const int PageSize = 20;
    private const int searchDelayMilliseconds = 500;

    private readonly SqlService _sqlService;
    private readonly ISnackbar _snackbar;
    private readonly Internationalization _i18n;
    private readonly DebounceExecutor _searchDebouncer;

    private CancellationTokenSource? _searchCts;
    private DateTime _lastCursorTime = DateTime.Now;
    private bool _isLoading = false;

    private bool CanLoadMore =>
        !_isLoading &&
        string.IsNullOrEmpty(SearchText) &&
        (TotalCount == 0 || _items.Count != TotalCount);

    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// <see href="https://blog.coldwind.top/posts/more-observable-collections/"/>
    /// </summary>
    private readonly ObservableList<HistoryListItem> _items = [];

    public INotifyCollectionChangedSynchronizedViewList<HistoryListItem> HistoryItems { get; }

    [ObservableProperty] public partial HistoryListItem? SelectedListItem { get; set; }

    [ObservableProperty] public partial HistoryModel? SelectedItem { get; set; }

    [ObservableProperty] public partial long TotalCount { get; set; }

    [ObservableProperty] public partial int ExportSelectedCount { get; set; }

    [ObservableProperty] public partial bool CanExportHistory { get; set; }

    public HistoryViewModel(
        SqlService sqlService,
        ISnackbar snackbar,
        Internationalization i18n)
    {
        _sqlService = sqlService;
        _snackbar = snackbar;
        _i18n = i18n;
        _searchDebouncer = new();

        HistoryItems = _items.ToNotifyCollectionChanged();

        _ = RefreshAsync();
    }

    partial void OnSelectedListItemChanged(HistoryListItem? value) => SelectedItem = value?.Model;

    // 搜索文本变化时修改定时器
    partial void OnSearchTextChanged(string value) =>
        _searchDebouncer.ExecuteAsync(SearchAsync, TimeSpan.FromMilliseconds(searchDelayMilliseconds));

    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();

        if (string.IsNullOrEmpty(SearchText))
        {
            await RefreshAsync();
            return;
        }

        var historyItems = await _sqlService.GetDataAsync(SearchText, _searchCts.Token);

        App.Current.Dispatcher.Invoke(() =>
        {
            SelectedListItem = null;
            SelectedItem = null;
            ClearItems();
            if (historyItems == null) return;

            AddItems(historyItems);
        });
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        TotalCount = await _sqlService.GetCountAsync();

        App.Current.Dispatcher.Invoke(() =>
        {
            SelectedListItem = null;
            SelectedItem = null;
            ClearItems();
        });
        _lastCursorTime = DateTime.Now;

        await LoadMoreAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(HistoryModel historyModel)
    {
        var success = await _sqlService.DeleteDataAsync(historyModel);
        if (success)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var item = _items.FirstOrDefault(i => i.Model.Id == historyModel.Id);
                if (item != null)
                    RemoveItem(item);
                if (SelectedItem?.Id == historyModel.Id)
                {
                    SelectedListItem = null;
                    SelectedItem = null;
                }
            });
            TotalCount--;
        }
        else
            _snackbar.ShowError(_i18n.GetTranslation("OperationFailed"));
    }

    [RelayCommand]
    private void Copy(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        ClipboardHelper.SetText(text);
        _snackbar.ShowSuccess(_i18n.GetTranslation("CopySuccess"));
    }

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private async Task LoadMoreAsync()
    {
        try
        {
            _isLoading = true;

            var historyData = await _sqlService.GetDataCursorPagedAsync(PageSize, _lastCursorTime);
            if (!historyData.Any()) return;

            App.Current.Dispatcher.Invoke(() =>
            {
                // 更新游标
                _lastCursorTime = historyData.Last().Time;
                var uniqueHistoryItems = historyData.Where(h => !_items.Any(existing => existing.Model.Id == h.Id));
                AddItems(uniqueHistoryItems);
            });
        }
        finally
        {
            _isLoading = false;
            LoadMoreCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void ToggleSelectAll()
    {
        if (_items.Count == 0) return;

        var shouldSelectAll = _items.Any(i => !i.IsExportSelected);
        foreach (var item in _items)
            item.IsExportSelected = shouldSelectAll;

        UpdateExportSelectionState();
    }

    [RelayCommand]
    private async Task ExportHistoryAsync()
    {
        var selected = _items
            .Where(i => i.IsExportSelected)
            .Select(i => i.Model)
            .ToList();

        if (selected.Count == 0)
        {
            _snackbar.Show(
                _i18n.GetTranslation("NoHistorySelected"),
                Severity.Warning,
                actionText: _i18n.GetTranslation("SelectAll"),
                actionCallback: ToggleSelectAll);
            return;
        }

        var saveFileDialog = new SaveFileDialog
        {
            Title = _i18n.GetTranslation("SaveAs"),
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            FileName = $"stranslate_history_{DateTime.Now:yyyyMMddHHmmss}.json",
            DefaultDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            AddToRecent = true
        };

        if (saveFileDialog.ShowDialog() != true)
            return;

        try
        {
            var export = new
            {
                app = Constant.AppName,
                exportedAt = DateTimeOffset.Now,
                count = selected.Count,
                items = selected.Select(h => new
                {
                    id = h.Id,
                    time = h.Time,
                    sourceLang = h.SourceLang,
                    targetLang = h.TargetLang,
                    sourceText = h.SourceText,
                    favorite = h.Favorite,
                    remark = h.Remark,
                    data = h.Data
                })
            };

            var json = JsonSerializer.Serialize(export, HistoryModel.JsonOption);
            await File.WriteAllTextAsync(saveFileDialog.FileName, json, Encoding.UTF8);

            var directory = Path.GetDirectoryName(saveFileDialog.FileName);
            _snackbar.ShowSuccess(_i18n.GetTranslation("ExportSuccess"));

            foreach (var item in _items)
                item.IsExportSelected = false;
            UpdateExportSelectionState();
        }
        catch (Exception ex)
        {
            _snackbar.ShowError($"{_i18n.GetTranslation("ExportFailed")}: {ex.Message}");
        }
    }

    private void AddItems(IEnumerable<HistoryModel> models)
    {
        var listItems = models.Select(m => new HistoryListItem(m)).ToList();
        foreach (var item in listItems)
            item.PropertyChanged += OnHistoryListItemPropertyChanged;
        _items.AddRange(listItems);
        UpdateExportSelectionState();
    }

    private void RemoveItem(HistoryListItem item)
    {
        item.PropertyChanged -= OnHistoryListItemPropertyChanged;
        _items.Remove(item);
        UpdateExportSelectionState();
    }

    private void ClearItems()
    {
        foreach (var item in _items)
            item.PropertyChanged -= OnHistoryListItemPropertyChanged;
        _items.Clear();
        UpdateExportSelectionState();
    }

    private void OnHistoryListItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryListItem.IsExportSelected))
            UpdateExportSelectionState();
    }

    private void UpdateExportSelectionState()
    {
        ExportSelectedCount = _items.Count(i => i.IsExportSelected);
        CanExportHistory = ExportSelectedCount > 0;
    }

    public void Dispose()
    {
        _searchDebouncer.Dispose();
        _searchCts?.Dispose();
    }
}

public partial class HistoryListItem : ObservableObject
{
    public HistoryModel Model { get; }

    [ObservableProperty] public partial bool IsExportSelected { get; set; }

    public HistoryListItem(HistoryModel model) => Model = model;
}

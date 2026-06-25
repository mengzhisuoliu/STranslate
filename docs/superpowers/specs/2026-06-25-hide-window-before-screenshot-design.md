# 设计：再次触发截图前主动隐藏已开窗口

**日期**: 2026-06-25
**分支**: feature/imagetranslate
**状态**: 已批准，待实现

## 背景

图片翻译独立窗口、OCR 窗口在已打开状态下，通过全局热键**再次触发**截图时，当前行为不一致且体验不佳：

| 路径 | 当前行为 | 问题 |
|---|---|---|
| 全局热键二次触发图片翻译 (`MainWindowViewModel.ImageTranslateAsync:1114`) | `CloseImageTranslateWindows()` 关闭已开窗口后截图 | Close/重建窗口较慢，窗口位置/状态丢失 |
| 全局热键二次触发 OCR (`MainWindowViewModel.OcrAsync:1165`) | **不处理**已开的 OcrWindow，直接截图 | OCR 窗口在截图选区时仍显示在屏幕上，遮挡选区 |
| 全局热键二次触发 QrCode (`MainWindowViewModel.QrCodeAsync:1182`) | **不处理**已开的 OcrWindow，直接截图 | 同 OCR，窗口遮挡选区 |
| OCR 窗口内"重新截图"按钮 (`OcrWindowViewModel.OcrAsync:213`) | `window?.Hide()` → 延迟 150ms → 截图 → `window?.Show()` | ✅ 已是理想的 Hide/Show 模式 |

参考实现（OCR 窗口内按钮）已证明 Hide/Show 模式更快且保留窗口状态。目标是将所有全局热键二次触发路径统一为该模式。

## 目标

- 全局热键二次触发图片翻译 / OCR / QrCode 时，截图前主动 `Hide()` 已开的对应窗口，截图后复用同一窗口实例 `Show()` 新结果。
- 不重建窗口对象（避免 Close/重建的开销），保留窗口位置与状态。
- 失败路径（服务未配置）恢复窗口显示，避免窗口卡在隐藏状态。

## 关键事实

1. **`SingletonWindowOpener.OpenAsync<T>` 复用窗口实例**（`Helpers/SingletonWindowOpener.cs:30`）：通过 `Application.Current.Windows.OfType<T>().FirstOrDefault()` 查找已存在窗口，找到则 `Activate`（内含 `Show()`），否则新建。因此 Hide 后再 OpenAsync 会拿到**同一个**窗口并 Show 它，不会重建。
2. **`Screenshot` 已内置 150ms 延迟**（`Core/Screenshot.cs:9` `DefaultCaptureDelayMs`，用于 `:36` 和 `:63`），目的是"隐藏窗口/主窗口后等 UI 刷新再截图"。全局热键路径无需额外加 Delay，复用现有延迟即可。
3. **重复 Hide 幂等**：OCR 窗口内按钮路径（`OcrWindowViewModel.OcrAsync`）会先 Hide 窗口再调用 `MainWindowViewModel.OcrCommand`。改造后 `OcrCommand` 内部也会查找并 Hide 同一窗口——已隐藏的窗口再 Hide 无害，安全。

## 改动设计

改动集中在一个文件：`src/STranslate/ViewModels/MainWindowViewModel.cs`。

### 改动 1：`OcrAsync`（`:1165`）

截图前查找并 Hide 已开的 `OcrWindow`；服务校验失败时恢复 Show。

```csharp
[RelayCommand]
private async Task OcrAsync()
{
    var existingWindow = Application.Current.Windows.OfType<OcrWindow>().FirstOrDefault();
    existingWindow?.Hide();

    if (GetOcrSvcAndNotify() == null)
    {
        existingWindow?.Show();
        return;
    }

    using var bitmap = await _screenshot.GetScreenshotAsync();
    await OcrHandlerAsync(bitmap);
}
```

### 改动 2：`QrCodeAsync`（`:1182`）

与 OCR 对称。QrCode 复用 `OcrWindow`，故查找同一类型。

```csharp
[RelayCommand]
private async Task QrCodeAsync()
{
    var existingWindow = Application.Current.Windows.OfType<OcrWindow>().FirstOrDefault();
    existingWindow?.Hide();

    if (GetOcrSvcAndNotify() == null)
    {
        existingWindow?.Show();
        return;
    }

    using var bitmap = await _screenshot.GetScreenshotAsync();
    await QrCodeHandlerAsync(bitmap);
}
```

### 改动 3：`ImageTranslateAsync`（`:1114`）

将原 `CloseImageTranslateWindows()`（Close/重建）改为 Hide/Show。图片翻译窗口有两种类型（`ImageTranslateWindow` / `ImageTranslateCompactWindow`），需一并 Hide；两个服务校验失败分支都要恢复 Show。

```csharp
[RelayCommand]
private async Task ImageTranslateAsync()
{
    var existingWindows = Application.Current.Windows
        .OfType<Window>()
        .Where(w => w is ImageTranslateWindow or ImageTranslateCompactWindow)
        .ToList();
    foreach (var w in existingWindows)
        w.Hide();

    var ocrPlugin = GetImageTranslateOcrSvcAndNotify();
    if (ocrPlugin == null)
    {
        foreach (var w in existingWindows)
            w.Show();
        return;
    }

    if (TranslateService.ImageTranslateService == null)
    {
        Helper.PromptConfigureService(
            _i18n.GetTranslation("ImageTranslateServiceNotFoundTitle"),
            _i18n.GetTranslation("ImageTranslateServiceNotFoundMessage"),
            nameof(TranslatePage));
        foreach (var w in existingWindows)
            w.Show();
        return;
    }

    using var captureResult = await _screenshot.GetScreenshotCaptureAsync();
    await ImageTranslateHandlerAsync(captureResult?.Bitmap, ocrPlugin, captureResult?.PhysicalBounds);
}
```

### 改动 4：`ImageTranslateHandlerAsync` 删除二次关闭（`:1140`）

当前 `:1140` 有 `CloseImageTranslateWindows()`。改为 Hide/Show 后，截图阶段窗口已被 Hide，随后 `OpenAsync`/`OpenPreparedAsync` 会复用并 Show 同一窗口——此处若再关闭会把刚 Show 的窗口关掉。**删除 `:1140` 的 `CloseImageTranslateWindows()` 调用**。

**外部调用影响**：`ImageTranslateHandlerAsync` 是 public，还被 `ExternalCallService.cs:179`（`translate_ocr_image` 动作，外部直接传入已截图 bitmap）调用。该路径不经过 `ImageTranslateAsync`，故窗口未被 Hide。删除 `:1140` 后，外部调用在旧窗口已可见时会**复用同一窗口刷新内容**（不再 Close/重建），符合 Hide/Show 复用窗口的精神，行为可接受。

### 改动 5：删除 `CloseImageTranslateWindows`（`:1275`）

改为 Hide/Show 后该方法无调用方，删除以保持整洁。不新增等价的 Hide 辅助方法——三处调用各有不同窗口类型与失败恢复逻辑，内联更清晰。

## 不变的部分

- OCR 窗口内"重新截图"按钮（`OcrWindowViewModel.OcrAsync:213`）维持现状。
- `SilentOcrAsync`（静默 OCR，无窗口）不动。
- `Screenshot` 内部隐藏主窗口（`App.Current.MainWindow`）的逻辑不动。
- `OcrHandlerAsync` / `QrCodeHandlerAsync` / `ImageTranslateHandlerAsync` 的窗口显示逻辑（`OpenAsync`/`OpenPreparedAsync`）不动——它们天然复用已 Hide 的窗口并 Show。

## 行为变化总结

- 图片翻译 / OCR / QrCode 全局热键二次触发：已开窗口先 `Hide()`，截图选区无遮挡，截图后同一窗口 `Show()` 新结果，窗口位置/状态保留，不再 Close/重建。
- 跨功能清理：OCR 窗口开着时触发 QrCode（或反之），旧窗口也会先 Hide 再复用，统一为同一窗口承载新结果。
- 服务未配置时窗口恢复显示，不卡隐藏。

## 测试验证（手动，WPF 窗口行为难自动化）

1. OCR 热键二次触发 → 旧窗口隐藏 → 截图选区无遮挡 → 同一窗口 Show 新结果（位置保留）。
2. QrCode 热键二次触发 → 同上。
3. 图片翻译热键二次触发 → 旧窗口隐藏（非关闭）→ 截图 → 同一窗口 Show 新结果（位置保留，不再重建）。
4. 服务未配置时触发 → 窗口恢复显示，不卡隐藏状态。
5. OCR 窗口开着时触发 QrCode（反之亦然）→ 旧窗口隐藏 → 截图 → 新结果。
6. 窗口内"重新截图"按钮仍正常工作（OCR 按钮的 Hide/Show 与全局路径幂等）。
7. 首次触发（无已开窗口）不受影响，行为与改造前一致。

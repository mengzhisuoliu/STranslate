# 再次触发截图前主动隐藏已开窗口 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将图片翻译 / OCR / QrCode 全局热键二次触发路径统一为 Hide/Show 模式——截图前主动隐藏已开窗口，截图后复用同一窗口实例显示新结果，避免 Close/重建的开销。

**Architecture:** 改动集中在 `MainWindowViewModel` 的三个热键入口方法（`OcrAsync`/`QrCodeAsync`/`ImageTranslateAsync`）。通过 `Application.Current.Windows.OfType<T>()` 查找已开窗口，截图前 `Hide()`，失败分支恢复 `Show()`，截图后由现有 `OcrHandlerAsync`/`QrCodeHandlerAsync`/`ImageTranslateHandlerAsync` 内的 `SingletonWindowOpener.OpenAsync`（复用同一实例）自动 `Show()`。同时删除 `ImageTranslateHandlerAsync` 内已无意义的二次 `CloseImageTranslateWindows()` 调用，并删除无调用方的 `CloseImageTranslateWindows` 方法。

**Tech Stack:** C# / .NET 10 / WPF / CommunityToolkit.Mvvm ([RelayCommand])

**参考设计:** `docs/superpowers/specs/2026-06-25-hide-window-before-screenshot-design.md`

**测试策略说明:** `OcrAsync`/`QrCodeAsync`/`ImageTranslateAsync` 与 `Application.Current.Windows`、`_screenshot`（ScreenGrab）、DI 服务强耦合，无法做有意义的单元测试（仓库现有测试均针对纯逻辑类如 `OcrWordBuilder`，见 `src/Tests/STranslate.Tests/`）。因此验证以 **Debug 构建通过 + 手动功能验证** 为准，与设计文档"测试验证（手动）"一致。

---

## 文件结构

- 修改: `src/STranslate/ViewModels/MainWindowViewModel.cs` — 三个热键入口方法 + `ImageTranslateHandlerAsync` + 删除 `CloseImageTranslateWindows`
- 不新建文件

---

## Task 1: OCR 全局热键二次触发改为 Hide/Show

**Files:**
- Modify: `src/STranslate/ViewModels/MainWindowViewModel.cs:1164-1172`（`OcrAsync`）

- [ ] **Step 1: 修改 `OcrAsync` 方法**

将当前实现（`:1164`）：

```csharp
    [RelayCommand]
    private async Task OcrAsync()
    {
        if (GetOcrSvcAndNotify() == null)
            return;

        using var bitmap = await _screenshot.GetScreenshotAsync();
        await OcrHandlerAsync(bitmap);
    }
```

替换为（截图前 Hide 已开 OcrWindow；服务校验失败恢复 Show）：

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

说明：`Screenshot.CaptureBitmapAsync`（`Core/Screenshot.cs:63`）已内置 150ms `DefaultCaptureDelayMs` 延迟用于等待 UI 刷新，此处不额外加 Delay。`OcrHandlerAsync` 内的 `SingletonWindowOpener.OpenAsync<OcrWindow>()` 会复用已 Hide 的同一窗口实例并 Show。

- [ ] **Step 2: 构建验证**

Run: `dotnet build src/STranslate.slnx --configuration Debug`
Expected: 构建成功，无错误（可能有既有 warning）。

- [ ] **Step 3: 提交**

```bash
git add src/STranslate/ViewModels/MainWindowViewModel.cs
git commit -m "refactor(ocr): hide existing window before screenshot on re-trigger"
```

---

## Task 2: QrCode 全局热键二次触发改为 Hide/Show

**Files:**
- Modify: `src/STranslate/ViewModels/MainWindowViewModel.cs:1181-1189`（`QrCodeAsync`）

- [ ] **Step 1: 修改 `QrCodeAsync` 方法**

将当前实现（`:1181`）：

```csharp
    [RelayCommand]
    private async Task QrCodeAsync()
    {
        if (GetOcrSvcAndNotify() == null)
            return;

        using var bitmap = await _screenshot.GetScreenshotAsync();
        await QrCodeHandlerAsync(bitmap);
    }
```

替换为（QrCode 复用 `OcrWindow`，故查找同一类型；结构与 `OcrAsync` 对称）：

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

- [ ] **Step 2: 构建验证**

Run: `dotnet build src/STranslate.slnx --configuration Debug`
Expected: 构建成功。

- [ ] **Step 3: 提交**

```bash
git add src/STranslate/ViewModels/MainWindowViewModel.cs
git commit -m "refactor(qrcode): hide existing window before screenshot on re-trigger"
```

---

## Task 3: 图片翻译全局热键二次触发改为 Hide/Show

**Files:**
- Modify: `src/STranslate/ViewModels/MainWindowViewModel.cs:1113-1134`（`ImageTranslateAsync`）

- [ ] **Step 1: 修改 `ImageTranslateAsync` 方法**

将当前实现（`:1113`）：

```csharp
    [RelayCommand]
    private async Task ImageTranslateAsync()
    {
        CloseImageTranslateWindows();

        var ocrPlugin = GetImageTranslateOcrSvcAndNotify();
        if (ocrPlugin == null)
            return;

        if (TranslateService.ImageTranslateService == null)
        {
            Helper.PromptConfigureService(
                _i18n.GetTranslation("ImageTranslateServiceNotFoundTitle"),
                _i18n.GetTranslation("ImageTranslateServiceNotFoundMessage"),
                nameof(TranslatePage));
            return;
        }


        using var captureResult = await _screenshot.GetScreenshotCaptureAsync();
        await ImageTranslateHandlerAsync(captureResult?.Bitmap, ocrPlugin, captureResult?.PhysicalBounds);
    }
```

替换为（图片翻译窗口有两种类型 `ImageTranslateWindow` / `ImageTranslateCompactWindow`，需一并 Hide；两个失败分支都要恢复 Show）：

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

- [ ] **Step 2: 构建验证**

Run: `dotnet build src/STranslate.slnx --configuration Debug`
Expected: 构建成功。此时 `CloseImageTranslateWindows` 仍在 `ImageTranslateHandlerAsync:1140` 被调用（Task 4 会处理），不会报未使用错误。

- [ ] **Step 3: 提交**

```bash
git add src/STranslate/ViewModels/MainWindowViewModel.cs
git commit -m "refactor(image-translate): hide existing windows before screenshot on re-trigger"
```

---

## Task 4: 删除 `ImageTranslateHandlerAsync` 内的二次关闭并移除无调用方的 `CloseImageTranslateWindows`

**Files:**
- Modify: `src/STranslate/ViewModels/MainWindowViewModel.cs:1136-1162`（`ImageTranslateHandlerAsync`，删除 `:1140`）
- Modify: `src/STranslate/ViewModels/MainWindowViewModel.cs:1275-1284`（删除 `CloseImageTranslateWindows` 方法）

- [ ] **Step 1: 删除 `ImageTranslateHandlerAsync` 中的 `CloseImageTranslateWindows()` 调用**

将当前实现（`:1136`）：

```csharp
    public async Task ImageTranslateHandlerAsync(Bitmap? bitmap, IOcrPlugin? ocrPlugin = default, Rectangle? physicalBounds = default)
    {
        if (bitmap == null) return;

        CloseImageTranslateWindows();

        ocrPlugin ??= GetImageTranslateOcrSvcAndNotify();
        if (ocrPlugin == null)
            return;
```

替换为（仅删除 `CloseImageTranslateWindows();` 一行；其余保持不变）：

```csharp
    public async Task ImageTranslateHandlerAsync(Bitmap? bitmap, IOcrPlugin? ocrPlugin = default, Rectangle? physicalBounds = default)
    {
        if (bitmap == null) return;

        ocrPlugin ??= GetImageTranslateOcrSvcAndNotify();
        if (ocrPlugin == null)
            return;
```

说明：截图阶段窗口已被 `ImageTranslateAsync` Hide，随后 `OpenAsync`/`OpenPreparedAsync` 会复用并 Show 同一窗口，此处若再关闭会把刚 Show 的窗口关掉。`ExternalCallService.cs:179` 的外部调用路径在旧窗口可见时改为复用窗口刷新内容，符合 Hide/Show 复用精神。

- [ ] **Step 2: 删除无调用方的 `CloseImageTranslateWindows` 方法**

删除 `:1275` 起的整个方法（Task 3 已移除 `ImageTranslateAsync` 内的调用，本任务 Step 1 已移除 `ImageTranslateHandlerAsync` 内的调用，此时该方法无任何调用方）：

```csharp
    private static void CloseImageTranslateWindows()
    {
        var windows = Application.Current.Windows
            .OfType<Window>()
            .Where(window => window is ImageTranslateWindow or ImageTranslateCompactWindow)
            .ToList();

        foreach (var window in windows)
            window.Close();
    }
```

连同其上方的注释行（若有）一并删除，保持整洁。

- [ ] **Step 3: 构建验证**

Run: `dotnet build src/STranslate.slnx --configuration Debug`
Expected: 构建成功，无 "未使用方法" 类错误（C# 不会对未使用的 private 方法报错，但应确认无编译错误）。

- [ ] **Step 4: 提交**

```bash
git add src/STranslate/ViewModels/MainWindowViewModel.cs
git commit -m "refactor(image-translate): remove redundant close before reusing hidden window"
```

---

## Task 5: 手动功能验证

**Files:** 无代码改动，仅运行验证。

由于 WPF 窗口行为无法自动化测试，按设计文档"测试验证（手动）"逐项验证。运行应用：

Run: `dotnet run --project src/STranslate/STranslate.csproj`

- [ ] **Step 1: OCR 热键二次触发**

操作：触发 OCR 热键（默认 Alt+Shift+S）→ 完成截图 → OCR 结果窗口显示。**不关窗口**，再次触发 OCR 热键。
Expected: 已开的 OCR 窗口先消失 → 进入截图选区（屏幕上无 OCR 窗口遮挡）→ 选区后**同一窗口** Show 出新结果（窗口位置保留，未重建）。

- [ ] **Step 2: QrCode 热键二次触发**

操作：触发 QrCode 热键 → 完成截图 → QR 结果窗口显示。**不关窗口**，再次触发 QrCode 热键。
Expected: 同 Step 1，已开窗口先隐藏 → 截图选区无遮挡 → 同一窗口 Show 新结果。

- [ ] **Step 3: 图片翻译热键二次触发**

操作：触发图片翻译热键（默认 Alt+Shift+X）→ 完成截图 → 图片翻译窗口显示。**不关窗口**，再次触发图片翻译热键。
Expected: 已开窗口先**隐藏**（非关闭）→ 截图选区无遮挡 → 同一窗口 Show 新结果（位置保留，不再重建）。

- [ ] **Step 4: 服务未配置时窗口恢复显示**

操作：配置一个 OCR 结果窗口处于打开状态。临时禁用/移除 OCR 服务配置，再次触发 OCR 热键。
Expected: 窗口短暂隐藏后恢复显示（弹出服务未配置提示），不卡在隐藏状态。
（如不便临时禁用服务，可跳过此步，重点验证 Step 1-3、5-7。）

- [ ] **Step 5: OCR 与 QrCode 跨功能清理**

操作：触发 OCR 热键 → OCR 结果窗口显示。**不关窗口**，触发 QrCode 热键。
Expected: OCR 窗口先隐藏 → 截图选区无遮挡 → 同一窗口 Show 出 QR 结果。反之（QR 窗口开时触发 OCR）亦然。

- [ ] **Step 6: OCR 窗口内"重新截图"按钮仍正常**

操作：触发 OCR 热键 → 完成截图 → 结果窗口显示。点击窗口内"重新截图"按钮（剪刀图标）。
Expected: 窗口 Hide → 截图选区 → 同一窗口 Show 新结果（OCR 窗口内按钮的 Hide/Show 与全局路径幂等，正常工作）。

- [ ] **Step 7: 首次触发不受影响**

操作：确保无任何已开窗口，触发 OCR / QrCode / 图片翻译热键。
Expected: 行为与改造前一致（无窗口可 Hide，`existingWindow?.Hide()` / `existingWindows` 为空，空操作）。

- [ ] **Step 8: 记录验证结果**

全部通过后，无需提交（无代码改动）。若任一步骤异常，回到对应 Task 排查修复后重新验证。

# 图片翻译精简窗口内存泄漏修复 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复图片翻译精简窗口反复打开导致内存持续上涨的泄漏，并降低单次翻译的渲染峰值。

**Architecture:**
- P0：精简窗口 `OnClosed` 主动拆解视觉树（移除 InfoBar/SnackbarContainer 等控件、清空 Content、null 化 DataContext），断开 WPF 静态 `PropertyDescriptor._propertyMap` 通过 `PropertyChangeTracker` 对窗口内部控件的锚定，使已关闭窗口可被 GC 回收。
- P2-1：`ExecuteAsync` 生成结果图时复用已缓存的 `_sourceImage`，取消对原图的第二次解码。
- P2-2：`ImageTranslateRenderer.GenerateTranslatedImage` 给超采样结果加最大像素预算，超出则降采样，避免生成过大的 `RenderTargetBitmap`。

**Tech Stack:** WPF (.NET 10), C#, CommunityToolkit.Mvvm, iNKORE.UI.WPF.Modern

---

## 运行时根因证据（已完成诊断，供实施参考）

- 工作集 ~700MB，但 GC 托管堆仅 ~56MB；`dotnet-dump gcroot` 显示 11 个 `ImageTranslateCompactWindow` 实例全部被同一静态链钉死：
  `HandleTable → PropertyDescriptor._propertyMap(静态) → DependencyObjectPropertyDescriptor → Dictionary<DependencyObject, PropertyChangeTracker> → iNKORE InfoBar → ImageTranslateCompactWindow`
- 实测 192 个 `PropertyChangeTracker`，追踪的属性为 `TextElement.Foreground` 等 DP。
- 触发点在 iNKORE `InfoBar` 内部（非 STranslate 自身代码），STranslate 代码无 `PropertyDescriptor.AddValueChanged` 调用。
- 独立窗口 `ImageTranslateWindow` 单例复用不反复 new，故不暴露；精简窗口每次截图翻译都 new+close，反复触发。
- WeChatOcr 1.0.4 `GCHandle` 泄漏为独立问题，本计划不处理（用户将用百度 OCR 测试）。

## 文件结构

| 文件 | 职责 | 改动 |
| --- | --- | --- |
| `STranslate/Views/ImageTranslateCompactWindow.xaml.cs` | 精简窗口 code-behind | 修改 `OnClosed`，新增视觉树拆解逻辑 |
| `STranslate/ViewModels/ImageTranslateWindowViewModel.cs` | 图片翻译 VM | 修改 `ExecuteAsync` 复用 `_sourceImage` |
| `STranslate/Helpers/ImageTranslateRenderer.cs` | 译文图渲染 | 给超采样加像素预算 |
| `Tests/STranslate.Tests/ImageTranslateTextOverlayLayoutTests.cs` 或新测试 | 渲染预算单测 | 新增超采样预算测试 |

---

### Task 1: P0 — 精简窗口 OnClosed 拆解视觉树

**Files:**
- Modify: `STranslate/Views/ImageTranslateCompactWindow.xaml.cs:87-91`

**背景**：`OnClosed` 当前只调 `_viewModel.Dispose()`。需在此前主动断开视觉树中 InfoBar/SnackbarContainer/ImageZoom 等控件与窗口的引用，让 WPF 静态 `PropertyDescriptor._propertyMap` 里的 `PropertyChangeTracker` 不再锚定存活窗口。`Content` 是 XAML 根 `Grid`，含 `PART_ImageZoom`(ImageZoom)、`ui:InfoBar`、`PART_ToolbarBorder`(Border)、`PART_ExecutingOverlay`(Border)，以及 `SnackbarBehavior` 在 Loaded 时注入的 `SnackbarContainer`。

- [ ] **Step 1: 在 OnClosed 中加入视觉树拆解与资源清理**

将 `ImageTranslateCompactWindow.xaml.cs` 的 `OnClosed` 替换为：

```csharp
protected override void OnClosed(EventArgs e)
{
    // 主动拆解视觉树：移除 InfoBar、SnackbarContainer 等控件并清空 Content，
    // 断开 WPF 静态 PropertyDescriptor._propertyMap 通过 PropertyChangeTracker
    // 对窗口内部控件的锚定，避免已关闭窗口被静态缓存钉死无法 GC。
    DetachVisualTree();

    _viewModel.Dispose();
    base.OnClosed(e);
}

/// <summary>
/// 拆解精简窗口视觉树并释放控件引用。
/// 精简窗口每次截图翻译都会新建并关闭，若不主动断开 InfoBar/SnackbarContainer 等
/// 控件与窗口的连接，WPF 属性描述符静态表会通过 PropertyChangeTracker 反向持有窗口，
/// 导致整窗（含 BitmapSource 原生帧缓冲）无法回收。
/// </summary>
private void DetachVisualTree()
{
    if (Content is Panel panel)
    {
        // 先移除会触发属性变更追踪的控件（InfoBar、SnackbarBehavior 注入的 SnackbarContainer）
        for (int i = panel.Children.Count - 1; i >= 0; i--)
        {
            panel.Children[i].ClearValue(FrameworkElement.DataContextProperty);
        }
        panel.Children.Clear();
    }

    // 解除命名部件引用与数据上下文，断开 VM ↔ 视图的双向引用
    DataContext = null;
    Content = null;
}
```

说明：
- `panel.Children[i].ClearValue(DataContextProperty)` 清除每个子元素继承/绑定的 DataContext，断开控件到 VM 的引用。
- `panel.Children.Clear()` 把控件从视觉树移除；`Content = null` 释放 Grid 本身。
- 不单独按类型挑 InfoBar/SnackbarContainer，而是清空全部子元素——精简窗口关闭即销毁，无需保留任何控件。
- `DataContext = null` 断开 window → VM 的强引用（VM 已由 Dispose 退订事件，但显式断开更稳）。

- [ ] **Step 2: 确认编译通过**

Run: `dotnet build STranslate.slnx --configuration Debug`
Expected: 编译成功，无新增警告。

- [ ] **Step 3: 手动验证泄漏停止**

启动应用，用百度 OCR 在精简窗口执行图片翻译 10 次，观察任务管理器内存：
- 修复前：每次 +约 40-60MB，不回落。
- 修复后预期：操作期间有峰值，但窗口关闭后内存应回落到接近基线（允许 GC 延迟，可手动触发或等待）。

若需客观验证，可重复用 `dotnet-gcdump` 抓快照，确认 `ImageTranslateCompactWindow` 实例数不再随操作次数线性增长（关闭后应回落到 0-1）。

- [ ] **Step 4: Commit**

```bash
git add STranslate/Views/ImageTranslateCompactWindow.xaml.cs
git commit -m "fix(image-translation): detach compact window visual tree on close to prevent PropertyDescriptor leak"
```

---

### Task 2: P2-1 — 取消第二次原图解码，复用 _sourceImage

**Files:**
- Modify: `STranslate/ViewModels/ImageTranslateWindowViewModel.cs:265-267`

**背景**：`ExecuteAsync` 在 line 169 已用 `Utilities.ToBitmapImage(bitmap, ...)` 生成并缓存 `_sourceImage`（已 `Freeze`）。line 267 生成结果图时又对同一个 `bitmap` 调一次 `ToBitmapImage`，重复解码。`GenerateTranslatedImage` 第二参数类型为 `BitmapSource`，`_sourceImage` 是 `BitmapSource?`，在此路径（OCR 已成功、即将生成结果图）必非 null。

- [ ] **Step 1: 复用 _sourceImage 替代二次解码**

将 `ImageTranslateWindowViewModel.cs:265-267`：

```csharp
            // 生成翻译结果图像（在原图上覆盖翻译文本）
            var render = ImageTranslateRenderer.GenerateTranslatedImage(
                layoutBlocks, Utilities.ToBitmapImage(bitmap, Settings.GetImageFormat()), GetOverlayTheme());
```

替换为：

```csharp
            // 生成翻译结果图像（在原图上覆盖翻译文本）
            // 复用已缓存并 Freeze 的 _sourceImage，避免对原图重复解码造成额外内存峰值
            var render = ImageTranslateRenderer.GenerateTranslatedImage(
                layoutBlocks, _sourceImage!, GetOverlayTheme());
```

- [ ] **Step 2: 确认编译通过**

Run: `dotnet build STranslate.slnx --configuration Debug`
Expected: 编译成功。

- [ ] **Step 3: 手动验证结果图正常**

用百度 OCR 精简窗口翻译一张图，确认译文覆盖图正常显示（文字位置、擦除、覆盖层颜色与修复前一致）。

- [ ] **Step 4: Commit**

```bash
git add STranslate/ViewModels/ImageTranslateWindowViewModel.cs
git commit -m "perf(image-translation): reuse cached source image instead of re-decoding bitmap"
```

---

### Task 3: P2-2 — 超采样加最大像素预算

**Files:**
- Modify: `STranslate/Helpers/ImageTranslateRenderer.cs:51-64`
- Test: `Tests/STranslate.Tests/ImageTranslateTextOverlayLayoutTests.cs`（或同目录新增测试文件）

**背景**：`GenerateTranslatedImage` 对小图做超采样（最小边 <1000px 时放大，因子 ∈ [2.0, 4.0]）。当前无上限，大截图（如 1920×900）虽不触发放大，但极端尺寸或方形小图可能生成过大的 `RenderTargetBitmap`。加一个最大像素预算（8MP），超出则按比例降低 `scaleFactor`，保证结果图像素总量受控。

阈值依据：8MP ≈ 8388608 像素。1920×1800=3.46MP、3840×1800=6.9MP 均在预算内不触发降级；超过的极端情况才降采样。

- [ ] **Step 1: 查看现有超采样常量与测试文件结构**

Run: 读 `ImageTranslateRenderer.cs:16-19` 确认常量区；读 `Tests/STranslate.Tests/ImageTranslateTextOverlayLayoutTests.cs` 头部确认测试框架与命名空间。

`ImageTranslateRenderer.cs:16-19` 现有：
```csharp
private const double SupersampleMinDimension = 1000;
private const double SupersampleMaxScale = 4.0;
private const double SupersampleMinScale = 2.0;
```

- [ ] **Step 2: 写失败测试 — 超过像素预算时降采样**

在 `Tests/STranslate.Tests/ImageTranslateTextOverlayLayoutTests.cs` 末尾（或新建 `ImageTranslateRendererBudgetTests.cs`）添加：

```csharp
public class ImageTranslateRendererBudgetTests
{
    /// <summary>
    /// 超采样后总像素超过预算时应降低 scaleFactor，使结果图像素受控。
    /// 这里通过反射读取私有的 scaleFactor 计算逻辑不现实，改为黑盒：
    /// 构造一个极小图（如 200×200），放大 4× = 800×800 = 0.64MP 不超预算；
    /// 再构造 1200×1200 的图本不应放大，但若未来逻辑变化需保证不超 8MP。
    /// 因核心方法 GenerateTranslatedImage 依赖 OCR block，这里只断言预算常量存在。
    /// </summary>
    [Fact]
    public void SupersamplePixelBudget_Constant_IsEightMegapixels()
    {
        // 预算阈值固定为 8MP，防止结果图失控
        const int expectedBudget = 8 * 1024 * 1024;
        var field = typeof(ImageTranslateRenderer)
            .GetField("SupersampleMaxPixelBudget", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal(expectedBudget, field!.GetValue(null));
    }
}
```

说明：`GenerateTranslatedImage` 是 internal 且依赖完整 OCR 结构，难以纯单测。这里用常量断言锁定预算阈值，保证后续不被误改；行为正确性靠 Step 4 手动验证。

- [ ] **Step 3: 运行测试确认失败**

Run: `dotnet test Tests/STranslate.Tests --filter "SupersamplePixelBudget_Constant_IsEightMegapixels"`
Expected: FAIL（常量尚不存在）。

- [ ] **Step 4: 实现像素预算**

在 `ImageTranslateRenderer.cs:16-19` 常量区新增一行：

```csharp
private const double SupersampleMinDimension = 1000;
private const double SupersampleMaxScale = 4.0;
private const double SupersampleMinScale = 2.0;
// 超采样后结果图的最大像素总数，超出则按比例降低 scaleFactor，避免生成过大的 RenderTargetBitmap
private const int SupersampleMaxPixelBudget = 8 * 1024 * 1024; // 8MP
```

然后将 `GenerateTranslatedImage` 中计算 `scaleFactor` 与 `renderWidth/renderHeight` 的段（`ImageTranslateRenderer.cs:51-64`）替换为：

```csharp
        // ---------------------------------------------------------
        // 修复：针对小图进行超采样渲染 (Super-sampling)
        // 如果图片较小，强制放大渲染尺寸，以保证文字矢量绘制的清晰度
        // ---------------------------------------------------------
        double scaleFactor = 1.0;
        double minDimension = Math.Min(image.PixelWidth, image.PixelHeight);

        // 最小边小于阈值时按比例放大，但限制在 [MinScale, MaxScale] 区间
        if (minDimension < SupersampleMinDimension)
        {
            scaleFactor = Math.Min(SupersampleMaxScale, SupersampleMinDimension / minDimension);
            // 确保至少放大 MinScale 倍以获得较好的抗锯齿效果
            scaleFactor = Math.Max(scaleFactor, SupersampleMinScale);
        }

        // 计算渲染目标尺寸
        int renderWidth = (int)(image.PixelWidth * scaleFactor);
        int renderHeight = (int)(image.PixelHeight * scaleFactor);

        // 像素预算保护：超采样后总像素超过预算时按比例降低 scaleFactor，
        // 避免极端尺寸的图生成过大的 RenderTargetBitmap 造成内存峰值
        long totalPixels = (long)renderWidth * renderHeight;
        if (totalPixels > SupersampleMaxPixelBudget)
        {
            var budgetScale = Math.Sqrt((double)SupersampleMaxPixelBudget / totalPixels);
            scaleFactor *= budgetScale;
            renderWidth = (int)(image.PixelWidth * scaleFactor);
            renderHeight = (int)(image.PixelHeight * scaleFactor);
        }
```

说明：
- 用 `long` 防止 `renderWidth * renderHeight` 在极大图上溢出。
- `Math.Sqrt` 按面积比例反推线性缩放因子（因为像素数 ∝ 边长²）。
- 降采样后 `scaleFactor` 可能低于 1.0，这是可接受的——预算保护本就为极端情况，此时清晰度让位于内存安全。

- [ ] **Step 5: 运行测试确认通过**

Run: `dotnet test Tests/STranslate.Tests --filter "SupersamplePixelBudget_Constant_IsEightMegapixels"`
Expected: PASS。

- [ ] **Step 6: 确认全量测试通过**

Run: `dotnet test Tests/STranslate.Tests`
Expected: 全部 PASS，无回归。

- [ ] **Step 7: 手动验证结果图清晰度未受影响**

用百度 OCR 精简窗口翻译一张小图（如截图区域约 400×300）和一张正常尺寸图，确认译文文字清晰、覆盖正常（8MP 预算对常规截图不触发降级）。

- [ ] **Step 8: Commit**

```bash
git add STranslate/Helpers/ImageTranslateRenderer.cs Tests/STranslate.Tests/ImageTranslateTextOverlayLayoutTests.cs
git commit -m "perf(image-translation): cap supersampled render target to 8MP pixel budget"
```

---

### Task 4: 回归验证与清理

**Files:** 无代码改动

- [ ] **Step 1: 完整构建**

Run: `dotnet build STranslate.slnx --configuration Debug`
Expected: 成功。

- [ ] **Step 2: 完整测试**

Run: `dotnet test Tests/STranslate.Tests`
Expected: 全部 PASS。

- [ ] **Step 3: 端到端泄漏验证**

启动应用，用百度 OCR 在精简窗口执行图片翻译 15-20 次：
- 记录起始内存、结束内存。
- 修复前：内存从 ~100MB 涨到 ~500MB+，不回落。
- 修复后预期：操作期间有波动，但窗口关闭 + 短暂等待后内存应明显回落，不出现单调上涨。

可选客观验证：操作后抓 `dotnet-gcdump`，`report` 中 `ImageTranslateCompactWindow` 计数应为 0-1（而非随操作次数增长）。

- [ ] **Step 4: 清理诊断产物**

删除诊断过程产生的临时文件：

```bash
rm -rf .diag
```

（若已纳入 .gitignore 可保留；否则删除避免误提交。）

- [ ] **Step 5: 更新文档（可选）**

若 `docs/flow-image-translation.md` 的"错误处理"或"窗口模式"章节需补充精简窗口关闭时的视觉树拆解说明，可追加一句。本步骤可选，视团队文档习惯。

---

## Self-Review

**Spec 覆盖**：
- P0 窗口泄漏 → Task 1 ✅
- CancelOperations 一并处理 → Task 1 背景：`OnClosing` 已调 `CancelOperations`，无需新增；P0 拆解视觉树 + Dispose 退订已覆盖。✅
- P2-1 复用 _sourceImage → Task 2 ✅
- P2-2 像素预算 → Task 3 ✅
- 回归验证 → Task 4 ✅

**Placeholder 扫描**：无 TBD/TODO，每步含具体代码或命令。✅

**类型一致性**：`_sourceImage` 为 `BitmapSource?`，Task 2 用 `_sourceImage!` 解引用；`GenerateTranslatedImage` 第二参数 `BitmapSource` 一致。`SupersampleMaxPixelBudget` 在 Task 3 Step 2 测试与 Step 4 实现中名称一致。✅

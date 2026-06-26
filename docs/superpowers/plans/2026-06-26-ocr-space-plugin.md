# OCR.Space 插件 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 STranslate 新增 OCR.Space 插件，实现 `IOcrPlugin`，支持纯文本 OCR 与图片翻译（返回像素坐标框），引擎可选 1/2/3 默认 2，固定免费端点。

**Architecture:** 新建插件项目 `STranslate.Plugin.Ocr.OcrSpace`，仿 Youdao OCR 插件结构。HTTP 走 `PostFormAsync`（`application/x-www-form-urlencoded`），用 `base64Image` 上传图片，`apikey` 放请求头。解析 `ParsedResults[].TextOverlay.Lines[].Words[]` 的 `Left/Top/Width/Height` 还原为像素 4 点坐标。纯逻辑（语言映射、base64 前缀检测、坐标还原）用 TDD 覆盖；WPF/HTTP 接线做构建验证。

**Tech Stack:** C# / .NET 10 / WPF / xUnit / CommunityToolkit.Mvvm / System.Text.Json

**PluginID:** `26535c2510d64c8b8f738131a1338e7c`

---

## 文件结构

新建插件目录 `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/`：
- `STranslate.Plugin.Ocr.OcrSpace.csproj` — 项目文件，net10.0-windows + UseWPF，引用 STranslate.Plugin
- `plugin.json` — 插件元数据
- `icon.png` — 图标（复用 Youdao 占位）
- `Settings.cs` — 配置模型 + `OcrSpaceEngine` 枚举
- `Main.cs` — 实现 `IOcrPlugin`，含 `RecognizeAsync`、`LangConverter`、`BuildContentsFromLines`、`GetBase64ImagePrefix`、响应 DTO
- `ViewModel/SettingsViewModel.cs` — 绑定 ApiKey、Engine
- `View/SettingsView.xaml` + `View/SettingsView.xaml.cs` — 设置面板
- `Languages/{en,zh-cn,zh-tw,ja,ko}.{xaml,json}` — 5 语言 i18n

修改：
- `src/STranslate.slnx` — 注册插件项目
- `src/Tests/STranslate.Tests/STranslate.Tests.csproj` — 引用插件项目以跑单元测试
- `src/Tests/STranslate.Tests/OcrSpacePluginTests.cs` — 新建测试文件

---

## Task 1: 脚手架项目与元数据

**Files:**
- Create: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/STranslate.Plugin.Ocr.OcrSpace.csproj`
- Create: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/plugin.json`
- Copy: `src/Plugins/STranslate.Plugin.Ocr.Youdao/icon.png` → `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/icon.png`

- [ ] **Step 1: 创建 csproj**

创建 `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/STranslate.Plugin.Ocr.OcrSpace.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net10.0-windows</TargetFramework>
        <UseWPF>true</UseWPF>
        <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
        <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
        <!--// 编译后打包为插件 //-->
        <!--<EnableAutoPackage>true</EnableAutoPackage>-->
    </PropertyGroup>

    <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
        <DebugSymbols>true</DebugSymbols>
        <DebugType>portable</DebugType>
        <Optimize>false</Optimize>
        <OutputPath>..\..\.artifacts\Debug\Plugins\STranslate.Plugin.Ocr.OcrSpace\</OutputPath>
        <DefineConstants>DEBUG;TRACE</DefineConstants>
        <ErrorReport>prompt</ErrorReport>
        <WarningLevel>4</WarningLevel>
        <Prefer32Bit>false</Prefer32Bit>
    </PropertyGroup>

    <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
        <DebugType>none</DebugType>
        <Optimize>true</Optimize>
        <OutputPath>..\..\.artifacts\Release\Plugins\STranslate.Plugin.Ocr.OcrSpace\</OutputPath>
        <DefineConstants>TRACE</DefineConstants>
        <ErrorReport>prompt</ErrorReport>
        <WarningLevel>4</WarningLevel>
        <Prefer32Bit>false</Prefer32Bit>
    </PropertyGroup>

    <ItemGroup>
        <Content Include="Languages\*.*">
            <Generator>MSBuild:Compile</Generator>
            <SubType>Designer</SubType>
            <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
        </Content>
        <Content Include="icon.png">
            <CopyToOutputDirectory>Always</CopyToOutputDirectory>
        </Content>
        <Content Include="plugin.json">
            <CopyToOutputDirectory>Always</CopyToOutputDirectory>
        </Content>
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\..\STranslate.Plugin\STranslate.Plugin.csproj" />
    </ItemGroup>

    <!-- 暴露 internal 给测试项目 -->
    <ItemGroup>
        <InternalsVisibleTo Include="STranslate.Tests" />
    </ItemGroup>

</Project>
```

- [ ] **Step 2: 创建 plugin.json**

创建 `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/plugin.json`：

```json
{
  "PluginID": "26535c2510d64c8b8f738131a1338e7c",
  "Name": "OCR.Space",
  "Description": "OCR.Space OCR plugin for STranslate",
  "Author": "zggsong",
  "Version": "1.0.0",
  "Website": "https://github.com/STranslate/STranslate",
  "ExecuteFileName": "STranslate.Plugin.Ocr.OcrSpace.dll",
  "IconPath": "icon.png"
}
```

- [ ] **Step 3: 复制图标占位**

复制 Youdao 插件图标作为占位（后续可替换）：

```bash
cp src/Plugins/STranslate.Plugin.Ocr.Youdao/icon.png src/Plugins/STranslate.Plugin.Ocr.OcrSpace/icon.png
```

- [ ] **Step 4: 提交**

```bash
git add src/Plugins/STranslate.Plugin.Ocr.OcrSpace/STranslate.Plugin.Ocr.OcrSpace.csproj src/Plugins/STranslate.Plugin.Ocr.OcrSpace/plugin.json src/Plugins/STranslate.Plugin.Ocr.OcrSpace/icon.png
git commit -m "feat: scaffold OCR.Space plugin project"
```

---

## Task 2: Settings 配置模型

**Files:**
- Create: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Settings.cs`

- [ ] **Step 1: 创建 Settings.cs**

创建 `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Settings.cs`：

```csharp
namespace STranslate.Plugin.Ocr.OcrSpace;

public class Settings
{
    public string ApiKey { get; set; } = string.Empty;
    public OcrSpaceEngine Engine { get; set; } = OcrSpaceEngine.Engine2;
}

public enum OcrSpaceEngine
{
    // Engine 1：最快，支持多语言含中英日韩
    Engine1,

    // Engine 2：最佳全能选择，支持语言自动检测
    Engine2,

    // Engine 3：最高精度，支持 200+ 语言与表格 Markdown
    Engine3
}
```

- [ ] **Step 2: 提交**

```bash
git add src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Settings.cs
git commit -m "feat(ocr-space): add settings model and engine enum"
```

---

## Task 3: LangConverter 语言映射（TDD）

**Files:**
- Create: `src/Tests/STranslate.Tests/STranslate.Tests.csproj` (modify: 加项目引用)
- Create: `src/Tests/STranslate.Tests/OcrSpacePluginTests.cs`
- Create: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Main.cs` (部分：先建类骨架 + LangConverter)

- [ ] **Step 1: 测试项目引用插件项目**

修改 `src/Tests/STranslate.Tests/STranslate.Tests.csproj`，在现有 `<ItemGroup>` 含 `ProjectReference` 处追加一行引用：

将：
```xml
  <ItemGroup>
    <ProjectReference Include="..\..\STranslate\STranslate.csproj" />
  </ItemGroup>
```
改为：
```xml
  <ItemGroup>
    <ProjectReference Include="..\..\STranslate\STranslate.csproj" />
    <ProjectReference Include="..\..\Plugins\STranslate.Plugin.Ocr.OcrSpace\STranslate.Plugin.Ocr.OcrSpace.csproj" />
  </ItemGroup>
```

- [ ] **Step 2: 写失败测试 — LangConverter 映射**

创建 `src/Tests/STranslate.Tests/OcrSpacePluginTests.cs`：

```csharp
using STranslate.Plugin;
using STranslate.Plugin.Ocr.OcrSpace;

namespace STranslate.Tests;

public class OcrSpacePluginTests
{
    [Theory]
    [InlineData(LangEnum.Auto, "auto")]
    [InlineData(LangEnum.ChineseSimplified, "chs")]
    [InlineData(LangEnum.ChineseTraditional, "cht")]
    [InlineData(LangEnum.Cantonese, "cht")]
    [InlineData(LangEnum.English, "eng")]
    [InlineData(LangEnum.Japanese, "jpn")]
    [InlineData(LangEnum.Korean, "kor")]
    [InlineData(LangEnum.French, "fre")]
    [InlineData(LangEnum.Spanish, "spa")]
    [InlineData(LangEnum.Russian, "rus")]
    [InlineData(LangEnum.German, "ger")]
    [InlineData(LangEnum.Italian, "ita")]
    [InlineData(LangEnum.Turkish, "tur")]
    [InlineData(LangEnum.PortuguesePortugal, "por")]
    [InlineData(LangEnum.PortugueseBrazil, "por")]
    [InlineData(LangEnum.Vietnamese, "vnm")]
    [InlineData(LangEnum.Thai, "tha")]
    [InlineData(LangEnum.Arabic, "ara")]
    [InlineData(LangEnum.Swedish, "swe")]
    [InlineData(LangEnum.Dutch, "dut")]
    [InlineData(LangEnum.Polish, "pol")]
    [InlineData(LangEnum.Ukrainian, "ukr")]
    public void LangConverter_MapsToOcrSpaceCode(LangEnum lang, string expected)
    {
        var plugin = new Main();
        var code = plugin.LangConverter(lang);
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(LangEnum.Malay)]
    [InlineData(LangEnum.Hindi)]
    [InlineData(LangEnum.Indonesian)]
    [InlineData(LangEnum.MongolianCyrillic)]
    [InlineData(LangEnum.Khmer)]
    [InlineData(LangEnum.NorwegianBokmal)]
    [InlineData(LangEnum.NorwegianNynorsk)]
    [InlineData(LangEnum.Persian)]
    [InlineData(LangEnum.Uzbek)]
    public void LangConverter_UnsupportedFallsBackToAuto(LangEnum lang)
    {
        var plugin = new Main();
        Assert.Equal("auto", plugin.LangConverter(lang));
    }
}
```

- [ ] **Step 3: 运行测试，确认失败**

Run: `dotnet test src/Tests/STranslate.Tests/STranslate.Tests.csproj --filter "FullyQualifiedName~OcrSpacePluginTests" --configuration Debug`
Expected: 编译失败 / FAIL，`Main` 类型或 `LangConverter` 未定义。

- [ ] **Step 4: 创建 Main.cs 骨架 + LangConverter**

创建 `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Main.cs`：

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using STranslate.Plugin.Ocr.OcrSpace.View;
using STranslate.Plugin.Ocr.OcrSpace.ViewModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Controls;

namespace STranslate.Plugin.Ocr.OcrSpace;

public class Main : ObservableObject, IOcrPlugin
{
    private const string Url = "https://api.ocr.space/parse/image";

    private Control? _settingUi;
    private SettingsViewModel? _viewModel;
    private Settings Settings { get; set; } = null!;
    private IPluginContext Context { get; set; } = null!;

    public IEnumerable<LangEnum> SupportedLanguages =>
    [
        LangEnum.Auto,
        LangEnum.ChineseSimplified,
        LangEnum.ChineseTraditional,
        LangEnum.Cantonese,
        LangEnum.English,
        LangEnum.Japanese,
        LangEnum.Korean,
        LangEnum.French,
        LangEnum.Spanish,
        LangEnum.Russian,
        LangEnum.German,
        LangEnum.Italian,
        LangEnum.Turkish,
        LangEnum.PortuguesePortugal,
        LangEnum.PortugueseBrazil,
        LangEnum.Vietnamese,
        LangEnum.Thai,
        LangEnum.Arabic,
        LangEnum.Swedish,
        LangEnum.Dutch,
        LangEnum.Polish,
        LangEnum.Ukrainian
    ];

    public bool SupportBoxPoints() => true;

    public Control GetSettingUI()
    {
        _viewModel ??= new SettingsViewModel(Context, Settings);
        _settingUi ??= new SettingsView { DataContext = _viewModel };
        return _settingUi;
    }

    public void Init(IPluginContext context)
    {
        Context = context;
        Settings = context.LoadSettingStorage<Settings>();
    }

    public void Dispose() => _viewModel?.Dispose();

    public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    /// <summary>
    ///     https://ocr.space/ocrapi 语言码为 3 字母；Engine 2/3 支持 auto 自动检测。
    /// </summary>
    public string? LangConverter(LangEnum lang)
    {
        return lang switch
        {
            LangEnum.Auto => "auto",
            LangEnum.ChineseSimplified => "chs",
            LangEnum.ChineseTraditional => "cht",
            LangEnum.Cantonese => "cht",
            LangEnum.English => "eng",
            LangEnum.Japanese => "jpn",
            LangEnum.Korean => "kor",
            LangEnum.French => "fre",
            LangEnum.Spanish => "spa",
            LangEnum.Russian => "rus",
            LangEnum.German => "ger",
            LangEnum.Italian => "ita",
            LangEnum.Turkish => "tur",
            LangEnum.PortuguesePortugal => "por",
            LangEnum.PortugueseBrazil => "por",
            LangEnum.Vietnamese => "vnm",
            LangEnum.Thai => "tha",
            LangEnum.Arabic => "ara",
            LangEnum.Swedish => "swe",
            LangEnum.Dutch => "dut",
            LangEnum.Polish => "pol",
            LangEnum.Ukrainian => "ukr",
            // OCR.Space 无以下语言码，回退 auto（Engine 2/3 自动检测兜底）
            LangEnum.Malay => "auto",
            LangEnum.Hindi => "auto",
            LangEnum.Indonesian => "auto",
            LangEnum.MongolianCyrillic => "auto",
            LangEnum.MongolianTraditional => "auto",
            LangEnum.Khmer => "auto",
            LangEnum.NorwegianBokmal => "auto",
            LangEnum.NorwegianNynorsk => "auto",
            LangEnum.Persian => "auto",
            LangEnum.Uzbek => "auto",
            _ => "auto"
        };
    }
}
```

注：本步 `GetSettingUI` 引用了尚未创建的 `SettingsView`/`SettingsViewModel`，会编译报错。这是预期——Task 6/7 创建它们后即可编译。为保证 Task 3 的单元测试能独立运行，临时将 `GetSettingUI` 体改为 `throw new NotImplementedException();`（仅本步），待 Task 7 完成后再恢复为正常实现。

**临时修改（仅 Task 3 期间）**：将上面 `GetSettingUI` 方法体替换为：
```csharp
    public Control GetSettingUI() => throw new NotImplementedException();
```
并移除文件顶部 `using STranslate.Plugin.Ocr.OcrSpace.View;` 与 `using STranslate.Plugin.Ocr.OcrSpace.ViewModel;` 两行（Task 7 时再加回）。

- [ ] **Step 5: 运行测试，确认通过**

Run: `dotnet test src/Tests/STranslate.Tests/STranslate.Tests.csproj --filter "FullyQualifiedName~OcrSpacePluginTests" --configuration Debug`
Expected: PASS（22 个映射用例 + 9 个回退用例全过）。

- [ ] **Step 6: 提交**

```bash
git add src/Tests/STranslate.Tests/STranslate.Tests.csproj src/Tests/STranslate.Tests/OcrSpacePluginTests.cs src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Main.cs
git commit -m "feat(ocr-space): add LangConverter with unit tests"
```

---

## Task 4: base64 前缀检测（TDD）

**Files:**
- Modify: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Main.cs` (加 `GetBase64ImagePrefix`)
- Modify: `src/Tests/STranslate.Tests/OcrSpacePluginTests.cs` (加测试)

- [ ] **Step 1: 写失败测试**

在 `OcrSpacePluginTests.cs` 末尾追加：

```csharp
    [Fact]
    public void GetBase64ImagePrefix_DetectsPng()
    {
        // PNG 魔数：89 50 4E 47
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2 };
        Assert.Equal("data:image/png;base64,", Main.GetBase64ImagePrefix(png));
    }

    [Fact]
    public void GetBase64ImagePrefix_DetectsJpeg()
    {
        // JPEG 魔数：FF D8 FF
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };
        Assert.Equal("data:image/jpeg;base64,", Main.GetBase64ImagePrefix(jpeg));
    }

    [Fact]
    public void GetBase64ImagePrefix_DefaultsToJpegForUnknown()
    {
        var unknown = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        Assert.Equal("data:image/jpeg;base64,", Main.GetBase64ImagePrefix(unknown));
    }
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test src/Tests/STranslate.Tests/STranslate.Tests.csproj --filter "FullyQualifiedName~GetBase64ImagePrefix" --configuration Debug`
Expected: FAIL，`GetBase64ImagePrefix` 未定义。

- [ ] **Step 3: 实现 GetBase64ImagePrefix**

在 `Main.cs` 的 `LangConverter` 方法之后、类结束前追加：

```csharp
    /// <summary>
    ///     根据图片魔数检测内容类型前缀，OCR.Space 要求 base64 字符串以 data:&lt;type&gt;;base64, 开头。
    /// </summary>
    internal static string GetBase64ImagePrefix(byte[] imageData)
    {
        if (imageData.Length >= 4 &&
            imageData[0] == 0x89 && imageData[1] == 0x50 && imageData[2] == 0x4E && imageData[3] == 0x47)
            return "data:image/png;base64,";

        if (imageData.Length >= 3 &&
            imageData[0] == 0xFF && imageData[1] == 0xD8 && imageData[2] == 0xFF)
            return "data:image/jpeg;base64,";

        return "data:image/jpeg;base64,";
    }
```

- [ ] **Step 4: 运行测试，确认通过**

Run: `dotnet test src/Tests/STranslate.Tests/STranslate.Tests.csproj --filter "FullyQualifiedName~GetBase64ImagePrefix" --configuration Debug`
Expected: PASS（3 个用例）。

- [ ] **Step 5: 提交**

```bash
git add src/Tests/STranslate.Tests/OcrSpacePluginTests.cs src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Main.cs
git commit -m "feat(ocr-space): add base64 image prefix detection with tests"
```

---

## Task 5: 坐标还原 BuildContentsFromLines（TDD）

**Files:**
- Modify: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Main.cs` (加 `BuildContentsFromLines` + DTO)
- Modify: `src/Tests/STranslate.Tests/OcrSpacePluginTests.cs` (加测试)

- [ ] **Step 1: 写失败测试 — 单词坐标还原**

在 `OcrSpacePluginTests.cs` 末尾追加：

```csharp
    [Fact]
    public void BuildContentsFromLines_ConvertsWordToFourBoxPoints()
    {
        // 一个 Word：Left=100, Top=50, Width=20, Height=10
        var lines = new List<Main.Line>
        {
            new()
            {
                Words =
                [
                    new Main.Word { WordText = "Hi", Left = 100, Top = 50, Width = 20, Height = 10 }
                ]
            }
        };

        var contents = Main.BuildContentsFromLines(lines);

        Assert.Single(contents);
        Assert.Equal("Hi", contents[0].Text);
        // 4 点：左上、右上、右下、左下
        Assert.Equal(4, contents[0].BoxPoints.Count);
        Assert.Equal(new BoxPoint(100, 50), contents[0].BoxPoints[0]);   // 左上
        Assert.Equal(new BoxPoint(120, 50), contents[0].BoxPoints[1]);   // 右上
        Assert.Equal(new BoxPoint(120, 60), contents[0].BoxPoints[2]);   // 右下
        Assert.Equal(new BoxPoint(100, 60), contents[0].BoxPoints[3]);   // 左下
    }

    [Fact]
    public void BuildContentsFromLines_KeepsReadingOrderAcrossLines()
    {
        var lines = new List<Main.Line>
        {
            new()
            {
                Words =
                [
                    new Main.Word { WordText = "A", Left = 0, Top = 0, Width = 5, Height = 5 }
                ]
            },
            new()
            {
                Words =
                [
                    new Main.Word { WordText = "B", Left = 10, Top = 10, Width = 5, Height = 5 },
                    new Main.Word { WordText = "C", Left = 20, Top = 10, Width = 5, Height = 5 }
                ]
            }
        };

        var contents = Main.BuildContentsFromLines(lines);

        Assert.Equal(["A", "B", "C"], contents.Select(c => c.Text));
    }

    [Fact]
    public void BuildContentsFromLines_SkipsEmptyWordsAndNullLines()
    {
        var lines = new List<Main.Line>
        {
            new()
            {
                Words =
                [
                    new Main.Word { WordText = "", Left = 0, Top = 0, Width = 5, Height = 5 },
                    new Main.Word { WordText = "OK", Left = 1, Top = 1, Width = 2, Height = 2 }
                ]
            }
        };

        var contents = Main.BuildContentsFromLines(lines);

        Assert.Single(contents);
        Assert.Equal("OK", contents[0].Text);
    }

    [Fact]
    public void BuildContentsFromLines_ReturnsEmptyForNullOrEmptyLines()
    {
        Assert.Empty(Main.BuildContentsFromLines(null));
        Assert.Empty(Main.BuildContentsFromLines(new List<Main.Line>()));
    }
```

- [ ] **Step 2: 运行测试，确认失败**

Run: `dotnet test src/Tests/STranslate.Tests/STranslate.Tests.csproj --filter "FullyQualifiedName~BuildContentsFromLines" --configuration Debug`
Expected: FAIL，`BuildContentsFromLines` 及嵌套类型 `Main.Line`/`Main.Word` 未定义。

- [ ] **Step 3: 实现 BuildContentsFromLines 与响应 DTO**

在 `Main.cs` 的 `GetBase64ImagePrefix` 方法之后追加：

```csharp
    /// <summary>
    ///     将 OCR.Space 的 TextOverlay.Lines 还原为带像素坐标的 OcrContent 列表。
    ///     每个 Word 转为 4 点包围盒（左上、右上、右下、左下），按行→词顺序保持阅读序。
    /// </summary>
    internal static List<OcrContent> BuildContentsFromLines(List<Line>? lines)
    {
        var contents = new List<OcrContent>();
        if (lines is null || lines.Count == 0)
            return contents;

        foreach (var line in lines)
        {
            if (line.Words is null || line.Words.Count == 0)
                continue;

            foreach (var word in line.Words)
            {
                if (string.IsNullOrEmpty(word.WordText))
                    continue;

                var left = word.Left;
                var top = word.Top;
                var right = word.Left + word.Width;
                var bottom = word.Top + word.Height;

                contents.Add(new OcrContent
                {
                    Text = word.WordText,
                    BoxPoints =
                    [
                        new BoxPoint(left, top),      // 左上
                        new BoxPoint(right, top),     // 右上
                        new BoxPoint(right, bottom),  // 右下
                        new BoxPoint(left, bottom)    // 左下
                    ]
                });
            }
        }

        return contents;
    }
```

并在 `Main.cs` 文件末尾（类闭合 `}` 之后、命名空间闭合之前）追加响应 DTO：

```csharp

    #region Response DTO

#pragma warning disable IDE1006 // 命名样式
    public class Word
    {
        [JsonPropertyName("WordText")] public string WordText { get; set; } = string.Empty;

        [JsonPropertyName("Left")] public float Left { get; set; }

        [JsonPropertyName("Top")] public float Top { get; set; }

        [JsonPropertyName("Height")] public float Height { get; set; }

        [JsonPropertyName("Width")] public float Width { get; set; }
    }

    public class Line
    {
        [JsonPropertyName("Words")] public List<Word> Words { get; set; } = [];

        [JsonPropertyName("MaxHeight")] public float MaxHeight { get; set; }

        [JsonPropertyName("MinTop")] public float MinTop { get; set; }
    }

    public class TextOverlay
    {
        [JsonPropertyName("Lines")] public List<Line> Lines { get; set; } = [];

        [JsonPropertyName("HasOverlay")] public bool HasOverlay { get; set; }

        [JsonPropertyName("Message")] public string? Message { get; set; }
    }

    public class ParsedResult
    {
        [JsonPropertyName("TextOverlay")] public TextOverlay? TextOverlay { get; set; }

        [JsonPropertyName("FileParseExitCode")] public int FileParseExitCode { get; set; }

        [JsonPropertyName("ParsedText")] public string? ParsedText { get; set; }

        [JsonPropertyName("ErrorMessage")] public string? ErrorMessage { get; set; }

        [JsonPropertyName("ErrorDetails")] public string? ErrorDetails { get; set; }
    }

    public class Root
    {
        [JsonPropertyName("ParsedResults")] public List<ParsedResult> ParsedResults { get; set; } = [];

        [JsonPropertyName("OCRExitCode")] public int OCRExitCode { get; set; }

        [JsonPropertyName("IsErroredOnProcessing")] public bool IsErroredOnProcessing { get; set; }

        [JsonPropertyName("ErrorMessage")] public string? ErrorMessage { get; set; }

        [JsonPropertyName("ErrorDetails")] public string? ErrorDetails { get; set; }

        [JsonPropertyName("ProcessingTimeInMilliseconds")] public string? ProcessingTimeInMilliseconds { get; set; }
    }
#pragma warning restore IDE1006 // 命名样式

    #endregion Response DTO
```

注意：DTO 的 `#region` 应置于 `Main` 类内部（与 Tencent 插件一致，DTO 作为 `Main` 的嵌套类）。确保缩进与类体对齐。

- [ ] **Step 4: 运行测试，确认通过**

Run: `dotnet test src/Tests/STranslate.Tests/STranslate.Tests.csproj --filter "FullyQualifiedName~BuildContentsFromLines" --configuration Debug`
Expected: PASS（4 个用例）。

- [ ] **Step 5: 提交**

```bash
git add src/Tests/STranslate.Tests/OcrSpacePluginTests.cs src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Main.cs
git commit -m "feat(ocr-space): add overlay coordinate restoration with tests"
```

---

## Task 6: SettingsViewModel

**Files:**
- Create: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/ViewModel/SettingsViewModel.cs`

- [ ] **Step 1: 创建 SettingsViewModel**

创建 `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/ViewModel/SettingsViewModel.cs`：

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace STranslate.Plugin.Ocr.OcrSpace.ViewModel;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IPluginContext _context;
    private readonly Settings _settings;

    [ObservableProperty] public partial string ApiKey { get; set; }

    [ObservableProperty] public partial OcrSpaceEngine Engine { get; set; }

    public List<OcrSpaceEngine> EngineOptions { get; } = [OcrSpaceEngine.Engine1, OcrSpaceEngine.Engine2, OcrSpaceEngine.Engine3];

    public SettingsViewModel(IPluginContext context, Settings settings)
    {
        _context = context;
        _settings = settings;

        ApiKey = settings.ApiKey;
        Engine = settings.Engine;

        PropertyChanged += OnSettingsPropertyChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ApiKey):
                _settings.ApiKey = ApiKey;
                break;
            case nameof(Engine):
                _settings.Engine = Engine;
                break;
            default:
                return;
        }
        _context.SaveSettingStorage<Settings>();
    }

    public void Dispose() => PropertyChanged -= OnSettingsPropertyChanged;
}
```

- [ ] **Step 2: 提交**

```bash
git add src/Plugins/STranslate.Plugin.Ocr.OcrSpace/ViewModel/SettingsViewModel.cs
git commit -m "feat(ocr-space): add settings view model"
```

---

## Task 7: SettingsView（XAML + 代码后置）

**Files:**
- Create: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/View/SettingsView.xaml`
- Create: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/View/SettingsView.xaml.cs`

- [ ] **Step 1: 创建 SettingsView.xaml**

创建 `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/View/SettingsView.xaml`：

```xml
<UserControl
    x:Class="STranslate.Plugin.Ocr.OcrSpace.View.SettingsView"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:ikw="http://schemas.inkore.net/lib/ui/wpf"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:plugin="clr-namespace:STranslate.Plugin;assembly=STranslate.Plugin"
    xmlns:s="https://github.com/zggsong/2022/xaml"
    xmlns:ui="http://schemas.inkore.net/lib/ui/wpf/modern"
    xmlns:vm="clr-namespace:STranslate.Plugin.Ocr.OcrSpace.ViewModel"
    d:DataContext="{d:DesignInstance Type=vm:SettingsViewModel}"
    d:DesignHeight="450"
    d:DesignWidth="800"
    mc:Ignorable="d">

    <ikw:SimpleStackPanel Spacing="12">
        <ui:SettingsCard Header="{DynamicResource STranslate_Plugin_Ocr_OcrSpace_ApiKey}">
            <ui:SettingsCard.HeaderIcon>
                <ui:FontIcon Icon="{x:Static ui:FluentSystemIcons.Key_24_Regular}" />
            </ui:SettingsCard.HeaderIcon>
            <PasswordBox
                MinWidth="300"
                plugin:PasswordBoxAssistant.Attach="True"
                plugin:PasswordBoxAssistant.Password="{Binding ApiKey}" />
        </ui:SettingsCard>

        <ui:SettingsCard Header="{DynamicResource STranslate_Plugin_Ocr_OcrSpace_Engine}">
            <ui:SettingsCard.HeaderIcon>
                <ui:FontIcon Icon="{x:Static ui:FluentSystemIcons.Gauge_20_Regular}" />
            </ui:SettingsCard.HeaderIcon>
            <ComboBox
                MinWidth="160"
                ItemsSource="{Binding EngineOptions}"
                SelectedItem="{Binding Engine}" />
        </ui:SettingsCard>

        <ui:SettingsCard Description="{DynamicResource STranslate_Plugin_Ocr_OcrSpace_Official_Description}" Header="{DynamicResource STranslate_Plugin_Ocr_OcrSpace_Official}">
            <ui:SettingsCard.HeaderIcon>
                <ui:FontIcon Icon="{x:Static ui:FluentSystemIcons.WebAsset_20_Regular}" />
            </ui:SettingsCard.HeaderIcon>
            <ui:HyperlinkButton Content="https://ocr.space/ocrapi" NavigateUri="https://ocr.space/ocrapi" />
        </ui:SettingsCard>
    </ikw:SimpleStackPanel>
</UserControl>
```

- [ ] **Step 2: 创建 SettingsView.xaml.cs**

创建 `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/View/SettingsView.xaml.cs`：

```csharp
namespace STranslate.Plugin.Ocr.OcrSpace.View;

public partial class SettingsView
{
    public SettingsView() => InitializeComponent();
}
```

- [ ] **Step 3: 恢复 Main.cs 的 GetSettingUI 与 using**

Task 3 临时把 `GetSettingUI` 改成了 `throw new NotImplementedException();` 并移除了 View/ViewModel 的 using。现在恢复：
1. 在 `Main.cs` 顶部加回：
```csharp
using STranslate.Plugin.Ocr.OcrSpace.View;
using STranslate.Plugin.Ocr.OcrSpace.ViewModel;
```
2. 将 `GetSettingUI` 恢复为：
```csharp
    public Control GetSettingUI()
    {
        _viewModel ??= new SettingsViewModel(Context, Settings);
        _settingUi ??= new SettingsView { DataContext = _viewModel };
        return _settingUi;
    }
```

- [ ] **Step 4: 构建插件项目，确认 View 编译通过**

Run: `dotnet build src/Plugins/STranslate.Plugin.Ocr.OcrSpace/STranslate.Plugin.Ocr.OcrSpace.csproj --configuration Debug`
Expected: BUILD SUCCEEDED。

- [ ] **Step 5: 提交**

```bash
git add src/Plugins/STranslate.Plugin.Ocr.OcrSpace/View/SettingsView.xaml src/Plugins/STranslate.Plugin.Ocr.OcrSpace/View/SettingsView.xaml.cs src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Main.cs
git commit -m "feat(ocr-space): add settings view"
```

---

## Task 8: 补全 RecognizeAsync

**Files:**
- Modify: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Main.cs` (替换 `RecognizeAsync` 占位)

- [ ] **Step 1: 实现 RecognizeAsync**

将 `Main.cs` 中的占位：
```csharp
    public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
        => throw new NotImplementedException();
```
替换为：

```csharp
    public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        var ocrResult = new OcrResult();

        // 1. 构造表单：base64Image 需带内容类型前缀
        var base64Str = Convert.ToBase64String(request.ImageData);
        var base64Image = GetBase64ImagePrefix(request.ImageData) + base64Str;

        var langCode = LangConverter(request.Language) ?? "eng";
        // Engine 1 不支持 auto，回退 eng 避免识别失败
        if (Settings.Engine == OcrSpaceEngine.Engine1 && langCode == "auto")
            langCode = "eng";

        var engine = Settings.Engine switch
        {
            OcrSpaceEngine.Engine1 => "1",
            OcrSpaceEngine.Engine2 => "2",
            OcrSpaceEngine.Engine3 => "3",
            _ => "2"
        };

        var formData = new Dictionary<string, string>
        {
            { "base64Image", base64Image },
            { "language", langCode },
            { "isOverlayRequired", "true" },
            { "scale", "true" },
            { "isTable", "true" },
            { "OCREngine", engine }
        };

        var options = new Options
        {
            Headers = new Dictionary<string, string>
            {
                { "apikey", Settings.ApiKey }
            }
        };

        // 2. POST
        var resp = await Context.HttpService.PostFormAsync(Url, formData, options, cancellationToken);
        if (string.IsNullOrEmpty(resp))
            throw new Exception("请求结果为空");

        // 3. 解析
        var parsedData = JsonSerializer.Deserialize<Root>(resp)
            ?? throw new Exception($"反序列化失败: {resp}");

        // 4. 判断错误：IsErroredOnProcessing 或 OCRExitCode 为 3/4 视为失败
        if (parsedData.IsErroredOnProcessing || parsedData.OCRExitCode is 3 or 4)
            return ocrResult.Fail(parsedData.ErrorMessage ?? parsedData.ErrorDetails ?? resp);

        // 5. 提取内容
        if (parsedData.ParsedResults is null || parsedData.ParsedResults.Count == 0)
            return ocrResult;

        foreach (var page in parsedData.ParsedResults)
        {
            // 单页失败则跳过，不中断其余页
            if (page.FileParseExitCode != 1)
                continue;

            var contents = BuildContentsFromLines(page.TextOverlay?.Lines);
            if (contents.Count > 0)
            {
                ocrResult.OcrContents.AddRange(contents);
            }
            else if (!string.IsNullOrEmpty(page.ParsedText))
            {
                // overlay 无词时回退到整页文本
                ocrResult.OcrContents.Add(new OcrContent { Text = page.ParsedText.Trim() });
            }
        }

        return ocrResult;
    }
```

- [ ] **Step 2: 构建插件项目，确认编译通过**

Run: `dotnet build src/Plugins/STranslate.Plugin.Ocr.OcrSpace/STranslate.Plugin.Ocr.OcrSpace.csproj --configuration Debug`
Expected: BUILD SUCCEEDED，无错误。

- [ ] **Step 3: 跑全部单元测试**

Run: `dotnet test src/Tests/STranslate.Tests/STranslate.Tests.csproj --filter "FullyQualifiedName~OcrSpacePluginTests" --configuration Debug`
Expected: PASS（LangConverter + GetBase64ImagePrefix + BuildContentsFromLines 全部用例）。

- [ ] **Step 4: 提交**

```bash
git add src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Main.cs
git commit -m "feat(ocr-space): implement RecognizeAsync with error handling"
```

---

## Task 9: i18n 语言文件

**Files:**
- Create: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Languages/en.xaml`
- Create: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Languages/en.json`
- Create: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Languages/zh-cn.xaml`
- Create: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Languages/zh-cn.json`
- Create: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Languages/zh-tw.xaml`
- Create: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Languages/zh-tw.json`
- Create: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Languages/ja.xaml`
- Create: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Languages/ja.json`
- Create: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Languages/ko.xaml`
- Create: `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Languages/ko.json`

i18n key 共 4 个：`STranslate_Plugin_Ocr_OcrSpace_ApiKey`、`STranslate_Plugin_Ocr_OcrSpace_Engine`、`STranslate_Plugin_Ocr_OcrSpace_Official`、`STranslate_Plugin_Ocr_OcrSpace_Official_Description`。

- [ ] **Step 1: en**

`Languages/en.xaml`：
```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:sys="clr-namespace:System;assembly=mscorlib">

    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_ApiKey">API Key</sys:String>
    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_Engine">OCR Engine</sys:String>
    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_Official">Official Website</sys:String>
    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_Official_Description">Click the link below to go to the official website to register and get a free API key.</sys:String>

</ResourceDictionary>
```

`Languages/en.json`：
```json
{
  "Name": "OCR.Space",
  "Description": "OCR.Space OCR plugin for STranslate"
}
```

- [ ] **Step 2: zh-cn**

`Languages/zh-cn.xaml`：
```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:sys="clr-namespace:System;assembly=mscorlib">

    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_ApiKey">API 密钥</sys:String>
    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_Engine">OCR 引擎</sys:String>
    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_Official">官方网站</sys:String>
    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_Official_Description">点击下面连接跳转官方网站进行注册并获取免费 API 密钥</sys:String>

</ResourceDictionary>
```

`Languages/zh-cn.json`：
```json
{
  "Name": "OCR.Space",
  "Description": "适用于 STranslate 的 OCR.Space OCR 插件"
}
```

- [ ] **Step 3: zh-tw**

`Languages/zh-tw.xaml`：
```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:sys="clr-namespace:System;assembly=mscorlib">

    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_ApiKey">API 金鑰</sys:String>
    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_Engine">OCR 引擎</sys:String>
    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_Official">官方網站</sys:String>
    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_Official_Description">點擊下面連結跳轉官方網站進行註冊並取得免費 API 金鑰</sys:String>

</ResourceDictionary>
```

`Languages/zh-tw.json`：
```json
{
  "Name": "OCR.Space",
  "Description": "適用於 STranslate 的 OCR.Space OCR 插件"
}
```

- [ ] **Step 4: ja**

`Languages/ja.xaml`：
```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:sys="clr-namespace:System;assembly=mscorlib">

    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_ApiKey">API キー</sys:String>
    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_Engine">OCR エンジン</sys:String>
    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_Official">公式ウェブサイト</sys:String>
    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_Official_Description">以下のリンクをクリックして、公式ウェブサイトで登録し無料 API キーを取得してください。</sys:String>

</ResourceDictionary>
```

`Languages/ja.json`：
```json
{
  "Name": "OCR.Space",
  "Description": "STranslate用のOCR.Space OCRプラグイン"
}
```

- [ ] **Step 5: ko**

`Languages/ko.xaml`：
```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:sys="clr-namespace:System;assembly=mscorlib">

    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_ApiKey">API 키</sys:String>
    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_Engine">OCR 엔진</sys:String>
    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_Official">공식 웹사이트</sys:String>
    <sys:String x:Key="STranslate_Plugin_Ocr_OcrSpace_Official_Description">아래 링크를 클릭하여 공식 웹사이트에서 등록하고 무료 API 키를 받으세요.</sys:String>

</ResourceDictionary>
```

`Languages/ko.json`：
```json
{
  "Name": "OCR.Space",
  "Description": "STranslate용 OCR.Space OCR 플러그인"
}
```

- [ ] **Step 6: 提交**

```bash
git add src/Plugins/STranslate.Plugin.Ocr.OcrSpace/Languages/
git commit -m "feat(ocr-space): add i18n resources for en/zh-cn/zh-tw/ja/ko"
```

---

## Task 10: 注册到解决方案并全量构建验证

**Files:**
- Modify: `src/STranslate.slnx`

- [ ] **Step 1: 在 slnx 注册插件项目**

修改 `src/STranslate.slnx`，在 OCR 插件段（Youdao 行之后）追加：

将：
```xml
    <Project Path="Plugins/STranslate.Plugin.Ocr.Youdao/STranslate.Plugin.Ocr.Youdao.csproj" />
```
改为：
```xml
    <Project Path="Plugins/STranslate.Plugin.Ocr.Youdao/STranslate.Plugin.Ocr.Youdao.csproj" />
    <Project Path="Plugins/STranslate.Plugin.Ocr.OcrSpace/STranslate.Plugin.Ocr.OcrSpace.csproj" />
```

- [ ] **Step 2: 全量构建解决方案**

Run: `dotnet build src/STranslate.slnx --configuration Debug`
Expected: BUILD SUCCEEDED，所有项目含 OcrSpace 插件编译通过，无错误。

- [ ] **Step 3: 跑全部测试**

Run: `dotnet test src/Tests/STranslate.Tests/STranslate.Tests.csproj --configuration Debug`
Expected: 全部 PASS（含新增 OcrSpace 用例与既有用例）。

- [ ] **Step 4: 确认插件产物输出**

Run: `ls src/.artifacts/Debug/Plugins/STranslate.Plugin.Ocr.OcrSpace/`
Expected: 包含 `STranslate.Plugin.Ocr.OcrSpace.dll`、`plugin.json`、`icon.png`、`Languages/` 目录。

- [ ] **Step 5: 提交**

```bash
git add src/STranslate.slnx
git commit -m "feat(ocr-space): register plugin in solution and verify build"
```

---

## Task 11: 文档与手动冒烟测试说明

**Files:**
- Modify: `docs/plugin-sdk-development.md` (在官方插件样例列表补充)

- [ ] **Step 1: 更新插件 SDK 文档关键文件清单**

在 `docs/plugin-sdk-development.md` 的「关键文件」段 OCR 相关样例后追加一行。将：
```
- `Plugins/STranslate.Plugin.Ocr.OpenAI/Main.cs`
```
改为：
```
- `Plugins/STranslate.Plugin.Ocr.OpenAI/Main.cs`
- `Plugins/STranslate.Plugin.Ocr.OcrSpace/Main.cs`
```

- [ ] **Step 2: 手动冒烟测试（需真实 API Key，非自动化）**

在应用中加载插件并验证（记录结果，不强制通过）：
1. 从 https://ocr.space 注册获取免费 API Key。
2. 在 STranslate 设置 → OCR 添加 OCR.Space 插件，填入 ApiKey，引擎默认 2。
3. 截图 OCR：对含中英文的截图执行 OCR，确认返回文本正确。
4. 图片翻译：确认插件出现在图片翻译 OCR 下拉列表，对截图执行图片翻译，确认译文覆盖位置正确。
5. 切换 Engine 1：对中文截图 OCR，确认不报 auto 错误（应回退 eng，中文可能识别差但不报错）。
6. 切换 Engine 3：确认表格类图片返回按行文本。

- [ ] **Step 3: 提交**

```bash
git add docs/plugin-sdk-development.md
git commit -m "docs: list OCR.Space plugin in SDK development doc"
```

---

## 完成标准
- 插件项目编译通过并输出到 `.artifacts/Debug/Plugins/STranslate.Plugin.Ocr.OcrSpace/`。
- 单元测试全绿：LangConverter（31 用例）、GetBase64ImagePrefix（3 用例）、BuildContentsFromLines（4 用例）。
- 解决方案全量构建与测试通过。
- 插件注册到 `STranslate.slnx`。
- 设置页可配置 ApiKey 与 Engine，5 语言 i18n 完整。
- 手动冒烟测试步骤已记录（真实 Key 验证由用户执行）。

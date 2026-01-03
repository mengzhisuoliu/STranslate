# CLAUDE.md

本文档为 Claude Code (claude.ai/code) 在处理本仓库代码时提供指导。

## 项目概述

**STranslate** 是一个基于 Windows WPF 的翻译和 OCR 工具，采用插件化架构。它通过可扩展的插件支持多种翻译服务、OCR 提供商、TTS（文本转语音）和词汇管理。

## 构建与开发命令

### 构建命令
```powershell
# 构建 Debug 配置
dotnet build src/STranslate.sln --configuration Debug

# 构建 Release 配置
dotnet build src/STranslate.sln --configuration Release

# 构建特定版本（build.ps1 使用）
dotnet build src/STranslate.sln --configuration Release /p:Version=2.0.0

# 运行构建脚本（清理、更新版本、构建、清理）
./build.ps1 -Version "2.0.0"
```

### 运行应用程序
```powershell
# 运行 Debug 构建
dotnet run --project src/STranslate/STranslate.csproj

# 或构建后直接运行可执行文件
./src/.artifacts/Debug/STranslate.exe
```

### 项目结构
```
src/
├── STranslate/                    # 主 WPF 应用程序
│   ├── Core/                     # 核心服务（PluginManager, ServiceManager 等）
│   ├── Services/                 # 应用程序服务（TranslateService, OcrService 等）
│   ├── ViewModels/               # MVVM ViewModels
│   ├── Views/                    # WPF Views/Pages
│   ├── Controls/                 # 自定义 WPF 控件
│   ├── Converters/               # 值转换器
│   └── Plugin/                   # 插件接口定义（共享）
├── STranslate.Plugin/            # 共享插件接口和模型
└── Plugins/                      # 插件实现
    ├── STranslate.Plugin.Translate.*      # 翻译插件
    ├── STranslate.Plugin.Ocr.*            # OCR 插件
    ├── STranslate.Plugin.Tts.*            # TTS 插件
    └── STranslate.Plugin.Vocabulary.*     # 词汇插件
```

## 架构

### 核心架构流程

1. **应用程序启动** (`App.xaml.cs:296-319`)
   - 通过 `SingleInstance<App>` 强制单实例
   - Velopack 更新检查
   - 设置加载（Settings, HotkeySettings, ServiceSettings）
   - DI 容器设置（Microsoft.Extensions.Hosting）
   - 通过 `PluginManager` 加载插件
   - 通过 `ServiceManager` 初始化服务

2. **插件系统** (`Core/PluginManager.cs`)
   - 插件从两个目录加载：
     - `PreinstalledDirectory`: 内置插件，在 `Plugins/` 文件夹
     - `PluginsDirectory`: 用户安装的插件，在数据目录
   - 每个插件是一个 `.spkg` 文件（ZIP 压缩包），包含：
     - `plugin.json` - 元数据
     - 插件 DLL
     - 可选资源（图标、语言文件）
   - 插件实现 `IPlugin` 接口及其子类型：
     - `ITranslatePlugin` - 翻译服务
     - `IOcrPlugin` - OCR 服务
     - `ITtsPlugin` - 文本转语音
     - `IVocabularyPlugin` - 词汇管理
     - `IDictionaryPlugin` - 字典查询

3. **服务管理** (`Core/ServiceManager.cs`)
   - `Service` 是包装插件的运行时实例
   - 服务从 `PluginMetaData` 创建并存储在 `ServiceData`（持久化配置）中
   - 四种服务类型：翻译、OCR、TTS、词汇
   - 服务在启动时从设置和插件元数据加载

4. **插件生命周期**
   ```
   PluginManager.LoadPlugins()
   → 扫描插件目录
   → 从 plugin.json 提取元数据
   → 通过 PluginAssemblyLoader 加载程序集
   → 查找 IPlugin 实现
   → 创建带类型信息的 PluginMetaData

   ServiceManager.LoadServices()
   → 遍历设置中的每个 ServiceData
   → 与 PluginMetaData 匹配
   → 创建 Service 实例
   → 调用 Service.Initialize() → Plugin.Init(IPluginContext)
   ```

### 关键接口

**IPlugin** (基础接口)
- `Init(IPluginContext context)` - 使用上下文初始化
- `GetSettingUI()` - 返回 UserControl 用于设置
- `Dispose()` - 清理

**IPluginContext** (提供给插件)
- `MetaData`, `Logger`, `HttpService`, `AudioPlayer`, `Snackbar`, `Notification`
- `LoadSettingStorage<T>()` / `SaveSettingStorage<T>()` - 持久化存储
- `GetTranslation(key)` - i18n 支持

**ITranslatePlugin** (翻译插件)
- `TranslateAsync(TranslateRequest, TranslateResult)` - 核心翻译
- `GetSourceLanguage()` / `GetTargetLanguage()` - 语言映射
- `TransResult` / `TransBackResult` - 结果属性

**IOcrPlugin** (OCR 插件)
- `RecognizeAsync(OcrRequest)` - 图像转文本
- `SupportedLanguages` - 可用语言

### 数据流：翻译示例

1. 用户触发翻译（快捷键、UI）
2. `TranslateService` 获取激活的服务
3. 对于每个 `Service`：
   - 创建 `TranslateRequest(text, sourceLang, targetLang)`
   - 调用 `plugin.TranslateAsync(request, result)`
   - 插件使用 `IPluginContext.HttpService` 进行 API 调用
   - 结果更新 `TranslateResult` 属性（ObservableObject）
4. UI 绑定到 `TranslateResult.Text`, `IsProcessing`, `IsSuccess`

### 设置与存储

**设置架构** (`StorageBase<T>`)
- JSON 序列化，原子写入（`.tmp` + `.bak` 备份）
- 位于 `DataLocation.DataDirectory()`：
  - 便携模式：`./PortableConfig/`
  - 漫游模式：`%APPDATA%\STranslate\`

**主要设置文件**
- `Settings.json` - 通用设置
- `HotkeySettings.json` - 快捷键配置
- `ServiceSettings.json` - 服务配置（启用、顺序、选项）

**插件存储**
- 设置：`%APPDATA%\STranslate\Settings\Plugins\{PluginName}_{PluginID}\`
- 缓存：`%APPDATA%\STranslate\Cache\Plugins\{PluginName}_{PluginID}\`
- 通过 `IPluginContext.LoadSettingStorage<T>()` 访问

### 插件包格式 (.spkg)

`.spkg` 文件是 ZIP 压缩包，包含：
```
plugin.json          # 元数据
YourPlugin.dll       # 主程序集
icon.png            # 可选图标
Languages/*.xaml     # 可选 i18n 文件
```

**plugin.json 示例:**
```json
{
  "PluginID": "unique-id",
  "Name": "Plugin Name",
  "Author": "Author",
  "Version": "1.0.0",
  "Description": "Description",
  "Website": "https://example.com",
  "ExecuteFileName": "YourPlugin.dll",
  "IconPath": "icon.png"
}
```

## 常见开发任务

### 添加新的插件类型
1. 在 `STranslate.Plugin/` 中定义接口（例如 `IMyPlugin.cs`）
2. 添加到 `ServiceType` 枚举（如果是新的服务类别）
3. 更新 `BaseService.LoadPlugins<T>()` 以加载该类型
4. 更新 `ServiceManager.CreateService()` 以处理该类型
5. 在 `Services/` 中创建服务类（例如 `MyService.cs`）

### 修改核心服务
- **TranslateService**: `src/STranslate/Services/TranslateService.cs`
- **OcrService**: `src/STranslate/Services/OcrService.cs`
- **TtsService**: `src/STranslate/Services/TtsService.cs`
- **VocabularyService**: `src/STranslate/Services/VocabularyService.cs`

### UI 更改
- Views 在 `src/STranslate/Views/`
- ViewModels 在 `src/STranslate/ViewModels/`
- 使用 CommunityToolkit.Mvvm 进行 MVVM
- 使用 iNKORE.UI.WPF.Modern 用于现代 UI 组件

### 调试插件加载
检查日志文件 `%APPDATA%\STranslate\Logs\{Version}\.log`：
- 插件发现
- 程序集加载
- 类型解析
- 初始化错误

### 测试插件安装
1. 构建插件为 `.spkg`（带 plugin.json 的 ZIP）
2. 使用 UI：设置 → 插件 → 安装
3. 或放在 `Plugins/` 目录作为预安装插件

## 重要文件

| 文件 | 用途 |
|------|---------|
| `src/STranslate/App.xaml.cs` | 应用程序入口、DI 设置、生命周期 |
| `src/STranslate/Core/PluginManager.cs` | 插件发现、加载、安装 |
| `src/STranslate/Core/ServiceManager.cs` | 服务创建、生命周期 |
| `src/STranslate/Services/BaseService.cs` | 所有服务类型的基础 |
| `src/STranslate.Plugin/IPlugin.cs` | 核心插件接口 |
| `src/STranslate.Plugin/PluginMetaData.cs` | 插件元数据模型 |
| `src/STranslate.Plugin/Service.cs` | 运行时服务实例 |
| `build.ps1` | Release 构建脚本 |
| `Directory.Packages.props` | 集中式 NuGet 版本 |

## 关键依赖

- **WPF 框架**: .NET 10.0-windows
- **MVVM**: CommunityToolkit.Mvvm
- **UI**: iNKORE.UI.WPF.Modern（现代控件/主题）
- **DI**: Microsoft.Extensions.*
- **日志**: Serilog
- **快捷键**: NHotkey.Wpf, MouseKeyHook
- **HTTP**: System.Net.Http（支持代理）
- **存储**: Microsoft.Data.Sqlite（历史数据库）
- **更新**: Velopack
- **插件加载**: System.Reflection.MetadataLoadContext
- **IL 织入**: Costura.Fody（程序集合并）, MethodBoundaryAspect.Fody

## 给 Claude 的注意事项

- 这是一个 **仅限 Windows 的 WPF 应用程序**（使用 Windows 特定 API）
- 插件在运行时从单独的 DLL **动态加载**
- 所有插件接口都在 `STranslate.Plugin` 项目中，与主应用程序共享
- 设置使用**原子写入**和备份文件
- 应用程序支持**便携模式**（创建 `PortableConfig/` 文件夹）
- 预安装插件在 `src/Plugins/` 并复制到输出
- 用户插件位于 `%APPDATA%\STranslate\Plugins\`
- 插件实例**按服务创建**（非单例）
- 使用 `IPluginContext` 获取插件功能（不要直接传递应用程序服务）
- 预安装插件 ID 定义在 `Constant.cs:56-74`
- 插件程序集加载使用 `PluginAssemblyLoader` 和 `System.Reflection.MetadataLoadContext`
- 服务被包装在 `Service` 类中，包含 `Plugin`, `MetaData`, `Context` 和 `Options`
- 翻译插件可以扩展 `TranslatePluginBase` 或 `LlmTranslatePluginBase` 以获得 LLM 功能
- 应用程序使用 Fody 织入器（Costura.Fody 用于程序集合并，MethodBoundaryAspect.Fody 用于 AOP）

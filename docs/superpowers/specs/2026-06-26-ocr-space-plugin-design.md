# OCR.Space 插件设计

## 背景与目标
为 STranslate 新增 OCR.Space 插件，复用免费 OCR API（`https://api.ocr.space/parse/image`），实现 `IOcrPlugin` 接口，支持纯文本 OCR 与图片翻译（返回像素坐标框）。

## 设计决策（已确认）
- **图片翻译**：支持坐标框。`SupportBoxPoints() => true`，请求带 `isOverlayRequired=true`，解析每行 `Words` 的 `Left/Top/Width/Height` 还原为像素坐标。插件会出现在图片翻译 OCR 下拉列表。
- **OCR 引擎**：设置页下拉可选 Engine 1/2/3，默认 Engine 2（all-round 最佳）。
- **端点**：固定免费端点 `https://api.ocr.space/parse/image`，不暴露 URL 配置。

## API 接入要点（来源 https://ocr.space/ocrapi）
- 请求：`POST https://api.ocr.space/parse/image`
- 认证：`apikey` 放在请求头（`apikey: <API Key>`）。
- 入参（form 字段）：
  - `base64Image`：图片 Base64，需带前缀 `data:image/jpeg;base64,<data>`（OCR.Space 要求）。统一按 jpeg 前缀构造，服务端按内容识别实际格式。
  - `language`：3 字母语言码（如 `eng`/`chs`/`cht`/`jpn`/`kor`...）；Engine 2/3 支持 `auto`。无匹配语言时传 `eng`。
  - `isOverlayRequired`：`true`（为支持坐标框）。
  - `OCREngine`：1/2/3，来自设置。
  - `scale`：`true`（提升低分辨率图效果，与首页 demo 一致）。
  - `isTable`：`true`（保证按行返回，利于坐标对齐）。
- 选择 `base64Image` 而非 multipart `file`：宿主 `PostFormAsync` 使用 `FormUrlEncodedContent`（`application/x-www-form-urlencoded`），不支持二进制文件上传，base64 字符串方式与此契合，且与 Youdao OCR 插件实现一致。
- 响应 JSON 关键字段：
  - `OCRExitCode`：1 成功 / 2 部分成功 / 3 全失败 / 4 致命错误。
  - `IsErroredOnProcessing`：是否出错。
  - `ErrorMessage` / `ErrorDetails`：错误信息。
  - `ParsedResults[]`：
    - `FileParseExitCode`：1 成功；其他为错误码（0/-10/-20/-30/-99）。
    - `ParsedText`：整页文本。
    - `TextOverlay.Lines[]`：每行 `Words[]`，每个 Word 含 `WordText`、`Left`、`Top`、`Height`、`Width`，`MaxHeight`、`MinTop`。
    - `ErrorMessage` / `ErrorDetails`：该页错误信息。

## 坐标还原策略
OCR.Space 的 Word 坐标为像素坐标，直接对应传入图片像素空间，无需归一化换算。
对每个 `Line`，遍历其 `Words`，将每个 Word 转为 4 点 `BoxPoint`（左上、右上、右下、左下）：
- 左上 `(Left, Top)`
- 右上 `(Left + Width, Top)`
- 右下 `(Left + Width, Top + Height)`
- 左下 `(Left, Top + Height)`
每个 Word 作为一个 `OcrContent`（文本=`WordText`，坐标=上述 4 点），追加到 `OcrResult.OcrContents`。
按行顺序追加，保持阅读顺序。若 `Words` 为空则跳过该行。
引擎 3 overlay 坐标精度略低但仍可用；不特殊处理，保持与 1/2 一致逻辑。

## 语言映射（LangEnum → OCR.Space 3 字母码）
OCR.Space 支持的语言码：`ara/bul/chs/cht/hrv/cze/dan/dut/eng/fin/fre/ger/gre/hun/kor/ita/jpn/pol/por/rus/slv/spa/swe/tha/tur/ukr/vnm`，Engine 2/3 支持 `auto`。

映射表：
| LangEnum | 码 |
| --- | --- |
| Auto | auto |
| ChineseSimplified | chs |
| ChineseTraditional | cht |
| Cantonese | cht |
| English | eng |
| Japanese | jpn |
| Korean | kor |
| French | fre |
| Spanish | spa |
| Russian | rus |
| German | ger |
| Italian | ita |
| Turkish | tur |
| PortuguesePortugal | por |
| PortugueseBrazil | por |
| Vietnamese | vnm |
| Thai | tha |
| Arabic | ara |
| Swedish | swe |
| Dutch | dut |
| Polish | pol |
| Ukrainian | ukr |
| 其余（Malay/Hindi/Indonesian/Mongolian*/Khmer/Persian/Uzbek/Cantonese 之外无码语言） | auto（Engine 2/3 自动检测兜底） |

> 注：OCR.Space 无马来语、印地语、印尼语、挪威语等码，统一回退 `auto`。
> Cantonese 回退 `cht`。

引擎 1 不支持 `auto`：当选 Engine 1 且语言映射结果为 `auto` 时，回退为 `eng`，避免 1 引擎识别失败。

## 支持语言列表（SupportedLanguages）
返回上面映射表覆盖的 LangEnum 集合（与映射项一致），其余语言不暴露，避免无意义选项。

## 配置模型（Settings）
```csharp
public class Settings
{
    public string ApiKey { get; set; } = string.Empty;
    public OcrSpaceEngine Engine { get; set; } = OcrSpaceEngine.Engine2;
}

public enum OcrSpaceEngine { Engine1, Engine2, Engine3 }
```
引擎映射到 API 的 1/2/3。

## 组件清单（新建插件项目 `STranslate.Plugin.Ocr.OcrSpace`）
路径 `src/Plugins/STranslate.Plugin.Ocr.OcrSpace/`：
- `Main.cs`：实现 `IOcrPlugin`，含 `RecognizeAsync`、`LangConverter`、响应 DTO。
- `Settings.cs`：配置模型 + `OcrSpaceEngine` 枚举。
- `ViewModel/SettingsViewModel.cs`：绑定 ApiKey、Engine，属性变更持久化。
- `View/SettingsView.xaml(.cs)`：设置面板（ApiKey PasswordBox + Engine 下拉 + 官网链接）。
- `Languages/{en,zh-cn,zh-tw,ja,ko}.{xaml,json}`：5 语言 i18n。
- `plugin.json`：含固定 PluginID（新 GUID）、Name、Description、Version 1.0.0、ExecuteFileName。
- `icon.png`：图标（占位复用现有，后续可替换）。
- `STranslate.Plugin.Ocr.OcrSpace.csproj`：仿 Youdao 结构，TargetFramework net10.0-windows，UseWPF，引用 `STranslate.Plugin.csproj`，输出到 `.artifacts/{Debug,Release}/Plugins/STranslate.Plugin.Ocr.OcrSpace/`。

## 解决方案注册
在 `src/STranslate.slnx` 的 `/Plugins/` → 「OCR 插件」注释段追加一行项目引用。

## 错误处理
- 响应为空 → 抛 `请求结果为空`。
- `IsErroredOnProcessing == true` 或 `OCRExitCode` 为 3/4 → `OcrResult.Fail(ErrorMessage)`。
- `ParsedResults` 为空 → 返回空结果（成功但无文本）。
- 单页 `FileParseExitCode != 1` → 跳过该页，不中断其余页。

## 测试策略
- 单元：`LangConverter` 映射正确性；引擎 1 + auto 回退 eng。
- 解析：用文档示例 JSON 验证坐标还原（Word→4 点）与 `OcrContents` 顺序。
- 手动：真实 API Key 对截图做 OCR 与图片翻译冒烟测试（受限于免费额度，开发期适度）。

## 范围外（YAGNI）
- 不实现 Searchable PDF 生成。
- 不暴露 PRO 端点 URL 配置。
- 不实现 multipart file 上传方式。

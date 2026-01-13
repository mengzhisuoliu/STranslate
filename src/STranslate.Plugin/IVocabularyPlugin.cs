namespace STranslate.Plugin;

/// <summary>
/// 生词本插件接口
/// </summary>
public interface IVocabularyPlugin : IPlugin
{
    /// <summary>
    /// 保存生词
    /// </summary>
    /// <param name="text"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<VocabularyResult> SaveAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存生词并附带笔记
    /// </summary>
    Task<VocabularyResult> SaveWithNoteAsync(string word, string note, CancellationToken cancellationToken = default)
    {
        return SaveAsync(word, cancellationToken);
    }
}

/// <summary>
/// 生词本结果
/// </summary>
public class VocabularyResult
{
    /// <summary>
    /// 耗时
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; set; } = true;

    /// <summary>
    /// 错误信息
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// 失败
    /// </summary>
    /// <param name="msg"></param>
    /// <returns></returns>
    public VocabularyResult Fail(string msg)
    {
        IsSuccess = false;
        ErrorMessage = msg;
        return this;
    }
}
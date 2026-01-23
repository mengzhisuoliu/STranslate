using Gma.System.MouseKeyHook;
using System.Collections.Concurrent;
using System.Windows.Input;

namespace STranslate.Helpers;

public static class GlobalKeyboardHelper
{
    private static IKeyboardMouseEvents? _hook;
    private static bool _started;

    /// <summary>
    /// 当前按下的按键状态表
    /// </summary>
    private static readonly ConcurrentDictionary<Key, bool> _keyStates = new();
    private static readonly HashSet<Key> _ignoredKeyUps = [];

    public static event Action<Key>? KeyDown;
    public static event Action<Key>? KeyUp;

    /// <summary>
    /// 启动全局键盘监听
    /// </summary>
    public static void Start()
    {
        if (_started)
            return;

        _hook = Hook.GlobalEvents();
        _hook.KeyDown += OnKeyDown;
        _hook.KeyUp += OnKeyUp;

        _started = true;
    }

    /// <summary>
    /// 停止全局键盘监听
    /// </summary>
    public static void Stop()
    {
        if (!_started)
            return;

        if (_hook != null)
        {
            _hook.KeyDown -= OnKeyDown;
            _hook.KeyUp -= OnKeyUp;
            _hook.Dispose();
            _hook = null;
        }

        _keyStates.Clear();
        _started = false;
    }

    /// <summary>
    /// 某个按键是否处于按下状态
    /// </summary>
    public static bool IsKeyPressed(Key key)
    {
        return _keyStates.TryGetValue(key, out var pressed) && pressed;
    }

    /// <summary>
    /// 是否同时按下多个按键
    /// </summary>
    public static bool IsPressed(params Key[] keys)
    {
        foreach (var key in keys)
        {
            if (!IsKeyPressed(key))
                return false;
        }
        return true;
    }

    public static void IgnoreNextKeyUp(Key key)
    {
        _ignoredKeyUps.Add(key);
    }

    private static void OnKeyDown(object? sender, System.Windows.Forms.KeyEventArgs e)
    {
        var key = ConvertKey(e.KeyCode);

        // 如果这个键已经处于按下状态,则忽略重复的KeyDown事件
        if (_keyStates.TryGetValue(key, out var pressed) && pressed)
            return;

        _keyStates[key] = true;

        KeyDown?.Invoke(key);
    }

    private static void OnKeyUp(object? sender, System.Windows.Forms.KeyEventArgs e)
    {
        var key = ConvertKey(e.KeyCode);

        // 检查是否需要忽略此次 KeyUp
        if (_ignoredKeyUps.Remove(key))
            return;

        // 如果这个键已经处于松开状态,则忽略重复的KeyUp事件
        if (_keyStates.TryGetValue(key, out var pressed) && !pressed)
            return;

        _keyStates[key] = false;

        KeyUp?.Invoke(key);
    }

    private static Key ConvertKey(System.Windows.Forms.Keys winFormsKey)
    {
        return KeyInterop.KeyFromVirtualKey((int)winFormsKey);
    }
}
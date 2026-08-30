using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// 2P用の打鍵入力の専用スレッド。DrumInput(1P)と同様に、設定画面の不変キー割り当てスナップショットを
/// 約1000Hzでポーリングする。設定変更中でも可変Listを直接列挙しないため、入力スレッドと競合しない。
/// </summary>
public static class DrumInput2
{
    public readonly record struct Hit(bool IsDon, bool IsLeft, long Ticks);

    private static readonly ConcurrentQueue<Hit> _queue = new();
    // 設定画面で複数キーを追加できるため、1Pと同じく余裕を持った追跡領域を確保する。
    private static readonly bool[] _prev = new bool[22];
    private static Thread? _thread;
    private static volatile bool _run;
    private static IntPtr _hwnd;

    public static volatile bool Enabled;
    public static volatile bool SoundEnabled;

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uPeriod);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uPeriod);

    /// <summary>
    /// 💡 呼び出し側でDrumInput.Start(hwnd)と同じタイミングで一緒に呼ぶこと(Program.cs等)。
    /// このクラス単体では自動的には起動しない。
    /// </summary>
    public static void Start(IntPtr hwnd)
    {
        if (_thread != null) return;
        _hwnd = hwnd;
        _run = true;
        _thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.Highest };
        _thread.Start();
    }

    public static void Stop()
    {
        _run = false;
        _thread?.Join(500);
        _thread = null;
    }

    // DrumInputと同じレート制限つきディスパッチ方式(詳細はDrumInput.csのコメント参照)
    public static volatile int MaxHitsPerSecond = 60;

    private static readonly Queue<Hit> _pending = new();
    private static long _nextDispatchTicks;
    private static bool _hasDispatched;

    public static bool TryDequeueRateLimited(out Hit hit)
    {
        while (_queue.TryDequeue(out var raw))
            _pending.Enqueue(raw);

        hit = default;
        if (_pending.Count == 0) return false;

        int maxHz = MaxHitsPerSecond;
        if (maxHz <= 0)
        {
            hit = _pending.Dequeue();
            return true;
        }

        long now = Stopwatch.GetTimestamp();
        if (_hasDispatched && now < _nextDispatchTicks) return false;

        long minIntervalTicks = Stopwatch.Frequency / maxHz;
        hit = _pending.Dequeue();
        long baseTime = (_hasDispatched && _nextDispatchTicks > now - minIntervalTicks)
            ? _nextDispatchTicks : now;
        _nextDispatchTicks = baseTime + minIntervalTicks;
        _hasDispatched = true;
        return true;
    }

    public static void Clear()
    {
        while (_queue.TryDequeue(out _)) { }
        _pending.Clear();
        _hasDispatched = false;
    }

    private const long FOCUS_RECHECK_INTERVAL_MS = 16;
    private static bool _cachedFocused;
    private static long _nextFocusCheckTicks;

    private static void Loop()
    {
        timeBeginPeriod(1);
        try
        {
            long ticksPerMs = Stopwatch.Frequency / 1000;
            while (_run)
            {
                long now = Stopwatch.GetTimestamp();

                if (now >= _nextFocusCheckTicks)
                {
                    _cachedFocused = GetForegroundWindow() == _hwnd;
                    _nextFocusCheckTicks = now + FOCUS_RECHECK_INTERVAL_MS * ticksPerMs;
                }
                bool focused = _cachedFocused;

                // 2Pキー設定は設定変更時に配列ごと差し替えられる。可変Listは直接列挙しない。
                var bindings = SettingsPanel.GetKeyBindingSnapshot();
                int keyIndex = 0;
                foreach (int vk in bindings.P2Left)
                    Poll(keyIndex++, vk, isDon: true, isLeft: true, focused, now);
                foreach (int vk in bindings.P2Right)
                    Poll(keyIndex++, vk, isDon: true, isLeft: false, focused, now);
                foreach (int vk in bindings.P2EdgeL)
                    Poll(keyIndex++, vk, isDon: false, isLeft: true, focused, now);
                foreach (int vk in bindings.P2EdgeR)
                    Poll(keyIndex++, vk, isDon: false, isLeft: false, focused, now);

                Thread.Sleep(1);
            }
        }
        finally
        {
            timeEndPeriod(1);
        }
    }

    private static void Poll(int i, int vk, bool isDon, bool isLeft, bool focused, long now)
    {
        bool down = focused && (GetAsyncKeyState(vk) & 0x8000) != 0;
        if (down && !_prev[i] && Enabled)
        {
            _queue.Enqueue(new Hit(isDon, isLeft, now));
        }
        _prev[i] = down;
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// 打鍵入力の専用スレッド。約1000HzでGetAsyncKeyStateをポーリングし、
/// 押下エッジをStopwatchタイムスタンプ付きでキューに積む（判定精度がフレームレートに縛られない）。
/// 打鍵音もこのスレッドから直接鳴らすため、応答はポーリング約1ms+出力バッファのみ。
/// </summary>
public static class DrumInput
{
    public readonly record struct Hit(bool IsDon, bool IsLeft, long Ticks);

    private const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02;

    private static readonly ConcurrentQueue<Hit> _queue = new();
    private static readonly bool[] _prev = new bool[22];
    private static Thread? _thread;
    private static volatile bool _run;
    private static IntPtr _hwnd;

    public static volatile bool Enabled;
    public static volatile bool SoundEnabled;
    public static SoundEffect? DonSound;
    public static SoundEffect? KaSound;

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uPeriod);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uPeriod);

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

    public static bool TryDequeue(out Hit hit) => _queue.TryDequeue(out hit);

    // 💡 全入力共通のレート制限つきディスパッチ（間引かず遅延させる方式）。
    // 秒速MaxHitsPerSecondを超える速さで叩いても入力そのものは失われない。
    // 例: 60Hz上限で秒速120で1秒間叩いた場合 → 120打ぶんの入力は消えず、
    //     以降2秒かけて60打/秒のペースで少しずつ処理される（TaikoNautsと同じ挙動）。
    // 0以下を指定すると無制限（従来通り、TryDequeueと同じ動作）になる。
    public static volatile int MaxHitsPerSecond = 60;

    // 生キュー(_queue)から取り出せた分をいったん溜めておく保留キュー。
    // ここに積まれた順序はそのまま維持され、レート制限に従って少しずつ吐き出される。
    private static readonly Queue<Hit> _pending = new();
    private static long _nextDispatchTicks;
    private static bool _hasDispatched;

    /// <summary>
    /// レート制限を適用しつつ1件取り出す。TryDequeueの代わりにEnso側から毎フレーム呼ぶ想定。
    /// レート制限に引っかかっている間はfalseを返す(=その回は取り出せないだけで、次回以降のフレームで
    /// 順番に取り出せる。入力自体は_pendingに保持されたまま失われない)。
    /// </summary>
    public static bool TryDequeueRateLimited(out Hit hit)
    {
        // 生キューに新規に届いている分をすべて保留キューへ移す(順序維持)
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
        // 💡 前回の予定時刻が現在より古くなっている場合（連打が止まって間が空いた後など）は
        //    now を起点にリセットする。古い予定時刻を積み増すと制限が即解除されてしまう。
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

    private static int _keyIndex = 0;
    // 💡 GetForegroundWindow()はカーネルを跨ぐウィンドウ管理系のAPIで、GetAsyncKeyStateに比べて
    //    コストが高い。フォーカス状態は1ms単位で変わるものではないので、毎ティック呼ばずに
    //    間引いて(約16ms=60Hzごとに)再チェックする。打鍵の取得自体は引き続き1msポーリングのまま。
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
                _keyIndex = 0;
                long now = Stopwatch.GetTimestamp();

                if (now >= _nextFocusCheckTicks)
                {
                    _cachedFocused = GetForegroundWindow() == _hwnd;
                    _nextFocusCheckTicks = now + FOCUS_RECHECK_INTERVAL_MS * ticksPerMs;
                }
                bool focused = _cachedFocused;

                // 設定画面の可変Listは直接列挙しない。更新時に丸ごと差し替えられる配列を1回取得する。
                // これによりキー設定を変更中でもCollection was modified例外を起こさない。
                var bindings = SettingsPanel.GetKeyBindingSnapshot();
                foreach (int vk in bindings.Left)
                {
                    Poll(_keyIndex++, vk, isDon: true, isLeft: true, focused, now);
                }
                foreach (int vk in bindings.Right)
                {
                    Poll(_keyIndex++, vk, isDon: true, isLeft: false, focused, now);
                }
                foreach (int vk in bindings.EdgeL)
                {
                    Poll(_keyIndex++, vk, isDon: false, isLeft: true, focused, now);
                }
                foreach (int vk in bindings.EdgeR)
                {
                    Poll(_keyIndex++, vk, isDon: false, isLeft: false, focused, now);
                }
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
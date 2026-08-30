using System;
using System.Numerics;
using Raylib_cs;

/// <summary>
/// 連打（ドラムロール）中に表示する「連打!!」ポップアップ。
/// Roll_Base.png は縦に5コマ並んだ扇のパラパラアニメ（棒→扇が開いていく）で、
/// 上から下へ順にコマ送りして再生し、開き切ったら最終コマで停止したまま
/// 何もしない。連打が終端（rollEnd）に到達したら即座には消えず、1秒待ってから
/// 今度は下→上へ（最終コマ→棒）コマ送りして閉じるアニメーションを再生して消える。
/// Roll_Number.png は横に0〜9が並んだ数字素材で、開いている間の連打数を表示する。
/// </summary>
public static class RendaPop
{
    // ---- 表示位置・大きさ調整（1920x1080基準）。自由に変更可 ----
    // X: 扇の左右中央、Y: 扇の上端の座標
    public static float X = 618f;
    public static float Y = 25f;
    // 幅・高さを別々に拡大縮小できる
    public static float ScaleX = 1f;
    public static float ScaleY = 1f;

    // 数字（連打数）のX座標（中央基準）
    public static float NumberX = 610f;
    // 数字のY座標（上端基準、絶対座標。扇のYには依存しない）
    public static float NumberY = 125f;
    // 数字の大きさ（扇とは独立して幅・高さを調整可能）
    public static float NumberScaleX = 1f;
    public static float NumberScaleY = 1f;
    // 数字1文字ごとの送り幅の補正（マイナスにするほど詰まる。素材の余白分を差し引く）
    public static float NumberSpacing = -10f;

    public static float P2X = 618f;
    public static float P2Y = 825f;
    public static float P2ScaleX = 1f;
    public static float P2ScaleY = 1f;
    public static float P2NumberX = 610f;
    public static float P2NumberY = 925f;
    public static float P2NumberScaleX = 1f;
    public static float P2NumberScaleY = 1f;
    public static float P2NumberSpacing = -10f;

    // ---- 連打数が増えるたびに数字が跳ねる演出（MiniTaikoのコンボポップと同様） ----
    // 💡 フレームテーブル駆動アニメ (60fps固定、打鍵のたびに先頭へリセット)
    //    Combo_Scale = 1, 1.1, 1.2, 1.17, 1.144, 1.115, 1.086, 1.057, 1.029, 1
    //    拡大率は digitH * NumberScaleY (baseH) に対する乗数。テーブル末尾の 1.0 で静止。
    private static readonly float[] NumberScaleFrames =
        { 1.000f, 1.100f, 1.200f, 1.170f, 1.144f, 1.115f, 1.086f, 1.057f, 1.029f, 1.000f };
    private const double NUMBER_FRAME_DT = 1.0 / 60.0; // 1フレーム = 1/60 秒

    private const int FRAME_COUNT = 5;
    // 開くアニメ（0→最終コマ）の再生時間
    private const double OPEN_DURATION = 0.15;
    // 連打終了後、閉じ始めるまでの待機時間
    private const double WAIT_DURATION = 2.0;
    // 閉じるアニメ（最終コマ→0コマ目）の再生時間
    private const double CLOSE_DURATION = 0.15;

    private const int DIGIT_COUNT = 10;

    private static Texture2D _baseTex;
    private static Texture2D _numTex;
    private static bool _loaded;

    private enum State { Hidden, Opening, Open, Waiting, Closing }
    private static State _state = State.Hidden;
    private static double _stateStartSec;

    // 連打数ポップ用の状態
    private static int _lastHitCount;
    private static double _numAnimStart = -1.0;
    private static State _p2State = State.Hidden;
    private static double _p2StateStartSec;
    private static int _p2LastHitCount;
    private static double _p2NumAnimStart = -1.0;

    public static void Init()
    {
        if (!_loaded)
        {
            _baseTex = Raylib.LoadTexture("Lumen/1.Enso/rendaPop/Roll_Base.png");
            _numTex = Raylib.LoadTexture("Lumen/1.Enso/rendaPop/Roll_Number.png");
            VramProbe.Track("RendaPop.Init (all)");
            _loaded = true;
        }
        _state = State.Hidden;
        _p2State = State.Hidden;
        _p2LastHitCount = 0;
        _p2NumAnimStart = -1.0;
    }

    public static void Unload()
    {
        if (_loaded)
        {
            if (_baseTex.Id != 0) Raylib.UnloadTexture(_baseTex);
            if (_numTex.Id != 0) Raylib.UnloadTexture(_numTex);
            _loaded = false;
        }
        _state = State.Hidden;
        _p2State = State.Hidden;
    }

    /// <summary>連打開始時に呼ぶ。開くアニメーションを頭から再生する。</summary>
    public static void Show(double nowSec)
    {
        _state = State.Opening;
        _stateStartSec = nowSec;
        _lastHitCount = 0;
        _numAnimStart = -1.0;
    }

    /// <summary>
    /// 連打が終端に到達した/中断された時に呼ぶ。
    /// 即座には消えず、1秒待ってから下から上へ閉じるアニメーションを再生して消える。
    /// </summary>
    public static void Hide(double nowSec)
    {
        if (_state == State.Hidden || _state == State.Waiting || _state == State.Closing) return;
        _state = State.Waiting;
        _stateStartSec = nowSec;
    }

    /// <summary>
    /// 待機・閉じるアニメーションを挟まず即座に非表示にする。
    /// 風船（風船・くすだま）開始時など、連打ポップアップを絶対に出したくない場面で使う。
    /// </summary>
    public static void ForceHide()
    {
        _state = State.Hidden;
    }

    /// <summary>2P連打開始時に呼ぶ。1P状態には影響しない。</summary>
    public static void ShowP2(double nowSec)
    {
        _p2State = State.Opening;
        _p2StateStartSec = nowSec;
        _p2LastHitCount = 0;
        _p2NumAnimStart = -1.0;
    }

    public static void HideP2(double nowSec)
    {
        if (_p2State == State.Hidden || _p2State == State.Waiting || _p2State == State.Closing) return;
        _p2State = State.Waiting;
        _p2StateStartSec = nowSec;
    }

    public static void ForceHideP2() => _p2State = State.Hidden;

    public static void Draw(int hitCount, float vx, float vy, float s, double nowSec)
    {
        if (!_loaded || _state == State.Hidden || _baseTex.Id == 0) return;

        double elapsed = nowSec - _stateStartSec;
        if (elapsed < 0) elapsed = 0;

        // 状態遷移（1フレームにつき1段階進める想定。開閉時間は十分長いため通常問題ない）
        if (_state == State.Opening && elapsed >= OPEN_DURATION)
        {
            _state = State.Open;
            _stateStartSec = nowSec;
            elapsed = 0;
        }
        if (_state == State.Waiting && elapsed >= WAIT_DURATION)
        {
            _state = State.Closing;
            _stateStartSec = nowSec;
            elapsed = 0;
        }
        if (_state == State.Closing && elapsed >= CLOSE_DURATION)
        {
            _state = State.Hidden;
            return;
        }

        int frame;
        switch (_state)
        {
            case State.Opening:
                // 0コマ目(棒)→最終コマ(全開+文字)まで一気に開く
                frame = (int)(elapsed / OPEN_DURATION * FRAME_COUNT);
                frame = Math.Clamp(frame, 0, FRAME_COUNT - 1);
                break;
            case State.Closing:
                // 最終コマ→0コマ目(棒)へ、下から上に閉じていく
                frame = (FRAME_COUNT - 1) - (int)(elapsed / CLOSE_DURATION * FRAME_COUNT);
                frame = Math.Clamp(frame, 0, FRAME_COUNT - 1);
                break;
            default:
                // Open / Waiting は最終コマで停止
                frame = FRAME_COUNT - 1;
                break;
        }

        float frameH = _baseTex.Height / (float)FRAME_COUNT;
        var src = new Rectangle(0, frame * frameH, _baseTex.Width, frameH);
        float w = _baseTex.Width * ScaleX;
        float h = frameH * ScaleY;
        var dest = new Rectangle(X * s + vx, Y * s + vy, w * s, h * s);
        var origin = new Vector2(w * s / 2f, 0f);

        Raylib.DrawTexturePro(_baseTex, src, dest, origin, 0f, Color.White);

        // 1打増えるごとに先頭フレームから再生しなおす
        if (hitCount > _lastHitCount)
        {
            _numAnimStart = nowSec;
        }
        _lastHitCount = hitCount;
        float numberPop = ComputeNumberPop(nowSec, _numAnimStart);

        // 開いている間（Open/Waiting）だけ連打数を表示。閉じ始めたら数字は消す。
        if ((_state == State.Open || _state == State.Waiting) && hitCount > 0)
            DrawNumber(hitCount, NumberX, NumberY, s, vx, vy, numberPop);
    }

    /// <summary>2P用の独立状態で連打ポップを描画する。</summary>
    public static void DrawP2(int hitCount, float vx, float vy, float s, double nowSec)
    {
        if (!_loaded || _p2State == State.Hidden || _baseTex.Id == 0) return;

        double elapsed = Math.Max(0.0, nowSec - _p2StateStartSec);
        if (_p2State == State.Opening && elapsed >= OPEN_DURATION)
        {
            _p2State = State.Open;
            _p2StateStartSec = nowSec;
            elapsed = 0;
        }
        if (_p2State == State.Waiting && elapsed >= WAIT_DURATION)
        {
            _p2State = State.Closing;
            _p2StateStartSec = nowSec;
            elapsed = 0;
        }
        if (_p2State == State.Closing && elapsed >= CLOSE_DURATION)
        {
            _p2State = State.Hidden;
            return;
        }

        int frame = _p2State switch
        {
            State.Opening => Math.Clamp((int)(elapsed / OPEN_DURATION * FRAME_COUNT), 0, FRAME_COUNT - 1),
            State.Closing => Math.Clamp((FRAME_COUNT - 1) - (int)(elapsed / CLOSE_DURATION * FRAME_COUNT), 0, FRAME_COUNT - 1),
            _ => FRAME_COUNT - 1,
        };

        float frameH = _baseTex.Height / (float)FRAME_COUNT;
        var src = new Rectangle(0, frame * frameH, _baseTex.Width, frameH);
        float w = _baseTex.Width * P2ScaleX;
        float h = frameH * P2ScaleY;
        var dest = new Rectangle(P2X * s + vx, P2Y * s + vy, w * s, h * s);
        Raylib.DrawTexturePro(_baseTex, src, dest, new Vector2(w * s / 2f, 0f), 0f, Color.White);

        if (hitCount > _p2LastHitCount) _p2NumAnimStart = nowSec;
        _p2LastHitCount = hitCount;
        float numberPop = ComputeNumberPop(nowSec, _p2NumAnimStart);
        if ((_p2State == State.Open || _p2State == State.Waiting) && hitCount > 0)
            DrawNumberP2(hitCount, s, vx, vy, numberPop);
    }

    private static void DrawNumberP2(int value, float s, float vx, float vy, float scale)
    {
        if (!_loaded || _numTex.Id == 0) return;
        string digits = value.ToString();
        float digitW = _numTex.Width / (float)DIGIT_COUNT;
        float digitH = _numTex.Height;
        float advance = digitW * P2NumberScaleX + P2NumberSpacing * P2NumberScaleX;
        float x = P2NumberX - advance * digits.Length / 2f;
        float baseH = digitH * P2NumberScaleY;
        float dh = baseH * scale;
        float bottomY = P2NumberY * s + vy + (baseH * s) / 2f;
        foreach (char c in digits)
        {
            int d = c - '0';
            var src = new Rectangle(d * digitW, 0, digitW, digitH);
            var dest = new Rectangle(x * s + vx, bottomY, digitW * P2NumberScaleX * s, dh * s);
            Raylib.DrawTexturePro(_numTex, src, dest, new Vector2(0, dh * s), 0f, Color.White);
            x += advance;
        }
    }

    /// <summary>
    /// 連打数字アニメの現在の拡大率を返す (1.0 = 等倍)。
    /// フレームテーブル <see cref="NumberScaleFrames"/> を 60fps で再生し、
    /// 2フレーム間を線形補間する。テーブル末尾到達後は 1.0 で静止。
    /// </summary>
    private static float ComputeNumberPop(double nowSec, double animStart)
    {
        if (animStart < 0) return 1.0f;

        double el = nowSec - animStart;
        double framePos = el / NUMBER_FRAME_DT;

        int lastFrame = NumberScaleFrames.Length - 1;
        if (framePos >= lastFrame) return NumberScaleFrames[lastFrame];

        int f0 = (int)framePos;
        float t = (float)(framePos - f0);
        return NumberScaleFrames[f0] + (NumberScaleFrames[f0 + 1] - NumberScaleFrames[f0]) * t;
    }

    private static void DrawNumber(int value, float centerX, float centerY, float s, float vx, float vy, float scale)
    {
        if (!_loaded || _numTex.Id == 0) return;

        string digits = value.ToString();
        float digitW = _numTex.Width / (float)DIGIT_COUNT;
        float digitH = _numTex.Height;
        float advance = digitW * NumberScaleX + NumberSpacing * NumberScaleX;
        float totalW = advance * digits.Length;
        float x = centerX - totalW / 2f;
        float baseH = digitH * NumberScaleY;
        // 💡 フレームテーブルの拡大率を直接乗算。下端アンカー固定で上方向へ拡大。
        float dh = baseH * scale;
        float bottomY = centerY * s + vy + (baseH * s) / 2f;

        foreach (char c in digits)
        {
            int d = c - '0';
            var src = new Rectangle(d * digitW, 0, digitW, digitH);
            var dest = new Rectangle(x * s + vx, bottomY, digitW * NumberScaleX * s, dh * s);
            Raylib.DrawTexturePro(_numTex, src, dest, new Vector2(0, dh * s), 0f, Color.White);
            x += advance;
        }
    }
}
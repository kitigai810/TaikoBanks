using System;
using System.Numerics;
using Raylib_cs;

/// <summary>
/// 50コンボ、および以降100コンボ刻み(100,200,300...)で表示する「コンボ!」ポップアップ。
/// Combo_Base.png は縦に3コマ並んだ巻物のパラパラアニメ（閉じた巻物→開いていく→
/// 「コンボ!」の文字が出た全開状態）で、上から下へ順にコマ送りして再生する。
/// 開き切ったら一定時間ホールドした後、自動的に閉じて消える。
/// Combo_Number.png は横に0〜9が並んだ数字素材で、現在のコンボ数を表示する。
/// </summary>
public static class ComboPop
{
    // X: 巻物の左右中央、Y: 巻物の上端の座標
    public static float X = 618f;
    public static float Y = 25f;
    // 幅・高さを別々に拡大縮小できる
    public static float ScaleX = 1f;
    public static float ScaleY = 1f;

    // 数字（コンボ数）のX座標（中央基準）
    public static float NumberX = 525f;
    // 数字のY座標（上端基準、絶対座標。巻物のYには依存しない）
    public static float NumberY = 135f;
    // 数字の大きさ（巻物とは独立して幅・高さを調整可能）
    public static float NumberScaleX = 1f;
    public static float NumberScaleY = 1f;
    // 数字1文字ごとの送り幅の補正（マイナスにするほど詰まる。素材の余白分を差し引く）
    public static float NumberSpacing = -10f;

    // コンボ数がこの値を超えたら、桁が増えすぎないようX方向を縮小する
    public static int OverflowComboThreshold = 1000;
    // 縮小時のXスケール倍率（NumberScaleXに対する追加倍率）
    public static float OverflowNumberScaleX = 0.8f;

    // ---- 2P ComboPop（下段レーンへ259px下げた初期配置。個別調整可能） ----
    public static float P2X = 618f;
    public static float P2Y = 825f;
    public static float P2ScaleX = 1f;
    public static float P2ScaleY = 1f;
    public static float P2NumberX = 525f;
    public static float P2NumberY = 935f;
    public static float P2NumberScaleX = 1f;
    public static float P2NumberScaleY = 1f;
    public static float P2NumberSpacing = -10f;
    public static int P2OverflowComboThreshold = 1000;
    public static float P2OverflowNumberScaleX = 0.8f;

    private const int FRAME_COUNT = 3;

    // 開くアニメの全体時間。内訳は下記2つに分割される。
    // 1) 一番上のコマ(閉じた巻物)を静止表示する時間
    private const double OPEN_FRAME0_DURATION = 0.05;
    // 2) 上から2番目のコマを左→右にワイプで出していく時間
    private const double OPEN_WIPE_DURATION = 0.20;
    private const double OPEN_DURATION = OPEN_FRAME0_DURATION + OPEN_WIPE_DURATION;
    // 全開後、閉じ始めるまでのホールド時間
    private const double HOLD_DURATION = 1.2;
    // 閉じるアニメ（逆再生：2番目のコマを右→左に閉じてから、一番上のコマを一瞬表示）
    private const double CLOSE_WIPE_DURATION = 0.20;
    private const double CLOSE_FRAME0_DURATION = 0.05;
    private const double CLOSE_DURATION = CLOSE_WIPE_DURATION + CLOSE_FRAME0_DURATION;

    private const int DIGIT_COUNT = 10;

    private static Texture2D _baseTex;
    private static Texture2D _numTex;
    private static bool _loaded;

    private enum State { Hidden, Opening, Hold, Closing }
    private static State _state = State.Hidden;
    private static double _stateStartSec;
    private static int _shownCombo;
    private static State _p2State = State.Hidden;
    private static double _p2StateStartSec;
    private static int _p2ShownCombo;

    public static void Init()
    {
        if (!_loaded)
        {
            _baseTex = Raylib.LoadTexture("Lumen/1.Enso/ComboPop/Combo_Base.png");
            _numTex = Raylib.LoadTexture("Lumen/1.Enso/ComboPop/Combo_Number.png");
            VramProbe.Track("ComboPop.Init (all)");
            _loaded = true;
        }
        _state = State.Hidden;
        _p2State = State.Hidden;
        _p2ShownCombo = 0;
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

    /// <summary>
    /// コンボ数を渡す。50、または100刻み(100,200,300...)に達した瞬間だけ
    /// ポップアップを頭から再生する。それ以外の値は無視される。
    /// </summary>
    public static void CheckMilestone(int combo, double nowSec)
    {
        if (combo == 50 || (combo > 0 && combo % 100 == 0))
            Show(combo, nowSec);
    }

    /// <summary>指定コンボ数で強制的に開くアニメーションを頭から再生する。</summary>
    public static void Show(int combo, double nowSec)
    {
        _shownCombo = combo;
        _state = State.Opening;
        _stateStartSec = nowSec;
    }

    /// <summary>即座に非表示にする（ホールド・閉じるアニメーションを挟まない）。</summary>
    public static void ForceHide()
    {
        _state = State.Hidden;
    }

    /// <summary>2Pコンボの50、または100刻み到達時に独立ポップを開始する。</summary>
    public static void CheckMilestoneP2(int combo, double nowSec)
    {
        if (combo == 50 || (combo > 0 && combo % 100 == 0)) ShowP2(combo, nowSec);
    }

    public static void ShowP2(int combo, double nowSec)
    {
        _p2ShownCombo = combo;
        _p2State = State.Opening;
        _p2StateStartSec = nowSec;
    }

    public static void ForceHideP2() => _p2State = State.Hidden;

    public static void Draw(float vx, float vy, float s, double nowSec)
    {
        if (!_loaded || _state == State.Hidden || _baseTex.Id == 0) return;

        double elapsed = nowSec - _stateStartSec;
        if (elapsed < 0) elapsed = 0;

        // 状態遷移
        if (_state == State.Opening && elapsed >= OPEN_DURATION)
        {
            _state = State.Hold;
            _stateStartSec = nowSec;
            elapsed = 0;
        }
        if (_state == State.Hold && elapsed >= HOLD_DURATION)
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

        float frameH = _baseTex.Height / (float)FRAME_COUNT;
        float w = _baseTex.Width * ScaleX;
        float h = frameH * ScaleY;
        float destW = w * s;
        float destH = h * s;
        float destX = X * s + vx - destW / 2f; // 中央寄せの左端座標
        float destY = Y * s + vy;

        // 左→右のワイプ割合(0〜1)。1コマ目(index=1)を左からrevealFrac分だけ表示する。
        float revealFrac = 0f;
        int staticFrame = -1; // -1ならワイプ描画、それ以外は指定コマを全体表示

        switch (_state)
        {
            case State.Opening:
                if (elapsed < OPEN_FRAME0_DURATION)
                {
                    // 1) 一番上のコマ(閉じた巻物)を静止表示
                    staticFrame = 0;
                }
                else
                {
                    // 2) 上から2番目のコマを左→右にワイプ
                    double wipeElapsed = elapsed - OPEN_FRAME0_DURATION;
                    revealFrac = (float)Math.Clamp(wipeElapsed / OPEN_WIPE_DURATION, 0.0, 1.0);
                }
                break;
            case State.Closing:
                if (elapsed < CLOSE_WIPE_DURATION)
                {
                    // 1) 2番目のコマを右→左に閉じていく(revealFracが1→0)
                    revealFrac = (float)Math.Clamp(1.0 - elapsed / CLOSE_WIPE_DURATION, 0.0, 1.0);
                }
                else
                {
                    // 2) 一番上のコマ(閉じた巻物)を一瞬表示してから消える
                    staticFrame = 0;
                }
                break;
            default:
                // Hold は一番下のコマ(全開+文字)で停止
                staticFrame = FRAME_COUNT - 1;
                break;
        }

        if (staticFrame >= 0)
        {
            var src = new Rectangle(0, staticFrame * frameH, _baseTex.Width, frameH);
            var dest = new Rectangle(destX, destY, destW, destH);
            Raylib.DrawTexturePro(_baseTex, src, dest, Vector2.Zero, 0f, Color.White);
        }
        else if (revealFrac > 0f)
        {
            // 上から2番目のコマを左端からrevealFrac分だけ切り出して表示
            float revealW = _baseTex.Width * revealFrac;
            var src = new Rectangle(0, 1 * frameH, revealW, frameH);
            var dest = new Rectangle(destX, destY, destW * revealFrac, destH);
            Raylib.DrawTexturePro(_baseTex, src, dest, Vector2.Zero, 0f, Color.White);
        }

        // 全開中(Hold)だけコンボ数を表示。開閉アニメ中は数字は出さない。
        if (_state == State.Hold && _shownCombo > 0)
            DrawNumber(_shownCombo, NumberX, NumberY, s, vx, vy);
    }

    /// <summary>2P用の独立状態でコンボポップを描画する。</summary>
    public static void DrawP2(float vx, float vy, float s, double nowSec)
    {
        if (!_loaded || _p2State == State.Hidden || _baseTex.Id == 0) return;

        double elapsed = Math.Max(0.0, nowSec - _p2StateStartSec);
        if (_p2State == State.Opening && elapsed >= OPEN_DURATION)
        {
            _p2State = State.Hold;
            _p2StateStartSec = nowSec;
            elapsed = 0;
        }
        if (_p2State == State.Hold && elapsed >= HOLD_DURATION)
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

        float frameH = _baseTex.Height / (float)FRAME_COUNT;
        float w = _baseTex.Width * P2ScaleX;
        float h = frameH * P2ScaleY;
        float destW = w * s;
        float destH = h * s;
        float destX = P2X * s + vx - destW / 2f;
        float destY = P2Y * s + vy;
        float revealFrac = 0f;
        int staticFrame = -1;

        switch (_p2State)
        {
            case State.Opening:
                if (elapsed < OPEN_FRAME0_DURATION) staticFrame = 0;
                else revealFrac = (float)Math.Clamp((elapsed - OPEN_FRAME0_DURATION) / OPEN_WIPE_DURATION, 0.0, 1.0);
                break;
            case State.Closing:
                if (elapsed < CLOSE_WIPE_DURATION) revealFrac = (float)Math.Clamp(1.0 - elapsed / CLOSE_WIPE_DURATION, 0.0, 1.0);
                else staticFrame = 0;
                break;
            default:
                staticFrame = FRAME_COUNT - 1;
                break;
        }

        if (staticFrame >= 0)
        {
            var src = new Rectangle(0, staticFrame * frameH, _baseTex.Width, frameH);
            Raylib.DrawTexturePro(_baseTex, src, new Rectangle(destX, destY, destW, destH), Vector2.Zero, 0f, Color.White);
        }
        else if (revealFrac > 0f)
        {
            float revealW = _baseTex.Width * revealFrac;
            var src = new Rectangle(0, frameH, revealW, frameH);
            Raylib.DrawTexturePro(_baseTex, src, new Rectangle(destX, destY, destW * revealFrac, destH), Vector2.Zero, 0f, Color.White);
        }

        if (_p2State == State.Hold && _p2ShownCombo > 0)
            DrawNumberP2(_p2ShownCombo, s, vx, vy);
    }

    private static void DrawNumberP2(int value, float s, float vx, float vy)
    {
        if (!_loaded || _numTex.Id == 0) return;
        string digits = value.ToString();
        float effScaleX = P2NumberScaleX * (value >= P2OverflowComboThreshold ? P2OverflowNumberScaleX : 1f);
        float digitW = _numTex.Width / (float)DIGIT_COUNT;
        float digitH = _numTex.Height;
        float glyphW = digitW * effScaleX;
        float advance = glyphW + P2NumberSpacing * effScaleX;
        float totalW = advance * (digits.Length - 1) + glyphW;
        float x = P2NumberX - totalW / 2f;
        float dh = digitH * P2NumberScaleY;
        foreach (char c in digits)
        {
            int d = c - '0';
            var src = new Rectangle(d * digitW, 0, digitW, digitH);
            var dest = new Rectangle(x * s + vx, P2NumberY * s + vy, digitW * effScaleX * s, dh * s);
            Raylib.DrawTexturePro(_numTex, src, dest, new Vector2(0, dh * s / 2f), 0f, Color.White);
            x += advance;
        }
    }

    private static void DrawNumber(int value, float centerX, float centerY, float s, float vx, float vy)
    {
        if (!_loaded || _numTex.Id == 0) return;

        string digits = value.ToString();
        // 桁が増えすぎる(コンボ数が閾値超え)場合はX方向のみ縮小する
        float effScaleX = NumberScaleX * (value >= OverflowComboThreshold ? OverflowNumberScaleX : 1f);

        float digitW = _numTex.Width / (float)DIGIT_COUNT;
        float digitH = _numTex.Height;
        float glyphW = digitW * effScaleX;
        float advance = glyphW + NumberSpacing * effScaleX;
        // 文字間の詰め(NumberSpacing)は文字と文字の間にしか発生しないため、
        // 全体幅は「最後の1文字の実幅 + それ以外の送り幅」で計算する。
        float totalW = advance * (digits.Length - 1) + glyphW;
        float x = centerX - totalW / 2f;
        float dh = digitH * NumberScaleY;

        foreach (char c in digits)
        {
            int d = c - '0';
            var src = new Rectangle(d * digitW, 0, digitW, digitH);
            var dest = new Rectangle(x * s + vx, centerY * s + vy, digitW * effScaleX * s, dh * s);
            Raylib.DrawTexturePro(_numTex, src, dest, new Vector2(0, dh * s / 2f), 0f, Color.White);
            x += advance;
        }
    }
}
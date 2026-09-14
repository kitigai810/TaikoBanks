using System;
using System.Numerics;
using Raylib_cs;

/// <summary>
/// 段位道場で1曲目→2曲目(以降)に切り替わる際の「衝立(ついたて)」演出。
/// OpenTaiko (https://github.com/0auBSQ/OpenTaiko) の Dan_Cert.cs を移植・簡略化したもの。
///
/// Lumen/5.Dan/DanEnso/Screen.png は横に2枚並んだ衝立(太鼓ちゃん顔)で、
/// 画像の左半分・右半分をそれぞれ独立した板として扱う。
///
/// 流れ:
///   1) Closing … 画面外(レーン脇)にある左右の衝立が中央に寄って合わさり、レーンを覆い隠す
///   2) Wait    … 衝立が閉じたまま静止。この間に裏側で次の曲の情報に差し替わる
///   3) Opening … 衝立が中央から左右に割れて開き、レーンが見える。
///                同時に次の曲のタイトルがフェードイン→フェードアウトしながら表示される
///
/// #NEXTSONG 相当のタイミング(曲の終端)で Show() を呼ぶと、上記が自動で再生される。
/// 3段階の合計時間は本家と同じ約6.2秒 (TotalDelaySec) で、TJA側の曲間の空白と合わせてある。
/// </summary>
public static class DanCurtain
{
    private const string TEX_PATH = "Lumen/5.Dan/DanEnso/Screen.png";

    // ---- 位置・大きさ調整（1920x1080基準）。自由に変更可 ----
    // 衝立が完全に開いたときに左右それぞれが引っ込む基準位置。
    // LaneX: レーン左端相当(この位置より内側には衝立が来ない＝開いた状態)
    public static float LaneX = 500f;
    // ScreenRightX: 画面右端相当
    public static float ScreenRightX = 1920f;
    // 衝立の上端Y座標
    public static float Y = 287.5f;
    public static float Scale = 1f;

    // タイトル文字の表示位置(画面中央付近を想定。自由に変更可)
    public static float TitleX = 960f;
    public static float TitleY = 460f;
    public static float TitleFontSize = 48f;
    public static float SubTitleFontSize = 28f;
    public static float SubTitleYOffset = 60f;

    // ---- タイミング（本家 Dan_Cert.cs の Counter_In/Wait/Out/Text を秒に換算したもの） ----
    private const double CLOSE_DURATION = 0.999; // 衝立が閉じる
    private const double WAIT_DURATION = 2.299;  // 閉じたまま静止(この間に曲・タイトルを差し替え)
    private const double OPEN_DURATION = 0.270;  // 衝立が開く物理アニメ
    private const double TEXT_DURATION = 2.899;  // タイトル表示全体の尺(Openingの間ずっと)
    private const double TEXT_FADE_START = 2.000;   // フェードアウト開始タイミング(Opening経過秒)
    private const double TEXT_FADE_DURATION = 0.510; // フェードアウトにかける秒数

    /// <summary>#NEXTSONG〜次の曲開始までの合計尺の目安。TJA側の空白調整に使う。</summary>
    public const double TotalDelaySec = CLOSE_DURATION + WAIT_DURATION + TEXT_DURATION; // ≒6.2秒

    /// <summary>Show()呼び出しから、衝立が物理的に開き切る(観音開きが完了する)までの秒数。</summary>
    public const double DoorsOpenAtSec = CLOSE_DURATION + WAIT_DURATION + OPEN_DURATION;

    private enum State { Hidden, Closing, Wait, Opening }
    private static State _state = State.Hidden;
    private static double _stateStartSec;

    // 表示中のタイトル(Wait終了時に_pendingから差し替わる)
    private static string _title;
    private static string _subTitle;
    private static string _pendingTitle;
    private static string _pendingSubTitle;

    // 衝立が閉じきった瞬間(Closing→Wait)に1回だけ呼ばれるコールバック。
    // 画面が完全に隠れている間に重い処理(次の曲のロード・Enso.Init()等)を
    // ここで実行すると、切り替えの一瞬が見えずに済む。
    private static Action _onCovered;

    private static Texture2D _tex;
    private static bool _loaded;

    public static void Init()
    {
        if (!_loaded)
        {
            _tex = Raylib.LoadTexture(TEX_PATH);
            VramProbe.Track("DanCurtain.Init");
            _loaded = true;
        }
        _state = State.Hidden;
        _title = _subTitle = _pendingTitle = _pendingSubTitle = null;
    }

    public static void Unload()
    {
        if (_loaded)
        {
            if (_tex.Id != 0) Raylib.UnloadTexture(_tex);
            _loaded = false;
        }
        _state = State.Hidden;
        _onCovered = null;
    }

    /// <summary>
    /// 曲の終端(#NEXTSONG相当)で呼ぶ。衝立を閉じる→待つ→開く、を頭から再生する。
    /// nextTitle/nextSubTitleは次に始まる曲のタイトル(衝立が閉じきったタイミングで裏側で差し替わり、
    /// 開くと同時にフェードインして見える)。
    /// </summary>
    public static void Show(double nowSec, string nextTitle, string nextSubTitle = null, Action onCovered = null)
    {
        _state = State.Closing;
        _stateStartSec = nowSec;
        _pendingTitle = nextTitle;
        _pendingSubTitle = nextSubTitle;
        _onCovered = onCovered;
    }

    /// <summary>即座に非表示にする（曲を中断してリトライする場合など）。</summary>
    public static void ForceHide()
    {
        _state = State.Hidden;
        _onCovered = null;
    }

    public static bool IsPlaying => _state != State.Hidden;

    /// <summary>
    /// 毎フレーム呼ぶ。状態遷移(Closing→Wait→Opening→Hidden)とonCoveredコールバックの発火はここで行う。
    /// 描画(Draw)から独立させているのは、DrawはEnso.Draw()の内部から呼ばれるため、
    /// そこで状態を進めてEnso.Init()等を呼ぶと描画中のEnso静的状態が壊れてしまうため。
    /// Program.cs側の更新フェーズ(Enso.Update()と同じタイミング)で呼ぶこと。
    /// </summary>
    public static void Update(double nowSec)
    {
        if (_state == State.Hidden) return;

        double elapsed = Math.Max(0.0, nowSec - _stateStartSec);

        if (_state == State.Closing && elapsed >= CLOSE_DURATION)
        {
            _state = State.Wait;
            _stateStartSec = nowSec;

            // 衝立が完全に閉じてレーンが見えなくなった瞬間。
            // ここで次の曲のロード等を実行すれば、切り替えの瞬間は画面に映らない。
            _onCovered?.Invoke();
            _onCovered = null;
            elapsed = 0;
        }
        if (_state == State.Wait && elapsed >= WAIT_DURATION)
        {
            // 衝立が閉じきって静止している間に次の曲のタイトルへ差し替える
            _title = _pendingTitle;
            _subTitle = _pendingSubTitle;
            _state = State.Opening;
            _stateStartSec = nowSec;
            elapsed = 0;
        }
        if (_state == State.Opening && elapsed >= TEXT_DURATION)
        {
            _state = State.Hidden;
        }
    }

    /// <summary>
    /// 現在の状態を描画するだけ。状態遷移やコールバック発火は一切行わない(Update()側の責務)。
    /// Enso.Draw()の内部から呼ばれる想定。
    /// </summary>
    public static void Draw(float vx, float vy, float s, double nowSec)
    {
        if (!_loaded || _state == State.Hidden || _tex.Id == 0) return;

        double elapsed = Math.Max(0.0, nowSec - _stateStartSec);

        float halfW = _tex.Width / 2f * Scale;
        float texH = _tex.Height * Scale;
        float centerX = (LaneX + ScreenRightX) / 2f;

        // leftEdge: 左の衝立の右端(合わせ目)のX座標
        // rightEdge: 右の衝立の左端(合わせ目)のX座標
        float leftEdge, rightEdge;

        switch (_state)
        {
            case State.Closing:
                {
                    // 本家のexponential-ease-out(smoothFactor)を簡略移植。後半ほど減速して中央にピタッと収まる。
                    double t = Math.Clamp(elapsed / CLOSE_DURATION, 0.0, 1.0);
                    float ratio = (float)(1.0 - Math.Pow(0.015, t));
                    leftEdge = ratio * centerX + (1f - ratio) * LaneX;
                    rightEdge = ratio * centerX + (1f - ratio) * ScreenRightX;
                    break;
                }
            case State.Wait:
                leftEdge = centerX;
                rightEdge = centerX;
                break;
            case State.Opening:
                {
                    // sinイージング(ease-out)で勢いよく開いてから収束する
                    double t = Math.Clamp(elapsed / OPEN_DURATION, 0.0, 1.0);
                    float openAmount = (float)Math.Sin(t * Math.PI / 2.0) * (ScreenRightX - LaneX) / 2f;
                    leftEdge = centerX - openAmount;
                    rightEdge = centerX + openAmount;
                    break;
                }
            default:
                leftEdge = LaneX;
                rightEdge = ScreenRightX;
                break;
        }

        // 左半分(合わせ目が右端に来るように描画)
        var srcL = new Rectangle(0, 0, _tex.Width / 2f, _tex.Height);
        var destL = new Rectangle((leftEdge * s + vx) - halfW * s, Y * s + vy, halfW * s, texH * s);
        Raylib.DrawTexturePro(_tex, srcL, destL, Vector2.Zero, 0f, Color.White);

        // 右半分(合わせ目が左端に来るように描画)
        var srcR = new Rectangle(_tex.Width / 2f, 0, _tex.Width / 2f, _tex.Height);
        var destR = new Rectangle(rightEdge * s + vx, Y * s + vy, halfW * s, texH * s);
        Raylib.DrawTexturePro(_tex, srcR, destR, Vector2.Zero, 0f, Color.White);

        // 開いている間(Opening)だけタイトルをフェードイン→フェードアウトさせながら表示
        if (_state == State.Opening && !string.IsNullOrEmpty(_title))
        {
            byte alpha;
            if (elapsed < TEXT_FADE_START)
                alpha = 255;
            else
                alpha = (byte)Math.Clamp(255.0 - 255.0 * (elapsed - TEXT_FADE_START) / TEXT_FADE_DURATION, 0, 255);

            if (alpha > 0)
                DrawTitle(vx, vy, s, alpha);
        }
    }

    private static void DrawTitle(float vx, float vy, float s, byte alpha)
    {
        var color = new Color((byte)255, (byte)255, (byte)255, alpha);
        var outlineColor = new Color((byte)0, (byte)0, (byte)0, alpha);

        var pos = new Vector2(TitleX * s + vx, TitleY * s + vy);
        G.DrawTextWithOutline16(G.Font, _title, pos, TitleFontSize * s, color, outlineColor, 7f * s, 0f);

        if (!string.IsNullOrEmpty(_subTitle))
        {
            var subPos = new Vector2(TitleX * s + vx, (TitleY + SubTitleYOffset) * s + vy);
            G.DrawTextWithOutline16(G.Font, _subTitle, subPos, SubTitleFontSize * s, color, outlineColor, 5f * s, 0f);
        }
    }
}
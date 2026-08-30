using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Numerics;
using TaikoNauts.Core.Taiko.Charts;

/// <summary>
/// ミニ太鼓マスコットキャラクターの常時表示、インタラクティブ太鼓（drum.png）のキー入力演出、
/// および太鼓左上のスコアカバー・画像スコアフォント描画を担当します。
/// </summary>
public static class MiniTaiko
{
    // ---- レイアウト調整用パラメータ (1920×1080基準、コードからリアルタイムに変更可能) ----

    // 💡 ミニ太鼓（マスコットキャラ）と打鍵用太鼓（drum.png）の位置パラメータを完全に分離しました

    public static float HaikeiX = 0f;
    public static float HaikeiY = 276f;

    public static float JX = 0f;
    public static float JY = 288f;

    /// <summary>ミニ太鼓マスコットの基準中心X座標</summary>
    public static float MiniTaikoX = 145f;

    /// <summary>ミニ太鼓マスコットのLANE_Y基準のYオフセット値（デフォルトで少し上に配置）</summary>
    public static float MiniTaikoYOffset = 0f;

    public static float TaikoX = 320f;
    public static float TaikoY = 316f;

    /// <summary>スコアカバーの右端からスコア数字列の右端までの余白調整X値</summary>
    public static float ScoreNumberOffsetX = -25f;

    /// <summary>スコアカバーの垂直中心からスコア数字列の描画位置までの微調整Y値</summary>
    public static float ScoreNumberOffsetY = -2f;

    // ---- コンボ表示レイアウト調整用パラメータ ----

    /// <summary>コンボ表示を開始する最小コンボ数</summary>
    public static int ComboDisplayMin = 10;

    /// <summary>コンボ表示の打鍵太鼓(drum.png)中心からのYオフセット値（上方向へずらすためデフォルトは負の値）</summary>
    public static float ComboYOffset = -50f;

    /// <summary>コンボ文字の数字描画位置からの追加Yオフセット値</summary>
    public static float ComboTextYOffset = 5f;

    /// <summary>コンボ文字のフォントサイズ (pt)</summary>
    public static float ComboTextFontSize = 27f;

    /// <summary>コンボ数字同士の間隔をぎゅっと詰めるための送り幅ピッチ割合（1文字の等幅サイズに対するパーセンテージ）</summary>
    public static float ComboCharPitchRatio = 0.64f;

    public static float ScoreRightX = 234f;
    public static float ScoreBaseY = 292f;
    public static float ScoreDigitGap = -8f;

    // 2Pは下段レーン用背景(Haikei_H2)に合わせ、反転したスコアカバーと
    // 数字列を1Pから独立して下方へ配置する。実機見た目に合わせて調整可能。
    public static float P2ScoreCoverOffsetY = 190f;
    public static float P2ScoreNumberOffsetY = 190f;
    /// <summary>2Pの加算スコアポップアップ専用の下方向オフセット</summary>
    public static float P2ScorePopupOffsetY = 300f;

    // スコア加算時の数字伸縮: 30msで高さ52pxへ、20msで元に戻る
    public static float ScorePopHeight = 52f;
    public static float ScorePopGrowMs = 30f;
    public static float ScorePopShrinkMs = 20f;

    public static float ComboDigitY = 330f;
    public static float ComboDigitGap = -20f;

    // コンボ数がこの値を超えたら、桁が増えすぎないようX方向を縮小する
    public static int ComboOverflowThreshold = 1000;
    // 縮小時のXスケール倍率
    public static float ComboOverflowScaleX = 0.8f;

    public static float ComboPopHeight = 104f;
    // 💡 フレームテーブル駆動アニメ (60fps固定、トリガーのたびに先頭へリセット)
    //    Combo_Scale = 1, 1.1, 1.2, 1.17, 1.144, 1.115, 1.086, 1.057, 1.029, 1
    //    拡大率はテクスチャの baseH に対する乗数。テーブル末尾の 1.0 で静止。
    private static readonly float[] ComboScaleFrames =
        { 1.000f, 1.100f, 1.200f, 1.170f, 1.144f, 1.115f, 1.086f, 1.057f, 1.029f, 1.000f };
    private const double COMBO_FRAME_DT = 1.0 / 60.0; // 1フレーム = 1/60 秒
    public static float ComboPopAnchorOffset = 8f;

    // ---- 段位道場モード専用: 合格条件表示 (Lumen/5.Dan/DanEnso/Base.png=Condition_Bar, Context.png=Condition_Content) ----
    //     DanSelect.csの表示方式(バー+バッジ+ラベル+「〇〇未満/以上」の数値)をそのまま流用する。

    /// <summary>Base.png（条件1種類につき1本の土台バー）の基準位置・拡大率・行間</summary>
    public static float DanConditionBarX = 150f;
    public static float DanConditionBarY = 550f;
    public static float DanConditionBarScale = 1f;
    public static float DanConditionBarLineHeight = 155f;

    /// <summary>Context.png（全体条件/1曲目/2曲目/3曲目のバッジ。縦4分割）の位置調整</summary>
    public static float DanConditionContentOffsetX = 10f;
    public static float DanConditionContentOffsetY = 50f;
    public static float DanConditionContentScale = 1f;

    /// <summary>個別条件で1st/2nd/3rdバッジを左から右へ並べるときの1個あたりの占有幅</summary>
    public static float DanConditionContentItemWidth = 220f;

    /// <summary>「良の数」等ラベルの位置(中央ぞろえ)・行間・フォントサイズ</summary>
    public static float DanConditionLabelX = 335f;
    public static float DanConditionLabelY = 557.5f;
    public static float DanConditionLabelLineHeight = 155f;
    public static float DanConditionLabelFontSize = 25f;

    /// <summary>数値テキスト(「18未満」等)の位置 = バッジのdest.X/Y + このオフセット</summary>
    public static float DanConditionValueOffsetX = 350f;
    public static float DanConditionValueOffsetY = 17.5f;
    public static float DanConditionValueFontSize = 30f;

    private static readonly Color DanConditionLabelOutlineColor = new Color((byte)0x4B, (byte)0x3C, (byte)0x33, (byte)255);
    public static float DanConditionLabelOutlineThickness = 5.5f;

    private static Texture2D _texHaikeiH;
    private static Texture2D _texHaikeiH2; // 2P下段専用背景 (Haikei_H2.png)
    private static Texture2D _texTest;
    private static Texture2D _texTaiko;      // 常時表示されるベースの太鼓 (taiko.png)
    private static Texture2D _texDrumDonL;
    private static Texture2D _texDrumDonR;
    private static Texture2D _texDrumKatL;
    private static Texture2D _texDrumKatR;
    private static Texture2D _texJ;
    private static Texture2D[] _texScoreDigits = new Texture2D[10];

    // コンボ表示用画像アセット (n/s の 3段階数字)
    private static Texture2D[] _texComboN = new Texture2D[10];
    private static Texture2D[] _texCombo50 = new Texture2D[10];
    private static Texture2D[] _texCombo100 = new Texture2D[10];
    private static Texture2D _texComboText; // 💡 追加：「コンボ」の文字画像

    // 段位道場モード専用アセット (Lumen/5.Dan/DanEnso/)
    private static Texture2D _texDanConditionBar;     // Base.png = Condition_Bar.png相当（土台バー）
    private static Texture2D _texDanConditionContent; // Context.png = Condition_Content.png相当（バッジ、縦4分割）
    private const int DAN_CONDITION_CONTENT_ROW_COUNT = 4; // Overall / 1st / 2nd / 3rd
    private static int _danConditionContentSrcW;
    private static int _danConditionContentSrcH;

    // 💡 DanSelect.csのConditionContentRowと同じ並び(Overall=0,Song1st=1,Song2nd=2,Song3rd=3)
    private enum DanConditionContentRow { Overall = 0, Song1st = 1, Song2nd = 2, Song3rd = 3 }

    private sealed class DanConditionEntry
    {
        public DanConditionContentRow Row;
        public string Value;
    }

    private static readonly List<(string Label, List<DanConditionEntry> Entries)> _danConditionTexts = new();
    private static int _danConditionCount;

    private static readonly Dictionary<Exam.ConditionType, string> _danConditionTypeLabels = new()
    {
        { Exam.ConditionType.Great, "良の数" },
        { Exam.ConditionType.Good, "可の数" },
        { Exam.ConditionType.Miss, "不可の数" },
        { Exam.ConditionType.Roll, "連打数" },
        { Exam.ConditionType.Hit, "たたけた数" },
        { Exam.ConditionType.Score, "スコア" },
        { Exam.ConditionType.MaxCombo, "コンボ数" },
    };

    private static readonly HashSet<Exam.ConditionType> _danConditionLessThanTypes = new()
    {
        Exam.ConditionType.Good, Exam.ConditionType.Miss,
    };

    private static bool _loaded;

    // 打鍵フラッシュ: 表示60ms待機後、60msでフェードアウト
    private const double DRUM_HOLD = 0.06;
    private const double DRUM_FADE = 0.06;
    private const double DRUM_DURATION = DRUM_HOLD + DRUM_FADE;

    private static double _drumDonLTime = -1.0;
    private static double _drumDonRTime = -1.0;
    private static double _drumKatLTime = -1.0;
    private static double _drumKatRTime = -1.0;

    // 2Pは1Pと同じテクスチャを共有するが、フラッシュ・スコアポップ・コンボ演出の
    // 時刻と前回値は完全に独立させる。これにより同一フレーム内で両方を描画しても
    // 1P/2Pのアニメーションが互いに上書きされない。
    private sealed class P2VisualState
    {
        public double DrumDonLTime = -1.0;
        public double DrumDonRTime = -1.0;
        public double DrumKatLTime = -1.0;
        public double DrumKatRTime = -1.0;
        public bool AutoAltDon;
        public bool AutoAltKat;
        public int LastCombo;
        public double ComboAnimStart = -1.0;
        public int LastScore;
        public double ScoreAnimStart = -1.0;
        public float ScoreAnimFrom;
        public readonly List<ScorePopup> ScorePopups = new();

        public void Reset()
        {
            DrumDonLTime = DrumDonRTime = DrumKatLTime = DrumKatRTime = -1.0;
            AutoAltDon = AutoAltKat = false;
            LastCombo = 0;
            ComboAnimStart = -1.0;
            LastScore = 0;
            ScoreAnimStart = -1.0;
            ScoreAnimFrom = 0f;
            ScorePopups.Clear();
        }
    }

    private static readonly P2VisualState _p2 = new();

    private static bool _autoAltDon;
    private static bool _autoAltKat;
    private static bool _mouseAltDon;
    private static bool _mouseAltKat;
    // ---- レイアウト調整用パラメータ ----
    /// <summary>難易度アイコンの X座標オフセット（taiko.png からの相対位置）</summary>
    public static float DiffXOffset = -292.5f;
    /// <summary>難易度アイコンの Y座標オフセット（taiko.png からの相対位置）</summary>
    public static float DiffYOffset = 30f;

    /// <summary>難易度アイコンの基準幅</summary>
    public static float DiffWidth = 133f;
    /// <summary>難易度アイコンの基準高さ</summary>
    public static float DiffHeight = 100f;

    /// <summary>難易度アイコンの大きさ倍率 (1.0fで等倍)</summary>
    public static float DiffScale = 1f;

    // 2Pは下段ミニ太鼓用に難易度アイコンを独立調整する。1PのDiff*は変更しない。
    /// <summary>2P難易度アイコンのX座標オフセット（2P taiko.pngからの相対位置）</summary>
    public static float P2DiffXOffset = -292.5f;
    /// <summary>2P難易度アイコンのY座標オフセット（2P taiko.pngからの相対位置）</summary>
    public static float P2DiffYOffset = 55f;
    /// <summary>2P難易度アイコンの基準幅</summary>
    public static float P2DiffWidth = 133f;
    /// <summary>2P難易度アイコンの基準高さ</summary>
    public static float P2DiffHeight = 100f;
    /// <summary>2P難易度アイコンの大きさ倍率 (1.0fで等倍)</summary>
    public static float P2DiffScale = 1f;

    /// <summary>AUTO表示（Auto.png）の位置パラメータ</summary>
    public static float AutoX = 155f;
    public static float AutoY = 350f;

    // ==================================================================
    // 💡 演奏オプションアイコン(はやさ/ドロン/あべこべ/ランダム)の表示。
    //    Autoの位置(AutoX, AutoY)を1枠目として、1行3個ずつ並べる。
    //    4個目からはAutoと同じX(AutoX)、Yだけ1行分下げた位置から再開する。
    // ==================================================================
    /// <summary>アイコン1個分の列間隔(px, AutoXからの相対)</summary>
    public static float OptionIconColGapX = 46f;
    /// <summary>アイコン1行分の行間隔(px, AutoYからの相対)</summary>
    public static float OptionIconRowGapY = 46f;
    /// <summary>アイコンの拡大率(1.0で原寸)</summary>
    public static float OptionIconScale = 1f;
    /// <summary>1行に並べるアイコンの数</summary>
    public static int OptionIconPerRow = 3;

    // ---- アセット配列 ----
    // 0:Easy, 1:Normal, 2:Hard, 3:Oni, 4:Edit
    private static Texture2D[] _texDiffIcons = new Texture2D[5];

    // AUTO表示用アセット
    private static Texture2D _texAuto;

    // 演奏オプションアイコン用アセット(Lumen/Option_Icon/)
    // Speedは1.1〜4.0の各値ごとにコマ分割された横並び画像、Doron/Abekobeは[しない/する]の2コマ、
    // Randomは[なし/きまぐれ/でたらめ]の3コマ横並び。
    private static Texture2D _texOptSpeed;
    private static Texture2D _texOptDoron;
    private static Texture2D _texOptAbekobe;
    private static Texture2D _texOptRandom;
    private const int SPEED_ICON_FRAME_COUNT = 15; // 0:なし(1.0, 使わない) + 1.1〜4.0の14コマ
    // Speed.pngの各コマに対応する速度値(1.0=デフォルトは表示自体しないので含まない)
    private static readonly float[] SpeedIconValues =
        { 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.7f, 1.8f, 1.9f, 2.0f, 2.5f, 3.0f, 3.5f, 4.0f };

    /// <summary>オート演奏などキー入力を経由しない打鍵時にドンのフラッシュを光らせる (左右交互、大音符は両面)</summary>
    public static void TriggerDon(bool both = false)
    {
        double now = Raylib.GetTime();
        if (both)
        {
            _drumDonLTime = now;
            _drumDonRTime = now;
            return;
        }
        if (_autoAltDon) _drumDonRTime = now; else _drumDonLTime = now;
        _autoAltDon = !_autoAltDon;
    }

    /// <summary>オート演奏などキー入力を経由しない打鍵時にカッのフラッシュを光らせる (左右交互、大音符は両フチ)</summary>
    public static void TriggerKat(bool both = false)
    {
        double now = Raylib.GetTime();
        if (both)
        {
            _drumKatLTime = now;
            _drumKatRTime = now;
            return;
        }
        if (_autoAltKat) _drumKatRTime = now; else _drumKatLTime = now;
        _autoAltKat = !_autoAltKat;
    }

    /// <summary>2Pの描画状態を曲開始時に初期化する。</summary>
    public static void ResetP2() => _p2.Reset();

    /// <summary>2Pのオート演奏時にドンのフラッシュを点灯する。</summary>
    public static void TriggerP2Don(bool both = false)
    {
        double now = Raylib.GetTime();
        if (both)
        {
            _p2.DrumDonLTime = now;
            _p2.DrumDonRTime = now;
            return;
        }
        if (_p2.AutoAltDon) _p2.DrumDonRTime = now; else _p2.DrumDonLTime = now;
        _p2.AutoAltDon = !_p2.AutoAltDon;
    }

    /// <summary>2Pのオート演奏時にカッのフラッシュを点灯する。</summary>
    public static void TriggerP2Kat(bool both = false)
    {
        double now = Raylib.GetTime();
        if (both)
        {
            _p2.DrumKatLTime = now;
            _p2.DrumKatRTime = now;
            return;
        }
        if (_p2.AutoAltKat) _p2.DrumKatRTime = now; else _p2.DrumKatLTime = now;
        _p2.AutoAltKat = !_p2.AutoAltKat;
    }

    /// <summary>2Pの手動入力に対応する太鼓面または縁のフラッシュを点灯する。</summary>
    public static void TriggerP2Side(bool isDon, bool isLeft, bool isBig)
    {
        double now = Raylib.GetTime();
        if (isDon)
        {
            if (isBig) { _p2.DrumDonLTime = now; _p2.DrumDonRTime = now; }
            else if (isLeft) _p2.DrumDonLTime = now;
            else _p2.DrumDonRTime = now;
        }
        else
        {
            if (isBig) { _p2.DrumKatLTime = now; _p2.DrumKatRTime = now; }
            else if (isLeft) _p2.DrumKatLTime = now;
            else _p2.DrumKatRTime = now;
        }
    }

    /// <summary>手動入力時に左右キー情報を直接使ってフラッシュを光らせる。大音符は両側。</summary>
    public static void TriggerSide(bool isDon, bool isLeft, bool isBig)
    {
        double now = Raylib.GetTime();
        if (isDon)
        {
            if (isBig) { _drumDonLTime = now; _drumDonRTime = now; }
            else if (isLeft) _drumDonLTime = now;
            else _drumDonRTime = now;
        }
        else
        {
            if (isBig) { _drumKatLTime = now; _drumKatRTime = now; }
            else if (isLeft) _drumKatLTime = now;
            else _drumKatRTime = now;
        }
    }

    private static int _lastCombo;
    private static double _comboAnimStart = -1.0;
    // 💡 フレームテーブル版ではアニメ開始時の中間値引き継ぎは行わない（常に先頭フレームから再生）

    private static int _lastScore;
    private static double _scoreAnimStart = -1.0;
    private static float _scoreAnimFrom;

    // スコア加算分ポップアップ
    private const float SP_Y_START = 234f;
    private const float SP_Y_UP = 222f;
    private const float SP_Y_END = 293f;
    private const float SP_X_START = 332f;
    private const float SP_X_OVER = 228f;
    private const float SP_X_END = 234f;
    private const float SP_W_BASE = 38f;
    private const float SP_W_SQUISH = 35f;
    private const float SP_SPACING_BASE = -8f;
    private const float SP_SPACING_SQUISH = -4f;
    private const double SP_FADE_IN = 0.03;
    private const double SP_MOVE1 = 0.08;
    private const double SP_MOVE2 = 0.05;
    private const double SP_HOLD = 0.13;
    private const double SP_STAGGER = 0.01;
    private const double SP_RISE = 0.08;
    private const double SP_FALL = 0.07;

    private struct ScorePopup
    {
        public int Value;
        public double SpawnTime;
        public string Digits;      // Value.ToString() をキャッシュ（毎フレームの再アロケーションを避ける）
        public double ExpireTime;  // 生存期限（SpawnTime + 寿命）をキャッシュ
    }

    private static readonly List<ScorePopup> _scorePopups = new();

    // 💡 段位道場モード(Enso.DanMode)かどうかで背景と難易度アイコン(5つ目)の画像を出し分ける。
    //    通常: Lumen/1.Enso/MiniTaiko/Haikei_H.png, Edit.png
    //    段位: Lumen/5.Dan/DanEnso/Background.png, CourseSymbol.png
    //    _loaded後にモードが切り替わった場合もInit()が呼ばれた時点で追従して再読み込みする。
    private static bool _loadedForDanMode;

    public static void Init()
    {
        if (_loaded && _loadedForDanMode == Enso.DanMode) return;

        if (_loaded)
        {
            // 既にロード済みでモードだけ変わった場合は、モード依存分だけ差し替える
            if (_texHaikeiH2.Id != 0 && _texHaikeiH2.Id != _texHaikeiH.Id)
                Raylib.UnloadTexture(_texHaikeiH2);
            _texHaikeiH2 = default;
            if (_texHaikeiH.Id != 0) Raylib.UnloadTexture(_texHaikeiH);
            {
                var seen = new HashSet<uint>();
                for (int i = 0; i < 5; i++)
                {
                    if (_texDiffIcons[i].Id != 0 && seen.Add(_texDiffIcons[i].Id))
                        Raylib.UnloadTexture(_texDiffIcons[i]);
                }
            }
            UnloadDanGaugeAssets();

            _texHaikeiH = Raylib.LoadTexture(Enso.DanMode
                ? "Lumen/5.Dan/DanEnso/Background.png"
                : "Lumen/1.Enso/MiniTaiko/Haikei_H.png");
            _texHaikeiH2 = Enso.DanMode
                ? _texHaikeiH
                : Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/Haikei_H2.png");

            if (Enso.DanMode)
            {
                var courseSymbol = Raylib.LoadTexture("Lumen/5.Dan/DanEnso/CourseSymbol.png");
                for (int i = 0; i < 5; i++) _texDiffIcons[i] = courseSymbol;
                LoadDanGaugeAssets();
            }
            else
            {
                _texDiffIcons[0] = Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/Easy.png");
                _texDiffIcons[1] = Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/Normal.png");
                _texDiffIcons[2] = Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/Hard.png");
                _texDiffIcons[3] = Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/Oni.png");
                _texDiffIcons[4] = Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/Edit.png");
            }

            _loadedForDanMode = Enso.DanMode;
            return;
        }

        _texHaikeiH = Raylib.LoadTexture(Enso.DanMode
            ? "Lumen/5.Dan/DanEnso/Background.png"
            : "Lumen/1.Enso/MiniTaiko/Haikei_H.png");
        _texHaikeiH2 = Enso.DanMode
            ? _texHaikeiH
            : Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/Haikei_H2.png");


        _texTest = Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/Test.png");

        // インタラクティブ太鼓＆打鍵状態アセットのロード
        _texTaiko = Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/taiko.png");
        _texDrumDonL = Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/f/DH.png");
        _texDrumDonR = Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/f/DM.png");
        _texDrumKatL = Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/f/KH.png");
        _texDrumKatR = Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/f/KM.png");

        // 難易度アイコンのロード（段位モードは5つとも共通でCourseSymbol.png）
        if (Enso.DanMode)
        {
            var courseSymbol = Raylib.LoadTexture("Lumen/5.Dan/DanEnso/CourseSymbol.png");
            for (int i = 0; i < 5; i++) _texDiffIcons[i] = courseSymbol;
            LoadDanGaugeAssets();
        }
        else
        {
            _texDiffIcons[0] = Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/Easy.png");
            _texDiffIcons[1] = Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/Normal.png");
            _texDiffIcons[2] = Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/Hard.png");
            _texDiffIcons[3] = Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/Oni.png");
            _texDiffIcons[4] = Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/Edit.png");
        }

        _texJ = Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/j.png");

        _texAuto = Raylib.LoadTexture("Lumen/Option_Icon/Auto.png");
        _texOptSpeed = Raylib.LoadTexture("Lumen/Option_Icon/Speed.png");
        _texOptDoron = Raylib.LoadTexture("Lumen/Option_Icon/Doron.png");
        _texOptAbekobe = Raylib.LoadTexture("Lumen/Option_Icon/Abekobe.png");
        _texOptRandom = Raylib.LoadTexture("Lumen/Option_Icon/Random.png");

        for (int i = 0; i < 10; i++)
            _texScoreDigits[i] = Raylib.LoadTexture($"Lumen/1.Enso/MiniTaiko/n/c/{i}.png");

        for (int i = 0; i < 10; i++)
        {
            _texComboN[i] = Raylib.LoadTexture($"Lumen/1.Enso/MiniTaiko/n/s/n/{i}.png");
            _texCombo50[i] = Raylib.LoadTexture($"Lumen/1.Enso/MiniTaiko/n/s/50/{i}.png");
            _texCombo100[i] = Raylib.LoadTexture($"Lumen/1.Enso/MiniTaiko/n/s/100/{i}.png");
        }
        // 💡 追加：ComboText.png の読み込み
        _texComboText = Raylib.LoadTexture("Lumen/1.Enso/MiniTaiko/ComboText.png");

        VramProbe.Track("MiniTaiko.Init (all)");

        _loadedForDanMode = Enso.DanMode;
        _loaded = true;
    }

    public static void Unload()
    {
        if (!_loaded) return;

        if (_texHaikeiH2.Id != 0 && _texHaikeiH2.Id != _texHaikeiH.Id)
            Raylib.UnloadTexture(_texHaikeiH2);
        _texHaikeiH2 = default;
        if (_texHaikeiH.Id != 0) Raylib.UnloadTexture(_texHaikeiH);
        if (_texTest.Id != 0) Raylib.UnloadTexture(_texTest);
        if (_texTaiko.Id != 0) Raylib.UnloadTexture(_texTaiko);
        if (_texDrumDonL.Id != 0) Raylib.UnloadTexture(_texDrumDonL);
        if (_texDrumDonR.Id != 0) Raylib.UnloadTexture(_texDrumDonR);
        if (_texDrumKatL.Id != 0) Raylib.UnloadTexture(_texDrumKatL);
        if (_texDrumKatR.Id != 0) Raylib.UnloadTexture(_texDrumKatR);
        {
            var seen = new HashSet<uint>();
            for (int i = 0; i < _texDiffIcons.Length; i++)
            {
                if (_texDiffIcons[i].Id != 0 && seen.Add(_texDiffIcons[i].Id))
                    Raylib.UnloadTexture(_texDiffIcons[i]);
            }
        }
        if (_texJ.Id != 0) Raylib.UnloadTexture(_texJ);
        if (_texAuto.Id != 0) Raylib.UnloadTexture(_texAuto);
        if (_texOptSpeed.Id != 0) Raylib.UnloadTexture(_texOptSpeed);
        if (_texOptDoron.Id != 0) Raylib.UnloadTexture(_texOptDoron);
        if (_texOptAbekobe.Id != 0) Raylib.UnloadTexture(_texOptAbekobe);
        if (_texOptRandom.Id != 0) Raylib.UnloadTexture(_texOptRandom);

        for (int i = 0; i < 10; i++)
        {
            if (_texScoreDigits[i].Id != 0) Raylib.UnloadTexture(_texScoreDigits[i]);
            if (_texComboN[i].Id != 0) Raylib.UnloadTexture(_texComboN[i]);
            if (_texCombo50[i].Id != 0) Raylib.UnloadTexture(_texCombo50[i]);
            if (_texCombo100[i].Id != 0) Raylib.UnloadTexture(_texCombo100[i]);
        }

        UnloadDanGaugeAssets();

        _drumDonLTime = _drumDonRTime = _drumKatLTime = _drumKatRTime = -1.0;
        _scorePopups.Clear();
        _p2.Reset();
        _loaded = false;
    }

    /// <summary>段位道場モード専用アセット (Base.png=Condition_Bar, Context.png=Condition_Content) をロード</summary>
    private static void LoadDanGaugeAssets()
    {
        _texDanConditionBar = Raylib.LoadTexture("Lumen/5.Dan/DanEnso/Base.png");
        if (_texDanConditionBar.Id == 0)
            Console.WriteLine("[MiniTaiko] Base.png の読み込みに失敗しました: Lumen/5.Dan/DanEnso/Base.png");

        _texDanConditionContent = Raylib.LoadTexture("Lumen/5.Dan/DanEnso/Context.png");
        if (_texDanConditionContent.Id == 0)
        {
            Console.WriteLine("[MiniTaiko] Context.png の読み込みに失敗しました: Lumen/5.Dan/DanEnso/Context.png");
        }
        else
        {
            _danConditionContentSrcW = _texDanConditionContent.Width;
            _danConditionContentSrcH = _texDanConditionContent.Height / DAN_CONDITION_CONTENT_ROW_COUNT; // 縦4分割
        }
    }

    /// <summary>段位道場モード専用アセットを破棄</summary>
    private static void UnloadDanGaugeAssets()
    {
        if (_texDanConditionBar.Id != 0) Raylib.UnloadTexture(_texDanConditionBar);
        if (_texDanConditionContent.Id != 0) Raylib.UnloadTexture(_texDanConditionContent);
        _texDanConditionBar = default;
        _texDanConditionContent = default;
        _danConditionContentSrcW = 0;
        _danConditionContentSrcH = 0;
    }

    /// <summary>
    /// Exam.csで解析済みのconditionsを、DanSelect.csと同じ規則で画面表示用の文字列に変換して保持する。
    /// 全曲で閾値が同じ条件は1個(Overall)のバッジ、曲ごとに閾値が異なる条件は1st/2nd/3rdのバッジを
    /// 1行の中に左から右へ並べる。演奏画面側(Program.cs等)が段位選択後・Enso.Init()前後に呼ぶ想定。
    /// </summary>
    public static void SetDanConditions(List<Exam.Condition> conditions, int songCount)
    {
        _danConditionTexts.Clear();
        _danConditionCount = 0;
        if (conditions == null)
        {
            Console.WriteLine("[MiniTaiko] SetDanConditions: conditionsがnullです");
            return;
        }
        Console.WriteLine($"[MiniTaiko] SetDanConditions: conditions={conditions.Count}件, songCount={songCount}");

        foreach (var cond in conditions)
        {
            string label = _danConditionTypeLabels.TryGetValue(cond.Type, out var l) ? l : cond.Type.ToString();
            string unit = _danConditionLessThanTypes.Contains(cond.Type) ? "未満" : "以上";

            bool sameForAllSongs = true;
            for (int i = 1; i < cond.ThresholdsPerSong.Length; i++)
            {
                if (cond.ThresholdsPerSong[i].Red != cond.ThresholdsPerSong[0].Red ||
                    cond.ThresholdsPerSong[i].Gold != cond.ThresholdsPerSong[0].Gold)
                {
                    sameForAllSongs = false;
                    break;
                }
            }

            var entries = new List<DanConditionEntry>();

            if (sameForAllSongs)
            {
                var th = cond.ThresholdsPerSong.Length > 0 ? cond.ThresholdsPerSong[0] : default;
                entries.Add(new DanConditionEntry { Row = DanConditionContentRow.Overall, Value = $"{th.Red}{unit}" });
            }
            else
            {
                for (int i = 0; i < cond.ThresholdsPerSong.Length; i++)
                {
                    var th = cond.ThresholdsPerSong[i];
                    int rowIndex = Math.Min(i + 1, (int)DanConditionContentRow.Song3rd);
                    entries.Add(new DanConditionEntry { Row = (DanConditionContentRow)rowIndex, Value = $"{th.Red}{unit}" });
                }
            }

            _danConditionTexts.Add((label, entries));
        }

        _danConditionCount = _danConditionTexts.Count;
        Console.WriteLine($"[MiniTaiko] SetDanConditions: _danConditionCount={_danConditionCount}");
    }

    /// <summary>
    /// 段位道場モードの合格条件を、DanSelect.csと同じ見た目(バー+バッジ+ラベル+「〇〇未満/以上」)で描画する。
    /// 1. Base.png（Condition_Bar相当）を条件の種類数ぶん縦に並べる
    /// 2. その上にContext.png（Condition_Content相当、Overall/1st/2nd/3rd）を重ねる
    /// 3. ラベル文字と数値文字(バッジの上)を描画する
    /// DanModeでない場合は何もしない。
    /// </summary>
    private static bool _loggedDrawDanGaugeSkip;

    public static void DrawDanGauge(float vx, float vy, float s)
    {
        if (!_loaded)
        {
            if (!_loggedDrawDanGaugeSkip) { Console.WriteLine("[MiniTaiko] DrawDanGauge: _loadedがfalseのため描画しません"); _loggedDrawDanGaugeSkip = true; }
            return;
        }
        if (!Enso.DanMode)
        {
            return; // 通常プレイでは毎フレーム出るログなので何も出さない
        }
        if (_danConditionCount == 0)
        {
            if (!_loggedDrawDanGaugeSkip) { Console.WriteLine("[MiniTaiko] DrawDanGauge: _danConditionCount=0のため描画しません(SetDanConditionsが呼ばれていないか、conditionsが空)"); _loggedDrawDanGaugeSkip = true; }
            return;
        }
        _loggedDrawDanGaugeSkip = false;

        // 💡 個別条件(1st/2nd/3rd)は現在演奏中の曲(Enso.DanSongIndex)に対応するバッジだけを表示する。
        //    全体条件(Overall)は常に1個表示。フィルタ後は各条件につき最大1バッジになるので、
        //    複数バッジを横に並べる必要はなくなり、常にContentOffsetの位置に1個だけ描画する。
        var visibleEntries = new List<DanConditionEntry>[_danConditionTexts.Count];
        for (int i = 0; i < _danConditionTexts.Count; i++)
        {
            var (_, entries) = _danConditionTexts[i];
            DanConditionEntry visible = null;
            foreach (var e in entries)
            {
                if (e.Row == DanConditionContentRow.Overall) { visible = e; break; }
                int songIdx = (int)e.Row - 1; // Song1st=0, Song2nd=1, Song3rd=2
                if (songIdx == Enso.DanSongIndex) { visible = e; break; }
            }
            visibleEntries[i] = visible != null ? new List<DanConditionEntry> { visible } : new List<DanConditionEntry>();
        }

        float x = DanConditionBarX * s + vx;
        float y = DanConditionBarY * s + vy;
        float lineHeight = DanConditionBarLineHeight * s;

        // 1. 土台バー(Base.png)を条件の種類数ぶん並べる
        if (_texDanConditionBar.Id != 0)
        {
            float barW = _texDanConditionBar.Width * DanConditionBarScale * s;
            float barH = _texDanConditionBar.Height * DanConditionBarScale * s;

            for (int i = 0; i < _danConditionCount; i++)
            {
                Rectangle dest = new Rectangle(x, y + i * lineHeight, barW, barH);
                Raylib.DrawTexturePro(_texDanConditionBar,
                    new Rectangle(0, 0, _texDanConditionBar.Width, _texDanConditionBar.Height),
                    dest, Vector2.Zero, 0f, Color.White);
            }
        }

        // 2. バッジ(Context.png)を重ねる（現在の曲に対応する1個だけ）
        if (_texDanConditionContent.Id != 0 && _danConditionContentSrcW > 0 && _danConditionContentSrcH > 0)
        {
            float contentW = _danConditionContentSrcW * DanConditionContentScale * s;
            float contentH = _danConditionContentSrcH * DanConditionContentScale * s;
            float contentBaseX = x + DanConditionContentOffsetX * s;
            float contentBaseY = y + DanConditionContentOffsetY * s;

            for (int i = 0; i < visibleEntries.Length; i++)
            {
                if (visibleEntries[i].Count == 0) continue;
                int rowIndex = (int)visibleEntries[i][0].Row;
                Rectangle src = new Rectangle(0, rowIndex * _danConditionContentSrcH, _danConditionContentSrcW, _danConditionContentSrcH);
                Rectangle dest = new Rectangle(contentBaseX, contentBaseY + i * lineHeight, contentW, contentH);
                Raylib.DrawTexturePro(_texDanConditionContent, src, dest, Vector2.Zero, 0f, Color.White);
            }
        }

        // 3. ラベル文字・数値文字
        float labelX = DanConditionLabelX * s + vx;
        float labelY = DanConditionLabelY * s + vy;
        float labelLineHeight = DanConditionLabelLineHeight * s;
        float labelFontSize = DanConditionLabelFontSize * s;

        float valueOffsetX = DanConditionValueOffsetX * s;
        float valueOffsetY = DanConditionValueOffsetY * s;
        float valueFontSize = DanConditionValueFontSize * s;

        float badgeBaseX = x + DanConditionContentOffsetX * s;
        float badgeBaseY = y + DanConditionContentOffsetY * s;

        for (int i = 0; i < _danConditionTexts.Count; i++)
        {
            var (label, _) = _danConditionTexts[i];

            if (!string.IsNullOrEmpty(label))
            {
                Vector2 labelSize = Raylib.MeasureTextEx(G.Font, label, labelFontSize, 0);
                Vector2 labelPos = new Vector2(labelX - labelSize.X / 2f, labelY + i * labelLineHeight);
                G.DrawTextWithOutline16(G.Font, label, labelPos, labelFontSize, Color.White, DanConditionLabelOutlineColor, DanConditionLabelOutlineThickness * s, 0);
            }

            if (visibleEntries[i].Count > 0)
            {
                string value = visibleEntries[i][0].Value;
                if (!string.IsNullOrEmpty(value))
                {
                    float badgeX = badgeBaseX;
                    float badgeY = badgeBaseY + i * lineHeight;
                    // 💡 右揃え(右端=badgeX+valueOffsetXを固定して左へ伸びる)で描く。
                    //    桁数が変わっても「未満/以上」の位置がずれないようにするため。
                    Vector2 valueSize = Raylib.MeasureTextEx(G.Font, value, valueFontSize, 0);
                    Vector2 valuePos = new Vector2(badgeX + valueOffsetX - valueSize.X, badgeY + valueOffsetY);
                    G.DrawTextWithOutline16(G.Font, value, valuePos, valueFontSize, Color.White, Color.Black, valueFontSize * 0.2f, 0);
                }
            }
        }
    }

    /// <summary>
    /// 背景 (Haikei_H.png) のみを描画。Effect/l をこの上に重ねるため本体描画とは分離している
    /// </summary>
    public static void DrawHaikei(float vx, float vy, float s)
    {
        if (!_loaded || _texHaikeiH.Id == 0) return;

        float hx = HaikeiX * s + vx;
        float hy = HaikeiY * s + vy;
        float w = _texHaikeiH.Width * s;
        float h = _texHaikeiH.Height * s;

        Rectangle sourceRect = new Rectangle(0, 0, _texHaikeiH.Width, _texHaikeiH.Height);
        Rectangle destRect = new Rectangle(hx, hy, w, h);
        Vector2 origin = new Vector2(0f, 0f);

        Raylib.DrawTexturePro(_texHaikeiH, sourceRect, destRect, origin, 0f, Color.White);
    }

    /// <summary>
    /// AUTO表示（Auto.png）のみを描画。呼び出し側でAuto==trueの時のみ呼ぶこと
    /// </summary>
    public static void DrawAuto(float vx, float vy, float s)
    {
        if (!_loaded || _texAuto.Id == 0) return;

        float ax = AutoX * s + vx;
        float ay = AutoY * s + vy;
        float w = _texAuto.Width * s;
        float h = _texAuto.Height * s;

        Rectangle sourceRect = new Rectangle(0, 0, _texAuto.Width, _texAuto.Height);
        Rectangle destRect = new Rectangle(ax, ay, w, h);

        Raylib.DrawTexturePro(_texAuto, sourceRect, destRect, Vector2.Zero, 0f, Color.White);
    }

    /// <summary>
    /// AUTO + 演奏オプション(はやさ/ドロン/あべこべ/ランダム)アイコンをまとめて描画する。
    /// AUTOは常にAutoX,AutoYの位置(1枠目)に固定。以降、有効なオプションだけを
    /// 1行OptionIconPerRow個ずつ、AutoXを起点に折り返して並べる
    /// (4個目からはAutoXへ戻り、Yだけ1行分下がる)。
    /// デフォルト値のままのオプションはアイコンごと表示しない。
    /// </summary>
    public static void DrawOptionIcons(float vx, float vy, float s)
    {
        if (!_loaded) return;

        int slot = 0;

        void PlaceNext(Texture2D tex, int frameIndex, int frameCount)
        {
            if (tex.Id == 0) return;

            int col = slot % OptionIconPerRow;
            int row = slot / OptionIconPerRow;
            slot++;

            float frameW = tex.Width / (float)frameCount;
            float frameH = tex.Height;

            float x = (AutoX + col * OptionIconColGapX) * s + vx;
            float y = (AutoY + row * OptionIconRowGapY) * s + vy;

            Rectangle src = new Rectangle(frameIndex * frameW, 0, frameW, frameH);
            Rectangle dest = new Rectangle(x, y, frameW * OptionIconScale * s, frameH * OptionIconScale * s);

            Raylib.DrawTexturePro(tex, src, dest, Vector2.Zero, 0f, Color.White);
        }

        // 1枠目: AUTO(有効な時だけ。表示自体はコマ分割なし=1枚絵)
        if (Enso.Auto)
            PlaceNext(_texAuto, 0, 1);

        // はやさ: 1.0(デフォルト)の間は非表示。1.1〜4.0の対応コマを表示
        float speedValue = DiffSelectScene.SpeedMultiplier;
        if (MathF.Abs(speedValue - 1.0f) > 0.001f)
            PlaceNext(_texOptSpeed, SpeedIconFrameIndex(speedValue) + 1, SPEED_ICON_FRAME_COUNT);

        // ドロン: しない(デフォルト)の間は非表示。する=右側(index1)のコマを表示
        if (DiffSelectScene.DoronEnabled)
            PlaceNext(_texOptDoron, 1, 2);

        // あべこべ: しない(デフォルト)の間は非表示。する=右側(index1)のコマを表示
        if (DiffSelectScene.AbekobeEnabled)
            PlaceNext(_texOptAbekobe, 1, 2);

        // ランダム: なし(デフォルト=0)の間は非表示。きまぐれ=index1(真ん中)、でたらめ=index2(右端)
        int randomMode = DiffSelectScene.RandomMode;
        if (randomMode != 0)
            PlaceNext(_texOptRandom, randomMode, 3);
    }

    /// <summary>
    /// はやさアイコンのコマ番号を、現在の速度値に最も近いものから求める。
    /// 戻り値はSpeedIconValues配列内でのindex(0〜13)。実際に描画するコマ番号は
    /// Speed.png側の「なし(1.0)」コマ分だけずらして呼び出し側で+1する。
    /// </summary>
    private static int SpeedIconFrameIndex(float speed)
    {
        int bestIdx = 0;
        float bestDiff = float.MaxValue;
        for (int i = 0; i < SpeedIconValues.Length; i++)
        {
            float diff = MathF.Abs(SpeedIconValues[i] - speed);
            if (diff < bestDiff) { bestDiff = diff; bestIdx = i; }
        }
        return bestIdx;
    }

    /// <summary>
    /// ミニ太鼓と数字フォント、コンボグラフィックの統合描画
    /// </summary>
    public static void Draw(float vx, float vy, float s, float LANE_Y, int score, int combo)
    {
        if (!_loaded) return;

        float mtx = MiniTaikoX * s + vx;
        float mty = (LANE_Y + MiniTaikoYOffset) * s + vy;

        float taikoLeft = TaikoX * s + vx;
        float taikoTop = TaikoY * s + vy;
        float dx = taikoLeft;
        float dy = taikoTop;

        if (_texTaiko.Id != 0)
        {
            float w = _texTaiko.Width * s;
            float h = _texTaiko.Height * s;
            dx = taikoLeft + w / 2f;
            dy = taikoTop + h / 2f;

            Rectangle sourceRect = new Rectangle(0, 0, _texTaiko.Width, _texTaiko.Height);
            Rectangle destRect = new Rectangle(taikoLeft, taikoTop, w, h);
            Vector2 origin = new Vector2(0f, 0f);

            Raylib.DrawTexturePro(_texTaiko, sourceRect, destRect, origin, 0f, Color.White);
        }

        // 💡 [新規] 難易度アイコンの描画（taiko.pngの左側）
        // 2. 難易度アイコンの選択
        int diffIdx = TJA.SelectedCourseType switch
        {
            Course.Easy => 0,
            Course.Normal => 1,
            Course.Hard => 2,
            Course.Oni => 3,
            _ => 4 // Edit
        };

        Texture2D diffTex = _texDiffIcons[diffIdx];
        if (diffTex.Id != 0)
        {
            // 設定された基準サイズに、個別のスケール倍率と全体の拡大率(s)を掛け合わせて「大きさ」を決定
            float finalWidth = DiffWidth * DiffScale * s;
            float finalHeight = DiffHeight * DiffScale * s;

            // taiko.png の描画基準位置（taikoLeft, taikoTop）からの相対位置で配置
            float finalX = taikoLeft + (DiffXOffset * s);
            float finalY = taikoTop + (DiffYOffset * s);

            Rectangle srcDiff = new Rectangle(0, 0, diffTex.Width, diffTex.Height);
            Rectangle destDiff = new Rectangle(finalX, finalY, finalWidth, finalHeight);

            // 描画実行
            Raylib.DrawTexturePro(diffTex, srcDiff, destDiff, Vector2.Zero, 0f, Color.White);
        }

        // 💡 3. 叩かれた瞬間の打鍵フラッシュを、打鍵太鼓(dx, dy)の座標に重ねて描画
        {
            double now = Raylib.GetTime();

            // 💡 キーポーリングは廃止。TriggerDon/TriggerKat経由でのみフラッシュを更新する。
            //    （DrumInputのレート制限・縁キー誤反応問題の解消のため）
            if (!SettingsPanel.Open)
            {
                if (Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    if (_mouseAltDon) _drumDonRTime = now; else _drumDonLTime = now;
                    _mouseAltDon = !_mouseAltDon;
                }
                if (Raylib.IsMouseButtonPressed(MouseButton.Right))
                {
                    if (_mouseAltKat) _drumKatRTime = now; else _drumKatLTime = now;
                    _mouseAltKat = !_mouseAltKat;
                }
            }

            DrawDrumPart(_texDrumDonL, _drumDonLTime, now, dx, dy, s);
            DrawDrumPart(_texDrumDonR, _drumDonRTime, now, dx, dy, s);
            DrawDrumPart(_texDrumKatL, _drumKatLTime, now, dx, dy, s);
            DrawDrumPart(_texDrumKatR, _drumKatRTime, now, dx, dy, s);
        }

        if (_texJ.Id != 0)
        {
            float coverW = _texJ.Width * s;
            float coverH = _texJ.Height * s;

            float cx = JX * s + vx;
            float cy = JY * s + vy;

            Rectangle srcCover = new Rectangle(0, 0, _texJ.Width, _texJ.Height);
            Rectangle destCover = new Rectangle(cx, cy, coverW, coverH);
            Vector2 originCover = new Vector2(0f, 0f);

            Raylib.DrawTexturePro(_texJ, srcCover, destCover, originCover, 0f, Color.White);

            if (score > _lastScore)
            {
                _scoreAnimFrom = ComputeScorePop();
                _scoreAnimStart = Raylib.GetTime();
                int popupValue = score - _lastScore;
                double spawnTime = Raylib.GetTime();
                string popupDigits = popupValue.ToString();
                double expireTime = spawnTime + SP_MOVE1 + SP_MOVE2 + SP_HOLD
                    + (popupDigits.Length - 1) * SP_STAGGER + SP_RISE + SP_FALL;
                _scorePopups.Add(new ScorePopup
                {
                    Value = popupValue,
                    SpawnTime = spawnTime,
                    Digits = popupDigits,
                    ExpireTime = expireTime,
                });
            }
            _lastScore = score;

            float scorePop = ComputeScorePop();

            string scoreStr = score.ToString();
            float scoreAnchorX = ScoreRightX * s + vx;
            float scoreAnchorY = ScoreBaseY * s + vy;
            float scoreGap = ScoreDigitGap * s;

            float prevLeft = scoreAnchorX;
            for (int i = scoreStr.Length - 1; i >= 0; i--)
            {
                int digit = Math.Clamp(scoreStr[i] - '0', 0, 9);
                Texture2D tex = _texScoreDigits[digit];
                if (tex.Id == 0) continue;

                float w = tex.Width * s;
                float baseH = tex.Height * s;
                float h = baseH + (ScorePopHeight * s - baseH) * scorePop;
                float top = scoreAnchorY + baseH - h;
                float left = (i == scoreStr.Length - 1) ? scoreAnchorX : (prevLeft - scoreGap - w);

                Rectangle src = new Rectangle(0, 0, tex.Width, tex.Height);
                Rectangle dst = new Rectangle(left, top, w, h);
                Raylib.DrawTexturePro(tex, src, dst, new Vector2(0f, 0f), 0f, Color.White);

                prevLeft = left;
            }

            // 加算スコアポップアップはドンちゃんより前面へ出すため、後段のDrawScorePopupOverlayで描画する。
        }

        if (combo > _lastCombo)
        {
            // 💡 トリガーのたびに先頭フレームから再生しなおす
            _comboAnimStart = Raylib.GetTime();
        }
        _lastCombo = combo;

        if (combo >= ComboDisplayMin)
        {
            Texture2D[] digits = combo >= 100 ? _texCombo100 : (combo >= 50 ? _texCombo50 : _texComboN);

            string comboStr = combo.ToString();
            // 桁が増えすぎる(コンボ数が閾値超え)場合はX方向のみ縮小する
            float overflowScaleX = combo >= ComboOverflowThreshold ? ComboOverflowScaleX : 1f;
            float gap = ComboDigitGap * s * overflowScaleX;
            float centerX = dx;
            float y = ComboDigitY * s + vy;

            // 💡 フレームテーブル版: scale は 1.0 = 等倍、1.2 なら 20% 大きい拡大倍率
            float scale = ComputeComboPop();

            float totalW = 0f;
            for (int i = 0; i < comboStr.Length; i++)
            {
                Texture2D t = digits[Math.Clamp(comboStr[i] - '0', 0, 9)];
                if (t.Id == 0) continue;
                totalW += t.Width * s * overflowScaleX;
                if (i < comboStr.Length - 1) totalW += gap;
            }

            float penX = centerX - totalW / 2f;
            float refH = 0f;
            for (int i = 0; i < comboStr.Length; i++)
            {
                Texture2D t = digits[Math.Clamp(comboStr[i] - '0', 0, 9)];
                if (t.Id == 0) continue;

                float w = t.Width * s * overflowScaleX;
                float baseH = t.Height * s;
                // 💡 テーブルの拡大率をそのまま乗算。ピボットは数字の下端(ComboPopAnchorOffset上)で固定。
                float h = baseH * scale;
                // 下端アンカー固定で上方向へ拡大: top = y - (h - baseH)
                float top = y - (h - baseH);
                Rectangle src = new Rectangle(0, 0, t.Width, t.Height);
                Rectangle dst = new Rectangle(penX, top, w, h);
                Raylib.DrawTexturePro(t, src, dst, new Vector2(0f, 0f), 0f, Color.White);

                penX += w + gap;
                refH = baseH;
            }

            // 💡 Drawメソッド内のComboText.png描画部分
            if (_texComboText.Id != 0)
            {
                float imgW = _texComboText.Width * s;
                float imgH = _texComboText.Height * s;

                // 💡 右にずらしたい場合は + 10f、左にずらしたい場合は - 10f などで微調整できます
                float textOffsetX = 5f; // 👈 ココをいじる！

                // 計算された中央位置に、微調整値を適用する
                float textX = centerX - imgW / 2f + (textOffsetX * s);
                float textY = y + refH + (ComboTextYOffset * s) - imgH / 2f;

                Rectangle src = new Rectangle(0, 0, _texComboText.Width, _texComboText.Height);
                Rectangle dst = new Rectangle(textX, textY, imgW, imgH);

                Raylib.DrawTexturePro(_texComboText, src, dst, Vector2.Zero, 0f, Color.White);
            }
        }
    }

    /// <summary>
    /// 2Pミニ太鼓を描画する。レーン間隔からYオフセットを計算するため、1P用の
    /// レイアウト調整値をそのまま保ったまま2P用UIを下段に配置できる。
    /// </summary>
    public static void DrawP2(float vx, float vy, float s, float laneY, int score, int combo)
    {
        if (!_loaded) return;

        float yOffset = laneY - Enso.LANE_Y;
        float taikoLeft = TaikoX * s + vx;
        float taikoTop = (TaikoY + yOffset) * s + vy;
        float dx = taikoLeft;
        float dy = taikoTop;

        if (_texTaiko.Id != 0)
        {
            float w = _texTaiko.Width * s;
            float h = _texTaiko.Height * s;
            dx = taikoLeft + w / 2f;
            dy = taikoTop + h / 2f;
            Raylib.DrawTexturePro(_texTaiko,
                new Rectangle(0, 0, _texTaiko.Width, _texTaiko.Height),
                new Rectangle(taikoLeft, taikoTop, w, h), Vector2.Zero, 0f, Color.White);
        }

        int diffIdx = TJA.SelectedCourseType switch
        {
            Course.Easy => 0,
            Course.Normal => 1,
            Course.Hard => 2,
            Course.Oni => 3,
            _ => 4,
        };
        Texture2D diffTex = _texDiffIcons[diffIdx];
        if (diffTex.Id != 0)
        {
            // 2PはP2Diff*で独立調整する。1PのDiff*は参照しない。
            Raylib.DrawTexturePro(diffTex, new Rectangle(0, 0, diffTex.Width, diffTex.Height),
                new Rectangle(taikoLeft + P2DiffXOffset * s, taikoTop + P2DiffYOffset * s,
                    P2DiffWidth * P2DiffScale * s, P2DiffHeight * P2DiffScale * s),
                Vector2.Zero, 0f, Color.White);
        }

        double now = Raylib.GetTime();
        DrawDrumPart(_texDrumDonL, _p2.DrumDonLTime, now, dx, dy, s);
        DrawDrumPart(_texDrumDonR, _p2.DrumDonRTime, now, dx, dy, s);
        DrawDrumPart(_texDrumKatL, _p2.DrumKatLTime, now, dx, dy, s);
        DrawDrumPart(_texDrumKatR, _p2.DrumKatRTime, now, dx, dy, s);

        if (_texJ.Id != 0)
        {
            float coverW = _texJ.Width * s;
            float coverH = _texJ.Height * s;
            // 2P用のj.pngは上下反転し、下段レーン向けの位置へ移す。
            Rectangle flippedCoverSrc = new Rectangle(0, _texJ.Height, _texJ.Width, -_texJ.Height);
            Raylib.DrawTexturePro(_texJ, flippedCoverSrc,
                new Rectangle(JX * s + vx, (JY + yOffset + P2ScoreCoverOffsetY) * s + vy, coverW, coverH),
                Vector2.Zero, 0f, Color.White);

            if (score > _p2.LastScore)
            {
                _p2.ScoreAnimFrom = ComputeScorePop(_p2.ScoreAnimStart, _p2.ScoreAnimFrom);
                _p2.ScoreAnimStart = now;
                int popupValue = score - _p2.LastScore;
                string popupDigits = popupValue.ToString();
                _p2.ScorePopups.Add(new ScorePopup
                {
                    Value = popupValue,
                    SpawnTime = now,
                    Digits = popupDigits,
                    ExpireTime = now + SP_MOVE1 + SP_MOVE2 + SP_HOLD
                        + (popupDigits.Length - 1) * SP_STAGGER + SP_RISE + SP_FALL,
                });
            }
            _p2.LastScore = score;

            float scorePop = ComputeScorePop(_p2.ScoreAnimStart, _p2.ScoreAnimFrom);
            string scoreStr = score.ToString();
            float scoreAnchorX = ScoreRightX * s + vx;
            float scoreAnchorY = (ScoreBaseY + yOffset + P2ScoreNumberOffsetY) * s + vy;
            float scoreGap = ScoreDigitGap * s;
            float prevLeft = scoreAnchorX;
            for (int i = scoreStr.Length - 1; i >= 0; i--)
            {
                Texture2D tex = _texScoreDigits[Math.Clamp(scoreStr[i] - '0', 0, 9)];
                if (tex.Id == 0) continue;
                float w = tex.Width * s;
                float baseH = tex.Height * s;
                float h = baseH + (ScorePopHeight * s - baseH) * scorePop;
                float top = scoreAnchorY + baseH - h;
                float left = i == scoreStr.Length - 1 ? scoreAnchorX : prevLeft - scoreGap - w;
                Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height),
                    new Rectangle(left, top, w, h), Vector2.Zero, 0f, Color.White);
                prevLeft = left;
            }
            // 2P加算スコアポップアップも後段のDrawScorePopupOverlayP2で描画する。
        }

        if (combo > _p2.LastCombo) _p2.ComboAnimStart = now;
        _p2.LastCombo = combo;

        if (combo >= ComboDisplayMin)
        {
            Texture2D[] digits = combo >= 100 ? _texCombo100 : (combo >= 50 ? _texCombo50 : _texComboN);
            string comboStr = combo.ToString();
            float overflowScaleX = combo >= ComboOverflowThreshold ? ComboOverflowScaleX : 1f;
            float gap = ComboDigitGap * s * overflowScaleX;
            float centerX = dx;
            float y = (ComboDigitY + yOffset) * s + vy;
            float scale = ComputeComboPop(_p2.ComboAnimStart);

            float totalW = 0f;
            for (int i = 0; i < comboStr.Length; i++)
            {
                Texture2D t = digits[Math.Clamp(comboStr[i] - '0', 0, 9)];
                if (t.Id == 0) continue;
                totalW += t.Width * s * overflowScaleX;
                if (i < comboStr.Length - 1) totalW += gap;
            }

            float penX = centerX - totalW / 2f;
            float refH = 0f;
            for (int i = 0; i < comboStr.Length; i++)
            {
                Texture2D t = digits[Math.Clamp(comboStr[i] - '0', 0, 9)];
                if (t.Id == 0) continue;
                float w = t.Width * s * overflowScaleX;
                float baseH = t.Height * s;
                float h = baseH * scale;
                Raylib.DrawTexturePro(t, new Rectangle(0, 0, t.Width, t.Height),
                    new Rectangle(penX, y - (h - baseH), w, h), Vector2.Zero, 0f, Color.White);
                penX += w + gap;
                refH = baseH;
            }

            if (_texComboText.Id != 0)
            {
                float imgW = _texComboText.Width * s;
                float imgH = _texComboText.Height * s;
                Raylib.DrawTexturePro(_texComboText,
                    new Rectangle(0, 0, _texComboText.Width, _texComboText.Height),
                    new Rectangle(centerX - imgW / 2f + 5f * s,
                        y + refH + ComboTextYOffset * s - imgH / 2f, imgW, imgH),
                    Vector2.Zero, 0f, Color.White);
            }
        }
    }

    /// <summary>2Pレーンに合わせてミニ太鼓用背景を描画する。</summary>
    public static void DrawHaikeiP2(float vx, float vy, float s, float laneY)
    {
        if (!_loaded) return;
        Texture2D tex = _texHaikeiH2.Id != 0 ? _texHaikeiH2 : _texHaikeiH;
        if (tex.Id == 0) return;

        float yOffset = laneY - Enso.LANE_Y;
        Raylib.DrawTexturePro(tex,
            new Rectangle(0, 0, tex.Width, tex.Height),
            new Rectangle(HaikeiX * s + vx, (HaikeiY + yOffset) * s + vy,
                tex.Width * s, tex.Height * s),
            Vector2.Zero, 0f, Color.White);
    }

    /// <summary>ミニ太鼓本体より後段に描く1P加算スコアポップアップ。</summary>
    public static void DrawScorePopupOverlay(float vx, float vy, float s)
    {
        if (!_loaded) return;
        DrawScorePopups(vx, vy, s);
    }

    /// <summary>ミニ太鼓本体より後段に描く2P加算スコアポップアップ。</summary>
    public static void DrawScorePopupOverlayP2(float vx, float vy, float s, float laneY)
    {
        if (!_loaded) return;
        float yOffset = laneY - Enso.LANE_Y;
        DrawScorePopups(_p2.ScorePopups, vx, vy, s, yOffset + P2ScorePopupOffsetY);
    }

    private static void DrawScorePopups(float vx, float vy, float s)
    {
        double now = Raylib.GetTime();

        // 期限切れのものを末尾から手動で除去（ラムダのクロージャ確保を避ける）
        for (int pi = _scorePopups.Count - 1; pi >= 0; pi--)
        {
            if (now >= _scorePopups[pi].ExpireTime)
                _scorePopups.RemoveAt(pi);
        }

        // 古いものから描画して新しいポップアップが上に重なるようにする
        for (int pi = 0; pi < _scorePopups.Count; pi++)
        {
            var p = _scorePopups[pi];
            double el = now - p.SpawnTime;
            string str = p.Digits;
            int n = str.Length;

            if (el < 0) continue;

            float w, spacing, anchorX;
            if (el < SP_MOVE1)
            {
                float t = (float)(el / SP_MOVE1);
                w = SP_W_BASE + (SP_W_SQUISH - SP_W_BASE) * t;
                spacing = SP_SPACING_BASE + (SP_SPACING_SQUISH - SP_SPACING_BASE) * t;
                anchorX = SP_X_START + (SP_X_OVER - SP_X_START) * t;
            }
            else if (el < SP_MOVE1 + SP_MOVE2)
            {
                float t = (float)((el - SP_MOVE1) / SP_MOVE2);
                w = SP_W_SQUISH + (SP_W_BASE - SP_W_SQUISH) * t;
                spacing = SP_SPACING_SQUISH + (SP_SPACING_BASE - SP_SPACING_SQUISH) * t;
                anchorX = SP_X_OVER + (SP_X_END - SP_X_OVER) * t;
            }
            else
            {
                w = SP_W_BASE;
                spacing = SP_SPACING_BASE;
                anchorX = SP_X_END;
            }

            double fallStartBase = SP_MOVE1 + SP_MOVE2 + SP_HOLD;

            for (int i = 0; i < n; i++)
            {
                int idxFromRight = n - 1 - i;
                Texture2D tex = _texScoreDigits[Math.Clamp(str[i] - '0', 0, 9)];
                if (tex.Id == 0) continue;

                double dEl = el - (fallStartBase + idxFromRight * SP_STAGGER);

                float y = SP_Y_START;
                float alphaF = 1f;
                if (dEl >= SP_RISE)
                {
                    float t = (float)((dEl - SP_RISE) / SP_FALL);
                    if (t >= 1f) continue;
                    float eased = t * t * t;
                    y = SP_Y_UP + (SP_Y_END - SP_Y_UP) * eased;
                    alphaF = 1f - t;
                }
                else if (dEl >= 0)
                {
                    float t = (float)(dEl / SP_RISE);
                    y = SP_Y_START + (SP_Y_UP - SP_Y_START) * t;
                }

                alphaF *= (float)Math.Clamp(el / SP_FADE_IN, 0.0, 1.0);

                float left = anchorX - idxFromRight * (w + spacing);
                byte alpha = (byte)(Math.Clamp(alphaF, 0f, 1f) * 255);

                Rectangle src = new Rectangle(0, 0, tex.Width, tex.Height);
                Rectangle dst = new Rectangle(left * s + vx, y * s + vy, w * s, tex.Height * s);

                // 白い部分を #f84828 に着色 (乗算ティントなので黒はそのまま)
                Raylib.DrawTexturePro(tex, src, dst, Vector2.Zero, 0f,
                    new Color((byte)0xF8, (byte)0x48, (byte)0x28, alpha));
            }
        }
    }

    private static void DrawScorePopups(List<ScorePopup> popups, float vx, float vy, float s, float yOffset)
    {
        double now = Raylib.GetTime();
        for (int pi = popups.Count - 1; pi >= 0; pi--)
        {
            if (now >= popups[pi].ExpireTime) popups.RemoveAt(pi);
        }

        for (int pi = 0; pi < popups.Count; pi++)
        {
            var p = popups[pi];
            double el = now - p.SpawnTime;
            string str = p.Digits;
            int n = str.Length;
            if (el < 0) continue;

            float w, spacing, anchorX;
            if (el < SP_MOVE1)
            {
                float t = (float)(el / SP_MOVE1);
                w = SP_W_BASE + (SP_W_SQUISH - SP_W_BASE) * t;
                spacing = SP_SPACING_BASE + (SP_SPACING_SQUISH - SP_SPACING_BASE) * t;
                anchorX = SP_X_START + (SP_X_OVER - SP_X_START) * t;
            }
            else if (el < SP_MOVE1 + SP_MOVE2)
            {
                float t = (float)((el - SP_MOVE1) / SP_MOVE2);
                w = SP_W_SQUISH + (SP_W_BASE - SP_W_SQUISH) * t;
                spacing = SP_SPACING_SQUISH + (SP_SPACING_BASE - SP_SPACING_SQUISH) * t;
                anchorX = SP_X_OVER + (SP_X_END - SP_X_OVER) * t;
            }
            else
            {
                w = SP_W_BASE;
                spacing = SP_SPACING_BASE;
                anchorX = SP_X_END;
            }

            double fallStartBase = SP_MOVE1 + SP_MOVE2 + SP_HOLD;
            for (int i = 0; i < n; i++)
            {
                int idxFromRight = n - 1 - i;
                Texture2D tex = _texScoreDigits[Math.Clamp(str[i] - '0', 0, 9)];
                if (tex.Id == 0) continue;

                double dEl = el - (fallStartBase + idxFromRight * SP_STAGGER);
                float y = SP_Y_START;
                float alphaF = 1f;
                if (dEl >= SP_RISE)
                {
                    float t = (float)((dEl - SP_RISE) / SP_FALL);
                    if (t >= 1f) continue;
                    y = SP_Y_UP + (SP_Y_END - SP_Y_UP) * t * t * t;
                    alphaF = 1f - t;
                }
                else if (dEl >= 0)
                {
                    y = SP_Y_START + (SP_Y_UP - SP_Y_START) * (float)(dEl / SP_RISE);
                }

                alphaF *= (float)Math.Clamp(el / SP_FADE_IN, 0.0, 1.0);
                float left = anchorX - idxFromRight * (w + spacing);
                Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height),
                    new Rectangle(left * s + vx, (y + yOffset) * s + vy, w * s, tex.Height * s),
                    Vector2.Zero, 0f, new Color((byte)0xF8, (byte)0x48, (byte)0x28,
                        (byte)(Math.Clamp(alphaF, 0f, 1f) * 255)));
            }
        }
    }

    private static float ComputeScorePop()
    {
        return ComputeScorePop(_scoreAnimStart, _scoreAnimFrom);
    }

    private static float ComputeScorePop(double animStart, float animFrom)
    {
        if (animStart < 0) return 0f;
        double grow = ScorePopGrowMs / 1000.0;
        double shrink = ScorePopShrinkMs / 1000.0;
        double el = Raylib.GetTime() - animStart;
        if (el < grow) return animFrom + (1f - animFrom) * (float)(el / grow);
        if (el < grow + shrink) return 1f - (float)((el - grow) / shrink);
        return 0f;
    }

    /// <summary>
    /// コンボアニメの現在の拡大率を返す (1.0 = 等倍)。
    /// フレームテーブル <see cref="ComboScaleFrames"/> を 60fps で再生し、
    /// 2フレーム間を線形補間する。テーブル末尾到達後は 1.0 で静止。
    /// </summary>
    private static float ComputeComboPop()
    {
        return ComputeComboPop(_comboAnimStart);
    }

    private static float ComputeComboPop(double animStart)
    {
        if (animStart < 0) return 1.0f;

        double el = Raylib.GetTime() - animStart;
        double framePos = el / COMBO_FRAME_DT; // 現在の経過フレーム数 (実数)

        int lastFrame = ComboScaleFrames.Length - 1;
        if (framePos >= lastFrame) return ComboScaleFrames[lastFrame]; // 静止

        int f0 = (int)framePos;
        float t = (float)(framePos - f0);
        return ComboScaleFrames[f0] + (ComboScaleFrames[f0 + 1] - ComboScaleFrames[f0]) * t;
    }

    private static float EaseOutCubic(float t)
    {
        float u = 1f - t;
        return 1f - u * u * u;
    }

    private static float EaseInOutCubic(float t)
    {
        return t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f;
    }

    private static void DrawDrumPart(Texture2D tex, double pressTime, double now, float mtx, float mty, float s)
    {
        if (tex.Id == 0 || pressTime < 0) return;

        double elapsed = now - pressTime;
        if (elapsed < 0 || elapsed >= DRUM_DURATION) return;

        byte alpha = 255;
        if (elapsed >= DRUM_HOLD)
            alpha = (byte)(Math.Clamp((DRUM_DURATION - elapsed) / DRUM_FADE, 0.0, 1.0) * 255);

        float w = tex.Width * s;
        float h = tex.Height * s;

        Rectangle sourceRect = new Rectangle(0, 0, tex.Width, tex.Height);
        Rectangle destRect = new Rectangle(mtx, mty, w, h);
        Vector2 origin = new Vector2(w / 2f, h / 2f);

        Raylib.DrawTexturePro(tex, sourceRect, destRect, origin, 0f,
            new Color((byte)255, (byte)255, (byte)255, alpha));
    }
}
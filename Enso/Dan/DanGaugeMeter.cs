using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

/// <summary>
/// 段位道場モード専用:各合格条件(Exam.Condition)ごとに、現在の達成状況を
/// 「数字」(Lumen/5.Dan/DanEnso/Number.png)と「バー」(Lumen/5.Dan/DanEnso/Long.png)で
/// リアルタイムに表示するクラス。MiniTaikoのDrawDanGauge(土台バー+バッジ+閾値の静的表示)とは別物で、
/// こちらは演奏中に刻々と変化する「今の値」「色」「点滅」を担当する。
///
/// 💡 Gauge.cs(魂ゲージ)とは完全に別の仕組みです。混同注意。
///
/// ---- 表示ルール(きぃさんの指定を実装) ----
/// 数字:
///   ・未満系(可の数/不可の数)条件 → 表示する数字 = 赤合格の閾値 - 現在の値 (残り許容数)
///   ・以上系(良の数/連打数/たたけた数/スコア/コンボ数)条件 → 表示する数字 = 現在の値そのまま
///   ・数字の色は白(Number.png上段)⇔グラデーション(Number.png下段)の2種類。
///     以上系: 金合格条件を満たすまでは白、満たした瞬間からグラデーション。
///     未満系: 「合格条件の範囲内」なら基本は白のまま。曲(個別条件なら該当曲、全体条件なら段位全体)の
///             残りノーツが0になった時点で、金合格条件内であればグラデーションに切り替わる。
/// バー:
///   ・演奏中/演奏終了後、達成状況に応じて色が変わる。
///     金合格達成 = レインボー(アニメ) / 赤合格達成(金合格未達) = 桃色 / 失格 = 灰色。
///   ・以上系(増えていくタイプ): 0～49%濃い黄色, 50～99%黄色, 赤合格達成後は桃色。
///     金合格までの残り距離の1/3で桃色がゆっくり点滅、2/3でさらに速く点滅。
///   ・未満系(減っていくタイプ): 残り許容数の割合が 30～100%かつ金合格まだ可能なら桃色、
///     金合格が不可能になったら黄色、20～30%は赤色、20%以下または残り許容数4以下は赤の点滅。
///     楽曲終盤(金合格がまだ可能な場合)はその時点の色で点滅、終了間際はさらに速く点滅する。
///
/// 💡 「終盤」「終了間際」の正確な秒数、Long.png/Number.pngの行割り(何行目が何色か)は
///    実機で目視しながら調整する前提で、下の public static なパラメータに集約してあります。
///    まずはこの値で動かしてみて、ズレていたらここだけ書き換えてください。
/// </summary>
public static class DanGaugeMeter
{
    // ==================================================================
    // 💡 レイアウト調整用パラメータ (MiniTaikoのDanConditionBar*と同じ基準系[1920x1080/vx,vy,s]で指定する)
    //    デフォルトはMiniTaiko.DanConditionBarX/Y等に重ねる位置。実際の見た目に合わせて微調整してください。
    // ==================================================================

    /// <summary>バー(Long.png)の基準位置・拡大率・行間。MiniTaiko.DanConditionBarX/Y/LineHeightと揃えるのが基本</summary>
    public static float BarX = 550f;
    public static float BarY = 475f;
    public static float BarScale = 1f;
    public static float BarLineHeight = 155f;

    /// <summary>バー内でLong.png(縦に色が並んだテクスチャ)を横に伸び縮みさせる領域の幅・高さ</summary>
    public static float BarWidth = 520f;
    public static float BarHeight = 80f;
    public static float BarOffsetX = 20f;
    public static float BarOffsetY = 105f;

    // ==================================================================
    // 💡 全体条件はLarge_Gauge_Base.png、個別条件はSmall_Gauge_Base.pngを土台として敷く。
    //    土台の絵柄によってゲージの実際の長さが変わるため、条件の種類ごとに
    //    ゲージ(Long.png)の長さを別々に指定できるようにしてある。
    // ==================================================================
    public static float BarWidthOverall = 967f;    // 全体条件(Large_Gauge_Base.png)でのゲージの長さ
    public static float BarWidthIndividual = 642f; // 個別条件(Small_Gauge_Base.png)でのゲージの長さ

    /// <summary>土台画像(Large/Small_Gauge_Base.png)の描画位置補正・拡大率</summary>
    public static float BaseOffsetX = 0f;
    public static float BaseOffsetY = 85f;
    public static float BaseScale = 1f;

    // 💡 全体条件(Large_Gauge_Base.png / 大きいゲージ)のリザルト画面(DrawResultRow)だけ
    //    土台・バーの位置を個別に調整したい場合用。演奏中(Draw)は引き続きBaseOffsetX/Y・BarOffsetX/Yを使う。
    public static float BaseOffsetXOverall = 250f;
    public static float BaseOffsetYOverall = 10f;
    public static float BarOffsetXOverall = 270f;
    public static float BarOffsetYOverall = 30f;

    /// <summary>数字(Number.png)の位置・拡大率・桁間隔。MiniTaikoの数値テキスト位置の近くを想定</summary>
    public static float NumberOffsetX = 25f;
    public static float NumberOffsetY = 90f;
    public static float NumberScale = 1f;
    public static float NumberDigitGap = -15f;

    // 💡 全体条件(Large_Gauge_Base.png / 大きいゲージ)のリザルト画面(DrawResultRow)だけ
    //    数字(Number.png)の位置・大きさを個別に調整したい場合用。演奏中(Draw)は引き続きNumberOffsetX/Y・NumberScaleを使う。
    public static float NumberOffsetXOverall = 275f;
    public static float NumberOffsetYOverall = 17.5f;
    public static float NumberScaleOverall = 1f;

    // ==================================================================
    // 💡 リザルト画面専用パラメータ。演奏中の表示(BarX/BarY等)とは別のレイアウトで、
    //    条件バーを横に並べて同時に表示する。
    //    ・全体条件 → 演奏中と同じ Large_Gauge_Base.png(土台)+Long.png(バー) をそのまま流用。
    //    ・個別条件 → リザルト専用の Lumen/5.Dan/DanResult/Small_base.png(土台)+Small.png(バー)。
    //    大きさが違う2種類が横一列に混在するため、列の位置(columnX)は呼び出し側(DanResultScene)が
    //    ResultColumnWidthOverall/Individual + ResultColumnGap を積み上げて計算する。
    // ==================================================================
    public static float ResultBarX = 550f;
    public static float ResultBarY = 475f;
    public static float ResultBarScale = 1f;
    /// <summary>条件(行)ごとの縦方向の間隔。1条件が終わったら次の条件は改行してこの分だけ下にずれる</summary>
    public static float ResultLineHeight = 155f;

    /// <summary>列と列の間の余白(全体条件・個別条件どちらの後にも共通で入る)</summary>
    public static float ResultColumnGap = 40f;
    /// <summary>全体条件(Large_Gauge_Base.png)1列ぶんの目安の幅。列を積み上げる時のオフセット計算に使う</summary>
    public static float ResultColumnWidthOverall = 1050f;
    /// <summary>個別条件(Small_base.png)1列ぶんの目安の幅。列を積み上げる時のオフセット計算に使う</summary>
    public static float ResultColumnWidthIndividual = 440f;

    // ---- 個別条件(Small_base.png / Small.png)用のオフセット・拡大率 ----
    public static float ResultBarWidth = 320f;
    public static float ResultBarHeight = 40f;
    public static float ResultBarOffsetX = -45f;
    public static float ResultBarOffsetY = 57.5f;

    public static float ResultBaseOffsetX = -50f;
    public static float ResultBaseOffsetY = 50f;
    public static float ResultBaseScale = 1f;

    public static float ResultNumberOffsetX = -35f;
    public static float ResultNumberOffsetY = 52.5f;
    public static float ResultNumberScale = 0.45f;
    /// <summary>個別条件(リザルト画面)専用の数字の桁間隔。全体条件・演奏中のNumberDigitGapとは独立して調整できる</summary>
    public static float ResultNumberDigitGap = -5f;

    // ==================================================================
    // 💡 Long.png の行割り(縦に何色ずつ並んでいるか)。上から0,1,2...の行番号で指定する。
    //    画像を見ながら実際の行番号に直してください。
    // ==================================================================
    public static int RowWhite = 0;       // 白(点滅用の"点滅OFF"側フレーム。灰色ティントをかけてFail表示にも使う)
    public static int RowRed = 1;         // 赤(点滅用の"点滅ON"側フレーム)
    public static int RowDarkYellow = 2;  // 濃い黄色 (以上系 1～49%)
    public static int RowYellow = 3;      // 黄色 (以上系50～99% / 未満系:金合格不可時)
    public static int RowPink = 4;        // 桃色
    public static int RowRainbowStart = 5; // レインボー(アニメ)の先頭行
    public static int RainbowFrameCount = 8;
    public static double RainbowFps = 12.0; // レインボーの色送りアニメ速度(1秒あたりのコマ送り数)

    private static readonly Color COL_GRAY = new Color((byte)150, (byte)150, (byte)150, (byte)255);

    // ==================================================================
    // 💡 点滅速度・しきい値(きぃさんの言う「終盤」「終了間際」「1/3, 2/3」等の実測用パラメータ)
    // ==================================================================
    public static double BlinkSlowHz = 0.75;
    public static double BlinkFastHz = 1.25;

    /// <summary>楽曲進行度(0~1, Enso.SongProgress01)がこれ以上で「終盤」とみなす</summary>
    public static double NearEndProgress = 0.925;
    /// <summary>楽曲進行度がこれ以上で「終了間際」(より速い点滅)とみなす</summary>
    public static double FinalProgress = 0.95;

    /// <summary>コンボ数条件:残り音符数がこの値以下でも未達成なら赤点滅</summary>
    public static int ComboDangerMargin = 50;
    /// <summary>良の数条件:残りノーツ数に対して、まだ出してよい可/不可の割合がこの比率未満なら赤点滅</summary>
    public static double GreatDangerRatio = 0.02;
    /// <summary>連打/たたけた数条件で使う、残り連打秒数から見込む1秒あたりの打数</summary>
    public static double RollHitsPerSecond = 20.0;

    /// <summary>未満系(可/不可)条件:曲(個別条件)または段位全体(全体条件)が始まってからこの秒数の間は
    /// 実際の値がどう変化してもゲージ・数字を一切動かさない(凍結する)。時間経過でのみ解除され、
    /// 途中で不可が出ても解除を早めたりはしない(スキップ不可)。</summary>
    public static double StartGraceSeconds = 3.0;

    // ==================================================================

    public enum BarColor { Gray, DarkYellow, Yellow, Pink, PinkBlinkSlow, PinkBlinkFast, Red, RedBlink, Rainbow }

    private sealed class GaugeEntry
    {
        public Exam.ConditionType Type;
        public bool IsOverall;               // 全体条件かどうか(全曲で閾値共通)
        public Exam.Threshold[] ThresholdsPerSong;

        // 💡 連打条件用: 一度「危険」判定になったら、その連打が終わる(または条件達成)まで
        //    点滅を継続させるためのラッチ。毎フレームの瞬間計算だけだと、連打速度が
        //    上限(20打/秒)ぎりぎりの時にneeded/achievableの大小がフレームごとに反転して
        //    チラついてしまうため。
        public bool RollDangerLatched;

        // 💡 未満系条件用: 曲(個別条件)/段位全体(全体条件)が切り替わったのを検知するためのキー。
        //    切り替わった瞬間の実時間(Raylib.GetTime())をGraceStartTimeに記録し、
        //    そこからStartGraceSeconds秒はゲージを凍結する。
        public int GraceSongKey = int.MinValue;
        public double GraceStartTime = -1.0;

        public int LastDisplayNumber = int.MinValue;
        public double NumberBounceStart = -1.0;
        public bool HasLastColor;
        public BarColor LastColor;
        public double ColorFlashStart = -1.0;
    }

    private static readonly List<GaugeEntry> _entries = new();
    private static int _songCount;

    private static readonly float[] NumberBounceScaleFrames =
    {
        1f, 1.1f, 1.2f, 1.17f, 1.144f, 1.115f, 1.086f, 1.057f, 1.029f, 1f
    };
    private const double NumberBounceFps = 60.0;

    private static Texture2D _texNumber;
    private static Texture2D _texLong;
    private static Texture2D _texBaseLarge;  // 全体条件用土台
    private static Texture2D _texBaseSmall;  // 個別条件用土台
    private static Texture2D _texResultBase; // リザルト専用土台 (Lumen/5.Dan/DanResult/Small_base.png)
    private static Texture2D _texResultBar;  // リザルト専用バー (Lumen/5.Dan/DanResult/Small.png)
    private static int _numberDigitW, _numberDigitH; // Number.png 1コマぶんのサイズ
    private static int _longFrameH;                   // Long.png 1行ぶんの高さ
    private static int _resultBarFrameH;               // Small.png 1行ぶんの高さ(Long.pngと同じ行割り前提)
    private static bool _loaded;

    public static void Init()
    {
        if (_loaded) return;
        _texNumber = Raylib.LoadTexture("Lumen/5.Dan/DanEnso/Number.png");
        _texLong = Raylib.LoadTexture("Lumen/5.Dan/DanEnso/Long.png");
        _texBaseLarge = Raylib.LoadTexture("Lumen/5.Dan/DanEnso/Large_Gauge_Base.png");
        _texBaseSmall = Raylib.LoadTexture("Lumen/5.Dan/DanEnso/Small_Gauge_Base.png");
        // 💡 リザルト専用アセット。演奏中のLarge/Small_Gauge_Base.png・Long.pngとは別物。
        _texResultBase = Raylib.LoadTexture("Lumen/5.Dan/DanResult/Small_base.png");
        _texResultBar = Raylib.LoadTexture("Lumen/5.Dan/DanResult/Small.png");

        if (_texBaseLarge.Id == 0)
            Console.WriteLine("[DanGaugeMeter] Large_Gauge_Base.png の読み込みに失敗しました");
        if (_texBaseSmall.Id == 0)
            Console.WriteLine("[DanGaugeMeter] Small_Gauge_Base.png の読み込みに失敗しました");
        if (_texResultBase.Id == 0)
            Console.WriteLine("[DanGaugeMeter] Small_base.png の読み込みに失敗しました");
        if (_texResultBar.Id == 0)
            Console.WriteLine("[DanGaugeMeter] Small.png の読み込みに失敗しました");

        if (_texNumber.Id != 0)
        {
            _numberDigitW = _texNumber.Width / 10;   // 横10コマ(0~9)
            _numberDigitH = _texNumber.Height / 2;   // 縦2段(白/グラデ)
        }
        else
        {
            Console.WriteLine("[DanGaugeMeter] Number.png の読み込みに失敗しました");
        }

        if (_texLong.Id != 0)
        {
            int rowCount = RowRainbowStart + RainbowFrameCount;
            _longFrameH = rowCount > 0 ? _texLong.Height / rowCount : _texLong.Height;
        }
        else
        {
            Console.WriteLine("[DanGaugeMeter] Long.png の読み込みに失敗しました");
        }

        if (_texResultBar.Id != 0)
        {
            // 💡 Small.pngはLong.pngと同じ行割り(白/赤/濃黄/黄/桃/レインボー×N)の縮小版という前提
            int rowCount = RowRainbowStart + RainbowFrameCount;
            _resultBarFrameH = rowCount > 0 ? _texResultBar.Height / rowCount : _texResultBar.Height;
        }

        _loaded = true;
    }

    public static void Unload()
    {
        if (!_loaded) return;
        if (_texNumber.Id != 0) Raylib.UnloadTexture(_texNumber);
        if (_texLong.Id != 0) Raylib.UnloadTexture(_texLong);
        if (_texBaseLarge.Id != 0) Raylib.UnloadTexture(_texBaseLarge);
        if (_texBaseSmall.Id != 0) Raylib.UnloadTexture(_texBaseSmall);
        if (_texResultBase.Id != 0) Raylib.UnloadTexture(_texResultBase);
        if (_texResultBar.Id != 0) Raylib.UnloadTexture(_texResultBar);
        _texNumber = default;
        _texLong = default;
        _texBaseLarge = default;
        _texBaseSmall = default;
        _texResultBase = default;
        _texResultBar = default;
        _entries.Clear();
        _loaded = false;
    }

    /// <summary>MiniTaiko.SetDanConditionsと同じタイミングで呼ぶ。conditionsを解析し全体/個別を判定する</summary>
    public static void SetConditions(List<Exam.Condition> conditions, int songCount)
    {
        _entries.Clear();
        _songCount = Math.Max(songCount, 1);
        if (conditions == null) return;

        foreach (var cond in conditions)
        {
            _entries.Add(new GaugeEntry
            {
                Type = cond.Type,
                IsOverall = cond.IsOverall,
                ThresholdsPerSong = cond.ThresholdsPerSong,
            });
        }
    }

    private static readonly HashSet<Exam.ConditionType> LessThanTypes = new()
    {
        Exam.ConditionType.Good, Exam.ConditionType.Miss,
    };

    private static bool MeetsThreshold(Exam.ConditionType type, int value, int threshold)
    {
        return LessThanTypes.Contains(type) ? value < threshold : value >= threshold;
    }

    // ---- 演奏中の状態を都度計算してそのまま描画する(状態を貯めこまない、フレームごとの純粋計算) ----
    private struct LiveState
    {
        public int Value;          // 現在値
        public int DisplayNumber;  // 表示する数字
        public bool RedMet;
        public bool GoldMet;
        public bool Failed;        // 未満系で赤合格閾値を超えて即失格した場合
        public bool NumberGradient; // true=下段(グラデ)を使う
        public BarColor Color;
        public double FillPercent; // 0.0~1.0。バーの表示幅(右から左に削れていく/左から右に伸びる)
    }

    private static int GetCurrentValue(Exam.ConditionType type, bool overall)
    {
        switch (type)
        {
            case Exam.ConditionType.Great: return overall ? Enso.DanTotalGreat : Enso.Perfect;
            case Exam.ConditionType.Good: return overall ? Enso.DanTotalGood : Enso.Good;
            case Exam.ConditionType.Miss: return overall ? Enso.DanTotalMiss : Enso.Miss;
            case Exam.ConditionType.Roll: return overall ? Enso.DanTotalRoll : Enso.RollThisSong;
            case Exam.ConditionType.Hit: return overall ? Enso.DanTotalHit : Enso.HitCountThisSong;
            // 💡 Score/MaxComboはこのゲーム側の仕様上、段位道場モードでは常に3曲通しで持ち越されるため
            //    個別条件/全体条件の区別なく、常にEnso側のライブ値をそのまま使う。
            case Exam.ConditionType.Score: return Enso.Score;
            case Exam.ConditionType.MaxCombo: return Enso.MaxCombo;
            default: return 0;
        }
    }

    /// <summary>全体条件(3曲合計)か個別条件(今の1曲)かに応じて、残りノーツ数を正しく取得する</summary>
    private static int GetRemainingNotes(bool overall)
    {
        if (overall)
        {
            // 💡 全体条件の残りノーツ = (全3曲の総ノーツ数) - (今までの累計判定済みノーツ数)
            // Enso.DanTotalNotes には3曲合計が入っている。
            // 累計判定済み = DanTotalGreat + DanTotalGood + DanTotalBad + DanTotalMiss
            int judged = Enso.DanTotalGreat + Enso.DanTotalGood + Enso.DanTotalBad + Enso.DanTotalMiss;
            return Math.Max(0, Enso.DanTotalNotes - judged);
        }
        else
        {
            // 個別条件なら今の曲の残りだけ
            return Enso.RemainingSingleNoteCount;
        }
    }

    private static LiveState ComputeLiveState(GaugeEntry entry, Exam.Threshold th, double nowSec)
    {
        var s = new LiveState();
        int value = GetCurrentValue(entry.Type, entry.IsOverall);
        s.Value = value;
        s.RedMet = MeetsThreshold(entry.Type, value, th.Red);
        s.GoldMet = MeetsThreshold(entry.Type, value, th.Gold);

        bool isLessThan = LessThanTypes.Contains(entry.Type);

        if (isLessThan)
        {
            // 💡 曲(個別条件)/段位全体(全体条件)が切り替わった最初のフレームで実時間を記録する。
            //    可/不可が1個も出ていない(value==0)間は、そこからStartGraceSeconds秒のあいだ
            //    完全に静止させて待機する(色・rainbow・終盤点滅も含めて一切動かさない)。
            //    1個でも出た瞬間はvalue!=0になるのでその場で解除され、通常通りアニメーションする。
            int graceKey = entry.IsOverall ? 0 : Enso.DanSongIndex;
            if (entry.GraceSongKey != graceKey)
            {
                entry.GraceSongKey = graceKey;
                entry.GraceStartTime = nowSec;
            }
            bool inStartGrace = value == 0 && entry.GraceStartTime >= 0.0 && (nowSec - entry.GraceStartTime) < StartGraceSeconds;

            int remaining = th.Red - value; // 残り許容数(これがそのまま表示数字)
            s.DisplayNumber = remaining;
            s.Failed = remaining <= 0; // 赤合格閾値を超えたら即失格(可/不可は減る一方のため)

            double percent = th.Red > 0 ? Math.Clamp(remaining / (double)th.Red, 0.0, 1.0) : (remaining > 0 ? 1.0 : 0.0);
            s.FillPercent = percent;
            int goldRemaining = th.Gold - value;
            bool goldAchievable = goldRemaining > 0;

            // 💡 songEnded の判定: 全体条件なら「段位最終曲が終了したか」、個別条件なら「今の曲が終了したか」
            bool isLastSong = Enso.DanSongIndex >= _songCount - 1;
            bool songEnded = !Enso.IsPlaying || Enso.SongProgress01 >= 1.0;
            bool relevantSongEnded = entry.IsOverall ? (isLastSong && songEnded) : songEnded;

            s.NumberGradient = relevantSongEnded && goldAchievable && remaining > 0;

            if (inStartGrace)
            {
                // 💡 可/不可がまだ1つも出ていない=グレース中は完全に静止させ、色も進行度も一切見ない
                //    (rainbow化や終盤点滅も含めて、待機色のまま3秒間じっと動かさない)。
                s.NumberGradient = false;
                s.Color = goldAchievable ? BarColor.Pink : BarColor.Yellow;
            }
            else if (s.Failed)
            {
                s.Color = BarColor.Gray;
            }
            else if (relevantSongEnded && goldAchievable)
            {
                // 💡 未満系は「曲(該当曲/段位全体)が終わって、かつ金合格まだ範囲内」で初めてレインボー。
                //    ライブ中はGoldMet=trueなだけでは虹にしない(可/不可は0の時点で常にGoldMet=trueになってしまうため)。
                s.Color = BarColor.Rainbow;
            }
            else
            {
                // 💡 全体条件は3曲通しての判定のため、「終盤」「終了間際」の点滅も段位最終曲の進行度でしか
                //    判定してはいけない。ここをEnso.SongProgress01(今の曲の進行度)だけで見ていたため、
                //    1曲目終了間際でも全体条件が(本来はまだ2曲残っているのに)点滅してしまっていた。
                double progressForNearEnd = entry.IsOverall
                    ? (isLastSong ? Enso.SongProgress01 : 0.0)
                    : Enso.SongProgress01;
                bool nearEnd = progressForNearEnd >= NearEndProgress && goldAchievable;
                bool veryNearEnd = progressForNearEnd >= FinalProgress && goldAchievable;

                bool isFull = remaining >= th.Red; // 💡 満タン(まだ可/不可を1つも使っていない状態)
                bool hardBlink = percent <= 0.20 || (!isFull && remaining <= 4);

                BarColor baseColor;
                if (hardBlink) baseColor = BarColor.RedBlink;
                else if (percent <= 0.30) baseColor = BarColor.Red;
                else baseColor = goldAchievable ? BarColor.Pink : BarColor.Yellow;

                // 楽曲終盤は「その時点の色」のまま点滅させる(すでに点滅色ならそのまま、そうでなければ点滅版へ)
                if (!hardBlink && percent > 0.30 && (nearEnd || veryNearEnd))
                {
                    baseColor = goldAchievable ? BarColor.PinkBlinkSlow : baseColor;
                    if (veryNearEnd && goldAchievable) baseColor = BarColor.PinkBlinkFast;
                }

                s.Color = baseColor;
            }
        }
        else
        {
            s.DisplayNumber = value;
            s.Failed = false; // 以上系は曲が終わるまで失格確定しない(このゲージでは常に「今のところ」の色を出すだけ)
            s.NumberGradient = s.GoldMet;

            if (s.GoldMet)
            {
                s.Color = BarColor.Rainbow;
                s.FillPercent = 1.0;
            }
            else if (s.RedMet)
            {
                double span = th.Gold - th.Red;
                double progress = span > 0 ? Math.Clamp((value - th.Red) / span, 0.0, 1.0) : 0.0;
                if (progress >= 2.0 / 3.0) s.Color = BarColor.PinkBlinkFast;
                else if (progress >= 1.0 / 3.0) s.Color = BarColor.PinkBlinkSlow;
                else s.Color = BarColor.Pink;
                s.FillPercent = 1.0; // 赤合格達成後はバーは満タン(桃色~虹)扱い
            }
            else
            {
                double percent = th.Red > 0 ? Math.Clamp(value / (double)th.Red, 0.0, 1.0) : 0.0;
                s.FillPercent = percent;

                bool danger = false;
                switch (entry.Type)
                {
                    case Exam.ConditionType.MaxCombo:
                        {
                            // 💡 曲頭でまだ1音も判定していない状態は「まだ危険かどうか判定不能」なので点滅させない。
                            bool anyJudged = entry.IsOverall
                                ? (value > 0 || Enso.DanTotalMiss > 0 || Enso.DanTotalBad > 0)
                                : (value > 0 || Enso.Miss > 0 || Enso.Bad > 0);

                            int target = s.RedMet ? th.Gold : th.Red;
                            int remainingNotes = GetRemainingNotes(entry.IsOverall);
                            danger = anyJudged && remainingNotes <= target + ComboDangerMargin;
                            break;
                        }
                    case Exam.ConditionType.Great:
                        {
                            // 💡 曲頭(value=0かつMiss/Bad=0)は判定不能なので点滅させない。
                            bool anyJudged = entry.IsOverall
                                ? (value > 0 || Enso.DanTotalGood > 0 || Enso.DanTotalBad > 0 || Enso.DanTotalMiss > 0)
                                : (value > 0 || Enso.Good > 0 || Enso.Bad > 0 || Enso.Miss > 0);

                            int target = s.RedMet ? th.Gold : th.Red;
                            int needed = Math.Max(0, target - value);
                            int remainingNotes = GetRemainingNotes(entry.IsOverall);

                            // 💡 修正: 「残りノーツを全て良で叩いても目標に届かない」場合は失格(Gray)へ
                            if (remainingNotes < needed)
                            {
                                s.Failed = true;
                            }
                            else
                            {
                                // 💡 許容される「可/不可」の数 (残りノーツ - 必要数)
                                int allowedNonGreat = remainingNotes - needed;

                                // 💡 「残りの演奏でミス(可/不可)が許される割合」が2%未満になったら点滅。
                                danger = anyJudged && remainingNotes > 0 && allowedNonGreat < remainingNotes * GreatDangerRatio;
                            }
                            break;
                        }
                    case Exam.ConditionType.Roll:
                        {
                            int target = s.RedMet ? th.Gold : th.Red;
                            int needed = Math.Max(0, target - value);
                            bool hasFutureSongs = entry.IsOverall && (Enso.DanSongIndex < _songCount - 1);

                            // 💡 修正: 現在アクティブな連打がある場合は、危険判定を保留して点滅させない
                            // AC実機では連打中に「残り秒数不足」で点滅しない挙動を再現するため
                            if (Enso.ActiveRollRemainingSeconds > 0)
                            {
                                entry.RollDangerLatched = false; // 連打中はラッチを強制解除
                                danger = false;
                                break;
                            }

                            double achievable = Enso.RemainingRollSeconds * RollHitsPerSecond;

                            if (needed <= 0)
                            {
                                entry.RollDangerLatched = false;
                            }
                            else if (needed > achievable && !hasFutureSongs)
                            {
                                entry.RollDangerLatched = true;
                            }
                            danger = entry.RollDangerLatched;
                            break;
                        }

                    case Exam.ConditionType.Hit:
                        {
                            int target = s.RedMet ? th.Gold : th.Red;
                            int needed = Math.Max(0, target - value);
                            int remainingNotes = GetRemainingNotes(entry.IsOverall);

                            // 💡 修正: 全体条件の場合、まだ演奏していない「先の曲」がある間は、
                            //    今の曲の連打・ノーツだけで足りなくても点滅(danger)や失格(Failed)にしない。
                            bool hasFutureSongs = entry.IsOverall && (Enso.DanSongIndex < _songCount - 1);

                            // 💡 全体条件の場合は「今プレイしている曲以降の全連打」を考慮する必要があるが、
                            //    Enso.RemainingRollSeconds は現在の曲の残りしか見ない。
                            double achievable = remainingNotes + Enso.RemainingRollSeconds * RollHitsPerSecond;

                            danger = !hasFutureSongs && (needed > achievable);

                            if (!hasFutureSongs && Enso.RemainingRollSeconds <= 0 && needed > remainingNotes)
                                s.Failed = true;
                            break;
                        }
                }

                if (s.Failed)
                {
                    s.Color = BarColor.Gray;
                }
                else if (danger)
                {
                    s.Color = BarColor.RedBlink;
                }
                else
                {
                    s.Color = percent < 0.5 ? BarColor.DarkYellow : BarColor.Yellow;
                }
            }
        }

        int visibleNumber = Math.Max(0, s.DisplayNumber);
        if (entry.LastDisplayNumber == int.MinValue)
            entry.LastDisplayNumber = visibleNumber;
        else if (entry.LastDisplayNumber != visibleNumber)
        {
            entry.LastDisplayNumber = visibleNumber;
            entry.NumberBounceStart = nowSec;
        }

        if (entry.HasLastColor && entry.LastColor != s.Color)
        {
            bool darkYellowToYellow = entry.LastColor == BarColor.DarkYellow && s.Color == BarColor.Yellow;
            bool yellowToPink = entry.LastColor == BarColor.Yellow &&
                (s.Color == BarColor.Pink || s.Color == BarColor.PinkBlinkSlow || s.Color == BarColor.PinkBlinkFast);

            if (darkYellowToYellow || yellowToPink)
                entry.ColorFlashStart = nowSec;
            else
                entry.ColorFlashStart = -1.0;
        }

        entry.LastColor = s.Color;
        entry.HasLastColor = true;
        return s;
    }

    /// <summary>Enso.Update()から段位道場モード中だけ呼ばれる。今フレームは特に状態保持なし(描画時にその都度計算)</summary>
    public static void Update(double nowSec)
    {
        // 💡 現状は「状態を持たない・毎フレームDraw側で計算」方式にしているため、ここでは何もしていない。
        //    将来的に点滅の立ち上がりタイミング等を保持したくなった場合はここで積算する。
    }

    /// <summary>
    /// 💡 リザルト画面用: 演奏終了後の最終値(achievedValue)から、点滅等のライブ判定なしで
    /// 「達成率(0~1)」「バーの色」「表示する数字」「数字をグラデーションにするか」を計算する。
    /// ComputeLiveStateの終了後版(danger/blink無し)。
    /// </summary>
    public static (double percent, BarColor color, int displayNumber, bool numberGradient) ComputeFinalBarState(
        Exam.ConditionType type, int achievedValue, Exam.Threshold th)
    {
        bool isLessThan = LessThanTypes.Contains(type);

        if (isLessThan)
        {
            int remaining = th.Red - achievedValue;
            bool failed = remaining <= 0;
            // 💡 リザルトのバーもライブ中と同じく100%(満タン)から可/不可が出るごとに減っていく向き。
            double percent = th.Red > 0
                ? Math.Clamp(remaining / (double)th.Red, 0.0, 1.0)
                : (remaining > 0 ? 1.0 : 0.0);
            bool goldAchievable = (th.Gold - achievedValue) > 0;

            BarColor color = failed ? BarColor.Gray : (goldAchievable ? BarColor.Rainbow : BarColor.Yellow);
            bool numberGradient = !failed && goldAchievable;
            int displayNumber = Math.Max(0, remaining);
            return (percent, color, displayNumber, numberGradient);
        }
        else
        {
            bool goldMet = achievedValue >= th.Gold;
            bool redMet = achievedValue >= th.Red;

            if (goldMet) return (1.0, BarColor.Rainbow, achievedValue, true);
            if (redMet) return (1.0, BarColor.Pink, achievedValue, false);

            double percent = th.Red > 0 ? Math.Clamp(achievedValue / (double)th.Red, 0.0, 1.0) : 0.0;
            return (percent, BarColor.Yellow, achievedValue, false);
        }
    }

    /// <summary>
    /// 💡 リザルト画面専用: _entries(演奏中の状態)を経由せず、1本ぶんのバーを描画する。
    /// isOverall=true(全体条件) なら演奏中と同じ Large_Gauge_Base.png+Long.png をそのまま流用し、
    /// isOverall=false(個別条件) ならリザルト専用の Small_base.png+Small.png を使う。
    /// columnXは同じ条件内で横に並ぶバー(個別条件は曲数ぶん)のオフセット、
    /// rowYは条件が変わるごとに改行して積む縦方向のオフセット。どちらも1920x1080基準の未スケール値。
    /// </summary>
    public static void DrawResultRow(float columnX, float rowY, bool isOverall, double percent, BarColor color, int displayNumber, bool numberGradient,
        float vx, float vy, float s, double animT)
    {
        if (!_loaded) return;

        float x = ResultBarX * s * ResultBarScale + vx + columnX * s;
        float y = ResultBarY * s + vy + rowY * s;

        if (isOverall)
        {
            float barWidth = BarWidthOverall * s;
            DrawBase(_texBaseLarge, x + BaseOffsetXOverall * s, y + BaseOffsetYOverall * s, s, BaseScale);
            DrawBar(_texLong, _longFrameH, color, percent, x + BarOffsetXOverall * s, y + BarOffsetYOverall * s, barWidth, BarHeight * s, animT);
            DrawNumber(displayNumber, numberGradient, x + NumberOffsetXOverall * s, y + NumberOffsetYOverall * s, s, NumberScaleOverall, NumberDigitGap);
        }
        else
        {
            float barWidth = ResultBarWidth * s;
            DrawBase(_texResultBase, x + ResultBaseOffsetX * s, y + ResultBaseOffsetY * s, s, ResultBaseScale);
            DrawBar(_texResultBar, _resultBarFrameH, color, percent, x + ResultBarOffsetX * s, y + ResultBarOffsetY * s, barWidth, ResultBarHeight * s, animT);
            DrawNumber(displayNumber, numberGradient, x + ResultNumberOffsetX * s, y + ResultNumberOffsetY * s, s, ResultNumberScale, ResultNumberDigitGap);
        }
    }

    public static void Draw(float vx, float vy, float s)
    {
        if (!_loaded || !Enso.DanMode || _entries.Count == 0) return;
        if (_texNumber.Id == 0 && _texLong.Id == 0) return;

        // 💡 Enso側のnowSecは非公開のため、点滅アニメ・開始グレース(StartGraceSeconds)判定の両方に
        //    Raylib.GetTime()を実時間として使う。
        double animT = Raylib.GetTime();
        double nowSec = animT;

        float x = BarX * s * BarScale + vx;
        float y = BarY * s + vy;
        float lineHeight = BarLineHeight * s;

        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];

            // 💡 個別条件は現在演奏中の曲(Enso.DanSongIndex)の閾値だけを見る(MiniTaikoのvisibleEntriesと同じ考え方)
            int songIdx = entry.IsOverall ? 0 : Math.Clamp(Enso.DanSongIndex, 0, entry.ThresholdsPerSong.Length - 1);
            if (songIdx < 0 || songIdx >= entry.ThresholdsPerSong.Length) continue;
            Exam.Threshold th = entry.ThresholdsPerSong[songIdx];

            var state = ComputeLiveState(entry, th, nowSec);

            float rowY = y + i * lineHeight;

            // 💡 全体条件はLarge_Gauge_Base.png、個別条件はSmall_Gauge_Base.pngを土台に敷く。
            //    ゲージ(Long.png)の長さも土台に合わせて条件の種類ごとに変える。
            Texture2D baseTex = entry.IsOverall ? _texBaseLarge : _texBaseSmall;
            float barWidth = (entry.IsOverall ? BarWidthOverall : BarWidthIndividual) * s;

            DrawBase(baseTex, x + BaseOffsetX * s, rowY + BaseOffsetY * s, s, BaseScale);
            DrawBar(_texLong, _longFrameH, state.Color, state.FillPercent, x + BarOffsetX * s, rowY + BarOffsetY * s, barWidth, BarHeight * s, animT);
            float colorFlashAlpha = GetColorFlashAlpha(entry.ColorFlashStart, nowSec);
            if (colorFlashAlpha > 0f)
            {
                DrawRow(_texLong, _longFrameH, RowWhite, state.FillPercent,
                    x + BarOffsetX * s, rowY + BarOffsetY * s, barWidth, BarHeight * s,
                    new Color((byte)255, (byte)255, (byte)255, (byte)(colorFlashAlpha * 255f)));
            }
            float numberPopScale = GetNumberBounceScale(entry.NumberBounceStart, nowSec);
            float numberY = rowY + NumberOffsetY * s
                - (_numberDigitH * NumberScale * s * (numberPopScale - 1f));
            DrawNumber(state.DisplayNumber, state.NumberGradient,
                x + NumberOffsetX * s, numberY, s, NumberScale, NumberDigitGap,
                NumberScale * numberPopScale);
        }
    }

    private static float GetColorFlashAlpha(double startSec, double nowSec)
    {
        if (startSec < 0.0) return 0f;

        double progress = nowSec - startSec;
        if (progress <= 0.0 || progress >= 1.0) return 0f;

        return (float)(progress < 0.5 ? progress * 2.0 : (1.0 - progress) * 2.0);
    }

    private static float GetNumberBounceScale(double startSec, double nowSec)
    {
        if (startSec < 0.0) return 1f;

        double frame = (nowSec - startSec) * NumberBounceFps;
        if (frame <= 0.0) return NumberBounceScaleFrames[0];

        int last = NumberBounceScaleFrames.Length - 1;
        if (frame >= last) return NumberBounceScaleFrames[last];

        int firstFrame = (int)frame;
        float t = (float)(frame - firstFrame);
        return NumberBounceScaleFrames[firstFrame]
            + (NumberBounceScaleFrames[firstFrame + 1] - NumberBounceScaleFrames[firstFrame]) * t;
    }

    private static void DrawBase(Texture2D tex, float x, float y, float s, float baseScale)
    {
        if (tex.Id == 0) return;
        float scale = s * baseScale;
        Rectangle src = new Rectangle(0, 0, tex.Width, tex.Height);
        Rectangle dst = new Rectangle(x, y, tex.Width * scale, tex.Height * scale);
        Raylib.DrawTexturePro(tex, src, dst, Vector2.Zero, 0f, Color.White);
    }

    /// <summary>
    /// 💡 tex/frameHを引数化し、演奏中バー(Long.png)とリザルト専用バー(Small.png)の両方から
    /// 同じ色分け・点滅ロジックを共有できるようにしてある。
    /// </summary>
    private static void DrawBar(Texture2D tex, int frameH, BarColor color, double percent, float x, float y, float w, float h, double animT)
    {
        if (tex.Id == 0 || frameH <= 0) return;
        percent = Math.Clamp(percent, 0.0, 1.0);
        if (percent <= 0.0) return; // 削り切ったら何も描かない

        switch (color)
        {
            case BarColor.Gray: DrawRow(tex, frameH, RowWhite, percent, x, y, w, h, COL_GRAY); break;
            case BarColor.DarkYellow: DrawRow(tex, frameH, RowDarkYellow, percent, x, y, w, h, Color.White); break;
            case BarColor.Yellow: DrawRow(tex, frameH, RowYellow, percent, x, y, w, h, Color.White); break;
            case BarColor.Red: DrawRow(tex, frameH, RowRed, percent, x, y, w, h, Color.White); break;
            case BarColor.Pink: DrawRow(tex, frameH, RowPink, percent, x, y, w, h, Color.White); break;
            // 💡 点滅はsin波。赤フレーム自体の明るさを0%→-100%で暗くしつつ、
            //    同じ位相で白フレームを透明度0%→100%で重ねる(暗くなった分を白がフェードインして覆う)。
            case BarColor.RedBlink: DrawBlink(tex, frameH, RowRed, percent, x, y, w, h, BlinkFastHz, animT, darken: true); break;
            case BarColor.PinkBlinkSlow: DrawBlink(tex, frameH, RowPink, percent, x, y, w, h, BlinkSlowHz, animT, darken: false); break;
            case BarColor.PinkBlinkFast: DrawBlink(tex, frameH, RowPink, percent, x, y, w, h, BlinkFastHz, animT, darken: false); break;
            case BarColor.Rainbow:
                {
                    int frame = (int)(animT * RainbowFps) % Math.Max(1, RainbowFrameCount);
                    DrawRow(tex, frameH, RowRainbowStart + frame, percent, x, y, w, h, Color.White);
                    break;
                }
            default: DrawRow(tex, frameH, RowWhite, percent, x, y, w, h, Color.White); break;
        }
    }

    /// <summary>
    /// バーを左端を基準にpercent(0~1)ぶんの幅だけ描く。左端は固定のまま、右端がpercentに応じて
    /// 左へ縮む(=右から左にどんどん削られていくように見える)。src側も同じ比率でクロップし、
    /// 引き伸ばし(ストレッチ)にならないようにしている。
    /// </summary>
    private static void DrawRow(Texture2D tex, int frameH, int row, double percent, float x, float y, float w, float h, Color tint)
    {
        float srcFullW = tex.Width;
        float srcW = srcFullW * (float)percent;
        float dstW = w * (float)percent;

        Rectangle src = new Rectangle(0, row * frameH, srcW, frameH);
        Rectangle dst = new Rectangle(x, y, dstW, h);
        Raylib.DrawTexturePro(tex, src, dst, Vector2.Zero, 0f, tint);
    }

    /// <summary>
    /// sin波点滅。
    /// ・darken=true(赤点滅): colorRowの透明度(アルファ)を100%→0%で上下させるだけ。以前は明るさ(RGB)を
    ///   暗くしていたが、それだと黒っぽくなるだけなので、透明度を下げてチカチカ透ける点滅にする。
    /// ・darken=false(桃点滅): colorRowを等倍の明るさで描いた上に、白フレームを透明度0%→100%で
    ///   同じ位相で重ねる(従来通り)。
    /// </summary>
    private static void DrawBlink(Texture2D tex, int frameH, int colorRow, double percent, float x, float y, float w, float h, double hz, double animT, bool darken)
    {
        double phase = Math.Sin(animT * hz * Math.PI * 2.0) * 0.5 + 0.5; // 0~1

        if (darken)
        {
            byte alpha = (byte)Math.Clamp(phase * 255.0, 0.0, 255.0);
            DrawRow(tex, frameH, colorRow, percent, x, y, w, h, new Color((byte)255, (byte)255, (byte)255, alpha));
            return;
        }

        DrawRow(tex, frameH, colorRow, percent, x, y, w, h, Color.White);

        byte whiteAlpha = (byte)Math.Clamp(phase * 255.0, 0.0, 255.0);
        if (whiteAlpha > 0)
            DrawRow(tex, frameH, RowWhite, percent, x, y, w, h, new Color((byte)255, (byte)255, (byte)255, whiteAlpha));
    }

    private static void DrawNumber(int value, bool gradient, float x, float y, float s, float numberScale, float digitGap, float numberScaleY = -1f)
    {
        if (_texNumber.Id == 0 || _numberDigitW <= 0 || _numberDigitH <= 0) return;

        // マイナス値(未満条件で超過してしまった場合)は0として扱う(表示上は"0"止まり、失格色はバー側で分かる)
        string str = Math.Max(0, value).ToString();
        int row = gradient ? 1 : 0;

        float digitW = _numberDigitW * numberScale * s;
        float digitH = _numberDigitH * (numberScaleY > 0f ? numberScaleY : numberScale) * s;
        float gap = digitGap * s;

        float penX = x;

        for (int i = 0; i < str.Length; i++)
        {
            int d = str[i] - '0';
            if (d < 0 || d > 9) continue;

            Rectangle src = new Rectangle(d * _numberDigitW, row * _numberDigitH, _numberDigitW, _numberDigitH);
            Rectangle dst = new Rectangle(penX, y, digitW, digitH);
            Raylib.DrawTexturePro(_texNumber, src, dst, Vector2.Zero, 0f, Color.White);

            penX += digitW + gap;
        }
    }
}
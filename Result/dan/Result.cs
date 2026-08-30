using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Numerics;
using NAudio.Wave;
using NAudio.Vorbis; // 💡 .ogg再生用(NAudio単体ではoggを直接読めないためNAudio.Vorbisを併用)

public static class DanResultScene
{
    // ==================================================================
    // 💡 Program.cs Enter()経由で受け取る表示用データ
    // ==================================================================
    private static string _danTitle = " ";
    private static List<string> _songTitles = new();
    private static List<Exam.SongResult> _songResults = new();
    private static List<Exam.Condition> _conditions = new();
    private static bool _gaugeClear;
    private static int _gaugePercent;
    private static Exam.ConditionGauge _gaugeThreshold;
    private static List<Exam.PassRank> _songRanks = new();
    private static Exam.PassRank _overallRank;
    private static bool _confirmed;
    public static bool Confirmed => _confirmed;
    private static bool _showingResultBg;
    private static Texture2D _selectedResultBgTexture;
    private static Texture2D _selectedResultHikariTexture;
    private static double _resultBgShownAtSec;
    private static float _inputGuardTimer;
    private const float INPUT_GUARD_SEC = 0.3f;

    // ==================================================================
    // 💡 背景/土台プレート画像
    // ==================================================================
    private static Texture2D _bgTexture;
    private static Texture2D _plateTexture;
    private static Texture2D _totalPlateTexture;
    private static Texture2D _songTexture;
    private static Texture2D _scorePanelTexture;
    private static Texture2D _numberTexture;
    private static Texture2D _gaugeBackTexture;
    private static Texture2D _texDanConditionBar;
    private static bool _texturesLoaded;

    // 💡 最終アニメーション終了後、Don1/Don2で戻る前に一度挟む結果背景(フルコンボ/合格/全良)
    private static Texture2D _kinBackTexture;
    private static Texture2D _shoudanKinHikariTexture;
    private static Texture2D _ginBackTexture;
    private static Texture2D _njiBackTexture;
    private static Texture2D _shoudanGinHikariTexture;

    // 💡 「(ランク)+ 合格」を組み立てて表示するための文字スプライトシート
    //    GinMozi/GoldMozi: 段人合格 / 玄名超達 (4列2行)
    //    GinMozi2/GoldMozi2: 一三五七九初 / 二四六八十級 (6列2行)
    private static Texture2D _ginMoziTexture;
    private static Texture2D _ginMozi2Texture;
    private static Texture2D _goldMoziTexture;
    private static Texture2D _goldMozi2Texture;

    private const string BG_PATH = "Lumen/5.Dan/DanResult/Bg.png";
    private const string PLATE_PATH = "Lumen/5.Dan/DanResult/plate.png";
    private const string TOTAL_PLATE_PATH = "Lumen/5.Dan/DanResult/Total_plate.png";
    private const string SONG_PATH = "Lumen/5.Dan/DanResult/Song.png";
    private const string SCORE_PANEL_PATH = "Lumen/5.Dan/DanResult/ScorePanel.png";
    private const string NUMBER_PATH = "Lumen/5.Dan/DanResult/Number.png";
    private const string GAUGE_BACK_PATH = "Lumen/5.Dan/DanResult/gauge_back.png";
    private const string DAN_CONDITION_BAR_PATH = "Lumen/5.Dan/DanEnso/Base.png";

    private const string KIN_BACK_PATH = "Lumen/5.Dan/DanSyoudan/KinBack.png";
    private const string SHOUDAN_KIN_HIKARI_PATH = "Lumen/5.Dan/DanSyoudan/ShoudanKinHikari.png";
    private const string GIN_BACK_PATH = "Lumen/5.Dan/DanSyoudan/GinBack.png";
    private const string NJI_BACK_PATH = "Lumen/5.Dan/DanSyoudan/NjiBack.png";
    private const string SHOUDAN_GIN_HIKARI_PATH = "Lumen/5.Dan/DanSyoudan/ShoudanGinHikari.png";

    private const string GIN_MOZI_PATH = "Lumen/5.Dan/DanSyoudan/GinMozi.png";
    private const string GIN_MOZI2_PATH = "Lumen/5.Dan/DanSyoudan/GinMozi2.png";
    private const string GOLD_MOZI_PATH = "Lumen/5.Dan/DanSyoudan/GoldMozi.png";
    private const string GOLD_MOZI2_PATH = "Lumen/5.Dan/DanSyoudan/GoldMozi2.png";

    private static bool _totalPlateShown;
    private static double _totalPlateShownAtSec;

    // ==================================================================
    // 💡 音声関連(BGM/SE)
    // ==================================================================
    private const string BGM_INTRO_PATH = "Lumen/2.sound/SEDanResult/Intro.ogg";       // 💡 結果画面開始時に1回だけ流すイントロ
    private const string BGM_LOOP_PATH = "Lumen/2.sound/BGM/DanResult.ogg";             // 💡 イントロ終了後にループ再生するBGM
    private const string SE_GAUGE_PATH = "Lumen/2.sound/SEDanResult/Gauge.ogg";         // 💡 魂ゲージが上がる音(percentに応じて途中で止める)
    private const string SE_PANEL_IN_PATH = "Lumen/2.sound/SEDanResult/PanelIn.ogg";    // 💡 曲パネルが出てくる音
    private const string SE_CONDITION_COUNT_UP_PATH = "Lumen/2.sound/SEDanResult/ConditionGaugeCountUp.ogg"; // 💡 条件バーが上下する音

    // 💡 BGM(イントロ→ループ)再生用
    private static WaveOutEvent _bgmOutput;
    private static VorbisWaveReader _bgmReader;
    private static LoopStream _bgmLoopStream;

    // 💡 魂ゲージが上がる音(SE_GAUGE_PATH)専用の再生管理。percentに応じて途中で止めるため専用フィールドを持つ。
    private static WaveOutEvent _gaugeSfxOutput;
    private static VorbisWaveReader _gaugeSfxReader;
    private static double _gaugeSfxStopAtSec;
    private static bool _gaugeSfxStopped = true;

    // 💡 条件バーごとに「上下する音」を1回だけ鳴らすためのフラグ・開始時刻(_finalConditionBarsと同じ並び)
    private static double[] _conditionBarSoundStartSec;
    private static bool[] _conditionBarSoundPlayed;

    private sealed class ResultBarGroup
    {
        public bool IsOverall;
        public Exam.ConditionType Type;
        public bool IsGauge;
        public List<(double finalPercent, DanGaugeMeter.BarColor finalColor, int finalDisplayNumber, bool finalGradient, int thresholdRed)> Bars = new();
    }
    private static List<ResultBarGroup> _finalConditionBars = new();

    private const double CONDITION_BAR_STAGGER_SEC = 1.5;
    private const double CONDITION_BAR_REVEAL_SEC = 1.0;

    // 💡 魂ゲージ自体の最大アニメーション時間(Gauge.cs側の仕様値。100%で3秒かけて満タンになる)。
    //    Gauge.ogg(ゲージが上がる音)を percent に応じた時間で止めるための基準として使う。
    private const double GAUGE_MAX_ANIM_SEC = 3.0;
    private const float DAN_CONDITION_BAR_OFFSET_X = -150f;
    private const float DAN_CONDITION_BAR_OFFSET_Y = 0f;
    private const float DAN_CONDITION_BAR_SCALE = 0.925f;

    private const int NUMBER_CELL_W = 39;
    private const int NUMBER_CELL_H = 50;
    private const float NUMBER_KERNING = 0.65f;


    private const float PLATE_OFFSET_X = 150f;
    private const float PLATE_OFFSET_Y = 0f;

    // 💡 ゲージ背面画像(gauge_back.png)の、ゲージ本体からの相対オフセット
    private const float GAUGE_BACK_OFFSET_X = 725f;
    private const float GAUGE_BACK_OFFSET_Y = 225f;
    private const float GAUGE_BACK_SCALE = 1.0f;

    // 💡 ScorePanel.pngの表示位置・サイズ調整用(Total_plate表示時)
    private const float TOTAL_SCORE_PANEL_OFFSET_X = 150f;
    private const float TOTAL_SCORE_PANEL_OFFSET_Y = -300f;
    private const float TOTAL_SCORE_PANEL_SCALE = 1.0f;

    // ==================================================================
    // 💡 【重要】位置・サイズ調整用オフセット（完全に分離）
    // ==================================================================

    // 💡 1. ゲージ単体(Gauge本体 + gauge_back.png)の位置・サイズ調整用
    //    ここを変更すると、ゲージだけが動き、条件バーは元の位置のままです。
    private const float GAUGE_OFFSET_X = -200f;
    private const float GAUGE_OFFSET_Y = 175f;
    private const float GAUGE_SCALE = 1.0f; // 1.0で標準サイズ、値を変更して拡大縮小

    // 💡 2. 条件バー単体(Base.png, Small_base, Small, 数字, ラベル等)の位置調整用
    //    ここを変更すると、条件バーだけが動き、ゲージは元の位置のままです。
    private const float CONDITION_BARS_OFFSET_X = 0f;
    private const float CONDITION_BARS_OFFSET_Y = -125f;

    // ==================================================================
    // 💡 Song.png内のレイアウト定義
    // ==================================================================
    private const int SONG_PANEL_X = 380;
    private const int SONG_ROW0_Y = 148;
    private const int SONG_ROW_WIDTH = 1440;
    private const int SONG_ROW_HEIGHT = 256;
    private const int SONG_ROW_STRIDE = 276;

    private const float SONG_TITLE_REL_X = 250f;
    private const float SONG_TITLE_REL_Y = 87f;
    private const float SONG_STAT_REL_Y = 187f;
    private const float SONG_GOOD_REL_X = 190f;
    private const float SONG_OK_REL_X = 520f;
    private const float SONG_MISS_REL_X = 870f;
    private const float SONG_ROLL_REL_X = 1225f;
    private const float SONG_STAT_DIGIT_HEIGHT = 50f;

    private static float _revealTimer;
    private static int _revealCount;
    private const float REVEAL_INTERVAL_SEC = 0.5f;

    private static List<float> _slideTimers = new();
    private const float SLIDE_DURATION_SEC = 0.25f;
    private const float SLIDE_DISTANCE = 700f;
    private const float ROW_GAP = 20f;
    private const float SONG_OFFSET_X = 140f;
    private const float SONG_OFFSET_Y = 0f;
    private const float SONG_TITLE_FONT_SIZE = 40f;

    private static void EnsureTexturesLoaded()
    {
        if (_texturesLoaded) return;
        _bgTexture = Raylib.LoadTexture(BG_PATH);
        _plateTexture = Raylib.LoadTexture(PLATE_PATH);
        _totalPlateTexture = Raylib.LoadTexture(TOTAL_PLATE_PATH);
        _songTexture = Raylib.LoadTexture(SONG_PATH);
        _scorePanelTexture = Raylib.LoadTexture(SCORE_PANEL_PATH);
        if (_scorePanelTexture.Id == 0)
            Console.WriteLine("[DanResultScene] ScorePanel.png の読み込みに失敗しました: " + SCORE_PANEL_PATH);
        _numberTexture = Raylib.LoadTexture(NUMBER_PATH);
        _gaugeBackTexture = Raylib.LoadTexture(GAUGE_BACK_PATH);
        _texDanConditionBar = Raylib.LoadTexture(DAN_CONDITION_BAR_PATH);
        if (_texDanConditionBar.Id == 0)
            Console.WriteLine("[DanResultScene] Base.png の読み込みに失敗しました: " + DAN_CONDITION_BAR_PATH);
        _kinBackTexture = Raylib.LoadTexture(KIN_BACK_PATH);
        _shoudanKinHikariTexture = Raylib.LoadTexture(SHOUDAN_KIN_HIKARI_PATH);
        _ginBackTexture = Raylib.LoadTexture(GIN_BACK_PATH);
        _njiBackTexture = Raylib.LoadTexture(NJI_BACK_PATH);
        _shoudanGinHikariTexture = Raylib.LoadTexture(SHOUDAN_GIN_HIKARI_PATH);
        // 💡 実ファイルの中身は Mozi.png=数字(6列2行) / Mozi2.png=漢字(4列2行) なので、
        //    "sheet1(漢字4列)"にはMozi2.png、"sheet2(数字6列)"にはMozi.pngを割り当てる
        _ginMoziTexture = Raylib.LoadTexture(GIN_MOZI2_PATH);
        _ginMozi2Texture = Raylib.LoadTexture(GIN_MOZI_PATH);
        _goldMoziTexture = Raylib.LoadTexture(GOLD_MOZI2_PATH);
        _goldMozi2Texture = Raylib.LoadTexture(GOLD_MOZI_PATH);
        DanGaugeMeter.Init();
        _texturesLoaded = true;
    }

    public static void Enter(
        string danTitle,
        List<string> songTitles,
        List<Exam.SongResult> songResults,
        List<Exam.Condition> conditions,
        bool gaugeClear,
        int gaugePercent,
        Exam.ConditionGauge gaugeThreshold = default)
    {
        EnsureTexturesLoaded();
        _danTitle = danTitle ?? "";
        _songTitles = songTitles != null ? new List<string>(songTitles) : new List<string>();
        _songResults = songResults != null ? new List<Exam.SongResult>(songResults) : new List<Exam.SongResult>();
        _conditions = conditions != null ? new List<Exam.Condition>(conditions) : new List<Exam.Condition>();
        _gaugeClear = gaugeClear;
        _gaugePercent = gaugePercent;
        _gaugeThreshold = gaugeThreshold;
        _songRanks = new List<Exam.PassRank>();
        for (int i = 0; i < _songResults.Count; i++)
        {
            _songRanks.Add(Exam.JudgeSong(_conditions, i, _songResults[i]));
        }
        Exam.PassRank examRank = Exam.JudgeExam(_conditions, _songResults);
        _overallRank = _gaugeClear ? examRank : Exam.PassRank.Fail;
        _confirmed = false;
        _showingResultBg = false;
        _totalPlateShown = false;
        _finalConditionBars = new List<ResultBarGroup>();
        _inputGuardTimer = 0f;
        _revealTimer = 0f;
        _revealCount = 0;
        _slideTimers = new List<float>();
        for (int i = 0; i < _songResults.Count; i++)
        {
            _slideTimers.Add(0f);
        }

        // 💡 音声状態をリセットしてBGM(Intro→ループ)を開始する
        StopBgm();
        StopGaugeSfx();
        _gaugeSfxStopped = true;
        _conditionBarSoundStartSec = null;
        _conditionBarSoundPlayed = null;
        PlayBgm();
    }

    /// <summary>
    /// 別の画面へ抜ける際に呼び、再生中の音声を止める。
    /// </summary>
    public static void ResetState()
    {
        StopBgm();
        StopGaugeSfx();
    }

    /// <summary>
    /// Lumen/2.sound/SEDanResult/Intro.oggを1回再生し、終わったらLumen/2.sound/BGM/DanResult.oggをループ再生する。
    /// </summary>
    private static void PlayBgm()
    {
        StopBgm();
        try
        {
            _bgmReader = new VorbisWaveReader(BGM_INTRO_PATH);
            _bgmOutput = new WaveOutEvent();
            _bgmOutput.Init(_bgmReader);
            _bgmOutput.PlaybackStopped += OnIntroFinished;
            _bgmOutput.Play();
        }
        catch (Exception e)
        {
            Console.WriteLine($"[DanResultScene] BGM(イントロ)再生失敗: {BGM_INTRO_PATH} ({e.Message})");
        }
    }

    /// <summary>
    /// イントロ(Intro.ogg)再生完了時に呼ばれ、ループBGM(DanResult.ogg)へ切り替える。
    /// </summary>
    private static void OnIntroFinished(object sender, StoppedEventArgs e)
    {
        if (sender is WaveOutEvent finishedOutput)
        {
            finishedOutput.PlaybackStopped -= OnIntroFinished;
        }
        _bgmOutput?.Dispose();
        _bgmOutput = null;
        _bgmReader?.Dispose();
        _bgmReader = null;

        try
        {
            _bgmReader = new VorbisWaveReader(BGM_LOOP_PATH);
            _bgmLoopStream = new LoopStream(_bgmReader);
            _bgmOutput = new WaveOutEvent();
            _bgmOutput.Init(_bgmLoopStream);
            _bgmOutput.Play();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DanResultScene] BGM(ループ)再生失敗: {BGM_LOOP_PATH} ({ex.Message})");
        }
    }

    /// <summary>
    /// 再生中のBGM(イントロ/ループどちらでも)を停止し、リソースを解放する。
    /// </summary>
    private static void StopBgm()
    {
        if (_bgmOutput != null)
        {
            _bgmOutput.PlaybackStopped -= OnIntroFinished;
            _bgmOutput.Stop();
            _bgmOutput.Dispose();
            _bgmOutput = null;
        }
        _bgmLoopStream?.Dispose();
        _bgmLoopStream = null;
        _bgmReader?.Dispose();
        _bgmReader = null;
    }

    /// <summary>
    /// 魂ゲージが上がる音(SE_GAUGE_PATH)の再生を開始する。stopAtSecに達したらUpdate()側で自動的に止める。
    /// </summary>
    private static void PlayGaugeSfx(double stopAtSec)
    {
        StopGaugeSfx();
        _gaugeSfxStopAtSec = stopAtSec;
        _gaugeSfxStopped = false;
        try
        {
            _gaugeSfxReader = new VorbisWaveReader(SE_GAUGE_PATH);
            _gaugeSfxOutput = new WaveOutEvent();
            _gaugeSfxOutput.Init(_gaugeSfxReader);
            _gaugeSfxOutput.Play();
        }
        catch (Exception e)
        {
            Console.WriteLine($"[DanResultScene] SE再生失敗: {SE_GAUGE_PATH} ({e.Message})");
            _gaugeSfxStopped = true;
        }
    }

    private static void StopGaugeSfx()
    {
        _gaugeSfxOutput?.Stop();
        _gaugeSfxOutput?.Dispose();
        _gaugeSfxOutput = null;
        _gaugeSfxReader?.Dispose();
        _gaugeSfxReader = null;
    }

    /// <summary>
    /// 1回限りのSE(PanelIn.ogg / ConditionGaugeCountUp.ogg等)を再生する。再生完了後は自動的に破棄する。
    /// </summary>
    private static void PlaySfx(string path)
    {
        try
        {
            var reader = new VorbisWaveReader(path);
            var output = new WaveOutEvent();
            output.Init(reader);
            output.PlaybackStopped += (s, e) =>
            {
                output.Dispose();
                reader.Dispose();
            };
            output.Play();
        }
        catch (Exception e)
        {
            Console.WriteLine($"[DanResultScene] SE再生失敗: {path} ({e.Message})");
        }
    }

    /// <summary>
    /// 内部のWaveStreamを末尾まで再生したら先頭に戻し、ループ再生させるためのラッパー(BGMループ用)。
    /// </summary>
    private sealed class LoopStream : WaveStream
    {
        private readonly WaveStream _source;

        public LoopStream(WaveStream source)
        {
            _source = source;
        }

        public override WaveFormat WaveFormat => _source.WaveFormat;
        public override long Length => _source.Length;

        public override long Position
        {
            get => _source.Position;
            set => _source.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = _source.Read(buffer, offset + totalRead, count - totalRead);
                if (read == 0)
                {
                    if (_source.Position == 0) break; // 💡 空ファイル等で無限ループしないための保険
                    _source.Position = 0;
                    continue;
                }
                totalRead += read;
            }
            return totalRead;
        }
    }

    private static List<ResultBarGroup> BuildFinalConditionBars()
    {
        var list = new List<ResultBarGroup>();
        {
            var gaugeGroup = new ResultBarGroup { IsOverall = false, IsGauge = true };
            var th = new Exam.Threshold { Red = _gaugeThreshold.Red, Gold = _gaugeThreshold.Gold };
            var final = DanGaugeMeter.ComputeFinalBarState(Exam.ConditionType.Score, _gaugePercent, th);
            gaugeGroup.Bars.Add((final.percent, final.color, final.displayNumber, final.numberGradient, th.Red));
            list.Add(gaugeGroup);
        }
        foreach (var cond in _conditions)
        {
            if (cond.ThresholdsPerSong == null || cond.ThresholdsPerSong.Length == 0) continue;
            var group = new ResultBarGroup { IsOverall = cond.IsOverall, Type = cond.Type };
            if (cond.IsOverall)
            {
                Exam.Threshold th = cond.ThresholdsPerSong[cond.ThresholdsPerSong.Length - 1];
                int achieved = GetOverallAchieved(cond.Type);
                var final = DanGaugeMeter.ComputeFinalBarState(cond.Type, achieved, th);
                group.Bars.Add((final.percent, final.color, final.displayNumber, final.numberGradient, th.Red));
            }
            else
            {
                int songCount = Math.Min(cond.ThresholdsPerSong.Length, _songResults.Count);
                for (int i = 0; i < songCount; i++)
                {
                    Exam.Threshold th = cond.ThresholdsPerSong[i];
                    int achieved = GetSongAchieved(cond.Type, _songResults[i]);
                    var final = DanGaugeMeter.ComputeFinalBarState(cond.Type, achieved, th);
                    group.Bars.Add((final.percent, final.color, final.displayNumber, final.numberGradient, th.Red));
                }
            }
            list.Add(group);
        }
        return list;
    }

    private static int GetOverallAchieved(Exam.ConditionType type)
    {
        if (type == Exam.ConditionType.Score || type == Exam.ConditionType.MaxCombo)
        {
            var last = _songResults.Count > 0 ? _songResults[_songResults.Count - 1] : default;
            return type == Exam.ConditionType.Score ? last.Score : last.MaxCombo;
        }
        int achieved = 0;
        foreach (var r in _songResults)
        {
            achieved += GetSongAchieved(type, r);
        }
        return achieved;
    }

    private static int GetSongAchieved(Exam.ConditionType type, Exam.SongResult r)
    {
        return type switch
        {
            Exam.ConditionType.Great => r.Great,
            Exam.ConditionType.Good => r.Good,
            Exam.ConditionType.Miss => r.Miss,
            Exam.ConditionType.Roll => r.Roll,
            Exam.ConditionType.Hit => r.Hit,
            Exam.ConditionType.Score => r.Score,
            Exam.ConditionType.MaxCombo => r.MaxCombo,
            _ => 0,
        };
    }

    private static void DrawGaugeRowLabelsOnTop(float vx, float vy, float scale)
    {
        for (int row = 0; row < _finalConditionBars.Count; row++)
        {
            var group = _finalConditionBars[row];
            if (!group.IsGauge) continue;
            float rowY = row * DanGaugeMeter.ResultLineHeight;
            float columnX = 0f;
            DrawConditionLabel(group, vx, vy, rowY, scale);
            for (int col = 0; col < group.Bars.Count; col++)
            {
                var (_, _, _, _, thresholdRed) = group.Bars[col];
                string valueText = $"{thresholdRed}以上";
                DrawConditionValue(valueText, columnX, rowY, group.IsOverall, vx, vy, scale);
                columnX += (group.IsOverall ? DanGaugeMeter.ResultColumnWidthOverall : DanGaugeMeter.ResultColumnWidthIndividual)
                           + DanGaugeMeter.ResultColumnGap;
            }
        }
    }

    private static void DrawConditionBars(float vx, float vy, float scale)
    {
        double nowSec = Raylib.GetTime();
        double stepSec = CONDITION_BAR_REVEAL_SEC + CONDITION_BAR_STAGGER_SEC;
        int barIndex = 0;
        for (int row = 0; row < _finalConditionBars.Count; row++)
        {
            var group = _finalConditionBars[row];
            double startAt = group.IsGauge
                ? _totalPlateShownAtSec
                : _totalPlateShownAtSec + stepSec + barIndex * stepSec;
            double revealT = group.IsGauge ? 1.0 : (CONDITION_BAR_REVEAL_SEC > 0
                ? Math.Clamp((nowSec - startAt) / CONDITION_BAR_REVEAL_SEC, 0.0, 1.0)
                : 1.0);
            if (!group.IsGauge) barIndex++;
            float rowY = row * DanGaugeMeter.ResultLineHeight;
            float columnX = 0f;
            DrawDanConditionBarBase(vx, vy, rowY, scale);
            DrawConditionLabel(group, vx, vy, rowY, scale);
            for (int col = 0; col < group.Bars.Count; col++)
            {
                var (finalPercent, finalColor, finalDisplayNumber, finalGradient, thresholdRed) = group.Bars[col];
                bool isLessThanType = !group.IsGauge && LessThanTypesForLabel.Contains(group.Type);
                double displayPercent = isLessThanType
                    ? 1.0 - (1.0 - finalPercent) * revealT
                    : finalPercent * revealT;
                int shownNumber = isLessThanType
                    ? (int)Math.Round(thresholdRed - (thresholdRed - finalDisplayNumber) * revealT)
                    : (int)Math.Round(finalDisplayNumber * revealT);
                bool revealed = revealT >= 1.0;
                DanGaugeMeter.BarColor shownColor = revealed
                    ? finalColor
                    : (displayPercent < 0.5 ? DanGaugeMeter.BarColor.DarkYellow : DanGaugeMeter.BarColor.Yellow);
                bool shownGradient = finalGradient && revealed;
                if (!group.IsGauge)
                {
                    DanGaugeMeter.DrawResultRow(columnX, rowY, group.IsOverall, displayPercent, shownColor, shownNumber, shownGradient, vx, vy, scale, nowSec);
                }
                string suffix = isLessThanType ? "未満" : "以上";
                string valueText = $"{thresholdRed}{suffix}";
                DrawConditionValue(valueText, columnX, rowY, group.IsOverall, vx, vy, scale);
                columnX += (group.IsOverall ? DanGaugeMeter.ResultColumnWidthOverall : DanGaugeMeter.ResultColumnWidthIndividual)
                           + DanGaugeMeter.ResultColumnGap;
            }
        }
    }

    private const float RESULT_VALUE_OFFSET_X_OVERALL = 200f;
    private const float RESULT_VALUE_OFFSET_Y_OVERALL = 60f;
    private const float RESULT_VALUE_OFFSET_X_INDIVIDUAL = 275f;
    private const float RESULT_VALUE_OFFSET_Y_INDIVIDUAL = 100f;
    private const float RESULT_VALUE_FONT_SIZE = 22f;
    private const float RESULT_VALUE_FONT_SIZE_OVERALL = 32f;

    private static void DrawConditionValue(string text, float columnX, float rowY, bool isOverall, float vx, float vy, float scale)
    {
        float offsetX = isOverall ? RESULT_VALUE_OFFSET_X_OVERALL : RESULT_VALUE_OFFSET_X_INDIVIDUAL;
        float offsetY = isOverall ? RESULT_VALUE_OFFSET_Y_OVERALL : RESULT_VALUE_OFFSET_Y_INDIVIDUAL;
        float x = vx + (DanGaugeMeter.ResultBarX + columnX + offsetX) * scale;
        float y = vy + (DanGaugeMeter.ResultBarY + rowY + offsetY) * scale;
        float fontSize = (isOverall ? RESULT_VALUE_FONT_SIZE_OVERALL : RESULT_VALUE_FONT_SIZE) * scale;
        Vector2 measure = Raylib.MeasureTextEx(G.Font, text, fontSize, 2);
        G.DrawTextWithOutline16(G.Font, text, new Vector2(x - measure.X, y), fontSize, Color.White, Color.Black);
    }

    private static readonly Dictionary<Exam.ConditionType, string> ConditionLabelNames = new()
{
    { Exam.ConditionType.Great, "良の数" },
    { Exam.ConditionType.Good, "可の数" },
    { Exam.ConditionType.Miss, "不可の数" },
    { Exam.ConditionType.Roll, "連打数" },
    { Exam.ConditionType.Hit, "たたけた数" },
    { Exam.ConditionType.Score, "スコア" },
    { Exam.ConditionType.MaxCombo, "コンボ数" },
};

    private static readonly HashSet<Exam.ConditionType> LessThanTypesForLabel = new()
{
    Exam.ConditionType.Good,
    Exam.ConditionType.Miss,
};

    private const float CONDITION_LABEL_OFFSET_X = 20f;
    private const float CONDITION_LABEL_OFFSET_Y = 20f;
    private const float CONDITION_LABEL_FONT_SIZE = 24f;

    private static void DrawDanConditionBarBase(float vx, float vy, float rowY, float scale)
    {
        if (_texDanConditionBar.Id == 0) return;
        float x = vx + (DanGaugeMeter.ResultBarX + DAN_CONDITION_BAR_OFFSET_X) * scale;
        float y = vy + (DanGaugeMeter.ResultBarY + rowY + DAN_CONDITION_BAR_OFFSET_Y) * scale;
        Raylib.DrawTextureEx(_texDanConditionBar, new Vector2(x, y), 0f, scale * DAN_CONDITION_BAR_SCALE, Color.White);
    }

    private static void DrawConditionLabel(ResultBarGroup group, float vx, float vy, float rowY, float scale)
    {
        string text = group.IsGauge ? "魂ゲージ" : (ConditionLabelNames.TryGetValue(group.Type, out var name) ? name : "不明な条件");
        float fontSize = CONDITION_LABEL_FONT_SIZE * scale;
        float x = vx + (DanGaugeMeter.ResultBarX + CONDITION_LABEL_OFFSET_X) * scale;
        float y = vy + (DanGaugeMeter.ResultBarY + rowY + CONDITION_LABEL_OFFSET_Y) * scale;
        Vector2 measure = Raylib.MeasureTextEx(G.Font, text, fontSize, 2);
        G.DrawTextWithOutline16(G.Font, text, new Vector2(x - measure.X / 2f, y - measure.Y / 2f), fontSize, Color.White, Color.Black);
    }

    public static void ResetConfirm()
    {
        _confirmed = false;
    }

    /// <summary>フルコンボ/合格/全良のいずれかを判定し、対応する背景・光テクスチャを返す。該当なしならfalse。
    /// 優先度: 全良(ミス0かつGood0) > フルコンボ(ミス0) > 合格(_gaugeClear)</summary>
    private static bool TryGetFinalBgTextures(out Texture2D back, out Texture2D hikari)
    {
        back = default;
        hikari = default;
        if (!_gaugeClear) return false; // 💡 合格していない場合はフルコンボ/全良判定に関わらず光らせない

        int totalMiss = 0;
        int totalGood = 0;
        foreach (Exam.SongResult r in _songResults)
        {
            totalMiss += r.Miss;
            totalGood += r.Good;
        }
        bool isZenryo = totalMiss == 0 && totalGood == 0;
        bool isFullCombo = totalMiss == 0;

        if (isZenryo)
        {
            back = _njiBackTexture;
            hikari = _shoudanGinHikariTexture;
            return true;
        }
        if (isFullCombo)
        {
            back = _kinBackTexture;
            hikari = _shoudanKinHikariTexture;
            return true;
        }
        if (_gaugeClear)
        {
            back = _ginBackTexture;
            hikari = _shoudanGinHikariTexture;
            return true;
        }
        back = default;
        hikari = default;
        return false;
    }

    // ==================================================================
    // 💡 「(ランク) 合格」表示用の文字スプライト合成
    // ==================================================================
    private const int MOZI_SHEET1_COLS = 4; // GinMozi/GoldMozi: 段人合格 / 玄名超達
    private const int MOZI_SHEET1_ROWS = 2;
    private const int MOZI_SHEET2_COLS = 6; // GinMozi/GoldMozi(数字): 一三五七九初 / 二四六八十級
    private const int MOZI_SHEET2_ROWS = 2;

    private const float RESULT_TEXT_GLYPH_HEIGHT = 360f; // 文字1つぶんの高さ
    private const float RESULT_TEXT_GLYPH_GAP = -25f;   // 通常の文字間隔
    private const float RESULT_TEXT_WORD_GAP = 25f;     // ランク文字と「合格」の間の、ちょっとだけ空けるスペース
    private const float RESULT_TEXT_CENTER_X_OFFSET = 0f;   // 画面中央からの横オフセット(文字列全体)
    private const float RESULT_TEXT_CENTER_Y_OFFSET = -175f; // 画面中央からの縦オフセット

    // 💡 結果背景(_showingResultBg)が出てからのタイミング
    //    0秒: 段位ランク文字(例:五級)を表示
    //    RESULT_PASS_TEXT_DELAY_SEC後: 「合格」を追加表示
    //    RESULT_PASS_BURST_DELAY_SEC後: 「合格」の文字自体を透明度0→100%・拡大率100%→500%でRESULT_PASS_BURST_DURATION_SEC秒かけて拡大表示する
    //    (背景・ヒカリ・通常表示の「合格」文字はそのまま残る。これは別レイヤーとして重ねて出す演出)
    private const float RESULT_PASS_TEXT_DELAY_SEC = 0.5f;
    private const float RESULT_PASS_BURST_DELAY_SEC = RESULT_PASS_TEXT_DELAY_SEC + 1.0f;
    private const float RESULT_PASS_BURST_DURATION_SEC = 0.1f;
    private const float RESULT_PASS_BURST_SCALE_START = 1.0f; // 100%
    private const float RESULT_PASS_BURST_SCALE_END = 2.0f;   // 500%

    private readonly struct MoziGlyph
    {
        public readonly int Sheet; // 1 = GinMozi/GoldMozi, 2 = GinMozi2/GoldMozi2
        public readonly int Col;
        public readonly int Row;
        public MoziGlyph(int sheet, int col, int row) { Sheet = sheet; Col = col; Row = row; }
    }

    private static readonly Dictionary<char, MoziGlyph> _moziGlyphMap = new()
    {
        ['段'] = new MoziGlyph(1, 0, 0),
        ['人'] = new MoziGlyph(1, 1, 0),
        ['合'] = new MoziGlyph(1, 2, 0),
        ['格'] = new MoziGlyph(1, 3, 0),
        ['玄'] = new MoziGlyph(1, 0, 1),
        ['名'] = new MoziGlyph(1, 1, 1),
        ['超'] = new MoziGlyph(1, 2, 1),
        ['達'] = new MoziGlyph(1, 3, 1),
        ['一'] = new MoziGlyph(2, 0, 0),
        ['三'] = new MoziGlyph(2, 1, 0),
        ['五'] = new MoziGlyph(2, 2, 0),
        ['七'] = new MoziGlyph(2, 3, 0),
        ['九'] = new MoziGlyph(2, 4, 0),
        ['初'] = new MoziGlyph(2, 5, 0),
        ['二'] = new MoziGlyph(2, 0, 1),
        ['四'] = new MoziGlyph(2, 1, 1),
        ['六'] = new MoziGlyph(2, 2, 1),
        ['八'] = new MoziGlyph(2, 3, 1),
        ['十'] = new MoziGlyph(2, 4, 1),
        ['級'] = new MoziGlyph(2, 5, 1),
    };

    private static bool TryGetGlyphSource(char c, Texture2D sheet1, Texture2D sheet2, out Texture2D tex, out Rectangle src)
    {
        tex = default;
        src = default;
        if (!_moziGlyphMap.TryGetValue(c, out MoziGlyph g)) return false;
        Texture2D sheet = g.Sheet == 1 ? sheet1 : sheet2;
        if (sheet.Id == 0) return false;
        int cols = g.Sheet == 1 ? MOZI_SHEET1_COLS : MOZI_SHEET2_COLS;
        int rows = g.Sheet == 1 ? MOZI_SHEET1_ROWS : MOZI_SHEET2_ROWS;
        float cw = sheet.Width / (float)cols;
        float ch = sheet.Height / (float)rows;
        tex = sheet;
        src = new Rectangle(g.Col * cw, g.Row * ch, cw, ch);
        return true;
    }

    /// <summary>_danTitleの中で文字スプライトに存在する文字(五/初/段/級など)を抜き出し、
    /// その後ろに「合格」を続けて中央揃えで描画する。例: "五級" + "合格" → "五級 合格"
    /// showPass=falseの間は「合格」を描画せず、ランク文字だけレイアウト上の位置を確保しておく(後から合格が出ても位置がズレない)</summary>
    private static void DrawResultMoziText(int sw, float centerY, float scale, bool gold, bool showPass)
    {
        Texture2D sheet1 = gold ? _goldMoziTexture : _ginMoziTexture;
        Texture2D sheet2 = gold ? _goldMozi2Texture : _ginMozi2Texture;

        List<char> chars = new();
        if (!string.IsNullOrEmpty(_danTitle))
        {
            foreach (char c in _danTitle)
            {
                if (_moziGlyphMap.ContainsKey(c)) chars.Add(c);
            }
        }
        // 💡 外伝など、_danTitleが文字表(段/級/初〜十など)に無い名称の場合はランク文字を付けず「合格」のみ表示する
        int wordGapIndex = chars.Count > 0 ? chars.Count : -1; // このインデックスの文字の手前にRESULT_TEXT_WORD_GAPを入れる(ランク文字が無ければ単語間ギャップも無し)
        int passStartIndex = chars.Count; // このインデックス以降が「合格」
        chars.Add('合');
        chars.Add('格');

        float digitH = RESULT_TEXT_GLYPH_HEIGHT * scale;
        float gap = RESULT_TEXT_GLYPH_GAP * scale;
        float wordGap = RESULT_TEXT_WORD_GAP * scale;

        List<(Texture2D tex, Rectangle src, float w, float gapBefore, bool isPass)> glyphs = new();
        for (int i = 0; i < chars.Count; i++)
        {
            if (!TryGetGlyphSource(chars[i], sheet1, sheet2, out Texture2D tex, out Rectangle src)) continue;
            float w = src.Width * (digitH / src.Height);
            float gapBefore = glyphs.Count == 0 ? 0f : (i == wordGapIndex ? wordGap : gap);
            glyphs.Add((tex, src, w, gapBefore, i >= passStartIndex));
        }
        if (glyphs.Count == 0) return;

        float totalW = 0f;
        foreach (var g in glyphs) totalW += g.w + g.gapBefore;

        float curX = (sw - totalW) / 2f + RESULT_TEXT_CENTER_X_OFFSET * scale;
        float y = centerY - digitH / 2f;
        foreach (var g in glyphs)
        {
            curX += g.gapBefore;
            if (!g.isPass || showPass)
            {
                Rectangle dst = new Rectangle(curX, y, g.w, digitH);
                Raylib.DrawTexturePro(g.tex, g.src, dst, Vector2.Zero, 0f, Color.White);
            }
            curX += g.w;
        }
    }

    /// <summary>合格が出てから一定時間後に、「合格」の文字だけを透明度0→100%・拡大率100%→500%で
    /// 中心から膨らませるように描画する(通常表示の「合格」文字はそのまま残り、これは重ねて出す演出レイヤー)</summary>
    private static void DrawResultPassBurst(int sw, float centerY, float scale, bool gold, float burstScale, float alpha)
    {
        if (alpha <= 0f) return;
        Texture2D sheet1 = gold ? _goldMoziTexture : _ginMoziTexture;
        if (!TryGetGlyphSource('合', sheet1, sheet1, out Texture2D tex1, out Rectangle src1)) return;
        if (!TryGetGlyphSource('格', sheet1, sheet1, out Texture2D tex2, out Rectangle src2)) return;

        float digitH = RESULT_TEXT_GLYPH_HEIGHT * scale * burstScale;
        float gap = RESULT_TEXT_GLYPH_GAP * scale * burstScale;
        float w1 = src1.Width * (digitH / src1.Height);
        float w2 = src2.Width * (digitH / src2.Height);
        float totalW = w1 + gap + w2;

        float curX = (sw - totalW) / 2f + RESULT_TEXT_CENTER_X_OFFSET * scale;
        float y = centerY - digitH / 2f;
        byte a = (byte)Math.Clamp(alpha * 255f, 0f, 255f);
        Color tint = new Color((byte)255, (byte)255, (byte)255, a);

        Raylib.DrawTexturePro(tex1, src1, new Rectangle(curX, y, w1, digitH), Vector2.Zero, 0f, tint);
        curX += w1 + gap;
        Raylib.DrawTexturePro(tex2, src2, new Rectangle(curX, y, w2, digitH), Vector2.Zero, 0f, tint);
    }

    public static void Update(float dt)
    {
        if (_inputGuardTimer < INPUT_GUARD_SEC)
        {
            _inputGuardTimer += dt;
        }
        if (_revealCount < _songResults.Count)
        {
            _revealTimer += dt;
            if (_revealTimer >= REVEAL_INTERVAL_SEC)
            {
                _revealTimer = 0f;
                _revealCount++;
                PlaySfx(SE_PANEL_IN_PATH); // 💡 曲パネルが出てくるタイミングで再生
            }
        }
        for (int i = 0; i < _revealCount && i < _slideTimers.Count; i++)
        {
            if (_slideTimers[i] < SLIDE_DURATION_SEC)
            {
                _slideTimers[i] = Math.Min(_slideTimers[i] + dt, SLIDE_DURATION_SEC);
            }
        }
        // 💡 魂ゲージが上がる音(SE_GAUGE_PATH)を、gaugePercentに応じた時間で自動的に止める
        if (!_gaugeSfxStopped && Raylib.GetTime() >= _gaugeSfxStopAtSec)
        {
            StopGaugeSfx();
            _gaugeSfxStopped = true;
        }

        // 💡 各条件バーが上下し始めるタイミングでConditionGaugeCountUp.oggを1回ずつ再生する
        if (_conditionBarSoundStartSec != null)
        {
            double nowSecSfx = Raylib.GetTime();
            for (int row = 0; row < _conditionBarSoundStartSec.Length; row++)
            {
                if (_finalConditionBars[row].IsGauge) continue;
                if (!_conditionBarSoundPlayed[row] && nowSecSfx >= _conditionBarSoundStartSec[row])
                {
                    _conditionBarSoundPlayed[row] = true;
                    PlaySfx(SE_CONDITION_COUNT_UP_PATH);
                }
            }
        }

        if (_confirmed) return;
        if (_inputGuardTimer < INPUT_GUARD_SEC) return;

        bool donPressed = SettingsPanel.IsAnyDonPressed();
        bool decide = donPressed || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Escape);
        if (!decide) return;

        // 💡 Don1/Don2が押されたら、アニメーション中なら一気に完了させる
        if (_revealCount < _songResults.Count)
        {
            _revealCount = _songResults.Count;
            _revealTimer = 0f;
            for (int i = 0; i < _slideTimers.Count; i++) _slideTimers[i] = SLIDE_DURATION_SEC;
            return;
        }

        bool anySliding = false;
        for (int i = 0; i < _slideTimers.Count; i++)
        {
            if (_slideTimers[i] < SLIDE_DURATION_SEC)
            {
                _slideTimers[i] = SLIDE_DURATION_SEC;
                anySliding = true;
            }
        }
        if (anySliding) return;

        if (!_totalPlateShown)
        {
            _totalPlateShown = true;
            _totalPlateShownAtSec = Raylib.GetTime();
            _finalConditionBars = BuildFinalConditionBars();
            Gauge.StartReveal(_totalPlateShownAtSec);

            // 💡 魂ゲージが上がる音を再生開始し、gaugePercent(最大100%=GAUGE_MAX_ANIM_SEC秒)に応じて途中で止める
            double gaugeStopAtSec = _totalPlateShownAtSec + (_gaugePercent / 100.0) * GAUGE_MAX_ANIM_SEC;
            PlayGaugeSfx(gaugeStopAtSec);

            // 💡 各条件バーが上下し始める時刻をあらかじめ計算しておく(DrawConditionBarsと同じ規則)
            double stepSecForSfx = CONDITION_BAR_REVEAL_SEC + CONDITION_BAR_STAGGER_SEC;
            _conditionBarSoundStartSec = new double[_finalConditionBars.Count];
            _conditionBarSoundPlayed = new bool[_finalConditionBars.Count];
            int barIndexForSfx = 0;
            for (int row = 0; row < _finalConditionBars.Count; row++)
            {
                if (_finalConditionBars[row].IsGauge) continue;
                _conditionBarSoundStartSec[row] = _totalPlateShownAtSec + stepSecForSfx + barIndexForSfx * stepSecForSfx;
                barIndexForSfx++;
            }
            return;
        }

        // 💡 Total_plate表示後、条件バーのrevealがまだ完了していなければ一気に完了させる
        double nowSec = Raylib.GetTime();
        double stepSec = CONDITION_BAR_REVEAL_SEC + CONDITION_BAR_STAGGER_SEC;
        int barIndex = 0;
        bool anyBarRevealing = false;
        for (int row = 0; row < _finalConditionBars.Count; row++)
        {
            var group = _finalConditionBars[row];
            if (group.IsGauge) continue;
            double startAt = _totalPlateShownAtSec + stepSec + barIndex * stepSec;
            double revealT = CONDITION_BAR_REVEAL_SEC > 0 ? Math.Clamp((nowSec - startAt) / CONDITION_BAR_REVEAL_SEC, 0.0, 1.0) : 1.0;
            if (revealT < 1.0) { anyBarRevealing = true; break; }
            barIndex++;
        }
        if (anyBarRevealing)
        {
            double totalDuration = stepSec * (barIndex + 1);
            _totalPlateShownAtSec = nowSec - totalDuration - 1.0;
            return;
        }

        // 💡 最終アニメーション終了後、まだ結果背景(フルコンボ/合格/全良)を見せていなければ
        //    ここで一度挟んで表示し、確定(_confirmed=true)は次のDon押下まで待つ。
        //    該当する結果が無い場合(不合格など)はそのまま従来通り確定する。
        if (!_showingResultBg)
        {
            if (TryGetFinalBgTextures(out Texture2D bg, out Texture2D hikari))
            {
                _showingResultBg = true;
                _resultBgShownAtSec = nowSec;
                _selectedResultBgTexture = bg;
                _selectedResultHikariTexture = hikari;
                return;
            }
        }

        _confirmed = true;
        ResetState(); // 💡 追加：BGMとゲージSEの両方を一括で止める

    }

    public static void Draw()
    {
        int sw = Program.VirtualWidth;
        int sh = Program.VirtualHeight;
        float scale = Math.Clamp(sw / 1920f, 0.5f, 2f);
        Raylib.DrawRectangle(0, 0, sw, sh, new Color((byte)10, (byte)14, (byte)26, (byte)255));

        if (_showingResultBg)
        {
            // 💡 元のback画像・ヒカリ画像はアニメーション中もずっとそのまま残す
            DrawTextureCentered(_selectedResultBgTexture, sw, sh, 0f, 0f);

            double elapsed = Raylib.GetTime() - _resultBgShownAtSec;
            DrawTextureCentered(_selectedResultHikariTexture, sw, sh, 0f, 0f);

            float textCenterY = sh / 2f + RESULT_TEXT_CENTER_Y_OFFSET * scale;

            if (_gaugeClear)
            {
                bool gold = _overallRank == Exam.PassRank.Gold;
                bool showPass = elapsed >= RESULT_PASS_TEXT_DELAY_SEC;
                DrawResultMoziText(sw, textCenterY, scale, gold, showPass);

                // 💡 合格が出てから1秒後、「合格」の文字だけ透明度100%→0%・拡大率100%→500%で0.25秒かけて膨らませながら消す
                if (elapsed >= RESULT_PASS_BURST_DELAY_SEC)
                {
                    float burstT = RESULT_PASS_BURST_DURATION_SEC > 0f
                        ? Math.Clamp((float)((elapsed - RESULT_PASS_BURST_DELAY_SEC) / RESULT_PASS_BURST_DURATION_SEC), 0f, 1f)
                        : 1f;
                    float burstScale = RESULT_PASS_BURST_SCALE_START + (RESULT_PASS_BURST_SCALE_END - RESULT_PASS_BURST_SCALE_START) * burstT;
                    float burstAlpha = 1f - burstT; // 100%→0%でフェードアウト
                    DrawResultPassBurst(sw, textCenterY, scale, gold, burstScale, burstAlpha);
                }
            }

            bool blinkBg = ((int)(Raylib.GetTime() * 2.0) % 2) == 0;
            if (blinkBg)
            {
                string promptBg = "決定ボタンで段位選択へ戻る";
                float promptBgSize = 26f * scale;
                Vector2 promptBgMeasure = Raylib.MeasureTextEx(G.Font, promptBg, promptBgSize, 2);
                G.DrawTextWithOutline16(
                    G.Font, promptBg,
                    new Vector2((sw - promptBgMeasure.X) / 2f, sh - 70f * scale),
                    promptBgSize, Color.White, Color.Black
                );
            }
            return;
        }

        DrawTextureCentered(_bgTexture, sw, sh, 0f, 0f);

        if (_totalPlateShown)
        {
            DrawTextureCentered(_totalPlateTexture, sw, sh, PLATE_OFFSET_X, PLATE_OFFSET_Y);

            // 💡 ScorePanel.pngをTotal_plate.pngの上に重ねて描画
            if (_scorePanelTexture.Id != 0)
            {
                float scorePanelX = (sw - _scorePanelTexture.Width) / 2f + TOTAL_SCORE_PANEL_OFFSET_X * scale;
                float scorePanelY = (sh - _scorePanelTexture.Height) / 2f + TOTAL_SCORE_PANEL_OFFSET_Y * scale;
                float scorePanelW = _scorePanelTexture.Width * scale * TOTAL_SCORE_PANEL_SCALE;
                float scorePanelH = _scorePanelTexture.Height * scale * TOTAL_SCORE_PANEL_SCALE;
                if (TOTAL_SCORE_PANEL_SCALE != 1.0f)
                {
                    scorePanelX += (_scorePanelTexture.Width * scale - scorePanelW) / 2f;
                    scorePanelY += (_scorePanelTexture.Height * scale - scorePanelH) / 2f;
                }
                Raylib.DrawTexturePro(
                    _scorePanelTexture,
                    new Rectangle(0, 0, _scorePanelTexture.Width, _scorePanelTexture.Height),
                    new Rectangle(scorePanelX, scorePanelY, scorePanelW, scorePanelH),
                    Vector2.Zero, 0f, Color.White
                );
            }

            float baseVx = (sw - 1920f * scale) / 2f;
            float baseVy = (sh - 1080f * scale) / 2f;

            // 💡 条件バー専用の座標 (CONDITION_BARS_OFFSET_X/Y の影響を受ける)
            float condVx = baseVx + CONDITION_BARS_OFFSET_X * scale;
            float condVy = baseVy + CONDITION_BARS_OFFSET_Y * scale;

            // 💡 ゲージ専用の座標 (GAUGE_OFFSET_X/Y の影響を受ける)
            float gaugeVx = baseVx + GAUGE_OFFSET_X * scale;
            float gaugeVy = baseVy + GAUGE_OFFSET_Y * scale;

            // 条件バーを描画 (condVx, condVy を使用)
            DrawConditionBars(condVx, condVy, scale);

            // ゲージ背面を描画 (gaugeVx, gaugeVy を使用)
            if (_gaugeBackTexture.Id != 0)
            {
                float gbx = gaugeVx + GAUGE_BACK_OFFSET_X * scale;
                float gby = gaugeVy + GAUGE_BACK_OFFSET_Y * scale;
                Raylib.DrawTextureEx(_gaugeBackTexture, new Vector2(gbx, gby), 0f, scale * GAUGE_BACK_SCALE, Color.White);
            }

            // ゲージ本体を描画 (gaugeVx, gaugeVy, GAUGE_SCALE を使用)
            Gauge.Draw(gaugeVx, gaugeVy, scale * GAUGE_SCALE, Raylib.GetTime());

            // 魂ゲージ行のラベルも条件バーと同じ座標系で描画
            DrawGaugeRowLabelsOnTop(condVx, condVy, scale);

            bool blinkTotal = ((int)(Raylib.GetTime() * 2.0) % 2) == 0;
            if (blinkTotal)
            {
                string promptTotal = "決定ボタンで段位選択へ戻る";
                float promptTotalSize = 26f * scale;
                Vector2 promptTotalMeasure = Raylib.MeasureTextEx(G.Font, promptTotal, promptTotalSize, 2);
                G.DrawTextWithOutline16(
                    G.Font, promptTotal,
                    new Vector2((sw - promptTotalMeasure.X) / 2f, sh - 70f * scale),
                    promptTotalSize, Color.White, Color.Black
                );
            }
            return;
        }

        DrawTextureCentered(_plateTexture, sw, sh, PLATE_OFFSET_X, PLATE_OFFSET_Y);

        float titleFontSize = 56f * scale;
        string headline = string.IsNullOrEmpty(_danTitle) ? "段位認定" : $"{_danTitle} 認定結果";
        Vector2 headlineSize = Raylib.MeasureTextEx(G.Font, headline, titleFontSize, 2);
        float headlineX = (sw - headlineSize.X) / 2f;
        float headlineY = 40f * scale;
        G.DrawTextWithOutline16(G.Font, headline, new Vector2(headlineX, headlineY), titleFontSize, Color.White, Color.Black);

        int songCount = _songResults.Count;
        float rowW = SONG_ROW_WIDTH * scale;
        float rowH = SONG_ROW_HEIGHT * scale;
        float rowGap = ROW_GAP * scale;
        float startX = (sw - rowW) / 2f + SONG_OFFSET_X * scale;
        float panelY = 130f * scale + SONG_OFFSET_Y * scale;

        for (int i = 0; i < songCount && i < _revealCount; i++)
        {
            float py = panelY + i * (rowH + rowGap);
            float slideT = (i < _slideTimers.Count) ? _slideTimers[i] / SLIDE_DURATION_SEC : 1f;
            slideT = Math.Clamp(slideT, 0f, 1f);
            float eased = EaseInOutCubic(slideT);
            float slideOffsetX = (1f - eased) * SLIDE_DISTANCE * scale;
            DrawSongPanel(startX + slideOffsetX, py, scale, i);
        }

        if (_revealCount >= songCount)
        {
            float summaryY = panelY + songCount * (rowH + rowGap) + 20f * scale;
            DrawOverallSummary(sw, summaryY, scale);

            bool blink = ((int)(Raylib.GetTime() * 2.0) % 2) == 0;
            if (blink)
            {
                string prompt = "決定ボタンで段位選択へ戻る";
                float promptSize = 26f * scale;
                Vector2 promptMeasure = Raylib.MeasureTextEx(G.Font, prompt, promptSize, 2);
                G.DrawTextWithOutline16(
                    G.Font, prompt,
                    new Vector2((sw - promptMeasure.X) / 2f, sh - 70f * scale),
                    promptSize, Color.White, Color.Black
                );
            }
        }
    }

    private static void DrawTextureCentered(Texture2D texture, int sw, int sh, float offsetX, float offsetY)
    {
        DrawTextureCentered(texture, sw, sh, offsetX, offsetY, 1f);
    }

    private static void DrawTextureCentered(Texture2D texture, int sw, int sh, float offsetX, float offsetY, float alpha)
    {
        if (texture.Id == 0) return;
        if (alpha <= 0f) return;
        float x = (sw - texture.Width) / 2f + offsetX;
        float y = (sh - texture.Height) / 2f + offsetY;
        byte a = (byte)Math.Clamp(alpha * 255f, 0f, 255f);
        Raylib.DrawTexture(texture, (int)x, (int)y, new Color((byte)255, (byte)255, (byte)255, a));
    }

    private static void DrawSongPanel(float x, float y, float scale, int index)
    {
        if (_songTexture.Id != 0)
        {
            Rectangle src = new Rectangle(SONG_PANEL_X, SONG_ROW0_Y + index * SONG_ROW_STRIDE, SONG_ROW_WIDTH, SONG_ROW_HEIGHT);
            Rectangle dst = new Rectangle(x, y, SONG_ROW_WIDTH * scale, SONG_ROW_HEIGHT * scale);
            Raylib.DrawTexturePro(_songTexture, src, dst, Vector2.Zero, 0f, Color.White);
        }
        Exam.SongResult r = _songResults[index];
        string title = (index < _songTitles.Count && !string.IsNullOrEmpty(_songTitles[index])) ? _songTitles[index] : "(タイトル不明)";
        title = TruncateForPanel(title, 20);
        float titleFontSize = SONG_TITLE_FONT_SIZE * scale;
        DrawValueOnRow(x, y, scale, SONG_TITLE_REL_X, SONG_TITLE_REL_Y, titleFontSize, title, Color.White, leftAlign: true);
        DrawNumberOnRow(x, y, scale, SONG_GOOD_REL_X, SONG_STAT_REL_Y, r.Great.ToString());
        DrawNumberOnRow(x, y, scale, SONG_OK_REL_X, SONG_STAT_REL_Y, r.Good.ToString());
        DrawNumberOnRow(x, y, scale, SONG_MISS_REL_X, SONG_STAT_REL_Y, r.Miss.ToString());
        DrawNumberOnRow(x, y, scale, SONG_ROLL_REL_X, SONG_STAT_REL_Y, r.Roll.ToString());
    }

    private static void DrawNumberOnRow(float rowX, float rowY, float scale, float relX, float relY, string number)
    {
        float digitH = SONG_STAT_DIGIT_HEIGHT * scale;
        float centerY = rowY + relY * scale;
        DrawNumberSprite(number, rowX + relX * scale, centerY - digitH / 2f, digitH);
    }

    private static void DrawNumberSprite(string number, float x, float y, float digitHeight)
    {
        if (_numberTexture.Id == 0 || string.IsNullOrEmpty(number)) return;
        float digitScale = digitHeight / NUMBER_CELL_H;
        float digitW = NUMBER_CELL_W * digitScale;
        float curX = x;
        foreach (char c in number)
        {
            if (c < '0' || c > '9') { curX += digitW * NUMBER_KERNING; continue; }
            int digit = c - '0';
            Rectangle src = new Rectangle(digit * NUMBER_CELL_W, 0, NUMBER_CELL_W, NUMBER_CELL_H);
            Rectangle dst = new Rectangle(curX, y, digitW, digitHeight);
            Raylib.DrawTexturePro(_numberTexture, src, dst, Vector2.Zero, 0f, Color.White);
            curX += digitW * NUMBER_KERNING;
        }
    }

    private static void DrawValueOnRow(float rowX, float rowY, float scale, float relX, float relY, float fontSize, string text, Color color, bool leftAlign)
    {
        Vector2 measure = Raylib.MeasureTextEx(G.Font, text, fontSize, 2);
        float drawX = rowX + relX * scale - (leftAlign ? 0f : measure.X / 2f);
        float drawY = rowY + relY * scale - measure.Y / 2f;
        G.DrawTextWithOutline16(G.Font, text, new Vector2(drawX, drawY), fontSize, color, Color.Black);
    }

    private static void DrawOverallSummary(int sw, float y, float scale)
    {
        string gaugeText = _gaugeClear ? $"魂ゲージ: クリア ({_gaugePercent}%)" : $"魂ゲージ: 未クリア ({_gaugePercent}%)";
        float gaugeFontSize = 28f * scale;
        Vector2 gaugeMeasure = Raylib.MeasureTextEx(G.Font, gaugeText, gaugeFontSize, 2);
        G.DrawTextWithOutline16(
            G.Font, gaugeText,
            new Vector2((sw - gaugeMeasure.X) / 2f, y),
            gaugeFontSize,
            _gaugeClear ? new Color((byte)255, (byte)230, (byte)120, (byte)255) : new Color((byte)200, (byte)200, (byte)200, (byte)255),
            Color.Black
        );
        DrawRankBadge(sw / 2f, y + 60f * scale, 420f * scale, scale, _overallRank, isOverall: true);
    }

    private static void DrawRankBadge(float centerX, float y, float width, float scale, Exam.PassRank rank, bool isOverall = false)
    {
        (string label, Color color) = rank switch
        {
            Exam.PassRank.Gold => ("金合格", new Color((byte)255, (byte)215, (byte)60, (byte)255)),
            Exam.PassRank.Red => ("赤合格", new Color((byte)255, (byte)90, (byte)70, (byte)255)),
            _ => ("不合格", new Color((byte)150, (byte)150, (byte)150, (byte)255)),
        };
        float badgeH = (isOverall ? 64f : 44f) * scale;
        float badgeW = Math.Min(width * 0.7f, (isOverall ? 320f : 220f) * scale);
        float badgeX = centerX - badgeW / 2f;
        Raylib.DrawRectangle((int)badgeX, (int)y, (int)badgeW, (int)badgeH, new Color(color.R, color.G, color.B, (byte)70));
        Raylib.DrawRectangleLines((int)badgeX, (int)y, (int)badgeW, (int)badgeH, color);
        float fontSize = (isOverall ? 34f : 24f) * scale;
        Vector2 measure = Raylib.MeasureTextEx(G.Font, label, fontSize, 2);
        G.DrawTextWithOutline16(
            G.Font, label,
            new Vector2(centerX - measure.X / 2f, y + (badgeH - fontSize) / 2f),
            fontSize, color, Color.Black
        );
    }

    private static string TruncateForPanel(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text;
        return text.Substring(0, maxChars - 1) + "…";
    }

    private static float EaseInOutCubic(float t)
    {
        return t < 0.5f ? 4f * t * t * t : 1f - (float)Math.Pow(-2f * t + 2f, 3f) / 2f;
    }
}
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;
using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Threading;
using TaikoNauts.Core.Taiko.Charts;

/// <summary>
/// 演奏シーン。TJAからデコードしたノーツの一律配点「真打スコア」の計算と適用、
/// 判定ラインでの演奏時計管理、入力タイムスタンプによる高精度判定および音符描画を統合的に処理します。
/// </summary>
public static class Enso
{
    public const float LANE_Y = 386f;
    private const float SE_NOTE_Y = 489f;
    public static float HitX = 618f;
    private const float LANE_X = 498f;
    private const float LANE_TOP = 276f;
    private const float BAR_Y = 288f;
    private const float LANE_RIGHT = 1920f;

    private const float NOTE_R_SMALL = 42f;
    private const float NOTE_R_BIG = 58f;
    private const float BAR_H = 120f;

    // 判定基準
    private const double WIN_PERFECT = 0.025f; // ±25ms (良)
    private const double WIN_GOOD = 0.075f;    // ±75ms (可)
    private const double WIN_BAD = 0.100f;     // ±100ms (不可)

    private static readonly Color COL_BAR = new Color((byte)180, (byte)180, (byte)180, (byte)160);
    private static readonly Color COL_HITFRAME = new Color((byte)255, (byte)255, (byte)255, (byte)200);
    private const float LANE_L_X = 498f;
    private const float LANE_FLASH_MAX_ALPHA = 0.5f;
    private const double LANE_FLASH_HOLD = 0.06;
    private const double LANE_FLASH_FADE = 0.06;
    private const double LANE_FLASH_DURATION = LANE_FLASH_HOLD + LANE_FLASH_FADE;

    private const double GOGO_LANE_GROW = 0.11;
    private const double GOGO_LANE_FADE = 0.06;

    private static double _laneFlashDonTime = -1.0;
    private static double _laneFlashKaTime = -1.0;
    private static double _laneFlashRollTime = -1.0;
    private static double _gogoLaneStartSec = -1.0;
    private static double _gogoLaneEndSec = -1.0;

    private static Texture2D _laneTex;
    private static Texture2D _hitTex;
    private static Texture2D _barTex;
    private static Texture2D _laneFlashDonTex;
    private static Texture2D _laneFlashKaTex;
    private static Texture2D _laneFlashRollTex;
    private static Texture2D _laneGogoTex;
    private static bool _laneAssetsLoaded;

    private static MusicTrack? _music;
    private static bool _musicLoaded;
    private static bool _musicStarted;
    private static string _loadedAudioPath; // 現在 _music にロード済みの音源パス（曲切り替え判定用）

    private static SoundEffect? _sndDon;
    private static SoundEffect? _sndKa;
    private static bool _soundsLoaded;

    private static byte[] _musicBytes;
    private static byte[] _donBytes;
    private static byte[] _kaBytes;

    private static long _nowTime;
    private static double _lastNowSec;
    private static bool _playing;
    private static double _startWallClock;

    // 初回のテクスチャ／音源ロード直後に発生する大きなフレーム時間で、
    // 開始前カウントを一気に消費しないための上限値。
    private const double MAX_INTRO_FRAME_STEP_SEC = 1.0 / 30.0;

    private static Chip[] _chips = Array.Empty<Chip>();
    private static int[] _noteLayers = { 0 };

    private static int _firstActiveIndex = 0;
    private static int _gogoSearchIndex = 0;
    private static int _lastVisibleIndex = -1;
    private const double NOTE_LOOKAHEAD_SEC = 20.0;

    private static int AdvanceLastVisibleIndex(double nowSec)
    {
        int n = _chips.Length;
        if (n == 0) return -1;
        if (_lastVisibleIndex < _firstActiveIndex) _lastVisibleIndex = _firstActiveIndex;

        double limit = nowSec + NOTE_LOOKAHEAD_SEC;
        while (_lastVisibleIndex + 1 < n && TJA.ChipSec(_chips[_lastVisibleIndex + 1]) <= limit)
            _lastVisibleIndex++;

        return _lastVisibleIndex;
    }



    private static int _perfect, _good, _bad, _miss, _combo, _maxCombo, _score;

    private static int _scorePerNote;

    private static Chip? _activeRoll;
    private static int _rollHits;

    private static NoteType[] _origNoteTypesForOptions;
    private static SeNoteType[] _origSeNoteTypesForOptions;

    private static int _danCumPerfect, _danCumGood, _danCumBad, _danCumMiss, _danCumRollHits;
    private static int _totalRollHitsThisSong;

    private static double _balloonPopSec = -1.0;
    private static SoundEffect? _sndBalloonPop;

    private const float BALLOON_NUM_H = 86f;
    private const float BALLOON_NUM_H_PEAK = 93f;
    private const double BALLOON_NUM_SHRINK = 0.05;
    private const double BALLOON_NUM_RECOVER = 0.11;
    private static double _balloonNumAnimStart = -1.0;
    private static float _balloonNumAnimFromH = BALLOON_NUM_H;
    private static LuaSkin _balloonFailSkin;
    private static double _balloonFailStartSec = -1.0;
    private static double _rollNextHitSec;
    private const double ROLL_HIT_INTERVAL = 1.0 / 60.0;

    private static bool _isGoGo;

    private const double END_FADE_SEC = 1.7;
    private const double END_WAIT_SEC = 0.73;
    private static double _endSequenceStartWall = -1.0;
    private static double _lastChipSec;
    public static bool EndSequenceDone { get; private set; }

    public static bool Auto = false;

    /// <summary>
    /// 難易度決定時の O + L 同時押しで確定する2P参加状態。
    /// false の間は1Pレイアウトとして下背景・Mob・ダンサーを表示する。
    /// </summary>
    public static bool IsP2Active { get; private set; }

    /// <summary>
    /// 次に開始する通常演奏の2P参加状態を設定する。段位道場は1P専用のため常に無効化する。
    /// </summary>
    public static void SetP2Active(bool active)
    {
        IsP2Active = active && !DanMode;
    }

    public static bool DanMode = false;
    public static bool DanFirstSong = true;
    public static int DanSongIndex = 0;
    public static int DanTotalNotes = 0;

    private static LuaSkin _haikeiUeSkin;
    private static LuaSkin _haikeiShitaSkin;
    private static LuaSkin _haikeiShitaClearSkin;
    // 💡 背景フォルダに katsu.lua が無く Anime.aup2 しか無いケースへのフォールバック。
    //    lua版が読めた場合はこちらはnullのまま(Skin側が優先される)。
    private static Aup2Anim _haikeiUeAnim;
    private static readonly List<Aup2Anim> _haikeiUeDanAnims = new();
    private static Aup2Anim _haikeiShitaAnim;
    private static float _haikeiUeAnimTime;
    private static float _haikeiUeDanAnimTime;
    private static float _haikeiShitaAnimTime;
    private static Texture2D _haikeiShitaShitaTex;
    private static bool _haikeiShitaShitaLoaded;
    private const float SHITA_SHITA_Y = 1014f;
    private static float _haikeiTime;
    private static SongMeta _haikeiMeta;
    // 下背景が2P用の上背景複製か、1P用の Shita/Normal かを記録する。
    private static bool _haikeiLoadedForP2;

    private static MediaFile _bgMovieFile;
    private static Texture2D _bgMovieTex;
    private static bool _bgMovieLoaded;
    private static bool _hasBgMovie;
    private static double _bgMovieLastDrawnSec = -999.0;
    private static Rectangle _bgMovieSrcRect;

    private static Thread _bgMovieThread;
    private static volatile bool _bgMovieThreadRunning;
    private static readonly byte[][] _bgMovieBuffers = new byte[2][];
    private static int _bgMovieWriteIndex;
    private static int _bgMovieReadIndex = -1;
    private static readonly object _bgMovieSwapLock = new object();
    private static volatile bool _bgMovieFrameReady;
    private static double _bgMovieTargetSec = -1.0;
    private static readonly object _bgMovieTargetLock = new object();
    private static bool _bgMovieLoggedMismatch;
    private static bool _bgMovieLoggedException;

    private sealed class BgMovieOpenResult
    {
        public MediaFile? File;
        public byte[]? FirstFrameData;
        public int VisibleWidth, VisibleHeight, StrideWidth;
        public bool Ok;
    }
    private static System.Threading.Tasks.Task<BgMovieOpenResult>? _bgMovieOpenTask;
    private static string? _bgMovieOpenPath;

    private const float GENRE_X = 1590f;
    private const float GENRE_Y = 104f;
    private const float GENRE_FONT_SIZE = 22.5f;
    private const float GENRE_OUTLINE_THICKNESS = 7f;
    private static string _genreDisplayText = "";
    private static Texture2D _genreTex;
    private static bool _genreLoaded;
    private static bool _genreTexLoaded;

    // 💡 box.defのGENRE値 → 画面表示用の正規化されたジャンル名。
    //    box.def側の表記ゆれ（英語表記等）を吸収しつつ、フォント描画用の文字列に変換する。
    private static readonly Dictionary<string, string> GenreDisplayNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "特集", "特集" },
        { "ポップス", "ポップス" }, { "J-POP", "ポップス" }, { "POPS", "ポップス" },
        { "キッズ", "キッズ" }, { "どうよう", "キッズ" },
        { "アニメ", "アニメ" }, { "ANIME", "アニメ" },
        { "ボーカロイド", "ボーカロイド" }, { "ボーカロイド曲", "ボーカロイド" }, { "VOCALOID", "ボーカロイド" },
        { "ゲームミュージック", "ゲームミュージック" }, { "ゲームミュージック曲", "ゲームミュージック" },
        { "バラエティ", "バラエティ" }, { "VARIETY", "バラエティ" },
        { "クラシック", "クラシック" }, { "CLASSIC", "クラシック" },
        { "ナムコオリジナル", "ナムコオリジナル" }, { "NAMCO ORIGINAL", "ナムコオリジナル" }
    };

    // 💡 box.defのGENRE値 → Lumen/1.Enso/Genre 配下の土台画像ファイル名。
    private static readonly Dictionary<string, string> GenreFileMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "特集", "0.Other" }, { "ポップス", "1.Pops" }, { "J-POP", "1.Pops" }, { "POPS", "1.Pops" },
        { "キッズ", "2.Kids" }, { "どうよう", "2.Kids" }, { "アニメ", "3.Anime" }, { "ANIME", "3.Anime" },
        { "ボーカロイド", "4.Vocalo" }, { "ボーカロイド曲", "4.Vocalo" }, { "VOCALOID", "4.Vocalo" },
        { "ゲームミュージック", "5.Game" }, { "ゲームミュージック曲", "5.Game" }, { "バラエティ", "6.Variety" },
        { "VARIETY", "6.Variety" }, { "クラシック", "7.Classic" }, { "CLASSIC", "7.Classic" },
        { "ナムコオリジナル", "8.Namco" }, { "NAMCO ORIGINAL", "8.Namco" }
    };

    private static string _hudTitleWidthCacheKey = "";
    private static float _hudTitleWidthCacheFontSize = -1f;
    private static float _hudExactTitleWidthCache;

    public static bool IsPlaying => _playing;
    public static int Score => _score;

    public static int CeilingScore { get; private set; }
    public static int PerfectMaxScore { get; private set; }
    // 💡 2P対応: EnsoP2が同じ「1音あたりの点数」を使えるように公開する。
    public static int ScorePerNoteValue => _scorePerNote;
    public static int Combo => _combo;
    public static int MaxCombo => _maxCombo;
    public static int Perfect => _perfect;
    public static int Good => _good;
    public static int Bad => _bad;
    public static int Miss => _miss;
    /// <summary>1PのAUTO状態。2P専用AIレベルはこの状態へ影響しない。</summary>
    public static bool IsAuto => Auto;

    public static int HitCountThisSong => _perfect + _good + _bad;
    public static int RollThisSong => _totalRollHitsThisSong;

    /// <summary>
    /// EnsoGayLane 用：現在の曲BPM、実効スクロール速度（BPM換算）、現在の小節番号／総小節数を返す。
    /// </summary>
    public static (double Bpm, double ScrollBpm, int Bar, int TotalBars) GetGayLaneStatus()
    {
        double nowSec = _nowTime / 1_000_000.0;
        double bpm = 0.0;
        double scroll = 1.0;
        int bar = 0;
        int totalBars = 0;

        int n = _chips.Length;
        for (int i = 0; i < n; i++)
        {
            var c = _chips[i];
            bool isMeasure = c._noteType == NoteType.Measure;
            if (isMeasure) totalBars++;

            double csec = TJA.ChipSec(c);
            if (csec <= nowSec)
            {
                bpm = c._bpm;
                scroll = c._scroll.Real;
                if (isMeasure) bar++;
            }
        }

        if (bpm <= 0.0 && n > 0)
        {
            bpm = _chips[0]._bpm;
            scroll = _chips[0]._scroll.Real;
        }

        return (bpm, bpm * scroll, bar, totalBars);
    }

    public static int DanTotalGreat => _danCumPerfect + _perfect;
    public static int DanTotalGood => _danCumGood + _good;
    public static int DanTotalBad => _danCumBad + _bad;
    public static int DanTotalMiss => _danCumMiss + _miss;
    public static int DanTotalRoll => _danCumRollHits + _totalRollHitsThisSong;
    public static int DanTotalHit => DanTotalGreat + DanTotalGood + DanTotalBad;

    public static double ActiveRollRemainingSeconds
    {
        get
        {
            if (_activeRoll == null || _activeRoll._rollEnd == null) return 0.0;
            double endSec = TJA.ChipSec(_activeRoll._rollEnd);
            double nowSec = _nowTime / 1_000_000.0;
            return Math.Max(0.0, endSec - nowSec);
        }
    }

    public static int RemainingSingleNoteCount
    {
        get
        {
            double nowSec = _nowTime / 1_000_000.0;
            int count = 0;
            for (int i = _firstActiveIndex; i < _chips.Length; i++)
            {
                var c = _chips[i];
                if (c._isHit) continue;
                bool isSingle = c._noteType == NoteType.Don || c._noteType == NoteType.Ka
                             || c._noteType == NoteType.DON || c._noteType == NoteType.KA;
                if (isSingle && TJA.ChipSec(c) >= nowSec - WIN_BAD) count++;
            }
            return count;
        }
    }

    public static double RemainingRollSeconds
    {
        get
        {
            double nowSec = _nowTime / 1_000_000.0;
            double total = 0.0;
            for (int i = _firstActiveIndex; i < _chips.Length; i++)
            {
                var c = _chips[i];
                bool isRollHead = c._noteType == NoteType.RollStart || c._noteType == NoteType.RollBigStart;
                if (!isRollHead || c._rollEnd == null) continue;
                double endSec = TJA.ChipSec(c._rollEnd);
                if (endSec <= nowSec) continue;
                double startSec = TJA.ChipSec(c);
                double effectiveStart = Math.Max(startSec, nowSec);
                double rollLength = endSec - effectiveStart;

                const double ROLL_MARGIN_SEC = 0.67;
                total += rollLength + ROLL_MARGIN_SEC;
            }
            return total;
        }
    }

    public static double SongProgress01
    {
        get
        {
            if (_lastChipSec <= 0) return 0.0;
            double nowSec = _nowTime / 1_000_000.0;
            return Math.Clamp(nowSec / _lastChipSec, 0.0, 1.0);
        }
    }

    public static void Init()
    {
        bool carryCombo = DanMode && !DanFirstSong;
        int savedCombo = _combo;
        int savedMaxCombo = _maxCombo;
        int savedScore = _score;

        if (DanMode && !DanFirstSong)
        {
            _danCumPerfect += _perfect;
            _danCumGood += _good;
            _danCumBad += _bad;
            _danCumMiss += _miss;
            _danCumRollHits += _totalRollHitsThisSong;
            DanSongIndex++;
        }
        else if (DanMode && DanFirstSong)
        {
            _danCumPerfect = _danCumGood = _danCumBad = _danCumMiss = _danCumRollHits = 0;
            DanSongIndex = 0;
        }

        // 💡 修正ポイント1: 演奏データを初期化する前に、ノーツ型を「あべこべ／ランダム」適用前の状態へ完全復元する
        RestoreNoteTypesForOptions();

        Reset();

        Auto = DiffSelectScene.AutoEnabled;

        if (carryCombo)
        {
            _combo = savedCombo;
            _maxCombo = savedMaxCombo;
            _score = savedScore;
        }

        VramProbe.Reset();

        if (!_laneAssetsLoaded)
        {
            _laneTex = Raylib.LoadTexture("Lumen/1.Enso/Lane/Lane.png");
            EnsoGayLane.Load();
            _hitTex = Raylib.LoadTexture("Lumen/1.Enso/Lane/h.png");
            _barTex = Raylib.LoadTexture("Lumen/1.Enso/Lane/s.png");
            _laneFlashDonTex = Raylib.LoadTexture("Lumen/1.Enso/Lane/l/don.png");
            _laneFlashKaTex = Raylib.LoadTexture("Lumen/1.Enso/Lane/l/katus.png");
            _laneFlashRollTex = Raylib.LoadTexture("Lumen/1.Enso/Lane/l/roll.png");
            _laneGogoTex = Raylib.LoadTexture("Lumen/1.Enso/Lane/l/gogo.png");
            _laneAssetsLoaded = true;
        }

        AudioEngine.Init();

        if (!_soundsLoaded)
        {
            string drumDir = Path.Combine(AppContext.BaseDirectory, "Lumen", "2.sound", "drum");
            _sndDon = SoundEffect.Load(Path.Combine(drumDir, "don.wav"));
            _sndKa = SoundEffect.Load(Path.Combine(drumDir, "ka.wav"));
            _sndBalloonPop = SoundEffect.Load(Path.Combine(AppContext.BaseDirectory, "Lumen", "2.sound", "SE", "balloon.wav"));
            _soundsLoaded = true;
        }

        DrumInput.DonSound = _sndDon;
        DrumInput.KaSound = _sndKa;

        SetMusicVolume(SettingsPanel.EffectiveBgmVolume);
        SetSEVolume(SettingsPanel.EffectiveSeVolume);

        EnsoEffect.Init();
        MiniTaiko.Init();
        if (DanMode)
        {
            MiniTaiko.SetDanConditions(new List<Exam.Condition>(DanSelectScene.SelectedConditions), DanSelectScene.SongPaths.Count);
            DanGaugeMeter.Init();
            DanGaugeMeter.SetConditions(new List<Exam.Condition>(DanSelectScene.SelectedConditions), DanSelectScene.SongPaths.Count);
        }
        DonChanEnso.Init();
        FlyingNotes.Init();
        Rainbow.Init();
        // 映像付き楽曲ではダンサーを使わない。初回だけテクスチャを読み込むことも避ける。
        if (!IsP2Active && !DanMode && !HasBgMovieForCurrentSong()) Dancer.Init();
        RendaPop.Init();
        ComboPop.Init();
        NamePlate.Init();

        // 💡 修正ポイント2: 背景アニメーション・スキンの初期化（再生成せずタイマー初期化）
        if (_haikeiUeSkin != null && _haikeiUeSkin.Ok && _haikeiLoadedForP2 == IsP2Active)
        {
            _haikeiTime = 0f;
            _haikeiUeSkin.CallInit();
            _haikeiShitaSkin?.CallInit();
            _haikeiShitaClearSkin?.CallInit();
        }
        else
        {
            // 前曲と参加人数が変わった場合は、下背景の読み込み元も切り替える。
            LoadHaikei();
        }

        // 💡 曲が変わるたびに毎回再計算する（以前はテクスチャ再読込のコスト回避用に
        //    _genreLoaded で一度きりにガードしていたが、文字列判定だけの今は不要かつ有害）。
        LoadGenreTex();

        if (!_hasBgMovie && _bgMovieOpenTask == null && !_bgMovieLoaded)
        {
            LoadBgMovie();
        }

        _bgMovieLastDrawnSec = -999.0;
        if (_hasBgMovie)
        {
            lock (_bgMovieTargetLock)
            {
                _bgMovieTargetSec = 0.0;
            }
        }

        // 1Pレイアウトでは魂ゲージ連動のMobを表示する。
        if (!IsP2Active) Mob.Init();

        if (!TJA.IsLoaded) return;

        // 💡 修正ポイント3: ノーツ配列(_chips)を毎回 new せず、TJAから正しく取得する
        if (TJA.Chips != null)
        {
            if (_chips == null || _chips.Length != TJA.Chips.Count)
            {
                _chips = new Chip[TJA.Chips.Count];
            }

            for (int i = 0; i < TJA.Chips.Count; i++)
            {
                _chips[i] = TJA.Chips[i];
                _chips[i]._isHit = false;
                _chips[i]._isFailed = false;
            }
        }
        else
        {
            _chips = Array.Empty<Chip>();
        }

        // 💡 修正ポイント4: 正しくリセットされたノーツに対して演奏オプションを再適用する
        ApplyPerformanceOptions();

        var layerSet = new SortedSet<int>();
        for (int i = 0; i < _chips.Length; i++)
        {
            layerSet.Add(_chips[i]._layer);
        }
        if (layerSet.Count == 0) layerSet.Add(0);
        _noteLayers = new int[layerSet.Count];
        layerSet.CopyTo(_noteLayers);

        int totalNotes = 0;
        for (int i = 0; i < _chips.Length; i++)
        {
            var t = _chips[i]._noteType;
            if (t == NoteType.Don || t == NoteType.Ka || t == NoteType.DON || t == NoteType.KA)
                totalNotes++;
        }

        double totalRollSeconds = 0.0;
        for (int i = 0; i < _chips.Length; i++)
        {
            var t = _chips[i]._noteType;
            bool isRollHead = t == NoteType.RollStart || t == NoteType.RollBigStart || t == NoteType.BalloonStart;
            if (isRollHead && _chips[i]._rollEnd != null)
            {
                double startSec = TJA.ChipSec(_chips[i]);
                double endSec = TJA.ChipSec(_chips[i]._rollEnd);
                if (endSec > startSec) totalRollSeconds += (endSec - startSec);
            }
        }

        const double PERFECT_ROLL_RATE = 16.6;
        const int ROLL_POINT_PER_HIT = 100;
        double estimatedRollScore = totalRollSeconds * PERFECT_ROLL_RATE * ROLL_POINT_PER_HIT;

        double rawScorePerNote = totalNotes > 0 ? (1000000.0 - estimatedRollScore) / totalNotes : 0;
        _scorePerNote = (int)(Math.Ceiling(Math.Max(0, rawScorePerNote) / 10.0) * 10.0);

        CeilingScore = (_scorePerNote * totalNotes) + (int)Math.Round(estimatedRollScore);
        PerfectMaxScore = CeilingScore;

        if (IsP2Active)
            EnsoP2.Init(_scorePerNote);
        else
            EnsoP2.Stop();

        int? danTotalOverride = DanMode ? DanTotalNotes : (int?)null;
        bool resetGauge = !DanMode || DanFirstSong;
        Gauge.Init(danTotalOverride, resetGauge);
        if (IsP2Active) Gauge.InitP2(resetGauge);

        // 💡 修正ポイント5: 音声再生を正しく巻き戻す
        // 前回ロードしたパスと今回の TJA.AudioPath が同じ曲の場合だけ Stop() で使い回し、
        // 曲が変わっていたら（F3等で選曲し直した場合を含め）必ず読み直す。
        // 以前は "_music != null && _musicLoaded" だけで判定していたため、
        // 前の曲の Music が生きたまま次の曲に来ると Stop() だけして再ロードされず、
        // 前の曲がそのまま鳴り続けるバグがあった。
        bool sameTrackAlreadyLoaded =
            _music != null && _musicLoaded &&
            !string.IsNullOrEmpty(_loadedAudioPath) &&
            string.Equals(_loadedAudioPath, TJA.AudioPath, StringComparison.OrdinalIgnoreCase);

        if (sameTrackAlreadyLoaded)
        {
            _music.Stop();
            ApplyMusicSpeed();
        }
        else if (!string.IsNullOrEmpty(TJA.AudioPath) && File.Exists(TJA.AudioPath))
        {
            _music?.Dispose();
            _music = MusicTrack.Load(TJA.AudioPath);
            _musicLoaded = _music.Loaded;
            _loadedAudioPath = _musicLoaded ? TJA.AudioPath : null;
            if (_musicLoaded) _music.Volume = SettingsPanel.EffectiveBgmVolume;
            ApplyMusicSpeed();
        }
        else
        {
            // 音源パスが無効/存在しない場合は、古い曲を鳴らし続けないよう確実に破棄する
            _music?.Dispose();
            _music = null;
            _musicLoaded = false;
            _loadedAudioPath = null;
        }
    }

    private static void ApplyPerformanceOptions()
    {
        bool abekobe = DiffSelectScene.AbekobeEnabled;
        int randomMode = DiffSelectScene.RandomMode;

        if (!abekobe && randomMode == 0)
        {
            _origNoteTypesForOptions = null;
            _origSeNoteTypesForOptions = null;
            return;
        }

        var rng = new Random();
        _origNoteTypesForOptions = new NoteType[_chips.Length];
        _origSeNoteTypesForOptions = new SeNoteType[_chips.Length];

        for (int i = 0; i < _chips.Length; i++)
        {
            NoteType origType = _chips[i]._noteType;
            _origNoteTypesForOptions[i] = origType;
            _origSeNoteTypesForOptions[i] = _chips[i]._seNoteType;

            bool isSmall = origType == NoteType.Don || origType == NoteType.Ka;
            bool isBig = origType == NoteType.DON || origType == NoteType.KA;
            if (!isSmall && !isBig) continue;

            NoteType newType = origType;

            if (abekobe)
            {
                newType = newType switch
                {
                    NoteType.Don => NoteType.Ka,
                    NoteType.Ka => NoteType.Don,
                    NoteType.DON => NoteType.KA,
                    NoteType.KA => NoteType.DON,
                    _ => newType,
                };
            }

            if (randomMode != 0)
            {
                double chance = randomMode == 1 ? 0.25 : 0.5;
                if (rng.NextDouble() < chance)
                {
                    bool nowBig = newType == NoteType.DON || newType == NoteType.KA;
                    bool nowKa = rng.Next(2) == 0;
                    newType = nowBig
                        ? (nowKa ? NoteType.KA : NoteType.DON)
                        : (nowKa ? NoteType.Ka : NoteType.Don);
                }
            }

            _chips[i]._noteType = newType;
            _chips[i]._seNoteType = SeNoteType.None;
        }

        SeNote.Assign(_chips);
    }

    private static void RestoreNoteTypesForOptions()
    {
        if (_origNoteTypesForOptions == null) return;
        int n = Math.Min(_chips.Length, _origNoteTypesForOptions.Length);
        for (int i = 0; i < n; i++)
        {
            _chips[i]._noteType = _origNoteTypesForOptions[i];
            if (_origSeNoteTypesForOptions != null && i < _origSeNoteTypesForOptions.Length)
                _chips[i]._seNoteType = _origSeNoteTypesForOptions[i];
        }
        _origNoteTypesForOptions = null;
        _origSeNoteTypesForOptions = null;
    }

    public static void Start()
    {
        if (!TJA.IsLoaded) return;
        _playing = true;

        _musicStarted = false;
        DrumInput.Clear();
        DrumInput.Enabled = true;
        if (IsP2Active)
            EnsoP2.Start();
        else
            EnsoP2.Stop();

        _endSequenceStartWall = -1.0;
        EndSequenceDone = false;
        _lastChipSec = 0;
        for (int i = 0; i < _chips.Length; i++)
        {
            if (_chips[i]._noteType == NoteType.Measure) continue;
            _lastChipSec = Math.Max(_lastChipSec, TJA.ChipSec(_chips[i]));
        }

        const double INTRO_DELAY_SEC = 2.0;
        double startSec = Math.Min(0.0, -TJA.Offset) - INTRO_DELAY_SEC;
        _nowTime = (long)(startSec * 1_000_000.0);
        _lastNowSec = startSec;
        _startWallClock = Now() - startSec;

        TryStartMusic(startSec);
    }

    private static void ApplyMusicSpeed()
    {
        if (_music == null || !_musicLoaded) return;
        float speed = DiffSelectScene.PlaybackSpeed;
        if (speed <= 0f) speed = 1.0f;

        ApplySpeedToTrack(_music, speed);
    }

    public static void ApplySpeedToTrack(object trackObj, float speed)
    {
        if (trackObj == null) return;

        try
        {
            var type = trackObj.GetType();
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (var propName in new[] { "Pitch", "Speed", "PlaybackSpeed", "Rate", "Tempo" })
            {
                var prop = type.GetProperty(propName, flags);
                if (prop != null && prop.CanWrite)
                {
                    try { prop.SetValue(trackObj, Convert.ChangeType(speed, prop.PropertyType)); } catch { }
                }
            }

            foreach (var methodName in new[] { "SetPitch", "SetSpeed", "SetPlaybackSpeed", "SetRate", "SetTempo" })
            {
                var method = type.GetMethod(methodName, flags);
                if (method != null && method.GetParameters().Length == 1)
                {
                    try
                    {
                        var paramType = method.GetParameters()[0].ParameterType;
                        method.Invoke(trackObj, new object[] { Convert.ChangeType(speed, paramType) });
                    }
                    catch { }
                }
            }

            foreach (var fieldName in new[] { "Pitch", "Speed", "PlaybackSpeed", "Rate", "Tempo", "_pitch", "_speed" })
            {
                var field = type.GetField(fieldName, flags);
                if (field != null)
                {
                    try { field.SetValue(trackObj, Convert.ChangeType(speed, field.FieldType)); } catch { }
                }
            }

            foreach (var field in type.GetFields(flags))
            {
                try
                {
                    var val = field.GetValue(trackObj);
                    if (val is Raylib_cs.Music m)
                    {
                        Raylib.SetMusicPitch(m, speed);
                    }
                }
                catch { }
            }

            foreach (var prop in type.GetProperties(flags))
            {
                try
                {
                    if (prop.CanRead && prop.GetIndexParameters().Length == 0)
                    {
                        var val = prop.GetValue(trackObj);
                        if (val is Raylib_cs.Music m)
                        {
                            Raylib.SetMusicPitch(m, speed);
                        }
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioSpeed] Error applying speed: {ex.Message}");
        }
    }

    private static void LoadHaikei()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Lumen", "1.Enso", "Haikei", "Normal");
        _haikeiTime = 0f;
        _haikeiMeta = new SongMeta { Title = TJA.Title };

        string ueDir = Path.Combine(root, "Ue");
        if (DanMode)
        {
            _haikeiUeSkin?.Dispose();
            _haikeiUeSkin = null;
            _haikeiUeAnim?.Dispose();
            _haikeiUeAnim = null;
            foreach (var anim in _haikeiUeDanAnims) anim?.Dispose();
            _haikeiUeDanAnims.Clear();

            string danUeDir = Path.Combine(root, "Dani", "Ue");
            for (int i = 0; i <= 5; i++)
            {
                string animPath = Path.Combine(danUeDir, i.ToString(), "Anime.aup2");
                if (File.Exists(animPath))
                {
                    Aup2Anim anim = Aup2Anim.Load(animPath);
                    if (anim != null) _haikeiUeDanAnims.Add(anim);
                }
            }
            _haikeiUeDanAnimTime = 0f;
        }
        else
        {
            foreach (var anim in _haikeiUeDanAnims) anim?.Dispose();
            _haikeiUeDanAnims.Clear();
            LoadHaikeiBg(ueDir, ref _haikeiUeSkin, ref _haikeiUeAnim);
            _haikeiUeAnimTime = 0f;
        }

        // 1P時は通常の下背景(Shita/Normal)、2P時は2レーン用に上背景を表示する。
        // 段位道場は専用の下背景を固定表示する。
        if (DanMode)
        {
            _haikeiShitaSkin?.Dispose();
            _haikeiShitaSkin = null;
            _haikeiShitaAnim?.Dispose();
            _haikeiShitaAnim = null;
        }
        else if (IsP2Active)
        {
            LoadHaikeiBg(ueDir, ref _haikeiShitaSkin, ref _haikeiShitaAnim);
        }
        else
        {
            string shitaDir = Path.Combine(root, "Shita", "Normal");
            LoadHaikeiBg(shitaDir, ref _haikeiShitaSkin, ref _haikeiShitaAnim);
        }
        _haikeiLoadedForP2 = IsP2Active;
        _haikeiShitaAnimTime = 0f;

        if (DanMode)
        {
            _haikeiShitaClearSkin?.Dispose();
            _haikeiShitaClearSkin = null;
            LoadShitaShitaFixed(Path.Combine(root, "Dani", "Shita", "Background.png"));
        }
        else
        {
            _haikeiShitaClearSkin = LoadHaikeiSkin(_haikeiShitaClearSkin, Path.Combine(root, "Shita", "Clear"));
            LoadShitaShita(Path.Combine(root, "Shita", "Shita"));
        }
    }

    /// <summary>
    /// dir直下のサブフォルダをランダムに1つ選び、katsu.lua があればLuaSkinとして、
    /// 無く Anime.aup2 があればAup2Animとしてフォールバック読み込みする。
    /// </summary>
    private static void LoadHaikeiBg(string dir, ref LuaSkin skinField, ref Aup2Anim animField)
    {
        animField?.Dispose();
        animField = null;

        if (!Directory.Exists(dir)) return;
        var dirs = Directory.GetDirectories(dir);
        if (dirs.Length == 0) return;

        string chosen = dirs[Random.Shared.Next(dirs.Length)];

        string luaPath = Path.Combine(chosen, "katsu.lua");
        if (File.Exists(luaPath))
        {
            skinField?.Dispose();
            skinField = new LuaSkin();
            skinField.Load(luaPath);
            skinField.CallInit();
            return;
        }

        string aup2Path = Path.Combine(chosen, "Anime.aup2");
        if (File.Exists(aup2Path))
        {
            skinField?.Dispose();
            skinField = null;
            animField = Aup2Anim.Load(aup2Path);
        }
    }

    private static void DrawHaikeiAnim(Aup2Anim anim, float animTime, float vx, float vy, float s)
    {
        if (anim == null) return;
        double dur = anim.Duration;
        float t = dur > 0 ? (float)(animTime % dur) : 0f;
        float w = anim.SceneWidth * s;
        float h = anim.SceneHeight * s;
        anim.Draw(vx, vy, w, h, t, Color.White);
    }

    private static void LoadBgMovie()
    {
        UnloadBgMovie();

        Console.WriteLine($"[BgMovie] TJA.BgMoviePath = '{TJA.BgMoviePath}'");

        if (string.IsNullOrEmpty(TJA.BgMoviePath))
        {
            Console.WriteLine("[BgMovie] BgMoviePathが空です");
            _hasBgMovie = false;
            return;
        }

        if (!File.Exists(TJA.BgMoviePath))
        {
            Console.WriteLine($"[BgMovie] ファイルが見つかりません: {TJA.BgMoviePath}");
            _hasBgMovie = false;
            return;
        }

        _bgMovieOpenPath = TJA.BgMoviePath;
        _bgMovieOpenTask = System.Threading.Tasks.Task.Run(() =>
        {
            var result = new BgMovieOpenResult();
            try
            {
                var file = MediaFile.Open(TJA.BgMoviePath,
                    new MediaOptions { VideoPixelFormat = ImagePixelFormat.Rgba32 });
                var firstFrame = file.Video.GetFrame(TimeSpan.Zero);

                result.File = file;
                result.VisibleWidth = firstFrame.ImageSize.Width;
                result.VisibleHeight = firstFrame.ImageSize.Height;
                result.StrideWidth = firstFrame.Stride / 4;
                result.FirstFrameData = new byte[firstFrame.Data.Length];
                firstFrame.Data.CopyTo(result.FirstFrameData);
                result.Ok = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BgMovie] 読み込み失敗: {ex}");
                result.Ok = false;
            }
            return result;
        });
    }

    private static void PollBgMovieOpen()
    {
        if (_bgMovieOpenTask == null || !_bgMovieOpenTask.IsCompleted) return;

        var task = _bgMovieOpenTask;
        _bgMovieOpenTask = null;

        if (task.IsFaulted || !task.Result.Ok)
        {
            _ = task.Exception;
            _hasBgMovie = false;
            return;
        }

        var r = task.Result;
        if (_bgMovieOpenPath != TJA.BgMoviePath)
        {
            r.File?.Dispose();
            return;
        }
        try
        {
            _bgMovieFile = r.File;

            var img = Raylib.GenImageColor(r.StrideWidth, r.VisibleHeight, Color.Black);
            _bgMovieTex = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);

            _bgMovieBuffers[0] = new byte[r.FirstFrameData!.Length];
            _bgMovieBuffers[1] = new byte[r.FirstFrameData.Length];
            r.FirstFrameData.CopyTo(_bgMovieBuffers[0], 0);
            _bgMovieWriteIndex = 1;
            _bgMovieReadIndex = 0;
            _bgMovieFrameReady = true;
            _bgMovieLoggedMismatch = false;
            _bgMovieLoggedException = false;

            _hasBgMovie = true;
            _bgMovieLoaded = true;
            _bgMovieLastDrawnSec = -999.0;
            _bgMovieSrcRect = new Rectangle(0, 0, r.VisibleWidth, r.VisibleHeight);
            Console.WriteLine($"[BgMovie] 読み込み成功: 表示={r.VisibleWidth}x{r.VisibleHeight} 実バッファ幅={r.StrideWidth}");

            _bgMovieTargetSec = -1.0;
            _bgMovieThreadRunning = true;
            _bgMovieThread = new Thread(BgMovieDecodeLoop) { IsBackground = true, Name = "BgMovieDecode", Priority = ThreadPriority.AboveNormal };
            _bgMovieThread.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BgMovie] テクスチャ生成失敗: {ex}");
            _hasBgMovie = false;
        }
    }

    private static void UnloadBgMovie()
    {
        if (_bgMovieOpenTask != null)
        {
            var pending = _bgMovieOpenTask;
            _bgMovieOpenTask = null;
            _ = pending.ContinueWith(t =>
            {
                if (!t.IsFaulted && t.Result.Ok) t.Result.File?.Dispose();
            });
        }

        if (_bgMovieThreadRunning)
        {
            _bgMovieThreadRunning = false;
            _bgMovieThread?.Join(500);
            _bgMovieThread = null;
        }

        if (_bgMovieLoaded)
        {
            Raylib.UnloadTexture(_bgMovieTex);
            _bgMovieFile?.Dispose();
            _bgMovieFile = null;
            _bgMovieLoaded = false;
        }
        _bgMovieBuffers[0] = null;
        _bgMovieBuffers[1] = null;
        _bgMovieReadIndex = -1;
        _bgMovieFrameReady = false;
        _hasBgMovie = false;
    }

    private static void BgMovieDecodeLoop()
    {
        double lastDecodedSec = -999.0;
        const double ResyncThreshold = 0.3;

        while (_bgMovieThreadRunning)
        {
            double targetSec;
            lock (_bgMovieTargetLock) { targetSec = _bgMovieTargetSec; }

            if (targetSec < 0)
            {
                Thread.Sleep(2);
                continue;
            }

            double diff = targetSec - lastDecodedSec;

            if (diff < 1.0 / 60.0 && diff > -ResyncThreshold)
            {
                Thread.Sleep(1);
                continue;
            }

            int idx = _bgMovieWriteIndex;
            var buf = _bgMovieBuffers[idx];
            if (buf == null) { Thread.Sleep(2); continue; }

            bool needsReseek = diff < 0 || diff > ResyncThreshold || lastDecodedSec < 0;

            try
            {
                if (needsReseek)
                {
                    var frame = _bgMovieFile.Video.GetFrame(TimeSpan.FromSeconds(targetSec));
                    if (frame.Data.Length == buf.Length)
                    {
                        frame.Data.CopyTo(buf);
                        CommitBgMovieFrame(idx);
                        lastDecodedSec = _bgMovieFile.Video.Position.TotalSeconds;
                    }
                    else
                    {
                        if (!_bgMovieLoggedMismatch)
                        {
                            _bgMovieLoggedMismatch = true;
                            Console.WriteLine($"[BgMovie] サイズ不一致: frame={frame.Data.Length} buffer={buf.Length}");
                        }
                        lastDecodedSec = targetSec;
                    }
                }
                else
                {
                    bool ok = _bgMovieFile.Video.TryGetNextFrame(buf);
                    if (ok)
                    {
                        CommitBgMovieFrame(idx);
                        lastDecodedSec = _bgMovieFile.Video.Position.TotalSeconds;
                    }
                    else
                    {
                        Thread.Sleep(2);
                        lastDecodedSec = targetSec;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_bgMovieLoggedException)
                {
                    _bgMovieLoggedException = true;
                    Console.WriteLine($"[BgMovie] デコード例外: {ex}");
                }
                lastDecodedSec = targetSec;
            }
        }
    }

    private static void CommitBgMovieFrame(int filledIndex)
    {
        lock (_bgMovieSwapLock)
        {
            _bgMovieReadIndex = filledIndex;
            _bgMovieWriteIndex = 1 - filledIndex;
            _bgMovieFrameReady = true;
        }
    }

    private static void UpdateBgMovie()
    {
        if (!_hasBgMovie) return;

        double nowSec = _nowTime / 1_000_000.0;
        double movieSec = nowSec + TJA.MovieOffset;
        if (movieSec < 0) return;

        lock (_bgMovieTargetLock) { _bgMovieTargetSec = movieSec; }

        if (!_bgMovieFrameReady) return;

        byte[] readyBuffer;
        lock (_bgMovieSwapLock)
        {
            if (!_bgMovieFrameReady) return;
            readyBuffer = _bgMovieBuffers[_bgMovieReadIndex];
            _bgMovieFrameReady = false;
        }

        if (readyBuffer == null) return;

        unsafe
        {
            fixed (byte* p = readyBuffer)
            {
                Raylib.UpdateTexture(_bgMovieTex, p);
            }
        }
        _bgMovieLastDrawnSec = movieSec;
    }

    private static void LoadGenreTex()
    {
        // 💡 ジャンルはTJA側の GENRE: ではなく、曲の実フォルダから親を遡って
        //    最初に見つかった box.def の GENRE: を参照する。
        //    (例: .../ESE/00 ポップス/003 曲名/曲名.tja → 一つ上の"00 ポップス"フォルダのbox.defを見る)
        string tjaPath = TJA.TjaPath ?? "";
        string genre = SongSelectScene.GetGenreFromNearestBoxDef(tjaPath);

        if (!GenreDisplayNameMap.TryGetValue(genre, out string display)) display = "特集";
        _genreDisplayText = display;
        _genreLoaded = true;

        // 💡 土台画像(Lumen/1.Enso/Genre配下)を読み込み、その上に上の文字を重ねて描画する。
        if (_genreTexLoaded)
        {
            Raylib.UnloadTexture(_genreTex);
            _genreTexLoaded = false;
        }

        if (!GenreFileMap.TryGetValue(genre, out string file)) file = "0.Other";
        string path = Path.Combine(AppContext.BaseDirectory, "Lumen", "1.Enso", "Genre", file + ".png");
        if (File.Exists(path))
        {
            _genreTex = Raylib.LoadTexture(path);
            _genreTexLoaded = _genreTex.Id != 0;
        }
    }

    private static void LoadShitaShita(string dir)
    {
        if (_haikeiShitaShitaLoaded)
        {
            Raylib.UnloadTexture(_haikeiShitaShitaTex);
            _haikeiShitaShitaLoaded = false;
        }

        if (!Directory.Exists(dir)) return;
        var pngs = Directory.GetFiles(dir, "*.png");
        if (pngs.Length == 0) return;

        _haikeiShitaShitaTex = Raylib.LoadTexture(pngs[Random.Shared.Next(pngs.Length)]);
        _haikeiShitaShitaLoaded = _haikeiShitaShitaTex.Id != 0;
    }

    private static void LoadShitaShitaFixed(string path)
    {
        if (_haikeiShitaShitaLoaded)
        {
            Raylib.UnloadTexture(_haikeiShitaShitaTex);
            _haikeiShitaShitaLoaded = false;
        }

        if (!File.Exists(path))
        {
            Console.WriteLine($"[Enso] 段位道場用の下背景が見つかりません: {path}");
            return;
        }

        _haikeiShitaShitaTex = Raylib.LoadTexture(path);
        _haikeiShitaShitaLoaded = _haikeiShitaShitaTex.Id != 0;
    }

    private static void UpdateHaikeiSkin(LuaSkin skin, float dt)
    {
        if (skin == null || !skin.Ok) return;
        skin.UpdateState(true, _haikeiTime,
            Program.VirtualWidth, Program.VirtualHeight, _haikeiMeta, Gauge.IsClear);
        skin.CallUpdate(dt);
    }

    private static LuaSkin LoadHaikeiSkin(LuaSkin skin, string dir)
    {
        if (!Directory.Exists(dir)) return skin;

        var dirs = Directory.GetDirectories(dir);
        if (dirs.Length == 0) return skin;

        string luaPath = Path.Combine(dirs[Random.Shared.Next(dirs.Length)], "katsu.lua");
        if (!File.Exists(luaPath)) return skin;

        skin?.Dispose();
        skin = new LuaSkin();
        skin.Load(luaPath);
        skin.CallInit();
        return skin;
    }

    private static double Now() => Raylib.GetTime();

    // 非同期の動画オープン完了前も含め、楽曲に有効な背景映像があるかを判定する。
    // _hasBgMovie は動画が実際に読み込まれた後にしかtrueにならないため、
    // ダンサーの初期化・更新・描画の切替にはこちらを使用する。
    private static bool HasBgMovieForCurrentSong() =>
        !string.IsNullOrWhiteSpace(TJA.BgMoviePath) && File.Exists(TJA.BgMoviePath);

    private static void TryStartMusic(double nowSec)
    {
        if (_musicStarted || !_musicLoaded || nowSec < 0) return;
        _music!.Play();
        ApplyMusicSpeed();
        _musicStarted = true;
    }

    public static void Stop()
    {
        _playing = false;
        DrumInput.Enabled = false;
        EnsoP2.Stop();
        if (_musicLoaded) _music!.Pause();
    }

    public static void Close()
    {
        Stop();
    }

    public static void Unload()
    {
        if (_musicLoaded) { _music!.Dispose(); _music = null; _musicLoaded = false; _loadedAudioPath = null; }

        _sndDon = null;
        _sndKa = null;
        _sndBalloonPop = null;
        _soundsLoaded = false;

        if (_laneAssetsLoaded)
        {
            if (_laneTex.Id != 0) Raylib.UnloadTexture(_laneTex);
            if (_hitTex.Id != 0) Raylib.UnloadTexture(_hitTex);
            if (_barTex.Id != 0) Raylib.UnloadTexture(_barTex);
            if (_laneFlashDonTex.Id != 0) Raylib.UnloadTexture(_laneFlashDonTex);
            if (_laneFlashKaTex.Id != 0) Raylib.UnloadTexture(_laneFlashKaTex);
            if (_laneFlashRollTex.Id != 0) Raylib.UnloadTexture(_laneFlashRollTex);
            if (_laneGogoTex.Id != 0) Raylib.UnloadTexture(_laneGogoTex);
            _laneFlashDonTime = _laneFlashKaTime = _laneFlashRollTime = -1.0;
            _gogoLaneStartSec = _gogoLaneEndSec = -1.0;
            _laneAssetsLoaded = false;
        }

        UnloadBgMovie();

        EnsoEffect.Unload();
        MiniTaiko.Unload();
        DanGaugeMeter.Unload();
        DonChanEnso.Unload();
        FlyingNotes.Unload();
        Rainbow.Unload();
        Dancer.Unload();
        RendaPop.Unload();
        ComboPop.Unload();
        NamePlate.Unload();
        Gauge.Unload();
        Mob.Unload();
        EnsoP2.Unload();

        _haikeiUeSkin?.Dispose();
        _haikeiUeSkin = null;
        _haikeiUeAnim?.Dispose();
        _haikeiUeAnim = null;
        foreach (var anim in _haikeiUeDanAnims) anim?.Dispose();
        _haikeiUeDanAnims.Clear();
        _haikeiShitaSkin?.Dispose();
        _haikeiShitaSkin = null;
        _haikeiShitaAnim?.Dispose();
        _haikeiShitaAnim = null;
        _haikeiShitaClearSkin?.Dispose();
        _haikeiShitaClearSkin = null;
        _balloonFailSkin?.Dispose();
        _balloonFailSkin = null;
        _balloonFailStartSec = -1.0;
        if (_haikeiShitaShitaLoaded)
        {
            Raylib.UnloadTexture(_haikeiShitaShitaTex);
            _haikeiShitaShitaLoaded = false;
        }
        _genreLoaded = false;
        _genreDisplayText = "";
        if (_genreTexLoaded)
        {
            Raylib.UnloadTexture(_genreTex);
            _genreTexLoaded = false;
        }
    }

    private static void PlayDonSound() => _sndDon?.Play();
    private static void PlayKaSound() => _sndKa?.Play();

    /// <summary>2P入力・AI演奏専用の太鼓SE。1P用の打鍵処理・演出経路を経由しない。</summary>
    internal static void PlayP2DrumSound(bool isDon)
    {
        if (isDon) _sndDon?.Play(); else _sndKa?.Play();
    }

    public static void SetMusicVolume(float vol)
    {
        if (_music != null && _musicLoaded)
            _music.Volume = vol;
    }

    public static void SetSEVolume(float vol)
    {
        if (_sndDon != null) _sndDon.Volume = vol;
        if (_sndKa != null) _sndKa.Volume = vol;
        if (_sndBalloonPop != null) _sndBalloonPop.Volume = vol;
    }

    public static void Update()
    {
        float hdt = Raylib.GetFrameTime();
        _haikeiTime += hdt;
        UpdateHaikeiSkin(_haikeiShitaSkin, hdt);
        UpdateHaikeiSkin(_haikeiShitaClearSkin, hdt);
        UpdateHaikeiSkin(_haikeiUeSkin, hdt);
        if (_haikeiUeAnim != null) _haikeiUeAnimTime += hdt;
        if (_haikeiUeDanAnims.Count > 0) _haikeiUeDanAnimTime += hdt;
        if (_haikeiShitaAnim != null) _haikeiShitaAnimTime += hdt;
        PollBgMovieOpen();
        UpdateBgMovie();

        Gauge.Update(hdt);

        if (!_playing) return;

        float speed = DiffSelectScene.PlaybackSpeed;
        if (speed <= 0f) speed = 1.0f;

        ApplyMusicSpeed();

        double nowSec;
        // Update冒頭で取得済みのhdtを使う。初回ロード直後などでhdtが数秒に
        // 跳ねた場合、その値を開始前2秒に加算すると初回だけ待機を飛ばしてしまう。
        // 音楽開始後は従来どおり実フレーム時間を使い、譜面と音声の同期を変えない。
        double frameDt = hdt * speed;
        if (!_musicStarted)
        {
            double maxIntroStep = MAX_INTRO_FRAME_STEP_SEC * speed;
            frameDt = Math.Min(frameDt, maxIntroStep);
        }

        _lastNowSec += frameDt;
        nowSec = _lastNowSec;
        _nowTime = (long)(nowSec * 1_000_000.0);

        if (!_musicStarted)
        {
            TryStartMusic(nowSec);
        }

        VramProbe.Tick(nowSec);

        if (_endSequenceStartWall < 0)
        {
            bool musicEnded = !_musicLoaded || (_musicStarted && _music!.Ended);
            if (musicEnded && nowSec > _lastChipSec + 1.0)
                _endSequenceStartWall = Now();
        }
        else if (Now() - _endSequenceStartWall >= END_FADE_SEC + END_WAIT_SEC)
        {
            EndSequenceDone = true;
        }

        UpdateGoGo();

        DonChanEnso.Update(nowSec, _isGoGo, Gauge.IsClear,
            _activeRoll != null && _activeRoll._noteType == NoteType.BalloonStart);

        EnsoEffect.Update(nowSec);
        FlyingNotes.Update(nowSec);
        Rainbow.Update(nowSec);

        if (!IsP2Active && !DanMode && !HasBgMovieForCurrentSong()) Dancer.Update(nowSec);

        if (Raylib.IsKeyPressed(KeyboardKey.F6)) Auto = !Auto;
        DrumInput.SoundEnabled = !Auto;

        if (Auto)
        {
            DrumInput.Clear();
            AutoPlay(nowSec);
        }
        else
        {
            long frameTicks = Stopwatch.GetTimestamp();
            if (DrumInput.TryDequeueRateLimited(out var hit))
            {
                double hitSec = nowSec - ((frameTicks - hit.Ticks) / (double)Stopwatch.Frequency) * speed;
                if (hitSec >= nowSec - 0.5)
                    HandleHit(hit.IsDon, hit.IsLeft, hitSec, nowSec);
            }
        }

        CheckRollEnd();
        CheckMiss();

        if (IsP2Active)
        {
            EnsoP2.Update(nowSec, speed, SettingsPanel.AILevel);
            DonChanEnso.UpdateP2(nowSec, _isGoGo, Gauge.IsP2Clear, EnsoP2.IsBalloonActive);
        }

        if (DanMode) DanGaugeMeter.Update(nowSec);
        if (!IsP2Active) Mob.Update((float)Gauge.Value, (float)hdt);
    }

    public static bool SuppressNotesDraw = false;

    public static void Draw()
    {
        var (vx, vy, vw, vh) = Songs.GetViewport();
        float s = vw / 1920f;
        double nowSec = _nowTime / 1_000_000.0;

        if (_hasBgMovie)
        {
            var movDest = new Rectangle(vx, vy, vw, vh);
            Raylib.DrawTexturePro(_bgMovieTex, _bgMovieSrcRect, movDest, Vector2.Zero, 0f, Color.White);

        }
        else
        {
            // 段位道場はNormal/Clearのスキンを使わず、固定の下背景を表示する。
            if (DanMode && _haikeiShitaShitaLoaded)
            {
                var danSrc = new Rectangle(0, 0, _haikeiShitaShitaTex.Width, _haikeiShitaShitaTex.Height);
                var danDest = new Rectangle(vx, vy + 540f * s,
                    _haikeiShitaShitaTex.Width * s, _haikeiShitaShitaTex.Height * s);
                Raylib.DrawTexturePro(_haikeiShitaShitaTex, danSrc, danDest, Vector2.Zero, 0f, Color.White);
            }
            else if (_haikeiShitaSkin != null && _haikeiShitaSkin.Ok)
                _haikeiShitaSkin.CallDraw();
            else if (_haikeiShitaAnim != null)
                DrawHaikeiAnim(_haikeiShitaAnim, _haikeiShitaAnimTime, vx, vy, s);

            if (!DanMode && Gauge.IsClear && _haikeiShitaClearSkin != null && _haikeiShitaClearSkin.Ok)
                _haikeiShitaClearSkin.CallDraw();

            if (DanMode && _haikeiUeDanAnims.Count > 0)
            {
                foreach (var anim in _haikeiUeDanAnims)
                    DrawHaikeiAnim(anim, _haikeiUeDanAnimTime, vx, vy, s);
            }
            else if (_haikeiUeSkin != null && _haikeiUeSkin.Ok)
                _haikeiUeSkin.CallDraw();
            else if (_haikeiUeAnim != null)
                DrawHaikeiAnim(_haikeiUeAnim, _haikeiUeAnimTime, vx, vy, s);


        }

        DrawLane(vx, vy, vw, vh, s);
        if (IsP2Active)
        {
            EnsoP2.DrawLane(vx, vy, s);

            // 2Pドンちゃん通常時は、フッターShita/Shitaとミニ太鼓関連の背面に置く。
            // 風船打鍵中・成功・失敗演出中だけは、末尾で最前面に描画し直す。
            if (!DonChanEnso.IsP2BalloonFrontmost)
                DonChanEnso.DrawP2(vx, vy, s);
        }

        // フッターはレーン描画後に重ねる。
        if (!_hasBgMovie && !DanMode && _haikeiShitaShitaLoaded)
        {
            var src = new Rectangle(0, 0, _haikeiShitaShitaTex.Width, _haikeiShitaShitaTex.Height);
            var dest = new Rectangle(vx, SHITA_SHITA_Y * s + vy,
                _haikeiShitaShitaTex.Width * s, _haikeiShitaShitaTex.Height * s);
            Raylib.DrawTexturePro(_haikeiShitaShitaTex, src, dest, Vector2.Zero, 0f, Color.White);
        }

        EnsoEffect.DrawFire(vx, vy, s, LANE_Y, HitX, nowSec, _isGoGo);
        if (IsP2Active)
            EnsoEffect.DrawFire(vx, vy, s, EnsoP2.LANE_Y, EnsoP2.HitX, nowSec, _isGoGo);

        // エフェクトインスタンスごとにレーン座標を保持しているため、ここで1P/2P分をまとめて背面描画する。
        EnsoEffect.DrawBehindNotes(vx, vy, s, LANE_Y, nowSec);

        if (!SuppressNotesDraw)
        {
            DrawNotes(vx, vy, vw, vh, s);
            if (IsP2Active) EnsoP2.DrawNotes(vx, vy, s, nowSec);
        }

        DrawBalloonUI(vx, vy, s, nowSec);
        if (IsP2Active) EnsoP2.DrawBalloonUI(vx, vy, s, nowSec);

        EnsoEffect.Draw(vx, vy, s, LANE_Y, HitX, nowSec, _isGoGo);

        Gauge.Draw(vx, vy, s, nowSec);
        if (IsP2Active) Gauge.DrawP2(vx, vy, s, nowSec);

        DrawHUD(vx, vy, vw, vh, s);

        MiniTaiko.DrawHaikei(vx, vy, s);
        if (IsP2Active) MiniTaiko.DrawHaikeiP2(vx, vy, s, EnsoP2.LANE_Y);
        MiniTaiko.DrawDanGauge(vx, vy, s);
        DanGaugeMeter.Draw(vx, vy, s);

        if (!IsP2Active && !DanMode && !HasBgMovieForCurrentSong()) Dancer.Draw(vx, vy, s, nowSec);
        if (!IsP2Active) Mob.Draw(vx, vy, s);

        Rainbow.Draw(vx, vy, s, nowSec);
        FlyingNotes.Draw(vx, vy, s, nowSec);
        if (IsP2Active) FlyingNotes.DrawP2(vx, vy, s, nowSec);

        bool balloonActive = _activeRoll != null &&
            (_activeRoll._noteType == NoteType.BalloonStart || _activeRoll._noteType == NoteType.Kusudama);
        if (balloonActive) RendaPop.ForceHide();
        if (IsP2Active && EnsoP2.IsBalloonActive) RendaPop.ForceHideP2();

        RendaPop.Draw(_rollHits, vx, vy, s, nowSec);
        if (IsP2Active) RendaPop.DrawP2(EnsoP2.RollHits, vx, vy, s, nowSec);
        ComboPop.Draw(vx, vy, s, nowSec);
        if (IsP2Active) ComboPop.DrawP2(vx, vy, s, nowSec);

        EnsoEffect.DrawLAnimation(vx, vy, s, LANE_Y, nowSec);

        EnsoEffect.DrawJudgeTexts(vx, vy, s, nowSec);

        MiniTaiko.Draw(vx, vy, s, LANE_Y, _score, _combo);
        if (IsP2Active) MiniTaiko.DrawP2(vx, vy, s, EnsoP2.LANE_Y, EnsoP2.Score, EnsoP2.Combo);

        // 指定レイヤー: ミニ太鼓本体の前面、スコア加算ポップアップの背面。
        DonChanEnso.Draw(vx, vy, s);
        MiniTaiko.DrawScorePopupOverlay(vx, vy, s);
        if (IsP2Active) MiniTaiko.DrawScorePopupOverlayP2(vx, vy, s, EnsoP2.LANE_Y);

        NamePlate.GlobalX = -35f;
        NamePlate.GlobalY = 450f;
        NamePlate.Draw(vx, vy, s);
        if (IsP2Active)
        {
            // 2PはNamePlate.P2GlobalX/Y/Scaleで下段レーンに合わせて個別調整できる。
            NamePlate.DrawP2(vx, vy, s);
        }

        MiniTaiko.DrawOptionIcons(vx, vy, s);

        // 2P風船中（打鍵・成功・失敗演出中）のどんちゃんだけは、すべてのUIより最前面に置く。
        if (IsP2Active && DonChanEnso.IsP2BalloonFrontmost)
            DonChanEnso.DrawP2(vx, vy, s);

        //EnsoGayLane.Draw(vx, vy, s);

        if (_endSequenceStartWall >= 0)
        {
            float t = (float)((Now() - _endSequenceStartWall) / END_FADE_SEC);
            byte a = (byte)(Math.Clamp(t, 0f, 1f) * 255f);
            Raylib.DrawRectangle(0, 0, Program.VirtualWidth, Program.VirtualHeight,
                new Color((byte)0, (byte)0, (byte)0, a));
        }
    }

    private static void DrawLane(float vx, float vy, float vw, float vh, float s)
    {
        if (_laneAssetsLoaded && _laneTex.Id != 0)
        {
            float lx = LANE_X * s + vx;
            float ly = LANE_TOP * s + vy;
            float w = _laneTex.Width * s;
            float h = _laneTex.Height * s;

            Rectangle sourceRect = new Rectangle(0, 0, _laneTex.Width, _laneTex.Height);
            Rectangle destRect = new Rectangle(lx, ly, w, h);

            Color laneColor = _hasBgMovie
                ? new Color((byte)255, (byte)255, (byte)255, (byte)140)
                : Color.White;
            Raylib.DrawTexturePro(_laneTex, sourceRect, destRect, Vector2.Zero, 0f, laneColor);
        }

        double nowSec = _nowTime / 1_000_000.0;

        float hx = HitX * s + vx;
        float hy = LANE_Y * s + vy;

        if (_laneAssetsLoaded && _hitTex.Id != 0)
        {
            float w = _hitTex.Width * s;
            float h = _hitTex.Height * s;

            Rectangle sourceRect = new Rectangle(0, 0, _hitTex.Width, _hitTex.Height);
            Rectangle destRect = new Rectangle(hx, hy, w, h);
            Vector2 origin = new Vector2(w / 2f, h / 2f);

            Raylib.DrawTexturePro(_hitTex, sourceRect, destRect, origin, 0f, Color.White);
        }
        else
        {
            Raylib.DrawCircleLines((int)hx, (int)hy, NOTE_R_BIG * s, COL_HITFRAME);
        }

        if (_laneAssetsLoaded && _laneGogoTex.Id != 0)
        {
            float gw = _laneGogoTex.Width * s;
            float gh = _laneGogoTex.Height * s;
            float gx = LANE_L_X * s + vx;
            float gyCenter = LANE_Y * s + vy;

            Rectangle src = new Rectangle(0, 0, _laneGogoTex.Width, _laneGogoTex.Height);

            if (_isGoGo && _gogoLaneStartSec >= 0)
            {
                double el = nowSec - _gogoLaneStartSec;
                float scaleY = (float)Math.Clamp(el / GOGO_LANE_GROW, 0.0, 1.0);
                float h2 = gh * scaleY;

                Rectangle dest = new Rectangle(gx, gyCenter, gw, h2);
                Raylib.DrawTexturePro(_laneGogoTex, src, dest, new Vector2(0f, h2 / 2f), 0f, Color.White);
            }
            else if (!_isGoGo && _gogoLaneEndSec >= 0)
            {
                double el = nowSec - _gogoLaneEndSec;
                if (el >= 0 && el < GOGO_LANE_FADE)
                {
                    byte alpha = (byte)(Math.Clamp(1.0 - el / GOGO_LANE_FADE, 0.0, 1.0) * 255);
                    Rectangle dest = new Rectangle(gx, gyCenter, gw, gh);
                    Raylib.DrawTexturePro(_laneGogoTex, src, dest, new Vector2(0f, gh / 2f), 0f,
                        new Color((byte)255, (byte)255, (byte)255, alpha));
                }
            }
        }

        DrawLaneFlash(_laneFlashDonTex, _laneFlashDonTime, nowSec, vx, vy, s);
        DrawLaneFlash(_laneFlashKaTex, _laneFlashKaTime, nowSec, vx, vy, s);
        DrawLaneFlash(_laneFlashRollTex, _laneFlashRollTime, nowSec, vx, vy, s);
    }

    private static void DrawLaneFlash(Texture2D tex, double hitSec, double nowSec, float vx, float vy, float s)
    {
        if (tex.Id == 0 || hitSec < 0) return;

        double elapsed = nowSec - hitSec;
        if (elapsed < 0 || elapsed >= LANE_FLASH_DURATION) return;

        double alphaRate = 1.0;
        if (elapsed >= LANE_FLASH_HOLD)
            alphaRate = Math.Clamp((LANE_FLASH_DURATION - elapsed) / LANE_FLASH_FADE, 0.0, 1.0);
        byte alpha = (byte)(alphaRate * LANE_FLASH_MAX_ALPHA * 255);

        float w = tex.Width * s;
        float h = tex.Height * s;

        Rectangle src = new Rectangle(0, 0, tex.Width, tex.Height);
        Rectangle dest = new Rectangle(LANE_L_X * s + vx, LANE_Y * s + vy - h / 2f, w, h);

        Raylib.DrawTexturePro(tex, src, dest, Vector2.Zero, 0f,
            new Color((byte)255, (byte)255, (byte)255, alpha));
    }

    private static void DrawNotes(float vx, float vy, float vw, float vh, float s)
    {
        double nowSec = _nowTime / 1_000_000.0;
        int chipCount = _chips.Length;

        int startIndex = Math.Max(0, _firstActiveIndex - 10);
        int endIndex = Math.Min(chipCount - 1, AdvanceLastVisibleIndex(nowSec));

        // ロールボディもX座標降順で描画し、速度差による重なりの前後関係を正しくする
        var rollDrawList = new System.Collections.Generic.List<(float noteX, float noteEndX, int i, int layer)>();
        for (int i = startIndex; i <= endIndex; i++)
        {
            var chip = _chips[i];
            bool isRollType = chip._noteType == NoteType.RollStart
                           || chip._noteType == NoteType.RollBigStart
                           || chip._noteType == NoteType.BalloonStart;
            if (!isRollType || chip._rollEnd == null) continue;

            double chipSec = TJA.ChipSec(chip);
            float noteX = chip._noteType == NoteType.BalloonStart
                ? BalloonNoteX(chip, nowSec, s, vx)
                : NoteX(chipSec, nowSec, chip._scroll.Real, chip._bpm, s, vx);
            double endSec = TJA.ChipSec(chip._rollEnd);
            float noteEndX = NoteX(endSec, nowSec, chip._scroll.Real, chip._bpm, s, vx);
            rollDrawList.Add((noteX, noteEndX, i, chip._layer));
        }
        rollDrawList.Sort((a, b) => b.noteX.CompareTo(a.noteX));

        foreach (int layer in _noteLayers)
            foreach (var (noteX, noteEndX, i, chipLayer) in rollDrawList)
            {
                if (chipLayer != layer) continue;
                var chip = _chips[i];
                float bandY = LANE_Y * s + vy;
                bool doron = DiffSelectScene.DoronEnabled;

                if (chip._noteType == NoteType.BalloonStart)
                {
                    if (chip._isHit) continue;
                    if (!doron && noteX >= vx - 400 * s && noteX <= vx + vw + 200 * s)
                        NotesTexture.DrawBalloonBody(new Vector2(noteX, bandY), s);
                }
                else if (noteEndX >= vx && noteX <= vx + vw)
                {
                    bool isBig = chip._noteType == NoteType.RollBigStart;

                    float startX = Math.Max(noteX, vx);
                    float width = Math.Max(0, noteEndX - startX);

                    if (!doron)
                    {
                        NotesTexture.DrawDrumrollBody(isBig, startX, width, bandY, s);
                        NotesTexture.DrawTail(isBig ? 6 : 5, new Vector2(noteEndX, bandY), s);
                    }
                    NotesTexture.DrawSeRollBody(isBig, noteX, noteEndX, SE_NOTE_Y * s + vy, s);
                    NotesTexture.DrawSeNote(chip._rollEnd._seNoteType, isBig,
                        new Vector2(noteEndX, SE_NOTE_Y * s + vy), s);
                }
            }

        for (int i = startIndex; i <= endIndex; i++)
        {
            var chip = _chips[i];
            if (chip._noteType != NoteType.Measure || !chip._isBarVisible) continue;
            double chipSec = TJA.ChipSec(chip);
            float bx = NoteX(chipSec, nowSec, chip._scroll.Real, chip._bpm, s, vx);
            if (bx < vx || bx > vx + vw) continue;

            if (_laneAssetsLoaded && _barTex.Id != 0)
            {
                float w = _barTex.Width * s;
                float h = _barTex.Height * s;

                Rectangle sourceRect = new Rectangle(0, 0, _barTex.Width, _barTex.Height);
                Rectangle destRect = new Rectangle(bx - w / 2f, BAR_Y * s + vy, w, h);

                Raylib.DrawTexturePro(_barTex, sourceRect, destRect, Vector2.Zero, 0f, Color.White);
            }
            else
            {
                Raylib.DrawLine(
                    (int)bx, (int)((LANE_Y - BAR_H / 2) * s + vy),
                    (int)bx, (int)((LANE_Y + BAR_H / 2) * s + vy),
                    COL_BAR);
            }
        }

        // ノーツヘッドは種類では分けず、現在のX座標で奥から手前へ描画する。
        // 速度差で単音と連打ヘッドが重なった場合も、画面上で手前にある音符が上に来る。
        var headDrawList = new System.Collections.Generic.List<(float nx, int i, int layer)>();

        for (int i = startIndex; i <= endIndex; i++)
        {
            var chip = _chips[i];
            if (!IsNote(chip)) continue;
            bool isRollHead = chip._noteType == NoteType.RollStart
                           || chip._noteType == NoteType.RollBigStart
                           || chip._noteType == NoteType.BalloonStart
                           || chip._noteType == NoteType.Kusudama;
            if (chip._isHit && !chip._isFailed && !isRollHead) continue;
            double chipSec = TJA.ChipSec(chip);
            float nx;
            if (chip._noteType == NoteType.BalloonStart)
            {
                if (chip._isHit) continue;
                nx = BalloonNoteX(chip, nowSec, s, vx);
            }
            else
            {
                nx = NoteX(chipSec, nowSec, chip._scroll.Real, chip._bpm, s, vx);
            }
            if (nx < vx - 200 * s || nx > vx + vw + 200 * s) continue;

            headDrawList.Add((nx, i, chip._layer));
        }

        // OpenTaikoと同じく、チャート順を逆から描画する。
        // スクロール速度が異なる音符ではX座標順が入れ替わるため、X座標ではなく
        // 元のチップ順で奥行きを決める。
        headDrawList.Sort((a, b) => b.i.CompareTo(a.i));

        // 右側にある音符を先に描き、左側にある音符を手前へ重ねる。
        foreach (var (nx, i, chipLayer) in headDrawList)
        {
            var chip = _chips[i];
            float ny = LANE_Y * s + vy;
            if (!DiffSelectScene.DoronEnabled)
                DrawNote(chip._noteType, nx, ny, s);
            NotesTexture.DrawSeNote(chip._seNoteType, chip._noteType == NoteType.RollBigStart,
                new Vector2(nx, SE_NOTE_Y * s + vy), s);
        }
    }

    private static void DrawBalloonUI(float vx, float vy, float s, double nowSec)
    {
        float bigX = (long)(nowSec * 1000.0 / 50.0) % 2 == 0 ? 650f : 654f;

        if (_activeRoll != null && _activeRoll._noteType == NoteType.BalloonStart)
        {
            int required = Math.Max(1, _activeRoll._playerBalloonCount);
            int remain = Math.Max(0, required - _rollHits);
            int frame = Math.Min(6, _rollHits * 7 / required);

            NotesTexture.DrawBalloonBubble(new Vector2(601f * s + vx, 51f * s + vy), s);
            NotesTexture.DrawBalloonNumber(remain, (601f + 268f / 2f) * s + vx, 107f * s + vy, s, BalloonNumberHeight());
            NotesTexture.DrawBalloonBig(frame, new Vector2(bigX * s + vx, 384f * s + vy), s);
        }
        else if (_balloonPopSec >= 0)
        {
            double elapsedMs = (nowSec - _balloonPopSec) * 1000.0;
            if (elapsedMs < 110.0)
            {
                byte alpha = (byte)(255.0 * (1.0 - elapsedMs / 110.0));
                NotesTexture.DrawBalloonBig(7, new Vector2(650f * s + vx, 384f * s + vy), s, alpha);
            }
        }

        if (_balloonFailStartSec >= 0 && _balloonFailSkin != null && _balloonFailSkin.Ok)
        {
            _balloonFailSkin.UpdateState(true, (float)(nowSec - _balloonFailStartSec),
                Program.VirtualWidth, Program.VirtualHeight, null);
            _balloonFailSkin.CallDraw();
            if (_balloonFailSkin.Finished) _balloonFailStartSec = -1.0;
        }
    }

    private static void DrawNote(NoteType type, float x, float y, float s)
    {
        Vector2 pos = new Vector2(x, y);
        switch (type)
        {
            case NoteType.Don: NotesTexture.DrawNote(1, pos, s); break;
            case NoteType.Ka: NotesTexture.DrawNote(2, pos, s); break;
            case NoteType.DON: NotesTexture.DrawNote(3, pos, s); break;
            case NoteType.KA: NotesTexture.DrawNote(4, pos, s); break;
            case NoteType.RollStart: NotesTexture.DrawNote(5, pos, s); break;
            case NoteType.RollBigStart: NotesTexture.DrawNote(6, pos, s); break;
            case NoteType.BalloonStart: NotesTexture.DrawNote(7, pos, s); break;
            case NoteType.Kusudama: NotesTexture.DrawNote(8, pos, s); break;
        }
    }

    private static void DrawHUD(float vx, float vy, float vw, float vh, float s)
    {
        if (!TJA.IsLoaded) return;

        const float TITLE_BOX_LEFT = 1590f;
        const float TITLE_BOX_RIGHT = 1813f;
        const float TITLE_BOX_WIDTH = TITLE_BOX_RIGHT - TITLE_BOX_LEFT;
        const float TITLE_CENTER_X = 1735f;

        string title = TJA.Title;
        float fontSize = 54 * s;
        float titleLetterSpacingEm = G.GetRecommendedTitleLetterSpacingEm(title);

        float exactTitleWidth;
        if (_hudTitleWidthCacheKey == title && _hudTitleWidthCacheFontSize == fontSize)
        {
            exactTitleWidth = _hudExactTitleWidthCache;
        }
        else
        {
            exactTitleWidth = G.MeasureTextWithOutline16Width(
                G.Font, title, fontSize, titleLetterSpacingEm);

            _hudTitleWidthCacheKey = title;
            _hudTitleWidthCacheFontSize = fontSize;
            _hudExactTitleWidthCache = exactTitleWidth;
        }

        const float TITLE_OUTLINE_THICKNESS = 7f;
        const float TITLE_RIGHT_EXTRA_MARGIN = -75f;
        float titleOutline = TITLE_OUTLINE_THICKNESS * (fontSize / 64f) + TITLE_RIGHT_EXTRA_MARGIN * s;

        float titleX = exactTitleWidth <= TITLE_BOX_WIDTH * s
            ? TITLE_CENTER_X * s + vx - exactTitleWidth / 2f
            : TITLE_BOX_RIGHT * s + vx - exactTitleWidth - titleOutline;

        float titleY = 38f * s + vy;

        G.DrawTextWithOutline16(G.Font, title,
            new Vector2(titleX, titleY),
            fontSize, Color.White, Color.Black, 7f, 0f, titleLetterSpacingEm);

        if (_genreTexLoaded)
        {
            var src = new Rectangle(0, 0, _genreTex.Width, _genreTex.Height);
            var dest = new Rectangle(GENRE_X * s + vx, GENRE_Y * s + vy,
                _genreTex.Width * s, _genreTex.Height * s);
            Raylib.DrawTexturePro(_genreTex, src, dest, Vector2.Zero, 0f, Color.White);
        }

        if (_genreLoaded && !string.IsNullOrEmpty(_genreDisplayText))
        {
            // 💡 土台画像(GENRE_X〜GENRE_Xの画像幅)の中央に、BoxDefの説明文と同じ G.FontSub で重ねて描画。
            float genreFontSize = GENRE_FONT_SIZE * s;
            float genreWidth = G.MeasureTextWithOutline16Width(G.FontSub, _genreDisplayText, genreFontSize);
            float baseCenterX = _genreTexLoaded
                ? (GENRE_X + _genreTex.Width / 2f)
                : GENRE_X;
            float baseCenterY = _genreTexLoaded
                ? (GENRE_Y + _genreTex.Height / 2f)
                : GENRE_Y;
            float genreX = baseCenterX * s + vx - genreWidth / 2f;
            float genreY = baseCenterY * s + vy - genreFontSize / 2f;

            G.DrawTextWithOutline16(
                G.FontSub,
                _genreDisplayText,
                new Vector2(genreX, genreY),
                genreFontSize,
                Color.White,
                Color.Black,
                GENRE_OUTLINE_THICKNESS
            );
        }

        if (!TJA.IsLoaded && !string.IsNullOrEmpty(TJA.LoadError))
        {
            G.DrawTextWithOutline16(G.Font, $"読み込みエラー: {TJA.LoadError}",
                new Vector2(vx + 20 * s, vy + 100 * s),
                32 * s, Color.Red, Color.Black);
        }
    }

    private static void HandleHit(bool isDon, bool isLeft, double hitSec, double nowSec)
    {
        bool balloonRim = _activeRoll != null && _activeRoll._noteType == NoteType.BalloonStart && !isDon;

        if (_activeRoll != null && !balloonRim)
        {
            _laneFlashRollTime = nowSec;
            _rollHits++;
            _totalRollHitsThisSong++;
            _rollNextHitSec = hitSec + ROLL_HIT_INTERVAL;

            int added = 100;
            // AC16 has no Go-Go multiplier
            // if (_isGoGo) added = (int)(added * 1.2);
            _score += added;

            bool isBig = _activeRoll._noteType == NoteType.RollBigStart;
            EnsoEffect.AddHitEffect(isBig ? EnsoEffectType.GoodBig : EnsoEffectType.Good, EnsoJudgeType.None, nowSec, HitX, isRoll: true);
            if (_activeRoll._noteType != NoteType.BalloonStart)
                FlyingNotes.Spawn(!isDon, isBig, nowSec);

            if (DrumInput.SoundEnabled)
            {
                if (isDon) PlayDonSound(); else PlayKaSound();
                MiniTaiko.TriggerSide(isDon, isLeft, isBig);
            }

            CountBalloonHit();
            return;
        }

        if (isDon) _laneFlashDonTime = nowSec;
        else _laneFlashKaTime = nowSec;

        if (DrumInput.SoundEnabled)
        {
            if (isDon) PlayDonSound(); else PlayKaSound();
            MiniTaiko.TriggerSide(isDon, isLeft, false);
        }

        JudgeNote(isDon ? 0 : 1, hitSec);
    }

    private static void AutoPlay(double nowSec)
    {
        if (_activeRoll != null && nowSec >= _rollNextHitSec)
        {
            _rollHits++;
            _totalRollHitsThisSong++;
            _rollNextHitSec = nowSec + ROLL_HIT_INTERVAL;

            int added = 100;
            // AC16 has no Go-Go multiplier
            // if (_isGoGo) added = (int)(added * 1.2);
            _score += added;

            bool isBig = _activeRoll._noteType == NoteType.RollBigStart;
            EnsoEffect.AddHitEffect(isBig ? EnsoEffectType.GoodBig : EnsoEffectType.Good, EnsoJudgeType.None, nowSec, HitX, isRoll: true);
            if (_activeRoll._noteType != NoteType.BalloonStart)
                FlyingNotes.Spawn(false, isBig, nowSec);
            PlayDonSound();
            MiniTaiko.TriggerDon(isBig);
            _laneFlashRollTime = nowSec;
            CountBalloonHit();
            return;
        }

        int chipCount = _chips.Length;

        while (true)
        {
            if (_activeRoll != null) break;

            int hitIndex = -1;
            for (int i = _firstActiveIndex; i < chipCount; i++)
            {
                var chip = _chips[i];
                if (!IsNote(chip) || chip._isHit) continue;
                if (chip._noteType == NoteType.RollEnd) continue;

                double chipSec = TJA.ChipSec(chip);
                if (chipSec > nowSec) break;

                hitIndex = i;
                break;
            }

            if (hitIndex < 0) break;

            var hitChip = _chips[hitIndex];
            double hitChipSec = TJA.ChipSec(hitChip);

            bool isKa = hitChip._noteType == NoteType.Ka || hitChip._noteType == NoteType.KA;
            if (isKa) PlayKaSound(); else PlayDonSound();
            // 大音符でも片手処理（both=false のまま交互打鍵扱い）
            if (isKa) MiniTaiko.TriggerKat(false); else MiniTaiko.TriggerDon(false);
            if (isKa) _laneFlashKaTime = nowSec; else _laneFlashDonTime = nowSec;

            JudgeNote(isKa ? 1 : 0, hitChipSec);
        }
    }



    private static void JudgeNote(int inputType, double inputSec)
    {
        double nowSec = inputSec;
        Chip? oldest = null;

        int chipCount = _chips.Length;
        for (int i = _firstActiveIndex; i < chipCount; i++)
        {
            var chip = _chips[i];
            if (!IsNote(chip) || chip._isHit) continue;
            if (chip._noteType == NoteType.RollEnd) continue;

            double chipSec = TJA.ChipSec(chip);

            if (chip._noteType == NoteType.BalloonStart && chip._rollEnd != null)
            {
                if (nowSec > TJA.ChipSec(chip._rollEnd)) continue;
                if (chipSec - nowSec > WIN_BAD) break;
            }
            else
            {
                if (nowSec - chipSec > WIN_BAD) continue;
                if (chipSec - nowSec > WIN_BAD) break;
            }

            bool chipIsDon = chip._noteType == NoteType.Don || chip._noteType == NoteType.DON;
            bool chipIsKa = chip._noteType == NoteType.Ka || chip._noteType == NoteType.KA;
            bool chipIsRoll = chip._noteType == NoteType.RollStart
                            || chip._noteType == NoteType.RollBigStart
                            || chip._noteType == NoteType.BalloonStart;

            bool matches;
            if (chipIsRoll)
                matches = chip._noteType != NoteType.BalloonStart || inputType == 0;
            else if (inputType == 0)
                matches = chipIsDon;
            else
                matches = chipIsKa;

            if (!matches) continue;

            oldest = chip;
            break;
        }

        if (oldest == null) return;

        bool isDonNote = oldest._noteType == NoteType.Don || oldest._noteType == NoteType.DON;
        bool isKaNote = oldest._noteType == NoteType.Ka || oldest._noteType == NoteType.KA;
        bool isRoll = oldest._noteType == NoteType.RollStart
                   || oldest._noteType == NoteType.RollBigStart
                   || oldest._noteType == NoteType.BalloonStart;

        oldest._isHit = true;

        double oldestSec = TJA.ChipSec(oldest);
        double diff = Math.Abs(oldestSec - nowSec);

        if (isRoll)
        {
            _activeRoll = oldest;
            _rollHits = 1;
            _totalRollHitsThisSong++;
            _rollNextHitSec = nowSec + ROLL_HIT_INTERVAL;

            int added = 100;
            // AC16 has no Go-Go multiplier
            // if (_isGoGo) added = (int)(added * 1.2);
            _score += added;

            _laneFlashRollTime = nowSec;

            bool isBigRoll = oldest._noteType == NoteType.RollBigStart;
            EnsoEffect.AddHitEffect(isBigRoll ? EnsoEffectType.GoodBig : EnsoEffectType.Good, EnsoJudgeType.None, nowSec, HitX, isRoll: true);
            if (oldest._noteType != NoteType.BalloonStart)
            {
                FlyingNotes.Spawn(false, isBigRoll, nowSec);
                RendaPop.Show(nowSec);
            }
            else
            {
                RendaPop.ForceHide();
            }
            CountBalloonHit();
            return;
        }

        bool isBigNote = oldest._noteType == NoteType.DON || oldest._noteType == NoteType.KA;

        if (diff <= WIN_PERFECT)
        {
            _perfect++;
            int added = _scorePerNote;
            _score += added;

            Gauge.AddGood(nowSec);
            EnsoEffect.AddHitEffect(isBigNote ? EnsoEffectType.GoodBig : EnsoEffectType.Good, EnsoJudgeType.Good, nowSec, HitX);
            FlyingNotes.Spawn(isKaNote, isBigNote, nowSec);
        }
        else if (diff <= WIN_GOOD)
        {
            _good++;
            // AC16 "Ok" score = (Initial Note Value / 2), then round up to the nearest 10
            int added = (int)(Math.Ceiling((_scorePerNote / 2.0) / 10.0) * 10.0);
            _score += added;

            Gauge.AddOk(nowSec);
            EnsoEffect.AddHitEffect(isBigNote ? EnsoEffectType.OkBig : EnsoEffectType.Ok, EnsoJudgeType.Ok, nowSec, HitX);
            FlyingNotes.Spawn(isKaNote, isBigNote, nowSec);
        }
        else
        {
            _bad++;
            Gauge.AddBad();
            EnsoEffect.AddHitEffect(EnsoEffectType.None, EnsoJudgeType.Bad, nowSec, HitX);
            ResetCombo();
            return;
        }

        _combo++;
        if (_combo > _maxCombo) _maxCombo = _combo;

        DonChanEnso.OnComboRecovered();
        DonChanEnso.OnComboMilestone(_combo, Gauge.IsClear);
        ComboPop.CheckMilestone(_combo, nowSec);
    }

    private static void CheckRollEnd()
    {
        if (_activeRoll == null) return;
        if (_activeRoll._rollEnd == null) { _activeRoll = null; return; }

        double endSec = TJA.ChipSec(_activeRoll._rollEnd);
        double nowSec = _nowTime / 1_000_000.0;
        if (nowSec >= endSec)
        {
            if (_activeRoll._noteType == NoteType.BalloonStart && !IsBalloonPopped(_activeRoll))
                StartBalloonFailAnim();
            _activeRoll._rollEnd._isHit = true;
            _activeRoll = null;
            RendaPop.Hide(nowSec);
        }
    }

    private static void CheckMiss()
    {
        double nowSec = _nowTime / 1_000_000.0;

        int chipCount = _chips.Length;
        for (int i = _firstActiveIndex; i < chipCount; i++)
        {
            var chip = _chips[i];
            if (!IsNote(chip) || chip._isHit) continue;
            if (chip._noteType == NoteType.RollEnd) continue;
            if (chip._noteType == NoteType.BalloonStart) continue;

            bool isRollType = chip._noteType == NoteType.RollStart
                           || chip._noteType == NoteType.RollBigStart
                           || chip._noteType == NoteType.Kusudama;

            double chipSec = TJA.ChipSec(chip);
            if (nowSec - chipSec > WIN_BAD)
            {
                if (isRollType)
                {
                    chip._isHit = true;
                    continue;
                }
                chip._isHit = true;
                chip._isFailed = true;
                _miss++;
                Gauge.AddMiss();
                ResetCombo();
            }
        }

        AdvanceFirstActiveIndex();
    }

    private static void AdvanceFirstActiveIndex()
    {
        int n = _chips.Length;
        while (_firstActiveIndex < n)
        {
            var c = _chips[_firstActiveIndex];
            if (IsNote(c) && !c._isHit) break;
            _firstActiveIndex++;
        }
    }

    private static void UpdateGoGo()
    {
        double nowSec = _nowTime / 1_000_000.0;
        bool nextGoGo = false;

        var events = TJA.GoGoEvents;
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].Sec > nowSec) break;
            nextGoGo = events[i].Start;
        }

        if (nextGoGo && !_isGoGo)
        {
            EnsoEffect.TriggerGoGoExplosion(nowSec);
            _gogoLaneStartSec = nowSec;
        }
        else if (!nextGoGo && _isGoGo)
        {
            _gogoLaneEndSec = nowSec;
        }

        _isGoGo = nextGoGo;
    }

    private static void ResetCombo()
    {
        _combo = 0;
        DonChanEnso.OnComboBreak();
        _activeRoll = null;
        RendaPop.Hide(_nowTime / 1_000_000.0);
    }

    private static bool IsNote(Chip chip) =>
        chip._noteType != NoteType.None && chip._noteType != NoteType.Measure;

    private static bool IsBalloonPopped(Chip chip) =>
        chip._noteType == NoteType.BalloonStart &&
        chip._playerRollCount >= Math.Max(1, chip._playerBalloonCount);

    private static float BalloonNumberHeight()
    {
        if (_balloonNumAnimStart < 0) return BALLOON_NUM_H;

        double t = Now() - _balloonNumAnimStart;
        if (t < BALLOON_NUM_SHRINK)
            return _balloonNumAnimFromH + (BALLOON_NUM_H_PEAK - _balloonNumAnimFromH) * (float)(t / BALLOON_NUM_SHRINK);
        if (t < BALLOON_NUM_SHRINK + BALLOON_NUM_RECOVER)
            return BALLOON_NUM_H_PEAK + (BALLOON_NUM_H - BALLOON_NUM_H_PEAK) * (float)((t - BALLOON_NUM_SHRINK) / BALLOON_NUM_RECOVER);
        return BALLOON_NUM_H;
    }

    private static void StartBalloonFailAnim()
    {
        string luaPath = Path.Combine(AppContext.BaseDirectory,
            "Lumen", "1.Enso", "notes", "balloon", "k", "Failed", "katsu.lua");
        if (!File.Exists(luaPath)) return;

        _balloonFailSkin?.Dispose();
        _balloonFailSkin = new LuaSkin();
        _balloonFailSkin.Load(luaPath);
        _balloonFailSkin.CallInit();
        _balloonFailStartSec = _balloonFailSkin.Ok ? _nowTime / 1_000_000.0 : -1.0;
        DonChanEnso.OnBalloonMiss();
    }

    private static void CountBalloonHit()
    {
        if (_activeRoll == null || _activeRoll._noteType != NoteType.BalloonStart) return;

        _balloonNumAnimFromH = BalloonNumberHeight();
        _balloonNumAnimStart = Now();

        _activeRoll._playerRollCount = _rollHits;
        if (IsBalloonPopped(_activeRoll))
        {
            if (_activeRoll._rollEnd != null) _activeRoll._rollEnd._isHit = true;
            _activeRoll = null;
            _balloonPopSec = _nowTime / 1_000_000.0;
            FlyingNotes.Spawn(false, true, _balloonPopSec, isBalloon: true);
            Rainbow.Spawn(_balloonPopSec);
            _sndBalloonPop?.Play();
            DonChanEnso.OnBalloonBroke();
        }
    }

    private static float NoteX(double noteSec, double nowSec, double scroll, double bpm, float s, float vx)
    {
        double delta = noteSec - nowSec;

        int bpmFix = DiffSelectScene.BpmFixValue;
        if (bpmFix > 0)
        {
            bpm = bpmFix;
            scroll = 1.0;
        }

        double speed = (LANE_RIGHT - HitX) * bpm / 240.0;
        speed *= DiffSelectScene.SpeedMultiplier;
        return (float)(HitX * s + vx + delta * speed * scroll * s);
    }

    private const float BALLOON_STOP_OFFSET = 12f;

    private static float BalloonNoteX(Chip chip, double nowSec, float s, float vx)
    {
        double headSec = TJA.ChipSec(chip);
        double endSec = chip._rollEnd != null ? Math.Max(headSec, TJA.ChipSec(chip._rollEnd)) : headSec;
        float stopX = (HitX + BALLOON_STOP_OFFSET) * s + vx;

        if (nowSec <= endSec)
            return Math.Max(NoteX(headSec, nowSec, chip._scroll.Real, chip._bpm, s, vx), stopX);

        return NoteX(endSec, nowSec, chip._scroll.Real, chip._bpm, s, vx) + BALLOON_STOP_OFFSET * s;
    }

    private static void Reset()
    {
        RestoreNoteTypesForOptions();

        _nowTime = 0;
        _lastNowSec = 0;
        _playing = false;
        _perfect = 0;
        _good = 0;
        _bad = 0;
        _miss = 0;
        _combo = 0;
        _maxCombo = 0;
        _score = 0;
        _scorePerNote = 0;
        _isGoGo = false;
        _activeRoll = null;
        _totalRollHitsThisSong = 0;
        _chips = Array.Empty<Chip>();
        _firstActiveIndex = 0;
        _gogoSearchIndex = 0;
        _lastVisibleIndex = -1;
        _endSequenceStartWall = -1.0;
        EndSequenceDone = false;
        _balloonPopSec = -1.0;
        _balloonNumAnimStart = -1.0;
        _balloonNumAnimFromH = BALLOON_NUM_H;
        _balloonFailStartSec = -1.0;

        _hudTitleWidthCacheKey = "";
        _hudTitleWidthCacheFontSize = -1f;
    }
}
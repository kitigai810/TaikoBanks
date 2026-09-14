using Raylib_cs;
using System.Numerics;
using System.IO;
using System.Collections.Generic;
using System;
using System.Text.Json;

// ==================================================================
// リザルト画面(単曲プレイ後の成績表示)関連ロジック。
// 元々 Program.cs に直書きされていたものを移植したファイル。
// ==================================================================
public static class ResultScene
{
    // リザルト専用再生アセット
    public static MusicTrack _musicResult = null!;
    public static bool _musicResultLoaded = false;

    // リザルト画面でレンダリングするプレイヤーの成績データ
    public static int _resultScore = 0;
    static int _resultPerfect = 0;
    static int _resultGood = 0;
    static int _resultBad = 0;
    static int _resultMiss = 0;       // 追加：不可(Bad)とは別に、ミス(空振り)数を保持する
    static int _resultMaxCombo = 0;
    static int _resultRollCount = 0;
    public static int _resultGaugePercent = 0;
    static int _resultClearType = -1; // -1=未クリア, 0=クリア, 1=フルコンボ, 2=全良(Missも含めて判定)

    // ==================================================================
    // 💡 曲＋難易度ごとの自己ベスト記録の永続化(スコア/ランク/クラウン/プレイ回数)。
    //    Lumen/Scores/scores.json にJSONで保存し、リザルト画面でロードして比較・表示する。
    // ==================================================================
    private static string GetScoreRecordPath(string tjaPath)
    {
        string dir = Path.GetDirectoryName(tjaPath) ?? "Lumen/Scores";
        // 難易度ごとにファイルを分ける (例: scores_Oni.json)
        string difficulty = string.IsNullOrEmpty(Program._rpcDifficulty) ? "Unknown" : Program._rpcDifficulty;
        return Path.Combine(dir, $"scores_{difficulty}.json");
    }

    private class SongScoreRecord
    {
        public int BestScore { get; set; }
        public int BestRankIndex { get; set; } = -1;
        public int BestClearType { get; set; } = -1; // -1=未クリア, 0=クリア, 1=フルコンボ, 2=全良
        public int PlayCount { get; set; }
        // 最新プレイの判定数
        public int LastPerfect { get; set; }
        public int LastGood { get; set; }
        public int LastBad { get; set; }
        public int LastMiss { get; set; }
        public int LastMaxCombo { get; set; }
        public int LastRollCount { get; set; }
    }

    private static Dictionary<string, SongScoreRecord> _scoreRecords = new();
    private static bool _isNewBestScore;
    private static SongScoreRecord _currentBestRecord;

    private static Texture2D bgTex;
    private static Texture2D bg0Tex, bg1Tex, bg2Tex;
    private static Texture2D mountain0Tex, mountain1Tex;
    private static Texture2D cloudTex;
    private static Texture2D shineTex;
    private static Texture2D[] hanabiTexs = new Texture2D[3];
    private static Texture2D panelTex;
    private static Texture2D headerTex;
    private static Texture2D scoreRankTex;
    private static bool scoreRankLoaded;
    private static Texture2D scoreNumberTex;   // Score_Number.png (0-9横並びスプライト)
    private static Texture2D judgeNumberTex;   // Judge_Number.png (0-9横並びスプライト)

    private static int _rankIndex = -1; // 0=白 1=銅 2=銀 3=金雅 4=桃雅 5=紫雅 6=極 (-1=表示なし)

    // アニメーション用タイマー群 (ミリ秒換算ベース)
    private static float commonCounter = 0f;
    private static float mountainClearIncounter = 0f;
    private static float shineCounter = 0f;
    private static float hanabiCounter = 0f;
    private static float mountainAppearValue = 0f;
    private static bool isInitialized = false;

    // ── リザルト演出タイムライン ──
    // BGM開始(commonCounter=0)を基点に、以下の順で演出が進む:
    //   0.5秒後: スコア表示 (ScoreDon.ogg)
    //   +1.0秒後: スコアランク表示 (ScoreRank.ogg)
    //   +0.25秒後: クラウン表示 (クリア時のみ / Crown_*.ogg)
    //   +2.0秒後: クリア背景・雲・山の演出に切り替わる (mountainAppearValueとして流用)
    private const float SCORE_APPEAR_MS = 1000f;
    private const float RANK_APPEAR_MS = SCORE_APPEAR_MS + 1000f;   // 1500ms
    private const float CROWN_APPEAR_MS = RANK_APPEAR_MS + 250f;    // 1750ms
    private const float MOUNTAIN_APPEAR_MS = CROWN_APPEAR_MS + 1500f; // 3750ms

    // スコア表示 (ScoreDon.ogg) ※NAudio(AudioEngine)経由のSoundEffectに統一
    public static SoundEffect scoreDonSound = null!;
    private static bool _scoreDonPlayed;

    // スコアランク出現演出 (CActResultParameterPanel.cs の Score rank apparition を移植)
    // ctFlash_Icon 相当: 0〜3000msでループするフラッシュ用カウンター
    private static float ctFlashIcon = 0f;
    private static readonly float[] FlashTimes = { 1500, 1540, 1580, 1620, 1660, 1700, 1740, 1780 };
    // ScoreRank.ogg
    public static SoundEffect scoreRankSound = null!;
    private static bool _scoreRankSoundPlayed;

    // クラウン出現演出 (CActResultParameterPanel.cs の Crown apparition を移植)
    private static Texture2D crownTex;
    private static bool crownLoaded;
    private static readonly float[] CrownFlashTimes = { 2000, 2040, 2080, 2120, 2160, 2200, 2240, 2280 };
    // クリア種別ごとのクラウン効果音 (Crown_Clear / Crown_FullCombo / Crown_DondaFullCombo)
    public static SoundEffect crownClearSound = null!, crownFullComboSound = null!, crownDondaSound = null!;
    private static bool _crownSoundPlayed;

    // クリア/非クリア時の背景切り替えSE (Glad_1/Glad_2 はランダム、Sad は非クリア時)
    public static SoundEffect gladSound1 = null!, gladSound2 = null!, sadSound = null!;
    private static bool _gladSadSoundPlayed;
    private static readonly Random _gladSadRandom = new Random();

    // フキダシ (Speech_Balloon.png / 4コマ縦並び: 0=魂ゲージ, 1=クリア, 2=非クリア&ゲージ50%以上, 3=それ以外)
    private static Texture2D speechBalloonTex;
    private static bool speechBalloonLoaded;

    // フキダシ内テキスト (Speech_Balloon_Text.png / 縦4行×横3列: 行=ムード種別, 列=ランダム選択用バリエーション)
    private static Texture2D speechBalloonTextTex;
    private static bool speechBalloonTextLoaded;
    private static int _speechBalloonTextColumn;
    private static readonly Random _speechBalloonTextRandom = new Random();

    // 花びらアニメーション (Lumen/04.Result/Flower/Anime.aup2、Header.pngの下敷きとしてループ再生)
    private static Aup2Anim _flowerAnim;
    private static float _flowerAnimTime;
    private static bool _flowerAnimPlaying;

    private static readonly float[] cloudX = { 963, 918, 978, 1722, 1770, 168, 12, 1632, 1650, 48, 618 };
    private static readonly float[] cloudY = { 303, 636, 954, 795, 954, 954, 153, 78, 162, 489, 966 };
    private static readonly float[] cloudMove = { 225, 180, 270, 90, 135, 225, 180, 75, 67, 180, 270 };

    private static readonly float[][] shineX = {
        new float[] { 1327, 1882, 1087, 1335, 1737, 1710 }, // 1P通常(赤)側
        new float[] { 592, 37, 832, 585, 183, 210 }        // 2P(青)側ポジション
    };

    private static readonly float[][] shineY = {
        new float[] { 975, 607, 967, 630, 303, 877 },
        new float[] { 975, 607, 967, 630, 303, 877 }
    };

    private static readonly float[] shineSize = { 0.44f, 0.6f, 0.4f, 0.15f, 0.35f, 0.6f };

    private static readonly float[][] hanabiX = {
        new float[] { 1200, 1350, 1740 },
        new float[] { 720, 570, 180 }
    };

    private static readonly float[][] hanabiY = {
        new float[] { 652, 277, 390 },
        new float[] { 652, 277, 390 }
    };

    private static readonly float[] hanabiTimeStamp = { 1000f, 2000f, 3000f };

    // 状態フラグ
    private static bool p1IsBlue = false;
    public static bool isClear = false;

    /// <summary>
    /// アプリ終了時に一度だけ呼ぶ解放処理。
    /// </summary>
    public static void Shutdown()
    {
        if (!isInitialized) return;

        Raylib.UnloadTexture(bgTex);
        Raylib.UnloadTexture(bg0Tex);
        Raylib.UnloadTexture(bg1Tex);
        Raylib.UnloadTexture(bg2Tex);
        Raylib.UnloadTexture(mountain0Tex);
        Raylib.UnloadTexture(mountain1Tex);
        Raylib.UnloadTexture(cloudTex);
        Raylib.UnloadTexture(shineTex);
        Raylib.UnloadTexture(panelTex);
        Raylib.UnloadTexture(headerTex);
        if (scoreRankLoaded) Raylib.UnloadTexture(scoreRankTex);
        if (crownLoaded) Raylib.UnloadTexture(crownTex);
        if (speechBalloonLoaded) Raylib.UnloadTexture(speechBalloonTex);
        if (speechBalloonTextLoaded) Raylib.UnloadTexture(speechBalloonTextTex);
        _flowerAnim?.Dispose();
        _flowerAnim = null;
        Raylib.UnloadTexture(scoreNumberTex);
        Raylib.UnloadTexture(judgeNumberTex);
        for (int i = 0; i < 3; i++) Raylib.UnloadTexture(hanabiTexs[i]);

        isInitialized = false;
    }

    /// <summary>
    /// リザルト画面遷移時に一度だけ呼ぶ初期化関数
    /// </summary>
    public static void Init()
    {
        // 既に読み込み済みなら先に解放してメモリ/VRAMリークを防止
        if (isInitialized)
        {
            Raylib.UnloadTexture(bgTex);
            Raylib.UnloadTexture(bg0Tex);
            Raylib.UnloadTexture(bg1Tex);
            Raylib.UnloadTexture(bg2Tex);
            Raylib.UnloadTexture(mountain0Tex);
            Raylib.UnloadTexture(mountain1Tex);
            Raylib.UnloadTexture(cloudTex);
            Raylib.UnloadTexture(shineTex);
            Raylib.UnloadTexture(panelTex);
            Raylib.UnloadTexture(headerTex);
            if (scoreRankLoaded) Raylib.UnloadTexture(scoreRankTex);
            if (crownLoaded) Raylib.UnloadTexture(crownTex);
            if (speechBalloonLoaded) Raylib.UnloadTexture(speechBalloonTex);
            if (speechBalloonTextLoaded) Raylib.UnloadTexture(speechBalloonTextTex);
            _flowerAnim?.Dispose();
            _flowerAnim = null;
            // SoundEffect(NAudio)はメモリ常駐のfloat配列のみ保持しており、
            // GCに任せて問題ないため明示的なUnload処理は不要
            Raylib.UnloadTexture(scoreNumberTex);
            Raylib.UnloadTexture(judgeNumberTex);
            for (int i = 0; i < 3; i++) Raylib.UnloadTexture(hanabiTexs[i]);
        }

        // アセットの読み込み (Lumen/04.Result/ パス)
        bgTex = Raylib.LoadTexture("Lumen/04.Result/Background.png");
        bg0Tex = Raylib.LoadTexture("Lumen/04.Result/Background_0.png"); // 赤背景
        bg1Tex = Raylib.LoadTexture("Lumen/04.Result/Background_1.png"); // クリア後背景
        bg2Tex = Raylib.LoadTexture("Lumen/04.Result/Background_2.png"); // 青背景
        mountain0Tex = Raylib.LoadTexture("Lumen/04.Result/Background_Mountain_0.png");
        mountain1Tex = Raylib.LoadTexture("Lumen/04.Result/Background_Mountain_1.png");
        cloudTex = Raylib.LoadTexture("Lumen/04.Result/Cloud.png");
        shineTex = Raylib.LoadTexture("Lumen/04.Result/Shine.png");
        panelTex = Raylib.LoadTexture("Lumen/04.Result/Panel.png");
        headerTex = Raylib.LoadTexture("Lumen/04.Result/Header.png");
        scoreRankTex = Raylib.LoadTexture("Lumen/04.Result/ScoreRankEffect.png");
        scoreRankLoaded = scoreRankTex.Id != 0;
        scoreNumberTex = Raylib.LoadTexture("Lumen/04.Result/Score_Number.png");
        judgeNumberTex = Raylib.LoadTexture("Lumen/04.Result/Judge_Number.png");

        // ScoreDon.ogg / ScoreRank.ogg / Crown_*.ogg 読み込み (AudioEngine/NAudio経由)
        scoreDonSound = SoundEffect.Load("Lumen/2.sound/result/ScoreDon.ogg");
        scoreRankSound = SoundEffect.Load("Lumen/2.sound/result/ScoreRank.ogg");
        scoreDonSound.Volume = SettingsPanel.EffectiveSeVolume;
        scoreRankSound.Volume = SettingsPanel.EffectiveSeVolume;

        crownTex = Raylib.LoadTexture("Lumen/04.Result/CrownEffect.png");
        crownLoaded = crownTex.Id != 0;

        speechBalloonTex = Raylib.LoadTexture("Lumen/04.Result/Speech_Balloon.png");
        speechBalloonLoaded = speechBalloonTex.Id != 0;

        speechBalloonTextTex = Raylib.LoadTexture("Lumen/04.Result/Speech_Balloon_Text.png");
        speechBalloonTextLoaded = speechBalloonTextTex.Id != 0;
        _speechBalloonTextColumn = _speechBalloonTextRandom.Next(3); // 3列からランダムに1つ選択(リザルト表示ごとに一度だけ)

        _flowerAnim = Aup2Anim.Load("Lumen/04.Result/Flower/Anime.aup2");
        _flowerAnimTime = 0f;
        _flowerAnimPlaying = false;

        crownClearSound = SoundEffect.Load("Lumen/2.sound/result/Crown_Clear.ogg");
        crownFullComboSound = SoundEffect.Load("Lumen/2.sound/result/Crown_FullCombo.ogg");
        crownDondaSound = SoundEffect.Load("Lumen/2.sound/result/Crown_DondaFullCombo.ogg");
        crownClearSound.Volume = SettingsPanel.EffectiveSeVolume;
        crownFullComboSound.Volume = SettingsPanel.EffectiveSeVolume;
        crownDondaSound.Volume = SettingsPanel.EffectiveSeVolume;

        gladSound1 = SoundEffect.Load("Lumen/2.sound/result/SE/Glad_1.ogg");
        gladSound2 = SoundEffect.Load("Lumen/2.sound/result/SE/Glad_2.ogg");
        sadSound = SoundEffect.Load("Lumen/2.sound/result/SE/Sad.ogg");
        gladSound1.Volume = SettingsPanel.EffectiveSeVolume;
        gladSound2.Volume = SettingsPanel.EffectiveSeVolume;
        sadSound.Volume = SettingsPanel.EffectiveSeVolume;

        for (int i = 0; i < 3; i++)
        {
            hanabiTexs[i] = Raylib.LoadTexture($"Lumen/04.Result/Hanabi/{i}.png");
        }

        // スコア称号の判定
        if (_resultScore >= Enso.PerfectMaxScore) _rankIndex = 6;      // 極
        else if (_resultScore >= 950000) _rankIndex = 5;               // 紫雅
        else if (_resultScore >= 900000) _rankIndex = 4;               // 桃雅
        else if (_resultScore >= 800000) _rankIndex = 3;               // 金雅
        else if (_resultScore >= 700000) _rankIndex = 2;               // 銀
        else if (_resultScore >= 600000) _rankIndex = 1;               // 銅
        else if (_resultScore >= 500000) _rankIndex = 0;               // 白
        else _rankIndex = -1;                                          // 表示なし

        // カウンター初期化
        commonCounter = 0f;
        mountainClearIncounter = 0f;
        shineCounter = 0f;
        hanabiCounter = 0f;
        ctFlashIcon = 0f;
        _scoreDonPlayed = false;
        _scoreRankSoundPlayed = false;
        _crownSoundPlayed = false;
        _gladSadSoundPlayed = false;

        // 演出が始まる基準時間 (BGM開始から2秒後にクリア背景・雲・山演出へ切り替わる)
        mountainAppearValue = MOUNTAIN_APPEAR_MS;

        isInitialized = true;

        // 読み込み直後にVRAM詳細スナップショットへ反映
        Program.RefreshVramDetailSnapshot();
    }

    /// <summary>
    /// 毎フレームのタイマー更新処理
    /// </summary>
    private static void Update(float deltaTime)
    {
        if (!isInitialized) return;

        float dtMs = deltaTime * 1000f; // 秒からミリ秒へ変換

        commonCounter += dtMs;

        // ctFlash_Icon.TickLoop() 相当: 0〜3000msでループ
        ctFlashIcon += dtMs;
        if (ctFlashIcon >= 3000f) ctFlashIcon -= 3000f;

        if (commonCounter >= mountainAppearValue)
        {
            mountainClearIncounter += deltaTime * 333f;
            if (mountainClearIncounter > 515f) mountainClearIncounter = 515f;

            // クリア背景に切り替わる瞬間に一度だけ喜怒哀楽SEを再生
            if (!_gladSadSoundPlayed)
            {
                if (isClear)
                {
                    SoundEffect gladSound = (_gladSadRandom.Next(2) == 0) ? gladSound1 : gladSound2;
                    if (gladSound.Loaded) gladSound.Play();

                    // クリア時のみ、同じタイミングで花びらアニメーションを1回だけ再生開始
                    _flowerAnimPlaying = true;
                    _flowerAnimTime = 0f;
                }
                else
                {
                    if (sadSound.Loaded) sadSound.Play();
                }
                _gladSadSoundPlayed = true;
            }
        }

        shineCounter += dtMs;
        if (shineCounter > 1000f) shineCounter = 0f;

        hanabiCounter += dtMs;
        if (hanabiCounter > 4000f) hanabiCounter = 0f;

        // 花びらアニメーション(Anime.aup2): ループさせず、再生開始後は最後のフレームで停止
        if (_flowerAnimPlaying && _flowerAnim != null && _flowerAnim.Duration > 0)
        {
            _flowerAnimTime += deltaTime;
            if (_flowerAnimTime >= (float)_flowerAnim.Duration)
            {
                _flowerAnimTime = (float)_flowerAnim.Duration;
                _flowerAnimPlaying = false;
            }
        }
    }

    /// <summary>
    /// 曲＋難易度をキーにした自己ベストレコードをファイルからロードする。
    /// </summary>
    private static void LoadScoreRecords(string tjaPath)
    {
        _scoreRecords = new Dictionary<string, SongScoreRecord>();
        try
        {
            string path = GetScoreRecordPath(tjaPath);
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, SongScoreRecord>>(json);
                if (loaded != null) _scoreRecords = loaded;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Result] スコア記録の読み込みに失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// 今回の成績(_resultScore/_rankIndex/ClearType)を自己ベストと比較し、
    /// 更新があればファイルへ保存する。_isNewBestScore と _currentBestRecord を更新する。
    /// </summary>
    private static void SaveScoreRecord(int rankIndex, int clearType)
    {
        string tjaPath = Program._currentTjaPath;
        LoadScoreRecords(tjaPath);

        string key = TJA.Title;
        if (!_scoreRecords.TryGetValue(key, out var record))
        {
            record = new SongScoreRecord();
            _scoreRecords[key] = record;
        }

        _isNewBestScore = _resultScore > record.BestScore;
        if (_isNewBestScore)
        {
            record.BestScore = _resultScore;
            record.BestRankIndex = rankIndex;
        }
        if (clearType > record.BestClearType)
            record.BestClearType = clearType;

        record.PlayCount++;
        record.LastPerfect = _resultPerfect;
        record.LastGood = _resultGood;
        record.LastBad = _resultBad;
        record.LastMiss = _resultMiss;
        record.LastMaxCombo = _resultMaxCombo;
        record.LastRollCount = _resultRollCount;
        _currentBestRecord = record;

        try
        {
            string scorePath = GetScoreRecordPath(tjaPath);
            string dir = Path.GetDirectoryName(scorePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(_scoreRecords, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(scorePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Result] スコア記録の保存に失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// 演奏終了時の成績集計～リザルト画面遷移処理
    /// </summary>
    public static void Finish()
    {
        // 💡 以前はEnsoのプロパティ名を推測しながらリフレクションで拾っていたため
        //    (GetEnsoValueに渡していた候補名リストがEnsoの実際のプロパティ名と一致せず)、
        //    最大コンボが0のまま・連打数の欄がずれる、といった不具合が起きていた。
        //    Ensoの実プロパティを直接参照する形に統一して、この手の取り違えを根本的に無くす。
        _resultScore = Enso.Score;
        _resultPerfect = Enso.Perfect;
        _resultGood = Enso.Good;
        _resultBad = Enso.Bad;
        _resultMiss = Enso.Miss;
        _resultMaxCombo = Enso.MaxCombo;
        _resultRollCount = Enso.RollThisSong;

        isClear = Gauge.IsClear;
        _resultGaugePercent = (int)Math.Round(Gauge.Value);

        // 💡 以前はMissを一切見ておらず(Bad==0だけでフルコンボ/全良と判定していた)、
        //    ミス(空振り)があってもフルコンボ扱いになってしまうことがあった。Missも条件に含める。
        _resultClearType = !isClear ? -1
            : (_resultGood == 0 && _resultBad == 0 && _resultMiss == 0) ? 2   // 全良
            : (_resultBad == 0 && _resultMiss == 0) ? 1                       // フルコンボ
            : 0;                                                              // クリア

        // 【修正ポイント1】既存のリザルトBGMが読み込まれている場合は破棄してから新しく読み込み
        if (_musicResultLoaded && _musicResult != null)
        {
            _musicResult.Stop();
            _musicResult.Dispose();
            _musicResult = null!;
            _musicResultLoaded = false;
        }

        string resultBgmPath = Path.Combine(SongSelectScene.SkinRoot, "2.sound", "BGM", "Result.ogg");

        if (File.Exists(resultBgmPath))
        {
            _musicResult = MusicTrack.Load(resultBgmPath);
            _musicResultLoaded = _musicResult != null && _musicResult.Loaded;

            if (_musicResultLoaded)
            {
                _musicResult.Volume = SettingsPanel.EffectiveBgmVolume;
                _musicResult.Looping = true;
                _musicResult.Play();
            }
        }

        Init(); // ここで_rankIndexが確定する

        // 曲＋難易度ごとの自己ベストと比較し、更新分をLumen/Scores/scores.jsonへ保存する
        SaveScoreRecord(_rankIndex, _resultClearType);

        // 【修正ポイント2】DonChanSceneはMainの初期化時に一度呼んでいるため、ここでの重複Init()を排除してVRAM漏れを防止
        // （DonChanScene側にResetStateなどがあればそれを呼ぶようにしてください）

    }

    public static void Draw()
    {
        int sw = Program.VirtualWidth;
        int sh = Program.VirtualHeight;
        float scale = 1f;

        Update(Raylib.GetFrameTime());

        string donNeedle = (commonCounter < mountainAppearValue)
            ? "don_select_loop"
            : (isClear ? "don_result_full_loop" : "don_result_failure_loop");

        DonChanScene.Update(donNeedle);
        DonChanScene.X = 60f;
        DonChanScene.BottomY = 950f;
        DonChanScene.Width = 420f;
        DonChanScene.Draw(0, 0, scale);

        // ベース背景
        Raylib.DrawTexturePro(
            bgTex,
            new Rectangle(0, 0, bgTex.Width, bgTex.Height),
            new Rectangle(0, 0, sw, sh),
            Vector2.Zero,
            0f,
            Color.White
        );

        float gaugeAnimFactors = (commonCounter - mountainAppearValue) * 3f;
        byte bg1Alpha = (byte)Math.Clamp(isClear ? gaugeAnimFactors : 0f, 0f, 255f);

        float mountainScale = 1.0f;

        if (commonCounter >= mountainAppearValue && isClear)
        {
            if (mountainClearIncounter <= 90f)
                mountainScale = 1.0f - (float)Math.Sin(mountainClearIncounter * (Math.PI / 180.0)) * 0.18f;
            else if (mountainClearIncounter <= 225f)
                mountainScale = 0.82f + (float)Math.Sin((mountainClearIncounter - 90f) / 1.5f * (Math.PI / 180.0)) * 0.58f;
            else if (mountainClearIncounter <= 245f)
                mountainScale = 1.4f;
            else if (mountainClearIncounter <= 335f)
                mountainScale = 0.9f + (float)Math.Sin((mountainClearIncounter - 155f) * (Math.PI / 180.0)) * 0.5f;
            else if (mountainClearIncounter <= 515f)
                mountainScale = 0.9f + (float)Math.Sin((mountainClearIncounter - 335f) * (Math.PI / 180.0)) * 0.4f;
            else
                mountainScale = 0.9f;
        }

        Texture2D baseToneTex = p1IsBlue ? bg2Tex : bg0Tex;

        Raylib.DrawTexturePro(
            baseToneTex,
            new Rectangle(0, 0, baseToneTex.Width, baseToneTex.Height),
            new Rectangle(0, 0, sw, sh),
            Vector2.Zero,
            0f,
            Color.White
        );

        Raylib.DrawTexturePro(
            bg1Tex,
            new Rectangle(0, 0, bg1Tex.Width, bg1Tex.Height),
            new Rectangle(0, 0, sw, sh),
            Vector2.Zero,
            0f,
            new Color((byte)255, (byte)255, (byte)255, bg1Alpha)
        );

        float mountainOffsetY = -((mountainScale - 1.0f) * sh);
        Texture2D currentMtnTex = isClear && commonCounter >= mountainAppearValue ? mountain1Tex : mountain0Tex;

        Raylib.DrawTexturePro(
            currentMtnTex,
            new Rectangle(0, 0, currentMtnTex.Width, currentMtnTex.Height),
            new Rectangle(0, mountainOffsetY, sw, sh * mountainScale),
            Vector2.Zero,
            0f,
            Color.White
        );

        float cloudOpacity = Math.Clamp(commonCounter - mountainAppearValue, 0f, 255f);
        byte normalCloudAlpha = (byte)(255 - (isClear ? cloudOpacity : 0f));
        byte clearCloudAlpha = (byte)(isClear ? cloudOpacity : 0f);

        for (int i = 0; i < 11; i++)
        {
            float normalValue = (commonCounter % 10000f) / 10000f;
            float move = (cloudMove[i] * normalValue) * scale;

            float clearValue = ((commonCounter - mountainAppearValue) % 10000f) / 10000f;
            float clearMove = (cloudMove[i] * clearValue) * scale;

            Rectangle srcRectNormal = new Rectangle(1200f * i, 0f, 1200f, 360f);
            Rectangle srcRectClear = new Rectangle(1200f * i, 360f, 1200f, 360f);

            if (normalCloudAlpha > 0)
            {
                Vector2 pos = new Vector2(cloudX[i] * scale - move, cloudY[i] * scale);

                Raylib.DrawTexturePro(
                    cloudTex,
                    srcRectNormal,
                    new Rectangle(pos.X, pos.Y, 1200f * scale, 360f * scale),
                    new Vector2(600f * scale, 180f * scale),
                    0f,
                    new Color((byte)255, (byte)255, (byte)255, normalCloudAlpha)
                );
            }

            if (isClear && commonCounter >= mountainAppearValue && clearCloudAlpha > 0)
            {
                float sineAlpha = Math.Min((float)Math.Sin(clearValue * Math.PI) * 1000f, 255f);
                byte finalClearAlpha = (byte)Math.Clamp(cloudOpacity + (sineAlpha - 255f), 0f, 255f);

                Vector2 pos = new Vector2(cloudX[i] * scale - clearMove, cloudY[i] * scale);

                Raylib.DrawTexturePro(
                    cloudTex,
                    srcRectClear,
                    new Rectangle(pos.X, pos.Y, 1200f * scale, 360f * scale),
                    new Vector2(600f * scale, 180f * scale),
                    0f,
                    new Color((byte)255, (byte)255, (byte)255, finalClearAlpha)
                );
            }
        }

        if (commonCounter >= mountainAppearValue && isClear)
        {
            int sideIdx = p1IsBlue ? 1 : 0;
            float quadrant500 = shineCounter % 500f;

            for (int i = 0; i < 6; i++)
            {
                byte shineAlpha = 255;

                if ((i < 3 && shineCounter >= 500f) || (i >= 3 && shineCounter < 500f))
                {
                    shineAlpha = 0;
                }
                else if (quadrant500 < 100f)
                {
                    shineAlpha = (byte)((255f * quadrant500) / 100f);
                }
                else if (quadrant500 > 400f)
                {
                    shineAlpha = (byte)((255f * (500f - quadrant500)) / 100f);
                }

                if (shineAlpha > 0)
                {
                    float sz = shineSize[i] * scale;
                    Vector2 center = new Vector2(shineX[sideIdx][i] * scale, shineY[sideIdx][i] * scale);

                    Raylib.DrawTexturePro(
                        shineTex,
                        new Rectangle(0, 0, shineTex.Width, shineTex.Height),
                        new Rectangle(center.X, center.Y, shineTex.Width * sz, shineTex.Height * sz),
                        new Vector2((shineTex.Width * sz) / 2f, (shineTex.Height * sz) / 2f),
                        0f,
                        new Color((byte)255, (byte)255, (byte)255, shineAlpha)
                    );
                }
            }

            for (int i = 0; i < 3; i++)
            {
                float tmpTimer = 0f;
                byte hanabiAlpha = 0;
                float hanabiScaleFactor = 0.6f;

                if (commonCounter <= mountainAppearValue + 1000f)
                {
                    if (commonCounter <= mountainAppearValue + 255f)
                    {
                        tmpTimer = commonCounter - mountainAppearValue;
                        hanabiAlpha = (byte)Math.Clamp(tmpTimer, 0f, 255f);
                        hanabiScaleFactor *= (tmpTimer / 225f);
                    }
                    else
                    {
                        tmpTimer = Math.Max(0f, (2f * 255f) - (commonCounter - mountainAppearValue - 255f));
                        hanabiAlpha = (byte)Math.Clamp(tmpTimer, 0f, 255f);
                    }
                }
                else
                {
                    float tmpStamp = hanabiTimeStamp[i];

                    if (hanabiCounter <= tmpStamp + 255f)
                    {
                        tmpTimer = hanabiCounter - tmpStamp;
                        hanabiAlpha = (byte)Math.Clamp(tmpTimer, 0f, 255f);
                        hanabiScaleFactor *= (tmpTimer / 225f);
                    }
                    else
                    {
                        tmpTimer = Math.Max(0f, (2f * 255f) - (hanabiCounter - tmpStamp - 255f));
                        hanabiAlpha = (byte)Math.Clamp(tmpTimer / 2f, 0f, 255f);
                    }
                }

                if (hanabiAlpha > 0)
                {
                    Texture2D hTex = hanabiTexs[i];
                    Vector2 center = new Vector2(hanabiX[sideIdx][i] * scale, hanabiY[sideIdx][i] * scale);

                    float finalW = hTex.Width * hanabiScaleFactor * scale;
                    float finalH = hTex.Height * hanabiScaleFactor * scale;

                    Raylib.DrawTexturePro(
                        hTex,
                        new Rectangle(0, 0, hTex.Width, hTex.Height),
                        new Rectangle(center.X, center.Y, finalW, finalH),
                        new Vector2(finalW / 2f, finalH / 2f),
                        0f,
                        new Color((byte)255, (byte)255, (byte)255, hanabiAlpha)
                    );
                }
            }
        }

        // TODO: Header.pngの位置・サイズが合わない場合はここを調整してください(スキン座標系)
        const float HEADER_X = 0f;
        const float HEADER_Y = 0f;
        const float HEADER_W = 1920f;
        float headerDrawW = HEADER_W * scale;
        float headerDrawH = headerDrawW * ((float)headerTex.Height / headerTex.Width);
        Raylib.DrawTexturePro(
            headerTex,
            new Rectangle(0, 0, headerTex.Width, headerTex.Height),
            new Rectangle(HEADER_X * scale, HEADER_Y * scale, headerDrawW, headerDrawH),
            Vector2.Zero,
            0f,
            Color.White
        );

        float titleSize = 64f * scale;
        string songTitle = SongSelectScene.GetSelectedSongDisplayTitle();

        float titleLetterSpacingEm = G.GetRecommendedTitleLetterSpacingEm(songTitle);
        float titleMeasureW = G.MeasureTextWithOutline16Width(G.Font, songTitle, titleSize, titleLetterSpacingEm);
        float titleX = (sw - titleMeasureW) / 2f;
        float titleY = 40f * scale;

        G.DrawTextWithOutline16(
            G.Font, songTitle, new Vector2(titleX, titleY), titleSize,
            Color.White, Color.Black, 7f, 0f, titleLetterSpacingEm);

        float PANEL_X = 0f;
        float PANEL_Y = 0f;
        float SCORE_X = 475f;
        float SCORE_Y = 315f;
        float SCORE_SIZE = 80f;
        float NUM_X = 875f;
        float NUM_Y_START = 275;
        float NUM_Y_GAP = -92f;
        float NUM_SIZE = 48f;
        float SCORE_SPACING = -17.5f;   // ← スコア数字の文字間隔(px, マイナスで詰める)
        float NUM_SPACING = -10f;     // ← 判定数字の文字間隔(px, マイナスで詰める)
        float RANK_X = 175f;
        float RANK_Y = 500f;
        float RANK_BASE_W = 320f;

        float panelBaseX = PANEL_X * scale, panelBaseY = PANEL_Y * scale;
        float panelAspect = (float)panelTex.Width / panelTex.Height;
        float panelW = panelTex.Width * scale;
        float panelH = panelW / panelAspect;

        Rectangle panelRect = new Rectangle(panelBaseX, panelBaseY, panelW, panelH);

        Raylib.DrawTexturePro(
            panelTex,
            new Rectangle(0, 0, panelTex.Width, panelTex.Height),
            panelRect,
            Vector2.Zero,
            0f,
            Color.White
        );

        // ── スコア表示 (BGM開始から0.5秒後) ──
        // 判定数字・スコア数字は同時に出現するが、瞬間表示ではなく
        // CActResultParameterPanel.cs のポップイン/バウンドアニメを適用する
        if (commonCounter >= SCORE_APPEAR_MS)
        {
            const float RESULT_NUM_ADD_COUNT = 135f; // 判定数字のポップインアニメ時間 (1.3倍→1.0倍のSinカーブ)

            float itemY = panelBaseY + NUM_Y_START * scale;
            float spacing = (panelH / 7f) + NUM_Y_GAP * scale;
            float valX = panelBaseX + NUM_X * scale;
            float numFontSize = NUM_SIZE * scale;

            float judgePopScale = (commonCounter <= SCORE_APPEAR_MS + RESULT_NUM_ADD_COUNT)
                ? 1.3f - (float)Math.Sin((commonCounter - SCORE_APPEAR_MS) / (RESULT_NUM_ADD_COUNT / 90f) * (Math.PI / 180.0)) * 0.3f
                : 1.0f;

            DrawSpriteNumber(judgeNumberTex, $"{_resultPerfect}", valX, itemY, numFontSize, Color.White, NUM_SPACING * scale, judgePopScale);
            DrawSpriteNumber(judgeNumberTex, $"{_resultGood}", valX, itemY + spacing, numFontSize, Color.White, NUM_SPACING * scale, judgePopScale);
            DrawSpriteNumber(judgeNumberTex, $"{_resultBad}", valX, itemY + spacing * 2, numFontSize, Color.White, NUM_SPACING * scale, judgePopScale);
            DrawSpriteNumber(judgeNumberTex, $"{_resultRollCount}", valX, itemY + spacing * 3, numFontSize, Color.White, NUM_SPACING * scale, judgePopScale);
            DrawSpriteNumber(judgeNumberTex, $"{_resultMaxCombo}", valX, itemY + spacing * 4, numFontSize, Color.White, NUM_SPACING * scale, judgePopScale);

            // スコア数字: 1.0→1.65倍(270ms)→0.9倍(90ms)→1.0倍 のバウンドアニメで登場 (参照コード593〜596行目を移植)
            float scoreX = panelBaseX + SCORE_X * scale;
            float scoreY = panelBaseY + SCORE_Y * scale;

            float st = commonCounter - SCORE_APPEAR_MS;
            float scorePopScale =
                st <= 270f ? 1.0f + (float)Math.Sin(st / 1.5f * (Math.PI / 180.0)) * 0.65f
                : st <= 360f ? 1.0f - (float)Math.Sin((st - 270f) * (Math.PI / 180.0)) * 0.1f
                : 1.0f;

            DrawSpriteNumber(scoreNumberTex, $"{_resultScore}", scoreX, scoreY, SCORE_SIZE * scale, Color.White, SCORE_SPACING * scale, scorePopScale);

            if (!_scoreDonPlayed)
            {
                if (scoreDonSound.Loaded) scoreDonSound.Play();
                _scoreDonPlayed = true;
            }
        }

        // ── スコアランク出現演出 (CActResultParameterPanel.cs の Score rank apparition を移植) ──
        // BGM開始から RANK_APPEAR_MS (= スコア表示の1秒後) で表示開始。
        if (scoreRankLoaded && _rankIndex >= 0 && commonCounter >= RANK_APPEAR_MS)
        {
            const float RANK_COLS = 7f, RANK_ROWS = 4f;

            float cellW = scoreRankTex.Width / RANK_COLS;
            float cellH = scoreRankTex.Height / RANK_ROWS;

            float rankOpacity;
            float rankScaleX, rankScaleY;

            if (commonCounter <= RANK_APPEAR_MS + 180f)
            {
                rankOpacity = (commonCounter - RANK_APPEAR_MS) / 180.0f * 255.0f;
                rankScaleX = rankScaleY = 1.0f + (float)Math.Sin((commonCounter - (RANK_APPEAR_MS - 90f)) / 1.5f * (Math.PI / 180)) * 1.4f;
            }
            else if (commonCounter <= RANK_APPEAR_MS + 270f)
            {
                rankOpacity = 255f;
                rankScaleX = rankScaleY = 0.5f + (float)Math.Sin((commonCounter - (RANK_APPEAR_MS + 180f)) * (Math.PI / 180)) * 0.5f;
            }
            else
            {
                rankOpacity = 255f;
                rankScaleX = rankScaleY = 1f;
            }

            // ctFlash_Icon 相当のフラッシュ判定 (0〜3000msループの中の特定区間でフレームを切り替える)
            int currentFlash = 0;
            if ((ctFlashIcon >= FlashTimes[0] && ctFlashIcon <= FlashTimes[1]) || (ctFlashIcon >= FlashTimes[4] && ctFlashIcon <= FlashTimes[5]))
                currentFlash = 1;
            else if ((ctFlashIcon >= FlashTimes[1] && ctFlashIcon <= FlashTimes[2]) || (ctFlashIcon >= FlashTimes[5] && ctFlashIcon <= FlashTimes[6]))
                currentFlash = 2;
            else if ((ctFlashIcon >= FlashTimes[2] && ctFlashIcon <= FlashTimes[3]) || (ctFlashIcon >= FlashTimes[6] && ctFlashIcon <= FlashTimes[7]))
                currentFlash = 3;

            Rectangle rankSrc = new Rectangle(_rankIndex * cellW, currentFlash * cellH, cellW, cellH);

            float drawW = RANK_BASE_W * scale * rankScaleX;
            float drawH = (RANK_BASE_W * scale * rankScaleY) * (cellH / cellW);

            float cx = RANK_X * scale;
            float cy = RANK_Y * scale;

            Rectangle rankDest = new Rectangle(cx, cy, drawW, drawH);

            Raylib.DrawTexturePro(
                scoreRankTex,
                rankSrc,
                rankDest,
                new Vector2(drawW / 2f, drawH / 2f),
                0f,
                new Color((byte)255, (byte)255, (byte)255, (byte)Math.Clamp(rankOpacity, 0f, 255f))
            );

            // ScoreRank.ogg を一度だけ再生
            if (!_scoreRankSoundPlayed)
            {
                if (scoreRankSound.Loaded) scoreRankSound.Play();
                _scoreRankSoundPlayed = true;
            }
        }

        // ── クラウン出現演出 (CActResultParameterPanel.cs の Crown apparition を移植) ──
        // ClearType (0=クリア,1=フルコンボ,2=全良,-1=非表示) はFinishSongAndGoToResultで
        // Missも含めて判定済みの _resultClearType をそのまま使う。
        int ClearType = _resultClearType;

        // BGM開始から CROWN_APPEAR_MS (= スコアランク表示の0.25秒後) で表示開始。クリアしていない場合は表示しない。
        if (crownLoaded && ClearType >= 0 && commonCounter >= CROWN_APPEAR_MS)
        {
            const float CROWN_COLS = 3f, CROWN_ROWS = 4f; // 銀冠/金冠/虹冠 × フラッシュ4段階
            float CROWN_X = RANK_X + 225f; // ランク表示の右側に配置(実際のスキン座標に合わせて調整してください)
            float CROWN_Y = RANK_Y;
            float CROWN_BASE_W = RANK_BASE_W * 0.55f; // ランクの半分のサイズ(お好みで調整してください)

            float crownCellW = crownTex.Width / CROWN_COLS;
            float crownCellH = crownTex.Height / CROWN_ROWS;

            float crownOpacity;
            float crownScaleX, crownScaleY;

            if (commonCounter <= CROWN_APPEAR_MS + 180f)
            {
                crownOpacity = (commonCounter - CROWN_APPEAR_MS) / 180.0f * 255.0f;
                crownScaleX = crownScaleY = 1.0f + (float)Math.Sin((commonCounter - (CROWN_APPEAR_MS - 90f)) / 1.5f * (Math.PI / 180)) * 1.4f;
            }
            else if (commonCounter <= CROWN_APPEAR_MS + 270f)
            {
                crownOpacity = 255f;
                crownScaleX = crownScaleY = 0.5f + (float)Math.Sin((commonCounter - (CROWN_APPEAR_MS + 180f)) * (Math.PI / 180)) * 0.5f;
            }
            else
            {
                crownOpacity = 255f;
                crownScaleX = crownScaleY = 1f;
            }

            // ctFlash_Icon 相当のフラッシュ判定 (クラウンは2000〜2280msの区間を使用)
            int crownFlash = 0;
            if ((ctFlashIcon >= CrownFlashTimes[0] && ctFlashIcon <= CrownFlashTimes[1]) || (ctFlashIcon >= CrownFlashTimes[4] && ctFlashIcon <= CrownFlashTimes[5]))
                crownFlash = 1;
            else if ((ctFlashIcon >= CrownFlashTimes[1] && ctFlashIcon <= CrownFlashTimes[2]) || (ctFlashIcon >= CrownFlashTimes[5] && ctFlashIcon <= CrownFlashTimes[6]))
                crownFlash = 2;
            else if ((ctFlashIcon >= CrownFlashTimes[2] && ctFlashIcon <= CrownFlashTimes[3]) || (ctFlashIcon >= CrownFlashTimes[6] && ctFlashIcon <= CrownFlashTimes[7]))
                crownFlash = 3;

            Rectangle crownSrc = new Rectangle(ClearType * crownCellW, crownFlash * crownCellH, crownCellW, crownCellH);

            float crownDrawW = CROWN_BASE_W * scale * crownScaleX;
            float crownDrawH = (CROWN_BASE_W * scale * crownScaleY) * (crownCellH / crownCellW);

            float ccx = CROWN_X * scale;
            float ccy = CROWN_Y * scale;

            Rectangle crownDest = new Rectangle(ccx, ccy, crownDrawW, crownDrawH);

            Raylib.DrawTexturePro(
                crownTex,
                crownSrc,
                crownDest,
                new Vector2(crownDrawW / 2f, crownDrawH / 2f),
                0f,
                new Color((byte)255, (byte)255, (byte)255, (byte)Math.Clamp(crownOpacity, 0f, 255f))
            );

            // クリア種別ごとの効果音を一度だけ再生 (0=クリア, 1=フルコンボ, 2=全良)
            if (!_crownSoundPlayed)
            {
                if (ClearType == 2 && crownDondaSound.Loaded) crownDondaSound.Play();
                else if (ClearType == 1 && crownFullComboSound.Loaded) crownFullComboSound.Play();
                else if (ClearType == 0 && crownClearSound.Loaded) crownClearSound.Play();
                _crownSoundPlayed = true;
            }
        }


        // ── フキダシ (Speech_Balloon.png) 演出 ──
        // 4コマ縦並び (0=魂ゲージ全良, 1=クリア, 2=非クリア&ゲージ50%以上, 3=それ以外)
        // 表示タイミング・ポップインアニメは CActResultParameterPanel.cs の Speech bubble 演出を移植
        // (MountainAppearValue到達時=Glad/Sadの背景切り替えSEと同じ瞬間から開始)
        if (speechBalloonLoaded && commonCounter >= mountainAppearValue)
        {
            int balloonMoodIndex =
                (_resultGaugePercent >= 100) ? 0                              // 1: 魂ゲージ乗ってるとき(ゲージ100%)
                : isClear ? 1                                                  // 2: クリアしてるとき
                : (_resultGaugePercent >= 50) ? 2                              // 3: クリアゲージ50%以上 かつ 非クリア
                : 3;                                                           // 4: それ以外

            const float BALLOON_ROWS = 4f;
            const float BALLOON_ANIM_MS = 135f; // CActResultParameterPanel.cs の AddCount と同一値
            const float BALLOON_X = 650f; // TODO: 実際のスキン座標に合わせて調整してください
            const float BALLOON_Y = 775f; // TODO: 実際のスキン座標に合わせて調整してください
            const float BALLOON_BASE_W = 550f; // TODO: 実際の表示サイズに合わせて調整してください

            float balloonCellW = speechBalloonTex.Width;
            float balloonCellH = speechBalloonTex.Height / BALLOON_ROWS;

            // ポップインアニメ: 1.3倍から1.0倍へSinカーブで収束 (元コードと同一の式)
            float balloonScale = (commonCounter <= mountainAppearValue + BALLOON_ANIM_MS)
                ? 1.3f - (float)Math.Sin((commonCounter - mountainAppearValue) / (BALLOON_ANIM_MS / 90f) * (Math.PI / 180.0)) * 0.3f
                : 1.0f;

            Rectangle balloonSrc = new Rectangle(0, balloonMoodIndex * balloonCellH, balloonCellW, balloonCellH);

            float balloonDrawW = BALLOON_BASE_W * scale * balloonScale;
            float balloonDrawH = (BALLOON_BASE_W * scale * balloonScale) * (balloonCellH / balloonCellW);

            Rectangle balloonDest = new Rectangle(BALLOON_X * scale, BALLOON_Y * scale, balloonDrawW, balloonDrawH);

            Raylib.DrawTexturePro(
                speechBalloonTex,
                balloonSrc,
                balloonDest,
                new Vector2(balloonDrawW / 2f, balloonDrawH / 2f),
                0f,
                Color.White
            );

            // フキダシ内テキスト (Speech_Balloon_Text.png: 縦4行=ムード種別, 横3列=ランダムバリエーション)
            // 素材ごとに余白の比率が違うため、フキダシ本体のdestを基準に
            // オフセット・倍率だけ個別調整できるようにしてある(位置/大きさが合わない場合はここを調整)
            if (speechBalloonTextLoaded)
            {
                const float BALLOON_TEXT_COLS = 3f;

                // TODO: 吹き出し本体とテキストの見た目が合わない場合はここを調整してください
                const float BALLOON_TEXT_SCALE = 1.0f;   // フキダシ本体に対するテキストの拡大率
                const float BALLOON_TEXT_OFFSET_X = 0f;  // フキダシ中心からのXオフセット(スキン座標系)
                const float BALLOON_TEXT_OFFSET_Y = 0f;  // フキダシ中心からのYオフセット(スキン座標系)

                float balloonTextCellW = speechBalloonTextTex.Width / BALLOON_TEXT_COLS;
                float balloonTextCellH = speechBalloonTextTex.Height / BALLOON_ROWS;

                Rectangle balloonTextSrc = new Rectangle(
                    _speechBalloonTextColumn * balloonTextCellW,
                    balloonMoodIndex * balloonTextCellH,
                    balloonTextCellW,
                    balloonTextCellH);

                float balloonTextDrawW = balloonDrawW * BALLOON_TEXT_SCALE;
                float balloonTextDrawH = balloonDrawH * BALLOON_TEXT_SCALE;

                Rectangle balloonTextDest = new Rectangle(
                    (BALLOON_X + BALLOON_TEXT_OFFSET_X) * scale,
                    (BALLOON_Y + BALLOON_TEXT_OFFSET_Y) * scale,
                    balloonTextDrawW,
                    balloonTextDrawH);

                Raylib.DrawTexturePro(
                    speechBalloonTextTex,
                    balloonTextSrc,
                    balloonTextDest,
                    new Vector2(balloonTextDrawW / 2f, balloonTextDrawH / 2f),
                    0f,
                    Color.White
                );
            }
        }

        // 花びらアニメーション(Flower/Anime.aup2) — 最前面に描画。クリア時、Glad/Sadのタイミングで1回だけ再生(ループなし)
        // TODO: 位置・サイズはここを調整してください(スキン座標系、scaleは自動で掛かります)
        if (_flowerAnimPlaying && _flowerAnim != null)
        {
            const float FLOWER_X = 0f;
            const float FLOWER_Y = 650f;
            const float FLOWER_W = 550f;


            float flowerDrawW = FLOWER_W * scale;
            float flowerDrawH = flowerDrawW * (_flowerAnim.SceneHeight / _flowerAnim.SceneWidth);
            _flowerAnim.Draw(FLOWER_X * scale, FLOWER_Y * scale, flowerDrawW, flowerDrawH, _flowerAnimTime, Color.White);
        }
    }

    private static void DrawResultRow(string label, string value, float lx, float vx, float y, float size, Color valColor)
    {
        G.DrawTextWithOutline16(G.Font, label, new Vector2(lx, y), size, Color.White, Color.Black);

        Vector2 valSize = Raylib.MeasureTextEx(G.Font, value, size, 2);

        G.DrawTextWithOutline16(G.Font, value, new Vector2(vx - valSize.X, y), size, valColor, Color.Black);
    }

    /// <summary>
    /// 0-9が横一列に並んだスプライトシート(Score_Number.png / Judge_Number.png)を使って数値を描画する。
    /// rightX は右端揃えの基準X座標(DrawResultRowのvxと同じ使い方)。
    /// spacing は文字間の隙間(px, スケール後の値)。マイナス値で詰められる。
    /// </summary>
    private static void DrawSpriteNumber(Texture2D tex, string number, float rightX, float y, float digitHeight, Color tint, float spacing = 0f, float popScale = 1f)
    {
        if (tex.Id == 0 || string.IsNullOrEmpty(number)) return;

        float cellW = tex.Width / 10f;
        float cellH = tex.Height;
        float digitScale = digitHeight / cellH;
        float digitW = cellW * digitScale;
        float advance = digitW + spacing;

        float x = rightX - (number.Length * advance - spacing);

        for (int i = 0; i < number.Length; i++)
        {
            char c = number[i];
            if (c < '0' || c > '9') continue;
            int digit = c - '0';

            Rectangle src = new Rectangle(digit * cellW, 0, cellW, cellH);

            float baseX = x + i * advance;
            float baseY = y;
            float scaledW = digitW * popScale;
            float scaledH = digitHeight * popScale;
            // 拡縮の中心が元の矩形中心と一致するように位置を補正(ポップインアニメでも位置がズレない)
            float dstX = baseX - (scaledW - digitW) / 2f;
            float dstY = baseY - (scaledH - digitHeight) / 2f;

            Rectangle dst = new Rectangle(dstX, dstY, scaledW, scaledH);

            Raylib.DrawTexturePro(tex, src, dst, Vector2.Zero, 0f, tint);
        }
    }
}

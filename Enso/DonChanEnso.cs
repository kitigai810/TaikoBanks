using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Enso（演奏画面）で MiniTaiko の上に表示する「どんちゃん」演出。
/// Don/Export への事前PNG書き出しは使わず、DonChan3D の3Dモデルをその場でレンダリングして再生する。
///
/// DonChan3D.GetMappedFolderName() が判定に使っているアニメ名の手掛かり（"don_normal" 等）を
/// そのまま DonChan3D.SetAnimationByName() に渡して、対応するクリップを再生させる。
/// Enso側で状態を検出できないもの（songselect_*, dan_select_in, entry_loop, result_*, clear/clearin）は対象外。
/// </summary>
public static class DonChanEnso
{
    // ---- 表示位置・大きさ調整（1920x1080基準、MiniTaikoの上に積む） ----
    // すべてpublicなので呼び出し側やデバッグ用コードから自由に変更可能。
    public static float X = -185f;
    // 風船を打っている最中/割れた/割れなかった(Tier.Balloon中)は、通常位置の代わりにこちらを使い、風船の左に表示する
    public static float BalloonX = 100f;
    // Tier.Balloon中のY(下端基準)。通常のBottomYとは別に、風船の高さ(だいたいy=384付近)に合わせる
    public static float BalloonBottomY = 800f;
    public static float BottomY = 612.5f;   // MiniTaikoの背景の上に積む
    public static float Width = 860f;     // 表示幅（px, 1920x1080基準）。大きくしたい場合はここを増やす
    public static float Scale = 1f;     // 全体の倍率。ここを 1.2f にすれば20%大きくなる

    // 2P下段ミニ太鼓用。1Pレーン(386)から2Pレーン(645)への差分259pxを反映した初期位置。
    // 同じ3Dモデル／再生アニメーションをもう一度描画する表示専用の設定で、1P状態には影響しない。
    public static float P2X = -185f;
    // 2Pは画面下端寄りへ配置する。下げ過ぎ／上げ過ぎはこの値で調整する。
    public static float P2BottomY = 1350f;
    // 2P風船Tier中（打鍵中・成功・失敗演出中）だけ使う専用位置。
    // 1Pの通常→風船移動量と同じ差分を2P通常位置へ加えた初期値。
    public static float P2BalloonX = 100f;
    public static float P2BalloonBottomY = 1537.5f;
    public static float P2Width = 860f;
    public static float P2Scale = 1f;
    // DonChan3Dのレンダーバッファは820x599固定なのでその比率に合わせる（枠に収まるよう強制はしない＝はみ出してもそのまま拡大表示される）
    private const float BufAspect = 604.99f / 828.2f;
    //kljishdtgiukjhsdrktuih
    // ゴーゴー中のジャンプ等でキャラがレンダーバッファ(820x599)の枠外に見切れないよう、
    // Kisekaeの着せ替え画面より少しカメラを引いた値をデフォルトにしている。
    // 見切れる/寄りすぎる場合はここを変えて調整する。
    public static float CamPosZ = 26f;
    public static float CamFovY = 0.66f;

    // 優先度（数値が大きいほど強い、Enum定義上の並び）。ただし実際の切り替え判定(Update内)では
    // 風船(Balloon)が最優先＝GoGo中でも風船が来たら先に風船のアニメーションを再生し、
    // 風船が解決したら元のGoGoに自動で戻る。
    private enum Tier { Normal = 0, Clear = 1, Miss = 2, Balloon = 3, Gogo = 4 }
    private enum OneShot { None, GogoStart, MissFirst, ComboCut, ClearIn, Combo10, Combo10Max, SoulIn, BalloonBroke, BalloonMiss }

    // 各Tierのループアニメ名の手掛かり（DonChan3D.GetMappedFolderName() のロジックを流用）
    private static readonly Dictionary<Tier, string> TierLoopNeedle = new()
    {
        { Tier.Normal, "don_normal" },
        { Tier.Clear, "don_norm_loop" },        // 合格ライン到達後
        { Tier.Miss, "don_miss6" },             // miss_loop(一発)の後、コンボが戻るまでループ(failed_loop)
        { Tier.Balloon, "don_balloon_loop" },
        { Tier.Gogo, "don_sabi" },              // "don_sabi_start" は下のTierEntryOneShotで先に判定される
    };

    // 各Tierに入った瞬間に一発だけ流すアニメ（無ければ即ループへ）
    private static readonly Dictionary<Tier, OneShot> TierEntryOneShot = new()
    {
        { Tier.Normal, OneShot.None },
        { Tier.Clear, OneShot.ClearIn },
        { Tier.Miss, OneShot.MissFirst },       // ミスした瞬間の miss_loop
        { Tier.Balloon, OneShot.None },         // balloon_breaking自体がループなので入りの一発は無し
        { Tier.Gogo, OneShot.GogoStart },
    };

    private static readonly Dictionary<OneShot, string> OneShotAnimNeedle = new()
    {
        { OneShot.GogoStart, "don_sabi_start" },
        { OneShot.MissFirst, "don_miss" },         // ミスした瞬間に1回流す(miss_loop)
        { OneShot.ComboCut, "don_miss_normal" },   // コンボ復活時用（必要なら）
        { OneShot.ClearIn, "don_norm_up" },        // 合格ラインに達した瞬間
        { OneShot.Combo10, "don_combo" },
        { OneShot.Combo10Max, "don_combo_max" },
        { OneShot.SoulIn, "don_full_gage" },       // 魂ゲージが虹(MAX)に達した瞬間
        { OneShot.BalloonBroke, "don_balloon_success" },
        { OneShot.BalloonMiss, "don_balloon_failure" },
    };

    // ワンショットの実尺が分からないため、一定時間で自動的に次へ進める
    // （ただしTierが変わった場合はこの時間を待たず即座に打ち切られる）
    private const double OneShotFallbackDurationSec = 1.2;

    private static bool _initialized;
    private static bool _inMiss = false;          // combocut後、コンボが戻るまでtrue（Tier.Missを維持）
    private static bool _isClearLoop = false;      // 合格ライン到達後、NormalではなくClearをループ
    private static bool _wasGoGo = false;
    private static bool _wasClear = false;
    private static bool _wasRainbow = false;
    private static bool _wasBalloonActive = false; // 風船を打っている最中かどうかの直前フレーム値(新しい風船の開始検出用)
    private static int _lastNotifiedCombo = -1;    // 同じコンボ数で何度も通知されるのを防ぐガード用

    // 風船が割れた/失敗した直後、その一発を再生しきるまでTier.Balloonを維持するためのフラグ
    // （割れた瞬間には既に演奏側でballoonActiveがfalseになっているため、これが無いと一発が出ないまま次のTierに切り替わってしまう）
    private static bool _balloonResolvePending = false;
    private static OneShot _balloonResolveShot = OneShot.None;

    // 10コンボ/魂ゲージMAXなど、Tierに割り込まない程度の付随ワンショット。GoGo中は再生しない。
    private static readonly Queue<OneShot> _flavorQueue = new();

    private static Tier _tier = Tier.Normal;
    private static OneShot _current = OneShot.None;
    private static double _stateStartSec = -1.0;
    private static string _appliedNeedle = null; // 直前にSetAnimationByNameした手掛かり（毎フレーム再セットしないためのキャッシュ）

    // 2Pは1Pと同じGPUモデルを共有するが、アニメーション選択・フレーム・イベント状態は完全に分離する。
    // 1Pの全演出（Tier遷移、コンボ、一発、魂、風船成功／失敗）を同じ優先規則で再生する。
    private static DonChan3D.PlaybackState _p2Playback;
    private static bool _p2Initialized;
    private static bool _p2InMiss;
    private static bool _p2IsClearLoop;
    private static bool _p2WasGoGo;
    // ゴーゴー開始を検出したら、風船等の上位Tierが先行していても後で一度だけ開始演出を流す。
    private static bool _p2GogoStartPending;
    private static bool _p2WasClear;
    private static bool _p2WasRainbow;
    private static bool _p2WasBalloonActive;
    private static int _p2LastNotifiedCombo = -1;
    private static bool _p2BalloonResolvePending;
    private static OneShot _p2BalloonResolveShot = OneShot.None;
    private static bool _p2BalloonFrontmost;
    /// <summary>2Pが風船打鍵中または成功・失敗演出中ならtrue。描画順の最前面切替に使用する。</summary>
    public static bool IsP2BalloonFrontmost => _p2BalloonFrontmost;
    private static readonly Queue<OneShot> _p2FlavorQueue = new();
    private static Tier _p2Tier = Tier.Normal;
    private static OneShot _p2Current = OneShot.None;
    private static double _p2StateStartSec = -1.0;
    private static string _p2AppliedNeedle;

    /// <summary>風船関連(打っている最中/割れた瞬間/割れなかった瞬間)のTierかどうか。
    /// Enso側はこれを見て、風船中だけどんちゃんを最前面に描画し直す。</summary>
    /// <summary>風船関連(打っている最中/割れた瞬間/割れなかった瞬間)かどうか。
    /// GoGoと同時に発生していてもtrueになる(=GoGo中でも風船中はどんちゃんを最前面にしたいため、
    /// アニメの優先度(Tier)とは別に独立して保持する)。</summary>
    public static bool IsBalloonActive => _balloonFrontmost;
    private static bool _balloonFrontmost = false;

    private const string ConfigPath = "Don/EnsoConfig.json";

    public static void LoadConfig()
    {
        if (!File.Exists(ConfigPath)) return;
        try
        {
            string json = File.ReadAllText(ConfigPath);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("X", out var x)) X = x.GetSingle();
            if (root.TryGetProperty("BalloonX", out var bx2)) BalloonX = bx2.GetSingle();
            if (root.TryGetProperty("BalloonBottomY", out var bby)) BalloonBottomY = bby.GetSingle();
            if (root.TryGetProperty("BottomY", out var by)) BottomY = by.GetSingle();
            if (root.TryGetProperty("Width", out var w)) Width = w.GetSingle();
            if (root.TryGetProperty("Scale", out var s)) Scale = s.GetSingle();
            if (root.TryGetProperty("CamPosZ", out var cz)) CamPosZ = cz.GetSingle();
            if (root.TryGetProperty("CamFovY", out var cf)) CamFovY = cf.GetSingle();
            if (root.TryGetProperty("P2X", out var p2x)) P2X = p2x.GetSingle();
            if (root.TryGetProperty("P2BottomY", out var p2y)) P2BottomY = p2y.GetSingle();
            if (root.TryGetProperty("P2BalloonX", out var p2bx)) P2BalloonX = p2bx.GetSingle();
            if (root.TryGetProperty("P2BalloonBottomY", out var p2by)) P2BalloonBottomY = p2by.GetSingle();
            if (root.TryGetProperty("P2Width", out var p2w)) P2Width = p2w.GetSingle();
            if (root.TryGetProperty("P2Scale", out var p2s)) P2Scale = p2s.GetSingle();
            Console.WriteLine("[DonChanEnso] Config loaded.");
        }
        catch (Exception ex) { Console.WriteLine("[DonChanEnso] LoadConfig Error: " + ex.Message); }
    }

    public static void SaveConfig()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var data = new { X, BalloonX, BottomY, BalloonBottomY, Width, Scale, CamPosZ, CamFovY, P2X, P2BottomY, P2BalloonX, P2BalloonBottomY, P2Width, P2Scale };
            string json = JsonSerializer.Serialize(data, options);
            string dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath, json);
            Console.WriteLine("[DonChanEnso] Config saved.");
        }
        catch (Exception ex) { Console.WriteLine("[DonChanEnso] SaveConfig Error: " + ex.Message); }
    }

    public static void Init()
    {
        // 自動ロードは廃止（Kisekae側で明示的にロードするか、現在のメモリ上の値を優先する）

        DonChan3D.CamPosX = 0f;
        DonChan3D.CamPosY = 0f;
        DonChan3D.CamPosZ = CamPosZ;
        DonChan3D.CamFovY = CamFovY;
        DonChan3D.TarPosY = 0f;
        DonChan3D.ModelRotX = 181.25f;
        DonChan3D.ModelRotY = 27.5f;
        DonChan3D.ModelRotZ = 0f;
        DonChan3D.Init(); // 既にロード済みなら内部でスキップされる（多重呼び出し安全）

        _initialized = true;

        _inMiss = false;
        _isClearLoop = false;
        _wasGoGo = false;
        _wasClear = false;
        _wasRainbow = false;
        _wasBalloonActive = false;
        _lastNotifiedCombo = -1;
        _balloonResolvePending = false;
        _balloonResolveShot = OneShot.None;
        _balloonFrontmost = false;
        _flavorQueue.Clear();
        _tier = Tier.Normal;
        _current = OneShot.None;
        _stateStartSec = -1.0;
        _appliedNeedle = null;

        _p2Playback = DonChan3D.CreatePlaybackState();
        _p2Initialized = true;
        _p2InMiss = false;
        _p2IsClearLoop = false;
        _p2WasGoGo = false;
        _p2GogoStartPending = false;
        _p2WasClear = false;
        _p2WasRainbow = false;
        _p2WasBalloonActive = false;
        _p2LastNotifiedCombo = -1;
        _p2BalloonResolvePending = false;
        _p2BalloonResolveShot = OneShot.None;
        _p2BalloonFrontmost = false;
        _p2FlavorQueue.Clear();
        _p2Tier = Tier.Normal;
        _p2Current = OneShot.None;
        _p2StateStartSec = -1.0;
        _p2AppliedNeedle = null;
    }

    /// <summary>
    /// Enso終了時に呼ぶ。DonChan3Dは Kisekae 側とも共有しているため、ここでは
    /// DonChan3D.Unload() は呼ばない（呼ぶと相手側が使用中のリソースを解放してしまう）。
    /// 曲ごとの演出状態のみリセットする。
    /// </summary>
    public static void Unload()
    {
        _initialized = false;
        _appliedNeedle = null;
        // 単発アニメの途中(LoopAnimation=false)で演奏が終わった場合、他画面(SongSelect/Result)が
        // それを引き継いでしまわないよう、抜ける時にループ再生前提の状態へ戻しておく。
        DonChan3D.LoopAnimation = true;
    }

    // ==== イベント通知（Enso側から呼び出す） ====

    /// <summary>10コンボ到達（gaugeClear=true なら魂ゲージが合格ラインに達している状態での10コンボ）</summary>
    public static void OnComboMilestone(int combo, bool gaugeClear)
    {
        if (combo <= 0 || combo % 10 != 0 || combo == _lastNotifiedCombo) return;
        _lastNotifiedCombo = combo;
        _flavorQueue.Enqueue(gaugeClear ? OneShot.Combo10Max : OneShot.Combo10);
    }

    /// <summary>バッド/ミスでコンボが切れた。Tier.Missへ入り、コンボが戻るまで維持する。</summary>
    public static void OnComboBreak() => _inMiss = true;

    /// <summary>良い/可の判定を取り直した（Tier.Missを抜けてNormal/Clear/GoGoへ戻る）</summary>
    public static void OnComboRecovered()
    {
        if (_inMiss) _flavorQueue.Enqueue(OneShot.ComboCut); // 復活時の演出(don_miss_normal)を積む
        _inMiss = false;
    }

    /// <summary>2Pの10コンボ到達。魂ゲージ状態に応じて通常／MAXコンボ演出を独立キューへ積む。</summary>
    public static void OnP2ComboMilestone(int combo, bool gaugeClear)
    {
        if (combo <= 0 || combo % 10 != 0 || combo == _p2LastNotifiedCombo) return;
        _p2LastNotifiedCombo = combo;
        _p2FlavorQueue.Enqueue(gaugeClear ? OneShot.Combo10Max : OneShot.Combo10);
    }

    /// <summary>2Pのコンボ切断。1PのTierには一切影響しない。</summary>
    public static void OnP2ComboBreak() => _p2InMiss = true;

    /// <summary>2Pが良／可を取り直した際、2PだけをMiss状態から戻し、復帰演出をキューへ積む。</summary>
    public static void OnP2ComboRecovered()
    {
        if (_p2InMiss) _p2FlavorQueue.Enqueue(OneShot.ComboCut);
        _p2InMiss = false;
    }

    /// <summary>2P風船成功時だけ再生する成功演出。</summary>
    public static void OnP2BalloonBroke()
    {
        _p2BalloonResolvePending = true;
        _p2BalloonResolveShot = OneShot.BalloonBroke;
    }

    /// <summary>2P風船失敗時だけ再生する失敗演出。</summary>
    public static void OnP2BalloonMiss()
    {
        _p2BalloonResolvePending = true;
        _p2BalloonResolveShot = OneShot.BalloonMiss;
    }

    public static void OnBalloonBroke()
    {
        _balloonResolvePending = true;
        _balloonResolveShot = OneShot.BalloonBroke;
    }

    public static void OnBalloonMiss()
    {
        _balloonResolvePending = true;
        _balloonResolveShot = OneShot.BalloonMiss;
    }

    // ==== 毎フレーム状態更新 ====

    /// <param name="gaugeClear">Gauge.IsClear（合格ラインに達したか）</param>
    /// <param name="balloonActive">風船を打っている最中か</param>
    public static void Update(double nowSec, bool isGoGo, bool gaugeClear, bool balloonActive)
    {
        if (!_initialized) return;
        if (_stateStartSec < 0) _stateStartSec = nowSec;

        // 毎フレーム反映：実行中に CamPosZ / CamFovY を書き換えれば即座に見え方に反映される
        DonChan3D.CamPosZ = CamPosZ;
        DonChan3D.CamFovY = CamFovY;

        // 合格ライン到達 → 以後 Normal の代わりに Clear をループ
        if (gaugeClear && !_wasClear) _isClearLoop = true;
        _wasClear = gaugeClear;

        // 魂ゲージが虹(MAX)に達した瞬間 → soulin。Tierには影響させない付随演出としてキューに積む。
        if (Gauge.IsRainbow && !_wasRainbow) _flavorQueue.Enqueue(OneShot.SoulIn);
        _wasRainbow = Gauge.IsRainbow;

        // 優先度(Tier)決定：風船(打っている最中/割れた/割れなかった)が最優先。
        // GoGo中に風船が来た場合も、まず風船のアニメーションを再生し、風船が解決したら
        // (balloonTierActiveがfalseに戻ったら) 自動的に元のGoGoへ戻る。
        bool balloonTierActive = balloonActive || _balloonResolvePending;
        // 最前面描画の判定もTierと同じ理由でBalloonを優先させる。
        _balloonFrontmost = balloonTierActive;

        // 新しい風船が始まった瞬間(balloonActiveがfalse→trueになった)を検出する。
        // 既にTier.Balloon中(=前の風船の割れ演出待ち等)であっても、次の風船が来たら
        // ループアニメを一から再生し直したいため、この場合はtierが変わらなくても強制的にリセットする。
        bool balloonJustStarted = balloonActive && !_wasBalloonActive;
        _wasBalloonActive = balloonActive;

        Tier targetTier = balloonTierActive ? Tier.Balloon
                         : isGoGo ? Tier.Gogo
                         : _inMiss ? Tier.Miss
                         : _isClearLoop ? Tier.Clear
                         : Tier.Normal;

        bool tierChanged = (targetTier != _tier);
        if (balloonJustStarted && !tierChanged && _tier == Tier.Balloon)
        {
            // 風船アニメーション中に次の風船が来た場合：前の風船の一発演出等を破棄し、
            // 風船打っている最中のループアニメを頭から再生し直す。
            _balloonResolvePending = false;
            _balloonResolveShot = OneShot.None;
            _current = OneShot.None;
            _appliedNeedle = null; // needleが変わらなくても再セットされるようにキャッシュを破棄
            _stateStartSec = nowSec;
        }
        if (tierChanged)
        {
            // 優先度(Tier)が変わったら、再生中のアニメがどこまで進んでいようと問答無用で打ち切って切り替える
            _tier = targetTier;

            // Gogo/Miss/Balloonは即座に割り込む必要があるため付随演出を破棄するが、
            // Clearへの遷移はクリアイン(ClearIn)の後で付随演出(10コンボMAX等)を続けて再生したいのでキューを保持する。
            // これを保持しないと、魂ゲージ突入と10コンボ目がほぼ同時に発生した場合、
            // Enso.Update()内でOnComboMilestone→Update(gaugeClear反映)の間に1フレームの遅延があるため、
            // せっかく積んだCombo10Maxがクリア突入の切り替えで即座に消されてしまう。
            if (_tier == Tier.Gogo || _tier == Tier.Miss || _tier == Tier.Balloon)
                _flavorQueue.Clear();
            if (_tier != Tier.Balloon) { _balloonResolvePending = false; _balloonResolveShot = OneShot.None; }

            // GoGo中に発生したイベント（不可・クリアライン到達）は、GoGoの優先度が高いため
            // Tier遷移がGoGo終了まで遅延される。GoGo終了と同時に tierChanged が発生するが、
            // その瞬間に入りのワンショットを流すと「GoGoが終わった後に急にアニメ」という
            // 不自然な演出になる。前フレームがGoGoだった場合はワンショットをスキップして
            // 即座にループへ入る。
            // ・Gogo→Miss : MissFirst(don_miss)をスキップ → miss_loop(don_miss6)へ
            // ・Gogo→Clear: ClearIn(don_norm_up)をスキップ → clear_loop(don_norm_loop)へ
            bool skipEntryShot = _wasGoGo && (_tier == Tier.Miss || _tier == Tier.Clear);
            _current = skipEntryShot ? OneShot.None : TierEntryOneShot[_tier];
            _stateStartSec = nowSec;
        }
        else
        {
            // Tierは変わらない：Tier内のワンショット消化
            if (_tier == Tier.Balloon && _balloonResolvePending && _current != _balloonResolveShot)
            {
                // 風船が割れた/失敗した一発をこのTier内に差し込む
                _current = _balloonResolveShot;
                _stateStartSec = nowSec;
            }
            else if (_current == OneShot.None)
            {
                if (_tier != Tier.Gogo && _flavorQueue.Count > 0)
                {
                    _current = _flavorQueue.Dequeue();
                    _stateStartSec = nowSec;
                }
            }
            else
            {
                // ワンショット再生中の終了判定
                bool isFinished = DonChan3D.IsCurrentAnimFinished || (nowSec - _stateStartSec >= OneShotFallbackDurationSec);
                if (isFinished)
                {
                    if (_current == _balloonResolveShot)
                    {
                        _balloonResolvePending = false;
                        _balloonResolveShot = OneShot.None;
                    }
                    _current = (_tier != Tier.Gogo && _flavorQueue.Count > 0) ? _flavorQueue.Dequeue() : OneShot.None;
                    _stateStartSec = nowSec;
                }
            }
        }

        _wasGoGo = isGoGo;

        string needle = _current != OneShot.None ? OneShotAnimNeedle[_current] : TierLoopNeedle[_tier];
        if (needle != _appliedNeedle)
        {
            // ワンショットの場合はループさせない。ただしTierが変わった直後のループ開始時はLoopAnimationをtrueに戻す必要がある。
            DonChan3D.LoopAnimation = (_current == OneShot.None);
            if (DonChan3D.SetAnimationByName(needle)) _appliedNeedle = needle;
        }
        else if (_current == OneShot.None && !DonChan3D.LoopAnimation)
        {
            // ワンショットが終わってループに戻った際に、LoopAnimationがfalseのままだと止まってしまうのでtrueに戻す
            DonChan3D.LoopAnimation = true;
        }

        DonChan3D.Update();
    }

    /// <summary>
    /// 2P専用の演奏状態から、共有モデルへ適用する独立アニメーション状態を更新する。
    /// 1Pと同じTier優先度・一発演出キュー規則を使用するが、全状態は2P専用である。
    /// </summary>
    public static void UpdateP2(double nowSec, bool isGoGo, bool gaugeClear, bool balloonActive)
    {
        if (!_initialized || !_p2Initialized) return;
        if (_p2Playback == null) _p2Playback = DonChan3D.CreatePlaybackState();
        if (_p2StateStartSec < 0) _p2StateStartSec = nowSec;

        if (gaugeClear && !_p2WasClear) _p2IsClearLoop = true;
        _p2WasClear = gaugeClear;

        // 2Pのゴーゴー開始を明示的に記録する。風船Tierが優先される場合も、戻り次第開始演出を再生する。
        if (isGoGo && !_p2WasGoGo) _p2GogoStartPending = true;

        // 2Pの魂MAX到達時だけsoulinを再生する。
        if (Gauge.IsP2Rainbow && !_p2WasRainbow) _p2FlavorQueue.Enqueue(OneShot.SoulIn);
        _p2WasRainbow = Gauge.IsP2Rainbow;

        bool balloonTierActive = balloonActive || _p2BalloonResolvePending;
        // 2P自身の風船状態だけで位置を切り替える。1P風船には影響されない。
        _p2BalloonFrontmost = balloonTierActive;
        bool balloonJustStarted = balloonActive && !_p2WasBalloonActive;
        _p2WasBalloonActive = balloonActive;

        Tier targetTier = balloonTierActive ? Tier.Balloon
                         : isGoGo ? Tier.Gogo
                         : _p2InMiss ? Tier.Miss
                         : _p2IsClearLoop ? Tier.Clear
                         : Tier.Normal;

        bool tierChanged = targetTier != _p2Tier;
        if (balloonJustStarted && !tierChanged && _p2Tier == Tier.Balloon)
        {
            _p2BalloonResolvePending = false;
            _p2BalloonResolveShot = OneShot.None;
            _p2Current = OneShot.None;
            _p2AppliedNeedle = null;
            _p2StateStartSec = nowSec;
        }

        if (tierChanged)
        {
            _p2Tier = targetTier;
            if (_p2Tier == Tier.Gogo || _p2Tier == Tier.Miss || _p2Tier == Tier.Balloon)
                _p2FlavorQueue.Clear();
            if (_p2Tier != Tier.Balloon)
            {
                _p2BalloonResolvePending = false;
                _p2BalloonResolveShot = OneShot.None;
            }

            bool skipEntryShot = _p2WasGoGo && (_p2Tier == Tier.Miss || _p2Tier == Tier.Clear);
            if (_p2Tier == Tier.Gogo && _p2GogoStartPending)
            {
                // 明示的な2Pゴーゴー開始演出: don_sabi_startを一発再生する。
                _p2Current = OneShot.GogoStart;
                _p2GogoStartPending = false;
            }
            else
            {
                _p2Current = skipEntryShot ? OneShot.None : TierEntryOneShot[_p2Tier];
            }
            _p2StateStartSec = nowSec;
        }
        else
        {
            if (_p2Tier == Tier.Balloon && _p2BalloonResolvePending && _p2Current != _p2BalloonResolveShot)
            {
                _p2Current = _p2BalloonResolveShot;
                _p2StateStartSec = nowSec;
            }
            else if (_p2Current == OneShot.None)
            {
                if (_p2Tier != Tier.Gogo && _p2FlavorQueue.Count > 0)
                {
                    _p2Current = _p2FlavorQueue.Dequeue();
                    _p2StateStartSec = nowSec;
                }
            }
            else
            {
                bool finished = _p2Playback.IsFinished || nowSec - _p2StateStartSec >= OneShotFallbackDurationSec;
                if (finished)
                {
                    if (_p2Current == _p2BalloonResolveShot)
                    {
                        _p2BalloonResolvePending = false;
                        _p2BalloonResolveShot = OneShot.None;
                    }
                    _p2Current = (_p2Tier != Tier.Gogo && _p2FlavorQueue.Count > 0)
                        ? _p2FlavorQueue.Dequeue()
                        : OneShot.None;
                    _p2StateStartSec = nowSec;
                }
            }
        }

        _p2WasGoGo = isGoGo;
        string needle = _p2Current != OneShot.None ? OneShotAnimNeedle[_p2Current] : TierLoopNeedle[_p2Tier];
        if (needle != _p2AppliedNeedle)
        {
            _p2Playback.LoopAnimation = _p2Current == OneShot.None;
            if (DonChan3D.SetAnimationByName(_p2Playback, needle)) _p2AppliedNeedle = needle;
        }
        else if (_p2Current == OneShot.None && !_p2Playback.LoopAnimation)
        {
            _p2Playback.LoopAnimation = true;
        }

        DonChan3D.UpdatePlayback(_p2Playback);
    }

    // ==== 描画 ====

    public static void Draw(float vx, float vy, float s)
    {
        if (!_initialized) return;

        // s: 画面解像度倍率, Scale: ユーザー指定倍率
        float finalScale = s * Scale;
        float w = Width * finalScale;
        float h = w * BufAspect;
        // 風船関連(打っている最中・割れた瞬間・割れなかった瞬間)は、Tier.Balloonが維持されている間ずっと
        // 風船の左側に表示する。それ以外は通常位置。
        float baseX = _balloonFrontmost ? BalloonX : X;
        float baseBottomY = _balloonFrontmost ? BalloonBottomY : BottomY;
        DrawAt(baseX, baseBottomY, w, h, vx, vy, s);
    }

    /// <summary>
    /// 2P下段ミニ太鼓の上に、2P専用の演奏状態でドンちゃんを描画する。
    /// 位置・大きさはP2X/P2BottomY/P2Width/P2Scaleで個別調整できる。
    /// </summary>
    public static void DrawP2(float vx, float vy, float s)
    {
        if (!_initialized) return;

        float finalScale = s * P2Scale;
        float w = P2Width * finalScale;
        float h = w * BufAspect;
        float baseX = _p2BalloonFrontmost ? P2BalloonX : P2X;
        float baseBottomY = _p2BalloonFrontmost ? P2BalloonBottomY : P2BottomY;
        DrawP2At(baseX, baseBottomY, w, h, vx, vy, s);
    }

    private static void DrawP2At(float x, float bottomY, float w, float h, float vx, float vy, float s)
    {
        float dx = x * s + vx;
        float dy = bottomY * s + vy - h;
        var dest = new Rectangle(dx, dy, w, h);
        try
        {
            DonChan3D._insideVirtualFrame = true;
            DonChan3D.DrawSimple(dest, _p2Playback);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[DonChanEnso] DrawP2 ERROR: " + ex);
        }
        finally
        {
            DonChan3D._insideVirtualFrame = false;
        }
    }

    private static void DrawAt(float x, float bottomY, float w, float h, float vx, float vy, float s)
    {
        float dx = x * s + vx;
        float dy = bottomY * s + vy - h;
        var dest = new Rectangle(dx, dy, w, h);
        try
        {
            DonChan3D._insideVirtualFrame = true;
            DonChan3D.DrawSimple(dest);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[DonChanEnso] DrawSimple ERROR: " + ex);
        }
        finally
        {
            DonChan3D._insideVirtualFrame = false;
        }
    }
}
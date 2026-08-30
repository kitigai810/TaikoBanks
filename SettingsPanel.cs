using Raylib_cs;
using System.Numerics;
using System.IO;
using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;

public static class SettingsPanel
{
    private static bool _isOpen;
    private static float _openT;

    // ==================================================================
    // 💡 GitHub Release 自動アップデート機能。
    //    Draw()側のボタンから TriggerUpdateCheck() を呼び出す想定。
    //    進捗・状態は他スレッドから更新されるため、Draw()内で読むだけにすること
    //    (直接書き換えない)。
    // ==================================================================
    // 💡 owner/repo/token等の設定はすべて GitHubUpdater.cs 側の Default* 定数にまとめてある。
    //    ここでは意識しない(GitHubUpdater.CreateDefault() を呼ぶだけ)。

    public enum UpdateState { Idle, Checking, UpToDate, Downloading, Updated, Error }

    private static volatile UpdateState _updateState = UpdateState.Idle;
    private static volatile float _updateProgress01 = 0f; // 0.0〜1.0
    private static volatile string _updateMessage = "";
    private static volatile string _updateLatestVersion = "";
    private static bool _updateTaskRunning = false;

    // Updated時にGitHubUpdaterが返すステージングフォルダ(展開済み更新ファイル置き場)。
    // 実行中のexe/dllは自プロセスからは上書きできないため、実際のコピーはInitiateRestart()で
    // LaunchUpdaterBat()を通じて外部バッチプロセスに委譲する。そのため再起動が実行されるまで保持しておく。
    private static volatile string? _updateStagingFolder = null;

    // 更新完了後の自動再起動カウントダウン
    // > 0: カウント中, <= 0: 起動待ち(InitiateRestart が呼ばれるまで一瞬 0 になる)
    private static float _restartCountdown = 0f;  // 秒
    private const float RestartCountdownSec = 5f;
    private static bool _restartInitiated = false;

    public static UpdateState CurrentUpdateState => _updateState;
    public static float UpdateProgress01 => _updateProgress01;
    public static string UpdateMessage => _updateMessage;

    /// <summary>
    /// 設定画面の「アップデート確認」ボタンから呼び出す。二重起動は無視する。
    /// </summary>
    public static void TriggerUpdateCheck()
    {
        if (_updateTaskRunning) return;
        _updateTaskRunning = true;
        _updateState = UpdateState.Checking;
        _updateProgress01 = 0f;
        _updateMessage = "最新バージョンを確認中...";

        // Draw()を呼ぶメインスレッドをブロックしないよう、バックグラウンドで実行する
        _ = RunUpdateCheckAsync();
    }

    private static async Task RunUpdateCheckAsync()
    {
        try
        {
            var updater = GitHubUpdater.CreateDefault();

            var progress = new Progress<double>(p =>
            {
                _updateState = UpdateState.Downloading;
                _updateProgress01 = (float)p;
                _updateMessage = $"ダウンロード中... {p:P0}";
            });

            string exeDir = AppContext.BaseDirectory;

            var result = await updater.CheckAndUpdateAsync(
                currentVersion: Program.GameVersion,
                destinationFolder: exeDir,
                progress: progress
            );

            switch (result.Status)
            {
                case UpdateStatus.UpToDate:
                    _updateState = UpdateState.UpToDate;
                    _updateMessage = "最新バージョンです。";
                    break;
                case UpdateStatus.Updated:
                    _updateState = UpdateState.Updated;
                    _updateLatestVersion = result.LatestVersion ?? "";
                    _updateStagingFolder = result.StagingFolder;
                    _updateMessage = $"Ver.{_updateLatestVersion} に更新しました。再起動してください。";
                    break;
                case UpdateStatus.Error:
                    _updateState = UpdateState.Error;
                    _updateMessage = $"更新に失敗しました: {result.ErrorMessage}";
                    break;
            }
        }
        catch (Exception ex)
        {
            _updateState = UpdateState.Error;
            _updateMessage = $"更新に失敗しました: {ex.Message}";
        }
        finally
        {
            _updateTaskRunning = false;
        }
    }

    /// <summary>
    /// 更新後の再起動シーケンスを開始する。
    /// bat を書き出して起動し、実行中exeの終了を待ってから上書きコピー→再起動させる。
    /// (LaunchUpdaterBat内部でEnvironment.Exit(0)されるため、正常系ではこの後には進まない)
    /// </summary>
    private static void InitiateRestart()
    {
        if (string.IsNullOrEmpty(_updateStagingFolder) || !Directory.Exists(_updateStagingFolder))
        {
            // ステージング済みファイルが無い場合は上書きできないので手動再起動を促す
            _updateMessage = "更新ファイルが見つかりませんでした。手動で再起動してください。";
            return;
        }

        try
        {
            string exeDir = AppContext.BaseDirectory;
            GitHubUpdater.LaunchUpdaterBat(_updateStagingFolder, exeDir);
        }
        catch
        {
            // bat 起動に失敗しても exe は終了させる（手動再起動を促す）
        }
        Environment.Exit(0);
    }

    private const int PANEL_W = 440;
    private const string CONFIG_PATH = "settings.cfg";

    public static bool IsFullscreen = false;

    // ウィンドウ解像度プリセット (すべて16:9)
    public static readonly (int W, int H, string Label)[] ResolutionOptions =
    {
        (1280, 720,  "HD"),
        (1366, 768,  "WXGA"),
        (1600, 900,  "しらん"),
        (1920, 1080, "FHD"),
        (2560, 1440, "WQHD"),
        (3840, 2160, "4K"),
    };
    public static int ResolutionIndex = 1; // デフォルト: FHD

    public static bool UseWasapiExclusive = true;
    public static int FpsLimit = 0;
    public static int MasterVolume = 100;
    public static int BgmVolume = 100;
    public static int SeVolume = 100;
    public static AudioBackend OutputBackend = AudioBackend.Wasapi;

    // マスター音量を掛け合わせた実効音量 (BGM/SE再生箇所から参照)
    public static float EffectiveBgmVolume => Math.Clamp(MasterVolume / 100f, 0f, 1f) * Math.Clamp(BgmVolume / 100f, 0f, 1f);
    public static float EffectiveSeVolume => Math.Clamp(MasterVolume / 100f, 0f, 1f) * Math.Clamp(SeVolume / 100f, 0f, 1f);
    public static int BufferSizeMs = 20; // 5〜100ms、AudioEngine.Reinit()で即座に反映
    public static int AsioDriverIndex = 0;

    // サンプルレート選択肢 (WASAPI排他/ASIO/DirectSoundで使用。共有WASAPIはデバイス側の形式に従うため対象外)
    public static readonly int[] SampleRateOptions = { 44100, 48000, 88200, 96000 };
    public static int SampleRateIndex = 1; // デフォルト: 48000Hz
    public static int PreferredSampleRate => SampleRateOptions[Math.Clamp(SampleRateIndex, 0, SampleRateOptions.Length - 1)];

    // ---- GAME CONFIG ----
    public enum SongsTypeOption { TJA, ESE }
    public static SongsTypeOption SongsType = SongsTypeOption.TJA;

    /// <summary>2P専用AIレベル。0=完全手動、10=完全AUTO、1〜9=人間らしいミスを伴う自動演奏。</summary>
    public static int AILevel = 0;

    // SongsType に対応した曲フォルダパスを返す
    public static string SongsFolder => SongsType == SongsTypeOption.ESE ? "Songs/ESE" : "Songs";

    // キー割り当て (Win32 仮想キーコード)。各種Listで管理
    // 0=左面, 1=右面, 2=左縁, 3=右縁
    public static List<int> KeyLeft = new List<int>();
    public static List<int> KeyRight = new List<int>();
    public static List<int> KeyEdgeL = new List<int>();
    public static List<int> KeyEdgeR = new List<int>();

    // 2Pキー割り当て。既定値はX=左縁、C=左面、Z=右面、V=右縁。
    public static List<int> P2KeyLeft = new List<int>();
    public static List<int> P2KeyRight = new List<int>();
    public static List<int> P2KeyEdgeL = new List<int>();
    public static List<int> P2KeyEdgeR = new List<int>();

    // 入力スレッドは可変Listを直接列挙しない。設定変更時だけ新しい配列を発行して参照を差し替える。
    private static readonly object KeyBindingsSync = new();
    public sealed class KeyBindingSnapshot
    {
        public readonly int[] Left;
        public readonly int[] Right;
        public readonly int[] EdgeL;
        public readonly int[] EdgeR;
        public readonly int[] P2Left;
        public readonly int[] P2Right;
        public readonly int[] P2EdgeL;
        public readonly int[] P2EdgeR;

        public KeyBindingSnapshot(int[] left, int[] right, int[] edgeL, int[] edgeR,
            int[] p2Left, int[] p2Right, int[] p2EdgeL, int[] p2EdgeR)
        {
            Left = left;
            Right = right;
            EdgeL = edgeL;
            EdgeR = edgeR;
            P2Left = p2Left;
            P2Right = p2Right;
            P2EdgeL = p2EdgeL;
            P2EdgeR = p2EdgeR;
        }
    }

    private static KeyBindingSnapshot _keyBindingSnapshot = new(
        Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(),
        Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>());

    /// <summary>入力スレッド用の不変キー割り当て。返却後にListを書き換えても内容は変化しない。</summary>
    public static KeyBindingSnapshot GetKeyBindingSnapshot() => Volatile.Read(ref _keyBindingSnapshot);

    private static void RefreshKeyBindingSnapshot()
    {
        lock (KeyBindingsSync)
            RefreshKeyBindingSnapshotLocked();
    }

    private static void RefreshKeyBindingSnapshotLocked()
    {
        Volatile.Write(ref _keyBindingSnapshot, new KeyBindingSnapshot(
            KeyLeft.ToArray(), KeyRight.ToArray(), KeyEdgeL.ToArray(), KeyEdgeR.ToArray(),
            P2KeyLeft.ToArray(), P2KeyRight.ToArray(), P2KeyEdgeL.ToArray(), P2KeyEdgeR.ToArray()));
    }

    // 互換性のためのプロパティ（DrumInput等で使用）
    public static int KeyDon1 => KeyLeft.Count > 0 ? KeyLeft[0] : 0x44;
    public static int KeyDon2 => KeyRight.Count > 0 ? KeyRight[0] : 0x46;
    public static int KeyKa1 => KeyEdgeL.Count > 0 ? KeyEdgeL[0] : 0x4A;
    public static int KeyKa2 => KeyEdgeR.Count > 0 ? KeyEdgeR[0] : 0x4B;

    // UIからは外したが他コードが参照するため保持
    public static bool IsLang = true;
    public static bool IsFHD = true;
    public static int AudioOffsetMs = 0;

    private static float _fullscreenT;
    private static float _wasapiT;
    private static float _freePlayT;

    private static bool _prevFullscreen;
    private static int _prevResolutionIndex = 1;
    private static int _prevFps = 60;
    private static bool _prevWasapi;
    private static int _prevMasterVolume = 100;
    private static int _prevBgmVolume = 100;
    private static int _prevSeVolume = 100;
    private static bool _prevFreePlay = true;
    private static int _prevRequiredCoins = 1;
    private static AudioBackend _prevBackend;
    private static int _prevBufferSize = 20;
    private static int _prevAsioDriverIndex = 0;
    private static int _prevSampleRateIndex = 1;
    private static bool _bufferDragging = false;

    private static bool _editingVolume = false;
    private static string _volumeInput = "";

    // スクロール関連
    private static float _scrollY = 0f;
    private static float _contentHeight = 0f;

    // ---- 曲再読み込み ----
    private static float _reloadFlashT = 0f; // 成功フラッシュ用タイマー

    // ---- タイトルテキスト編集 ----
    private static bool _editingTitleText = false;
    private static string _titleTextInput = "";

    // ---- 曲検索 ----
    private static bool _editingSearch = false;
    private static string _searchInput = "";

    // ---- ネームプレート設定 ----
    private static bool _editingName = false;
    private static string _nameInput = "";
    private static bool _editingNpTitle = false;
    private static string _npTitleInput = "";
    private static bool _editingDan = false;
    private static string _danInput = "";
    private static bool _npIsGold = false;
    private static float _npIsGoldT = 0f;
    private static int _npDanFrame = 3;

    // ---- 2Pネームプレート設定（Data/PlayData2.json専用） ----
    private static bool _editingP2Name = false;
    private static string _p2NameInput = "";
    private static bool _editingP2NpTitle = false;
    private static string _p2NpTitleInput = "";
    private static bool _editingP2Dan = false;
    private static string _p2DanInput = "";
    private static bool _p2NpIsGold = false;
    private static float _p2NpIsGoldT = 0f;
    private static int _p2NpDanFrame = 3;

    // キャプチャ状態: (type, index) - type 0=左面, 1=右面, 2=左縁, 3=右縁
    private static int _captureType = -1;
    private static int _captureIndex = -1;

    // ダークテーマパレット
    private static readonly Color Primary = new Color(30, 30, 30, 255);
    private static readonly Color PrimaryDark = new Color(15, 15, 15, 255);
    private static readonly Color Accent = new Color(255, 64, 129, 255);
    private static readonly Color AccentHalf = new Color(255, 64, 129, 128);
    private static readonly Color Paper = new Color(18, 18, 18, 255);
    private static readonly Color TextPrimary = new Color(255, 255, 255, 255);
    private static readonly Color TextSecondary = new Color(180, 180, 180, 255);
    private static readonly Color Divider = new Color(255, 255, 255, 31);
    private static readonly Color TrackOff = new Color(255, 255, 255, 97);
    private static readonly Color ThumbOff = new Color(80, 80, 80, 255);
    private static readonly Color HoverTint = new Color(255, 255, 255, 15);

    private static readonly int[] FpsOptions = { 30, 60, 120, 144, 240, 0 };

    // ---- パフォーマンス設定 ----
    private static readonly int[] DancerFpsOptions = { 30, 60, 120 };
    private static readonly int[] DonChanFpsOptions = { 2, 30, 60, 120 };
    public static int DancerFps = 120;
    public static int DonChanFps = 120;
    public static bool DonChanLowQuality = false; // false=High, true=Low
    public static bool DancerLowQuality = false; // false=High, true=Low

    private static int _prevDancerFps = 120;
    private static int _prevDonChanFps = 120;
    private static bool _prevDonChanLowQuality = false;
    private static bool _prevDancerLowQuality = false;

    // Program._vramDetailMode に対応するラベル (0=概要のみ,1=テクスチャ,2=所有者,3=種別,4=完全非表示)
    private static readonly string[] VramModeLabels = { "概要のみ", "テクスチャ詳細", "所有者別", "種別ごと", "完全非表示" };

    // ---- テストメニュー風 ナビゲーション ----
    private static readonly string[] CategoryLabels =
    {
        "DISPLAY OPTIONS",
        "SOUND & VOLUME OPTIONS",
        "COIN OPTIONS",
        "GAME CONFIG",
        "TEXT OPTIONS",
        "SONG SEARCH",
        "SONG LIST",
        "SCENE JUMP",
        "VRAM DEBUG",
        "UPDATE",
        "PERFORMANCE",
    };
    private static int _menuCursor = 0;
    private static bool _inCategory = false;
    private static readonly Color TestMenuRed = new Color(230, 30, 30, 255);

    private static Rectangle _rippleRect;
    private static Vector2 _rippleCenter;
    private static float _rippleMaxR;
    private static float _rippleT = 1f;

    // 追加用の一時変数
    private static int _pendingAddType = -1;
    private static int _pendingAddIndex = -1;

    public static bool Open => _isOpen;

    static SettingsPanel()
    {
        Load();
        if (KeyLeft.Count == 0) KeyLeft.Add(0x44); // D
        if (KeyRight.Count == 0) KeyRight.Add(0x46); // F
        if (KeyEdgeL.Count == 0) KeyEdgeL.Add(0x4A); // J
        if (KeyEdgeR.Count == 0) KeyEdgeR.Add(0x4B); // K
        if (P2KeyLeft.Count == 0) P2KeyLeft.Add(0x43); // C
        if (P2KeyRight.Count == 0) P2KeyRight.Add(0x5A); // Z
        if (P2KeyEdgeL.Count == 0) P2KeyEdgeL.Add(0x58); // X
        if (P2KeyEdgeR.Count == 0) P2KeyEdgeR.Add(0x56); // V
        RefreshKeyBindingSnapshot();
        _prevFullscreen = false;
        _prevResolutionIndex = ResolutionIndex;
        _prevFps = 60;
        _prevWasapi = UseWasapiExclusive;
        _prevMasterVolume = MasterVolume;
        _prevBgmVolume = BgmVolume;
        _prevSeVolume = SeVolume;
        _prevBackend = OutputBackend;
        _prevBufferSize = BufferSizeMs;
        _prevAsioDriverIndex = AsioDriverIndex;
        _fullscreenT = IsFullscreen ? 1f : 0f;
        _wasapiT = UseWasapiExclusive ? 1f : 0f;
        _freePlayT = Coin.FreePlay ? 1f : 0f;
        _prevFreePlay = Coin.FreePlay;
        _prevRequiredCoins = Coin.RequiredCoins;

        // パフォーマンス設定を起動時点で反映しておく(ApplySettings()はDraw()内でしか呼ばれないため)
        _prevDancerFps = DancerFps;
        Dancer.TargetFps = DancerFps;
        _prevDonChanFps = DonChanFps;
        DonChan3D.PlaybackFps = DonChanFps;
        _prevDonChanLowQuality = DonChanLowQuality;
        DonChan3D.RenderQuality = DonChanLowQuality ? DonChan3D.QualityLevel.Low : DonChan3D.QualityLevel.High;
        _prevDancerLowQuality = DancerLowQuality;
        Dancer.Quality = DancerLowQuality ? Dancer.QualityLevel.Low : Dancer.QualityLevel.High;

        // ネームプレート初期値
        _nameInput = NamePlate.CurrentName;
        _npTitleInput = NamePlate.CurrentTitle;
        _danInput = NamePlate.CurrentDan;
        _npIsGold = NamePlate.CurrentIsGold;
        _npDanFrame = NamePlate.CurrentDanFrame;
        _p2NameInput = NamePlate.CurrentP2Name;
        _p2NpTitleInput = NamePlate.CurrentP2Title;
        _p2DanInput = NamePlate.CurrentP2Dan;
        _p2NpIsGold = NamePlate.CurrentP2IsGold;
        _p2NpDanFrame = NamePlate.CurrentP2DanFrame;

        // タイトルテキスト初期値
        _titleTextInput = TitleScene.TitleGuideText;
    }

    public static void Toggle()
    {
        _isOpen = !_isOpen;
        if (!_isOpen)
        {
            _captureIndex = -1;
            _editingTitleText = false;
            _editingName = false;
            _editingNpTitle = false;
            _editingDan = false;
            _editingP2Name = false;
            _editingP2NpTitle = false;
            _editingP2Dan = false;
            _editingSearch = false;
            _inCategory = false;
            // 開いた直後にネームプレートの最新値を反映
            _nameInput = NamePlate.CurrentName;
            _npTitleInput = NamePlate.CurrentTitle;
            _danInput = NamePlate.CurrentDan;
            _npIsGold = NamePlate.CurrentIsGold;
            _npDanFrame = NamePlate.CurrentDanFrame;
            _p2NameInput = NamePlate.CurrentP2Name;
            _p2NpTitleInput = NamePlate.CurrentP2Title;
            _p2DanInput = NamePlate.CurrentP2Dan;
            _p2NpIsGold = NamePlate.CurrentP2IsGold;
            _p2NpDanFrame = NamePlate.CurrentP2DanFrame;
            _titleTextInput = TitleScene.TitleGuideText;
        }
    }

    public static void Update()
    {
        bool capturing = _captureIndex >= 0 && _isOpen;
        if (capturing)
        {
            int key;
            while ((key = Raylib.GetKeyPressed()) != 0)
            {
                if (key == (int)KeyboardKey.Escape || key == (int)KeyboardKey.F1)
                {
                    _captureIndex = -1;
                    break;
                }
                int vk = RaylibKeyToVk(key);
                if (vk == 0) continue;
                SetKey(_captureType, _captureIndex, vk);
                _captureIndex = -1;
                Save();
                break;
            }
        }

        bool anyTextEditing = _editingTitleText || _editingName || _editingNpTitle || _editingDan
            || _editingP2Name || _editingP2NpTitle || _editingP2Dan || _editingSearch;
        if (!capturing && (!anyTextEditing || !_isOpen) && Raylib.IsKeyPressed(KeyboardKey.F1))
            Toggle();

        // ---- テストメニュー風のカーソル移動 (MENUカテゴリ選択画面) ----
        if (_isOpen && !capturing && !anyTextEditing)
        {
            if (!_inCategory)
            {
                if (Raylib.IsKeyPressed(KeyboardKey.Down))
                    _menuCursor = (_menuCursor + 1) % CategoryLabels.Length;
                if (Raylib.IsKeyPressed(KeyboardKey.Up))
                    _menuCursor = (_menuCursor - 1 + CategoryLabels.Length) % CategoryLabels.Length;
                if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter)
                    || Raylib.IsKeyPressed(KeyboardKey.Right))
                {
                    _inCategory = true;
                    _scrollY = 0f;
                }
            }
            else
            {
                if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.Left))
                    _inCategory = false;
            }
        }

        // マウスホイールでスクロール
        if (_isOpen)
        {
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0)
            {
                _scrollY -= wheel * 40f;
            }
        }

        // ── 更新完了後の自動再起動カウントダウン ──
        if (_updateState == UpdateState.Updated && !_restartInitiated)
        {
            if (_restartCountdown <= 0f)
                _restartCountdown = RestartCountdownSec;

            _restartCountdown -= Raylib.GetFrameTime();

            if (_restartCountdown <= 0f)
            {
                _restartInitiated = true;
                InitiateRestart();
            }
        }

        float dt = Raylib.GetFrameTime();
        float target = _isOpen ? 1f : 0f;
        _openT += (target - _openT) * System.MathF.Min(1f, dt * 12f);
        if (System.MathF.Abs(target - _openT) < 0.002f)
            _openT = target;
    }

    public static void Draw()
    {
        if (_openT <= 0.002f)
        {
            ApplySettings();
            return;
        }

        var (vx, vy, vw, vh) = Songs.GetViewport();
        float scale = vw / 1920f;
        float dt = Raylib.GetFrameTime();

        int px = vx, py = vy, pw = vw, ph = vh;

        // 業務用テストモード風: 全画面黒背景
        Raylib.DrawRectangle(px, py, pw, ph, new Color(0, 0, 0, (int)(255 * _openT)));
        if (_openT < 0.999f)
        {
            DrawRipple(dt);
            return;
        }

        // タイトル (中央上)
        string title = _inCategory ? CategoryLabels[_menuCursor] : "MENU";
        var titleSize = Raylib.MeasureTextEx(G.FontSub, title, 40 * scale, 5);
        Raylib.DrawTextEx(G.FontSub, title,
            new Vector2(px + pw / 2f - titleSize.X / 2f, py + 40 * scale), 44 * scale, 3, Color.White);

        float mx = px + pw * 0.20f;
        float cy = py + 200 * scale;

        if (!_inCategory)
        {
            // ---- カテゴリ一覧 (COIN OPTIONS 等と同じ見た目) ----
            // 選択中の項目は0.5秒ごとに赤/白で点滅 (実機テストメニュー風)
            bool blinkOn = (Raylib.GetTime() % 1.0) < 0.5;
            for (int i = 0; i < CategoryLabels.Length; i++)
            {
                bool sel = i == _menuCursor;
                Color col = sel ? (blinkOn ? TestMenuRed : Color.White) : Color.White;
                Raylib.DrawTextEx(G.FontSub, CategoryLabels[i], new Vector2(mx, cy), 40 * scale, 2, col);
                cy += 35 * scale;
            }

            cy += 35 * scale;
            Raylib.DrawTextEx(G.FontSub, "LAST GAME STATUS", new Vector2(mx, cy), 40 * scale, 2, Color.White);
            cy += 35 * scale;
            Raylib.DrawTextEx(G.FontSub, $"    LEFT CREDIT          {(Coin.FreePlay ? 0 : Coin.RequiredCoins)}",
                new Vector2(mx, cy), 40 * scale, 4, Color.White);
            cy += 35 * scale;
            Raylib.DrawTextEx(G.FontSub, $"    USE CREDIT    　     0",
                new Vector2(mx, cy), 40 * scale, 4, Color.White);

            string hint = "SELECT SW:CHOOSE  ENTER SW:ENTER";
            var hs = Raylib.MeasureTextEx(G.FontSub, hint, 44 * scale, 2);
            Raylib.DrawTextEx(G.FontSub, hint,
                new Vector2(px + pw / 2f - hs.X / 2f, py + ph - 300 * scale), 40 * scale, 2, Color.White);
        }
        else
        {
            // ---- カテゴリ詳細 (従来の設定項目をそのまま流用) ----
            float dw = pw * 0.6f;
            switch (_menuCursor)
            {
                case 0: // DISPLAY & FPS OPTIONS
                    cy = DrawSwitchRow(mx, cy, dw, scale, dt, "フルスクリーン", null, ref IsFullscreen, ref _fullscreenT);
                    cy = DrawSubheader(mx, cy, scale, "ウィンドウサイズ");
                    for (int i = 0; i < ResolutionOptions.Length; i++)
                        cy = DrawResolutionRow(mx, cy, dw, scale, i);
                    cy = DrawDivider(mx, cy, dw, scale);
                    cy = DrawSubheader(mx, cy, scale, "FPS上限");
                    foreach (int opt in FpsOptions)
                        cy = DrawRadioRow(mx, cy, dw, scale, opt);
                    break;

                case 1: // SOUND & VOLUME OPTIONS
                    cy = DrawAudioBackendRow(mx, cy, dw, scale, AudioBackend.Wasapi, "WASAPI");
                    cy = DrawAudioBackendRow(mx, cy, dw, scale, AudioBackend.Asio, "ASIO");
                    cy = DrawAudioBackendRow(mx, cy, dw, scale, AudioBackend.DirectSound, "DirectSound");
                    if (OutputBackend == AudioBackend.Wasapi)
                        cy = DrawSwitchRow(mx, cy, dw, scale, dt, "WASAPI 排他モード", "低遅延ですが他アプリの音が止まります",
                            ref UseWasapiExclusive, ref _wasapiT);
                    if (OutputBackend == AudioBackend.Asio)
                        cy = DrawAsioDriverPicker(mx, cy, dw, scale);
                    cy = DrawSubheader(mx, cy, scale, "サンプルレート");
                    cy = DrawSampleRatePicker(mx, cy, dw, scale);
                    cy = DrawSubheader(mx, cy, scale, "バッファサイズ");
                    cy = DrawBufferSizeSlider(mx, cy, dw, scale);
                    if (!string.IsNullOrEmpty(AudioEngine.LastError))
                        Raylib.DrawTextEx(G.FontSub, $"⚠ {AudioEngine.LastError}",
                            new Vector2(mx + 24 * scale, cy), 15 * scale, 2, TestMenuRed);
                    cy = DrawDivider(mx, cy, dw, scale);
                    cy = DrawSubheader(mx, cy, scale, "音量");
                    cy = DrawVolumeSlider(mx, cy, dw, scale, "マスター", ref MasterVolume);
                    cy = DrawVolumeSlider(mx, cy, dw, scale, "BGM", ref BgmVolume);
                    cy = DrawVolumeSlider(mx, cy, dw, scale, "SE", ref SeVolume);
                    break;

                case 2: // COIN OPTIONS
                    cy = DrawSwitchRow(mx, cy, dw, scale, dt, "フリープレイ", "コインを使わずに遊べます", ref Coin.FreePlay, ref _freePlayT);
                    if (!Coin.FreePlay)
                        cy = DrawCoinStepper(mx, cy, dw, scale);
                    break;

                case 3: // GAME CONFIG
                    cy = DrawSubheader(mx, cy, scale, "曲フォルダ形式 (SongsType)");
                    cy = DrawSongsTypeRow(mx, cy, dw, scale, SongsTypeOption.TJA, "TJA  (Songs/)");
                    cy = DrawSongsTypeRow(mx, cy, dw, scale, SongsTypeOption.ESE, "ESE  (Songs/ESE/)");
                    cy = DrawDivider(mx, cy, dw, scale);
                    cy = DrawSubheader(mx, cy, scale, "2P AI演奏");
                    cy = DrawAiLevelStepper(mx, cy, dw, scale);
                    cy = DrawDivider(mx, cy, dw, scale);
                    cy = DrawSubheader(mx, cy, scale, "1P キー設定");
                    cy = DrawKeyColumn(mx, cy, dw, scale, 0, "左面 (Don)", KeyLeft);
                    cy = DrawKeyColumn(mx, cy, dw, scale, 1, "右面 (Don)", KeyRight);
                    cy = DrawKeyColumn(mx, cy, dw, scale, 2, "左縁 (Ka)", KeyEdgeL);
                    cy = DrawKeyColumn(mx, cy, dw, scale, 3, "右縁 (Ka)", KeyEdgeR);
                    cy = DrawDivider(mx, cy, dw, scale);
                    cy = DrawSubheader(mx, cy, scale, "2P キー設定");
                    cy = DrawKeyColumn(mx, cy, dw, scale, 4, "左面 (Don)", P2KeyLeft);
                    cy = DrawKeyColumn(mx, cy, dw, scale, 5, "右面 (Don)", P2KeyRight);
                    cy = DrawKeyColumn(mx, cy, dw, scale, 6, "左縁 (Ka)", P2KeyEdgeL);
                    cy = DrawKeyColumn(mx, cy, dw, scale, 7, "右縁 (Ka)", P2KeyEdgeR);
                    break;

                case 4: // TITLE TEXT & NAME PLATE
                    cy = DrawSubheader(mx, cy, scale, "タイトルテキスト");
                    cy = DrawTextFieldRow(mx, cy, dw, scale, "ガイド文字", ref _titleTextInput, ref _editingTitleText,
                        newVal => { TitleScene.TitleGuideText = newVal; SaveTitleConfig(); });
                    cy = DrawDivider(mx, cy, dw, scale);
                    cy = DrawSubheader(mx, cy, scale, "1P ネームプレート");
                    cy = DrawTextFieldRow(mx, cy, dw, scale, "プレイヤー名", ref _nameInput, ref _editingName, _ => SaveNamePlate());
                    cy = DrawTextFieldRow(mx, cy, dw, scale, "称号", ref _npTitleInput, ref _editingNpTitle, _ => SaveNamePlate());
                    cy = DrawTextFieldRow(mx, cy, dw, scale, "段位名", ref _danInput, ref _editingDan, _ => SaveNamePlate());
                    cy = DrawNpIsGoldRow(mx, cy, dw, scale, dt);
                    cy = DrawNpDanFrameStepper(mx, cy, dw, scale);
                    cy = DrawDivider(mx, cy, dw, scale);
                    cy = DrawSubheader(mx, cy, scale, "2P ネームプレート（Data/PlayData2.json）");
                    cy = DrawTextFieldRow(mx, cy, dw, scale, "プレイヤー名", ref _p2NameInput, ref _editingP2Name, _ => SaveNamePlateP2());
                    cy = DrawTextFieldRow(mx, cy, dw, scale, "称号", ref _p2NpTitleInput, ref _editingP2NpTitle, _ => SaveNamePlateP2());
                    cy = DrawTextFieldRow(mx, cy, dw, scale, "段位名", ref _p2DanInput, ref _editingP2Dan, _ => SaveNamePlateP2());
                    cy = DrawP2NpIsGoldRow(mx, cy, dw, scale, dt);
                    cy = DrawP2NpDanFrameStepper(mx, cy, dw, scale);
                    break;

                case 5: // SONG SEARCH
                    cy = DrawSearchRow(mx, cy, dw, scale);
                    break;

                case 6: // SONG LIST
                    cy = DrawReloadSongsButton(mx, cy, dw, scale, dt);
                    break;

                case 7: // SCENE JUMP
                    cy = DrawSceneJumpRow(mx, cy, dw, scale);
                    break;

                case 8: // VRAM DEBUG
                    for (int i = 0; i < VramModeLabels.Length; i++)
                        cy = DrawVramModeRow(mx, cy, dw, scale, i);
                    break;

                case 9: // UPDATE
                    cy = DrawUpdateCheckRow(mx, cy, dw, scale, dt);
                    break;

                case 10: // PERFORMANCE
                    cy = DrawSubheader(mx, cy, scale, "ダンサーのFPS");
                    foreach (int opt in DancerFpsOptions)
                        cy = DrawIntRadioRow(mx, cy, dw, scale, opt, ref DancerFps, $"{opt} FPS");
                    cy = DrawDivider(mx, cy, dw, scale);
                    cy = DrawSubheader(mx, cy, scale, "ダンサーの画質");
                    cy = DrawBoolRadioRow(mx, cy, dw, scale, false, ref DancerLowQuality, "High");
                    cy = DrawBoolRadioRow(mx, cy, dw, scale, true, ref DancerLowQuality, "Low");
                    cy = DrawDivider(mx, cy, dw, scale);
                    cy = DrawSubheader(mx, cy, scale, "どんちゃんのFPS");
                    foreach (int opt in DonChanFpsOptions)
                        cy = DrawIntRadioRow(mx, cy, dw, scale, opt, ref DonChanFps, $"{opt} FPS");
                    cy = DrawDivider(mx, cy, dw, scale);
                    cy = DrawSubheader(mx, cy, scale, "どんちゃんの画質");
                    cy = DrawBoolRadioRow(mx, cy, dw, scale, false, ref DonChanLowQuality, "High");
                    cy = DrawBoolRadioRow(mx, cy, dw, scale, true, ref DonChanLowQuality, "Low");
                    break;
            }

            string hint2 = "←/ESC:BACK   F1:CLOSE";
            var hs2 = Raylib.MeasureTextEx(G.FontSub, hint2, 20 * scale, 2);
            Raylib.DrawTextEx(G.FontSub, hint2,
                new Vector2(px + pw / 2f - hs2.X / 2f, py + ph - 60 * scale), 20 * scale, 2, TextSecondary);
        }

        DrawRipple(dt);
        ApplySettings();
    }

    private static float DrawSubheader(float x, float y, float scale, string text)
    {
        float h = 46 * scale;
        Raylib.DrawTextEx(G.FontSub, text, new Vector2(x + 24 * scale, y + h - 26 * scale), 18 * scale, 2, Accent);
        return y + h;
    }

    private static float DrawDivider(float x, float y, float w, float scale)
    {
        Raylib.DrawRectangle((int)x, (int)(y + 4 * scale), (int)w, System.Math.Max(1, (int)(1 * scale)), Divider);
        return y + 9 * scale;
    }

    private static float DrawSwitchRow(float x, float y, float w, float scale, float dt,
        string label, string? sub, ref bool value, ref float animT)
    {
        float h = (sub == null ? 64f : 84f) * scale;
        var rect = new Rectangle(x, y, w, h);
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);

        if (hover)
            Raylib.DrawRectangleRec(rect, HoverTint);

        float labelY = sub == null ? y + (h - 24 * scale) / 2f : y + 16 * scale;
        Raylib.DrawTextEx(G.FontSub, label, new Vector2(x + 24 * scale, labelY), 24 * scale, 2, TextPrimary);
        if (sub != null)
            Raylib.DrawTextEx(G.FontSub, sub, new Vector2(x + 24 * scale, y + 48 * scale), 17 * scale, 2, TextSecondary);

        if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            value = !value;
            StartRipple(rect);
        }

        float target = value ? 1f : 0f;
        animT += (target - animT) * System.MathF.Min(1f, dt * 16f);
        if (System.MathF.Abs(target - animT) < 0.001f)
            animT = target;

        // MD1 スイッチ: 細いトラックの上を大きめのサムが動く
        float trackW = 36 * scale, trackH = 14 * scale;
        float thumbR = 11 * scale;
        float swX = x + w - trackW - 28 * scale;
        float swY = y + (h - trackH) / 2f;

        Color trackCol = LerpColor(TrackOff, AccentHalf, animT);
        Raylib.DrawRectangleRounded(new Rectangle(swX, swY, trackW, trackH), 1f, 8, trackCol);

        float thumbX = swX + animT * trackW;
        float thumbY = swY + trackH / 2f;
        Raylib.DrawCircleV(new Vector2(thumbX + 1.5f * scale, thumbY + 2 * scale), thumbR, new Color(0, 0, 0, 80));
        Raylib.DrawCircleV(new Vector2(thumbX, thumbY), thumbR, LerpColor(ThumbOff, Accent, animT));

        return y + h;
    }

    private static float DrawRadioRow(float x, float y, float w, float scale, int option)
    {
        float h = 48 * scale;
        var rect = new Rectangle(x, y, w, h);
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
        bool selected = FpsLimit == option;

        if (hover)
            Raylib.DrawRectangleRec(rect, HoverTint);

        float cx = x + 36 * scale;
        float cyMid = y + h / 2f;
        float outerR = 11 * scale;

        Color ring = selected ? Accent : new Color(0, 0, 0, 138);
        Raylib.DrawRing(new Vector2(cx, cyMid), outerR - 2.2f * scale, outerR, 0, 360, 32, ring);
        if (selected)
            Raylib.DrawCircleV(new Vector2(cx, cyMid), outerR - 5.5f * scale, Accent);

        string label = option == 0 ? "無制限" : $"{option} FPS";
        Raylib.DrawTextEx(G.FontSub, label,
            new Vector2(cx + outerR + 20 * scale, cyMid - 11 * scale), 22 * scale, 2, TextPrimary);

        if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            FpsLimit = option;
            StartRipple(rect);
        }

        return y + h;
    }

    /// <summary>DrawRadioRowの汎用版。任意のint参照変数を対象にラジオボタン行を1つ描く。</summary>
    private static float DrawIntRadioRow(float x, float y, float w, float scale, int option, ref int target, string label)
    {
        float h = 48 * scale;
        var rect = new Rectangle(x, y, w, h);
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
        bool selected = target == option;

        if (hover)
            Raylib.DrawRectangleRec(rect, HoverTint);

        float cx = x + 36 * scale;
        float cyMid = y + h / 2f;
        float outerR = 11 * scale;

        Color ring = selected ? Accent : new Color(0, 0, 0, 138);
        Raylib.DrawRing(new Vector2(cx, cyMid), outerR - 2.2f * scale, outerR, 0, 360, 32, ring);
        if (selected)
            Raylib.DrawCircleV(new Vector2(cx, cyMid), outerR - 5.5f * scale, Accent);

        Raylib.DrawTextEx(G.FontSub, label,
            new Vector2(cx + outerR + 20 * scale, cyMid - 11 * scale), 22 * scale, 2, TextPrimary);

        if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            target = option;
            StartRipple(rect);
        }

        return y + h;
    }

    /// <summary>DrawIntRadioRowのbool版(二択の画質設定などに使用)。</summary>
    private static float DrawBoolRadioRow(float x, float y, float w, float scale, bool option, ref bool target, string label)
    {
        float h = 48 * scale;
        var rect = new Rectangle(x, y, w, h);
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
        bool selected = target == option;

        if (hover)
            Raylib.DrawRectangleRec(rect, HoverTint);

        float cx = x + 36 * scale;
        float cyMid = y + h / 2f;
        float outerR = 11 * scale;

        Color ring = selected ? Accent : new Color(0, 0, 0, 138);
        Raylib.DrawRing(new Vector2(cx, cyMid), outerR - 2.2f * scale, outerR, 0, 360, 32, ring);
        if (selected)
            Raylib.DrawCircleV(new Vector2(cx, cyMid), outerR - 5.5f * scale, Accent);

        Raylib.DrawTextEx(G.FontSub, label,
            new Vector2(cx + outerR + 20 * scale, cyMid - 11 * scale), 22 * scale, 2, TextPrimary);

        if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            target = option;
            StartRipple(rect);
        }

        return y + h;
    }

    private static float DrawResolutionRow(float x, float y, float w, float scale, int index)
    {
        float h = 48 * scale;
        var rect = new Rectangle(x, y, w, h);
        bool disabled = IsFullscreen; // フルスクリーン中はウィンドウサイズ変更不可
        bool hover = !disabled && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
        bool selected = ResolutionIndex == index;

        if (hover)
            Raylib.DrawRectangleRec(rect, HoverTint);

        float cx = x + 36 * scale;
        float cyMid = y + h / 2f;
        float outerR = 11 * scale;

        Color ring = disabled ? new Color(0, 0, 0, 60) : (selected ? Accent : new Color(0, 0, 0, 138));
        Raylib.DrawRing(new Vector2(cx, cyMid), outerR - 2.2f * scale, outerR, 0, 360, 32, ring);
        if (selected)
            Raylib.DrawCircleV(new Vector2(cx, cyMid), outerR - 5.5f * scale, disabled ? new Color(0, 0, 0, 60) : Accent);

        var opt = ResolutionOptions[index];
        string label = $"{opt.W} x {opt.H} ({opt.Label})";
        Color textColor = disabled ? TextSecondary : TextPrimary;
        Raylib.DrawTextEx(G.FontSub, label,
            new Vector2(cx + outerR + 20 * scale, cyMid - 11 * scale), 22 * scale, 2, textColor);

        if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            ResolutionIndex = index;
            StartRipple(rect);
        }

        return y + h;
    }

    private static float DrawAsioDriverPicker(float x, float y, float w, float scale)
    {
        float h = 56 * scale;
        var rowRect = new Rectangle(x, y, w, h);
        bool rowHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rowRect);
        if (rowHover)
            Raylib.DrawRectangleRec(rowRect, HoverTint);

        var drivers = AudioEngine.GetAsioDriverNames();
        string label = "ASIOドライバ";
        Raylib.DrawTextEx(G.FontSub, label,
            new Vector2(x + 24 * scale, y + (h - 24 * scale) / 2f), 22 * scale, 2, TextPrimary);

        if (drivers.Length == 0)
        {
            Raylib.DrawTextEx(G.FontSub, "見つかりません",
                new Vector2(x + 220 * scale, y + (h - 20 * scale) / 2f), 18 * scale, 2, TextSecondary);
            return y + h;
        }

        AsioDriverIndex = Math.Clamp(AsioDriverIndex, 0, drivers.Length - 1);

        float btnSize = 28 * scale;
        float minusX = x + 220 * scale;
        float minusY = y + (h - btnSize) / 2f;
        var minusRect = new Rectangle(minusX, minusY, btnSize, btnSize);
        bool minusHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), minusRect);

        Color minusColor = minusHover ? Accent : new Color(60, 60, 60, 255);
        Raylib.DrawRectangleRounded(minusRect, 0.5f, 4, minusColor);
        Raylib.DrawTextEx(G.FontSub, "<", new Vector2(minusX + 8 * scale, minusY + 4 * scale), 20 * scale, 2, Color.White);

        if (minusHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            AsioDriverIndex = (AsioDriverIndex - 1 + drivers.Length) % drivers.Length;
            StartRipple(minusRect);
        }

        string name = drivers[AsioDriverIndex];
        var ts = Raylib.MeasureTextEx(G.FontSub, name, 18 * scale, 2);
        float nameW = System.MathF.Min(ts.X, w - 340 * scale);
        float nameX = minusX + btnSize + 12 * scale;
        Raylib.BeginScissorMode((int)nameX, (int)y, (int)(w - 340 * scale), (int)h);
        Raylib.DrawTextEx(G.FontSub, name, new Vector2(nameX, y + (h - 18 * scale) / 2f), 18 * scale, 2, TextPrimary);
        Raylib.EndScissorMode();

        float plusX = x + w - 24 * scale - btnSize;
        var plusRect = new Rectangle(plusX, minusY, btnSize, btnSize);
        bool plusHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), plusRect);

        Color plusColor = plusHover ? Accent : new Color(60, 60, 60, 255);
        Raylib.DrawRectangleRounded(plusRect, 0.5f, 4, plusColor);
        Raylib.DrawTextEx(G.FontSub, ">", new Vector2(plusX + 8 * scale, minusY + 4 * scale), 20 * scale, 2, Color.White);

        if (plusHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            AsioDriverIndex = (AsioDriverIndex + 1) % drivers.Length;
            StartRipple(plusRect);
        }

        return y + h;
    }

    private static float DrawSampleRatePicker(float x, float y, float w, float scale)
    {
        float h = 56 * scale;
        var rowRect = new Rectangle(x, y, w, h);
        bool rowHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rowRect);
        if (rowHover)
            Raylib.DrawRectangleRec(rowRect, HoverTint);

        Raylib.DrawTextEx(G.FontSub, "サンプルレート",
            new Vector2(x + 24 * scale, y + (h - 24 * scale) / 2f), 22 * scale, 2, TextPrimary);

        SampleRateIndex = Math.Clamp(SampleRateIndex, 0, SampleRateOptions.Length - 1);

        float btnSize = 28 * scale;
        float minusX = x + 220 * scale;
        float minusY = y + (h - btnSize) / 2f;
        var minusRect = new Rectangle(minusX, minusY, btnSize, btnSize);
        bool minusHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), minusRect);

        Color minusColor = minusHover ? Accent : new Color(60, 60, 60, 255);
        Raylib.DrawRectangleRounded(minusRect, 0.5f, 4, minusColor);
        Raylib.DrawTextEx(G.FontSub, "<", new Vector2(minusX + 8 * scale, minusY + 4 * scale), 20 * scale, 2, Color.White);

        if (minusHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            SampleRateIndex = (SampleRateIndex - 1 + SampleRateOptions.Length) % SampleRateOptions.Length;
            StartRipple(minusRect);
        }

        string name = $"{SampleRateOptions[SampleRateIndex]}Hz";
        var ts = Raylib.MeasureTextEx(G.FontSub, name, 18 * scale, 2);
        float nameW = System.MathF.Min(ts.X, w - 340 * scale);
        float nameX = minusX + btnSize + 12 * scale;
        Raylib.BeginScissorMode((int)nameX, (int)y, (int)(w - 340 * scale), (int)h);
        Raylib.DrawTextEx(G.FontSub, name, new Vector2(nameX, y + (h - 18 * scale) / 2f), 18 * scale, 2, TextPrimary);
        Raylib.EndScissorMode();

        float plusX = x + w - 24 * scale - btnSize;
        var plusRect = new Rectangle(plusX, minusY, btnSize, btnSize);
        bool plusHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), plusRect);

        Color plusColor = plusHover ? Accent : new Color(60, 60, 60, 255);
        Raylib.DrawRectangleRounded(plusRect, 0.5f, 4, plusColor);
        Raylib.DrawTextEx(G.FontSub, ">", new Vector2(plusX + 8 * scale, minusY + 4 * scale), 20 * scale, 2, Color.White);

        if (plusHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            SampleRateIndex = (SampleRateIndex + 1) % SampleRateOptions.Length;
            StartRipple(plusRect);
        }

        float extraH = 0f;
        if (!SettingsPanel.UseWasapiExclusive && OutputBackend == AudioBackend.Wasapi)
        {
            Raylib.DrawTextEx(G.FontSub, "※WASAPI共有モードではデバイス側の形式が優先されます",
                new Vector2(x + 24 * scale, y + h), 15 * scale, 2, TextSecondary);
            extraH = 24 * scale;
        }

        return y + h + extraH;
    }

    private static float DrawAudioBackendRow(float x, float y, float w, float scale, AudioBackend option, string label)
    {
        float h = 48 * scale;
        var rect = new Rectangle(x, y, w, h);
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
        bool selected = OutputBackend == option;

        if (hover)
            Raylib.DrawRectangleRec(rect, HoverTint);

        float cx = x + 36 * scale;
        float cyMid = y + h / 2f;
        float outerR = 11 * scale;

        Color ring = selected ? Accent : new Color(0, 0, 0, 138);
        Raylib.DrawRing(new Vector2(cx, cyMid), outerR - 2.2f * scale, outerR, 0, 360, 32, ring);
        if (selected)
            Raylib.DrawCircleV(new Vector2(cx, cyMid), outerR - 5.5f * scale, Accent);

        Raylib.DrawTextEx(G.FontSub, label,
            new Vector2(cx + outerR + 20 * scale, cyMid - 11 * scale), 22 * scale, 2, TextPrimary);

        if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            OutputBackend = option;
            StartRipple(rect);
        }

        return y + h;
    }

    private static float DrawBufferSizeSlider(float x, float y, float w, float scale)
    {
        const int MinMs = 5, MaxMs = 100;

        float h = 40 * scale;
        float sliderH = 20 * scale;
        float trackW = w - 48 * scale;
        float trackX = x + 24 * scale;
        float trackY = y + h / 2f - sliderH / 2f;

        Raylib.DrawRectangleRounded(new Rectangle(trackX, trackY, trackW, sliderH), 0.5f, 4, TrackOff);

        float t = (BufferSizeMs - MinMs) / (float)(MaxMs - MinMs);
        float thumbX = trackX + t * trackW;
        Raylib.DrawCircleV(new Vector2(thumbX + sliderH / 2f, trackY + sliderH / 2f), sliderH / 2f, Accent);

        Raylib.DrawTextEx(G.FontSub, $"{BufferSizeMs}ms",
            new Vector2(thumbX + sliderH + 8 * scale, trackY + (sliderH - 16 * scale) / 2f),
            16 * scale, 2, TextPrimary);

        var trackRect = new Rectangle(trackX, trackY, trackW, sliderH);
        bool overTrack = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), trackRect);

        if (overTrack && Raylib.IsMouseButtonPressed(MouseButton.Left))
            _bufferDragging = true;

        if (_bufferDragging && Raylib.IsMouseButtonDown(MouseButton.Left))
        {
            float nt = (Raylib.GetMousePosition().X - trackX) / trackW;
            BufferSizeMs = Math.Clamp(MinMs + (int)MathF.Round(nt * (MaxMs - MinMs)), MinMs, MaxMs);
        }

        if (Raylib.IsMouseButtonReleased(MouseButton.Left))
            _bufferDragging = false;

        float extraH = 0f;
        if (OutputBackend == AudioBackend.Asio)
        {
            Raylib.DrawTextEx(G.FontSub, "※ASIOはドライバ設定のバッファが優先されます",
                new Vector2(x + 24 * scale, y + h), 15 * scale, 2, TextSecondary);
            extraH = 24 * scale;
        }

        return y + h + extraH + 20 * scale;
    }

    /// <summary>
    /// 「Coin(s)」の必要コイン数を +/- ボタンで増減させる行。
    /// </summary>
    private static float DrawCoinStepper(float x, float y, float w, float scale)
    {
        float h = 56 * scale;
        var rowRect = new Rectangle(x, y, w, h);
        bool rowHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rowRect);
        if (rowHover)
            Raylib.DrawRectangleRec(rowRect, HoverTint);

        Raylib.DrawTextEx(G.FontSub, "Coin(s)",
            new Vector2(x + 24 * scale, y + (h - 24 * scale) / 2f), 22 * scale, 2, TextPrimary);

        float btnSize = 28 * scale;
        float valueW = 48 * scale;
        float minusX = x + w - 24 * scale - btnSize - valueW - 12 * scale - btnSize;
        float minusY = y + (h - btnSize) / 2f;
        var minusRect = new Rectangle(minusX, minusY, btnSize, btnSize);
        bool minusHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), minusRect);

        Color minusColor = minusHover ? Accent : new Color(60, 60, 60, 255);
        Raylib.DrawRectangleRounded(minusRect, 0.5f, 4, minusColor);
        Raylib.DrawTextEx(G.FontSub, "-", new Vector2(minusX + 9 * scale, minusY + 4 * scale), 20 * scale, 2, Color.White);

        if (minusHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            Coin.RequiredCoins = System.Math.Max(1, Coin.RequiredCoins - 1);
            Coin.Save();
            StartRipple(minusRect);
        }

        string valueText = Coin.RequiredCoins.ToString();
        var vs = Raylib.MeasureTextEx(G.FontSub, valueText, 22 * scale, 2);
        float valueX = minusX + btnSize + 12 * scale;
        Raylib.DrawTextEx(G.FontSub, valueText,
            new Vector2(valueX + (valueW - vs.X) / 2f, y + (h - vs.Y) / 2f), 22 * scale, 2, TextPrimary);

        float plusX = valueX + valueW + 12 * scale;
        var plusRect = new Rectangle(plusX, minusY, btnSize, btnSize);
        bool plusHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), plusRect);

        Color plusColor = plusHover ? Accent : new Color(60, 60, 60, 255);
        Raylib.DrawRectangleRounded(plusRect, 0.5f, 4, plusColor);
        Raylib.DrawTextEx(G.FontSub, "+", new Vector2(plusX + 8 * scale, minusY + 4 * scale), 20 * scale, 2, Color.White);

        if (plusHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            Coin.RequiredCoins = System.Math.Min(99, Coin.RequiredCoins + 1);
            Coin.Save();
            StartRipple(plusRect);
        }

        return y + h;
    }

    /// <summary>2P AIレベルを0〜10で設定する。0=完全手動、10=完全AUTO、1〜9=人間らしいミスを伴う自動演奏。</summary>
    private static float DrawAiLevelStepper(float x, float y, float w, float scale)
    {
        float h = 56 * scale;
        var rowRect = new Rectangle(x, y, w, h);
        bool rowHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rowRect);
        if (rowHover) Raylib.DrawRectangleRec(rowRect, HoverTint);

        string label = AILevel <= 0 ? "2P AIレベル 0（完全手動）"
            : AILevel >= 10 ? "2P AIレベル 10（完全AUTO）"
            : $"2P AIレベル {AILevel}（人間らしいミスあり）";
        Raylib.DrawTextEx(G.FontSub, label,
            new Vector2(x + 24 * scale, y + (h - 24 * scale) / 2f), 18 * scale, 2, TextPrimary);

        float btnSize = 28 * scale;
        float minusX = x + w - 24 * scale - btnSize * 2 - 40 * scale;
        float minusY = y + (h - btnSize) / 2f;
        var minusRect = new Rectangle(minusX, minusY, btnSize, btnSize);
        bool minusHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), minusRect);
        Raylib.DrawRectangleRounded(minusRect, 0.5f, 4, minusHover ? Accent : new Color(60, 60, 60, 255));
        Raylib.DrawTextEx(G.FontSub, "-", new Vector2(minusX + 9 * scale, minusY + 4 * scale), 20 * scale, 2, Color.White);
        if (minusHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            AILevel = Math.Max(0, AILevel - 1);
            Save();
            StartRipple(minusRect);
        }

        string valueText = AILevel.ToString();
        var vs = Raylib.MeasureTextEx(G.FontSub, valueText, 22 * scale, 2);
        float valueX = minusX + btnSize + 8 * scale;
        Raylib.DrawTextEx(G.FontSub, valueText,
            new Vector2(valueX + (28 * scale - vs.X) / 2f, y + (h - vs.Y) / 2f), 22 * scale, 2, TextPrimary);

        float plusX = valueX + 36 * scale;
        var plusRect = new Rectangle(plusX, minusY, btnSize, btnSize);
        bool plusHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), plusRect);
        Raylib.DrawRectangleRounded(plusRect, 0.5f, 4, plusHover ? Accent : new Color(60, 60, 60, 255));
        Raylib.DrawTextEx(G.FontSub, "+", new Vector2(plusX + 8 * scale, minusY + 4 * scale), 20 * scale, 2, Color.White);
        if (plusHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            AILevel = Math.Min(10, AILevel + 1);
            Save();
            StartRipple(plusRect);
        }

        return y + h;
    }

    private static float DrawKeyColumn(float x, float y, float w, float scale, int type, string label, List<int> keys)
    {
        float chipH = 32 * scale;
        float chipW = 56 * scale;
        float rowH = 22 * scale + keys.Count * (chipH + 12 * scale) + 12 * scale;

        // 行の背景ホバー
        var rowRect = new Rectangle(x, y, w, rowH);
        bool rowHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rowRect);
        if (rowHover)
            Raylib.DrawRectangleRec(rowRect, HoverTint);

        // ラベル
        Raylib.DrawTextEx(G.FontSub, label,
            new Vector2(x + 24 * scale, y + 4 * scale), 22 * scale, 2, TextPrimary);

        float keyX = x + 120 * scale;

        // 各キーを縦に並べる（左揃え、上から下へ）
        for (int i = 0; i < keys.Count; i++)
        {
            string keyText = VkName(keys[i]);
            float fsz = 19 * scale;
            var ts = Raylib.MeasureTextEx(G.FontSub, keyText, fsz, 2);
            float actualChipW = System.MathF.Max(ts.X + 28 * scale, 56 * scale);
            float chipY = y + 22 * scale + i * (chipH + 12 * scale) + 10 * scale;

            var keyRect = new Rectangle(keyX, chipY, actualChipW, chipH);
            bool keyHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), keyRect);

            if (keyHover)
                Raylib.DrawRectangleRec(keyRect, HoverTint);

            Raylib.DrawRectangleRounded(keyRect, 0.4f, 8, new Color(50, 50, 50, 255));
            Raylib.DrawTextEx(G.FontSub, keyText,
                new Vector2(keyX + (actualChipW - ts.X) / 2f, chipY + (chipH - fsz) / 2f), fsz, 2, TextPrimary);

            // 削除ボタン
            float delBtnSize = 24 * scale;
            float delBtnX = keyX + actualChipW + 12 * scale;
            float delBtnY = chipY + (chipH - delBtnSize) / 2f + 8 * scale;
            var delBtnRect = new Rectangle(delBtnX, delBtnY, delBtnSize, delBtnSize);
            bool delHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), delBtnRect);

            Color delBtnColor = delHover ? Accent : new Color(60, 60, 60, 255);
            Raylib.DrawRectangleRounded(delBtnRect, 0.5f, 4, delBtnColor);
            Raylib.DrawTextEx(G.FontSub, "-",
                new Vector2(delBtnX + 8 * scale, delBtnY + 6 * scale), 18 * scale, 2, Color.White);

            // 削除ボタンクリック
            if (delHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                lock (KeyBindingsSync)
                {
                    keys.RemoveAt(i);
                    RefreshKeyBindingSnapshotLocked();
                }
                Save();
                StartRipple(delBtnRect);
                return y + rowH;
            }

            // キークリックで編集モード
            if (keyHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _captureType = type;
                _captureIndex = i;
                StartRipple(keyRect);
            }
        }

        // + ボタン
        float btnSize = 24 * scale;
        float addBtnX = x + 300 * scale;
        float addBtnY = y + 22 * scale + (keys.Count > 0 ? (keys.Count - 1) * (chipH + 12 * scale) + chipH + 10 * scale : 10 * scale);
        var addBtnRect = new Rectangle(addBtnX, addBtnY, btnSize, btnSize);
        bool addHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), addBtnRect);

        Color addBtnColor = addHover ? Accent : new Color(60, 60, 60, 255);
        Raylib.DrawRectangleRounded(addBtnRect, 0.5f, 4, addBtnColor);
        Raylib.DrawTextEx(G.FontSub, "+",
            new Vector2(addBtnX + 8 * scale, addBtnY + 6 * scale), 18 * scale, 2, Color.White);

        // + ボタンクリック
        if (addHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            lock (KeyBindingsSync)
            {
                keys.Add(0);
                RefreshKeyBindingSnapshotLocked();
                _captureType = type;
                _captureIndex = keys.Count - 1;
            }
            Save();
            StartRipple(addBtnRect);
        }

        return y + rowH;
    }

    private static float DrawVolumeSlider(float x, float y, float w, float scale, string label, ref int value)
    {
        float labelH = 26 * scale;
        Raylib.DrawTextEx(G.FontSub, label,
            new Vector2(x + 24 * scale, y), 16 * scale, 2, TextSecondary);

        float h = 40 * scale;
        float sliderH = 20 * scale;
        float trackW = w - 48 * scale;
        float trackX = x + 24 * scale;
        float trackY = y + labelH + h / 2f - sliderH / 2f;

        Raylib.DrawRectangleRounded(new Rectangle(trackX, trackY, trackW, sliderH), 0.5f, 4, TrackOff);

        float thumbX = trackX + value / 100f * trackW;
        Raylib.DrawCircleV(new Vector2(thumbX + sliderH / 2f, trackY + sliderH / 2f),
            sliderH / 2f, Accent);

        Raylib.DrawTextEx(G.FontSub, $"{value}%",
            new Vector2(thumbX + sliderH + 8 * scale, trackY + (sliderH - 16 * scale) / 2f),
            16 * scale, 2, TextPrimary);

        // クリックまたは長押しで音量設定
        if (Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(),
            new Rectangle(trackX, trackY, trackW, sliderH)) && Raylib.IsMouseButtonDown(MouseButton.Left))
        {
            value = (int)((Raylib.GetMousePosition().X - trackX) / trackW * 100);
            value = Math.Clamp(value, 0, 100);
            ApplyVolume();
        }

        return y + labelH + h + 16 * scale;
    }

    // ---- クリップボード文字列取得（raylib-csのバージョンによりGetClipboardText()の戻り値がsbyte*のため、安全にマーシャリングする） ----
    private static unsafe string GetClipboardTextSafe()
    {
        sbyte* ptr = Raylib.GetClipboardText();
        if (ptr == null) return "";
        return new string(ptr);
    }

    // ---- テキストフィールド共通: Ctrl+C/V/X/A ----
    private static void HandleClipboardShortcuts(ref string value)
    {
        bool ctrlDown = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        if (!ctrlDown) return;

        // Ctrl+V: クリップボードから貼り付け（改行・制御文字は除去）
        if (Raylib.IsKeyPressed(KeyboardKey.V))
        {
            string clip = GetClipboardTextSafe();
            if (!string.IsNullOrEmpty(clip))
            {
                foreach (char c in clip)
                {
                    if (c == '\r' || c == '\n' || char.IsControl(c)) continue;
                    value += c;
                }
            }
        }
        // Ctrl+C: コピー
        if (Raylib.IsKeyPressed(KeyboardKey.C) && !string.IsNullOrEmpty(value))
            Raylib.SetClipboardText(value);
        // Ctrl+X: 切り取り
        if (Raylib.IsKeyPressed(KeyboardKey.X) && !string.IsNullOrEmpty(value))
        {
            Raylib.SetClipboardText(value);
            value = "";
        }
        // Ctrl+A: 全消去（選択概念がないため、次の入力で置き換えられるよう全クリア）
        if (Raylib.IsKeyPressed(KeyboardKey.A))
            value = "";
    }

    // ---- テキスト入力行（テキストフィールド風） ----
    private static float DrawTextFieldRow(float x, float y, float w, float scale, string label,
        ref string value, ref bool editing, System.Action<string> onConfirm)
    {
        float h = 64 * scale;
        var rect = new Rectangle(x, y, w, h);
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
        if (hover) Raylib.DrawRectangleRec(rect, HoverTint);

        Raylib.DrawTextEx(G.FontSub, label, new Vector2(x + 24 * scale, y + 14 * scale), 18 * scale, 2, TextSecondary);

        // フィールド背景
        float fieldX = x + 24 * scale;
        float fieldY = y + 34 * scale;
        float fieldW = w - 48 * scale;
        float fieldH = 24 * scale;
        var fieldRect = new Rectangle(fieldX, fieldY, fieldW, fieldH);
        Raylib.DrawRectangleRec(fieldRect, editing ? new Color(50, 50, 70, 255) : new Color(40, 40, 40, 255));

        // テキスト表示
        string display = editing ? value + "|" : value;
        Raylib.DrawTextEx(G.FontSub, display, new Vector2(fieldX + 4 * scale, fieldY + 2 * scale), 18 * scale, 2, TextPrimary);

        if (editing)
        {
            // 文字入力受け付け
            int ch;
            while ((ch = Raylib.GetCharPressed()) != 0)
                value += (char)ch;
            if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && value.Length > 0)
                value = value[..^1];

            HandleClipboardShortcuts(ref value);

            if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
            {
                editing = false;
                onConfirm(value);
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                editing = false;
        }
        else
        {
            bool fieldHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), fieldRect);
            if (fieldHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                editing = true;
                StartRipple(fieldRect);
            }
        }

        return y + h;
    }

    // ---- 金合格スイッチ ----
    private static float DrawNpIsGoldRow(float x, float y, float w, float scale, float dt)
    {
        return DrawSwitchRow(x, y, w, scale, dt, "金合格（段位）", null, ref _npIsGold, ref _npIsGoldT);
    }

    // ---- 段位フレームステッパー ----
    private static float DrawNpDanFrameStepper(float x, float y, float w, float scale)
    {
        float h = 56 * scale;
        var rowRect = new Rectangle(x, y, w, h);
        bool rowHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rowRect);
        if (rowHover) Raylib.DrawRectangleRec(rowRect, HoverTint);

        Raylib.DrawTextEx(G.FontSub, "段位フレーム (3〜5)",
            new Vector2(x + 24 * scale, y + (h - 24 * scale) / 2f), 18 * scale, 2, TextPrimary);

        float btnSize = 28 * scale;
        float minusX = x + w - 24 * scale - btnSize * 2 - 40 * scale;
        float minusY = y + (h - btnSize) / 2f;
        var minusRect = new Rectangle(minusX, minusY, btnSize, btnSize);
        bool minusH = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), minusRect);
        Raylib.DrawRectangleRounded(minusRect, 0.5f, 4, minusH ? Accent : new Color(60, 60, 60, 255));
        Raylib.DrawTextEx(G.FontSub, "-", new Vector2(minusX + 9 * scale, minusY + 4 * scale), 20 * scale, 2, Color.White);
        if (minusH && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            _npDanFrame = System.Math.Max(3, _npDanFrame - 1);
            SaveNamePlate();
            StartRipple(minusRect);
        }

        string valText = _npDanFrame.ToString();
        var vs = Raylib.MeasureTextEx(G.FontSub, valText, 22 * scale, 2);
        float valueX = minusX + btnSize + 8 * scale;
        Raylib.DrawTextEx(G.FontSub, valText, new Vector2(valueX + (28 * scale - vs.X) / 2f, y + (h - vs.Y) / 2f), 22 * scale, 2, TextPrimary);

        float plusX = valueX + 36 * scale;
        var plusRect = new Rectangle(plusX, minusY, btnSize, btnSize);
        bool plusH = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), plusRect);
        Raylib.DrawRectangleRounded(plusRect, 0.5f, 4, plusH ? Accent : new Color(60, 60, 60, 255));
        Raylib.DrawTextEx(G.FontSub, "+", new Vector2(plusX + 8 * scale, minusY + 4 * scale), 20 * scale, 2, Color.White);
        if (plusH && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            _npDanFrame = System.Math.Min(5, _npDanFrame + 1);
            SaveNamePlate();
            StartRipple(plusRect);
        }

        return y + h;
    }

    // ---- 2P金合格スイッチ ----
    private static float DrawP2NpIsGoldRow(float x, float y, float w, float scale, float dt)
    {
        bool before = _p2NpIsGold;
        float nextY = DrawSwitchRow(x, y, w, scale, dt, "金合格（段位）", null, ref _p2NpIsGold, ref _p2NpIsGoldT);
        if (before != _p2NpIsGold) SaveNamePlateP2();
        return nextY;
    }

    // ---- 2P段位フレームステッパー ----
    private static float DrawP2NpDanFrameStepper(float x, float y, float w, float scale)
    {
        float h = 56 * scale;
        var rowRect = new Rectangle(x, y, w, h);
        bool rowHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rowRect);
        if (rowHover) Raylib.DrawRectangleRec(rowRect, HoverTint);

        Raylib.DrawTextEx(G.FontSub, "段位フレーム (3〜5)",
            new Vector2(x + 24 * scale, y + (h - 24 * scale) / 2f), 18 * scale, 2, TextPrimary);

        float btnSize = 28 * scale;
        float minusX = x + w - 24 * scale - btnSize * 2 - 40 * scale;
        float minusY = y + (h - btnSize) / 2f;
        var minusRect = new Rectangle(minusX, minusY, btnSize, btnSize);
        bool minusH = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), minusRect);
        Raylib.DrawRectangleRounded(minusRect, 0.5f, 4, minusH ? Accent : new Color(60, 60, 60, 255));
        Raylib.DrawTextEx(G.FontSub, "-", new Vector2(minusX + 9 * scale, minusY + 4 * scale), 20 * scale, 2, Color.White);
        if (minusH && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            _p2NpDanFrame = System.Math.Max(3, _p2NpDanFrame - 1);
            SaveNamePlateP2();
            StartRipple(minusRect);
        }

        string valText = _p2NpDanFrame.ToString();
        var vs = Raylib.MeasureTextEx(G.FontSub, valText, 22 * scale, 2);
        float valueX = minusX + btnSize + 8 * scale;
        Raylib.DrawTextEx(G.FontSub, valText, new Vector2(valueX + (28 * scale - vs.X) / 2f, y + (h - vs.Y) / 2f), 22 * scale, 2, TextPrimary);

        float plusX = valueX + 36 * scale;
        var plusRect = new Rectangle(plusX, minusY, btnSize, btnSize);
        bool plusH = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), plusRect);
        Raylib.DrawRectangleRounded(plusRect, 0.5f, 4, plusH ? Accent : new Color(60, 60, 60, 255));
        Raylib.DrawTextEx(G.FontSub, "+", new Vector2(plusX + 8 * scale, minusY + 4 * scale), 20 * scale, 2, Color.White);
        if (plusH && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            _p2NpDanFrame = System.Math.Min(5, _p2NpDanFrame + 1);
            SaveNamePlateP2();
            StartRipple(plusRect);
        }

        return y + h;
    }

    // ---- 曲検索候補 ----
    private static bool _showCandidates = false;
    private static List<SongSelectScene.SearchCandidate> _searchCandidates = new List<SongSelectScene.SearchCandidate>();
    private const int SEARCH_CANDIDATE_MAX = 6;

    // ---- 曲検索行 ----
    private static float DrawSearchRow(float x, float y, float w, float scale)
    {
        float h = 64 * scale;

        Raylib.DrawTextEx(G.FontSub, "検索キーワード", new Vector2(x + 24 * scale, y + 14 * scale), 18 * scale, 2, TextSecondary);

        float fieldX = x + 24 * scale;
        float fieldY = y + 34 * scale;
        float fieldW = w - 48 * scale - 70 * scale; // 検索ボタン分を除く
        float fieldH = 24 * scale;
        var fieldRect = new Rectangle(fieldX, fieldY, fieldW, fieldH);
        Raylib.DrawRectangleRec(fieldRect, _editingSearch ? new Color(50, 50, 70, 255) : new Color(40, 40, 40, 255));

        string display = _editingSearch ? _searchInput + "|" : _searchInput;
        Raylib.DrawTextEx(G.FontSub, display, new Vector2(fieldX + 4 * scale, fieldY + 2 * scale), 18 * scale, 2, TextPrimary);

        if (_editingSearch)
        {
            int ch;
            int beforeLen = _searchInput.Length;
            while ((ch = Raylib.GetCharPressed()) != 0)
                _searchInput += (char)ch;
            if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && _searchInput.Length > 0)
                _searchInput = _searchInput[..^1];

            HandleClipboardShortcuts(ref _searchInput);

            // 文字が変化したら、一度出した候補は古くなるので隠す（再度Enter/検索ボタンで出し直す）
            if (_searchInput.Length != beforeLen)
                _showCandidates = false;

            if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
            {
                ShowSearchCandidates();
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
            {
                _editingSearch = false;
                _showCandidates = false;
            }
        }
        else
        {
            bool fh = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), fieldRect);
            if (fh && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _editingSearch = true;
                _showCandidates = false;
                StartRipple(fieldRect);
            }
        }

        // 「検索」ボタン
        float btnW = 64 * scale;
        float btnH = 24 * scale;
        float btnX = fieldX + fieldW + 6 * scale;
        var btnRect = new Rectangle(btnX, fieldY, btnW, btnH);
        bool btnHov = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), btnRect);
        Raylib.DrawRectangleRounded(btnRect, 0.4f, 8, btnHov ? Accent : Primary);
        var bts = Raylib.MeasureTextEx(G.FontSub, "検索", 16 * scale, 2);
        Raylib.DrawTextEx(G.FontSub, "検索", new Vector2(btnX + (btnW - bts.X) / 2f, fieldY + (btnH - bts.Y) / 2f), 16 * scale, 2, Color.White);
        if (btnHov && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            ShowSearchCandidates();
            StartRipple(btnRect);
        }

        // ---- 検索候補一覧（Enter/検索ボタンを押した時だけ表示。タイトル[ジャンル]形式でクリックして選択） ----
        float afterFieldY = y + h;
        if (_showCandidates && _searchCandidates.Count > 0)
        {
            float rowH = 30 * scale;
            float listX = fieldX;
            float listY = afterFieldY + 4 * scale;
            float listW = w - 48 * scale;

            Raylib.DrawRectangleRec(new Rectangle(listX, listY, listW, rowH * _searchCandidates.Count),
                new Color(30, 30, 30, 255));
            Raylib.DrawRectangleLinesEx(new Rectangle(listX, listY, listW, rowH * _searchCandidates.Count), 1, Divider);

            for (int i = 0; i < _searchCandidates.Count; i++)
            {
                var cand = _searchCandidates[i];
                var rowRect = new Rectangle(listX, listY + i * rowH, listW, rowH);
                bool rowHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rowRect);
                if (rowHover) Raylib.DrawRectangleRec(rowRect, HoverTint);
                if (i > 0)
                    Raylib.DrawLineEx(new Vector2(listX, listY + i * rowH), new Vector2(listX + listW, listY + i * rowH), 1, Divider);

                Raylib.DrawTextEx(G.FontSub, cand.DisplayText,
                    new Vector2(listX + 10 * scale, listY + i * rowH + (rowH - 16 * scale) / 2f), 16 * scale, 1, TextPrimary);

                // マウスクリックの検出は次フレームのボタン押下判定と競合しないよう
                // MouseButtonPressedの単発イベントのみを使う
                if (rowHover && Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    SongSelectScene.RequestSearchJumpToPath(cand.TjaPath);
                    _editingSearch = false;
                    _showCandidates = false;
                    _searchCandidates.Clear();
                    _isOpen = false; // 設定を閉じて選曲画面へ
                    _captureIndex = -1;
                }
            }

            afterFieldY = listY + rowH * _searchCandidates.Count;
        }

        return afterFieldY + 6 * scale;
    }

    private static void ShowSearchCandidates()
    {
        if (string.IsNullOrWhiteSpace(_searchInput))
        {
            _showCandidates = false;
            return;
        }

        _searchCandidates = SongSelectScene.GetSearchCandidates(_searchInput, SEARCH_CANDIDATE_MAX);

        if (_searchCandidates.Count > 0)
        {
            // 候補があればリストを表示し、選ぶまでパネルは開いたまま
            _showCandidates = true;
        }
        else
        {
            // 候補が無ければ従来通りの曖昧一致ジャンプにフォールバック
            _showCandidates = false;
            SongSelectScene.RequestSearchJump(_searchInput);
            _editingSearch = false;
            _isOpen = false; // 設定を閉じて選曲画面へ
            _captureIndex = -1;
        }
    }

    // ---- 曲再読み込みボタン ----
    private static float DrawUpdateCheckRow(float x, float y, float w, float scale, float dt)
    {
        float h = 90 * scale;
        float btnW = 220 * scale;
        float btnH = 36 * scale;
        float btnX = x + 24 * scale;
        float btnY = y + 12 * scale;
        var btnRect = new Rectangle(btnX, btnY, btnW, btnH);
        bool btnHov = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), btnRect);

        bool busy = CurrentUpdateState is UpdateState.Checking or UpdateState.Downloading;

        Color btnColor = CurrentUpdateState switch
        {
            UpdateState.Updated => new Color(76, 175, 80, 255),  // 緑：更新完了
            UpdateState.Error => new Color(211, 47, 47, 255),    // 赤：エラー
            _ => (btnHov && !busy ? Accent : Primary)
        };

        Raylib.DrawRectangleRounded(btnRect, 0.4f, 8, btnColor);

        string label = busy ? "確認中..." : "アップデートを確認";
        var ts = Raylib.MeasureTextEx(G.FontSub, label, 18 * scale, 2);
        Raylib.DrawTextEx(G.FontSub, label,
            new Vector2(btnX + (btnW - ts.X) / 2f, btnY + (btnH - ts.Y) / 2f), 18 * scale, 2, Color.White);

        if (!busy && btnHov && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            TriggerUpdateCheck();
            StartRipple(btnRect);
        }

        // 状態メッセージ・進捗バー
        float msgY = btnY + btnH + 10 * scale;
        if (!string.IsNullOrEmpty(UpdateMessage))
        {
            Raylib.DrawTextEx(G.FontSub, UpdateMessage,
                new Vector2(x + 24 * scale, msgY), 16 * scale, 2, TextSecondary);
        }

        if (CurrentUpdateState == UpdateState.Downloading)
        {
            float barW = w - 48 * scale;
            float barH = 8 * scale;
            float barY = msgY + 22 * scale;
            Raylib.DrawRectangleRounded(new Rectangle(x + 24 * scale, barY, barW, barH), 0.5f, 6, TrackOff);
            Raylib.DrawRectangleRounded(new Rectangle(x + 24 * scale, barY, barW * Math.Clamp(UpdateProgress01, 0f, 1f), barH), 0.5f, 6, Accent);
        }

        // 更新完了後: カウントダウン表示
        if (CurrentUpdateState == UpdateState.Updated && !_restartInitiated)
        {
            int secs = Math.Max(1, (int)MathF.Ceiling(_restartCountdown));
            string countdown = $"{secs}秒後に自動で再起動します...";
            Raylib.DrawTextEx(G.FontSub, countdown,
                new Vector2(x + 24 * scale, msgY + 22 * scale), 16 * scale, 2,
                new Color(76, 175, 80, 255));

            // 残り時間バー
            float barW = w - 48 * scale;
            float barH = 6 * scale;
            float barY = msgY + 42 * scale;
            float progress = Math.Clamp(_restartCountdown / RestartCountdownSec, 0f, 1f);
            Raylib.DrawRectangleRounded(new Rectangle(x + 24 * scale, barY, barW, barH), 0.5f, 6, TrackOff);
            Raylib.DrawRectangleRounded(new Rectangle(x + 24 * scale, barY, barW * progress, barH), 0.5f, 6,
                new Color(76, 175, 80, 255));

            h += 52 * scale; // 行の高さを拡張して下の項目と被らないように
        }

        return y + h;
    }

    private static float DrawReloadSongsButton(float x, float y, float w, float scale, float dt)
    {
        _reloadFlashT = System.MathF.Max(0f, _reloadFlashT - dt);

        float h = 60 * scale;
        float btnW = 160 * scale;
        float btnH = 36 * scale;
        float btnX = x + 24 * scale;
        float btnY = y + (h - btnH) / 2f;
        var btnRect = new Rectangle(btnX, btnY, btnW, btnH);
        bool btnHov = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), btnRect);

        Color btnColor = _reloadFlashT > 0f
            ? new Color(76, 175, 80, 255)  // 緑：成功フラッシュ
            : (btnHov ? Accent : Primary);

        Raylib.DrawRectangleRounded(btnRect, 0.4f, 8, btnColor);

        string label = _reloadFlashT > 0f ? "✔ 再読み込み完了" : "曲を再読み込み";
        var ts = Raylib.MeasureTextEx(G.FontSub, label, 18 * scale, 2);
        Raylib.DrawTextEx(G.FontSub, label,
            new Vector2(btnX + (btnW - ts.X) / 2f, btnY + (btnH - ts.Y) / 2f), 18 * scale, 2, Color.White);

        if (btnHov && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            SongSelectScene.ReloadSongs();
            _reloadFlashT = 2f;
            StartRipple(btnRect);
        }

        return y + h;
    }

    // ---- シーン移動（デバッグ用）----
    // Program.cs側のメインループが毎フレーム ConsumeSceneJumpRequest() を確認し、
    // None以外が返ってきたら該当シーンへ切り替える。SettingsPanel自体はシーン管理を持たないため、
    // ここでは「どこへ移動したいか」のリクエストを溜めておくだけ。
    public enum DebugSceneTarget { None, Title, SongSelect, Demo }
    private static DebugSceneTarget _pendingSceneJump = DebugSceneTarget.None;

    /// <summary>Program.cs側から毎フレーム呼ぶ。リクエストを消費して返す（一度取得したら消える）。</summary>
    public static DebugSceneTarget ConsumeSceneJumpRequest()
    {
        var req = _pendingSceneJump;
        _pendingSceneJump = DebugSceneTarget.None;
        return req;
    }

    private static float DrawSceneJumpRow(float x, float y, float w, float scale)
    {
        float h = 60 * scale;
        float btnH = 36 * scale;
        float btnY = y + (h - btnH) / 2f;
        float gap = 12 * scale;
        float btnW = (w - 48 * scale - gap * 2) / 3f;

        float btn1X = x + 24 * scale;
        DrawSceneJumpButton(new Rectangle(btn1X, btnY, btnW, btnH), scale, "デモへ", DebugSceneTarget.Demo);

        float btn2X = btn1X + btnW + gap;
        DrawSceneJumpButton(new Rectangle(btn2X, btnY, btnW, btnH), scale, "タイトルへ", DebugSceneTarget.Title);

        float btn3X = btn2X + btnW + gap;
        DrawSceneJumpButton(new Rectangle(btn3X, btnY, btnW, btnH), scale, "選曲画面へ", DebugSceneTarget.SongSelect);

        return y + h;
    }

    private static void DrawSceneJumpButton(Rectangle rect, float scale, string label, DebugSceneTarget target)
    {
        bool hov = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
        Raylib.DrawRectangleRounded(rect, 0.4f, 8, hov ? Accent : Primary);

        var ts = Raylib.MeasureTextEx(G.FontSub, label, 15 * scale, 1);
        Raylib.DrawTextEx(G.FontSub, label,
            new Vector2(rect.X + (rect.Width - ts.X) / 2f, rect.Y + (rect.Height - ts.Y) / 2f), 15 * scale, 1, Color.White);

        if (hov && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            _pendingSceneJump = target;
            _isOpen = false; // 設定を閉じて移動先のシーンを見せる
            _captureIndex = -1;
            StartRipple(rect);
        }
    }

    // ---- 保存ヘルパー ----
    private static void SaveNamePlate()
    {
        NamePlate.SavePlayerData(_nameInput, _npTitleInput, _danInput, _npIsGold, _npDanFrame);
        NamePlate.LoadPlayerData(); // リロードして表示に即反映
    }

    private static void SaveNamePlateP2()
    {
        NamePlate.SavePlayerDataP2(_p2NameInput, _p2NpTitleInput, _p2DanInput, _p2NpIsGold, _p2NpDanFrame);
        NamePlate.LoadPlayerDataP2(); // Data/PlayData2.jsonをリロードして2P表示へ即反映
    }

    private const string TITLE_CONFIG_PATH = "title.cfg";

    private static void SaveTitleConfig()
    {
        try { System.IO.File.WriteAllText(TITLE_CONFIG_PATH, TitleScene.TitleGuideText); }
        catch { }
    }

    public static void LoadTitleConfig()
    {
        if (!System.IO.File.Exists(TITLE_CONFIG_PATH)) return;
        try
        {
            string txt = System.IO.File.ReadAllText(TITLE_CONFIG_PATH).Trim();
            if (!string.IsNullOrEmpty(txt))
            {
                TitleScene.TitleGuideText = txt;
                _titleTextInput = txt;
            }
        }
        catch { }
    }

    private static void SetKey(int type, int index, int vk)
    {
        lock (KeyBindingsSync)
        {
            var list = GetKeyList(type);
            if (index < list.Count)
            {
                list[index] = vk;
                RefreshKeyBindingSnapshotLocked();
            }
        }
    }

    private static List<int> GetKeyList(int type) => type switch
    {
        0 => KeyLeft,
        1 => KeyRight,
        2 => KeyEdgeL,
        3 => KeyEdgeR,
        4 => P2KeyLeft,
        5 => P2KeyRight,
        6 => P2KeyEdgeL,
        _ => P2KeyEdgeR
    };

    private static readonly (int rl, int vk)[] KeyMap =
    {
        (32, 0x20), (257, 0x0D), (258, 0x09), (259, 0x08),
        (263, 0x25), (262, 0x27), (265, 0x26), (264, 0x28),
        (340, 0xA0), (344, 0xA1), (341, 0xA2), (345, 0xA3), (342, 0xA4), (346, 0xA5),
        (44, 0xBC), (45, 0xBD), (46, 0xBE), (47, 0xBF), (59, 0xBB),
        (91, 0xDB), (92, 0xDC), (93, 0xDD)
    };

    public static int RaylibKeyToVk(int rl)
    {
        if (rl is >= 65 and <= 90 or >= 48 and <= 57) return rl;
        if (rl is >= 290 and <= 301) return rl - 290 + 0x70;
        foreach (var (r, v) in KeyMap)
            if (r == rl) return v;
        return 0;
    }

    public static int VkToRaylibKey(int vk)
    {
        if (vk is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39) return vk;
        if (vk is >= 0x70 and <= 0x7B) return vk - 0x70 + 290;
        foreach (var (r, v) in KeyMap)
            if (v == vk) return r;
        return 0;
    }

    public static bool IsVkPressed(int vk)
    {
        int rl = VkToRaylibKey(vk);
        return rl != 0 && Raylib.IsKeyPressed((KeyboardKey)rl);
    }

    // 各種キーリストのチェック
    public static bool IsAnyDonPressed()
    {
        foreach (int vk in KeyLeft)
            if (IsVkPressed(vk)) return true;
        foreach (int vk in KeyRight)
            if (IsVkPressed(vk)) return true;
        return false;
    }

    public static bool IsAnyKaPressed()
    {
        foreach (int vk in KeyEdgeL)
            if (IsVkPressed(vk)) return true;
        foreach (int vk in KeyEdgeR)
            if (IsVkPressed(vk)) return true;
        return false;
    }

    public static bool IsAnyKaUpPressed()
    {
        foreach (int vk in KeyEdgeL)
            if (IsVkPressed(vk)) return true;
        return false;
    }

    public static bool IsAnyKaDownPressed()
    {
        foreach (int vk in KeyEdgeR)
            if (IsVkPressed(vk)) return true;
        return false;
    }

    public static string VkName(int vk) => vk switch
    {
        >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        0x20 => "Space",
        0x0D => "Enter",
        0x09 => "Tab",
        0x08 => "BS",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0xA0 => "LShift",
        0xA1 => "RShift",
        0xA2 => "LCtrl",
        0xA3 => "RCtrl",
        0xA4 => "LAlt",
        0xA5 => "RAlt",
        >= 0x70 and <= 0x7B => "F" + (vk - 0x6F),
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xBB => ";",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        _ => "?"
    };

    private static float DrawSongsTypeRow(float x, float y, float w, float scale, SongsTypeOption option, string label)
    {
        float h = 48 * scale;
        var rect = new Rectangle(x, y, w, h);
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
        bool selected = SongsType == option;

        if (hover)
            Raylib.DrawRectangleRec(rect, HoverTint);

        float cx = x + 36 * scale;
        float cyMid = y + h / 2f;
        float outerR = 11 * scale;

        Color ring = selected ? Accent : new Color(0, 0, 0, 138);
        Raylib.DrawRing(new Vector2(cx, cyMid), outerR - 2.2f * scale, outerR, 0, 360, 32, ring);
        if (selected)
            Raylib.DrawCircleV(new Vector2(cx, cyMid), outerR - 5.5f * scale, Accent);

        Raylib.DrawTextEx(G.FontSub, label,
            new Vector2(cx + outerR + 20 * scale, cyMid - 11 * scale), 22 * scale, 2, TextPrimary);

        if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            // 💡 [修正] ここでSave()を呼んでいなかったため、再起動すると常にTJA(既定値)へ戻っていた
            //    (settings.cfgにSongsTypeの行が一度も書き込まれず、Load()が拾える値が無かった)。
            //    トグル選択と同時に確実に永続化する。
            if (SongsType != option)
            {
                SongsType = option;
                Save();
            }
            StartRipple(rect);
        }

        return y + h;
    }

    private static float DrawVramModeRow(float x, float y, float w, float scale, int index)
    {
        float h = 48 * scale;
        var rect = new Rectangle(x, y, w, h);
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
        bool selected = Program._vramDetailMode == index;

        if (hover)
            Raylib.DrawRectangleRec(rect, HoverTint);

        float cx = x + 36 * scale;
        float cyMid = y + h / 2f;
        float outerR = 11 * scale;

        Color ring = selected ? Accent : new Color(0, 0, 0, 138);
        Raylib.DrawRing(new Vector2(cx, cyMid), outerR - 2.2f * scale, outerR, 0, 360, 32, ring);
        if (selected)
            Raylib.DrawCircleV(new Vector2(cx, cyMid), outerR - 5.5f * scale, Accent);

        Raylib.DrawTextEx(G.FontSub, VramModeLabels[index],
            new Vector2(cx + outerR + 20 * scale, cyMid - 11 * scale), 22 * scale, 2, TextPrimary);

        if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            Program._vramDetailMode = index;
            StartRipple(rect);
        }

        return y + h;
    }

    private static void StartRipple(Rectangle rect)
    {
        _rippleRect = rect;
        _rippleCenter = Raylib.GetMousePosition();
        float dx = System.MathF.Max(_rippleCenter.X - rect.X, rect.X + rect.Width - _rippleCenter.X);
        float dy = System.MathF.Max(_rippleCenter.Y - rect.Y, rect.Y + rect.Height - _rippleCenter.Y);
        _rippleMaxR = System.MathF.Sqrt(dx * dx + dy * dy);
        _rippleT = 0f;
    }

    private static void DrawRipple(float dt)
    {
        if (_rippleT >= 1f) return;

        _rippleT = System.MathF.Min(1f, _rippleT + dt * 2.4f);
        float ease = 1f - System.MathF.Pow(1f - _rippleT, 2f);
        int alpha = (int)(30 * (1f - _rippleT));

        Raylib.BeginScissorMode((int)_rippleRect.X, (int)_rippleRect.Y,
            (int)_rippleRect.Width, (int)_rippleRect.Height);
        Raylib.DrawCircleV(_rippleCenter, _rippleMaxR * ease, new Color(0, 0, 0, alpha));
        Raylib.EndScissorMode();
    }

    private static Color LerpColor(Color a, Color b, float t)
    {
        return new Color(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t),
            (int)(a.A + (b.A - a.A) * t));
    }

    public static void Save()
    {
        try
        {
            var lines = new List<string>
            {
                $"IsFullscreen={IsFullscreen}",
                $"ResolutionIndex={ResolutionIndex}",
                $"FpsLimit={FpsLimit}",
                $"UseWasapiExclusive={UseWasapiExclusive}",
                $"MasterVolume={MasterVolume}",
                $"BgmVolume={BgmVolume}",
                $"SeVolume={SeVolume}",
                $"OutputBackend={OutputBackend}",
                $"BufferSizeMs={BufferSizeMs}",
                $"AsioDriverIndex={AsioDriverIndex}",
                $"SampleRateIndex={SampleRateIndex}",
                $"VramDetailMode={Program._vramDetailMode}",
                $"KeyLeft={string.Join(",", KeyLeft)}",
                $"KeyRight={string.Join(",", KeyRight)}",
                $"KeyEdgeL={string.Join(",", KeyEdgeL)}",
                $"KeyEdgeR={string.Join(",", KeyEdgeR)}",
                $"P2KeyLeft={string.Join(",", P2KeyLeft)}",
                $"P2KeyRight={string.Join(",", P2KeyRight)}",
                $"P2KeyEdgeL={string.Join(",", P2KeyEdgeL)}",
                $"P2KeyEdgeR={string.Join(",", P2KeyEdgeR)}",
                $"DancerFps={DancerFps}",
                $"DonChanFps={DonChanFps}",
                $"DonChanLowQuality={DonChanLowQuality}",
                $"DancerLowQuality={DancerLowQuality}",
                $"SongsType={SongsType}",
                $"AILevel={AILevel}"
            };
            File.WriteAllLines(CONFIG_PATH, lines);
            File.AppendAllText("songs_debug.txt",
                $"[Save] CWD={Directory.GetCurrentDirectory()} absPath={System.IO.Path.GetFullPath(CONFIG_PATH)} SongsType={SongsType}\n");
        }
        catch (Exception ex)
        {
            File.AppendAllText("songs_debug.txt", $"[Save] 書き込み失敗の例外: {ex}\n");
        }
    }

    public static void Load()
    {
        string absPath = System.IO.Path.GetFullPath(CONFIG_PATH);
        bool exists = File.Exists(CONFIG_PATH);
        try
        {
            string rawContent = exists ? File.ReadAllText(CONFIG_PATH) : "(ファイルが存在しません)";
            File.AppendAllText("songs_debug.txt",
                $"[Load] CWD={Directory.GetCurrentDirectory()} absPath={absPath} exists={exists}\n" +
                $"[Load] rawContent=====\n{rawContent}\n=====\n");
        }
        catch (Exception ex)
        {
            File.AppendAllText("songs_debug.txt", $"[Load] ログ出力自体で例外: {ex}\n");
        }

        if (!exists) return;

        try
        {
            foreach (var line in File.ReadAllLines(CONFIG_PATH))
            {
                var parts = line.Split('=', 2);
                if (parts.Length < 2) continue;
                var key = parts[0].Trim();
                var val = parts[1].Trim();

                switch (key)
                {
                    case "IsFullscreen": IsFullscreen = bool.Parse(val); break;
                    case "ResolutionIndex":
                        if (int.TryParse(val, out int resIdx))
                            ResolutionIndex = Math.Clamp(resIdx, 0, ResolutionOptions.Length - 1);
                        break;
                    case "FpsLimit": FpsLimit = int.Parse(val); break;
                    case "UseWasapiExclusive": UseWasapiExclusive = bool.Parse(val); break;
                    // 旧バージョン(音量が1本だった頃)の設定ファイルとの互換用
                    case "SoundVolume":
                        if (int.TryParse(val, out int legacyVol))
                        {
                            legacyVol = Math.Clamp(legacyVol, 0, 100);
                            MasterVolume = legacyVol;
                            BgmVolume = legacyVol;
                            SeVolume = legacyVol;
                        }
                        break;
                    case "MasterVolume": if (int.TryParse(val, out int mv)) MasterVolume = Math.Clamp(mv, 0, 100); break;
                    case "BgmVolume": if (int.TryParse(val, out int bv)) BgmVolume = Math.Clamp(bv, 0, 100); break;
                    case "SeVolume": if (int.TryParse(val, out int sv)) SeVolume = Math.Clamp(sv, 0, 100); break;
                    case "OutputBackend":
                        if (Enum.TryParse<AudioBackend>(val, out var backend)) OutputBackend = backend;
                        break;
                    case "BufferSizeMs":
                        if (int.TryParse(val, out int bufMs)) BufferSizeMs = Math.Clamp(bufMs, 5, 100);
                        break;
                    case "AsioDriverIndex":
                        if (int.TryParse(val, out int asioIdx)) AsioDriverIndex = Math.Max(0, asioIdx);
                        break;
                    case "SampleRateIndex":
                        if (int.TryParse(val, out int srIdx)) SampleRateIndex = Math.Clamp(srIdx, 0, SampleRateOptions.Length - 1);
                        break;
                    case "VramDetailMode":
                        if (int.TryParse(val, out int vramMode)) Program._vramDetailMode = Math.Clamp(vramMode, 0, 4);
                        break;
                    case "KeyDon1": KeyLeft.Clear(); KeyLeft.Add(int.Parse(val)); break;
                    case "KeyDon2": KeyRight.Clear(); KeyRight.Add(int.Parse(val)); break;
                    case "KeyKa1": KeyEdgeL.Clear(); KeyEdgeL.Add(int.Parse(val)); break;
                    case "KeyKa2": KeyEdgeR.Clear(); KeyEdgeR.Add(int.Parse(val)); break;
                    case "KeyLeft": KeyLeft.Clear(); foreach (var v in val.Split(',')) if (int.TryParse(v, out int vk)) KeyLeft.Add(vk); break;
                    case "KeyRight": KeyRight.Clear(); foreach (var v in val.Split(',')) if (int.TryParse(v, out int vk)) KeyRight.Add(vk); break;
                    case "KeyEdgeL": KeyEdgeL.Clear(); foreach (var v in val.Split(',')) if (int.TryParse(v, out int vk)) KeyEdgeL.Add(vk); break;
                    case "KeyEdgeR": KeyEdgeR.Clear(); foreach (var v in val.Split(',')) if (int.TryParse(v, out int vk)) KeyEdgeR.Add(vk); break;
                    case "P2KeyLeft": P2KeyLeft.Clear(); foreach (var v in val.Split(',')) if (int.TryParse(v, out int vk)) P2KeyLeft.Add(vk); break;
                    case "P2KeyRight": P2KeyRight.Clear(); foreach (var v in val.Split(',')) if (int.TryParse(v, out int vk)) P2KeyRight.Add(vk); break;
                    case "P2KeyEdgeL": P2KeyEdgeL.Clear(); foreach (var v in val.Split(',')) if (int.TryParse(v, out int vk)) P2KeyEdgeL.Add(vk); break;
                    case "P2KeyEdgeR": P2KeyEdgeR.Clear(); foreach (var v in val.Split(',')) if (int.TryParse(v, out int vk)) P2KeyEdgeR.Add(vk); break;
                    case "DancerFps":
                        if (int.TryParse(val, out int dcFps) && Array.IndexOf(DancerFpsOptions, dcFps) >= 0) DancerFps = dcFps;
                        break;
                    case "DonChanFps":
                        if (int.TryParse(val, out int donFps) && Array.IndexOf(DonChanFpsOptions, donFps) >= 0) DonChanFps = donFps;
                        break;
                    case "DonChanLowQuality":
                        if (bool.TryParse(val, out bool lowQ)) DonChanLowQuality = lowQ;
                        break;
                    case "DancerLowQuality":
                        if (bool.TryParse(val, out bool dancerLowQ)) DancerLowQuality = dancerLowQ;
                        break;
                    case "AILevel":
                        if (int.TryParse(val, out int aiLevel)) AILevel = Math.Clamp(aiLevel, 0, 10);
                        break;
                    case "SongsType":
                        if (Enum.TryParse<SongsTypeOption>(val, out var songsType))
                        {
                            SongsType = songsType;
                            File.AppendAllText("songs_debug.txt", $"[Load] SongsTypeを{songsType}に反映しました\n");
                        }
                        else
                        {
                            File.AppendAllText("songs_debug.txt", $"[Load] SongsTypeのパースに失敗: val='{val}'\n");
                        }
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            File.AppendAllText("songs_debug.txt", $"[Load] パース中に例外: {ex}\n");
        }

        File.AppendAllText("songs_debug.txt", $"[Load] 完了後のSongsType={SongsType}\n");
    }

    private static void ApplySettings()
    {
        bool changed = false;

        if (FpsLimit != _prevFps)
        {
            _prevFps = FpsLimit;
            Raylib.SetTargetFPS(FpsLimit);
            changed = true;
        }

        if (IsFullscreen != _prevFullscreen)
        {
            _prevFullscreen = IsFullscreen;
            if (IsFullscreen != Raylib.IsWindowFullscreen())
                Raylib.ToggleFullscreen();
            changed = true;
        }

        if (ResolutionIndex != _prevResolutionIndex)
        {
            _prevResolutionIndex = ResolutionIndex;
            var res = ResolutionOptions[ResolutionIndex];
            IsFHD = res.W >= 1920; // 互換プロパティを同期
            if (!IsFullscreen)
                Program.ApplyWindowResolution(res.W, res.H);
            changed = true;
        }

        if (UseWasapiExclusive != _prevWasapi)
        {
            _prevWasapi = UseWasapiExclusive;
            if (OutputBackend == AudioBackend.Wasapi)
                AudioEngine.Reinit();
            changed = true;
        }

        // バッファサイズはドラッグ中は毎フレーム再初期化しないよう、ドラッグ終了後にまとめて反映する
        if (!_bufferDragging && (OutputBackend != _prevBackend || BufferSizeMs != _prevBufferSize
            || AsioDriverIndex != _prevAsioDriverIndex || SampleRateIndex != _prevSampleRateIndex))
        {
            _prevBackend = OutputBackend;
            _prevBufferSize = BufferSizeMs;
            _prevAsioDriverIndex = AsioDriverIndex;
            _prevSampleRateIndex = SampleRateIndex;
            AudioEngine.Reinit();
            changed = true;
        }

        if (MasterVolume != _prevMasterVolume || BgmVolume != _prevBgmVolume || SeVolume != _prevSeVolume)
        {
            _prevMasterVolume = MasterVolume;
            _prevBgmVolume = BgmVolume;
            _prevSeVolume = SeVolume;
            ApplyVolume();
            changed = true;
        }

        if (Coin.FreePlay != _prevFreePlay)
        {
            _prevFreePlay = Coin.FreePlay;
            Coin.Save();
        }

        if (Coin.RequiredCoins != _prevRequiredCoins)
        {
            _prevRequiredCoins = Coin.RequiredCoins;
            Coin.Save();
        }

        if (DancerFps != _prevDancerFps)
        {
            _prevDancerFps = DancerFps;
            Dancer.TargetFps = DancerFps;
            changed = true;
        }

        if (DonChanFps != _prevDonChanFps)
        {
            _prevDonChanFps = DonChanFps;
            DonChan3D.PlaybackFps = DonChanFps;
            changed = true;
        }

        if (DonChanLowQuality != _prevDonChanLowQuality)
        {
            _prevDonChanLowQuality = DonChanLowQuality;
            DonChan3D.RenderQuality = DonChanLowQuality ? DonChan3D.QualityLevel.Low : DonChan3D.QualityLevel.High;
            changed = true;
        }

        if (DancerLowQuality != _prevDancerLowQuality)
        {
            _prevDancerLowQuality = DancerLowQuality;
            Dancer.Quality = DancerLowQuality ? Dancer.QualityLevel.Low : Dancer.QualityLevel.High;
            changed = true;
        }

        if (changed)
            Save();
    }

    /// <summary>
    /// AudioEngine初期化後に起動時1回だけ呼ぶ。
    /// 静的コンストラクタのLoad()時点ではAudioEngineが未初期化のため音量を適用できないため、
    /// Program.csのAudioEngine.Init()直後にここから改めて反映する。
    /// </summary>
    public static void ApplyVolumeOnStartup() => ApplyVolume();

    // 💡 SE/BGM/マスターの3系統を掛け合わせた実効音量をそれぞれの再生系へ反映する
    private static void ApplyVolume()
    {
        float bgmVol = EffectiveBgmVolume;
        float seVol = EffectiveSeVolume;

        DrumInput.DonSound?.Volume = seVol;
        DrumInput.KaSound?.Volume = seVol;
        Enso.SetMusicVolume(bgmVol);
        Enso.SetSEVolume(seVol);
        Program.UpdateMenuBgmVolume();

        _prevMasterVolume = MasterVolume;
        _prevBgmVolume = BgmVolume;
        _prevSeVolume = SeVolume;
    }
}
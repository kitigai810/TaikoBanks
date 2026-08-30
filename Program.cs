using Raylib_cs;
using System.Numerics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Text;
using System.Globalization;
using System.Text.Json;

public partial class Program
{
    // ==================================================================
    // 💡 バージョン表記。ウィンドウタイトルに "TaikoStormNeo Rewrite(Debug Build) Ver.00.00" のように付与する。
    //    Debug Buildならビルド種別を明記し、Releaseビルドでは空欄にする。バージョン番号はここで手動管理。
    // ==================================================================
    public const string GameVersion = "20260829";

    private static string GetWindowTitle()
    {
#if DEBUG
        const string buildTag = "Debug Build";
#else
        const string buildTag = "";
#endif
        string buildPart = string.IsNullOrEmpty(buildTag) ? "" : $"({buildTag})";
        return $"TaikoBank {buildPart} {GameVersion}";
    }

    private static bool _titleInitialized;
    private static bool _songSelectInitialized;
    private static bool _diffSelectInitialized;

    private static void EnsureTitleInitialized()
    {
        if (_titleInitialized) return;
        StartupStep("TitleScene.Init (lazy)", TitleScene.Init);
        StartupStep("DonChanScene.Configure (title)", DonChanScene.Init);
        TitleScene.PauseBgm();
        _titleInitialized = true;
    }

    private static void EnsureSongSelectInitialized()
    {
        if (_songSelectInitialized) return;
        StartupStep("SongSelectScene.Init (lazy)", SongSelectScene.Init);
        StartupStep("DonChanScene.Init (lazy)", DonChanScene.Init);
        _songSelectInitialized = true;
    }

    private static void EnsureDiffSelectInitialized()
    {
        if (_diffSelectInitialized) return;
        StartupStep("DiffSelectScene.Init (lazy)", DiffSelectScene.Init);
        _diffSelectInitialized = true;
    }

    enum Scene { Demo, Title, SongSelect, DanSelect, DifficultySelect, SongLoad, Playing, Result, DanResult, Kisekae, Error }

    static Scene _scene = Scene.Demo;
    static bool _ensoStarted;
    // 難易度選択中にF9を押すと2P参加を予約する。曲決定フレームでの同時押しは不要。
    static bool _p2JoinRequested;

    // ====================================================================
    // 仮想スクリーン(常に1920x1080固定)。
    // ゲーム内の描画は全てこの仮想解像度を前提にしているため、実ウィンドウサイズが
    // 変わってもここへは常に1920x1080でレンダリングし、最後にウィンドウサイズへ
    // スケーリング(レターボックス)して転写する。これによりウィンドウをリサイズしても
    // 座標計算が崩れなくなる。
    // ====================================================================
    public const int VirtualWidth = 1920;
    public const int VirtualHeight = 1080;
    private static RenderTexture2D _virtualScreen;

    // 💡 Discord Rich Presence表示用に、選択中の曲名/難易度を一時保持しておく
    static string _rpcSongTitle = "";
    internal static string _rpcDifficulty = "";

    // 現在プレイ中のTJAファイルパス(スコア保存先の決定に使用)
    internal static string _currentTjaPath = "";

    // ==================================================================
    // 💡 段位道場モード(DanSelectSceneでDon1/Don2確定後、1~3曲を連続して演奏する特別仕様)
    //     通常のSongLoad/Playing/Result遷移をそのまま使い、このフラグで挙動だけ分岐する。
    // ==================================================================
    static bool _isDanMode;
    static List<string> _danSongPaths = new();
    static List<TaikoNauts.Core.Taiko.Charts.Course> _danSongCourses = new();
    static List<string> _danSongGenres = new();
    static string _danTitle = "";
    static int _danSongIndex;
    static int _danTotalNotes; // 💡 選択中段位の1~3曲合計ノーツ数(Enso.DanTotalNotes経由でGaugeへ渡す)
    static List<string> _danSongTitles = new();          // 💡 DanResultScene表示用(確定時にDanSelectSceneからコピー)
    static List<Exam.Condition> _danConditions = new();  // 💡 DanResultScene判定用(確定時にDanSelectSceneからコピー)
    static Exam.ConditionGauge _danGauge;                 // 💡 魂ゲージ条件バー(Result.cs)用の合格閾値(conditionGauge)
    static List<Exam.SongResult> _danSongResults = new(); // 💡 1曲終わるごとに追加していく各曲の成績

    // Title→SongSelect遷移時のフェードイン(黒→通常)用タイマー
    static float _songSelectFadeInTimer = 999f;
    const float SONGSELECT_FADE_IN_DURATION = 0.5f;




    private static long _memLogLast = 0;
    private static readonly System.Diagnostics.Stopwatch _startupTimer = new();

    private static void StartupStep(string name, Action action)
    {
        long started = _startupTimer.ElapsedMilliseconds;
        try
        {
            action();
        }
        finally
        {
            Console.WriteLine($"[STARTUP] {name,-24} {(_startupTimer.ElapsedMilliseconds - started),6} ms  total={_startupTimer.ElapsedMilliseconds,6} ms");
        }
    }

    private static void MemLog(string label)
    {
        using var proc = System.Diagnostics.Process.GetCurrentProcess();
        proc.Refresh();

        long ws = proc.WorkingSet64;
        long privateBytes = proc.PrivateMemorySize64;
        long managedHeap = GC.GetTotalMemory(false);
        long deltaWs = ws - _memLogLast;

        Console.WriteLine(
            $"[MEM] {label,-28} " +
            $"WorkingSet={ws / 1024.0 / 1024.0,8:0.0}MB " +
            $"(Δ{deltaWs / 1024.0 / 1024.0,+8:0.0}MB)  " +
            $"Private={privateBytes / 1024.0 / 1024.0,8:0.0}MB  " +
            $"ManagedHeap={managedHeap / 1024.0 / 1024.0,8:0.0}MB"
        );

        _memLogLast = ws;
    }

    private static List<System.Reflection.FieldInfo> _vramFieldCache;
    private static long _vramLogLast = 0;

    // ===== VRAM詳細表示用 =====
    private sealed class VramTextureItem
    {
        public uint Id;
        public int Width;
        public int Height;
        public int Mipmaps;
        public PixelFormat Format;
        public long Bytes;
        public readonly List<string> References = new List<string>();
    }

    private sealed class VramOwnerItem
    {
        public string Owner = "";
        public long Bytes;
        public int TextureCount;
        public readonly HashSet<uint> Ids = new HashSet<uint>();
    }

    private static readonly List<VramTextureItem> _vramDetailItems = new List<VramTextureItem>();
    private static readonly List<VramOwnerItem> _vramDetailOwnerList = new List<VramOwnerItem>();
    private static readonly List<KeyValuePair<string, long>> _vramDetailTypeList = new List<KeyValuePair<string, long>>();

    private static long _vramDetailTotalBytes = 0;
    private static int _vramDetailUniqueCount = 0;
    private static int _vramDetailReferenceCount = 0;
    private static bool _vramDetailInitialized = false;
    private static float _vramDetailUpdateTimer = 0f;

    // 0=OFF, 1=テクスチャ, 2=所有者, 3=種別
    public static int _vramDetailMode = 0;
    private static int _vramDetailScroll = 0;
    private static int _vramDetailMaxLines = 20;

    private static void BuildVramFieldCache()
    {
        _vramFieldCache = new List<System.Reflection.FieldInfo>();

        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;

        foreach (var type in System.Reflection.Assembly.GetExecutingAssembly().GetTypes())
        {
            System.Reflection.FieldInfo[] fields;
            try
            {
                fields = type.GetFields(flags);
            }
            catch
            {
                continue;
            }

            foreach (var f in fields)
            {
                var ft = f.FieldType;

                if (ft == typeof(Texture2D) ||
                    ft == typeof(Texture2D[]) ||
                    ft == typeof(Texture2D[][]))
                {
                    _vramFieldCache.Add(f);
                }
            }
        }
    }

    private static long EstimateTextureBytes(Texture2D tex)
    {
        if (tex.Id == 0) return 0;

        int bpp = tex.Format switch
        {
            PixelFormat.UncompressedGrayscale => 1,
            PixelFormat.UncompressedGrayAlpha => 2,
            PixelFormat.UncompressedR5G6B5 => 2,
            PixelFormat.UncompressedR5G5B5A1 => 2,
            PixelFormat.UncompressedR4G4B4A4 => 2,
            PixelFormat.UncompressedR8G8B8 => 3,
            PixelFormat.UncompressedR8G8B8A8 => 4,
            PixelFormat.UncompressedR32 => 4,
            PixelFormat.UncompressedR32G32B32 => 12,
            PixelFormat.UncompressedR32G32B32A32 => 16,
            _ => 0,
        };

        long baseBytes = bpp > 0
            ? (long)tex.Width * tex.Height * bpp
            : (long)tex.Width * tex.Height / 2;

        if (tex.Mipmaps > 1)
            baseBytes = (long)(baseBytes * 1.33);

        return baseBytes;
    }

    internal static void RefreshVramDetailSnapshot()
    {
        if (_vramFieldCache == null)
            BuildVramFieldCache();

        var byId = new Dictionary<uint, VramTextureItem>();
        var byOwner = new Dictionary<string, VramOwnerItem>();
        var typeIds = new Dictionary<string, HashSet<uint>>();
        var typeBytes = new Dictionary<string, long>();

        long uniqueTotal = 0;
        int referenceCount = 0;

        void AddTexture(string owner, string typeName, Texture2D tex)
        {
            if (tex.Id == 0) return;

            referenceCount++;
            long bytes = EstimateTextureBytes(tex);

            if (!byId.TryGetValue(tex.Id, out var item))
            {
                item = new VramTextureItem
                {
                    Id = tex.Id,
                    Width = tex.Width,
                    Height = tex.Height,
                    Mipmaps = tex.Mipmaps,
                    Format = tex.Format,
                    Bytes = bytes,
                };

                byId[tex.Id] = item;
                uniqueTotal += bytes;
            }

            if (!item.References.Contains(owner))
                item.References.Add(owner);

            if (!byOwner.TryGetValue(owner, out var ownerItem))
            {
                ownerItem = new VramOwnerItem
                {
                    Owner = owner
                };
                byOwner[owner] = ownerItem;
            }

            if (ownerItem.Ids.Add(tex.Id))
            {
                ownerItem.Bytes += bytes;
                ownerItem.TextureCount++;
            }

            if (!typeIds.TryGetValue(typeName, out var ids))
            {
                ids = new HashSet<uint>();
                typeIds[typeName] = ids;
                typeBytes[typeName] = 0;
            }

            if (ids.Add(tex.Id))
                typeBytes[typeName] += bytes;
        }

        foreach (var f in _vramFieldCache)
        {
            object val;
            try
            {
                val = f.GetValue(null);
            }
            catch
            {
                continue;
            }

            if (val == null) continue;

            string typeName = f.DeclaringType?.Name ?? "?";
            string owner = $"{typeName}.{f.Name}";

            if (val is Texture2D tex)
            {
                AddTexture(owner, typeName, tex);
            }
            else if (val is Texture2D[] arr)
            {
                foreach (var t in arr)
                    AddTexture(owner, typeName, t);
            }
            else if (val is Texture2D[][] arr2)
            {
                foreach (var row in arr2)
                {
                    if (row == null) continue;

                    foreach (var t in row)
                        AddTexture(owner, typeName, t);
                }
            }
        }

        _vramDetailItems.Clear();
        _vramDetailItems.AddRange(
            byId.Values
                .OrderByDescending(x => x.Bytes)
                .ThenBy(x => x.Id)
        );

        _vramDetailOwnerList.Clear();
        _vramDetailOwnerList.AddRange(
            byOwner.Values
                .OrderByDescending(x => x.Bytes)
                .ThenBy(x => x.Owner)
        );

        _vramDetailTypeList.Clear();
        _vramDetailTypeList.AddRange(
            typeBytes
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
        );

        _vramDetailTotalBytes = uniqueTotal;
        _vramDetailUniqueCount = _vramDetailItems.Count;
        _vramDetailReferenceCount = referenceCount;
        _vramDetailInitialized = true;
        _vramDetailUpdateTimer = 0f;
    }

    private static void UpdateVramDetailSnapshot(float dt)
    {
        _vramDetailUpdateTimer += dt;

        // 0.5秒ごとに更新
        if (_vramDetailInitialized && _vramDetailUpdateTimer < 0.5f)
            return;

        RefreshVramDetailSnapshot();
    }

    private static string FormatShort(PixelFormat format)
    {
        return format.ToString()
            .Replace("Uncompressed", "")
            .Replace("PixelFormat.", "");
    }

    private static string JoinReferences(List<string> refs, int max)
    {
        if (refs == null || refs.Count == 0)
            return "-";

        var distinct = refs.Distinct().ToArray();

        if (distinct.Length <= max)
            return string.Join(", ", distinct);

        return string.Join(", ", distinct.Take(max)) + $", +{distinct.Length - max}";
    }

    private static void VramLog(string label)
    {
        RefreshVramDetailSnapshot();

        long delta = _vramDetailTotalBytes - _vramLogLast;

        double totalMB = _vramDetailTotalBytes / 1048576.0;
        double deltaMB = delta / 1048576.0;

        string deltaText =
            (deltaMB >= 0 ? "+" : "") +
            deltaMB.ToString("0.00", CultureInfo.InvariantCulture);

        Console.WriteLine(
            $"[VRAM] {label,-28} " +
            $"UniqueTotal={totalMB,8:0.00}MB " +
            $"(Δ{deltaText,10}MB)  " +
            $"Refs={_vramDetailReferenceCount}  " +
            $"Unique={_vramDetailUniqueCount}  " +
            $"GLID={_currentTextureId}(+{_textureIdGrowthPerSec}/s)"
        );

        foreach (var kv in _vramDetailTypeList.Take(5))
        {
            Console.WriteLine(
                $"        [TYPE] {kv.Key,-24} {kv.Value / 1048576.0,8:0.00}MB"
            );
        }

        foreach (var item in _vramDetailItems.Take(10))
        {
            Console.WriteLine(
                $"        [TEX ] ID={item.Id,4} " +
                $"{item.Width,4}x{item.Height,-4} " +
                $"{FormatShort(item.Format),-18} " +
                $"mip={item.Mipmaps} " +
                $"{item.Bytes / 1048576.0,8:0.00}MB " +
                $"refs={JoinReferences(item.References, 2)}"
            );
        }

        _vramLogLast = _vramDetailTotalBytes;
    }

    // ==================================================================
    // 💡 ウィンドウ解像度変更(設定パネルから呼ばれる)。
    //     モニタサイズと完全一致させるとWindowsのフルスクリーン最適化が誤爆することがあるため、
    //     一致する場合は1px内側にずらして回避する。
    // ==================================================================
    public static void ApplyWindowResolution(int w, int h)
    {
        Raylib.SetWindowSize(w, h);
        CenterWindowOnMonitor(w, h);
    }

    private static void CenterWindowOnMonitor(int w, int h)
    {
        int monitor = Raylib.GetCurrentMonitor();
        int mw = Raylib.GetMonitorWidth(monitor);
        int mh = Raylib.GetMonitorHeight(monitor);

        if (w >= mw && h >= mh)
        {
            // モニタと同一/超過サイズだとフルスクリーン最適化のトリガーになりうるので1px縮める
            w = Math.Max(1, Math.Min(w, mw - 1));
            Raylib.SetWindowSize(w, h);
        }

        int px = Math.Max(0, (mw - w) / 2);
        int py = Math.Max(0, (mh - h) / 2);
        Raylib.SetWindowPosition(px, py);
    }

    // ==================================================================
    // 💡 ウィンドウ/画面のアスペクト比がゲーム基準(16:9)と異なる場合に、
    //     はみ出す領域(左右または上下)へ黒帯を描画する。
    //     Songs.GetViewport() が返す安全描画領域(vx,vy,vw,vh)の外側を塗りつぶす。
    // ==================================================================
    private static void DrawScreenBars()
    {
        var (vx, vy, vw, vh) = Songs.GetViewport();
        int sw = VirtualWidth;
        int sh = VirtualHeight;

        if (vx > 0)
        {
            Raylib.DrawRectangle(0, 0, vx, sh, Color.Black);
            Raylib.DrawRectangle(vx + vw, 0, sw - (vx + vw), sh, Color.Black);
        }

        if (vy > 0)
        {
            Raylib.DrawRectangle(0, 0, sw, vy, Color.Black);
            Raylib.DrawRectangle(0, vy + vh, sw, sh - (vy + vh), Color.Black);
        }
    }

    // ====================================================================
    // 実ウィンドウサイズに対する仮想スクリーン(1920x1080)の拡大率とレターボックス量を計算する。
    // ====================================================================
    private static (float scale, float offsetX, float offsetY) GetVirtualScreenLayout()
    {
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();

        float scale = Math.Min((float)sw / VirtualWidth, (float)sh / VirtualHeight);
        if (scale <= 0f) scale = 1f;

        float offsetX = (sw - VirtualWidth * scale) * 0.5f;
        float offsetY = (sh - VirtualHeight * scale) * 0.5f;

        return (scale, offsetX, offsetY);
    }

    // マウス座標を仮想スクリーン(1920x1080)基準に変換する。
    // 毎フレームの入力処理前に呼んでおくことで、SettingsPanel等が使う
    // Raylib.GetMousePosition() が実ウィンドウサイズに関わらず1920x1080基準の座標を返すようになる。
    private static void UpdateVirtualMouseMapping()
    {
        var (scale, offsetX, offsetY) = GetVirtualScreenLayout();
        Raylib.SetMouseOffset((int)-offsetX, (int)-offsetY);
        Raylib.SetMouseScale(1f / scale, 1f / scale);
    }

    // 仮想スクリーン(1920x1080)に描画した内容を、実ウィンドウへアスペクト比を保ったまま
    // 中央寄せで拡大縮小して転写する(はみ出す領域は黒帯)。
    // BootScreen等、メインループの外(Main()内でBootScreen.Run()を呼ぶタイミング)から
    // 仮想スクリーンへ描画するための公開ラッパー。
    public static void BeginVirtualFrame()
    {
        Raylib.BeginTextureMode(_virtualScreen);
    }

    public static void EndVirtualFrame()
    {
        Raylib.EndTextureMode();
    }

    public static void PresentVirtualScreen()
    {
        var (scale, offsetX, offsetY) = GetVirtualScreenLayout();

        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.Black);

        // 💡 末尾を 'Premultiply' (dなし) に変更
        Raylib.BeginBlendMode(BlendMode.AlphaPremultiply);

        Raylib.DrawTexturePro(
            _virtualScreen.Texture,
            new Rectangle(0, 0, VirtualWidth, -VirtualHeight),
            new Rectangle(offsetX, offsetY, VirtualWidth * scale, VirtualHeight * scale),
            Vector2.Zero,
            0f,
            Color.White
        );

        Raylib.EndBlendMode();

        Raylib.EndDrawing();
    }

    public static void Main()
    {
        _startupTimer.Restart();

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            AudioEngine.StopAll();
            if (e.ExceptionObject is Exception uex) Error.Show(uex);
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            AudioEngine.StopAll();
            Error.Show(e.Exception);
            e.SetObserved();
        };

        MemLog("起動直後");
        VramLog("起動直後");

        // 💡 Discord Rich Presence 初期化(Discordが起動していない/APP_ID未設定でも例外を投げない)
#if DEBUG
        DiscordRpc.IsDebugMode = true;
#endif
        StartupStep("DiscordRpc.Initialize", () =>
        {
            DiscordRpc.Initialize();
            DiscordRpc.SetTitle();
        });

        var initRes = SettingsPanel.ResolutionOptions[SettingsPanel.ResolutionIndex];
        int initW = initRes.W;
        int initH = initRes.H;

        // INFOログ(デフォルトの1x1テクスチャ読込/解放など)を抑制し、警告以上のみ表示する
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        StartupStep("Raylib.InitWindow", () =>
        {
            Raylib.InitWindow(initW, initH, GetWindowTitle());
            Raylib.SetTargetFPS(120);
            Raylib.SetExitKey(KeyboardKey.Null);
        });

        // 常に1920x1080でレンダリングするための仮想スクリーンを作成
        StartupStep("LoadRenderTexture", () =>
        {
            _virtualScreen = Raylib.LoadRenderTexture(VirtualWidth, VirtualHeight);
        });
        // Raylib.SetTextureFilter(_virtualScreen.Texture, TextureFilter.Bilinear);

        // モニタ中央に配置(モニタサイズと完全一致する場合はWindowsのフルスクリーン最適化誤爆を避けるため微調整)
        CenterWindowOnMonitor(initW, initH);

        MemLog("InitWindow後");
        VramLog("InitWindow後");

        // ==================================================================
        // 💡 起動演出(システムチェック→LOADING→NOTICE)
        //     この中でTextureSongs.PreloadAll()によるテクスチャ・曲の一括先読みも
        //     LOADING表示と並行して実行される。
        // ==================================================================
        StartupStep("BootScreen.Run", () => BootScreen.Run(SettingsPanel.SongsFolder));

        MemLog("BootScreen.Run後");
        VramLog("BootScreen.Run後");

        unsafe { DrumInput.Start((IntPtr)Raylib.GetWindowHandle()); }
        unsafe { DrumInput2.Start((IntPtr)Raylib.GetWindowHandle()); }

        MemLog("DrumInput.Start後");
        VramLog("DrumInput.Start後");

        StartupStep("AudioEngine.Init", AudioEngine.Init);

        // AudioEngine初期化後に保存済み音量を即時反映する
        // (SettingsPanel静的コンストラクタのLoad()時点ではAudioEngineが未初期化のため反映できない)
        SettingsPanel.ApplyVolumeOnStartup();

        // ガイド文字の保存値を読み込む(呼び忘れ防止のためここで一元化)
        SettingsPanel.LoadTitleConfig();

        MemLog("AudioEngine.Init後");
        VramLog("AudioEngine.Init後");

        StartupStep("G.InitializeFonts", () => G.InitializeFonts(SettingsPanel.SongsFolder));

        MemLog("G.InitializeFonts後");
        VramLog("G.InitializeFonts後");

        StartupStep("NotesTexture.Load", NotesTexture.Load);

        MemLog("NotesTexture.Load後");
        VramLog("NotesTexture.Load後");

        StartupStep("DemoScene.Init", DemoScene.Init);

        MemLog("DemoScene.Init後");
        VramLog("DemoScene.Init後");

        MemLog("TitleScene.Init後");
        VramLog("TitleScene.Init後");

        MemLog("DiffSelectScene.Init後");
        VramLog("DiffSelectScene.Init後");

        MemLog("SongSelectScene.Init後");
        VramLog("SongSelectScene.Init後");

        MemLog("DonChanScene.Init後");
        VramLog("DonChanScene.Init後");

        while (!Raylib.WindowShouldClose() && !SongSelectScene.ShouldExit)
        {
            // 実ウィンドウサイズがどうであれ、マウス座標は常に仮想スクリーン(1920x1080)基準になるよう変換しておく
            UpdateVirtualMouseMapping();

            SettingsPanel.Update();
            AudioEngine.Update();

            float dt = Raylib.GetFrameTime();

            // VRAM詳細表示の操作（デバッグ用）
            // モード: 0=概要のみ, 1=テクスチャ, 2=所有者, 3=種別, 4=完全非表示
            if (Raylib.IsKeyPressed(KeyboardKey.F8))
            {
                _vramDetailMode = (_vramDetailMode + 1) % 5;
            }

            if (_vramDetailMode >= 1 && _vramDetailMode <= 3)
            {
                if (Raylib.IsKeyPressed(KeyboardKey.PageDown))
                    _vramDetailScroll++;

                if (Raylib.IsKeyPressed(KeyboardKey.PageUp))
                    _vramDetailScroll = Math.Max(0, _vramDetailScroll - 1);

                if (Raylib.IsKeyPressed(KeyboardKey.Home))
                    _vramDetailScroll = 0;

                if (Raylib.IsKeyPressed(KeyboardKey.End))
                    _vramDetailScroll = int.MaxValue;
            }

            // Discord Rich Presenceのコールバック処理(接続確立/切断イベント等)
            DiscordRpc.Update();

            // OpenGLテクスチャIDのリアルタイムチェックと毎秒増加量の計算
            // 💡 毎フレーム1x1ダミーテクスチャをLoad/Unloadするため、raylibのINFOログが
            //    埋まる原因になっていた。VRAM詳細表示(F8)が非表示の間は不要なので呼ばない。
            if (_vramDetailMode >= 1 && _vramDetailMode <= 3)
                UpdateOpenGLTextureDebug(dt);

            // VRAM詳細表示が有効なときだけスナップショットを更新する。
            // 通常プレイ中のリフレクション走査とコンソール出力を避け、周期的なフレーム停止を防ぐ。
            if (_vramDetailMode >= 1 && _vramDetailMode <= 3)
                UpdateVramDetailSnapshot(dt);

            try
            {
                SongSelectScene.TickAlways(dt);

                // 💡 設定パネルからのデバッグ用シーン移動リクエストを反映
                //    (常時状態が揃っているTitle/SongSelectのみ対応。DifficultySelect等は選択中データが必要なため対象外)
                var sceneJumpReq = SettingsPanel.ConsumeSceneJumpRequest();
                if (sceneJumpReq != SettingsPanel.DebugSceneTarget.None)
                {
                    switch (sceneJumpReq)
                    {
                        case SettingsPanel.DebugSceneTarget.Title:
                            EnsureTitleInitialized();
                            SongSelectScene.PauseMenuBgm();
                            TitleScene.ResumeBgm();
                            _scene = Scene.Title;
                            break;
                        case SettingsPanel.DebugSceneTarget.SongSelect:
                            EnsureSongSelectInitialized();
                            TitleScene.PauseBgm();
                            SongSelectScene.ResumeMenuBgm();
                            _songSelectFadeInTimer = 0f;
                            _scene = Scene.SongSelect;
                            DiscordRpc.SetSongSelect();
                            break;
                    }
                }

                if (_scene == Scene.Error)
                {
                    Error.Update();

                    Raylib.BeginTextureMode(_virtualScreen);
                    Raylib.ClearBackground(Color.Black);
                    Error.Draw();
                    DrawScreenBars();
                    DrawDebugOverlay();
                    Raylib.EndTextureMode();

                    if (!Error.HasError)
                    {
                        EnsureTitleInitialized();
                        _scene = Scene.Title;
                    }
                }
                else if (_scene == Scene.Demo)
                {
                    if (!SettingsPanel.Open) DemoScene.Update(dt);

                    Raylib.BeginTextureMode(_virtualScreen);
                    DemoScene.Draw();
                    SettingsPanel.Draw();
                    DrawScreenBars();
                    DrawDebugOverlay();
                    Raylib.EndTextureMode();

                    if (DemoScene.Confirmed)
                    {
                        DemoScene.ResetState();
                        EnsureTitleInitialized();
                        TitleScene.ResumeBgm();
                        _scene = Scene.Title;
                    }
                }
                else if (_scene == Scene.Title)
                {
                    if (Raylib.IsKeyPressed(KeyboardKey.F9))
                    {
                        throw new InvalidOperationException("F9によるテスト例外です");
                    }

                    if (!SettingsPanel.Open) TitleScene.Update(dt);

                    Raylib.BeginTextureMode(_virtualScreen);
                    TitleScene.Draw();
                    SettingsPanel.Draw();
                    DrawScreenBars();
                    DrawDebugOverlay();
                    Raylib.EndTextureMode();

                    if (TitleScene.Confirmed)
                    {
                        bool wasDanMode = TitleScene.DanMode; // 💡 ResetState()で戻される前に読んでおく
                        TitleScene.ResetState();

                        if (wasDanMode)
                        {
                            DanSelectScene.Init();
                            DanSelectScene.Enter();
                            _scene = Scene.DanSelect;
                            DiscordRpc.SetSongSelect(); // 段位選択もひとまず「曲選択中」として表示
                        }
                        else
                        {
                            EnsureSongSelectInitialized();
                            SongSelectScene.ResumeMenuBgm();
                            _scene = Scene.SongSelect;
                            _songSelectFadeInTimer = 0f;
                            DiscordRpc.SetSongSelect();
                        }
                    }
                }
                else if (_scene == Scene.DanSelect)
                {
                    DanSelectScene.Update(dt);

                    Raylib.BeginTextureMode(_virtualScreen);
                    DanSelectScene.Draw();
                    DrawScreenBars();
                    DrawDebugOverlay();
                    Raylib.EndTextureMode();

                    if (DanSelectScene.Confirmed)
                    {
                        DanSelectScene.ResetConfirm();

                        // 💡 段位道場モード開始:選択中段位の曲(1~3曲)を先頭から順に演奏する
                        _danSongPaths = new List<string>(DanSelectScene.SongPaths);
                        _danSongCourses = new List<TaikoNauts.Core.Taiko.Charts.Course>(DanSelectScene.SongCourses);
                        _danSongGenres = new List<string>(DanSelectScene.SongGenres);
                        _danTitle = DanSelectScene.SelectedDanTitle;
                        _danTotalNotes = DanSelectScene.TotalNoteCount;
                        _danSongTitles = new List<string>(DanSelectScene.SongTitles);
                        _danConditions = new List<Exam.Condition>(DanSelectScene.SelectedConditions);
                        _danGauge = DanSelectScene.SelectedGauge; // ⚠️ DanSelectScene側にこのプロパティが無ければ、
                                                                  //    dan.jsonのconditionGauge(Exam.LoadFromDanJson*)を
                                                                  //    保持している既存のプロパティ名に合わせて直してください
                        _danSongResults = new List<Exam.SongResult>();
                        _danSongIndex = 0;
                        _isDanMode = true;

                        StartDanSong();
                    }
                }
                else if (_scene == Scene.SongSelect)
                {
                    if (Raylib.IsKeyPressed(KeyboardKey.F7))
                    {
                        DonChan3D.LogMemoryReport();
                    }

                    SongSelectScene.Update();

                    if (SongSelectScene.WantsKisekae)
                    {
                        SongSelectScene.ResetTransitionFlags();
                        _scene = Scene.Kisekae;
                        DiscordRpc.SetKisekae();
                    }
                    else if (SongSelectScene.SongConfirmed)
                    {
                        // 曲ごとに2P参加予約を初期化する。F9を押した曲だけ2Pになる。
                        _p2JoinRequested = false;
                        SongSelectScene.ResetTransitionFlags();
                        EnsureDiffSelectInitialized();
                        _scene = Scene.DifficultySelect;

                        var confirmedSong = SongSelectScene.SelectedSong;
                        string confirmedTitle = string.IsNullOrEmpty(confirmedSong.Title) ? confirmedSong.TitleJP : confirmedSong.Title;
                        DiscordRpc.SetDifficultySelect(confirmedTitle);
                    }

                    Raylib.BeginTextureMode(_virtualScreen);
                    Raylib.ClearBackground(Color.Black);
                    SongSelectScene.DrawBackground();
                    SongSelectScene.Draw();
                    SettingsPanel.Draw();

                    if (_songSelectFadeInTimer < SONGSELECT_FADE_IN_DURATION)
                    {
                        _songSelectFadeInTimer += dt;

                        float p = Math.Clamp(1f - _songSelectFadeInTimer / SONGSELECT_FADE_IN_DURATION, 0f, 1f);
                        byte fadeAlpha = (byte)(255 * p);

                        Raylib.DrawRectangle(
                            0,
                            0,
                            VirtualWidth,
                            VirtualHeight,
                            new Color((byte)0, (byte)0, (byte)0, fadeAlpha)
                        );
                    }

                    DrawScreenBars();
                    DrawDebugOverlay();
                    Raylib.EndTextureMode();
                }
                else if (_scene == Scene.DifficultySelect)
                {
                    if (!SettingsPanel.Open)
                    {
                        DiffSelectScene.Update(dt);

                        // 難易度決定と同時に押す必要はない。選択中に一度F9を押せば2P参加を予約する。
                        if (Raylib.IsKeyPressed(KeyboardKey.F9))
                            _p2JoinRequested = true;
                    }

                    if (DiffSelectScene.BackToSongSelect)
                    {
                        _p2JoinRequested = false;
                        DiffSelectScene.ResetState();
                        _scene = Scene.SongSelect;
                        SongSelectScene.ResumeMenuBgm();
                    }
                    else if (DiffSelectScene.IsDifficultyConfirmed)
                    {
                        int lvIdx = DiffSelectScene.ChosenCourseIndex;
                        var chosenCourse = (TaikoNauts.Core.Taiko.Charts.Course)lvIdx;
                        var selectedSong = SongSelectScene.SelectedSong;

                        var meta = new SongMeta
                        {
                            // ロード画面では SongLoadScreen 側がJAフィールドを優先表示する。
                            Title = selectedSong.Title,
                            Subtitle = selectedSong.Subtitle,
                            TitleJP = selectedSong.TitleJP,
                            SubtitleJP = selectedSong.SubtitleJP,
                            Genre = SongSelectScene.SelectedSongGenre,
                            Difficulty = DiffSelectScene.DiffNames[lvIdx],
                            Level = selectedSong.Levels[lvIdx]
                        };

                        // 難易度選択中にF9で予約された場合だけ2Pとして開始する。
                        Enso.SetP2Active(_p2JoinRequested);

                        SongLoadScreen.Begin(selectedSong.TjaPath, chosenCourse, meta);
                        VramLog($"曲読み込み開始: {meta.Title}");

                        _ensoStarted = false;
                        _scene = Scene.SongLoad;
                        _rpcSongTitle = meta.Title;
                        _rpcDifficulty = meta.Difficulty;
                        _currentTjaPath = selectedSong.TjaPath;

                        DiffSelectScene.ResetState();
                    }

                    Raylib.BeginTextureMode(_virtualScreen);
                    Raylib.ClearBackground(Color.Black);
                    DiffSelectScene.Draw();
                    SettingsPanel.Draw();
                    DrawScreenBars();
                    DrawDebugOverlay();
                    Raylib.EndTextureMode();
                }
                else if (_scene == Scene.Kisekae)
                {
                    Kisekae.Update();

                    Raylib.BeginTextureMode(_virtualScreen);
                    Raylib.ClearBackground(Color.Black);
                    Kisekae.Draw();
                    DrawScreenBars();
                    DrawDebugOverlay();
                    Raylib.EndTextureMode();

                    if (Kisekae.ShouldReturn)
                    {
                        Kisekae.Unload();
                        _scene = Scene.SongSelect;
                        DiscordRpc.SetSongSelect();
                    }
                }
                else if (_scene == Scene.SongLoad)
                {
                    var st = SongLoadScreen.Update(dt);
                    var phase = SongLoadScreen.Phase;

                    if (SongLoadScreen.LoadFailed)
                    {
                        _ensoStarted = false;
                        SongLoadScreen.Dispose(); // 読み込み失敗時のアセット解放
                        _scene = Scene.SongSelect;
                    }
                    else
                    {
                        if (phase == SongLoadScreen.LoadPhase.Out)
                        {
                            if (!_ensoStarted)
                            {
                                SongSelectScene.PauseMenuBgm();
                                Enso.Start();
                                _ensoStarted = true;
                            }

                            Enso.Update();
                            NotesTexture.Update();
                        }

                        Raylib.BeginTextureMode(_virtualScreen);
                        Raylib.ClearBackground(Color.Black);

                        if (phase == SongLoadScreen.LoadPhase.In)
                        {
                            DiffSelectScene.Draw();
                        }
                        else if (phase == SongLoadScreen.LoadPhase.Out)
                        {
                            Enso.Draw();
                        }

                        SongLoadScreen.Draw();
                        DrawScreenBars();
                        DrawDebugOverlay();
                        Raylib.EndTextureMode();

                        if (st == SongLoadScreen.Status.Ready)
                        {
                            _scene = Scene.Playing;
                            DiscordRpc.SetPlaying(_rpcSongTitle, _rpcDifficulty);
                        }
                    }
                }
                else if (_scene == Scene.Playing)
                {
                    Enso.Update();
                    NotesTexture.Update();

                    if (Raylib.IsKeyPressed(KeyboardKey.F2))
                    {
                        // 💡 デバッグ用: 一からやり直し(スコア/コンボ/譜面位置を含め完全リセットして再スタート)
                        // Stop()で入力受付・再生を止めてからInit()する(積み残しの入力キューや
                        // 再生中フラグを引きずったまま再初期化しない)
                        Enso.Stop();
                        Enso.Init();
                        Enso.Start();
                    }
                    else if (Raylib.IsKeyPressed(KeyboardKey.F3))
                    {
                        // 💡 デバッグ用: 強制的にSongSelectへ戻る(段位モード中でもSongSelect優先)
                        Enso.Stop();
                        _isDanMode = false;
                        Enso.DanMode = false;
                        SongLoadScreen.Dispose();
                        _scene = Scene.SongSelect;
                        SongSelectScene.ResumeMenuBgm();
                        DiscordRpc.SetSongSelect();
                    }
                    else if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                    {
                        Enso.Stop();

                        if (_isDanMode)
                        {
                            // 💡 段位モードはSongLoadScreenを経由していないためDispose()は不要
                            _isDanMode = false;
                            Enso.DanMode = false;
                            DanSelectScene.Enter();
                            _scene = Scene.DanSelect;
                            DiscordRpc.SetSongSelect();
                        }
                        else
                        {
                            SongLoadScreen.Dispose(); // 演奏中断時に曲ロード画面のアセットを解放
                            _scene = Scene.SongSelect;
                            SongSelectScene.ResumeMenuBgm();
                            DiscordRpc.SetSongSelect();
                        }
                    }
                    else if (Enso.EndSequenceDone)
                    {
                        Enso.Stop();

                        if (_isDanMode)
                        {
                            // 💡 段位モード:1曲終わるごとに、その曲の成績をExam.SongResultとして積んでおく
                            _danSongResults.Add(CaptureDanSongResult());
                        }

                        if (_isDanMode && _danSongIndex + 1 < _danSongPaths.Count)
                        {
                            // 💡 段位モード:まだ次の曲が残っていれば、結果画面を挟まず続けて演奏する
                            //    (SongLoadScreenは経由していないためDispose()は不要)
                            _danSongIndex++;
                            StartDanSong();
                        }
                        else if (_isDanMode)
                        {
                            // 💡 段位モード:全曲(1~3曲)演奏し終えたので、専用のDanResultScene(dan/Result.cs)へ
                            FinishDanAndGoToResult();
                        }
                        else
                        {
                            SongLoadScreen.Dispose(); // 演奏終了時に曲ロード画面のアセットを解放
                            ResultScene.Finish();
                            _scene = Scene.Result;
                            DiscordRpc.SetResult(_rpcSongTitle, ResultScene._resultScore);
                        }
                    }

                    Raylib.BeginTextureMode(_virtualScreen);
                    Raylib.ClearBackground(Color.Black);
                    Enso.Draw();
                    SettingsPanel.Draw();
                    DrawScreenBars();
                    DrawDebugOverlay();
                    Raylib.EndTextureMode();
                }
                else if (_scene == Scene.Result)
                {
                    if (SettingsPanel.IsAnyDonPressed() ||
                        Raylib.IsKeyPressed(KeyboardKey.Enter) ||
                        Raylib.IsKeyPressed(KeyboardKey.Escape))
                    {
                        if (ResultScene._musicResultLoaded && ResultScene._musicResult != null)
                        {
                            ResultScene._musicResult.Stop();
                            ResultScene._musicResult.Dispose();
                            ResultScene._musicResult = null!;
                            ResultScene._musicResultLoaded = false;
                        }

                        _scene = Scene.SongSelect;
                        SongSelectScene.ResumeMenuBgm();
                        DiscordRpc.SetSongSelect();
                    }

                    Raylib.BeginTextureMode(_virtualScreen);
                    Raylib.ClearBackground(Color.Black);
                    ResultScene.Draw();
                    SettingsPanel.Draw();
                    DrawScreenBars();
                    DrawDebugOverlay();
                    Raylib.EndTextureMode();
                }
                else // _scene == Scene.DanResult
                {
                    DanResultScene.Update(dt);

                    if (DanResultScene.Confirmed)
                    {
                        DanResultScene.ResetConfirm();

                        if (ResultScene._musicResultLoaded && ResultScene._musicResult != null)
                        {
                            ResultScene._musicResult.Stop();
                            ResultScene._musicResult.Dispose();
                            ResultScene._musicResult = null!;
                            ResultScene._musicResultLoaded = false;
                        }

                        // 💡 段位モードは全曲(1~3曲)の演奏後、専用リザルト確認を経て段位選択画面へ戻る
                        _isDanMode = false;
                        Enso.DanMode = false;
                        DanSelectScene.Enter();
                        _scene = Scene.DanSelect;
                        DiscordRpc.SetSongSelect();
                    }

                    Raylib.BeginTextureMode(_virtualScreen);
                    Raylib.ClearBackground(Color.Black);
                    DanResultScene.Draw();
                    SettingsPanel.Draw();
                    DrawScreenBars();
                    DrawDebugOverlay();
                    Raylib.EndTextureMode();
                }

                // 仮想スクリーン(1920x1080)の描画結果を実ウィンドウへ拡大縮小して転写する
                PresentVirtualScreen();
            }
            catch (Exception ex)
            {
                AudioEngine.StopAll();
                Error.Show(ex);
                _scene = Scene.Error;

                try { Raylib.EndTextureMode(); } catch { }
                try { Raylib.EndDrawing(); } catch { }
            }
        }

        SongLoadScreen.Dispose();
        DiffSelectScene.Unload();

        Raylib.UnloadRenderTexture(_virtualScreen);

        ResultScene.Shutdown();

        if (ResultScene._musicResultLoaded && ResultScene._musicResult != null)
        {
            ResultScene._musicResult.Stop();
            ResultScene._musicResult.Dispose();
            ResultScene._musicResultLoaded = false;
        }

        DrumInput.Stop();
        DrumInput2.Stop();
        SongSelectScene.Unload();
        AudioEngine.Shutdown();
        NotesTexture.Unload();
        G.UnloadFonts();
        DiscordRpc.Shutdown();

        Raylib.CloseWindow();
    }

    /// <summary>
    /// 段位道場モード:_danSongIndex番目の曲を読み込み、SongLoadScreen(曲読み込み演出)を経由せず
    /// 即座に演奏を開始する(Scene.Playingへ直接遷移)。
    /// </summary>
    static void StartDanSong()
    {
        string tjaPath = _danSongPaths[_danSongIndex];
        var course = _danSongCourses[_danSongIndex];

        // 💡 SongLoadScreen.csは使わず、TJAを直接読み込む。
        //    Course(難易度譜面)の選択方法はTJA.csの実装依存のため、
        //    もしTJA.Load(string)しか無い場合はここをTJA.Load(tjaPath); TJA.SetCourse(course);等に調整してください。
        TJA.Load(tjaPath, course);

        Enso.DanMode = true;
        Enso.SetP2Active(false);
        Enso.DanFirstSong = (_danSongIndex == 0);
        Enso.DanTotalNotes = _danTotalNotes;

        SongSelectScene.PauseMenuBgm();
        Enso.Init();
        Enso.Start();

        _ensoStarted = true;
        _scene = Scene.Playing;

        _rpcSongTitle = _danSongTitles.Count > _danSongIndex ? _danSongTitles[_danSongIndex] : _danTitle;
        _currentTjaPath = tjaPath;
        DiscordRpc.SetDanMode(_danTitle, _danSongIndex, _danSongPaths.Count);
    }



    /// <summary>
    /// 段位道場モード:1曲分の演奏結果をExam.SongResultとして取り出す。
    /// Great/Good/Miss/Roll/MaxCombo/Scoreは通常リザルトと同じくEnsoから拾う。
    /// Hitに対応する専用の値がEnso側に無いため、暫定でGreat+Good(=空振り/不可を除いた総打数)を使っている。
    /// dan.jsonのconditions側で"hit"を使う予定がある場合は、Enso側の実際の値に差し替えてください。
    /// </summary>
    static Exam.SongResult CaptureDanSongResult()
    {
        // 💡 こちらもFinishSongAndGoToResultと同じ理由でEnsoの実プロパティを直接参照する形に統一
        int great = Enso.Perfect;
        int good = Enso.Good;

        return new Exam.SongResult
        {
            Great = great,
            Good = good,
            Miss = Enso.Bad + Enso.Miss, // 不可+ミスの合計(元のロジックがBadとMissを区別せず合算していた挙動を踏襲)
            Roll = Enso.RollThisSong,
            Hit = great + good, // 💡 暫定値。専用のHitカウントがあれば差し替え
            Score = Enso.Score,
            MaxCombo = Enso.MaxCombo,
        };
    }

    /// <summary>
    /// 段位道場モード:全曲(1~3曲)の演奏後に呼ぶ。専用のDanResultScene(dan/Result.cs)へ遷移する。
    /// </summary>
    static void FinishDanAndGoToResult()
    {
        ResultScene.isClear = Gauge.IsClear;
        ResultScene._resultGaugePercent = (int)Math.Round(Gauge.Value);

        // 💡 既存のリザルトBGMが読み込まれている場合は破棄してから新しく読み込み(通常リザルトと共通のBGMを流用)
        if (ResultScene._musicResultLoaded && ResultScene._musicResult != null)
        {
            ResultScene._musicResult.Stop();
            ResultScene._musicResult.Dispose();
            ResultScene._musicResult = null!;
            ResultScene._musicResultLoaded = false;
        }

        string resultBgmPath = Path.Combine(SongSelectScene.SkinRoot, "2.sound", "BGM", "Result.ogg");

        if (File.Exists(resultBgmPath))
        {
            ResultScene._musicResult = MusicTrack.Load(resultBgmPath);
            ResultScene._musicResultLoaded = ResultScene._musicResult != null && ResultScene._musicResult.Loaded;

            if (ResultScene._musicResultLoaded)
            {
                ResultScene._musicResult.Volume = SettingsPanel.EffectiveBgmVolume;
                ResultScene._musicResult.Looping = true;
                ResultScene._musicResult.Play();
            }
        }

        DanResultScene.Enter(
            _danTitle,
            _danSongTitles,
            _danSongResults,
            _danConditions,
            ResultScene.isClear,
            ResultScene._resultGaugePercent,
            _danGauge
        );

        _scene = Scene.DanResult;
        DiscordRpc.SetResult(_danTitle, ResultScene._resultScore);
    }

    static int GetEnsoValue(params string[] names)
    {
        var type = typeof(Enso);

        foreach (var name in names)
        {
            try
            {
                var prop = type.GetProperty(
                    name,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.IgnoreCase
                );

                if (prop != null) return Convert.ToInt32(prop.GetValue(null));

                var field = type.GetField(
                    name,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.IgnoreCase
                );

                if (field != null) return Convert.ToInt32(field.GetValue(null));
            }
            catch { }
        }

        return 0;
    }

    static int GetStaticIntValue(Type type, params string[] names)
    {
        foreach (var name in names)
        {
            try
            {
                var prop = type.GetProperty(
                    name,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.IgnoreCase
                );

                if (prop != null) return Convert.ToInt32(prop.GetValue(null));

                var field = type.GetField(
                    name,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.IgnoreCase
                );

                if (field != null) return Convert.ToInt32(field.GetValue(null));
            }
            catch { }
        }

        return 0;
    }

    static bool? GetEnsoBool(params string[] names)
    {
        var type = typeof(Enso);

        foreach (var name in names)
        {
            try
            {
                var prop = type.GetProperty(
                    name,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.IgnoreCase
                );

                if (prop != null) return Convert.ToBoolean(prop.GetValue(null));

                var field = type.GetField(
                    name,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.IgnoreCase
                );

                if (field != null) return Convert.ToBoolean(field.GetValue(null));
            }
            catch { }
        }

        return null;
    }


    // OpenGLテクスチャID監視用デバッグ変数
    private static uint _currentTextureId = 0;
    private static uint _lastMaxTextureId = 0;
    private static uint _textureIdGrowthPerSec = 0;
    private static uint _textureIdLastSec = 0;
    private static float _debugTimerSec = 0f;

    /// <summary>
    /// ダミーテクスチャを発行・即解放することで、現在のOpenGLテクスチャIDハンドルを取得・更新する
    /// </summary>
    private static void UpdateOpenGLTextureDebug(float dt)
    {
        // 1x1ダミーテクスチャを発行して最新のGLテクスチャIDを取得
        Image dummyImg = Raylib.GenImageColor(1, 1, Color.White);
        Texture2D dummyTex = Raylib.LoadTextureFromImage(dummyImg);

        _currentTextureId = dummyTex.Id;

        Raylib.UnloadTexture(dummyTex);
        Raylib.UnloadImage(dummyImg);

        // 毎秒あたりの増加数を計算
        _debugTimerSec += dt;

        if (_debugTimerSec >= 1.0f)
        {
            _textureIdGrowthPerSec = _currentTextureId >= _textureIdLastSec
                ? _currentTextureId - _textureIdLastSec
                : 0;

            _textureIdLastSec = _currentTextureId;
            _debugTimerSec = 0f;
        }

        // 急激にIDが増加した場合はコンソールへ警告ログを出力
        if (_currentTextureId > _lastMaxTextureId + 20)
        {
            Console.WriteLine(
                $"[VRAM GL LEAK DETECTED] 最新GLテクスチャID: {_currentTextureId} " +
                $"(前回比 +{_currentTextureId - _lastMaxTextureId})"
            );

            _lastMaxTextureId = _currentTextureId;
        }
    }

    /// <summary>
    /// 現在ロードされているテクスチャ群の理論値VRAM使用量（バイト）を計算
    /// </summary>
    private static long GetTotalVramBytes()
    {
        if (!_vramDetailInitialized)
            RefreshVramDetailSnapshot();

        return _vramDetailTotalBytes;
    }

    /// <summary>
    /// 8pxの黒色縁取り付きでテキストを描画する（旧デバッグ表示用・残置）
    /// </summary>
    private static void DrawTextWithOutline8px(Font font, string text, Vector2 position, float fontSize, Color textColor, Color outlineColor)
    {
        int radius = 8;

        for (int dx = -radius; dx <= radius; dx += 4)
        {
            for (int dy = -radius; dy <= radius; dy += 4)
            {
                if (dx == 0 && dy == 0) continue;

                Raylib.DrawTextEx(font, text, position + new Vector2(dx, dy), fontSize, 2, outlineColor);
            }
        }

        Raylib.DrawTextEx(font, text, position, fontSize, 2, textColor);
    }

    /// <summary>
    /// 画面左上にVRAM合計と詳細パネルを表示する
    /// </summary>
    private static void DrawDebugOverlay()
    {
        // 完全非表示モード: 何も描画しない(F8で概要のみ→詳細→非表示…と巡回)
        if (_vramDetailMode == 4)
            return;

        if (!_vramDetailInitialized)
            RefreshVramDetailSnapshot();

        int sw = VirtualWidth;

        float uiScale = Math.Clamp(sw / 1920f, 0.8f, 1.25f);
        float fontSize = 22f * uiScale;
        float lineHeight = fontSize + 6f;

        string modeName = _vramDetailMode switch
        {
            0 => "OFF",
            1 => "テクスチャ",
            2 => "所有者",
            3 => "種別",
            _ => "?"
        };

        string line1 =
            $"VRAM({_scene}) " +
            $"合計:{_vramDetailTotalBytes / 1048576.0:0.00}MB " +
            $"参照:{_vramDetailReferenceCount} " +
            $"ユニーク:{_vramDetailUniqueCount} " +
            $"GL ID:{_currentTextureId}(+{_textureIdGrowthPerSec}/s)";

        string line2 =
            $"[F8]詳細:{modeName} (F8連打で非表示) " +
            $"[PageUp/Down]スクロール " +
            $"scroll={_vramDetailScroll}";

        DrawTextWithOutline(
            G.Font,
            line1,
            new Vector2(10f, 10f),
            fontSize,
            Color.White,
            Color.Black,
            4
        );

        DrawTextWithOutline(
            G.Font,
            line2,
            new Vector2(10f, 10f + lineHeight),
            fontSize * 0.85f,
            new Color((byte)255, (byte)255, (byte)0, (byte)255),
            Color.Black,
            3
        );

        if (_vramDetailMode >= 1 && _vramDetailMode <= 3)
            DrawVramDetailPanel(uiScale);
    }

    private static void DrawVramDetailPanel(float uiScale)
    {
        int sw = VirtualWidth;
        int sh = VirtualHeight;

        float fontSize = 18f * uiScale;
        float lineHeight = fontSize + 5f;

        float panelX = 10f;
        float panelY = 10f + (22f * uiScale + 6f) * 2 + 6f;
        float panelW = Math.Min(sw - 20f, 1300f * uiScale);

        int headerLines = 2;

        int maxLines = Math.Max(
            3,
            (int)((sh - panelY - 24f) / lineHeight) - headerLines
        );

        _vramDetailMaxLines = maxLines;

        int itemCount = _vramDetailMode switch
        {
            1 => _vramDetailItems.Count,
            2 => _vramDetailOwnerList.Count,
            3 => _vramDetailTypeList.Count,
            _ => 0
        };

        int maxScroll = Math.Max(0, itemCount - maxLines);

        if (_vramDetailScroll > maxScroll)
            _vramDetailScroll = maxScroll;

        if (_vramDetailScroll < 0)
            _vramDetailScroll = 0;

        var lines = new List<string>(maxLines + headerLines);

        if (_vramDetailMode == 1)
        {
            int showCount = Math.Min(maxLines, Math.Max(0, itemCount - _vramDetailScroll));

            lines.Add($"ユニークテクスチャ上位 (全{itemCount}件 / 表示:{showCount}件)");
            lines.Add("No. ID   サイズ      フォーマット        Mip  推定VRAM   参照元");

            int end = Math.Min(_vramDetailScroll + maxLines, _vramDetailItems.Count);

            for (int i = _vramDetailScroll; i < end; i++)
            {
                var it = _vramDetailItems[i];

                lines.Add(
                    $"{i + 1,3} " +
                    $"ID{it.Id,4} " +
                    $"{it.Width,4}x{it.Height,-4} " +
                    $"{FormatShort(it.Format),-18} " +
                    $"{it.Mipmaps,3} " +
                    $"{it.Bytes / 1048576.0,8:0.00}MB " +
                    $"{JoinReferences(it.References, 2)}"
                );
            }
        }
        else if (_vramDetailMode == 2)
        {
            int showCount = Math.Min(maxLines, Math.Max(0, itemCount - _vramDetailScroll));

            lines.Add($"所有者(フィールド/配列)上位 (全{itemCount}件 / 表示:{showCount}件)");
            lines.Add("No.  推定VRAM   枚数  所有者");

            int end = Math.Min(_vramDetailScroll + maxLines, _vramDetailOwnerList.Count);

            for (int i = _vramDetailScroll; i < end; i++)
            {
                var owner = _vramDetailOwnerList[i];

                lines.Add(
                    $"{i + 1,3} " +
                    $"{owner.Bytes / 1048576.0,8:0.00}MB " +
                    $"{owner.TextureCount,4}  " +
                    $"{owner.Owner}"
                );
            }
        }
        else if (_vramDetailMode == 3)
        {
            int showCount = Math.Min(maxLines, Math.Max(0, itemCount - _vramDetailScroll));

            lines.Add($"種別(クラス)上位 (全{itemCount}件 / 表示:{showCount}件)");
            lines.Add("No.  推定VRAM   種別");

            int end = Math.Min(_vramDetailScroll + maxLines, _vramDetailTypeList.Count);

            for (int i = _vramDetailScroll; i < end; i++)
            {
                var kv = _vramDetailTypeList[i];

                lines.Add(
                    $"{i + 1,3} " +
                    $"{kv.Value / 1048576.0,8:0.00}MB " +
                    $"{kv.Key}"
                );
            }
        }

        float panelH = lineHeight * lines.Count + 12f;

        Raylib.DrawRectangle(
            (int)panelX,
            (int)panelY,
            (int)panelW,
            (int)panelH,
            new Color((byte)0, (byte)0, (byte)0, (byte)175)
        );

        Raylib.DrawRectangleLines(
            (int)panelX,
            (int)panelY,
            (int)panelW,
            (int)panelH,
            new Color((byte)255, (byte)255, (byte)255, (byte)110)
        );

        float y = panelY + 6f;

        foreach (var line in lines)
        {
            DrawTextWithOutline(
                G.Font,
                line,
                new Vector2(panelX + 8f, y),
                fontSize,
                Color.White,
                Color.Black,
                2
            );

            y += lineHeight;
        }
    }

    private static void DrawTextWithOutline(
        Font font,
        string text,
        Vector2 position,
        float fontSize,
        Color textColor,
        Color outlineColor,
        int outline)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (outline > 0)
        {
            Raylib.DrawTextEx(font, text, position + new Vector2(-outline, 0f), fontSize, 2f, outlineColor);
            Raylib.DrawTextEx(font, text, position + new Vector2(outline, 0f), fontSize, 2f, outlineColor);
            Raylib.DrawTextEx(font, text, position + new Vector2(0f, -outline), fontSize, 2f, outlineColor);
            Raylib.DrawTextEx(font, text, position + new Vector2(0f, outline), fontSize, 2f, outlineColor);
        }

        Raylib.DrawTextEx(font, text, position, fontSize, 2f, textColor);
    }

    public static void UpdateMenuBgmVolume()
    {
        float bgmVol = SettingsPanel.EffectiveBgmVolume;
        float seVol = SettingsPanel.EffectiveSeVolume;

        // タイトル画面の全トラック
        if (TitleScene._musicTitle != null && TitleScene._musicTitle.Loaded)
            TitleScene._musicTitle.Volume = bgmVol;
        if (TitleScene._seStart != null && TitleScene._seStart.Loaded)
            TitleScene._seStart.Volume = bgmVol;
        if (TitleScene._musicBanaClear != null && TitleScene._musicBanaClear.Loaded)
            TitleScene._musicBanaClear.Volume = bgmVol;
        if (TitleScene._musicSelectPlaySide != null && TitleScene._musicSelectPlaySide.Loaded)
            TitleScene._musicSelectPlaySide.Volume = bgmVol;
        if (TitleScene._musicSelectMode != null && TitleScene._musicSelectMode.Loaded)
            TitleScene._musicSelectMode.Volume = bgmVol;
        if (TitleScene._musicEnsoGame != null && TitleScene._musicEnsoGame.Loaded)
            TitleScene._musicEnsoGame.Volume = bgmVol;

        // 選曲画面のBGM・プレビュー・SE
        if (SongSelectScene._musicSongSelectStart != null && SongSelectScene._musicSongSelectStart.Loaded)
            SongSelectScene._musicSongSelectStart.Volume = bgmVol;
        if (SongSelectScene._musicSongSelectLoop != null && SongSelectScene._musicSongSelectLoop.Loaded)
            SongSelectScene._musicSongSelectLoop.Volume = bgmVol;
        if (SongSelectScene._musicPreview != null && SongSelectScene._musicPreview.Loaded)
            SongSelectScene._musicPreview.Volume = bgmVol;
        if (SongSelectScene._sndKa != null) SongSelectScene._sndKa.Volume = seVol;
        if (SongSelectScene._sndDon != null) SongSelectScene._sndDon.Volume = seVol;

        // 難易度選択画面のプレビュー曲
        if (DiffSelectScene._musicPreview != null && DiffSelectScene._musicPreview.Loaded)
            DiffSelectScene._musicPreview.Volume = bgmVol;

        // リザルト画面のBGM・SE
        if (ResultScene._musicResult != null && ResultScene._musicResult.Loaded)
            ResultScene._musicResult.Volume = bgmVol;
        if (ResultScene.scoreDonSound != null) ResultScene.scoreDonSound.Volume = seVol;
        if (ResultScene.scoreRankSound != null) ResultScene.scoreRankSound.Volume = seVol;
        if (ResultScene.crownClearSound != null) ResultScene.crownClearSound.Volume = seVol;
        if (ResultScene.crownFullComboSound != null) ResultScene.crownFullComboSound.Volume = seVol;
        if (ResultScene.crownDondaSound != null) ResultScene.crownDondaSound.Volume = seVol;
        if (ResultScene.gladSound1 != null) ResultScene.gladSound1.Volume = seVol;
        if (ResultScene.gladSound2 != null) ResultScene.gladSound2.Volume = seVol;
        if (ResultScene.sadSound != null) ResultScene.sadSound.Volume = seVol;
    }
}
using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

/// <summary>
/// 起動直後に表示する演出一式(業務用筐体風の起動シーケンス)。
///   フェーズ1: DONGLE/BACKUP等のシステムチェック一覧を上から順に表示
///   フェーズ2: 「LOADING.」→「LOADING..」→「LOADING...」のループ表示(実際の先読みと並行)
///   フェーズ3: 国内専用販売に関するNOTICE(赤字タイトル)表示
/// Program.Main() の中で、他のScene.Init()より前に BootScreen.Run(songsRoot) を1回呼ぶだけで良い。
/// </summary>
public static class BootScreen
{
    // ==================================================================
    // 表示内容(ここを書き換えれば項目や文言を調整できる)
    // ==================================================================
    private static readonly (string label, string status)[] SystemChecks =
        {
        ("DONGLE", "正常"),     // DONGLE -> ジャングル
        ("BACKUP", "正常"),     // BACKUP -> FUCKUP（大失敗）
        ("US I/O", "正常"),     // US I/O -> USBIO / 生体汚染風
        ("CREDIT", "正常"),     // CREDIT -> DEBTIN（借金突入）
        ("NETWORK","正常"),    // NETWORK -> DEADWORK（回線死亡）
        ("UPDATER","正常"),   // UPDATER -> DESTROYER（破壊者）
        ("CARD READER-WRITER", "正常"), // CARD READER... -> 魂の読み書き
        ("CODEREADER", "正常"), // CODEREADER -> 地獄読み
    };



    private const string DeviceCheckTitle = "DEVICE CHECK";
    private const string DeviceCheckSerial = "S12227-1-NA-MPR0-E07";

    private static readonly string[] NoticeLines =
    {
        "THIS GAME IS FOR USE",
        "EXCLUSIVELY IN JAPAN.",
        "THE SALE,EXPORT,USE",
        "OR OPERATION OF THIS",
        "GAME OUTSIDE OF JAPAN",
        "MAY BE A VIOLATION OF",
        "INTERNATIONAL COPYRIGHT,",
        "TRADEMARK AND/OR OTHER",
        "RELATED LAWS SUBJECTING",
        "THE VIOLATOR TO LEGAL",
        "PENALTIES.",
    }; // ※現在はLumen/notice/text.pngの画像表示に切り替えたため未使用(将来テキスト描画に戻す場合用に残置)

    // ==================================================================
    // タイミング調整用パラメータ
    // ==================================================================
    private const float PostCheckDelay = 0.25f;   // 全項目表示後、LOADINGへ移るまでの待ち時間(秒)
    private const float LoadingMinDuration = 0.5f; // LOADINGループの最低表示時間(秒)
    private const float LoadingDotInterval = 0.5f;  // LOADINGの「.」が増える間隔(秒)
    private const float NoticeMinDuration = 0.5f;   // NOTICE画面を最低限表示しておく時間(秒)

    // ==================================================================
    // レイアウト調整用パラメータ(左端Xは全項目で共通=均一)
    // ==================================================================
    private const float CheckLabelX = 500f;      // ラベル(DONGLE等)の左端X座標。全行共通。
    private const float CheckStatusLeftX = 1000f; // ★【変更】ステータスの左端X座標(お好みの位置に数値調整してください)
    private const float CheckStartY = 213f;
    private const float CheckLineGap = 38f;
    private const int CheckFontSize = 36;

    private const int DeviceCheckTitleFontSize = 36;   // 「DEVICE CHECK」の文字サイズ
    private const float DeviceCheckTitleX = 760f;      // 「DEVICE CHECK」のX座標(-1で画面中央に自動配置)
    private const float DeviceCheckTitleY = 100f;      // 「DEVICE CHECK」のY座標

    private const int DeviceCheckSerialFontSize = 36;  // シリアル文字列の文字サイズ
    private const float DeviceCheckSerialX = 680f;     // シリアル文字列の左端X座標
    private const float DeviceCheckSerialY = 782f;     // シリアル文字列のY座標

    // LOADING表示の位置・大きさ(ここを書き換えるだけで調整できる)
    //   画面中央からのズレ(オフセット)。0で画面中央。
    //   X: マイナスで左、プラスで右。 Y: マイナスで上、プラスで下。
    private const float LoadingX = -750f;        // 中央からのXオフセット
    private const float LoadingY = 400f;         // 中央からのYオフセット
    private const int LoadingFontSize = 36;      // 文字サイズ

    // ==================================================================
    // 表示専用フォント(font/SubFont.otf)
    //     G.InitializeFonts()はこのBootScreenより後に呼ばれるため、Gのフォントには
    //     頼らずここで独自に必要な文字だけを読み込む(TextureSongs.csと同じ方針)。
    // ==================================================================
    private static Font _font;
    private static bool _fontLoaded;

    private static void EnsureFontLoaded()
    {
        if (_fontLoaded) return;
        _fontLoaded = true;

        var set = new HashSet<int>();
        for (int i = 32; i < 127; i++) set.Add(i); // ASCII基本文字

        foreach (var (label, status) in SystemChecks)
        {
            AddCodepoints(set, label);
            AddCodepoints(set, status);
        }

        AddCodepoints(set, DeviceCheckTitle);
        AddCodepoints(set, DeviceCheckSerial);
        AddCodepoints(set, "LOADING.");

        var codepoints = new int[set.Count];
        set.CopyTo(codepoints);
        Array.Sort(codepoints);

        _font = System.IO.File.Exists("font/SubFont.otf")
            ? Raylib.LoadFontEx("font/SubFont.otf", 64, codepoints, codepoints.Length)
            : Raylib.GetFontDefault();

        if (_font.Texture.Id != 0)
            Raylib.SetTextureFilter(_font.Texture, TextureFilter.Bilinear);
    }

    private static void AddCodepoints(HashSet<int> set, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        int idx = 0;
        while (idx < text.Length)
        {
            int cp = char.ConvertToUtf32(text, idx);
            set.Add(cp);
            idx += char.IsSurrogatePair(text, idx) ? 2 : 1;
        }
    }

    /// <summary>
    /// 起動シーケンスをまとめて実行する。songsRootはTextureSongs.PreloadAllにそのまま渡す。
    /// </summary>
    public static void Run(string songsRoot)
    {
        EnsureFontLoaded();
        EnsureNoticeTextureLoaded();
        RunSystemCheck();
        RunLoadingWithPreload(songsRoot);
        RunNotice();
    }

    // ==================================================================
    // フェーズ1: システムチェック一覧
    // ==================================================================
    private static void RunSystemCheck()
    {
        // 全項目を一気に表示した状態でしばらく待つ
        float waitTimer = 0f;
        while (waitTimer < PostCheckDelay)
        {
            waitTimer += Raylib.GetFrameTime();
            DrawSystemCheck();
        }
    }

    private static void DrawSystemCheck()
    {
        Program.BeginVirtualFrame();
        Raylib.ClearBackground(Color.Black);

        var font = _font;
        int sw = Program.VirtualWidth;

        // 「DEVICE CHECK」タイトル(DeviceCheckTitleXが-1なら画面中央に自動配置)
        var titleSize = Raylib.MeasureTextEx(font, DeviceCheckTitle, DeviceCheckTitleFontSize, 1f);
        float titleX = DeviceCheckTitleX == -1f ? sw / 2f - titleSize.X / 2f : DeviceCheckTitleX;
        Raylib.DrawTextEx(
            font,
            DeviceCheckTitle,
            new Vector2(titleX, DeviceCheckTitleY),
            DeviceCheckTitleFontSize,
            1f,
            Color.White
        );

        for (int i = 0; i < SystemChecks.Length; i++)
        {
            var (label, status) = SystemChecks[i];
            float y = CheckStartY + CheckLineGap * i;

            // ラベルは左端Xを揃えて描画(左揃え・全項目共通のX座標)
            Raylib.DrawTextEx(font, label, new Vector2(CheckLabelX, y), CheckFontSize, 1f, Color.White);

            // 【変更前】ステータス("正常")は緑色・右端Xで揃えて描画
            // float statusWidth = Raylib.MeasureTextEx(font, status, CheckFontSize, 1f).X;
            // Raylib.DrawTextEx(font, status, new Vector2(CheckStatusRightX - statusWidth, y), CheckFontSize, 1f, Color.Green);

            // ★【変更後】ステータスを一定のX座標(CheckStatusLeftX)から左揃えで描画
            Raylib.DrawTextEx(font, status, new Vector2(CheckStatusLeftX, y), CheckFontSize, 1f, Color.Green);
        }

        // 画面下部にシリアル番号風の文字列を表示
        Raylib.DrawTextEx(
            font,
            DeviceCheckSerial,
            new Vector2(DeviceCheckSerialX, DeviceCheckSerialY),
            DeviceCheckSerialFontSize,
            1f,
            Color.White
        );

        Program.EndVirtualFrame();
        Program.PresentVirtualScreen();
    }

    // ==================================================================
    // フェーズ2: LOADING表示(実際のテクスチャ/曲先読みと並行して進める)
    // ==================================================================
    private static void RunLoadingWithPreload(string songsRoot)
    {
        TitleScene.PreloadAnimations();
        Task preloadTask = SongSelectScene.PreloadSongCacheAsync();

        float elapsed = 0f;
        float dotTimer = 0f;
        int dotCount = 1;

        while (true)
        {
            float dt = Raylib.GetFrameTime();
            elapsed += dt;
            dotTimer += dt;

            if (dotTimer >= LoadingDotInterval)
            {
                dotTimer -= LoadingDotInterval;
                dotCount = dotCount % 3 + 1; // 1→2→3→1…とループ
            }

            DrawLoading(dotCount);

            if (elapsed >= LoadingMinDuration && preloadTask.IsCompleted)
                break;
        }
    }

    private static void DrawLoading(int dotCount)
    {
        Program.BeginVirtualFrame();
        Raylib.ClearBackground(Color.Black);

        var font = _font;
        const int fontSize = LoadingFontSize;

        string text = "LOADING" + new string('.', dotCount);

        int sw = Program.VirtualWidth;
        int sh = Program.VirtualHeight;
        var size = Raylib.MeasureTextEx(font, text, fontSize, 1f);

        // 画面中央を基準に、LoadingX/Yぶんだけズラした位置に文字の中心が来るようにする
        float centerX = sw / 2f + LoadingX;
        float centerY = sh / 2f + LoadingY;
        float x = centerX - size.X / 2f;
        float y = centerY - size.Y / 2f;

        Raylib.DrawTextEx(
            font,
            text,
            new Vector2(x, y),
            fontSize,
            1f,
            Color.White
        );

        Program.EndVirtualFrame();
        Program.PresentVirtualScreen();
    }

    // ==================================================================
    // フェーズ3: NOTICE画面(国内専用販売の警告文)
    // ==================================================================
    private static void RunNotice()
    {
        float elapsed = 0f;

        // フェーズA: バーが満タン(progress>=1)になるまで通常通り進行
        while (true)
        {
            elapsed += Raylib.GetFrameTime();

            // バーの進捗(0→1)。NoticeMinDurationの時間で0%→100%になる。
            float progress = NoticeMinDuration > 0f ? elapsed / NoticeMinDuration : 1f;

            if (progress >= 1f)
            {
                DrawNotice(1f, 0f);
                break;
            }

            DrawNotice(progress, 0f);
        }

        // フェーズB: 満タンになったら画面全体を白くフェードさせてからデモ画面へ遷移する
        float whiteoutElapsed = 0f;
        while (whiteoutElapsed < NoticeWhiteoutDuration)
        {
            whiteoutElapsed += Raylib.GetFrameTime();
            float whiteAlpha = Math.Clamp(whiteoutElapsed / NoticeWhiteoutDuration, 0f, 1f);
            DrawNotice(1f, whiteAlpha);
        }
    }

    private const float NoticeWhiteoutDuration = 0.5f; // 満タン後、画面が真っ白になるまでの時間(秒)

    // NOTICE画像の位置・大きさ(ここを書き換えるだけで調整できる)
    //   画面中央からのズレ(オフセット)。0で画面中央。X: マイナスで左、プラスで右。 Y: マイナスで上、プラスで下。
    private const float NoticeImageX = 0f;
    private const float NoticeImageY = -50f;
    private const float NoticeImageScale = 1f; // 画像の拡大率(1で原寸)

    // NOTICEバー(barbase.png / bar.png)の位置・大きさ
    //   画面中央からのズレ(オフセット)。文字画像の下に出したいのでYはプラス(下)にしておく。
    private const float NoticeBarX = 0f;         // 中央からのXオフセット
    private const float NoticeBarY = 450f;       // 中央からのYオフセット(文字の下に来るよう調整)
    private const float NoticeBarWidth = 926f;   // バーの描画幅
    private const float NoticeBarHeight = 38f;   // バーの描画高さ

    private static Texture2D _noticeTexture;
    private static bool _noticeTextureLoaded;

    private static Texture2D _noticeBarBaseTexture; // barbase.png(土台。常に満タン表示)
    private static Texture2D _noticeBarFillTexture; // bar.png(進捗ぶんだけ左から表示)

    private static void EnsureNoticeTextureLoaded()
    {
        if (_noticeTextureLoaded) return;
        _noticeTextureLoaded = true;

        if (System.IO.File.Exists("Lumen/notice/text.png"))
        {
            _noticeTexture = Raylib.LoadTexture("Lumen/notice/text.png");
            if (_noticeTexture.Id != 0)
                Raylib.SetTextureFilter(_noticeTexture, TextureFilter.Bilinear);
        }

        if (System.IO.File.Exists("Lumen/notice/barbase.png"))
        {
            _noticeBarBaseTexture = Raylib.LoadTexture("Lumen/notice/barbase.png");
            if (_noticeBarBaseTexture.Id != 0)
                Raylib.SetTextureFilter(_noticeBarBaseTexture, TextureFilter.Bilinear);
        }

        if (System.IO.File.Exists("Lumen/notice/bar.png"))
        {
            _noticeBarFillTexture = Raylib.LoadTexture("Lumen/notice/bar.png");
            if (_noticeBarFillTexture.Id != 0)
                Raylib.SetTextureFilter(_noticeBarFillTexture, TextureFilter.Bilinear);
        }
    }

    private static void DrawNotice(float progress, float whiteAlpha)
    {
        Program.BeginVirtualFrame();
        Raylib.ClearBackground(Color.Black);

        int sw = Program.VirtualWidth;
        int sh = Program.VirtualHeight;

        if (_noticeTexture.Id != 0)
        {
            float drawW = _noticeTexture.Width * NoticeImageScale;
            float drawH = _noticeTexture.Height * NoticeImageScale;

            // 画面中央を基準に、NoticeImageX/Yぶんだけズラした位置に画像の中心が来るようにする
            float centerX = sw / 2f + NoticeImageX;
            float centerY = sh / 2f + NoticeImageY;

            var dest = new Rectangle(centerX - drawW / 2f, centerY - drawH / 2f, drawW, drawH);
            var src = new Rectangle(0, 0, _noticeTexture.Width, _noticeTexture.Height);

            Raylib.DrawTexturePro(_noticeTexture, src, dest, Vector2.Zero, 0f, Color.White);
        }
        else
        {
            // 💡 text.pngが見つからない場合のフォールバック(黒画面のままだと状況が分からないため)
            const string fallbackText = "NOTICE";
            var size = Raylib.MeasureTextEx(_font, fallbackText, 48, 1f);
            float centerX = sw / 2f + NoticeImageX;
            float centerY = sh / 2f + NoticeImageY;
            Raylib.DrawTextEx(_font, fallbackText, new Vector2(centerX - size.X / 2f, centerY - size.Y / 2f), 48, 1f, Color.Red);
        }

        // 文字の下にバー(barbase.pngを土台として全体表示 → bar.pngを進捗ぶんだけ左から重ねる)
        {
            float barCenterX = sw / 2f + NoticeBarX;
            float barCenterY = sh / 2f + NoticeBarY;
            var barDest = new Rectangle(
                barCenterX - NoticeBarWidth / 2f,
                barCenterY - NoticeBarHeight / 2f,
                NoticeBarWidth,
                NoticeBarHeight
            );

            if (_noticeBarBaseTexture.Id != 0)
            {
                var baseSrc = new Rectangle(0, 0, _noticeBarBaseTexture.Width, _noticeBarBaseTexture.Height);
                Raylib.DrawTexturePro(_noticeBarBaseTexture, baseSrc, barDest, Vector2.Zero, 0f, Color.White);
            }
            else
            {
                // 💡 barbase.pngが見つからない場合のフォールバック(進捗が見えないと分かりにくいため)
                Raylib.DrawRectangleRec(barDest, new Color((byte)60, (byte)60, (byte)60, (byte)255));
                Raylib.DrawRectangleLinesEx(barDest, 2f, new Color((byte)255, (byte)255, (byte)255, (byte)200));
            }

            float clamped = Math.Clamp(progress, 0f, 1f);

            if (clamped > 0f)
            {
                if (_noticeBarFillTexture.Id != 0)
                {
                    var fillSrc = new Rectangle(0, 0, _noticeBarFillTexture.Width * clamped, _noticeBarFillTexture.Height);
                    var fillDest = new Rectangle(barDest.X, barDest.Y, NoticeBarWidth * clamped, NoticeBarHeight);

                    Raylib.DrawTexturePro(_noticeBarFillTexture, fillSrc, fillDest, Vector2.Zero, 0f, Color.White);
                }
                else
                {
                    // 💡 bar.pngが見つからない場合のフォールバック
                    var fillDest = new Rectangle(barDest.X, barDest.Y, NoticeBarWidth * clamped, NoticeBarHeight);
                    Raylib.DrawRectangleRec(fillDest, Color.White);
                }
            }
        }

        // 💡 ゲージ満タン後、画面全体に白い矩形を重ねてフェードアウト(→フェードイン白)させる
        if (whiteAlpha > 0f)
        {
            byte alphaByte = (byte)Math.Clamp(whiteAlpha * 255f, 0f, 255f);
            Raylib.DrawRectangle(0, 0, sw, sh, new Color((byte)255, (byte)255, (byte)255, alphaByte));
        }

        Program.EndVirtualFrame();
        Program.PresentVirtualScreen();
    }
}
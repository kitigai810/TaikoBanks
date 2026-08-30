using System;
using System.IO;
using System.Numerics;
using System.Text;
using System.Collections.Generic;
using Raylib_cs;

/// <summary>
/// 画像モックアップに基づくレイアウト：
///   左下「どんちゃん」box  … 3Dプレビュー(DonChan3D.DrawSimple)を表示
///   右側の大きな表        … 着せ替え / 体 / 頭 / 顔 / 色 の5カラムから選ぶ設定パネル
/// </summary>
public static class Kisekae
{
    // メインの Scene.Kisekae からポーリングされる「戻る」フラグ。ESCで true になる。
    public static bool ShouldReturn { get; private set; }
    private static bool _initialized = false;
    const int ScreenW = 1500;
    const int ScreenH = 980;

    // 左側の空きスペースいっぱいに使う、大きめのどんちゃんプレビューbox
    const float DonBoxW = 650f;
    const float DonBoxH = DonBoxW * 599f / 820f;
    static readonly Rectangle DonBox = new Rectangle(20, 300, DonBoxW, DonBoxH);

    // どんちゃんプレビューの背景画像
    const string BackTexturePath = "Skin/Kisekae/Back.png";
    static Texture2D _backTexture;
    static bool _backTextureLoaded = false;

    // セーブ・ロードボタンの配置
    static readonly Rectangle SaveBtnRect = new Rectangle(20, 750, 180, 50);
    static readonly Rectangle LoadBtnRect = new Rectangle(220, 750, 180, 50);

    // 表示モード切替タブ（DonBox上部）：着せ替え / 体+頭
    const float ModeTabH = 32f;

    static readonly Rectangle TableBox = new Rectangle(400, 140, 960, 720);
    const int HeaderH = 56;
    const int ColCount = 5; // 着せ替え / 体 / 頭 / 顔 / 色
    static float ColW => TableBox.Width / ColCount;

    static readonly string[] ColumnLabels = { "着せ替え", "体", "頭", "顔", "色" };

    enum ColorTarget { Face, Body, Rim }
    static ColorTarget _colorTarget = ColorTarget.Face;

    static float _scrollCostume = 0f;
    static float _scrollBody = 0f;
    static float _scrollHead = 0f;
    static float _scrollFace = 0f;

    const int ListItemH = 34;
    const int ListMaxIdShown = 500;

    static void LoadBackTexture()
    {
        if (!File.Exists(BackTexturePath))
        {
            Console.WriteLine($"[Back] 背景画像が見つかりません: {BackTexturePath}（無くても動作しますが背景は白のままです）");
            return;
        }

        Texture2D tex = Raylib.LoadTexture(BackTexturePath);
        if (tex.Id != 0)
        {
            _backTexture = tex;
            _backTextureLoaded = true;
            Console.WriteLine($"[Back] 背景画像を読み込みました: {BackTexturePath}");
        }
    }

    static readonly string[] FontCandidates =
    {
        "font/font.otf",
        "font/NotoSansJP-Regular.otf",
        "font/NotoSansJP-Regular.ttf",
        @"C:\Windows\Fonts\meiryo.ttc",
        @"C:\Windows\Fonts\YuGothM.ttc",
        @"C:\Windows\Fonts\msgothic.ttc",
        "/System/Library/Fonts/ヒラギノ角ゴシック W3.ttc",
        "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
    };

    static Font _jpFont;
    static bool _jpFontLoaded = false;

    static void LoadJapaneseFont()
    {
        int[] codepoints = BuildCodepoints();

        foreach (string path in FontCandidates)
        {
            if (!File.Exists(path)) continue;

            Font font = Raylib.LoadFontEx(path, 48, codepoints, codepoints.Length);
            if (font.Texture.Id != 0 && font.GlyphCount > 0)
            {
                _jpFont = font;
                _jpFontLoaded = true;
                Console.WriteLine($"[Font] 日本語フォントを読み込みました: {path}");
                return;
            }
        }

        Console.WriteLine("[Font][警告] 日本語フォントが見つかりませんでした。");
        _jpFont = Raylib.GetFontDefault();
        _jpFontLoaded = false;
    }

    static int[] BuildCodepoints()
    {
        var set = new HashSet<int>();
        // 基本ASCII
        for (int c = 0x20; c <= 0x7E; c++) set.Add(c);

        // ひらがな・カタカナ・句読点・記号
        for (int c = 0x3000; c <= 0x30FF; c++) set.Add(c);

        // 漢字領域（JIS第1・第2水準を網羅）
        for (int c = 0x4E00; c <= 0x9FFF; c++) set.Add(c);

        string[] allTexts =
        {
            "どんちゃん", "着せ替え", "体", "頭", "顔", "色",
            "Face", "Body", "Rim", "color:", "どんちゃん着せ替えビューア", "save", "load"
        };

        foreach (string s in allTexts)
        {
            foreach (Rune r in s.EnumerateRunes())
            {
                set.Add(r.Value);
            }
        }

        var arr = new int[set.Count];
        set.CopyTo(arr);
        return arr;
    }

    static void DrawTextJP(string text, int x, int y, int fontSize, Color color)
    {
        if (_jpFontLoaded)
        {
            Raylib.DrawTextEx(_jpFont, text, new Vector2(x, y), fontSize, 1f, color);
        }
        else
        {
            Raylib.DrawText(text, x, y, fontSize, color);
        }
    }

    static int MeasureTextJP(string text, int fontSize)
    {
        if (_jpFontLoaded)
        {
            return (int)Raylib.MeasureTextEx(_jpFont, text, fontSize, 1f).X;
        }
        return Raylib.MeasureText(text, fontSize);
    }

    public static void Init()
    {
        ShouldReturn = false;

        if (!_initialized)
        {
            LoadJapaneseFont();
            LoadBackTexture();
            _initialized = true;
        }

        DonChan3D.CamPosX = 0.00f;
        DonChan3D.CamPosY = 0.00f;
        DonChan3D.CamPosZ = 20.00f;
        DonChan3D.TarPosY = 0.00f;
        DonChan3D.ModelRotX = 181.25f;
        DonChan3D.ModelRotY = 27.50f;
        DonChan3D.ModelRotZ = 0.00f;

        DonChan3D.Init();
    }

    public static void Update()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            ShouldReturn = true;
            return;
        }

        DonChan3D.Update();
        HandleInput();

        Vector2 mouse = Raylib.GetMousePosition();
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            if (Raylib.CheckCollisionPointRec(mouse, SaveBtnRect))
            {
                DonChan3D.WriteConfig();
            }
            else if (Raylib.CheckCollisionPointRec(mouse, LoadBtnRect))
            {
                DonChan3D.LoadConfig();
            }
        }
    }

    public static void Draw()
    {
        Raylib.ClearBackground(new Color((byte)235, (byte)235, (byte)235, (byte)255));

        if (_backTextureLoaded)
        {
            Rectangle backSrc = new Rectangle(0, 0, _backTexture.Width, _backTexture.Height);
            Rectangle backDst = new Rectangle(0, 0, ScreenW, ScreenH);
            Raylib.DrawTexturePro(_backTexture, backSrc, backDst, Vector2.Zero, 0f, Color.White);
        }

        DrawDonBox();
        DrawTable();
        DrawSaveLoadButtons();
    }

    static void DrawSaveLoadButtons()
    {
        Raylib.DrawRectangleRec(SaveBtnRect, Color.LightGray);
        Raylib.DrawRectangleLinesEx(SaveBtnRect, 2, Color.Black);
        int saveTextW = MeasureTextJP("save", 24);
        DrawTextJP("save", (int)(SaveBtnRect.X + (SaveBtnRect.Width - saveTextW) / 2), (int)(SaveBtnRect.Y + 13), 24, Color.Black);

        Raylib.DrawRectangleRec(LoadBtnRect, Color.LightGray);
        Raylib.DrawRectangleLinesEx(LoadBtnRect, 2, Color.Black);
        int loadTextW = MeasureTextJP("load", 24);
        DrawTextJP("load", (int)(LoadBtnRect.X + (LoadBtnRect.Width - loadTextW) / 2), (int)(LoadBtnRect.Y + 13), 24, Color.Black);
    }

    public static void Unload()
    {
        // DonChan3D などのリソースは保持
    }

    static void HandleInput()
    {
        Vector2 mouse = Raylib.GetMousePosition();

        HandleModeTabClick(mouse);

        // 💡 着せ替え選択時：CostumeIdを更新し、描画モードを CosOnly（着せ替え）にする
        HandleIdListClick(ColumnBodyRect(0), ref _scrollCostume, DonChan3D.CostumeId, id =>
        {
            DonChan3D.SetCostumeId(id);
            DonChan3D.CurrentDrawMode = DonChan3D.DrawMode.CosOnly;
        });

        // 💡 体・頭・顔 選択時：各パーツIDを更新し、描画モードを BodyHeadOnly（体+頭）にする
        // ※ CostumeId を消さずに保持したまま安全に描画モードだけを切り替えます
        HandleIdListClick(ColumnBodyRect(1), ref _scrollBody, DonChan3D.BodyId, id =>
        {
            DonChan3D.SetBodyId(id);
            DonChan3D.CurrentDrawMode = DonChan3D.DrawMode.BodyHeadOnly;
        });

        HandleIdListClick(ColumnBodyRect(2), ref _scrollHead, DonChan3D.HeadId, id =>
        {
            DonChan3D.SetHeadId(id);
            DonChan3D.CurrentDrawMode = DonChan3D.DrawMode.BodyHeadOnly;
        });

        HandleIdListClick(ColumnBodyRect(3), ref _scrollFace, DonChan3D.FaceId, id =>
        {
            DonChan3D.SetFaceId(id);
            DonChan3D.CurrentDrawMode = DonChan3D.DrawMode.BodyHeadOnly;
        });

        HandleColorColumnClick(ColumnBodyRect(4), mouse);
    }

    static void HandleModeTabClick(Vector2 mouse)
    {
        Rectangle tabsRect = new Rectangle(DonBox.X, DonBox.Y, DonBox.Width, ModeTabH);
        if (!Raylib.CheckCollisionPointRec(mouse, tabsRect)) return;
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;

        float half = DonBox.Width / 2f;
        bool leftTab = (mouse.X - DonBox.X) < half;
        DonChan3D.CurrentDrawMode = leftTab ? DonChan3D.DrawMode.CosOnly : DonChan3D.DrawMode.BodyHeadOnly;
    }

    static void HandleIdListClick(Rectangle colRect, ref float scroll, int currentId, Action<int> onSelect)
    {
        Vector2 mouse = Raylib.GetMousePosition();
        bool hoveredCol = Raylib.CheckCollisionPointRec(mouse, colRect);

        if (hoveredCol)
        {
            float wheel = Raylib.GetMouseWheelMove();
            scroll -= wheel * ListItemH * 2f;
        }

        float maxScroll = Math.Max(0, ListMaxIdShown * ListItemH - colRect.Height);
        scroll = Math.Clamp(scroll, 0, maxScroll);

        if (!hoveredCol) return;
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;

        int relativeY = (int)(mouse.Y - colRect.Y + scroll);
        int idx = relativeY / ListItemH;
        if (idx < 0 || idx >= ListMaxIdShown) return;

        onSelect(idx);
    }

    static void HandleColorColumnClick(Rectangle colRect, Vector2 mouse)
    {
        float tabH = 28f;
        Rectangle tabsRect = new Rectangle(colRect.X, colRect.Y, colRect.Width, tabH);
        if (Raylib.CheckCollisionPointRec(mouse, tabsRect) && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            float third = colRect.Width / 3f;
            int tabIdx = (int)((mouse.X - colRect.X) / third);
            tabIdx = Math.Clamp(tabIdx, 0, 2);
            _colorTarget = (ColorTarget)tabIdx;
            return;
        }

        var palette = DonChan3D.SharedColorPalette;
        const int cols = 9;
        int rows = (int)Math.Ceiling(palette.Length / (float)cols);
        float cell = colRect.Width / cols;
        float gridY = colRect.Y + tabH + 8f;

        Rectangle gridRect = new Rectangle(colRect.X, gridY, colRect.Width, rows * cell);
        if (!Raylib.CheckCollisionPointRec(mouse, gridRect)) return;
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;

        int col = (int)((mouse.X - colRect.X) / cell);
        int row = (int)((mouse.Y - gridY) / cell);
        int idx = row * cols + col;
        if (idx < 0 || idx >= palette.Length) return;

        switch (_colorTarget)
        {
            case ColorTarget.Face: DonChan3D.SetFaceColorIndex(idx); break;
            case ColorTarget.Body: DonChan3D.SetBodyColorIndex(idx); break;
            case ColorTarget.Rim: DonChan3D.SetRimColorIndex(idx); break;
        }
    }

    static void DrawDonBox()
    {
        Raylib.DrawRectangleLinesEx(DonBox, 2, Color.Black);

        DrawModeTabs();

        Rectangle inner = new Rectangle(
            DonBox.X + 4, DonBox.Y + ModeTabH + 4,
            DonBox.Width - 8, DonBox.Height - ModeTabH - 8);

        const float srcW = 820f;
        const float srcH = 599f;

        float scale = Math.Min(inner.Width / srcW, inner.Height / srcH);
        float dstW = srcW * scale;
        float dstH = srcH * scale;
        Rectangle previewRect = new Rectangle(
            inner.X + (inner.Width - dstW) / 2f,
            inner.Y + (inner.Height - dstH) / 2f,
            dstW, dstH);

        try
        {
            DonChan3D._insideVirtualFrame = true;
            DonChan3D.DrawSimple(previewRect);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[DrawSimple ERROR] " + ex);
        }
        finally
        {
            DonChan3D._insideVirtualFrame = false;
        }

        int textW = MeasureTextJP("どんちゃん", 22);
        DrawTextJP("どんちゃん",
            (int)(DonBox.X + DonBox.Width / 2 - textW / 2),
            (int)(DonBox.Y + DonBox.Height + 8),
            22, Color.Black);
    }

    static void DrawModeTabs()
    {
        float half = DonBox.Width / 2f;
        bool cosActive = DonChan3D.CurrentDrawMode == DonChan3D.DrawMode.CosOnly;

        Rectangle tabCos = new Rectangle(DonBox.X, DonBox.Y, half, ModeTabH);
        Rectangle tabBodyHead = new Rectangle(DonBox.X + half, DonBox.Y, half, ModeTabH);

        Color activeColor = new Color((byte)255, (byte)210, (byte)90, (byte)255);
        Color inactiveColor = new Color((byte)225, (byte)225, (byte)225, (byte)255);

        Raylib.DrawRectangleRec(tabCos, cosActive ? activeColor : inactiveColor);
        Raylib.DrawRectangleRec(tabBodyHead, !cosActive ? activeColor : inactiveColor);
        Raylib.DrawRectangleLinesEx(tabCos, 1, Color.Black);
        Raylib.DrawRectangleLinesEx(tabBodyHead, 1, Color.Black);

        int tw1 = MeasureTextJP("着せ替え", 16);
        DrawTextJP("着せ替え", (int)(tabCos.X + tabCos.Width / 2 - tw1 / 2), (int)(tabCos.Y + 7), 16, Color.Black);

        int tw2 = MeasureTextJP("体+頭", 16);
        DrawTextJP("体+頭", (int)(tabBodyHead.X + tabBodyHead.Width / 2 - tw2 / 2), (int)(tabBodyHead.Y + 7), 16, Color.Black);
    }

    static void DrawTable()
    {
        Raylib.DrawRectangleLinesEx(TableBox, 2, Color.Black);
        Raylib.DrawRectangleRec(TableBox, Color.White);

        Rectangle headerRect = new Rectangle(TableBox.X, TableBox.Y, TableBox.Width, HeaderH);
        Raylib.DrawRectangleLinesEx(headerRect, 2, Color.Black);

        for (int i = 0; i < ColCount; i++)
        {
            float x = TableBox.X + i * ColW;
            Rectangle cell = new Rectangle(x, TableBox.Y, ColW, HeaderH);
            Raylib.DrawRectangleLinesEx(cell, 1, Color.Black);

            int tw = MeasureTextJP(ColumnLabels[i], 24);
            DrawTextJP(ColumnLabels[i],
                (int)(x + ColW / 2 - tw / 2),
                (int)(TableBox.Y + HeaderH / 2 - 12),
                24, Color.Black);
        }

        DrawIdListColumn(ColumnBodyRect(0), _scrollCostume, DonChan3D.CostumeId, "着せ替え");
        DrawIdListColumn(ColumnBodyRect(1), _scrollBody, DonChan3D.BodyId, "体");
        DrawIdListColumn(ColumnBodyRect(2), _scrollHead, DonChan3D.HeadId, "頭");
        DrawIdListColumn(ColumnBodyRect(3), _scrollFace, DonChan3D.FaceId, "顔");
        DrawColorColumn(ColumnBodyRect(4));

        for (int i = 1; i < ColCount; i++)
        {
            float x = TableBox.X + i * ColW;
            Raylib.DrawLineEx(new Vector2(x, TableBox.Y + HeaderH), new Vector2(x, TableBox.Y + TableBox.Height), 1, Color.Black);
        }
    }

    static Rectangle ColumnBodyRect(int colIndex)
    {
        float x = TableBox.X + colIndex * ColW;
        return new Rectangle(x, TableBox.Y + HeaderH, ColW, TableBox.Height - HeaderH);
    }

    static void DrawIdListColumn(Rectangle colRect, float scroll, int currentId, string label)
    {
        Raylib.BeginScissorMode((int)colRect.X, (int)colRect.Y, (int)colRect.Width, (int)colRect.Height);

        for (int i = 0; i < ListMaxIdShown; i++)
        {
            float y = colRect.Y + i * ListItemH - scroll;
            if (y + ListItemH < colRect.Y || y > colRect.Y + colRect.Height) continue;

            Rectangle rowRect = new Rectangle(colRect.X + 6, y + 2, colRect.Width - 12, ListItemH - 4);
            bool selected = (i == currentId);
            bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rowRect);

            Color bg = selected ? new Color((byte)255, (byte)210, (byte)90, (byte)255)
                     : hover ? new Color((byte)230, (byte)230, (byte)230, (byte)255)
                     : Color.Blank;

            if (bg.A > 0) Raylib.DrawRectangleRec(rowRect, bg);
            if (selected) Raylib.DrawRectangleLinesEx(rowRect, 2, new Color((byte)200, (byte)140, (byte)0, (byte)255));

            DrawTextJP($"{label} {i:D2}", (int)rowRect.X + 8, (int)rowRect.Y + 8, 16, Color.Black);
        }

        Raylib.EndScissorMode();
    }

    static void DrawColorColumn(Rectangle colRect)
    {
        Raylib.BeginScissorMode((int)colRect.X, (int)colRect.Y, (int)colRect.Width, (int)colRect.Height);

        float tabH = 28f;
        float third = colRect.Width / 3f;
        string[] tabNames = { "Face", "Body", "Rim" };
        for (int i = 0; i < 3; i++)
        {
            Rectangle tabRect = new Rectangle(colRect.X + i * third, colRect.Y, third, tabH);
            bool active = ((int)_colorTarget == i);
            Raylib.DrawRectangleRec(tabRect, active ? new Color((byte)255, (byte)210, (byte)90, (byte)255) : new Color((byte)225, (byte)225, (byte)225, (byte)255));
            Raylib.DrawRectangleLinesEx(tabRect, 1, Color.Black);
            int tw = MeasureTextJP(tabNames[i], 14);
            DrawTextJP(tabNames[i], (int)(tabRect.X + third / 2 - tw / 2), (int)(tabRect.Y + 7), 14, Color.Black);
        }

        Color currentColor = _colorTarget switch
        {
            ColorTarget.Face => DonChan3D.FaceColor,
            ColorTarget.Body => DonChan3D.BodyColor,
            ColorTarget.Rim => DonChan3D.RimColor,
            _ => Color.White
        };
        int curIdx = _colorTarget switch
        {
            ColorTarget.Face => DonChan3D.FaceColorIndex,
            ColorTarget.Body => DonChan3D.BodyColorIndex,
            ColorTarget.Rim => DonChan3D.RimColorIndex,
            _ => -1
        };

        var palette = DonChan3D.SharedColorPalette;
        const int cols = 9;
        int rows = (int)Math.Ceiling(palette.Length / (float)cols);
        float cell = colRect.Width / cols;
        float gridY = colRect.Y + tabH + 8f;

        for (int i = 0; i < palette.Length; i++)
        {
            int col = i % cols;
            int row = i / cols;
            float x = colRect.X + col * cell;
            float y = gridY + row * cell;

            Raylib.DrawRectangleRec(new Rectangle(x + 1, y + 1, cell - 2, cell - 2), palette[i]);
            bool selected = (i == curIdx);
            Raylib.DrawRectangleLinesEx(new Rectangle(x + 1, y + 1, cell - 2, cell - 2), 1, selected ? Color.Orange : Color.Black);
            if (selected)
                Raylib.DrawRectangleLinesEx(new Rectangle(x, y, cell, cell), 2, Color.Orange);
        }

        float sampleY = gridY + rows * cell + 12f;
        DrawTextJP($"{tabNames[(int)_colorTarget]} color:", (int)colRect.X + 6, (int)sampleY, 14, Color.Black);
        Raylib.DrawRectangleRec(new Rectangle(colRect.X + 6, sampleY + 18, 40, 20), currentColor);
        Raylib.DrawRectangleLinesEx(new Rectangle(colRect.X + 6, sampleY + 18, 40, 20), 1, Color.Black);

        Raylib.EndScissorMode();
    }
}
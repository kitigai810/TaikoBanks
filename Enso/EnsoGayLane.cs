using Raylib_cs;
using System.Numerics;

/// <summary>
/// Lumen/1.Enso/syousai/GAY/Lane.png をレーンの下に描画するクラス。
/// あわせて曲のBPM・実効スクロール速度(BPM換算)・小節線(現在/総数)を
/// Lumen/1.Enso/syousai/GAY/Number.png の数字素材で描画する。
/// Enso.DrawLane() の直後から呼び出すこと。
/// </summary>
public static class EnsoGayLane
{
    private const string TEX_PATH = "Lumen/1.Enso/syousai/GAY/Lane.png";
    private const string NUM_TEX_PATH = "Lumen/1.Enso/syousai/GAY/Number.png";
    private const string SLASH_TEX_PATH = "Lumen/1.Enso/syousai/GAY/slash.png";

    // 位置・大きさ調整用（自由に変更可）
    public static float X = 500f;      // Enso.LANE_X と同じ基準
    public static float Y = 535f;      // レーンの下に来るように調整
    public static float Scale = 0.335f;  // 大きさ倍率

    // 各グループの「左端」の基準座標（UIの枠に合わせて数値を調整してください）
    public static float BpmX = 925f;          // BPM表示の左端
    public static float ScrollBpmX = 1300f;   // スクロール速度表示の左端
    public static float BarX = 1700f;         // 小節線表示（〇/〇）の左端

    // 数字・記号の共通デザイン設定
    public static float NumberY = 557.5f;
    public static float NumberH = 50f;        // 数字1文字の描画高さ
    public static float NumberDigitGap = -15f;  // 数字同士の間隔
    public static float NumberSlashPad = 0f;  // "/" の左右余白

    private static Texture2D _tex;
    private static bool _loaded = false;

    private static Texture2D _numTex;
    private static bool _numLoaded = false;
    private static int _numDigitW; // Number.png 内の1文字分の幅（10等分）

    private static Texture2D _slashTex;
    private static bool _slashLoaded = false;

    public static void Load()
    {
        if (!_loaded)
        {
            _tex = Raylib.LoadTexture(TEX_PATH);
            _loaded = _tex.Id != 0;
        }

        if (!_numLoaded)
        {
            _numTex = Raylib.LoadTexture(NUM_TEX_PATH);
            _numLoaded = _numTex.Id != 0;
            if (_numLoaded) _numDigitW = _numTex.Width / 10;
        }

        if (!_slashLoaded)
        {
            _slashTex = Raylib.LoadTexture(SLASH_TEX_PATH);
            _slashLoaded = _slashTex.Id != 0;
        }
    }

    public static void Unload()
    {
        if (_loaded)
        {
            Raylib.UnloadTexture(_tex);
            _loaded = false;
        }

        if (_numLoaded)
        {
            Raylib.UnloadTexture(_numTex);
            _numLoaded = false;
        }

        if (_slashLoaded)
        {
            Raylib.UnloadTexture(_slashTex);
            _loaded = false; // 元のコードのバグ（_slashLoadedではなく_loadedをリセットしていた）を修正
            _slashLoaded = false;
        }
    }

    public static void Draw(float vx, float vy, float s)
    {
        if (_loaded && _tex.Id != 0)
        {
            float w = _tex.Width * s * Scale;
            float h = _tex.Height * s * Scale;
            float dx = X * s + vx;
            float dy = Y * s + vy;

            Rectangle src = new Rectangle(0, 0, _tex.Width, _tex.Height);
            Rectangle dest = new Rectangle(dx, dy, w, h);

            Raylib.DrawTexturePro(_tex, src, dest, Vector2.Zero, 0f, Color.White);
        }

        DrawStatusNumbers(vx, vy, s);
    }

    private static void DrawStatusNumbers(float vx, float vy, float s)
    {
        if (!_numLoaded || _numTex.Id == 0) return;

        var status = Enso.GetGayLaneStatus();

        int bpm = (int)System.Math.Round(status.Bpm);
        int scrollBpm = (int)System.Math.Round(status.ScrollBpm);

        // 各項目に独立した固定のX座標を渡して描画
        // これにより、BPMが何桁になろうとも、スクロール速度や小節線の位置は1ミリもズレません
        DrawNumberText($"{bpm}", BpmX * s + vx, NumberY * s + vy, NumberH * s, s);
        DrawNumberText($"{scrollBpm}", ScrollBpmX * s + vx, NumberY * s + vy, NumberH * s, s);
        DrawNumberText($"{status.Bar}/{status.TotalBars}", BarX * s + vx, NumberY * s + vy, NumberH * s, s);
    }

    /// <summary>
    /// 指定された左上 (x, y) を起点に、左詰めで数字・記号を描画する。
    /// </summary>
    private static void DrawNumberText(string text, float x, float y, float h, float s)
    {
        float digitW = h * ((float)_numDigitW / _numTex.Height);
        float slashGlyphW = _slashLoaded
            ? h * ((float)_slashTex.Width / _slashTex.Height)
            : digitW * 0.55f;
        float slashW = slashGlyphW + NumberSlashPad * 2f * s;

        float cx = x;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '/')
            {
                DrawSlash(cx + NumberSlashPad * s, y, slashGlyphW, h);
                cx += slashW + NumberDigitGap * s;
                continue;
            }

            if (c >= '0' && c <= '9')
            {
                int digit = c - '0';
                Rectangle src = new Rectangle(digit * _numDigitW, 0, _numDigitW, _numTex.Height);
                Rectangle dest = new Rectangle(cx, y, digitW, h);
                Raylib.DrawTexturePro(_numTex, src, dest, Vector2.Zero, 0f, Color.White);
                cx += digitW + NumberDigitGap * s;
                continue;
            }
        }
    }

    private static void DrawSlash(float x, float y, float w, float h)
    {
        if (_slashLoaded && _slashTex.Id != 0)
        {
            Rectangle src = new Rectangle(0, 0, _slashTex.Width, _slashTex.Height);
            Rectangle dest = new Rectangle(x, y, w, h);
            Raylib.DrawTexturePro(_slashTex, src, dest, Vector2.Zero, 0f, Color.White);
            return;
        }

        // slash.png が読み込めない場合の簡易フォールバック
        Vector2 top = new Vector2(x + w, y);
        Vector2 bottom = new Vector2(x, y + h);
        Raylib.DrawLineEx(top, bottom, h * 0.16f, Color.Black);
        Raylib.DrawLineEx(top, bottom, h * 0.09f, Color.White);
    }
}

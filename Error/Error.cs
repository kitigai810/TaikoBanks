using Raylib_cs;
using System;
using System.Numerics;

public static class Error
{
    private static string _errorName = "";
    private static string _message = "";
    private static string _stackTrace = "";
    private static bool _hasError;
    private static double _shownAtTime;

    public static bool HasError => _hasError;

    // ==================================================================
    // 💡 [位置調整用パラメータ] ここの数値を変えるだけで各テキストの位置・サイズ・色を調整できます。
    //    座標は左上原点。
    // ==================================================================

    // エラー名(例外の型名。NullReferenceException等)
    public static float ErrorNameY = 220f;
    public static float ErrorNameFontSize = 32f;
    public static Color ErrorNameColor = Color.White;

    // 「エラーが発生しています。」(赤文字・sin波で0.5秒間に3回点滅)
    public static float BlinkTextY = 355f;
    public static float BlinkTextFontSize = 32f;
    public static Color BlinkTextColor = new Color(255, 0, 0, 255);
    public const float BLINK_PERIOD_SECONDS = 3f; // この秒数の間に…
    public const int BLINK_COUNT = 2;               // …何回点滅させるか

    // 「係員をお呼びください。」
    public static float CallStaffY = 570f;
    public static float CallStaffFontSize = 28f;
    public static Color CallStaffColor = Color.White;

    // 「取扱説明書の指示に従ってください。」
    public static float ManualY = 620f;
    public static float ManualFontSize = 28f;
    public static Color ManualColor = Color.White;

    /// <summary>
    /// 例外発生時にこれを呼んでError画面へ切り替える
    /// </summary>
    public static void Show(Exception ex)
    {
        _hasError = true;
        _errorName = ex.GetType().Name;
        _message = ex.Message;
        _stackTrace = ex.ToString();
        _shownAtTime = Raylib.GetTime();

        // コンソール＆ログファイルにも残す(強制終了した場合の追跡用。画面には出さない)
        Console.WriteLine("[FATAL] " + _stackTrace);
        try
        {
            System.IO.File.AppendAllText("crash_log.txt",
                $"[{DateTime.Now}] {_stackTrace}\n\n");
        }
        catch { /* ログ書き込み自体の失敗は無視 */ }
    }

    public static void Update()
    {
        // Enterキーでタイトルに戻る(画面表示は消したが機能は残す)
        if (Raylib.IsKeyPressed(KeyboardKey.Enter))
        {
            _hasError = false;
        }
    }

    /// <summary>
    /// テキストを画面の水平中央に配置するためのX座標を計算するヘルパー
    /// </summary>
    private static Vector2 CenteredPos(string text, float fontSize, float y)
    {
        float screenWidth = Program.VirtualWidth;
        Vector2 size = Raylib.MeasureTextEx(G.FontSub, text, fontSize, 2);
        float x = (screenWidth - size.X) * 0.5f;
        return new Vector2(x, y);
    }

    public static void Draw()
    {
        Raylib.ClearBackground(Color.Black);

        // --- エラー名 (SubFont・中央揃え) ---
        Raylib.DrawTextEx(G.FontSub, _errorName, CenteredPos(_errorName, ErrorNameFontSize, ErrorNameY), ErrorNameFontSize, 2, ErrorNameColor);

        // --- 「エラーが発生しています。」(SubFont・赤・最初の点滅期間だけsin波でなめらかに点滅→以降は点灯・中央揃え) ---
        double t = Raylib.GetTime() - _shownAtTime;
        float blinkAlpha;

        if (t < BLINK_PERIOD_SECONDS)
        {
            // 点滅期間中: sin波を0.0〜1.0に正規化してアルファ値として使う(なめらかなフェード)
            float frequency = BLINK_COUNT / BLINK_PERIOD_SECONDS;
            float sinValue = (float)Math.Sin(2.0 * Math.PI * frequency * t);
            blinkAlpha = (sinValue + 1f) * 0.5f; // -1〜1 → 0〜1
        }
        else
        {
            // 点滅期間終了後: 点灯したまま
            blinkAlpha = 1f;
        }

        Color blinkColor = new Color(
            BlinkTextColor.R,
            BlinkTextColor.G,
            BlinkTextColor.B,
            (byte)(255 * blinkAlpha)
        );

        string blinkText = "エラーが発生しています。";
        Raylib.DrawTextEx(G.FontSub, blinkText, CenteredPos(blinkText, BlinkTextFontSize, BlinkTextY), BlinkTextFontSize, 2, blinkColor);

        // --- 「係員をお呼びください。」「取扱説明書の指示に従ってください。」(SubFont・2行の左端を揃えてグループごと中央配置) ---
        string callStaffText = "係員をお呼びください。";
        string manualText = "取扱説明書の指示に従ってください。";

        float screenWidth = Program.VirtualWidth;
        Vector2 callStaffSize = Raylib.MeasureTextEx(G.FontSub, callStaffText, CallStaffFontSize, 2);
        Vector2 manualSize = Raylib.MeasureTextEx(G.FontSub, manualText, ManualFontSize, 2);
        float groupWidth = Math.Max(callStaffSize.X, manualSize.X);
        float groupX = (screenWidth - groupWidth) * 0.5f;

        Raylib.DrawTextEx(G.FontSub, callStaffText, new Vector2(groupX, CallStaffY), CallStaffFontSize, 2, CallStaffColor);
        Raylib.DrawTextEx(G.FontSub, manualText, new Vector2(groupX, ManualY), ManualFontSize, 2, ManualColor);
    }
}
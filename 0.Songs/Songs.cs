using Raylib_cs;

// 💡 [修正] 横スクロール背景（GENREカテゴリ連動のクロスフェード実装）はProgram.cs側へ移動した。
// このクラスにはUI配置のための16:9ビューポート計算のみを残す。
public static class Songs
{
    /// <summary>
    /// 16:9 描画領域を返す（FHD固定）
    /// </summary>
    public static (int x, int y, int w, int h) GetViewport()
    {
        return (0, 0, 1920, 1080);
    }

    /// <summary>
    /// SongsType 設定に応じた曲フォルダパスを返す。
    /// TJA → "Songs"、ESE → "Songs/ESE"
    /// </summary>
    public static string GetSongsFolder() => SettingsPanel.SongsFolder;
}

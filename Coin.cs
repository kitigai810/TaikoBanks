using Raylib_cs;
using System;
using System.IO;

/// <summary>
/// コイン投入・フリープレイ設定を管理するクラス。
/// FreePlay = true の場合はコイン投入不要で、Don1/Don2キーでスタート可能。
/// FreePlay = false の場合は Cキー(サービスコイン投入)で投入数をカウントし、
/// RequiredCoins(必要コイン数)以上になったらスタート可能になる。
/// </summary>
public static class Coin
{
    private const string CONFIG_PATH = "coin.cfg";

    // 設定項目 (SettingsPanel.csから編集される)
    public static bool FreePlay = true;
    public static int RequiredCoins = 1; // Coin(s)

    // 現在投入されているコイン数 (Cキーを押した数)
    private static int _insertedCount;
    public static int InsertedCount => _insertedCount;

    static Coin()
    {
        Load();
    }

    private static float _lastCoinTime = -1f;

    /// <summary>
    /// Cキーが押されたときに呼ぶ。コイン投入数を1増やす。
    /// 物理的なチャタリングや多重検知を防ぐため、ごく短いクールタイムを設ける。
    /// </summary>
    public static void AddCoin()
    {
        float now = (float)Raylib.GetTime();
        if (now - _lastCoinTime < 0.001f) return; // 0.1秒以内の連続投入は無視

        _insertedCount++;
        _lastCoinTime = now;
    }

    /// <summary>
    /// 投入数をリセットする(新しいアトラクトループの開始時などに使用)。
    /// </summary>
    public static void ResetInserted()
    {
        _insertedCount = 0;
    }

    /// <summary>
    /// スタート可能かどうか(1人プレイ基準)。フリープレイなら常にtrue。
    /// そうでなければ 投入数 >= RequiredCoins で true。
    /// </summary>
    public static bool IsReadyToStart => FreePlay || _insertedCount >= RequiredCoins;

    /// <summary>
    /// 2人プレイ側の表示切り替え用。フリープレイなら常にtrue。
    /// そうでなければ 投入数 >= RequiredCoins*2 で true。
    /// </summary>
    public static bool IsReadyToStart2P => FreePlay || _insertedCount >= RequiredCoins * 2;

    /// <summary>
    /// コインが1枚も投入されていないかどうか(表示の出し分け用)。
    /// </summary>
    public static bool NoCoinInserted => _insertedCount <= 0;

    /// <summary>
    /// 1人プレイに必要な残りコイン数 (表示用)。0未満にはならない。
    /// </summary>
    public static int Remaining1P => Math.Max(0, RequiredCoins - _insertedCount);

    /// <summary>
    /// 2人プレイに必要な残りコイン数 (表示専用。機能的な制御はしない)。
    /// </summary>
    public static int Remaining2P => Math.Max(0, RequiredCoins * 2 - _insertedCount);

    public static void Save()
    {
        try
        {
            File.WriteAllLines(CONFIG_PATH, new[]
            {
                $"FreePlay={FreePlay}",
                $"RequiredCoins={RequiredCoins}"
            });
        }
        catch { /* 保存失敗は無視 */ }
    }

    public static void Load()
    {
        if (!File.Exists(CONFIG_PATH)) return;

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
                    case "FreePlay": FreePlay = bool.Parse(val); break;
                    case "RequiredCoins": RequiredCoins = int.Parse(val); break;
                }
            }
        }
        catch { /* 読み込み失敗は無視してデフォルト値を使う */ }
    }
}
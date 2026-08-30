using System;
using System.Collections.Concurrent;
using Raylib_cs;

/// <summary>
/// 【調査用・一時的なクラス】VRAMを消費するLoad系API(LoadTexture/LoadRenderTexture/
/// LoadTextureFromImage/LoadModel等)の呼び出し箇所ごとの累計回数を記録し、
/// 5秒おき(またはF4キーで即時)にコンソールへダンプする。
///
/// 使い方:
///   1. Enso.Update() の適当な場所（毎フレーム呼ばれる箇所）に VramProbe.Tick(nowSec) を追加。
///   2. 各Load系呼び出しの直後に VramProbe.Track("ファイル名.場所") を1行追加（このパッチ内で既に追加済み）。
///   3. 同じ曲・同じ長さで「Auto再生」と「手動プレイ」をそれぞれ実行し、コンソール出力を見比べる。
///   4. Autoでは増えないのに手動だと増え続けるタグがあれば、それが犯人。
///
/// 調査が終わったら、この呼び出しごと削除してOK（本番挙動には一切影響しない）。
/// </summary>
public static class VramProbe
{
    private static readonly ConcurrentDictionary<string, int> _counts = new();
    private static double _lastDump = -1;
    private const double DUMP_INTERVAL_SEC = 5.0;

    /// <summary>Load系APIを呼んだ直後に呼ぶ。tagは「クラス名.内容」など呼び出し箇所が分かる文字列にする。</summary>
    public static void Track(string tag)
    {
        _counts.AddOrUpdate(tag, 1, (_, c) => c + 1);
    }

    /// <summary>毎フレーム呼ぶ。5秒おきに自動ダンプ、F4キーで即時ダンプ。</summary>
    public static void Tick(double nowSec)
    {
        bool manualDump = Raylib.IsKeyPressed(KeyboardKey.F4);
        if (!manualDump && (_lastDump >= 0 && nowSec - _lastDump < DUMP_INTERVAL_SEC)) return;
        _lastDump = nowSec;
        Dump();
    }

    public static void Dump()
    {
        Console.WriteLine("---- [VramProbe] Load系 累計呼び出し回数 ----");
        foreach (var kv in _counts)
            Console.WriteLine($"  {kv.Key}: {kv.Value}");
        Console.WriteLine("---------------------------------------------");
    }

    /// <summary>曲の頭(Enso.Init等)で呼んで、曲ごとにカウントをリセットする。</summary>
    public static void Reset()
    {
        _counts.Clear();
        _lastDump = -1;
        Console.WriteLine("[VramProbe] カウンタをリセットしました");
    }
}

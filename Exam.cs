using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

/// <summary>
/// dan.jsonの"conditionGauge"/"conditions"(段位の合格条件)を解析し、
/// プレイ結果(Great/Good/Miss/Roll/Hit/Score/MaxCombo)から
/// 赤合格・金合格・不合格を判定するクラス。
///
/// dan.json 側の仕様(詳細はDANJSON_README参照):
///   conditionGauge : { "red": Int, "gold": Int }               … 魂ゲージの初期値(%)
///   conditions[]   : { "type": String, "threshold": Object|Object[] }
///     - thresholdが単一Objectの場合 → 全曲共通の条件として扱う
///     - thresholdがObject配列の場合 → danSongsの並び順と1:1対応する曲ごとの個別条件
///   type別の閾値判定方向:
///     Great/Roll/Hit/Score/MaxCombo … 閾値「以上」で合格
///     Good/Miss                    … 閾値「未満」で合格
/// </summary>
public static class Exam
{
    public enum ConditionType { Great, Good, Miss, Roll, Hit, Score, MaxCombo }

    public enum PassRank { Fail, Red, Gold }

    /// <summary>1つのtype条件について、赤合格・金合格それぞれの閾値</summary>
    public struct Threshold
    {
        public int Red;
        public int Gold;
    }

    /// <summary>
    /// 1つの条件(conditions[]の1要素)。ThresholdsPerSongは曲数ぶんの配列で保持し、
    /// dan.json側がObject単体(全曲共通)だった場合は同じThresholdを曲数ぶん複製して格納する。
    /// </summary>
    public sealed class Condition
    {
        public ConditionType Type;
        public Threshold[] ThresholdsPerSong; // 💡 danSongsと同じ並び順・同じ要素数
        // 💡 dan.json側の"threshold"が全曲共通のつもりだったか、曲ごとの個別条件だったか。
        //    Object単体は常に全体条件。Array(配列)の場合は、要素数が曲数ぴったりなら個別条件、
        //    曲数より少ない(1個だけ書いて全曲に使い回す等)なら全体条件として扱う。
        //    ThresholdsPerSongはどちらも同じ形(曲数ぶんの配列)に展開してしまうため、
        //    値の比較だけでは区別できない(たまたま全曲同じ値の個別条件もあり得る)。パース時点でここに確定して持たせる。
        public bool IsOverall;
    }

    /// <summary>1曲分のプレイ結果</summary>
    public struct SongResult
    {
        public int Great;
        public int Good;
        public int Miss;
        public int Roll;
        public int Hit;
        public int Score;
        public int MaxCombo;
    }

    /// <summary>conditionGauge(魂ゲージの初期値)</summary>
    public struct ConditionGauge
    {
        public int Red;
        public int Gold;
    }

    // ==================================================================
    // 💡 dan.jsonからの読み込み
    // ==================================================================

    /// <summary>
    /// dan.jsonファイルを読み込み、conditionGauge・conditions(+曲数展開済み)を取り出す。
    /// songCountにはdanSongsの要素数(=段位の曲数)を渡すこと。
    /// </summary>
    public static (ConditionGauge gauge, List<Condition> conditions) LoadFromDanJsonFile(string danJsonPath, int songCount)
    {
        string json = File.ReadAllText(danJsonPath);
        using var doc = JsonDocument.Parse(json);
        return LoadFromDanJsonRoot(doc.RootElement, songCount);
    }

    /// <summary>
    /// 既にパース済みのdan.jsonルート要素(JsonDocument.RootElement)からconditionGauge・conditionsを取り出す。
    /// DanSelectScene側などで既にJsonDocumentを開いている場合はこちらを直接使う。
    /// </summary>
    public static (ConditionGauge gauge, List<Condition> conditions) LoadFromDanJsonRoot(JsonElement root, int songCount)
    {
        var gauge = new ConditionGauge();
        if (root.TryGetProperty("conditionGauge", out var gaugeEl) && gaugeEl.ValueKind == JsonValueKind.Object)
        {
            gauge.Red = GetIntProp(gaugeEl, "red");
            gauge.Gold = GetIntProp(gaugeEl, "gold");
        }

        var conditions = new List<Condition>();
        if (root.TryGetProperty("conditions", out var condsEl) && condsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var condEl in condsEl.EnumerateArray())
            {
                var cond = ParseCondition(condEl, songCount);
                if (cond != null) conditions.Add(cond);
            }
        }

        return (gauge, conditions);
    }

    private static Condition ParseCondition(JsonElement condEl, int songCount)
    {
        if (!condEl.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            return null;

        if (!Enum.TryParse<ConditionType>(typeEl.GetString(), ignoreCase: true, out var type))
        {
            System.Console.WriteLine($"[Exam] 不明なconditions.type: {typeEl.GetString()}");
            return null;
        }

        if (!condEl.TryGetProperty("threshold", out var thEl))
            return null;

        var perSong = new Threshold[Math.Max(songCount, 1)];
        bool isOverall;

        if (thEl.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var th in thEl.EnumerateArray())
            {
                if (i >= perSong.Length) break;
                perSong[i] = ParseThreshold(th);
                i++;
            }

            // 💡 配列の要素数(i)が曲数ぴったりなら「曲ごとの個別条件」。
            //    それより少ない(例:1個だけ書いて全曲に使い回すつもり)なら実質「全体条件」として扱う。
            //    dan.json側が単に書き方として配列を使っているだけで、意味的には全曲共通条件のケースがあるため
            //    (例: Missを1個だけ書いて全曲通しての合計判定に使う等)、Array/Objectの構文だけでは判定できない。
            isOverall = i < perSong.Length;

            // 💡 曲数より少ない場合、末尾は最後に読んだ要素を引き継ぐ(全曲共通条件の複製と同じ結果になる)
            for (int j = i; j < perSong.Length; j++)
            {
                perSong[j] = i > 0 ? perSong[i - 1] : default;
            }
        }
        else if (thEl.ValueKind == JsonValueKind.Object)
        {
            // 💡 全曲共通条件 → 曲数ぶん複製
            isOverall = true;
            var single = ParseThreshold(thEl);
            for (int i = 0; i < perSong.Length; i++) perSong[i] = single;
        }
        else
        {
            return null;
        }

        return new Condition { Type = type, ThresholdsPerSong = perSong, IsOverall = isOverall };
    }

    private static Threshold ParseThreshold(JsonElement th)
    {
        return new Threshold
        {
            Red = GetIntProp(th, "red"),
            Gold = GetIntProp(th, "gold"),
        };
    }

    private static int GetIntProp(JsonElement obj, string name)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)) return i;
        }
        return 0;
    }

    // ==================================================================
    // 💡 判定ロジック
    // ==================================================================

    /// <summary>
    /// 1曲ぶんのプレイ結果(result)を、その曲番(songIndex, 0始まり)に対応する
    /// 各conditionのThresholdsPerSong[songIndex]と照らし合わせて合否ランクを判定する。
    /// 全条件を金合格の閾値で満たせば Gold、赤合格の閾値まで全て満たせば Red、
    /// どれか1つでも赤合格の閾値を満たさなければ Fail。
    /// </summary>
    public static PassRank JudgeSong(List<Condition> conditions, int songIndex, SongResult result)
    {
        if (conditions == null || conditions.Count == 0) return PassRank.Gold;

        bool allGold = true;
        bool allRed = true;

        foreach (var cond in conditions)
        {
            if (songIndex < 0 || songIndex >= cond.ThresholdsPerSong.Length) continue;

            Threshold th = cond.ThresholdsPerSong[songIndex];
            int value = GetValue(result, cond.Type);

            if (!MeetsThreshold(cond.Type, value, th.Gold)) allGold = false;
            if (!MeetsThreshold(cond.Type, value, th.Red)) allRed = false;
        }

        if (allGold) return PassRank.Gold;
        if (allRed) return PassRank.Red;
        return PassRank.Fail;
    }

    /// <summary>
    /// 段位全体(全曲)の合否ランクを判定する。各曲のJudgeSong結果のうち
    /// 一番悪いランクが段位全体のランクになる(1曲でもFailがあれば段位はFail)。
    /// </summary>
    public static PassRank JudgeExam(List<Condition> conditions, IReadOnlyList<SongResult> songResults)
    {
        PassRank overall = PassRank.Gold;
        for (int i = 0; i < songResults.Count; i++)
        {
            var rank = JudgeSong(conditions, i, songResults[i]);
            if (rank < overall) overall = rank;
            if (overall == PassRank.Fail) break;
        }
        return overall;
    }

    /// <summary>type別の閾値判定方向を適用し、value が threshold を満たすかどうかを返す</summary>
    private static bool MeetsThreshold(ConditionType type, int value, int threshold)
    {
        switch (type)
        {
            case ConditionType.Good:
            case ConditionType.Miss:
                return value < threshold; // 💡 未満で合格
            case ConditionType.Great:
            case ConditionType.Roll:
            case ConditionType.Hit:
            case ConditionType.Score:
            case ConditionType.MaxCombo:
                return value >= threshold; // 💡 以上で合格
            default:
                return false;
        }
    }

    private static int GetValue(SongResult result, ConditionType type)
    {
        switch (type)
        {
            case ConditionType.Great: return result.Great;
            case ConditionType.Good: return result.Good;
            case ConditionType.Miss: return result.Miss;
            case ConditionType.Roll: return result.Roll;
            case ConditionType.Hit: return result.Hit;
            case ConditionType.Score: return result.Score;
            case ConditionType.MaxCombo: return result.MaxCombo;
            default: return 0;
        }
    }
}
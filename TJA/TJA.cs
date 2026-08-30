using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaikoNauts.Core.Taiko.Charts;

/// <summary>
/// TJAファイルの読み込みとパース結果の保持。
/// TJAChartCore (https://github.com/touhourenren/TJAChartCore) を使用。
///
/// TJAChartCore のチップ時間は「譜面先頭=0 のミリ秒」なので、
/// ChipSec() で OFFSET を適用した秒に変換して使う
/// （旧TJADotNetが焼き込んでいた時間規約と同じになる）。
/// </summary>
public static class TJA
{
    // ---- パース済みデータ ----
    public static Song? Song { get; private set; }
    public static SongCourse? SelectedCourse { get; private set; }
    public static Course SelectedCourseType { get; private set; } = Course.Oni;
    public static string Title => Song?._header._title ?? "";
    public static string? AudioPath { get; private set; }
    public static string? BgMoviePath { get; private set; }
    public static double MovieOffset { get; private set; }

    // 演奏中に音源の相対パス（フォルダ階層）を安全に解決できるように、読み込んだTJA自体のパスを保持します
    public static string? TjaPath { get; private set; }

    public static double Offset { get; private set; }
    public static double BPM { get; private set; }

    /// <summary>選択されたコースのチップリスト（演奏用）。分岐譜面は達人譜面をマージ済み。</summary>
    public static IReadOnlyList<Chip> Chips { get; private set; } = Array.Empty<Chip>();

    /// <summary>ゴーゴータイムの開始/終了イベント（秒, 開始か）。時間順。</summary>
    public static IReadOnlyList<(double Sec, bool Start)> GoGoEvents { get; private set; }
        = Array.Empty<(double, bool)>();

    // ---- 状態 ----
    public static bool IsLoaded { get; private set; }
    public static string? LoadError { get; private set; }

    /// <summary>
    /// TJAファイルを読み込んでパースする。
    /// </summary>
    /// <param name="tjaPath">TJAファイルのパス</param>
    /// <param name="course">使用するコース (省略時 Oni)</param>
    public static void Load(string tjaPath, Course course = Course.Oni)
    {
        IsLoaded = false;
        LoadError = null;
        Song = null;
        SelectedCourse = null;
        Chips = Array.Empty<Chip>();
        GoGoEvents = Array.Empty<(double, bool)>();

        // 読み込み開始時にパスをプロパティへ退避保存します
        TjaPath = tjaPath;

        try
        {
            var parser = new TjaChartReader();
            Song = parser.GetSongDataFromTja(tjaPath, TjaChartReader.LoadType.Normal);
            if (Song == null)
                throw new Exception("TJAファイルが読み込めません。");

            BPM = Song._header._bpm;
            Offset = Song._header._offset;

            string dir = Path.GetDirectoryName(tjaPath) ?? ".";
            AudioPath = string.IsNullOrEmpty(Song._header._wave)
                 ? null
                 : Path.Combine(dir, Song._header._wave);

            BgMoviePath = string.IsNullOrEmpty(Song._header._moviePath)
                 ? null
                 : Path.Combine(dir, Song._header._moviePath);
            MovieOffset = Song._header._movieOffset;

            // コース選択（指定 → Oni → 存在する最上位、の順にフォールバック）
            var found = Song._songCourses[(int)course]
                     ?? Song._songCourses[(int)Course.Oni]
                     ?? Song._songCourses.LastOrDefault(c => c != null);

            if (found == null)
                throw new Exception("コースが見つかりません。");

            SelectedCourse = found;
            SelectedCourseType = (Course)Math.Max(0, Array.IndexOf(Song._songCourses, found));

            // 分岐譜面は共通部+達人譜面を時間順にマージして1本にする
            var chips = new List<Chip>(found._chips);
            if (found._hasBranch)
                chips.AddRange(found._chipsMaster);
            Chips = chips.OrderBy(c => c._time).ToList();

            SeNote.Assign(Chips);

            GoGoEvents = BuildGoGoEvents(found);

            IsLoaded = true;
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
        }
    }

    /// <summary>チップの譜面時間(ms)を、OFFSET適用済みの演奏秒に変換する。</summary>
    public static double ChipSec(Chip chip) => chip._time / 1000.0 - Offset;

    /// <summary>譜面時間(ms)を、OFFSET適用済みの演奏秒に変換する。</summary>
    public static double TimeSec(double chartMs) => chartMs / 1000.0 - Offset;

    /// <summary>
    /// コマンドリストから GoGo の開始/終了イベント列を作る。
    /// 分岐で同じコマンドが複数回記録されるため、状態遷移のみ拾う。
    /// </summary>
    private static List<(double Sec, bool Start)> BuildGoGoEvents(SongCourse course)
    {
        var events = new List<(double Sec, bool Start)>();
        bool state = false;

        var gogoCmds = course._commands
            .Where(c => c._cmdType == CommandType.GogoStart || c._cmdType == CommandType.GogoEnd)
            .OrderBy(c => c._time);

        foreach (var cmd in gogoCmds)
        {
            bool start = cmd._cmdType == CommandType.GogoStart;
            if (start == state) continue;
            state = start;
            events.Add((TimeSec(cmd._time), start));
        }
        return events;
    }
}
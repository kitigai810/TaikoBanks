using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Raylib_cs;
using TaikoNauts.Core.Taiko.Charts;

public sealed class SongMeta
{
    public string Title = "";
    public string Subtitle = "";
    // 日本語表示用。設定されている場合は Title / Subtitle より優先する。
    public string TitleJP = "";
    public string SubtitleJP = "";
    public string Genre = "";
    public string Difficulty = "";
    public int Level;
}

public static class SongLoadScreen
{
    public enum Status { NotReady, Ready }
    public enum LoadPhase { In, Loading, Out }

    public static LoadPhase Phase =>
        _loaded ? LoadPhase.Out :
        (_loadTask == null ? LoadPhase.In : LoadPhase.Loading);

    public static SongMeta Meta => _meta;
    public static bool LoadFailed => _loadFailed;

    static LuaSkin _skin;
    static Task _loadTask;
    static SongMeta _meta = new();
    static float _timer;
    static bool _loaded;
    static string _tjaPath;
    static Course _course;

    // hint.jsonから選んだ、今回のロード画面用ヒント。
    sealed class HintEntry
    {
        public string Title = "ゲームのヒント";
        public string Body = "";
    }

    static readonly Random _hintRandom = new();
    static HintEntry _selectedHint = new();

    public static void Begin(string tjaPath, Course course, SongMeta meta)
    {
        _meta = meta ?? new SongMeta();
        _timer = 0f;
        _loaded = false;
        _loadFailed = false;
        _loadTask = null;
        _tjaPath = tjaPath;
        _course = course;

        string luaPath = Path.Combine(AppContext.BaseDirectory, "Lumen", "03.SongLoad", "katsu.lua");
        _skin ??= new LuaSkin();
        _skin.Load(luaPath);
        _skin.CallInit();

        // hint.jsonはUTF-8として毎回読み、全候補から制限なしで1件をランダム選択する。
        SelectRandomHint();

        if (!_skin.Ok) StartLoad();
    }

    static bool _loadFailed;

    static void StartLoad()
    {
        if (_loadTask == null)
        {
            var tcs = new TaskCompletionSource();
            _loadTask = tcs.Task;

            // 💡 Task.Run(スレッドプール)だと、他の箇所(曲プレビュー破棄など)が投げた
            //    Task.Runと同じプールを取り合い、順番待ちで読み込み開始そのものが
            //    遅れることがあった。譜面読み込みは最優先で即座に走らせたいので、
            //    専用スレッドを直接立てて他のバックグラウンドタスクと競合しないようにする。
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    TJA.Load(_tjaPath, _course);
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    // 💡 重い譜面ほどパース処理が長くなり、例外(メモリ不足・配列サイズ超過など)が
                    //    発生しやすくなる。ここで捕まえずに投げっぱなしにすると、タスクが
                    //    「未観測の例外」としてファイナライズ時にプロセスごと落ちることがあった。
                    Console.WriteLine($"[SongLoadScreen] TJA読み込み中に例外が発生しました: {ex}");
                    tcs.SetException(ex);
                }
            })
            { IsBackground = true, Priority = System.Threading.ThreadPriority.AboveNormal };
            thread.Start();
        }
    }

    public static Status Update(float dt)
    {
        _timer += dt;

        if (_loadTask == null && _skin.Ok && _skin.LoadRequested)
            StartLoad();

        if (!_loaded && _loadTask != null && _loadTask.IsCompleted)
        {
            // タスクの例外を必ず観測しておく（未観測のままだと後で落ちる原因になる）
            if (_loadTask.IsFaulted)
            {
                _ = _loadTask.Exception;
                _loadFailed = true;
            }
            else
            {
                Enso.Init();
            }
            _loaded = true;
        }

        _skin.UpdateState(_loaded, _timer, Program.VirtualWidth, Program.VirtualHeight, _meta);
        _skin.CallUpdate(dt);

        if (_skin.Ok) return _skin.Finished ? Status.Ready : Status.NotReady;
        return _loaded ? Status.Ready : Status.NotReady;
    }

    public static void Draw()
    {
        if (_skin != null && _skin.Ok)
        {
            _skin.CallDraw();
            if (Phase == LoadPhase.Loading) DrawLoadingMeta();
            return;
        }

        string txt = _loaded ? "READY!" : "Now Loading...";
        float sw = G.MeasureTextWithOutline16Width(G.Font, txt, 48);
        float sh = Raylib.MeasureTextEx(G.Font, txt, 48, 2).Y;
        int w = Program.VirtualWidth;
        int h = Program.VirtualHeight;
        G.DrawTextWithOutline16(G.Font, txt, new Vector2(w / 2f - sw / 2f, h / 2f - sh / 2f), 48, Color.White, Color.White, 0f);
        if (Phase == LoadPhase.Loading) DrawLoadingMeta();
    }

    static void DrawLoadingMeta()
    {
        int w = Program.VirtualWidth;

        string title = !string.IsNullOrEmpty(_meta?.TitleJP)
            ? _meta.TitleJP
            : (_meta?.Title ?? "");
        if (title.Length > 0)
        {
            float tw = G.MeasureTextWithOutline16Width(G.Font, title, 64);
            G.DrawTextWithOutline16(G.Font, title,
                new Vector2(w / 2f - tw / 2f, 361), 64, Color.White, Color.Black);
        }

        string sub = !string.IsNullOrEmpty(_meta?.SubtitleJP)
            ? _meta.SubtitleJP
            : (_meta?.Subtitle ?? "");
        if (sub.Length > 0)
        {
            // 💡 以前はRaylib.MeasureTextEx/DrawTextExを直接使っていたが、これらはG.Fontの
            //    スプライトアトラス(font/JP64.png)経由の描画を認識せず、標準Raylibフォント
            //    (日本語グリフ無し)を見てしまうため、サブタイトルが表示されない原因だった。
            //    G.DrawTextWithOutline16(縁取り0px)経由にしてJP64アトラスへ描画する。
            float sw = G.MeasureTextWithOutline16Width(G.Font, sub, 39);
            G.DrawTextWithOutline16(G.Font, sub,
                new Vector2(w / 2f - sw / 2f, 452), 39, Color.Black, Color.Black, 0f);
        }

        DrawSelectedHint(w);
    }

    // hint.jsonの構造に依存せず、配列・ネスト・文字列だけの要素をすべて候補化する。
    // 日本語はUTF-8で読み、font.cs側で同じ全文字をhint.otfのアトラスへ追加するため文字化けしない。
    static void SelectRandomHint()
    {
        var hints = new List<HintEntry>();
        string path = Path.Combine(AppContext.BaseDirectory, "Lumen", "03.SongLoad", "hint.json");

        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path, new UTF8Encoding(false, true));
                using var doc = JsonDocument.Parse(json);
                CollectHintEntries(doc.RootElement, hints, "ゲームのヒント");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SongLoadScreen] hint.jsonを読み込めませんでした: {ex.Message}");
        }

        _selectedHint = hints.Count > 0
            ? hints[_hintRandom.Next(hints.Count)]
            : new HintEntry();
    }

    static void CollectHintEntries(JsonElement node, List<HintEntry> hints, string inheritedTitle)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.String:
                AddHint(hints, inheritedTitle, node.GetString());
                return;

            case JsonValueKind.Array:
                foreach (JsonElement item in node.EnumerateArray())
                    CollectHintEntries(item, hints, inheritedTitle);
                return;

            case JsonValueKind.Object:
                string title = GetFirstString(node, "title", "heading", "header", "name", "見出し", "タイトル") ?? inheritedTitle;
                string body = GetFirstString(node, "body", "text", "description", "content", "hint", "message", "本文", "説明", "内容", "ヒント");
                if (!string.IsNullOrWhiteSpace(body))
                {
                    AddHint(hints, title, body);
                    return;
                }

                // 定型キーを持たないフラットなJSONも全件読む。例: { "基本": "文字列" }
                foreach (JsonProperty property in node.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        if (!IsTitleKey(property.Name))
                            AddHint(hints, title == inheritedTitle ? property.Name : title, property.Value.GetString());
                    }
                    else
                    {
                        CollectHintEntries(property.Value, hints, title);
                    }
                }
                return;
        }
    }

    static string GetFirstString(JsonElement node, params string[] names)
    {
        foreach (string name in names)
        {
            if (node.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }
        return null;
    }

    static bool IsTitleKey(string key) =>
        key is "title" or "heading" or "header" or "name" or "見出し" or "タイトル";

    static void AddHint(List<HintEntry> hints, string title, string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        hints.Add(new HintEntry
        {
            Title = string.IsNullOrWhiteSpace(title) ? "ゲームのヒント" : title.Replace("\\n", "\n"),
            Body = body.Replace("\\n", "\n"),
        });
    }

    static void DrawSelectedHint(int w)
    {
        if (G.FontHint.Texture.Id == 0) return;

        // 添付指定: 見出し X=960/Y=750/32px/行間1.1/白文字・黒縁10
        DrawHintMultiline(_selectedHint.Title, new Vector2(w / 2f, 750), 32, 1.1f,
            Color.White, Color.Black, 7.5f);
        // 添付指定: 本文 X=960/Y=800/32px/行間1.75/黒文字・黒縁1
        DrawHintMultiline(_selectedHint.Body, new Vector2(w / 2f, 825), 32, 1.75f,
            Color.Black, Color.Black, 0f);
    }

    static void DrawHintMultiline(string text, Vector2 centerTop, float fontSize, float lineHeight,
        Color textColor, Color outlineColor, float outlineThickness)
    {
        if (string.IsNullOrEmpty(text)) return;
        string[] lines = text.Replace("\\n", "\n").Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            Vector2 size = Raylib.MeasureTextEx(G.FontHint, line, fontSize, 2);
            Vector2 position = new(centerTop.X - size.X / 2f, centerTop.Y + index * fontSize * lineHeight);
            G.DrawTextWithOutline16(G.FontHint, line, position, fontSize, textColor, outlineColor, outlineThickness);
        }
    }

    public static void Dispose() => _skin?.Dispose();
}
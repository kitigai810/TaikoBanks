using System;
using DiscordRPC;
using DiscordRPC.Logging;
public static class DiscordRpc
{
    // ⚠️ ここにDiscord Developer Portalで発行したClient IDを設定してください
    private const string APP_ID = "1536657629747875860";

    // デバッグ時に使う別アプリのClient ID(任意)。
    // 未設定(空文字)の場合はデバッグ時もAPP_IDをそのまま使い、Details/Stateの表示だけを変える。
    private const string APP_ID_DEBUG = "1536658042559406120";

    private static DiscordRpcClient _client;
    private static DateTime _sessionStart;
    private static bool _enabled = true; // 初期化に失敗した場合は自動でfalseになる

    /// <summary>
    /// trueの場合、RPCのDetails/Stateにデバッグ用の情報を付加して表示する。
    /// Program.cs側の起動時オプションやビルド設定(#if DEBUG等)に応じて切り替える想定。
    /// </summary>
    public static bool IsDebugMode { get; set; } = false;

    public static bool IsAvailable => _enabled && _client != null && !_client.IsDisposed;

    /// <summary>
    /// アプリ起動時に一度だけ呼ぶ。失敗しても例外は投げず、以降のRPC呼び出しは無視される。
    /// </summary>
    public static void Initialize()
    {
        // デバッグモードかつAPP_ID_DEBUGが設定されていれば、そちらのアプリに接続する
        string appId = (IsDebugMode && !string.IsNullOrEmpty(APP_ID_DEBUG)) ? APP_ID_DEBUG : APP_ID;

        if (string.IsNullOrEmpty(appId) || appId == "YOUR_DISCORD_APPLICATION_CLIENT_ID")
        {
            Console.WriteLine("[DiscordRPC] APP_IDが未設定のため無効化します。DiscordRpc.cs内のAPP_IDを設定してください。");
            _enabled = false;
            return;
        }

        try
        {
            _sessionStart = DateTime.UtcNow;

            // AutoEventsはコンストラクタでのみ指定可能な読み取り専用プロパティのため、ここでfalseを渡す
            // (Update()内で手動でInvoke()するため、自動呼び出しと衝突しないようにする)
            _client = new DiscordRpcClient(appId, autoEvents: false)
            {
                Logger = new ConsoleLogger(LogLevel.Warning, false)
            };

            _client.OnReady += (sender, e) =>
            {
                Console.WriteLine($"[DiscordRPC] 接続完了: {e.User.Username}");
            };

            _client.OnConnectionFailed += (sender, e) =>
            {
                Console.WriteLine("[DiscordRPC] 接続失敗。Discordクライアントが起動していない可能性があります。");
            };

            _client.Initialize();
            _enabled = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DiscordRPC] 初期化に失敗しました: {ex.Message}");
            _client = null;
            _enabled = false;
        }
    }

    /// <summary>
    /// メインループの中で毎フレーム(または数フレームに一度)呼ぶ。
    /// コールバック(OnReady等)を処理するために必要。
    /// </summary>
    public static void Update()
    {
        if (!IsAvailable) return;

        try
        {
            _client.Invoke();
        }
        catch (Exception ex)
        {
            // RPC更新中の例外(DiscordRPCライブラリ内部のバグ等)でゲーム本体を落とさない
            // 原因調査用に一度だけログを出す
            Console.WriteLine($"[DiscordRPC] Invoke中に例外が発生しました(無視して継続): {ex.Message}");
        }
    }

    private static void SetPresenceSafe(string details, string state, bool withElapsed = true)
    {
        if (!IsAvailable) return;

        try
        {
            // デバッグモード時はDetails/Stateにプレフィックスを付けて、
            // 通常表示と見分けが付くようにする。
            if (IsDebugMode)
            {
                details = $"[DEBUG] {details}";
                state = $"{state} (Debug Build)";
            }

            var presence = new RichPresence
            {
                Details = Truncate(details),
                State = Truncate(state)
            };

            if (withElapsed)
            {
                presence.Timestamps = new Timestamps(_sessionStart);
            }

            // DiscordRPCライブラリ(1.6.1系)にはAssetsマージ時にNullReferenceExceptionが
            // 発生するバグがある(1.6.2で修正予定だがまだ正式リリースされていない)。
            // ClearPresence()でCurrentPresenceを一旦nullに戻してから設定することで、
            // バグのあるマージ経路を通らないようにする。
            _client.ClearPresence();
            _client.SetPresence(presence);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DiscordRPC] SetPresence失敗: {ex.Message}");
        }
    }

    // Discord側の文字数制限(128文字)対策
    private static string Truncate(string s, int max = 128)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Length <= max ? s : s.Substring(0, max);
    }

    // ==================================================================
    // シーンごとのプリセット。Program.cs側からシーン遷移時に呼び出す想定。
    // largeImageKey / smallImageKey はDiscord Developer PortalのArt Assetsに
    // 事前アップロードしたキー名に合わせて適宜変更してください。
    // ==================================================================

    public static void SetTitle()
    {
        SetPresenceSafe(
            details: "タイトル画面",
            state: "メニューを操作中"
        );
    }

    public static void SetSongSelect()
    {
        SetPresenceSafe(
            details: "曲選択中",
            state: "太鼓の達人"
        );
    }

    public static void SetDifficultySelect(string songTitle)
    {
        SetPresenceSafe(
            details: "難易度選択中",
            state: songTitle
        );
    }

    public static void SetPlaying(string songTitle, string difficulty)
    {
        SetPresenceSafe(
            details: $"演奏中: {songTitle}",
            state: string.IsNullOrEmpty(difficulty) ? "プレイ中" : $"難易度: {difficulty}"
        );
    }

    public static void SetDanMode(string danTitle, int songIndex, int songCount)
    {
        SetPresenceSafe(
            details: $"段位道場: {danTitle}",
            state: $"{songIndex + 1}曲目 / 全{songCount}曲"
        );
    }

    public static void SetResult(string songTitle, int score)
    {
        SetPresenceSafe(
            details: "リザルト画面",
            state: $"{songTitle} (Score: {score:N0})",
            withElapsed: false
        );
    }

    public static void SetKisekae()
    {
        SetPresenceSafe(
            details: "きせかえ画面",
            state: "どんちゃんをきせかえ中"
        );
    }

    /// <summary>
    /// アプリ終了時に呼ぶ。呼ばないとDiscordにプレゼンスが残り続けることがある。
    /// </summary>
    public static void Shutdown()
    {
        if (_client == null) return;

        try
        {
            _client.ClearPresence();
            _client.Dispose();
        }
        catch
        {
            // 終了処理中の例外は無視
        }
        finally
        {
            _client = null;
        }
    }
}
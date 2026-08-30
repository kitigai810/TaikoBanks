using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// GitHubのプライベートリポジトリの最新Releaseを確認し、Zipアセットをダウンロードして
/// 指定フォルダへ展開(上書き更新)するためのクラス。
///
/// 使い方の例:
///
///   // トークンやリポジトリ名はこのファイル上部のDefault*定数にまとめてあるので、
///   // 呼び出し側はCreateDefault()を呼ぶだけでよい。
///   var updater = GitHubUpdater.CreateDefault();
///
///   var progress = new Progress&lt;double&gt;(p => Console.WriteLine($"{p:P0}"));
///
///   var result = await updater.CheckAndUpdateAsync(
///       currentVersion: Program.GameVersion,
///       destinationFolder: AppContext.BaseDirectory,
///       progress: progress
///   );
///
///   switch (result.Status)
///   {
///       case UpdateStatus.UpToDate:
///           Console.WriteLine("最新版です。");
///           break;
///       case UpdateStatus.Updated:
///           // 💡 実行中のexe自身は自プロセスからは上書きできないので、
///           //    ここではまだファイルは差し替わっていない(ステージング済みの状態)。
///           //    LaunchUpdaterBat()を呼ぶと、外部プロセスへコピー処理を
///           //    委譲したうえで自プロセスを終了する(戻ってこない)。
///           GitHubUpdater.LaunchUpdaterBat(result.StagingFolder!, destinationFolder);
///           break;
///       case UpdateStatus.Error:
///           Console.WriteLine($"更新に失敗しました: {result.ErrorMessage}");
///           break;
///   }
///
/// </summary>
public enum UpdateStatus
{
    UpToDate,
    Updated,
    Error
}

public class UpdateResult
{
    public UpdateStatus Status { get; set; }
    public string? LatestVersion { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Status == Updated のとき、展開済みファイル一式が置かれているステージングフォルダ。
    /// 実行中のexe/dllはロックされているため、ここへは直接destinationFolderへ展開せず、
    /// 一旦このフォルダに退避してある。GitHubUpdater.LaunchUpdaterBat() に
    /// このパスを渡すと、外部プロセスにコピー&再起動を委譲できる。
    /// </summary>
    public string? StagingFolder { get; set; }
}

public class GitHubUpdater
{
    // ==================================================================
    // 💡 更新設定はここ1箇所にまとめる。
    //    PATはこのファイル内にだけ書き、他のファイル(SettingsPanel.cs等)には
    //    一切登場させない。GitHubUpdater.csはビルド成果物(Zip配布物)に
    //    含めない/公開リポジトリにはpushしない、等の運用を徹底すること。
    // ==================================================================
    private const string DefaultOwner = "kitigai810";
    private const string DefaultRepo = "TaikoStormNeoUpDataer";
    private const string DefaultToken = "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"; // 💡 ここに新しく発行したPATを入れる(このファイルだけで完結させる)

    private const string DefaultAssetNamePattern = "TaikoStormNeo";

    /// <summary>
    /// デフォルト設定(上のDefault*定数)を使ってインスタンスを作る。
    /// 呼び出し側(SettingsPanel.cs等)はトークンを一切意識しなくてよい。
    /// </summary>
    public static GitHubUpdater CreateDefault() =>
        new GitHubUpdater(DefaultOwner, DefaultRepo, DefaultToken, DefaultAssetNamePattern);

    private readonly string _owner;
    private readonly string _repo;
    private readonly string _token;
    private readonly string? _assetNamePattern;
    private readonly HttpClient _apiClient;

    // GitHub API向け(リダイレクトを自動で追わせない: アセットDLは302先がAzure Blob等になり
    // Authorizationヘッダーを引き継ぐと逆に失敗する場合があるため手動制御する)
    private readonly HttpClient _downloadClient;

    public GitHubUpdater(string owner, string repo, string token, string? assetNamePattern = null)
    {
        _owner = owner;
        _repo = repo;
        _token = token;
        _assetNamePattern = assetNamePattern;

        // ── API呼び出し用クライアント(JSON取得) ──
        _apiClient = new HttpClient();
        _apiClient.DefaultRequestHeaders.UserAgent.ParseAdd($"{repo}-Updater/1.0"); // GitHub APIはUser-Agent必須
        _apiClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _apiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        _apiClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        // ── アセットダウンロード用クライアント ──
        // 302リダイレクトを自動で追わせない。理由:
        //   1回目 → api.github.com (Authorization: Bearer 必要)
        //   302後 → objects.githubusercontent.com (署名済みURL。Authorizationヘッダーを
        //           付けたまま転送すると 400 Bad Request になることがある)
        // そのため AllowAutoRedirect=false にして自前でリダイレクト先へは
        // 認証ヘッダーなしで再リクエストする。
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        _downloadClient = new HttpClient(handler);
        _downloadClient.DefaultRequestHeaders.UserAgent.ParseAdd($"{repo}-Updater/1.0");
        _downloadClient.Timeout = Timeout.InfiniteTimeSpan; // 進捗はProgressで見るので個別に長めのタイムアウトにする
    }

    private record ReleaseAsset(int Id, string Name, string Url, string BrowserDownloadUrl, long Size);
    private record ReleaseInfo(string TagName, ReleaseAsset[] Assets);

    /// <summary>
    /// 最新Release情報を取得する。
    /// エンドポイント: GET https://api.github.com/repos/{owner}/{repo}/releases/latest
    /// </summary>
    private async Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken ct)
    {
        string url = $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest";

        using var response = await _apiClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode(); // 404: Releaseが1件も無い/リポジトリ名やトークン権限を確認

        string json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string tagName = root.GetProperty("tag_name").GetString() ?? "";

        var assetsElement = root.GetProperty("assets");
        var assets = new ReleaseAsset[assetsElement.GetArrayLength()];
        int i = 0;
        foreach (var a in assetsElement.EnumerateArray())
        {
            assets[i++] = new ReleaseAsset(
                Id: a.GetProperty("id").GetInt32(),
                Name: a.GetProperty("name").GetString() ?? "",
                // "url" はAPI用アセットURL(こちらを Accept: application/octet-stream 付きで叩く)
                Url: a.GetProperty("url").GetString() ?? "",
                // "browser_download_url" はプライベートリポジトリでは認証なしだと使えないので参考値のみ
                BrowserDownloadUrl: a.GetProperty("browser_download_url").GetString() ?? "",
                Size: a.GetProperty("size").GetInt64()
            );
        }

        return new ReleaseInfo(tagName, assets);
    }

    /// <summary>
    /// バージョン文字列を単純な数値配列として比較する ("01.23" のようなドット区切りに対応)。
    /// tag_name が "v1.2.3" のように先頭にプレフィックスが付く場合は数字以外を除去してから比較する。
    /// 要件が単純なゼロ埋め2桁文字列("00.00")なので、数値変換して比較する簡易実装。
    /// </summary>
    public static bool IsNewer(string latestTag, string currentVersion)
    {
        static double[] Parse(string s)
        {
            // 先頭の 'v' やプレフィックスを除去し、数字とドットのみ残す
            var cleaned = new string(s.Trim().TrimStart('v', 'V').ToCharArray());
            var parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var nums = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                double.TryParse(parts[i], out nums[i]);
            }
            return nums;
        }

        var latest = Parse(latestTag);
        var current = Parse(currentVersion);
        int len = Math.Max(latest.Length, current.Length);

        for (int i = 0; i < len; i++)
        {
            double l = i < latest.Length ? latest[i] : 0;
            double c = i < current.Length ? current[i] : 0;
            if (l > c) return true;
            if (l < c) return false;
        }
        return false; // 完全一致 = 新しくない
    }

    /// <summary>
    /// 最新Releaseの確認 → 新しければZipをダウンロード → 展開(上書き)まで一括で行う。
    /// </summary>
    /// <param name="currentVersion">現在のアプリバージョン(例: Program.GameVersion)</param>
    /// <param name="destinationFolder">展開先フォルダ(通常はアプリの実行フォルダ)</param>
    /// <param name="progress">0.0〜1.0の進捗通知(ダウンロード部分のみ)</param>
    /// <param name="ct">キャンセル用トークン</param>
    public async Task<UpdateResult> CheckAndUpdateAsync(
        string currentVersion,
        string destinationFolder,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            ReleaseInfo release = await GetLatestReleaseAsync(ct);

            if (!IsNewer(release.TagName, currentVersion))
            {
                return new UpdateResult { Status = UpdateStatus.UpToDate, LatestVersion = release.TagName };
            }

            // 💡 Releaseにアップロードされているアセットを全部対象にする。
            //    ("Source code (zip)"/"(tar.gz)"はGitHubの自動生成リンクであり、
            //     そもそも assets 配列には含まれないので、除外を気にしなくてよい)
            if (release.Assets.Length == 0)
            {
                return new UpdateResult
                {
                    Status = UpdateStatus.Error,
                    ErrorMessage = "Releaseにアセットがアップロードされていません。",
                    LatestVersion = release.TagName
                };
            }

            // 💡 実行中のexeやロード済みdllは自プロセスからは上書きできないため、
            //    destinationFolderへ直接展開するのではなく、一旦ステージングフォルダへ
            //    展開しておく。実際の上書き＆再起動は LaunchUpdaterBat() で
            //    外部プロセス(バッチ)に委譲する。
            string tempDir = Path.Combine(Path.GetTempPath(), $"update_{Guid.NewGuid():N}");
            string stagingDir = Path.Combine(tempDir, "staged");
            Directory.CreateDirectory(stagingDir);

            try
            {
                long totalSize = 0;
                foreach (var a in release.Assets) totalSize += Math.Max(a.Size, 1);
                long doneSize = 0;

                foreach (var a in release.Assets)
                {
                    long assetSize = Math.Max(a.Size, 1);

                    // 複数アセットぶん通しての進捗(0.0〜1.0)を合成して報告する
                    var assetProgress = new Progress<double>(p =>
                    {
                        double overall = (doneSize + p * assetSize) / (double)totalSize;
                        progress?.Report(overall);
                    });

                    if (a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        // Zipはダウンロード後にステージングフォルダへ展開する(destinationFolderには触れない)
                        string tempZipPath = Path.Combine(tempDir, a.Name);
                        await DownloadAssetAsync(a, tempZipPath, assetProgress, ct);
                        ZipFile.ExtractToDirectory(tempZipPath, stagingDir, overwriteFiles: true);
                        File.Delete(tempZipPath);
                    }
                    else
                    {
                        // Zip以外(default.txt等)もステージングフォルダに同名で配置する
                        string destPath = Path.Combine(stagingDir, a.Name);
                        string? destDir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                        await DownloadAssetAsync(a, destPath, assetProgress, ct);
                    }

                    doneSize += assetSize;
                }

                // 注意: ここではtempDir(=stagingDirの親)を削除しない。
                // LaunchUpdaterBat() 側のバッチスクリプトがコピー完了後に削除する。
                return new UpdateResult
                {
                    Status = UpdateStatus.Updated,
                    LatestVersion = release.TagName,
                    StagingFolder = stagingDir
                };
            }
            catch
            {
                // 展開に失敗した場合のみここで後始末する(成功時はバッチ側が消す)
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
                throw;
            }
        }
        catch (Exception ex)
        {
            return new UpdateResult { Status = UpdateStatus.Error, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// ステージング済みの更新ファイル(CheckAndUpdateAsyncが返したUpdateResult.StagingFolder)で
    /// destinationFolderを上書きし、アプリを再起動する。
    ///
    /// 実行中のexe/dllは自プロセスが握っている間はロックされていて直接上書きできないため、
    /// 外部のcmdバッチプロセスを1つ起動し、
    ///   1. このプロセス(PID)の終了を待つ
    ///   2. robocopyでstagingFolder → destinationFolderへ上書きコピー
    ///   3. 更新後のexeを再起動
    ///   4. 一時フォルダとバッチ自身を削除
    /// という流れを委譲する。この呼び出し後、自プロセスはEnvironment.Exit(0)で終了するため
    /// 戻ってこない(呼び出し側で追加処理は書けない)。
    /// </summary>
    /// <param name="stagingFolder">UpdateResult.StagingFolder</param>
    /// <param name="destinationFolder">上書き先(通常はAppContext.BaseDirectory)</param>
    /// <param name="exeToRestart">再起動する実行ファイルのフルパス。省略時は現在実行中のexeを自動取得</param>
    public static void LaunchUpdaterBat(string stagingFolder, string destinationFolder, string? exeToRestart = null)
    {
        exeToRestart ??= Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("再起動対象のexeパスを特定できませんでした。exeToRestartを明示的に指定してください。");

        int currentPid = Environment.ProcessId;
        string tempParent = Directory.GetParent(stagingFolder)?.FullName ?? Path.GetTempPath();
        string batPath = Path.Combine(Path.GetTempPath(), $"apply_update_{Guid.NewGuid():N}.bat");

        // tasklistでPIDの生存確認 → 消えるまで1秒間隔でポーリング → robocopyで上書き → 再起動 → 自己削除
        // robocopy /E: 空フォルダ含め全コピー /IS /IT: 同一/古いファイルでも上書き対象にする
        // /R:10 /W:1: ファイルロック解除待ちで最大10回・1秒間隔リトライ(直後は旧exeが完全解放されるまで僅かにラグがあるため)
        string script =
            "@echo off\r\n" +
            "setlocal\r\n" +
            ":waitloop\r\n" +
            $"tasklist /FI \"PID eq {currentPid}\" 2>NUL | find \"{currentPid}\" >NUL\r\n" +
            "if not errorlevel 1 (\r\n" +
            "    timeout /t 1 /nobreak >NUL\r\n" +
            "    goto waitloop\r\n" +
            ")\r\n" +
            $"robocopy \"{stagingFolder}\" \"{destinationFolder}\" /E /IS /IT /R:10 /W:1 /NFL /NDL /NJH /NJS\r\n" +
            $"start \"\" \"{exeToRestart}\"\r\n" +
            $"rmdir /s /q \"{tempParent}\" 2>NUL\r\n" +
            "del \"%~f0\"\r\n";

        File.WriteAllText(batPath, script, System.Text.Encoding.Default);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{batPath}\"",
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        Process.Start(psi);

        // ここで自プロセスを終了させ、exe/dllのファイルロックを解放する。
        // (Environment.Exitなのでこの後にコードは実行されない)
        Environment.Exit(0);
    }

    /// <summary>
    /// アセットをストリームでダウンロードする。
    /// GitHub APIのアセットDLエンドポイント( .../releases/assets/{id} )に
    /// Accept: application/octet-stream を付けて叩くと、302で実体のURLへリダイレクトされる。
    /// プライベートリポジトリの場合は Authorization ヘッダーが必須(1回目のみ)。
    /// </summary>
    private async Task DownloadAssetAsync(ReleaseAsset asset, string savePath, IProgress<double>? progress, CancellationToken ct)
    {
        // 1回目: GitHub APIへ直接リクエスト。Authorizationヘッダー必須。
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.UserAgent.ParseAdd($"{_repo}-Updater/1.0");

        using var response = await _downloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        // 302等のリダイレクトを検知したら、Locationへ「認証ヘッダーなし」で再リクエストする
        HttpResponseMessage finalResponse = response;
        if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
        {
            var redirectUrl = response.Headers.Location;
            if (redirectUrl == null)
                throw new HttpRequestException("リダイレクト先URLが取得できませんでした。");

            using var redirectRequest = new HttpRequestMessage(HttpMethod.Get, redirectUrl);
            redirectRequest.Headers.UserAgent.ParseAdd($"{_repo}-Updater/1.0");
            // ここでは意図的にAuthorizationヘッダーを付けない(署名済みURLのため不要かつ
            // 付けると失敗するホスト先が多い)

            finalResponse = await _downloadClient.SendAsync(redirectRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        }

        using (finalResponse)
        {
            finalResponse.EnsureSuccessStatusCode();

            long? totalBytes = finalResponse.Content.Headers.ContentLength ?? asset.Size;
            long readBytes = 0;

            await using var httpStream = await finalResponse.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);

            var buffer = new byte[81920];
            int read;
            while ((read = await httpStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                readBytes += read;

                if (totalBytes is > 0)
                {
                    progress?.Report((double)readBytes / totalBytes.Value);
                }
            }
        }
    }

}
using Raylib_cs;
using System.Numerics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Text;
using System.Globalization;
using System.Threading.Tasks;

/// <summary>
/// 選曲画面（ジャンル/フォルダのナビゲーション、TJA・box.defのロード、選曲画面の描画、BGM/効果音）を
/// すべて内包するクラス。Program 側はシーン制御のための呼び出しと、選曲結果の参照のみを行う。
/// </summary>
public static class SongSelectScene
{
    // ==================================================================
    // 💡 どんちゃん(DonChan)とネームプレート(NamePlate)の表示位置調整用パラメータ
    //    （数値を直接書き換えるだけで調整できます）
    // ==================================================================
    public static float DonChanX = -225f;         // どんちゃんの左端X
    public static float DonChanBottomY = 1350f;   // どんちゃんの下端Y
    public static float DonChanWidth = 980f;      // どんちゃんの幅

    public static float NamePlateGlobalX = 30f;   // ネームプレート全体のX
    public static float NamePlateGlobalY = 970f; // ネームプレート全体のY（どんちゃんの下端Yより下にする）

    // ==================================================================
    // 💡 操作ガイドアニメーション (Lumen/0.Songs/SongSelect/Control/Anime.aup2) 表示調整用パラメータ
    // =========================================
    // =========================
    private static Aup2Anim _animeGuide;
    private static float _animeGuideTime = 0f;
    public static float AnimeGuideCenterX = 0.075f;   // 表示中心X (vw比率、外部から変更可)
    public static float AnimeGuideCenterY = 0.15f;   // 表示中心Y (vh比率、外部から変更可)
    public static float AnimeGuideWidth = 350f;     // 表示幅(px, 1280x720基準、外部から変更可)。高さは元の縦横比を保って自動計算


    private static float _diffNumScale = 1f;         // 数字全体の基本大きさ（倍率）
    private static float _diffNumOffsetX = 20f;          // 数字の描画位置のX方向オフセット（星エリア基準）
    private static float _diffNumOffsetY = 0f;          // 数字の描画位置のY方向オフセット
    private static float _diffNumSpacing = -8f;         // 2桁以上の数字同士の間隔（マイナス値で少し重なりを表現）
    private static float _diffNumWidthScale10Max = 0.8f; // ★10以上の場合に数字を横に細くする比率（1.0fで通常、小さくするほど細くなります）

    // ==================================================================
    // 💡 曲名／ジャンル名タイトルのフォントサイズ調整用パラメータ
    // ==================================================================
    private static float _titleFontScaleUnsel = 1f;   // 非選択時のフォントサイズ倍率（1.0fで選択時と同じ、小さくするほど縮小）

    // 💡 AC実機ではジャンル(Box)の文字サイズとSongsの文字サイズが異なるため、
    //    ボックス種別ごとに基準フォントサイズ(px)を直接指定できるようにする
    public static float GenreTitleFontSize = 57.5f;    // ジャンル(Box)選択時のタイトル基準フォントサイズ(px)
    public static float SongTitleFontSize = 42.5f;     // 曲(Song)選択時のタイトル基準フォントサイズ(px)
    public static float GenreTitleOutlineThickness = 8f; // ジャンル(Box)選択時のタイトル縁取りの太さ
    public static float SongTitleOutlineThickness = 8f;  // 曲(Song)選択時のタイトル縁取りの太さ

    // ==================================================================
    // 💡 説明文（EXPLANATION）の位置・大きさ調整用パラメータ
    //    （オーバーレイ上に表示される説明テキストの調整はここを変更してください）
    // ==================================================================
    private static float _explanationOffsetX = 0f;        // 基準位置からのX方向オフセット
    private static float _explanationOffsetY = 175f;        // 基準位置からのY方向オフセット
    private static float _explanationFontSize = 34f;      // フォントサイズ
    private static float _explanationLineSpacing = 50f;   // 行間（複数行の場合）

    // ==================================================================
    // 💡 ベストスコアアイコン（クラウン・スコアランク）の表示調整用パラメータ
    // ==================================================================
    public static float BestIconHeightPx = 65f;  // アイコンの高さ(px, 1280x720基準)。選択中・非選択中で常に同じ大きさになる
    public static float BestIconOffsetX = -610f;     // アイコン全体のX方向オフセット(px, 1280x720基準)
    public static float BestIconOffsetYSel = -0.475f;  // 選択中: バー中央からのY方向オフセット(barTotalH比率)。ここを変えると選択中アイコン全体の縦位置を調整できる
    public static float BestIconOffsetYUnsel = -0.45f;     // 非選択: バー中央からのY方向オフセット(totalH_unsel比率)
    public static float BestIconSpacing = -10f;     // クラウンとスコアランク間の隙間(px, 1280x720基準)
    public static float BestIconGrowDuration = 0.1f;  // フォルダ/曲を選択してから、非選択時→選択時の大きさ・位置へ変化するのにかかる時間（秒）

    // ==================================================================
    // 💡 選択中バーに重ねて表示する Bar_Select.png の表示調整用パラメータ
    // ==================================================================
    private static float _selectedWidthScale = 1.125f;   // 全体の横幅倍率（Bar_Select移行に伴い、バーより横に大きくなりすぎないよう縮小）
    private static float _selectedHeightScale = 1.25f;   // 縦幅倍率（Y方向だけ個別調整可能）
    private static float _selectedOffsetX = 0f;         // 横方向オフセット
    private static float _selectedOffsetY = 0f;         // 縦方向オフセット

    // ==================================================================
    // 💡 Bar_Select.png（TJAPlayer-Nijiiro の SongSelect_Bar_Select を移植）
    //    「点滅ストロボ → 常時呼吸フェード」のアニメーション状態管理
    //    画像は縦3分割のスプライトシート想定（TaikoBank独自に新規追加するアセット）：
    //      区画0(y: 0        〜 H/3  ) = 成長レイヤー用
    //      区画1(y: H/3      〜 2H/3 ) = 呼吸レイヤー用
    //      区画2(y: 2H/3     〜 H    ) = ストロボレイヤー用
    //    各区画はさらに縦4等分し、上端キャップ=1/4・伸縮中央=2/4・下端キャップ=1/4として使用する
    //    （Nijiiro側の barSelect_height = Height/3, height = barSelect_height/4 と同じ考え方）
    // ==================================================================
    const float BAR_FLASH_DURATION_MS = 2700f;   // Nijiiro ctBarFlash と同じ総尺(ms)
    const float SELECT_FADE_LOOP_MS = 1000f;     // 呼吸フェードの1周期(ms)

    static float _barFlashElapsedMs = BAR_FLASH_DURATION_MS; // 起動直後は「点滅済み＝呼吸フェードのみ」扱い
    static bool _barFlashActive = false;                     // true の間は0→2700msをカウントしてストロボ演出中
    static float _selectFadeElapsedMs = 0f;                  // 呼吸フェード用ループタイマー(0〜1000msを繰り返す)

    // ==================================================================
    // 💡 オーバーレイ画像（Overlay.png）の表示・アニメーション調整用パラメータ
    //    （選択中 (Sel) と非選択 (Unsel) の位置や大きさをそれぞれ個別に微調整可能にしました）
    // ==================================================================
    // 📂 ジャンル（フォルダ・Box）選択時のオーバーレイ設定
    private static float _genreOverlayWidthScaleSel = 1.0f;     // 選択中（拡がった状態）の横幅倍率
    private static float _genreOverlayHeightScaleSel = 0.7f;    // 選択中（拡がった状態）の縦幅倍率（Y変動追従）
    private static float _genreOverlayOffsetXSel = 0f;          // 選択中の横方向オフセット
    private static float _genreOverlayOffsetYSel = 55f;          // 選択中の縦方向オフセット

    private static float _genreOverlayWidthScaleUnsel = 1f;  // 非選択時（普通の状態）の横幅倍率
    private static float _genreOverlayHeightScaleUnsel = 1f; // 非選択時（普通の状態）の縦幅倍率
    private static float _genreOverlayOffsetXUnsel = 0f;        // 非選択時の横方向オフセット
    private static float _genreOverlayOffsetYUnsel = 0f;        // 非選択時の縦方向オフセット

    // 🎵 曲（Song）選択時のオーバーレイ設定
    private static float _songOverlayWidthScaleSel = 1.0f;      // 選択中（拡がった状態）の横幅倍率
    private static float _songOverlayHeightScaleSel = 1f;     // 選択中（拡がった状態）の縦幅倍率（Y変動追従）
    private static float _songOverlayOffsetXSel = 0f;           // 選択中の横方向オフセット
    private static float _songOverlayOffsetYSel = 0f;           // 選択中の縦方向オフセット

    private static float _songOverlayWidthScaleUnsel = 1f;   // 非選択時（普通の状態）の横幅倍率
    private static float _songOverlayHeightScaleUnsel = 1f;  // 非選択時（普通の状態）の縦幅倍率
    private static float _songOverlayOffsetXUnsel = 0f;         // 非選択時の横方向オフセット
    private static float _songOverlayOffsetYUnsel = 0f;         // 非選択時の縦方向オフセット

    // ==================================================================
    // 💡 ScorePanel.png（Data/PlayData.json の "ScoreBoard" 値で切り替える成績パネル）表示調整用パラメータ
    //    ScorePanel.png は縦に5枚積まれたスプライトシート（0番目が一番上）。
    //    "ScoreBoard": 3 なら上から4番目(0始まり)のパネルを表示する。
    // ==================================================================
    public static float ScorePanelCenterX = 0.1f;   // 表示中心X (vw比率、外部から変更可)
    public static float ScorePanelCenterY = 0.4f;   // 表示中心Y (vh比率、外部から変更可)
    public static float ScorePanelWidth = 375f;      // 表示幅(px, scaleFactor倍率で使用)。高さは元の縦横比を保って自動計算

    // ==================================================================
    // 💡 ScorePanel_Number.png（0〜9の数字が横一列に並んだスプライトシート）を
    //    ScorePanel.png の上に重ねて、全曲スキャンで集計した「スコアランク○の曲数」
    //    「王冠○の曲数」を描画するための調整用パラメータ。
    //    グリッド配置（3列×4行、ScorePanelの中心を基準にしたpxオフセット）:
    //      row0:  (空白)      (空白)      ランク6の数
    //      row1:  ランク3の数  ランク4の数  ランク5の数
    //      row2:  ランク0の数  ランク1の数  ランク2の数
    //      row3:  王冠0の数    王冠1の数    王冠2の数
    // ==================================================================
    public static float NumberGridOffsetX = 15f;       // ScorePanel中心からのXオフセット(px)
    public static float NumberGridOffsetY = -65f;     // ScorePanel中心からのYオフセット(px)
    public static float NumberGridColSpacing = 105f;  // 列間隔(px)
    public static float NumberGridRowSpacing = 42.5f; // 行間隔(px)
    public static float NumberDigitScale = 1.1f;        // 数字スプライトの描画スケール
    public static float NumberDigitGap = -5f;         // 桁同士の間隔(px)

    // ==================================================================
    // 💡 Program 側から呼び出す公開API（Init/Update/Draw/Unload と、シーン遷移用の状態）
    // ==================================================================

    private static bool _wantsKisekae = false;
    private static bool _songConfirmed = false;

    // ==================================================================
    // 💡 Box(ジャンル)決定時の「待機→Xスケール0まで縮小→Songの箱に切替→Xスケールを元に戻す」演出用パラメータ
    // ==================================================================
    private static bool _boxTransitionActive = false;
    private static float _boxTransitionTimer = 0f;
    private static BoxEntry _boxTransitionEntry = null;
    private static bool _boxTransitionSwitched = false;
    private const float BOX_TRANS_WAIT = 0f;      // 決定してから縮小を始めるまでの待機時間（秒）
    private const float BOX_TRANS_SHRINK = 0.35f; // Xスケールを1→0にする時間（散開時間に合わせる）
    private const float BOX_TRANS_EXPAND = 0.40f; // Xスケールを0→1に戻す時間（HOLD+収束時間に合わせる）

    // ==================================================================
    // 💡 フォルダを「閉じる」演出用パラメータ（Escキー / もどるエントリ決定時）
    //    「縮小→ExitDirectory()呼び出し→拡大」という開くアニメの逆方向版。
    //    _boxCloseXScale は現在のXスケール値で、開く演出の _boxTransitionXScale と同じ役割。
    // ==================================================================
    private static bool _boxCloseActive = false;
    private static float _boxCloseTimer = 0f;
    private const float BOX_CLOSE_SHRINK = 0.35f; // Xスケールを1→0にする時間（散開時間に合わせる）
    private const float BOX_CLOSE_EXPAND = 0.40f; // Xスケールを0→1に戻す時間（HOLD+収束時間に合わせる）
    private static bool _boxCloseExitDone = false; // ExitDirectory()をすでに呼んだかどうか

    /// <summary>F2キーでどんちゃん着せ替え(Kisekae)への遷移が要求されたか。Program側で読んだ後は ResetTransitionFlags() でクリアすること。</summary>
    public static bool WantsKisekae => _wantsKisekae;

    /// <summary>曲・難易度対象の曲が確定し、難易度選択画面へ遷移すべきか。</summary>
    public static bool SongConfirmed => _songConfirmed;

    /// <summary>Escキー2度押し等でゲーム終了が要求されたか。</summary>
    public static bool ShouldExit => _shouldExit;

    /// <summary>選択された曲データ（難易度選択・リザルト画面から参照される）。</summary>
    public static SongData SelectedSong => _selectedSong;

    /// <summary>選択された曲のジャンル（難易度選択・リザルト画面から参照される）。</summary>
    public static string SelectedSongGenre => _selectedSongGenre;

    /// <summary>スキン（Lumen）のルートパス。Result画面のBGM読み込み等,Program側からも参照される。</summary>
    public static string SkinRoot => _skinRoot;

    /// <summary>曲データ（box.def群）のルートパス。フォント初期化時のExplanation文字スキャン等,Program側からも参照される。</summary>
    public static string SongsRoot => _songsRoot;

    /// <summary>設定画面などから曲リストを再構築する。ナビスタックをリセットしてルートから読み直す。</summary>
    public static void ReloadSongs()
    {
        StopSongPreview();
        _pendingPreviewSong = null;
        _songsRoot = ResolveSongsRoot();
        File.AppendAllText("songs_debug.txt", $"[ReloadSongs] SongsType={SettingsPanel.SongsType} _songsRoot={_songsRoot}\n");
        LoadSongList();
        UpdateBgForFocus();
    }

    // ---- 曲検索ジャンプ ----
    // SettingsPanel から検索クエリをセットすると、次フレームの Update() で該当曲へカーソルを移動する
    private static string _pendingSearchQuery = null;
    public static bool HasPendingSearch => _pendingSearchQuery != null || _pendingSearchPath != null;

    /// <summary>
    /// 曲タイトルを部分一致で検索し、最初にヒットした曲へカーソルを移動する。
    /// 選曲画面がアクティブでない間も呼べる（次のUpdate()で反映される）。
    /// </summary>
    public static void RequestSearchJump(string query)
    {
        _pendingSearchQuery = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
    }

    // ---- 検索候補（TjaPathを直接指定してジャンプ） ----
    private static string _pendingSearchPath = null;

    /// <summary>検索候補一覧から選んだ曲へ、TjaPathを直接指定してジャンプする。</summary>
    public static void RequestSearchJumpToPath(string tjaPath)
    {
        _pendingSearchPath = string.IsNullOrWhiteSpace(tjaPath) ? null : tjaPath;
    }

    /// <summary>検索候補データ。SettingsPanel等の検索UIに表示するための最小限の情報。</summary>
    public class SearchCandidate
    {
        public string TjaPath;
        public string DisplayText; // 例: "夏祭り ~OffVocal~[キッズ]"
    }

    /// <summary>
    /// タイトル・サブタイトルの部分一致でジャンル付き候補を最大maxResults件返す。
    /// 曲リスト全体を（現在表示中の階層に関わらず）再帰的に検索する。
    /// </summary>
    public static List<SearchCandidate> GetSearchCandidates(string query, int maxResults = 6)
    {
        var results = new List<SearchCandidate>();
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrEmpty(_songsRoot)) return results;
        string q = query.Trim();
        CollectSearchCandidates(_songsRoot, q, results, maxResults);
        return results;
    }

    static void CollectSearchCandidates(string dir, string q, List<SearchCandidate> results, int maxResults)
    {
        if (results.Count >= maxResults) return;
        var entries = BuildEntries(dir);
        foreach (var e in entries)
        {
            if (results.Count >= maxResults) return;

            if (e is SongEntry se)
            {
                AddCandidateIfMatch(se.Song, se.OwnerDef?.Genre, q, results);
            }
            else if (e is BoxEntry be)
            {
                if (be.VirtualTjaPaths != null && be.VirtualTjaPaths.Count > 0)
                {
                    var virtualSongs = ParseSongDataBatch(be.VirtualTjaPaths);
                    foreach (var song in virtualSongs)
                    {
                        if (results.Count >= maxResults) return;
                        AddCandidateIfMatch(song, be.Def?.Genre, q, results);
                    }
                }
                else if (!string.IsNullOrEmpty(be.FolderPath))
                {
                    CollectSearchCandidates(be.FolderPath, q, results, maxResults);
                }
            }
        }
    }

    static void AddCandidateIfMatch(SongData song, string ownerGenre, string q, List<SearchCandidate> results)
    {
        string titleJp = song.TitleJP ?? "";
        string title = song.Title ?? "";
        string subJp = song.SubtitleJP ?? "";
        string sub = song.Subtitle ?? "";
        string haystack = string.Join("|", titleJp, title, subJp, sub);
        if (!haystack.Contains(q, StringComparison.OrdinalIgnoreCase)) return;

        string displayTitle = !string.IsNullOrEmpty(titleJp) ? titleJp : title;
        string displaySub = !string.IsNullOrEmpty(subJp) ? subJp : sub;
        if (!string.IsNullOrEmpty(displaySub)) displayTitle += " " + displaySub;

        string genre = !string.IsNullOrEmpty(song.Genre) ? song.Genre : (ownerGenre ?? "");
        string display = string.IsNullOrEmpty(genre) ? displayTitle : $"{displayTitle}[{genre}]";

        results.Add(new SearchCandidate { TjaPath = song.TjaPath, DisplayText = display });
    }

    /// <summary>指定したTjaPathを持つ曲を、階層を辿りながら深さ優先で探し、実際にジャンプする。</summary>
    static void ApplyPendingSearchPath()
    {
        if (_pendingSearchPath == null) return;
        string target = _pendingSearchPath;
        _pendingSearchPath = null;

        UnloadAllNavAssetsAndClearStack();
        _currentDir = _songsRoot;
        _currentEntries = BuildEntries(_currentDir);
        _currentDirGenre = GetCurrentDirBoxDef()?.Genre ?? "";
        _cursor = 0;
        _boxAnimTimer = 99f;
        ResetSongScroll();

        // 最大10階層まで掘り下げる（通常は2〜3階層程度のはず）
        for (int depth = 0; depth < 10; depth++)
        {
            int foundIdx = _currentEntries.FindIndex(e => e is SongEntry se && se.Song.TjaPath == target);
            if (foundIdx >= 0)
            {
                _cursor = foundIdx;
                UpdateBgForFocus();
                return;
            }

            int boxIdx = -1;
            bool useVirtual = false;
            for (int i = 0; i < _currentEntries.Count; i++)
            {
                if (_currentEntries[i] is BoxEntry be)
                {
                    if (be.VirtualTjaPaths != null && be.VirtualTjaPaths.Contains(target))
                    {
                        boxIdx = i;
                        useVirtual = true;
                        break;
                    }
                    if (!string.IsNullOrEmpty(be.FolderPath) && DirContainsTjaPath(be.FolderPath, target))
                    {
                        boxIdx = i;
                        useVirtual = false;
                        break;
                    }
                }
            }

            if (boxIdx < 0) break; // 見つからない

            _cursor = boxIdx;
            if (useVirtual)
                EnterVirtualDirectory((BoxEntry)_currentEntries[boxIdx]);
            else
                EnterDirectory(((BoxEntry)_currentEntries[boxIdx]).FolderPath);
        }

        UpdateBgForFocus();
    }

    static bool DirContainsTjaPath(string dir, string targetTjaPath)
    {
        var entries = BuildEntries(dir);
        foreach (var e in entries)
        {
            if (e is SongEntry se && se.Song.TjaPath == targetTjaPath) return true;
            if (e is BoxEntry be)
            {
                if (be.VirtualTjaPaths != null && be.VirtualTjaPaths.Contains(targetTjaPath)) return true;
                if (!string.IsNullOrEmpty(be.FolderPath) && DirContainsTjaPath(be.FolderPath, targetTjaPath)) return true;
            }
        }
        return false;
    }

    static void ApplyPendingSearch()
    {
        if (_pendingSearchQuery == null) return;
        string q = _pendingSearchQuery;
        _pendingSearchQuery = null;

        // ナビスタックをリセットしてルートから再検索
        UnloadAllNavAssetsAndClearStack();
        _currentDir = _songsRoot;
        _currentEntries = BuildEntries(_currentDir);
        _currentDirGenre = GetCurrentDirBoxDef()?.Genre ?? "";
        _cursor = 0;
        _boxAnimTimer = 99f;
        ResetSongScroll();

        // 全エントリをフラット検索(現在レベルのみ)
        for (int i = 0; i < _currentEntries.Count; i++)
        {
            if (_currentEntries[i] is SongEntry se)
            {
                string t = (se.Song.TitleJP ?? "") + "|" + (se.Song.Title ?? "");
                if (t.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    _cursor = i;
                    UpdateBgForFocus();
                    return;
                }
            }
            else if (_currentEntries[i] is BoxEntry be)
            {
                // フォルダへ降りて再帰検索する
                var children = BuildEntries(be.FolderPath);
                foreach (var child in children)
                {
                    if (child is SongEntry childSe)
                    {
                        string t = (childSe.Song.TitleJP ?? "") + "|" + (childSe.Song.Title ?? "");
                        if (t.Contains(q, StringComparison.OrdinalIgnoreCase))
                        {
                            // フォルダを開いてカーソル移動
                            EnterDirectory(be.FolderPath);
                            int idx = _currentEntries.FindIndex(e => e is SongEntry s &&
                                (s.Song.TjaPath == childSe.Song.TjaPath));
                            if (idx >= 0) _cursor = idx;
                            UpdateBgForFocus();
                            return;
                        }
                    }
                }
            }
        }
        // ヒットなし → ルートのまま
        UpdateBgForFocus();
    }

    /// <summary>WantsKisekae / SongConfirmed を読んでシーン遷移した後、Program側が呼んでフラグをクリアする。</summary>
    public static void ResetTransitionFlags()
    {
        _wantsKisekae = false;
        _songConfirmed = false;
    }

    /// <summary>リザルト画面のタイトル表示用。選択中の曲が無い場合は「不明な曲」を返す。</summary>
    public static string GetSelectedSongDisplayTitle()
    {
        if (_selectedSong == null) return "不明な曲";
        bool wantJapanese = SettingsPanel.IsLang;
        if (wantJapanese)
            return !string.IsNullOrEmpty(_selectedSong.TitleJP) ? _selectedSong.TitleJP : _selectedSong.Title;
        return _selectedSong.Title;
    }

    /// <summary>アプリ起動時に一度だけ呼ぶ。アセット読み込み・曲リスト構築・メニューBGM再生開始まで一括で行う。</summary>
    public static void Init()
    {
        // 💡 [修正] 「SettingsPanel.Load()が先に終わっている」という暗黙の実行順序に依存していたのが
        //    ずっとSongsTypeがTJA(既定値)のまま_songsRootが確定してしまっていた原因の一つだった
        //    (根本原因はSettingsPanel側のトグルでSave()を呼んでいなかったこと。そちらも修正済み)。
        //    保険として明示的にLoad()し直してからSongsTypeを確定させる。Load()は何度呼んでも安全。
        SettingsPanel.Load();
        _songsRoot = ResolveSongsRoot();
        File.AppendAllText("songs_debug.txt", $"[Init] _songsRoot={_songsRoot}\n");

        // 💡 デフォルトのバー画像（角丸を含む1枚絵。bar.pngのみで管理し、上下の角丸部分を
        //    描画時に自動でスライスして使う。barup.png/bardown.pngはもう読み込まない）
        _texDefBar = Raylib.LoadTexture(Path.Combine(_skinRoot, "0.Songs", "category", "0.other", "bar.png"));
        _defBarLoaded = _texDefBar.Id != 0;

        // 💡 星画像の代わりに Difficulty_Level_Number.png を指定フォルダから読み込む
        _texStar = Raylib.LoadTexture(Path.Combine(_skinRoot, "0.Songs", "difficulty_bar", "Difficulty_Level_Number.png"));
        _starLoaded = _texStar.Id != 0;

        _texHeader = Raylib.LoadTexture(Path.Combine(_skinRoot, "0.Songs", "Header.png"));
        _headerLoaded = _texHeader.Id != 0;

        // 💡 バーに載せるオーバーレイ画像のロード
        _texOverlay = Raylib.LoadTexture(Path.Combine(_skinRoot, "0.Songs", "Overlay.png"));
        _overlayLoaded = _texOverlay.Id != 0;

        // 💡 TJAPlayer-Nijiiro の Bar_Select.png 相当（縦3分割スプライトシート）。
        //    未用意の場合は _barSelectLoaded=false のままとなり、ハイライト演出は描画されない。
        _texBarSelect = Raylib.LoadTexture(Path.Combine(_skinRoot, "0.Songs", "Bar_Select.png"));
        _barSelectLoaded = _texBarSelect.Id != 0 && _texBarSelect.Height >= 12; // 最低でも3区画×4分割=12px相当は必要

        for (int i = 0; i < 5; i++)
        {
            string path = Path.Combine(_skinRoot, "0.Songs", "difficulty_bar", $"{i}.png");
            _texDiffBars[i] = Raylib.LoadTexture(path);
            _diffBarsLoaded[i] = _texDiffBars[i].Id != 0;
        }

        _texDifficultyPanel = Raylib.LoadTexture(Path.Combine(_skinRoot, "0.Songs", "difficulty_bar", "Difficulty_Panel.png"));
        _difficultyPanelLoaded = _texDifficultyPanel.Id != 0;

        _texClearTypeSymbol = Raylib.LoadTexture(Path.Combine(_skinRoot, "0.Songs", "ClearType_Symbol.png"));
        _clearTypeSymbolLoaded = _texClearTypeSymbol.Id != 0;
        _texScoreRankSymbol = Raylib.LoadTexture(Path.Combine(_skinRoot, "0.Songs", "ScoreRank_Symbol.png"));
        _scoreRankSymbolLoaded = _texScoreRankSymbol.Id != 0;

        _texScorePanel = Raylib.LoadTexture(Path.Combine(_skinRoot, "0.Songs", "ScorePanel.png"));
        _scorePanelLoaded = _texScorePanel.Id != 0;
        _texScorePanelNumber = Raylib.LoadTexture(Path.Combine(_skinRoot, "0.Songs", "ScorePanel_Number.png"));
        _scorePanelNumberLoaded = _texScorePanelNumber.Id != 0;
        LoadScorePanelIndex();
        RefreshScorePanelCounts();

        LoadReturnEntryAssets();

        // メニューBGMの読み込み
        string bgmDir = GetExeBasedPath("Lumen", "2.sound", "BGM");
        string bgmStartPath = Path.Combine(bgmDir, "SongSelect_Start.ogg");
        string bgmLoopPath = Path.Combine(bgmDir, "SongSelect.ogg");
        _musicSongSelectStart = MusicTrack.Load(bgmStartPath);
        _musicStartLoaded = _musicSongSelectStart.Loaded;
        _musicSongSelectStart.Volume = SettingsPanel.EffectiveBgmVolume;
        _musicSongSelectLoop = MusicTrack.Load(bgmLoopPath);
        _musicLoopLoaded = _musicSongSelectLoop.Loaded;
        _musicSongSelectLoop.Volume = SettingsPanel.EffectiveBgmVolume;

        // カッ/ドン操作音の読み込み
        string drumDir = GetExeBasedPath("Lumen", "2.sound", "drum");
        _sndKa = SoundEffect.Load(Path.Combine(drumDir, "ka.wav"));
        _sndKa.Volume = SettingsPanel.EffectiveSeVolume;
        _sndDon = SoundEffect.Load(Path.Combine(drumDir, "don.wav"));
        _sndDon.Volume = SettingsPanel.EffectiveSeVolume;

        // スキップ音の読み込み（Lumen/2.sound/Songselect/Skip.ogg）
        string skipPath = Path.Combine(Path.GetDirectoryName(drumDir)!, "Songselect", "Skip.ogg");
        _skipSoundDuration = 0f;
        if (File.Exists(skipPath))
        {
            _sndSkip = SoundEffect.Load(skipPath);
            _sndSkipLoaded = _sndSkip.Loaded;
            if (_sndSkipLoaded)
            {
                _sndSkip.Volume = SettingsPanel.EffectiveSeVolume;
                try
                {
                    using var reader = AudioEngine.OpenReader(skipPath);
                    _skipSoundDuration = (float)reader.TotalTime.TotalSeconds;
                }
                catch { _skipSoundDuration = 2f; }
            }
        }

        LoadSongList();
        StartMenuBgm();

        NamePlate.Init();

        string animeGuidePath = "Lumen/0.Songs/SongSelect/Control/Anime.aup2";
        if (File.Exists(animeGuidePath))
        {
            _animeGuide = Aup2Anim.Load(animeGuidePath);
        }
        _animeGuideTime = 0f;
    }

    /// <summary>毎フレーム、シーンによらず呼ぶ（アニメ用タイマーとメニューBGMのストリーム更新）。</summary>
    public static void TickAlways(float dt)
    {
        _boxAnimTimer += dt;
        _songScrollTimer += dt;
        if (_songScrollTimer >= SONG_SCROLL_DURATION)
            _lastMoveDir = 0;
        if (_boxTransitionActive)
        {
            _boxTransitionTimer += dt;
            _boxOpenTimer = _boxTransitionTimer;
        }
        else if (_boxCloseActive)
        {
            _boxCloseTimer += dt;
            _boxOpenTimer = _boxCloseTimer;
        }
        else
        {
            _boxOpenTimer = 99f;
        }
        if (_exitConfirmTimer > 0f) _exitConfirmTimer -= dt;
        if (_kaDoublePressTimer < float.MaxValue) _kaDoublePressTimer += dt;
        if (_skipPlayTimer < float.MaxValue) _skipPlayTimer += dt;
        UpdateMenuBgm();
        UpdateSongPreviewTimer(dt);
    }

    /// <summary>選曲画面がアクティブな間、毎フレーム呼ぶ入力・状態更新。</summary>
    public static void Update() => UpdateSongSelect();

    /// <summary>選曲画面のスクロール背景を描画する。</summary>
    public static void DrawBackground() => DrawScrollingBackground();

    /// <summary>選曲画面本体を描画する。</summary>
    public static void Draw() => DrawSongSelectMain();

    /// <summary>他シーンから選曲画面へ戻った際にメニューBGMを再開する。</summary>
    public static void ResumeMenuBgm()
    {
        StartMenuBgm();
        UpdateBgForFocus(); // フォーカス中の曲があればデモ再生を再評価
        LoadScorePanelIndex(); // プレイ後に戻ってきた際、最新のScoreBoard値を反映する
        RefreshScorePanelCounts(); // プレイ後に戻ってきた際、最新のランク/王冠集計を反映する
    }

    /// <summary>演奏開始などでメニューBGMを止める必要がある時に呼ぶ。</summary>
    public static void PauseMenuBgm()
    {
        StopMenuBgm();
        StopSongPreview();
        _pendingPreviewSong = null;
    }

    /// <summary>アプリ終了時に一度だけ呼ぶ。選曲画面が確保した全アセットを解放する。</summary>
    public static void Unload()
    {
        StopMenuBgm();

        if (_defBarLoaded)
        {
            Raylib.UnloadTexture(_texDefBar);
        }
        if (_headerLoaded) { Raylib.UnloadTexture(_texHeader); _headerLoaded = false; }
        if (_footerLoaded) Raylib.UnloadTexture(_texFooter);
        if (_footerAnimLoaded) Raylib.UnloadTexture(_texFooteranim);
        if (_starLoaded) Raylib.UnloadTexture(_texStar);
        if (_overlayLoaded) { Raylib.UnloadTexture(_texOverlay); _overlayLoaded = false; }
        if (_barSelectLoaded) { Raylib.UnloadTexture(_texBarSelect); _barSelectLoaded = false; }

        for (int i = 0; i < 5; i++) if (_diffBarsLoaded[i]) Raylib.UnloadTexture(_texDiffBars[i]);
        if (_difficultyPanelLoaded) Raylib.UnloadTexture(_texDifficultyPanel);
        if (_clearTypeSymbolLoaded) { Raylib.UnloadTexture(_texClearTypeSymbol); _clearTypeSymbolLoaded = false; }
        if (_scoreRankSymbolLoaded) { Raylib.UnloadTexture(_texScoreRankSymbol); _scoreRankSymbolLoaded = false; }
        if (_scorePanelLoaded) { Raylib.UnloadTexture(_texScorePanel); _scorePanelLoaded = false; }
        if (_scorePanelNumberLoaded) { Raylib.UnloadTexture(_texScorePanelNumber); _scorePanelNumberLoaded = false; }

        UnloadCurrentEntries();
        while (_navStack.Count > 0)
        {
            _currentEntries = _navStack.Pop().entries;
            UnloadCurrentEntries();
        }

        _musicSongSelectStart?.Dispose();
        _musicSongSelectLoop?.Dispose();
        StopSongPreview();
        _pendingPreviewSong = null;

        if (_sndSkipLoaded) { _sndSkipLoaded = false; }
        UnloadReturnEntryAssets();
        UnloadScrollingBackground();

        NamePlate.Unload();

        _animeGuide?.Dispose();
        _animeGuide = null;
    }

    // ==================================================================
    // 💡 以下、Program.cs から移行してきた選曲画面の内部実装
    // ==================================================================

    static readonly Dictionary<string, (string Folder, Color? Outline)> _genreBgOverrideMap = new()
    {
        { "A", ("A", null) },
        { "特集", ("0.other", null) },
        { "ポップス", ("1.pops", new Color((byte)0x1f, (byte)0x4e, (byte)0x4a, (byte)255)) },
        { "キッズ", ("2.kids", new Color((byte)0x9a, (byte)0x39, (byte)0x13, (byte)255)) },
        { "アニメ", ("3.anime", new Color((byte)0xa1, (byte)0x15, (byte)0x4f, (byte)255)) },
        { "ボーカロイド", ("4.vocalo", new Color((byte)0x29, (byte)0x35, (byte)0x46, (byte)255)) },
        { "ボーカロイド曲", ("4.vocalo", new Color((byte)0x29, (byte)0x35, (byte)0x46, (byte)255)) },
        { "ゲームミュージック", ("5.game", new Color((byte)0x4c, (byte)0x1e, (byte)0x79, (byte)255)) },
        { "バラエティ", ("6.variety", new Color((byte)0x18, (byte)0x4b, (byte)0x18, (byte)255)) },
        { "クラシック", ("7.classic", new Color((byte)0x65, (byte)0x42, (byte)0x2f, (byte)255)) },
        { "ナムコオリジナル", ("8.namco", new Color((byte)0xa6, (byte)0x1a, (byte)0x07, (byte)255)) }
    };

    static bool TryGetGenreInfo(string genre, out (string Folder, Color? Outline) info)
    {
        info = default;
        if (string.IsNullOrEmpty(genre)) return false;
        return _genreBgOverrideMap.TryGetValue(genre.Trim(), out info)
            || _genreBgOverrideMap.TryGetValue(genre.Replace("™", "").Trim(), out info);
    }

    static Color? GetGenreOutlineColor(string genre)
        => TryGetGenreInfo(genre, out var info) ? info.Outline : null;

    public class SongData
    {
        public string TjaPath;
        public string Title;
        public string TitleJP;
        public string Subtitle = "";
        public string SubtitleJP = "";
        public string WavePath;
        public double DemoStart = 0;
        public string Genre = "";
        public int[] Levels = new int[5];
    }

    class BoxDef
    {
        public string SourceDir = "";
        public string Title = "";
        public string Genre = "";
        public string Explanation = "";
        public string[] ExplanationLines = Array.Empty<string>();
        public Color BoxColor = Color.White;
        public Color ForeColor = Color.White;
        public Color BackColor = Color.Black;
        public bool HasBackColor = false;
        public bool UseBarDecoration = false;

        public string BoxFocusSoundPath = "";
        public string BgImagePath = "";
        public string BarImageName = "";
        public string BoxPopImagePath = "";
        public string DiffBgImagePath = "";

        public Texture2D TexBar, TexBoxPop, TexBg, TexDiffBg;
        public bool BarLoaded, BoxPopLoaded, BgLoaded, DiffBgLoaded;
        public SoundEffect? FocusSound;
        public bool FocusSoundLoaded;
        public bool AssetsLoaded = false;
    }

    abstract class NavEntry
    {
        public string DisplayTitle = "";
    }

    class SongEntry : NavEntry { public SongData Song; public BoxDef OwnerDef; }
    class BoxEntry : NavEntry
    {
        public string FolderPath;
        public BoxDef Def;
        public List<string> VirtualTjaPaths = null;
    }
    class ReturnEntry : NavEntry { }

    static string _songsRoot = "Songs"; // Init() で ResolveSongsRoot() により上書きされる
    static string _currentDir = "";
    static Stack<(string dir, int cursor, List<NavEntry> entries)> _navStack = new();
    static List<NavEntry> _currentEntries = new();
    static SongData _selectedSong = null;
    static string _selectedSongGenre = "";
    static string _currentDirGenre = "";

    static int _cursor = 0;

    static string _skinRoot = ResolveSkinRoot();

    // 💡 曲名の位置調整用の設定値（選択中の項目のみ適用されます）
    private const float TITLE_OFFSET_Y_START = 0f;        // アニメーション開始時のYオフセット
    private const float TITLE_OFFSET_Y_TARGET = -75f;     // アニメーション終了（目標値）のYオフセット
                                                          // X位置の調整（マイナスで左、プラスで右へ移動）
    private const float GENRE_TITLE_OFFSET_X_TARGET = 150f;

    // 💡 ジャンル名（BOXフォルダ名）の位置調整用の設定値（選択中の項目のみ適用されます）
    private const float GENRE_TITLE_OFFSET_Y_START = 0f;        // アニメーション開始時のYオフセット
    private const float GENRE_TITLE_OFFSET_Y_TARGET = -100f;     // アニメーション終了（目標値）のYオフセット

    // 💡 選択中バーと非選択バーの境目だけ追加で開ける隙間（px、通常の gap に上乗せされます）
    private const float SELECTED_GAP_EXTRA = 35f;

    // 💡 デフォルトのバー画像（bar.png 1枚のみ。角丸を含む1枚絵を上下でスライスして使用）
    static Texture2D _texDefBar;
    static bool _defBarLoaded = false;

    // 💡 バー画像の角丸部分の厚み(px)を求める。Selected.png/Overlay.pngの9パッチと同じ
    //    「短辺の25%」を採用し、角の丸みが引き伸ばしで歪まないようにする。
    static int GetBarBorder(Texture2D tex) => (int)(Math.Min(tex.Width, tex.Height) * 0.25f);

    // 💡 bar.png 1枚を up/mid/down の3領域にスライスするための描画元Rectangleを求める。
    //    up/mid/downが全て同じテクスチャ（1枚絵管理）の場合のみスライスし、
    //    従来通り別々のテクスチャ（例:戻るボタンのback.png/backUp.png/backDown.png）が
    //    渡された場合はそれぞれの全体をそのまま使う（従来動作を維持）。
    static (Rectangle up, Rectangle mid, Rectangle down) GetBarSourceRects(Texture2D texUp, Texture2D texMid, Texture2D texDown)
    {
        bool singleTexture = texUp.Id == texMid.Id && texMid.Id == texDown.Id;
        if (singleTexture)
        {
            int border = GetBarBorder(texMid);
            var up = new Rectangle(0, 0, texUp.Width, border);
            var mid = new Rectangle(0, border, texMid.Width, Math.Max(1, texMid.Height - border * 2));
            var down = new Rectangle(0, texDown.Height - border, texDown.Width, border);
            return (up, mid, down);
        }
        return (
            new Rectangle(0, 0, texUp.Width, texUp.Height),
            new Rectangle(0, 0, texMid.Width, texMid.Height),
            new Rectangle(0, 0, texDown.Width, texDown.Height)
        );
    }

    // 💡 バーの up/mid/down それぞれの高さ・全体の幅を求める（scaleFactor適用済みのpx）。
    //    bar.png 1枚管理（up==mid==downが同一テクスチャ）の場合は角丸の厚み分をupH/downHとし、
    //    残りをmidHの基準値とする。従来の3ファイル管理の場合はテクスチャの高さをそのまま使う。
    static (float upH, float midH, float downH, float barW) GetBarDimensions(Texture2D texUp, Texture2D texMid, Texture2D texDown, bool loaded, float scaleFactor)
    {
        if (!loaded) return (50f * scaleFactor, 200f * scaleFactor, 50f * scaleFactor, 500f * scaleFactor);

        bool singleTexture = texUp.Id == texMid.Id && texMid.Id == texDown.Id;
        if (singleTexture)
        {
            int border = GetBarBorder(texMid);
            float upH = border * scaleFactor;
            float downH = border * scaleFactor;
            float midH = Math.Max(1, texMid.Height - border * 2) * scaleFactor;
            float barW = texMid.Width * scaleFactor;
            return (upH, midH, downH, barW);
        }
        return (texUp.Height * scaleFactor, texMid.Height * scaleFactor, texDown.Height * scaleFactor, texMid.Width * scaleFactor);
    }

    // 💡 ヘッダー画像用変数
    static Texture2D _texHeader;
    static bool _headerLoaded = false;

    // 💡 オーバーレイ画像
    static Texture2D _texOverlay;
    static bool _overlayLoaded = false;

    static Texture2D _texBarSelect;
    static bool _barSelectLoaded = false;

    static Texture2D _texFooter, _texFooteranim, _texStar;
    static bool _footerLoaded = false, _footerAnimLoaded = false, _starLoaded = false;
    static float _boxAnimTimer = 99f;

    // 参照実装の ctScrollCounter 相当。カーソル移動前の配置を保持し、
    // 新しい配置へイージングしながら各バーを移動させる。
    private const float SONG_SCROLL_DURATION = 0.12f;
    static float _songScrollTimer = 99f;
    static int _songScrollFromCursor = 0;

    // ==================================================================
    // 💡 BOX展開演出（TJAPlayer-Nijiiro の ctBoxOpen 相当）
    //    BOXを開いて新しいリストに切り替わった瞬間、中心以外のバーが弧を描いて
    //    外側へ散り、続いて新しいリストへ弧を描いて収束する。
    //    それに合わせて選択中バーの王冠／スコアランクアイコンをフェードアウト→フェードインさせる。
    // ==================================================================
    static float _boxOpenTimer = 99f; // 99fで演出なし（通常状態）
    private const float BOX_OPEN_SCATTER = 0.35f; // 外側へ散るのにかかる時間（秒）
    private const float BOX_OPEN_HOLD = 0.05f;    // 散った状態を維持する時間（秒）
    private const float BOX_OPEN_RETURN = 0.35f;  // 新リストへ収束するのにかかる時間（秒）

    /// <summary>0(通常位置)〜1(最も散った状態)の進行度を返す。</summary>
    static float GetBoxOpenScatter01(float boxOpenT)
    {
        if (boxOpenT < BOX_OPEN_SCATTER)
            return (float)Math.Sin(boxOpenT / BOX_OPEN_SCATTER * Math.PI / 2.0); // 0→1 イーズアウト
        if (boxOpenT < BOX_OPEN_SCATTER + BOX_OPEN_HOLD)
            return 1f;
        if (boxOpenT < BOX_OPEN_SCATTER + BOX_OPEN_HOLD + BOX_OPEN_RETURN)
        {
            float p = (boxOpenT - BOX_OPEN_SCATTER - BOX_OPEN_HOLD) / BOX_OPEN_RETURN;
            return (float)Math.Cos(p * Math.PI / 2.0); // 1→0 イーズイン
        }
        return 0f;
    }

    // OpenTaiko の ctBoxOpen と同じ進み方。Start(200, 2700, 1.3) の後、
    // BoxOpenAnime 内で変更される 0.79 / 1.2 の interval も再現する。
    private const float BOX_ANIM_COUNTER_START = 200f;
    private const float BOX_ANIM_COUNTER_SWITCH = 1570f;
    private const float BOX_ANIM_COUNTER_END = 2700f;
    private const float BOX_ANIM_INTERVAL_INITIAL = 1.3f;
    private const float BOX_ANIM_INTERVAL_MIDDLE = 0.79f;
    private const float BOX_ANIM_INTERVAL_FINAL = 1.2f;

    static float GetBoxAnimationCounter(float elapsed)
    {
        if (elapsed <= 0f) return BOX_ANIM_COUNTER_START;

        float initialSeconds = (1300f - BOX_ANIM_COUNTER_START) * BOX_ANIM_INTERVAL_INITIAL / 1000f;
        if (elapsed <= initialSeconds)
            return BOX_ANIM_COUNTER_START + elapsed * 1000f / BOX_ANIM_INTERVAL_INITIAL;

        float middleSeconds = (1690f - 1300f) * BOX_ANIM_INTERVAL_MIDDLE / 1000f;
        float afterInitial = elapsed - initialSeconds;
        if (afterInitial <= middleSeconds)
            return 1300f + afterInitial * 1000f / BOX_ANIM_INTERVAL_MIDDLE;

        float afterMiddle = afterInitial - middleSeconds;
        return Math.Min(BOX_ANIM_COUNTER_END, 1690f + afterMiddle * 1000f / BOX_ANIM_INTERVAL_FINAL);
    }

    static bool IsBoxAnimationActive => _boxTransitionActive || _boxCloseActive;

    static float GetBoxAnimationContentOpacity(float elapsed)
    {
        if (!IsBoxAnimationActive || elapsed >= 99f) return 1f;

        float counter = GetBoxAnimationCounter(elapsed);
        if (counter <= 1100f) return 1f;
        if (counter <= 1620f)
            return Math.Clamp(1f - (counter - 1100f) / 520f, 0f, 1f);
        if (counter < 1900f) return 0f;
        return Math.Clamp((counter - 1900f) * 2.3f / 255f, 0f, 1f);
    }

    // OpenTaikoのDrawBarCenterと同じ、中央バーだけの横つぶれ。
    // ctBoxOpen.CurrentValue 1300〜1890 の間で中央までつぶれ、その後元の幅へ戻る。
    static float GetBoxCenterHorizontalScale(float elapsed)
    {
        if (!IsBoxAnimationActive || elapsed >= 99f) return 1f;

        float counter = GetBoxAnimationCounter(elapsed);
        if (counter < 1300f || counter > 1890f) return 1f;

        float scale = 1f - MathF.Sin((counter - 1250f) * 0.28125f * MathF.PI / 180f);
        return Math.Clamp(scale, 0.01f, 1f);
    }

    // CActSelect曲リスト.cs の BoxOpenAnime を、現在の表示行番号(-5～5)へ対応させる。
    static (float X, float Y, float Opacity) GetReferenceGenreBoxAnimation(int relativeSlot, float elapsed)
    {
        int distance = Math.Abs(relativeSlot);
        if (distance < 1 || distance > 3)
            return (0f, 0f, 1f);

        float counter = GetBoxAnimationCounter(elapsed);
        float sideSign = relativeSlot < 0 ? 1f : -1f;
        float initialX = sideSign * (200f - distance * 40f);       // 160 / 120 / 80
        float initialY = sideSign * (927.5f - distance * 132.5f);  // 795 / 662.5 / 530
        float firstStart = 1000f + (3 - distance) * 60f;

        // 最初の散開。参照実装の角度式をそのまま使用する。
        if (counter <= 1300f)
        {
            if (counter < firstStart)
                return (0f, 0f, 1f);

            if (counter <= firstStart + 360f)
            {
                float angle = ((counter - firstStart) / 2.5f + 90f) * MathF.PI / 180f;
                float sine = MathF.Sin(angle);
                return (initialX - sine * initialX, initialY - sine * initialY, 1f);
            }

            return (initialX, initialY, 1f);
        }

        int returnStart = 1690 + (distance - 1) * 50;
        if (counter >= returnStart && counter <= returnStart + 360f)
        {
            float angle = ((counter - returnStart) / 4f) * MathF.PI / 180f;
            float sine = MathF.Sin(angle);
            float returnY = sideSign * (662.5f + distance * 132.5f); // 795 / 927.5 / 1060
            return (initialX - sine * initialX, returnY - sine * returnY, 1f);
        }

        if (counter > 1300f && counter < 1790f)
            return (sideSign * 600f, sideSign * 600f, 1f);

        if (counter < returnStart)
            return (sideSign * 600f, sideSign * 600f, 1f);

        return (0f, 0f, 1f);
    }

    // OpenTaiko の ctBoxOpen / BoxOpenAnime 相当。
    // 中央から離れたバーを上下左右へ順番に逃がし、いったん画面外へ出してから
    // 新しいジャンルの配置へ戻す。relativeSlot は中央を 0 とする表示行番号。
    static (float X, float Y, float Opacity) GetGenreBoxAnimation(int relativeSlot, float timer)
    {
        int distance = Math.Abs(relativeSlot);
        if (distance < 1 || distance > 3) return (0f, 0f, 1f);

        // 参照側のカウンタは 200 から 2700 まで進む。
        float counter = 200f + timer * 1000f;
        float sideSign = relativeSlot < 0 ? 1f : -1f;
        float delay = (distance - 1) * 60f;
        float returnDelay = (3 - distance) * 50f;

        float nearX = sideSign * (80f + (distance - 1) * 40f);
        float nearY = sideSign * (530f + (distance - 1) * 132.5f);
        float outside = sideSign * 600f;

        float EaseOut(float p) => (float)Math.Sin(Math.Clamp(p, 0f, 1f) * Math.PI / 2.0);

        if (counter < 1000f + delay)
            return (0f, 0f, 1f);

        if (counter < 1300f + delay)
        {
            float p = EaseOut((counter - (1000f + delay)) / 300f);
            return (nearX * p, nearY * p, 1f);
        }

        if (counter < 1690f)
        {
            float phaseDuration = 390f - delay;
            float p = EaseOut((counter - (1300f + delay)) / phaseDuration);
            return (nearX + (outside - nearX) * p, nearY + (outside - nearY) * p, 1f);
        }

        if (counter < 1690f + returnDelay)
            return (outside, outside, 1f);

        if (counter < 2050f + returnDelay)
        {
            float p = EaseOut((counter - (1690f + returnDelay)) / 360f);
            return (outside * (1f - p), outside * (1f - p), 1f);
        }

        return (0f, 0f, 1f);
    }
    static float _exitConfirmTimer = 0f;
    static bool _shouldExit = false;

    static Texture2D[] _texDiffBars = new Texture2D[5];
    static bool[] _diffBarsLoaded = new bool[5];

    static Texture2D _texDifficultyPanel;
    static bool _difficultyPanelLoaded = false;

    // 💡 選曲画面のクリアタイプ・スコアランクアイコン用スプライトシート
    static Texture2D _texClearTypeSymbol;
    static Texture2D _texScorePanel;
    static bool _scorePanelLoaded = false;
    static int _scorePanelIndex = 0; // Data/PlayData.json の "ScoreBoard" 値（0〜4にクランプ）

    static Texture2D _texScorePanelNumber;
    static bool _scorePanelNumberLoaded = false;

    // 💡 全曲の scores_○○.json を再帰スキャンして集計した「ランク0〜6の曲数」「王冠0〜2の曲数」
    static readonly int[] _scoreRankCounts = new int[7];
    static readonly int[] _scoreCrownCounts = new int[3];
    static readonly object _scoreCountLock = new object();
    static int _scoreCountRequestId = 0;
    static bool _clearTypeSymbolLoaded = false;
    static Texture2D _texScoreRankSymbol;
    static bool _scoreRankSymbolLoaded = false;

    static float _diffBarHeight = 300f;
    static float _starSize = 28f;

    internal static MusicTrack _musicSongSelectStart = null!;
    internal static MusicTrack _musicSongSelectLoop = null!;
    static bool _musicStartLoaded = false;
    static bool _musicLoopLoaded = false;
    static bool _menuBgmActive = false;   // メニューBGMを再生中かどうか
    static bool _playingIntro = false;    // 現在イントロ(Start)側を再生中かどうか

    // 💡 選曲時のデモ再生（TJAのWAVE/DEMOSTART指定曲を、DEMOSTARTのタイミングから再生）
    internal static MusicTrack _musicPreview = null;
    static SongData _previewingSong = null;


    internal static SoundEffect _sndKa = null!;
    internal static SoundEffect _sndDon = null!;

    // ==================================================================
    // 💡 Kaダブル押しによる7曲スキップ機能
    //    0.5秒以内にKa（上/左）を2回押したら7曲スキップして Skip.ogg を再生する。
    //    Skip.ogg の再生中は入力を受け付けない（連打しても一度しか反応しない）。
    // ==================================================================
    private static SoundEffect _sndSkip;
    private static bool _sndSkipLoaded = false;
    private static float _skipSoundDuration = 0f;   // Skip.ogg の長さ（秒）。ロード時に計測して保存
    private static float _skipPlayTimer = float.MaxValue; // 再生開始からの経過秒（MaxValueで未再生状態）
    private static float _kaDoublePressTimer = float.MaxValue; // 前回Ka押し時刻からの経過秒（MaxValueで未押し状態）
    private const float KA_DOUBLE_PRESS_WINDOW = 0.125f;  // ダブル押しと見なす時間窓（秒）
    private const float SKIP_COOLDOWN = 0.5f;  // スキップ発動後、次の入力を受け付けるまでの空白（秒）
    private static bool _skipChainActive = false; // 一度スキップしたあと、空白明け直後のウィンドウ内ならダブル押し不要でスキップし続ける
    private const int SKIP_COUNT = 7;                 // 1回のスキップで飛ばす曲数

    private const float ANIM_SHRINK_DURATION = 0.10f;
    private const float ANIM_RESTORE_DURATION = 0.10f;
    private const float SHRINK_SCALE_Y = 0.25f; // 💡 縦方向のシュリンクスケール

    // 💡 非選択フォルダの中央画像の縦幅比率（選択中は100% × SelectedMidGrowScale、非選択はこの値）
    private const float UNSELECTED_MID_SCALE = 1f;

    // 💡 選択中バーの中央部分をさらに縦に伸ばす倍率。1.0fで従来通り（元画像のmid部分と同じ高さ）、
    //    2.0fにすると選択中のmidが2倍の高さまで展開されるようになる。
    public static float SelectedMidGrowScale = 4f;

    // 💡 上へ行くほど左に、下へ行くほど右にずらす「階段状」レイアウトの1段あたりの横移動量
    private const float DIAGONAL_OFFSET_PER_STEP = 40f;

    static string _bgCategory = null;
    static Texture2D _texBgCurrent, _texBgPrev;
    static bool _bgCurrentLoaded = false, _bgPrevLoaded = false;
    static float _bgCrossfadeTimer = 999f;
    static float _bgScrollX = 0f;
    private const float BG_CROSSFADE_DURATION = 0.6f;
    private const float BG_SCROLL_SPEED = 40f;

    static void SetActiveBgCategory(string catFolder)
    {
        string resolvedCat = catFolder;
        string bgPath = string.IsNullOrEmpty(resolvedCat) ? null : Path.Combine(_skinRoot, "0.Songs", "category", resolvedCat, "background.png");
        if (string.IsNullOrEmpty(resolvedCat) || !File.Exists(bgPath))
        {
            resolvedCat = "0.other";
            bgPath = Path.Combine(_skinRoot, "0.Songs", "category", resolvedCat, "background.png");
        }

        if (resolvedCat == _bgCategory) return;
        if (!File.Exists(bgPath)) return;

        if (_bgCurrentLoaded)
        {
            if (_bgPrevLoaded) Raylib.UnloadTexture(_texBgPrev);
            _texBgPrev = _texBgCurrent;
            _bgPrevLoaded = true;
        }

        _texBgCurrent = Raylib.LoadTexture(bgPath);
        _bgCurrentLoaded = _texBgCurrent.Id != 0;
        _bgCategory = resolvedCat;
        _bgCrossfadeTimer = 0f;
    }

    static void DrawScrollingBackground()
    {
        var (vx, vy, vw, vh) = Songs.GetViewport();
        float dt = Raylib.GetFrameTime();
        _bgScrollX += BG_SCROLL_SPEED * dt;
        _bgCrossfadeTimer += dt;

        float fadeProgress = BG_CROSSFADE_DURATION <= 0f ? 1f : Math.Min(1f, _bgCrossfadeTimer / BG_CROSSFADE_DURATION);

        if (_bgCurrentLoaded)
        {
            DrawTiledVerticalFitBackground(_texBgCurrent, vx, vy, vw, vh, _bgScrollX, 255);
        }
        if (_bgPrevLoaded && fadeProgress < 1f)
        {
            byte prevAlpha = (byte)((1f - fadeProgress) * 255);
            DrawTiledVerticalFitBackground(_texBgPrev, vx, vy, vw, vh, _bgScrollX, prevAlpha);
        }
    }

    static void DrawTiledVerticalFitBackground(Texture2D tex, float vx, float vy, float vw, float vh, float scrollX, byte alpha)
    {
        float scale = vh / tex.Height;
        float scaledW = tex.Width * scale;
        if (scaledW <= 0f) return;

        float offset = scrollX % scaledW;
        if (offset < 0) offset += scaledW;

        Color tint = new Color((byte)255, (byte)255, (byte)255, alpha);
        Rectangle src = new Rectangle(0, 0, tex.Width, tex.Height);

        for (float x = vx - offset; x < vx + vw; x += scaledW)
        {
            Raylib.DrawTexturePro(tex, src, new Rectangle(x, vy, scaledW, vh), Vector2.Zero, 0f, tint);
        }
    }

    static void UnloadScrollingBackground()
    {
        if (_bgCurrentLoaded) { Raylib.UnloadTexture(_texBgCurrent); _bgCurrentLoaded = false; }
        if (_bgPrevLoaded) { Raylib.UnloadTexture(_texBgPrev); _bgPrevLoaded = false; }
    }

    static string GetExeBasedPath(params string[] parts)
    {
        string combined = AppContext.BaseDirectory;
        foreach (var p in parts) combined = Path.Combine(combined, p);
        return combined;
    }

    // ==================================================================
    // 💡 Data/PlayData.json の "ScoreBoard" 値を読み込み、ScorePanel.png の
    //    どの段（0始まり）を表示するかを更新する。
    // ==================================================================
    static void LoadScorePanelIndex()
    {
        _scorePanelIndex = 0;
        try
        {
            string path = GetExeBasedPath("Data", "PlayData.json");
            if (!File.Exists(path)) return;

            string json = File.ReadAllText(path);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ScoreBoard", out var sb) && sb.TryGetInt32(out int val))
            {
                _scorePanelIndex = Math.Clamp(val, 0, 4); // ScorePanel.pngは縦5段構成
            }
        }
        catch { /* 読み込み失敗時は0段目(先頭)のまま */ }
    }

    // ==================================================================
    // 💡 ScorePanel.png（縦5段のスプライトシート）から、Data/PlayData.json の
    //    "ScoreBoard" 値に対応する段だけを切り出して描画する。
    //    位置は ScorePanelCenterX/Y (vw/vh比率)、大きさは ScorePanelWidth (px) で調整可能。
    // ==================================================================
    static void DrawScorePanel(int vx, int vy, int vw, int vh, float scaleFactor)
    {
        if (!_scorePanelLoaded) return;

        float cellW = _texScorePanel.Width;
        float cellH = _texScorePanel.Height / 5f;

        Rectangle src = new Rectangle(0, _scorePanelIndex * cellH, cellW, cellH);

        float drawW = ScorePanelWidth * scaleFactor;
        float drawH = drawW * (cellH / cellW);

        float centerX = vx + vw * ScorePanelCenterX;
        float centerY = vy + vh * ScorePanelCenterY;

        Rectangle dest = new Rectangle(centerX - drawW / 2f, centerY - drawH / 2f, drawW, drawH);
        Raylib.DrawTexturePro(_texScorePanel, src, dest, Vector2.Zero, 0f, Color.White);

        DrawScorePanelNumbers(centerX, centerY, scaleFactor);
    }

    // ==================================================================
    // 💡 _scorePanelIndex（ScoreBoard値）が対象とする難易度の scores_○○.json を
    //    ライブラリ全体から再帰的に探し、ランク0〜6・王冠0〜2の曲数を集計する。
    //    ScoreBoard=4（おに）のときはおに・うらおに両方の scores_*.json を合算する。
    //    ファイル探索・パースは重いのでバックグラウンドで実行し、完了時に結果を反映する。
    // ==================================================================
    static void RefreshScorePanelCounts()
    {
        int myRequestId = System.Threading.Interlocked.Increment(ref _scoreCountRequestId);
        string songsRoot = _songsRoot;
        int panelIndex = _scorePanelIndex;

        Task.Run(() =>
        {
            var rankCounts = new int[7];
            var crownCounts = new int[3];

            try
            {
                if (Directory.Exists(songsRoot))
                {
                    List<int> targetDiffIndexes = (panelIndex == 4)
                        ? new List<int> { 3, 4 }
                        : new List<int> { panelIndex };

                    foreach (int d in targetDiffIndexes)
                    {
                        string fileName = $"scores_{_difficultyJsonNames[d]}.json";
                        foreach (string jsonPath in Directory.EnumerateFiles(songsRoot, fileName, SearchOption.AllDirectories))
                        {
                            try
                            {
                                string json = File.ReadAllText(jsonPath);
                                using var doc = System.Text.Json.JsonDocument.Parse(json);
                                foreach (var songProp in doc.RootElement.EnumerateObject())
                                {
                                    var obj = songProp.Value;
                                    if (obj.TryGetProperty("BestRankIndex", out var ri) && ri.TryGetInt32(out int riVal)
                                        && riVal >= 0 && riVal <= 6)
                                    {
                                        rankCounts[riVal]++;
                                    }
                                    if (obj.TryGetProperty("BestClearType", out var ct) && ct.TryGetInt32(out int ctVal)
                                        && ctVal >= 0 && ctVal <= 2)
                                    {
                                        crownCounts[ctVal]++;
                                    }
                                }
                            }
                            catch { /* 1ファイルの読み込み失敗は無視して続行 */ }
                        }
                    }
                }
            }
            catch { /* 探索失敗時は0のまま */ }

            // 💡 実行中に新しいリクエストが来ていたら、古い結果は破棄する
            if (myRequestId != _scoreCountRequestId) return;

            lock (_scoreCountLock)
            {
                Array.Copy(rankCounts, _scoreRankCounts, 7);
                Array.Copy(crownCounts, _scoreCrownCounts, 3);
            }
        });
    }

    // ==================================================================
    // 💡 ScorePanel_Number.png（0〜9の数字が横一列に並んだスプライトシート）を使って
    //    集計済みのランク/王冠曲数を、ScorePanelの上に3列×4行のグリッドで描画する。
    //    row0:(空白)(空白)ランク6 / row1:ランク3,4,5 / row2:ランク0,1,2 / row3:王冠0,1,2
    // ==================================================================
    static void DrawScorePanelNumbers(float panelCenterX, float panelCenterY, float scaleFactor)
    {
        if (!_scorePanelNumberLoaded || _texScorePanelNumber.Id == 0) return;

        int[] rankCounts;
        int[] crownCounts;
        lock (_scoreCountLock)
        {
            rankCounts = (int[])_scoreRankCounts.Clone();
            crownCounts = (int[])_scoreCrownCounts.Clone();
        }

        // (row, col, value) の並び。col=-1は空白（描画スキップ）
        var cells = new (int row, int col, int value)[]
        {
            (0, 2, rankCounts[6]),
            (1, 0, rankCounts[3]), (1, 1, rankCounts[4]), (1, 2, rankCounts[5]),
            (2, 0, rankCounts[0]), (2, 1, rankCounts[1]), (2, 2, rankCounts[2]),
            (3, 0, crownCounts[0]), (3, 1, crownCounts[1]), (3, 2, crownCounts[2]),
        };

        float digitCellW = _texScorePanelNumber.Width / 10f;
        float digitCellH = _texScorePanelNumber.Height;
        float digitDrawW = digitCellW * NumberDigitScale * scaleFactor;
        float digitDrawH = digitCellH * NumberDigitScale * scaleFactor;
        float gap = NumberDigitGap * scaleFactor;

        float gridBaseX = panelCenterX + NumberGridOffsetX * scaleFactor;
        float gridBaseY = panelCenterY + NumberGridOffsetY * scaleFactor;
        float colSpacing = NumberGridColSpacing * scaleFactor;
        float rowSpacing = NumberGridRowSpacing * scaleFactor;

        foreach (var (row, col, value) in cells)
        {
            string text = Math.Max(0, value).ToString(CultureInfo.InvariantCulture);
            float textW = text.Length * digitDrawW + (text.Length - 1) * gap;

            float cellCenterX = gridBaseX + (col - 1) * colSpacing;
            float cellCenterY = gridBaseY + row * rowSpacing;

            // 桁数に関わらず、そのセルの中心(cellCenterX)を基準に左右対称に配置する
            float drawX = cellCenterX - textW / 2f;
            float drawY = cellCenterY - digitDrawH / 2f;

            foreach (char c in text)
            {
                int digit = c - '0';
                if (digit < 0 || digit > 9) { drawX += digitDrawW + gap; continue; }

                Rectangle src = new Rectangle(digit * digitCellW, 0, digitCellW, digitCellH);
                Rectangle dest = new Rectangle(drawX, drawY, digitDrawW, digitDrawH);
                Raylib.DrawTexturePro(_texScorePanelNumber, src, dest, Vector2.Zero, 0f, Color.White);

                drawX += digitDrawW + gap;
            }
        }
    }

    static void StartMenuBgm()
    {
        if (!AudioEngine.IsReady) return;

        StopMenuBgm();
        _menuBgmActive = true;

        if (_musicStartLoaded)
        {
            _musicSongSelectStart.Looping = false;
            _musicSongSelectStart.Play();
            _playingIntro = true;
        }
        else if (_musicLoopLoaded)
        {
            _musicSongSelectLoop.Looping = true;
            _musicSongSelectLoop.Play();
            _playingIntro = false;
        }
    }

    static void StopMenuBgm()
    {
        if (_musicStartLoaded) _musicSongSelectStart.Stop();
        if (_musicLoopLoaded) _musicSongSelectLoop.Stop();
        _menuBgmActive = false;
        _playingIntro = false;
    }

    static void UpdateMenuBgm()
    {
        if (!_menuBgmActive) return;

        if (_playingIntro && _musicStartLoaded && _musicSongSelectStart.Ended)
        {
            _playingIntro = false;
            if (_musicLoopLoaded)
            {
                _musicSongSelectLoop.Looping = true;
                _musicSongSelectLoop.Play();
            }
        }
    }

    internal static void PlayKaSound() => _sndKa?.Play();

    internal static void PlayDonSound() => _sndDon?.Play();

    static string ResolveSkinRoot()
    {
        string envSkin = Environment.GetEnvironmentVariable("TAIKOSTORM_SKIN");
        if (!string.IsNullOrEmpty(envSkin) && Directory.Exists(envSkin)) return envSkin;

        const string configPath = "skin.txt";
        if (File.Exists(configPath))
        {
            try
            {
                string configured = File.ReadAllText(configPath).Trim();
                if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured)) return configured;
            }
            catch { }
        }

        return Path.Combine("Lumen");
    }

    static string ResolveSongsRoot()
    {
        if (SettingsPanel.SongsType == SettingsPanel.SongsTypeOption.ESE)
            return "Songs/ESE";

        string env = Environment.GetEnvironmentVariable("TAIKOSTORM_SONGS_DIR");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;

        const string configPath = "songs_root.txt";
        if (File.Exists(configPath))
        {
            try
            {
                string configured = File.ReadAllText(configPath).Trim();
                if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured)) return configured;
            }
            catch { }
        }

        return "Songs";
    }

    // 💡 [修正] TJAモードで"Songs"を再帰スキャンすると、直下の"Songs/ESE"も
    //    ただのサブフォルダとして拾われてESEの曲がTJA側の一覧に混ざってしまっていた。
    //    TJAモード時のみ、"Songs/ESE"フォルダをスキャン対象から除外する。
    //    (ESEモード時は_songsRoot自体が"Songs/ESE"なのでこの除外は不要=常にfalseで無害)
    static readonly string _eseFolderFullPath = Path.GetFullPath(Path.Combine("Songs", "ESE"));

    static bool IsExcludedEseFolder(string dir)
    {
        if (SettingsPanel.SongsType == SettingsPanel.SongsTypeOption.ESE) return false;
        try { return Path.GetFullPath(dir).Equals(_eseFolderFullPath, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    static IEnumerable<string> GetSubDirectoriesExcludingEse(string dir)
        => Directory.GetDirectories(dir).Where(sub => !IsExcludedEseFolder(sub));

    static string ResolveAssetPath(string baseDir, string relativeOrName)
    {
        if (string.IsNullOrWhiteSpace(relativeOrName)) return "";
        string direct = Path.Combine(baseDir, relativeOrName);
        if (File.Exists(direct)) return direct;
        try
        {
            string fileName = Path.GetFileName(relativeOrName);
            if (!string.IsNullOrEmpty(fileName) && Directory.Exists(baseDir))
            {
                var found = Directory.EnumerateFiles(baseDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (found != null) return found;
            }
        }
        catch { }
        return direct;
    }

    static string ResolveBarVariant(string baseDir, string barBaseName, string suffix)
    {
        if (string.IsNullOrWhiteSpace(barBaseName)) return "";
        return ResolveAssetPath(baseDir, barBaseName + suffix + ".png");
    }

    static SongSelectScene()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// bytesが正当なUTF-8バイト列かどうかを例外を使わずに判定します。
    /// 💡 [軽量化] 以前はUTF8Encoding(throwOnInvalidBytes:true)のtry/catchで判定しており、
    ///    Shift-JISファイル(このプロジェクトの.tjaで非常に多い)を読むたびに必ず例外が発生していた。
    ///    .NETの例外は1回あたり数十ms級のコストがあり、984ファイル中の大半がShift-JISだと
    ///    PrewarmSongCacheが24秒以上かかる原因になっていた。バイト単位の検証に変えることで
    ///    例外を完全に回避できる。
    /// </summary>
    static bool IsValidUtf8(byte[] bytes)
    {
        int i = 0;
        int len = bytes.Length;
        while (i < len)
        {
            byte b = bytes[i];
            int extra;
            if (b < 0x80) { i++; continue; }
            else if ((b & 0xE0) == 0xC0) extra = 1;
            else if ((b & 0xF0) == 0xE0) extra = 2;
            else if ((b & 0xF8) == 0xF0) extra = 3;
            else return false;

            if (i + extra >= len) return false;
            for (int j = 1; j <= extra; j++)
            {
                if ((bytes[i + j] & 0xC0) != 0x80) return false;
            }
            i += extra + 1;
        }
        return true;
    }

    static string DecodeAuto(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new System.Text.UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        else if (IsValidUtf8(bytes))
            return new System.Text.UTF8Encoding(false).GetString(bytes);
        else
            return System.Text.Encoding.GetEncoding("shift_jis").GetString(bytes);
    }

    static string[] ReadLinesAuto(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string text = DecodeAuto(bytes);
        return text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
    }


    static BoxDef ParseBoxDef(string path)
    {
        var box = new BoxDef();
        string dir = Path.GetDirectoryName(path) ?? ".";
        box.SourceDir = dir;
        string[] lines = ReadLinesAuto(path);

        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || !line.StartsWith("#")) continue;
            int colonIdx = line.IndexOf(':');
            if (colonIdx == -1) continue;
            string key = line.Substring(1, colonIdx - 1).Trim().ToUpper();
            string val = line.Substring(colonIdx + 1).Trim();

            switch (key)
            {
                case "TITLE": box.Title = val; break;
                case "GENRE": box.Genre = val; break;
                case "EXPLANATION":
                    box.Explanation = val.Replace("\\n", "\n");
                    box.ExplanationLines = box.Explanation.Split('\n');
                    break;
                case "BOXCOLOR": box.BoxColor = ParseHexColor(val, Color.White); break;
                case "FORECOLOR": box.ForeColor = ParseHexColor(val, Color.White); break;
                case "BACKCOLOR": box.BackColor = ParseHexColor(val, Color.Black); box.HasBackColor = true; break;
                case "USEBARDECORATION": box.UseBarDecoration = val.Trim().ToLower() == "true"; break;
                case "BOXFOCUSSOUND": box.BoxFocusSoundPath = ResolveAssetPath(dir, val); break;
                case "BGIMAGE": box.BgImagePath = ResolveAssetPath(dir, val); break;
                case "BARIMAGE": box.BarImageName = Path.GetFileNameWithoutExtension(val); break;
                case "BOXPOPIMAGE": box.BoxPopImagePath = ResolveAssetPath(dir, val); break;
                case "DIFFBGIMAGE": box.DiffBgImagePath = ResolveAssetPath(dir, val); break;
            }
        }
        return box;
    }

    /// <summary>
    /// 指定パス（tjaファイルのパス等）から親ディレクトリを一つずつ遡り、
    /// 最初に見つかった box.def の GENRE 値を返す（見つからなければ空文字）。
    /// 演奏画面（Enso）のジャンル表示から、TJA側のGENRE:ではなくこちらを参照するために公開している。
    /// </summary>
    public static string GetGenreFromNearestBoxDef(string fromPath)
    {
        if (string.IsNullOrEmpty(fromPath)) return "";
        try
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(fromPath));
            while (!string.IsNullOrEmpty(dir))
            {
                string boxDefPath = Path.Combine(dir, "box.def");
                if (File.Exists(boxDefPath))
                {
                    try { return ParseBoxDef(boxDefPath).Genre; }
                    catch { return ""; }
                }
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch { }
        return "";
    }

    static Color ParseHexColor(string hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        hex = hex.Trim();
        if (hex.StartsWith("#")) hex = hex.Substring(1);
        if (hex.Length != 6 && hex.Length != 8) return fallback;
        try
        {
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            byte a = hex.Length == 8 ? Convert.ToByte(hex.Substring(6, 2), 16) : (byte)255;
            return new Color(r, g, b, a);
        }
        catch { return fallback; }
    }

    static void LoadBoxAssets(BoxDef def)
    {
        if (def.AssetsLoaded) return;
        def.AssetsLoaded = true;

        // 💡 bar.png 1枚のみで管理する（角丸を含む1枚絵。barup.png/bardown.pngはもう使わない）
        if (!string.IsNullOrEmpty(def.BarImageName))
        {
            string barPath = ResolveBarVariant(def.SourceDir, def.BarImageName, "");
            if (File.Exists(barPath)) { def.TexBar = Raylib.LoadTexture(barPath); def.BarLoaded = def.TexBar.Id != 0; }
        }
        if (!def.BarLoaded && TryGetGenreInfo(def.Genre, out var barCat))
        {
            string catDir = Path.Combine(_skinRoot, "0.Songs", "category", barCat.Folder);
            string p = Path.Combine(catDir, "bar.png");
            if (File.Exists(p)) { def.TexBar = Raylib.LoadTexture(p); def.BarLoaded = def.TexBar.Id != 0; }
        }

        if (!string.IsNullOrEmpty(def.BoxPopImagePath) && File.Exists(def.BoxPopImagePath))
        {
            def.TexBoxPop = Raylib.LoadTexture(def.BoxPopImagePath);
            def.BoxPopLoaded = def.TexBoxPop.Id != 0;
        }

        bool bgImageApplied = false;
        if (!string.IsNullOrEmpty(def.BgImagePath) && File.Exists(def.BgImagePath))
        {
            def.TexBg = Raylib.LoadTexture(def.BgImagePath);
            def.BgLoaded = def.TexBg.Id != 0;
            bgImageApplied = def.BgLoaded;
        }
        if (!bgImageApplied && TryGetGenreInfo(def.Genre, out var bgCat))
        {
            string overrideBgPath = Path.Combine(_skinRoot, "0.Songs", "category", bgCat.Folder, "bg.png");
            if (File.Exists(overrideBgPath))
            {
                def.TexBg = Raylib.LoadTexture(overrideBgPath);
                def.BgLoaded = def.TexBg.Id != 0;
            }
        }
        if (!string.IsNullOrEmpty(def.DiffBgImagePath) && File.Exists(def.DiffBgImagePath))
        {
            def.TexDiffBg = Raylib.LoadTexture(def.DiffBgImagePath);
            def.DiffBgLoaded = def.TexDiffBg.Id != 0;
        }
        if (!string.IsNullOrEmpty(def.BoxFocusSoundPath) && File.Exists(def.BoxFocusSoundPath))
        {
            def.FocusSound = SoundEffect.Load(def.BoxFocusSoundPath);
            def.FocusSoundLoaded = def.FocusSound.Loaded;
        }
    }

    static void UnloadBoxAssets(BoxDef def)
    {
        if (!def.AssetsLoaded) return;
        if (def.BarLoaded) { Raylib.UnloadTexture(def.TexBar); def.BarLoaded = false; }
        if (def.BoxPopLoaded) { Raylib.UnloadTexture(def.TexBoxPop); def.BoxPopLoaded = false; }
        if (def.BgLoaded) { Raylib.UnloadTexture(def.TexBg); def.BgLoaded = false; }
        if (def.DiffBgLoaded) { Raylib.UnloadTexture(def.TexDiffBg); def.DiffBgLoaded = false; }
        if (def.FocusSoundLoaded) { def.FocusSound = null; def.FocusSoundLoaded = false; }
        def.AssetsLoaded = false;
    }

    static void UnloadCurrentEntries()
    {
        if (_currentEntries == null) return;
        foreach (var e in _currentEntries)
        {
            if (e is BoxEntry be) UnloadBoxAssets(be.Def);
        }
    }

    static void UnloadAllNavAssetsAndClearStack()
    {
        var allEntries = new List<NavEntry>(_currentEntries);
        foreach (var frame in _navStack) allEntries.AddRange(frame.entries);

        var unloadedDefs = new HashSet<BoxDef>();
        foreach (var e in allEntries)
        {
            if (e is BoxEntry be && unloadedDefs.Add(be.Def)) UnloadBoxAssets(be.Def);
            else if (e is SongEntry se && se.OwnerDef != null && unloadedDefs.Add(se.OwnerDef)) UnloadBoxAssets(se.OwnerDef);
        }
        _navStack.Clear();
        _currentEntries = new List<NavEntry>();
    }

    static BoxDef GetCurrentDirBoxDef()
    {
        string boxDefPath = Path.Combine(_currentDir, "box.def");
        if (File.Exists(boxDefPath))
        {
            try { return ParseBoxDef(boxDefPath); }
            catch { return null; }
        }
        return null;
    }

    static List<string> GatherUncategorizedTjas(string dir)
    {
        var result = new List<string>();
        if (!Directory.Exists(dir)) return result;

        result.AddRange(Directory.GetFiles(dir, "*.tja", SearchOption.TopDirectoryOnly));

        foreach (var sub in GetSubDirectoriesExcludingEse(dir))
        {
            if (!File.Exists(Path.Combine(sub, "box.def")))
            {
                result.AddRange(GatherUncategorizedTjas(sub));
            }
        }
        return result;
    }

    static List<string> GatherNestedBoxFolders(string dir)
    {
        var result = new List<string>();
        if (!Directory.Exists(dir)) return result;

        foreach (var sub in GetSubDirectoriesExcludingEse(dir))
        {
            if (File.Exists(Path.Combine(sub, "box.def")))
            {
                result.Add(sub);
            }
            else
            {
                result.AddRange(GatherNestedBoxFolders(sub));
            }
        }
        return result;
    }

    static List<string> GatherOwnedTjas(string dir)
    {
        var result = new List<string>();
        if (!Directory.Exists(dir)) return result;

        result.AddRange(Directory.GetFiles(dir, "*.tja", SearchOption.TopDirectoryOnly));

        foreach (var sub in GetSubDirectoriesExcludingEse(dir))
        {
            if (!File.Exists(Path.Combine(sub, "box.def")))
            {
                result.AddRange(GatherOwnedTjas(sub));
            }
        }
        return result;
    }

    static readonly CompareInfo _jaCompare = CultureInfo.GetCultureInfo("ja-JP").CompareInfo;

    static string GetSortTitle(SongData song)
        => !string.IsNullOrEmpty(song.TitleJP) ? song.TitleJP : song.Title;

    static int CompareSongTitle(SongData a, SongData b)
        => _jaCompare.Compare(GetSortTitle(a), GetSortTitle(b),
            CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType);

    static long? GetNumberPrefix(string name)
    {
        long n = 0;
        int i = 0;
        while (i < name.Length && i < 18 && char.IsDigit(name[i]))
        {
            n = n * 10 + (long)char.GetNumericValue(name[i]);
            i++;
        }
        return i > 0 ? n : (long?)null;
    }

    static int CompareDirPath(string a, string b)
    {
        long? na = GetNumberPrefix(Path.GetFileName(a) ?? "");
        long? nb = GetNumberPrefix(Path.GetFileName(b) ?? "");
        if (na.HasValue && nb.HasValue)
        {
            int c = na.Value.CompareTo(nb.Value);
            if (c != 0) return c;
        }
        else if (na.HasValue) return -1;
        else if (nb.HasValue) return 1;
        return StringComparer.OrdinalIgnoreCase.Compare(a, b);
    }

    static readonly IComparer<string> DirPathComparer = Comparer<string>.Create(CompareDirPath);

    static int CompareSongEntry(SongData a, SongData b)
    {
        var sep = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var pa = Path.GetFullPath(a.TjaPath).Split(sep);
        var pb = Path.GetFullPath(b.TjaPath).Split(sep);

        int dirs = Math.Min(pa.Length, pb.Length) - 1;
        for (int i = 0; i < dirs; i++)
        {
            if (string.Equals(pa[i], pb[i], StringComparison.OrdinalIgnoreCase)) continue;

            long? na = GetNumberPrefix(pa[i]);
            long? nb = GetNumberPrefix(pb[i]);
            if (na.HasValue && nb.HasValue)
            {
                int c = na.Value.CompareTo(nb.Value);
                if (c != 0) return c;
                c = StringComparer.OrdinalIgnoreCase.Compare(pa[i], pb[i]);
                if (c != 0) return c;
                continue;
            }
            if (na.HasValue) return -1;
            if (nb.HasValue) return 1;
            return CompareSongTitle(a, b);
        }
        return CompareSongTitle(a, b);
    }

    static List<NavEntry> BuildEntries(string dir)
    {
        var list = new List<NavEntry>();
        if (!Directory.Exists(dir)) return list;

        bool hasBoxDef = File.Exists(Path.Combine(dir, "box.def"));
        List<string> uncatTjas = new List<string>();

        if (hasBoxDef)
        {
            BoxDef ownDef;
            try { ownDef = ParseBoxDef(Path.Combine(dir, "box.def")); } catch { ownDef = new BoxDef { SourceDir = dir }; }
            LoadBoxAssets(ownDef);

            var ownedTjas = GatherOwnedTjas(dir).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            var ownedSongs = ParseSongDataBatch(ownedTjas).ToList();
            ownedSongs.Sort(CompareSongEntry);
            foreach (var song in ownedSongs)
            {
                list.Add(new SongEntry { Song = song, DisplayTitle = song.Title, OwnerDef = ownDef });
            }

            var nestedBoxDirs = GatherNestedBoxFolders(dir).OrderBy(s => s, DirPathComparer);
            foreach (var sub in nestedBoxDirs)
            {
                string nestedBoxDefPath = Path.Combine(sub, "box.def");
                BoxDef nestedDef;
                try { nestedDef = ParseBoxDef(nestedBoxDefPath); } catch { nestedDef = new BoxDef { SourceDir = sub }; }
                if (string.IsNullOrEmpty(nestedDef.Title)) nestedDef.Title = Path.GetFileName(sub);
                LoadBoxAssets(nestedDef);
                list.Add(new BoxEntry { FolderPath = sub, Def = nestedDef, DisplayTitle = nestedDef.Title });
            }
        }
        else
        {
            uncatTjas.AddRange(Directory.GetFiles(dir, "*.tja", SearchOption.TopDirectoryOnly));

            foreach (var sub in GetSubDirectoriesExcludingEse(dir).OrderBy(s => s, DirPathComparer))
            {
                string boxDefPath = Path.Combine(sub, "box.def");
                if (File.Exists(boxDefPath))
                {
                    BoxDef def;
                    try { def = ParseBoxDef(boxDefPath); } catch { def = new BoxDef { SourceDir = sub }; }
                    if (string.IsNullOrEmpty(def.Title)) def.Title = Path.GetFileName(sub);
                    LoadBoxAssets(def);
                    list.Add(new BoxEntry { FolderPath = sub, Def = def, DisplayTitle = def.Title });
                }
                else
                {
                    var nestedBoxDirs = GatherNestedBoxFolders(sub).OrderBy(s => s, DirPathComparer);
                    foreach (var nestedBoxDir in nestedBoxDirs)
                    {
                        string nestedBoxDefPath = Path.Combine(nestedBoxDir, "box.def");
                        BoxDef nestedDef;
                        try { nestedDef = ParseBoxDef(nestedBoxDefPath); } catch { nestedDef = new BoxDef { SourceDir = nestedBoxDir }; }
                        if (string.IsNullOrEmpty(nestedDef.Title)) nestedDef.Title = Path.GetFileName(nestedBoxDir);
                        LoadBoxAssets(nestedDef);
                        list.Add(new BoxEntry { FolderPath = nestedBoxDir, Def = nestedDef, DisplayTitle = nestedDef.Title });
                    }

                    uncatTjas.AddRange(GatherUncategorizedTjas(sub));
                }
            }

            if (uncatTjas.Count > 0)
            {
                var uncatDef = new BoxDef { SourceDir = dir, Title = "未分類", BoxColor = Color.Red, Genre = "特集" };
                uncatDef.Explanation = "このフォルダ内の未分類の曲を\nまとめて表示します。";
                uncatDef.ExplanationLines = uncatDef.Explanation.Split('\n');
                LoadBoxAssets(uncatDef); // 💡 これが抜けていたため category/0.other/bar.png が永久に読み込まれず、
                                         //    常にフォールバックの巨大デフォルトサイズで描画されていた

                list.Add(new BoxEntry
                {
                    FolderPath = "",
                    Def = uncatDef,
                    DisplayTitle = "未分類",
                    VirtualTjaPaths = uncatTjas,
                });
            }
        }

        return list;
    }

    static List<NavEntry> ApplyReturnEntries(List<NavEntry> list)
    {
        var result = new List<NavEntry> { new ReturnEntry { DisplayTitle = "とじる" } };
        int count = 0;
        foreach (var entry in list)
        {
            result.Add(entry);
            count++;
            if (count % 7 == 0) result.Add(new ReturnEntry { DisplayTitle = "とじる" });
        }
        return result;
    }

    static readonly Dictionary<string, (DateTime mtime, SongData data)> _songCache = new();

    static SongData ParseSongDataCached(string tjaPath)
    {
        DateTime mtime;
        try { mtime = File.GetLastWriteTimeUtc(tjaPath); } catch { mtime = DateTime.MinValue; }

        lock (_songCache)
        {
            if (_songCache.TryGetValue(tjaPath, out var cached) && cached.mtime == mtime) return cached.data;
        }

        var data = ParseSongData(tjaPath);

        lock (_songCache) { _songCache[tjaPath] = (mtime, data); }
        return data;
    }

    static SongData[] ParseSongDataBatch(IReadOnlyList<string> tjaPaths)
    {
        var result = new SongData[tjaPaths.Count];
        Parallel.For(0, tjaPaths.Count, i => result[i] = ParseSongDataCached(tjaPaths[i]));
        return result;
    }

    internal static Task PreloadSongCacheAsync()
    {
        return PrewarmSongCache(ResolveSongsRoot());
    }

    static Task PrewarmSongCache(string songsRoot)
    {
        if (!Directory.Exists(songsRoot)) return Task.CompletedTask;
        return Task.Run(() =>
        {
            try
            {
                long wsBefore = Environment.WorkingSet;
                long heapBefore = GC.GetTotalMemory(false);

                var paths = Directory.EnumerateFiles(songsRoot, "*.tja", SearchOption.AllDirectories).ToList();
                var sw = System.Diagnostics.Stopwatch.StartNew();

                // 💡 全コア並列で一気にパースすると、File.ReadAllBytes+string.Split由来の
                //    一時アロケーションが短時間に集中してGCが追いつかず、ManagedHeapが
                //    数百MB〜1GB級に張り付く原因になっていた。並列度を抑えつつバッチごとに
                //    GCを挟むことでピークメモリを大幅に下げる。
                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) };
                const int batchSize = 200;
                for (int offset = 0; offset < paths.Count; offset += batchSize)
                {
                    var batch = paths.Skip(offset).Take(batchSize);
                    Parallel.ForEach(batch, parallelOptions, p => ParseSongDataCached(p));

                    // 💡 バッチ間でGen0/1だけ軽く回収しておくことで、一時オブジェクトが
                    //    積み上がったまま次のバッチに突入するのを防ぐ（Gen2の重いGCはループ後に1回だけ）
                    GC.Collect(1, GCCollectionMode.Optimized, false);
                }
                sw.Stop();

                long wsAfterParse = Environment.WorkingSet;
                long heapAfterParse = GC.GetTotalMemory(false);

                // 💡 [診断] 1000曲規模を全コア並列でパースすると、File.ReadAllBytes+string.Split由来の
                //    一時アロケーションが極めて短時間に大量発生し、GCが追いつかずWorkingSetが
                //    数百MB～1GB級に張り付く(フォントスキャンと同じ現象)可能性がある。
                //    バックグラウンドタスクの最後で1回だけ明示的にGCを回して切り分ける。
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true, true);

                long wsAfterGc = Environment.WorkingSet;
                long heapAfterGc = GC.GetTotalMemory(false);

                long cacheEntries;
                lock (_songCache) { cacheEntries = _songCache.Count; }

                Console.WriteLine(
                    $"[PrewarmSongCache] files={paths.Count} cacheEntries={cacheEntries} elapsed={sw.ElapsedMilliseconds}ms\n" +
                    $"  before      : WorkingSet={wsBefore / 1024.0 / 1024.0:0.0}MB Heap={heapBefore / 1024.0 / 1024.0:0.0}MB\n" +
                    $"  afterParse  : WorkingSet={wsAfterParse / 1024.0 / 1024.0:0.0}MB Heap={heapAfterParse / 1024.0 / 1024.0:0.0}MB\n" +
                    $"  afterGC     : WorkingSet={wsAfterGc / 1024.0 / 1024.0:0.0}MB Heap={heapAfterGc / 1024.0 / 1024.0:0.0}MB");
            }
            catch { }
        });
    }

    static Encoding DetectFileEncoding(string path, out int bomLength)
    {
        bomLength = 0;
        long fileLen = new FileInfo(path).Length;
        int sampleSize = (int)Math.Min(65536L, fileLen);
        byte[] head = new byte[sampleSize];

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            int totalRead = 0;
            while (totalRead < sampleSize)
            {
                int n = fs.Read(head, totalRead, sampleSize - totalRead);
                if (n <= 0) break;
                totalRead += n;
            }
            if (totalRead < head.Length) Array.Resize(ref head, totalRead);
        }

        if (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) { bomLength = 3; return new UTF8Encoding(false); }
        if (head.Length >= 2 && head[0] == 0xFF && head[1] == 0xFE) { bomLength = 2; return Encoding.Unicode; }
        if (head.Length >= 2 && head[0] == 0xFE && head[1] == 0xFF) { bomLength = 2; return Encoding.BigEndianUnicode; }
        if (IsValidUtf8(head)) return new UTF8Encoding(false);
        return Encoding.GetEncoding("shift_jis");
    }

    static readonly char[] _lineBreakChars = { '\r', '\n' };

    static SongData ParseSongData(string tjaPath)
    {
        string title = Path.GetFileNameWithoutExtension(tjaPath);
        string titleJa = "";
        string subtitle = "";
        string subtitleJa = "";
        string wave = "";
        double demoStart = 0;
        string genre = "";
        int[] levels = new int[5];
        int currentCourse = 3;

        try
        {
            // 💡 [メモリ最適化・本命] 従来は File.ReadAllBytes でファイル全体をバイト配列化し、
            //    さらにそれを丸ごと1本のstringにデコードしてから処理していた。譜面ノーツ本体を
            //    含む大きいTJAだと1ファイルで数百KB〜数MBの一時オブジェクトが「一瞬だけ」でも
            //    同時に確保され、これが並列処理と重なるとGCヒープが一時的に3GB級まで
            //    膨張していた（GC後は数百MBまで戻るが、.NETはOSに借りたページをすぐには
            //    返却しないため、WorkingSet＝実メモリ使用量はピーク値のまま高止まりする）。
            //    ここではファイルを一切まるごとメモリに載せず、FileStream+StreamReaderで
            //    1行ずつストリーム処理する。瞬間的な保持量が常に数KB程度に収まるため、
            //    ファイル数や譜面サイズに関わらずピークメモリが跳ね上がらなくなる。
            Encoding enc = DetectFileEncoding(tjaPath, out int bomLength);

            using var fs = new FileStream(tjaPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan);
            if (bomLength > 0) fs.Seek(bomLength, SeekOrigin.Begin);
            using var reader = new StreamReader(fs, enc, detectEncodingFromByteOrderMarks: false);

            bool inNotes = false;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                ReadOnlySpan<char> trimmed = line.AsSpan().Trim();
                if (trimmed.Length == 0) continue;

                if (inNotes)
                {
                    if (trimmed.StartsWith("#END", StringComparison.OrdinalIgnoreCase)) inNotes = false;
                    continue;
                }

                if (trimmed.StartsWith("#START", StringComparison.OrdinalIgnoreCase))
                {
                    inNotes = true;
                    continue;
                }

                int colonIdx = trimmed.IndexOf(':');
                if (colonIdx == -1) continue;

                ReadOnlySpan<char> keySpan = trimmed.Slice(0, colonIdx).Trim();
                ReadOnlySpan<char> valSpan = trimmed.Slice(colonIdx + 1).Trim();

                if (keySpan.Equals("TITLE", StringComparison.OrdinalIgnoreCase)) title = valSpan.ToString();
                else if (keySpan.Equals("TITLEJA", StringComparison.OrdinalIgnoreCase)) titleJa = valSpan.ToString();
                else if (keySpan.Equals("SUBTITLE", StringComparison.OrdinalIgnoreCase))
                {
                    if ((valSpan.Length >= 2) && (valSpan.StartsWith("--") || valSpan.StartsWith("++")))
                        valSpan = valSpan.Slice(2);
                    subtitle = valSpan.ToString();
                }
                else if (keySpan.Equals("SUBTITLEJA", StringComparison.OrdinalIgnoreCase))
                {
                    if ((valSpan.Length >= 2) && (valSpan.StartsWith("--") || valSpan.StartsWith("++")))
                        valSpan = valSpan.Slice(2);
                    subtitleJa = valSpan.ToString();
                }
                else if (keySpan.Equals("GENRE", StringComparison.OrdinalIgnoreCase)) genre = valSpan.ToString();
                else if (keySpan.Equals("WAVE", StringComparison.OrdinalIgnoreCase))
                {
                    string dir = Path.GetDirectoryName(tjaPath) ?? ".";
                    string candidate = Path.Combine(dir, valSpan.ToString());
                    if (File.Exists(candidate)) wave = candidate;
                }
                else if (keySpan.Equals("DEMOSTART", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(valSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out double ds) && ds > 0)
                        demoStart = ds;
                }
                else if (keySpan.Equals("COURSE", StringComparison.OrdinalIgnoreCase))
                {
                    if (valSpan.Equals("easy", StringComparison.OrdinalIgnoreCase) || valSpan.SequenceEqual("0")) currentCourse = 0;
                    else if (valSpan.Equals("normal", StringComparison.OrdinalIgnoreCase) || valSpan.SequenceEqual("1")) currentCourse = 1;
                    else if (valSpan.Equals("hard", StringComparison.OrdinalIgnoreCase) || valSpan.SequenceEqual("2")) currentCourse = 2;
                    else if (valSpan.Equals("oni", StringComparison.OrdinalIgnoreCase) || valSpan.SequenceEqual("3")) currentCourse = 3;
                    else if (valSpan.Equals("edit", StringComparison.OrdinalIgnoreCase) || valSpan.SequenceEqual("4")) currentCourse = 4;
                }
                else if (keySpan.Equals("LEVEL", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(valSpan, out int lvl))
                    {
                        if (currentCourse >= 0 && currentCourse < 5) levels[currentCourse] = lvl;
                    }
                }
            }
        }
        catch { }

        return new SongData { TjaPath = tjaPath, Title = title, TitleJP = titleJa, Subtitle = subtitle, SubtitleJP = subtitleJa, WavePath = wave, DemoStart = demoStart, Genre = genre, Levels = levels };
    }


    static string GetEntryDisplayTitle(NavEntry entry)
    {
        if (entry is SongEntry se)
        {
            bool wantJapanese = SettingsPanel.IsLang;
            if (wantJapanese)
            {
                return !string.IsNullOrEmpty(se.Song.TitleJP) ? se.Song.TitleJP : se.Song.Title;
            }
            else
            {
                return se.Song.Title;
            }
        }

        return entry.DisplayTitle;
    }

    static void UpdateBgForFocus()
    {
        if (_currentEntries.Count == 0)
        {
            RequestSongPreview(null);
            return;
        }
        var focused = _currentEntries[_cursor];

        string genre = null;
        if (focused is BoxEntry be) genre = be.Def.Genre;
        else if (focused is SongEntry se && !string.IsNullOrEmpty(se.Song?.Genre)) genre = se.Song.Genre;
        else
        {
            var curDef = GetCurrentDirBoxDef();
            genre = curDef?.Genre;
        }

        string cat = null;
        if (TryGetGenreInfo(genre, out var catInfo)) cat = catInfo.Folder;
        SetActiveBgCategory(cat);

        // 💡 曲にフォーカスしている場合、WAVE/DEMOSTART指定に沿ったデモ再生を予約する
        RequestSongPreview(focused is SongEntry songEntry ? songEntry.Song : null);
    }

    /// <summary>
    /// 選曲カーソルのフォーカス対象が変わるたびに呼ぶ。実際のロード・再生は
    /// UpdateSongPreviewTimer 側で少し遅延させてから行い、素早いカーソル送り中の
    /// 無駄なデコード開始を避ける。
    /// </summary>
    static void RequestSongPreview(SongData song)
    {
        if (ReferenceEquals(song, _pendingPreviewSong)) return;
        _pendingPreviewSong = song;
    }

    /// <summary>
    /// 選択バーの展開アニメーション（Selected.png表示・_boxAnimTimerがGROW_START+GROWTH_DURATIONに達する）が
    /// 完了して初めてデモ再生を開始する。展開が終わる前に別の曲へカーソルが移動した場合は
    /// _pendingPreviewSongが書き換わるだけで再生自体が始まっていないため、その曲が流れっぱなしになることはない。
    /// </summary>
    static void UpdateSongPreviewTimer(float dt)
    {
        // 💡 フォーカスが移り、今鳴っているプレビューが「現在選んでいる曲」ではなくなったら、
        //    次の曲の展開アニメーションを待たずに即座に止める。
        //    （これをしないと、次の曲が展開し終わるまで前の曲の音がそのまま流れ続けてしまう）
        if (_previewingSong != null && !ReferenceEquals(_previewingSong, _pendingPreviewSong))
        {
            StopSongPreview();
            if (!_menuBgmActive) StartMenuBgm();
        }

        if (ReferenceEquals(_pendingPreviewSong, _previewingSong)) return;

        if (_pendingPreviewSong == null || string.IsNullOrEmpty(_pendingPreviewSong.WavePath))
        {
            return;
        }

        // 💡 展開アニメーションが完了するまでは再生開始を待つ
        if (_boxAnimTimer < GROW_START + GROWTH_DURATION) return;

        StartSongPreview(_pendingPreviewSong);
    }

    static void StartSongPreview(SongData song)
    {
        StopSongPreview();

        _musicPreview = MusicTrack.Load(song.WavePath);
        if (_musicPreview.Loaded)
        {
            // 💡 デモ再生中はメニューBGMを止めて曲のWAVEに差し替える
            StopMenuBgm();
            _musicPreview.Looping = true;
            _musicPreview.Volume = SettingsPanel.EffectiveBgmVolume;
            _musicPreview.Play(Math.Max(0.0, song.DemoStart));
            _previewingSong = song;
        }
        else
        {
            _musicPreview = null;
            _previewingSong = null;
        }
    }

    static void StopSongPreview()
    {
        // 💡 Dispose()は内部でデコードスレッドをJoin(500)するため、メインスレッドで直接呼ぶと
        //    曲送りのたびに最大500ms級のフリーズが起きていた。今から作る新しいプレビュートラックとは
        //    完全に独立したオブジェクトなので、後始末はバックグラウンドスレッドに任せて問題ない。
        var old = _musicPreview;
        if (old != null)
        {
            System.Threading.Tasks.Task.Run(() => old.Dispose());
        }
        _musicPreview = null;
        _previewingSong = null;
    }

    /// <summary>
    /// 再生中のデモ再生トラックを止めずに切り離して呼び出し元へ引き渡す（所有権の移譲）。
    /// song が現在プレビュー中の曲と一致する場合のみトラックを返し、以後このクラスは一切関与しない
    /// （破棄も含めて呼び出し先の責任になる）。一致しない・未再生の場合は null を返す。
    /// </summary>
    static MusicTrack DetachPreviewTrack(SongData song)
    {
        if (_musicPreview == null || !ReferenceEquals(_previewingSong, song)) return null;
        var track = _musicPreview;
        _musicPreview = null;
        _previewingSong = null;
        return track;
    }

    static SongData _pendingPreviewSong = null;

    static void LoadSongList()
    {
        if (!Directory.Exists(_songsRoot))
        {
            _currentDir = _songsRoot;
            _navStack.Clear();
            _currentEntries = new List<NavEntry>();
            _cursor = 0;
            ResetSongScroll();
            return;
        }

        UnloadAllNavAssetsAndClearStack();

        _currentDir = _songsRoot;
        _currentEntries = BuildEntries(_currentDir);
        _currentDirGenre = GetCurrentDirBoxDef()?.Genre ?? "";
        _cursor = 0;
        _boxAnimTimer = 99f;
        ResetSongScroll();
        UpdateBgForFocus();
    }

    static void EnterDirectory(string path)
    {
        _navStack.Push((_currentDir, _cursor, _currentEntries));

        int openIndex = _cursor;
        var children = ApplyReturnEntries(BuildEntries(path));

        var merged = new List<NavEntry>(_currentEntries);
        merged.RemoveAt(openIndex);
        merged.InsertRange(openIndex, children);

        _currentDir = path;
        _currentEntries = merged;
        _currentDirGenre = GetCurrentDirBoxDef()?.Genre ?? "";
        _cursor = merged.Count == 0 ? 0 : Math.Min(openIndex, merged.Count - 1);
        _boxAnimTimer = 99f;
        ResetSongScroll();
        PlayFocusSoundIfBox();
        UpdateBgForFocus();
    }

    static void EnterVirtualDirectory(BoxEntry box)
    {
        _navStack.Push((_currentDir, _cursor, _currentEntries));
        _currentDirGenre = box.Def?.Genre ?? "";

        int openIndex = _cursor;
        var children = new List<NavEntry>();
        var virtualSongs = ParseSongDataBatch(box.VirtualTjaPaths).ToList();
        virtualSongs.Sort(CompareSongEntry);
        foreach (var song in virtualSongs)
        {
            children.Add(new SongEntry { Song = song, DisplayTitle = song.Title });
        }
        children = ApplyReturnEntries(children);

        var merged = new List<NavEntry>(_currentEntries);
        merged.RemoveAt(openIndex);
        merged.InsertRange(openIndex, children);

        _currentDir = box.DisplayTitle;
        _currentEntries = merged;
        _cursor = merged.Count == 0 ? 0 : Math.Min(openIndex, merged.Count - 1);
        _boxAnimTimer = 99f;
        ResetSongScroll();
        UpdateBgForFocus();
    }

    static void ExitDirectory()
    {
        if (_navStack.Count == 0) return;

        var popData = _navStack.Pop();
        var keepSet = new HashSet<NavEntry>(popData.entries);

        foreach (var e in _currentEntries)
        {
            if (e is BoxEntry be && !keepSet.Contains(e)) UnloadBoxAssets(be.Def);
            else if (e is SongEntry se && se.OwnerDef != null && !keepSet.Contains(e)) UnloadBoxAssets(se.OwnerDef);
        }

        _currentDir = popData.dir;
        _currentEntries = popData.entries;
        _currentDirGenre = GetCurrentDirBoxDef()?.Genre ?? "";
        _cursor = _currentEntries.Count == 0 ? 0 : Math.Min(popData.cursor, _currentEntries.Count - 1);
        _boxAnimTimer = 99f;
        ResetSongScroll();
        UpdateBgForFocus();
    }

    static void PlayFocusSoundIfBox()
    {
        if (_currentEntries.Count == 0) return;
        if (_currentEntries[_cursor] is BoxEntry be && be.Def.FocusSoundLoaded)
        {
            be.Def.FocusSound?.Play();
        }
    }

    /// <summary>
    /// Box(ジャンル)決定演出の毎フレーム更新。
    /// 0〜WAIT秒: 何もせず待機（Xスケール1のまま）
    /// WAIT〜WAIT+SHRINK秒: Xスケールを1→0へ縮小
    /// 縮小完了時: 実際にEnterDirectory/EnterVirtualDirectoryを呼び、表示をSongの箱へ切り替える
    /// 切替後〜+EXPAND秒: Xスケールを0→1へ拡大
    /// 完了後: 演出終了、通常の展開アニメ(_boxAnimTimer)を最初からやり直す
    /// </summary>
    static void UpdateBoxTransition()
    {
        float counter = GetBoxAnimationCounter(_boxTransitionTimer);
        if (!_boxTransitionSwitched && counter >= BOX_ANIM_COUNTER_SWITCH)
        {
            _boxTransitionSwitched = true;
            var boxEntry = _boxTransitionEntry;
            _boxTransitionEntry = null;

            if (boxEntry != null)
            {
                if (_navStack.Count > 0 && _navStack.Peek().entries.Contains(boxEntry))
                {
                    ExitDirectory();
                    int idx = _currentEntries.IndexOf(boxEntry);
                    if (idx >= 0) _cursor = idx;
                }

                if (boxEntry.VirtualTjaPaths != null) EnterVirtualDirectory(boxEntry);
                else EnterDirectory(boxEntry.FolderPath);
            }
        }

        if (counter >= BOX_ANIM_COUNTER_END)
        {
            _boxTransitionActive = false;
            _boxOpenTimer = 99f;
            _boxAnimTimer = GROW_START + GROWTH_DURATION + 1f;
        }

#if false

        float shrinkStart = BOX_TRANS_WAIT;
        float shrinkEnd = shrinkStart + BOX_TRANS_SHRINK;
        float expandStart = shrinkEnd;
        float expandEnd = expandStart + BOX_TRANS_EXPAND;

        if (_boxTransitionTimer < shrinkStart)
        {
            _boxTransitionXScale = 1f;
            return;
        }

        if (_boxTransitionTimer < shrinkEnd)
        {
            float p = (_boxTransitionTimer - shrinkStart) / BOX_TRANS_SHRINK;
            _boxTransitionXScale = Math.Max(0f, 1f - p); // 1 → 0
            return;
        }

        // 💡 縮小が完了した瞬間に、まだ箱を切り替えていなければ切り替える（Songの箱へ）
        if (_boxTransitionEntry != null)
        {
            var boxEntry = _boxTransitionEntry;
            _boxTransitionEntry = null;

            if (_navStack.Count > 0 && _navStack.Peek().entries.Contains(boxEntry))
            {
                ExitDirectory();
                int idx = _currentEntries.IndexOf(boxEntry);
                if (idx >= 0) _cursor = idx;
            }
            if (boxEntry.VirtualTjaPaths != null) EnterVirtualDirectory(boxEntry);
            else EnterDirectory(boxEntry.FolderPath);

            _boxOpenTimer = 0f; // 💡 新リストへの切替と同時にBOX展開演出（散開→収束）を開始する
        }

        if (_boxTransitionTimer < expandEnd)
        {
            float p = (_boxTransitionTimer - expandStart) / BOX_TRANS_EXPAND;
            _boxTransitionXScale = Math.Min(1f, p); // 0 → 1
        }
        else
        {
            _boxTransitionXScale = 1f;
            _boxTransitionActive = false;
            _boxAnimTimer = GROW_START + GROWTH_DURATION + 1f; // 💡 展開アニメをスキップして即座に展開完了状態にする
        }
    }

    /// <summary>フォルダを閉じるアニメーション演出を開始する。</summary>
#endif
    }

    static void BeginBoxClose()
    {
        if (_navStack.Count == 0) return; // 閉じられる階層がなければ何もしない
        _boxCloseActive = true;
        _boxCloseTimer = 0f;
        _boxCloseExitDone = false;
        _boxOpenTimer = 0f; // 閉じるときも散開演出を再生する
        StartBarSelectFlash(); // 💡 Nijiiro同様、フォルダを閉じる瞬間にもストロボ演出を再生する
        PlayDonSound();
    }

    /// <summary>
    /// フォルダを「閉じる」演出の毎フレーム更新。
    /// 0〜BOX_CLOSE_SHRINK秒: 選択中バーのXスケールを1→0へ縮小
    /// 縮小完了時: ExitDirectory()を呼び、親リストへ戻す
    /// 〜BOX_CLOSE_EXPAND秒: Xスケールを0→1へ拡大して完了
    /// </summary>
    static void UpdateBoxClose()
    {
        float counter = GetBoxAnimationCounter(_boxCloseTimer);
        if (!_boxCloseExitDone && counter >= BOX_ANIM_COUNTER_SWITCH)
        {
            _boxCloseExitDone = true;
            ExitDirectory();
        }

        if (counter >= BOX_ANIM_COUNTER_END)
        {
            _boxCloseActive = false;
            _boxOpenTimer = 99f;
            _boxAnimTimer = GROW_START + GROWTH_DURATION + 1f;
        }

#if false

        if (_boxCloseTimer < BOX_CLOSE_SHRINK)
        {
            float p = _boxCloseTimer / BOX_CLOSE_SHRINK;
            // イーズイン（sin後半）: 最初はゆっくり、最後にパッと消える
            _boxCloseXScale = Math.Max(0f, 1f - (float)Math.Sin(p * Math.PI / 2.0));
            return;
        }

        // 縮小完了時に ExitDirectory() を一度だけ呼ぶ
        if (!_boxCloseExitDone)
        {
            _boxCloseExitDone = true;
            ExitDirectory();
        }

        float expandElapsed = _boxCloseTimer - BOX_CLOSE_SHRINK;
        if (expandElapsed < BOX_CLOSE_EXPAND)
        {
            float p = expandElapsed / BOX_CLOSE_EXPAND;
            // イーズアウト（sin前半）: すっと広がる
            _boxCloseXScale = Math.Min(1f, (float)Math.Sin(p * Math.PI / 2.0));
        }
        else
        {
            _boxCloseXScale = 1f;
            _boxCloseActive = false;
            _boxAnimTimer = GROW_START + GROWTH_DURATION + 1f; // 💡 展開アニメをスキップして即座に展開完了状態にする
        }
    }

#endif
    }

    /// <summary>
    /// 選択ハイライト(Bar_Select.png)の「点滅ストロボ→常時呼吸フェード」演出を開始する。
    /// Nijiiro側の ctBarFlash.Start(0, 2700, ...) に相当。フォルダを開く/閉じるタイミングで呼び出す。
    /// </summary>
    static void StartBarSelectFlash()
    {
        _barFlashElapsedMs = 0f;
        _barFlashActive = true;
        _selectFadeElapsedMs = 0f;
    }

    /// <summary>毎フレーム呼び出す、選択ハイライトのタイマー更新（フォルダ開閉中も含め常に進行させる）。</summary>
    static void UpdateBarSelectAnimation()
    {
        float dtMs = Raylib.GetFrameTime() * 1000f;

        if (_barFlashActive)
        {
            _barFlashElapsedMs += dtMs;
            if (_barFlashElapsedMs >= BAR_FLASH_DURATION_MS)
            {
                _barFlashElapsedMs = BAR_FLASH_DURATION_MS;
                _barFlashActive = false; // 点滅シーケンス終了 → 以後は常時呼吸フェードへ切り替わる
            }
        }

        // 呼吸フェードは「点滅が終わった後」だけ進行させる（Nijiiroの ctSelectFadeAnime.TickLoop() 開始条件と同じ）
        if (!_barFlashActive)
        {
            _selectFadeElapsedMs += dtMs;
            if (_selectFadeElapsedMs >= SELECT_FADE_LOOP_MS) _selectFadeElapsedMs -= SELECT_FADE_LOOP_MS;
        }
    }

    /// <summary>
    /// 「成長」レイヤーの不透明度。点滅中は0〜700msでフル表示、700〜1000msでフェードアウトし、
    /// 点滅終了後は箱の展開進捗(focusEnvelope)にそのまま追従する（Nijiiroの ctBarFlash.IsEnded分岐に相当）。
    /// </summary>
    static byte GetGrowingLayerOpacity(float focusEnvelope01)
    {
        if (!_barFlashActive)
            return (byte)Math.Clamp(focusEnvelope01 * 255f, 0f, 255f);

        float raw = 255f - (_barFlashElapsedMs - 700f) * 2.55f;
        return (byte)Math.Clamp(raw, 0f, 255f);
    }

    /// <summary>
    /// 「呼吸」レイヤーの不透明度。点滅終了後、300ms fade-in→400ms保持→300ms fade-outを1000msループで繰り返す。
    /// 点滅中は成長レイヤーと同じ値を共有する（Nijiiro実装の踏襲）。
    /// </summary>
    static byte GetBreathingLayerOpacity(float focusEnvelope01)
    {
        if (_barFlashActive) return GetGrowingLayerOpacity(focusEnvelope01);

        float t = _selectFadeElapsedMs;
        float env = t <= 300f ? t / 300f
                  : t <= 700f ? 1f
                  : 1f - (t - 700f) / 300f;
        return (byte)Math.Clamp(focusEnvelope01 * env * 255f, 0f, 255f);
    }

    /// <summary>
    /// フォルダを開閉した瞬間だけ入る、8段階の素早いストロボ点滅（0〜800msのみ有効）。
    /// Nijiiroの [ BarFlash ] リージョンと同じ三角波パターン。
    /// </summary>
    static byte GetFlashStrobeOpacity()
    {
        if (!_barFlashActive) return 0;
        float v = _barFlashElapsedMs;
        if (v > 800f) return 0;

        float raw =
            v <= 100f ? v * 2.55f :
            v <= 200f ? 255f - (v - 100f) * 2.55f :
            v <= 300f ? (v - 200f) * 2.55f :
            v <= 400f ? 255f - (v - 300f) * 2.55f :
            v <= 500f ? (v - 400f) * 2.55f :
            v <= 600f ? 255f - (v - 500f) * 2.55f :
            v <= 700f ? (v - 600f) * 2.55f :
                         255f - (v - 700f) * 2.55f;
        return (byte)Math.Clamp(raw, 0f, 255f);
    }

    /// <summary>
    /// Bar_Select.png の指定区画(segIndex: 0=成長, 1=呼吸, 2=ストロボ)を
    /// 上端キャップ/伸縮中央/下端キャップの3枚に分けて描画する（角が歪まないようにするため）。
    /// </summary>
    static void DrawBarSelectSlice(int segIndex, float centerX, float centerY, float destW, float destH, Color tint)
    {
        if (!_barSelectLoaded || tint.A == 0 || destW <= 0f || destH <= 0f) return;

        int segH = _texBarSelect.Height / 3;
        int capH = Math.Max(1, segH / 4);
        int segY = segIndex * segH;

        // 💡 selWが横に大きく伸びた場合でもキャップが肥大化しすぎないようスケールを頭打ちにする
        //    （キャップが伸びすぎると中央の伸縮部分が潰れ、上下のキャップだけが離れた位置に見えてしまうため）
        float scale = Math.Min(1.5f, destW / _texBarSelect.Width);
        float capDrawH = Math.Min(destH * 0.3f, capH * scale);
        float midDrawH = Math.Max(0f, destH - capDrawH * 2f);

        // 下端キャップ（画像側の下端＝角丸コーナー部分を使用）
        var srcBottom = new Rectangle(0, segY + capH * 3, _texBarSelect.Width, capH);
        var destBottom = new Rectangle(centerX, centerY + destH / 2f, destW, capDrawH);
        Raylib.DrawTexturePro(_texBarSelect, srcBottom, destBottom, new Vector2(destW / 2f, capDrawH), 0f, tint);

        // 伸縮する中央部分（センターバーの高さ変化に追従してここだけY方向に伸縮する）
        if (midDrawH > 0f)
        {
            var srcMid = new Rectangle(0, segY + capH, _texBarSelect.Width, capH);
            var destMid = new Rectangle(centerX, centerY, destW, midDrawH);
            Raylib.DrawTexturePro(_texBarSelect, srcMid, destMid, new Vector2(destW / 2f, midDrawH / 2f), 0f, tint);
        }

        // 上端キャップ（画像側の上端＝角丸コーナー部分を使用）
        var srcTop = new Rectangle(0, segY, _texBarSelect.Width, capH);
        var destTop = new Rectangle(centerX, centerY - destH / 2f, destW, capDrawH);
        Raylib.DrawTexturePro(_texBarSelect, srcTop, destTop, new Vector2(destW / 2f, 0f), 0f, tint);
    }

    static void UpdateSongSelect()
    {
        UpdateBarSelectAnimation(); // 💡 フォルダ開閉中も含め、ハイライト演出タイマーは常に進行させる

        ApplyPendingSearch(); // 設定画面の曲検索ジャンプを反映（テキスト部分一致）
        ApplyPendingSearchPath(); // 設定画面の検索候補選択によるジャンプを反映（TjaPath直接指定）
        DonChanScene.Update("don_result_clear_loop");

        if (_animeGuide != null)
        {
            _animeGuideTime += Raylib.GetFrameTime();
            double dur = _animeGuide.Duration;
            if (dur > 0) _animeGuideTime = (float)(_animeGuideTime % dur);
        }

        if (_boxTransitionActive)
        {
            UpdateBoxTransition();
            return;
        }

        if (_boxCloseActive)
        {
            UpdateBoxClose();
            return;
        }

        if (SettingsPanel.Open) return;

        if (Raylib.IsKeyPressed(KeyboardKey.F2))
        {
            Kisekae.Init();
            _wantsKisekae = true;
            return;
        }

        if (_currentEntries.Count == 0) return;

        // 参照側と同じく、スクロールが終わるまでは次の入力を受け付けない。
        // ただしKaダブル押しのタイマー計測だけはスクロール中でも行う。
        if (IsSongScrollActive)
        {
            bool kaUpDuring = Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressed(KeyboardKey.Left) || SettingsPanel.IsAnyKaUpPressed();
            bool kaDownDuring = Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressed(KeyboardKey.Right) || SettingsPanel.IsAnyKaDownPressed();
            bool kaAny = kaUpDuring || kaDownDuring;
            if (kaAny)
            {
                // 連続スキップモード中はスクロール中の入力も空白扱い（無視）。
                // 通常の1回目→2回目判定はダブル押しウィンドウで行う。
                if (!_skipChainActive && _kaDoublePressTimer <= KA_DOUBLE_PRESS_WINDOW)
                {
                    TriggerSkip(kaUpDuring);
                }
                else if (!_skipChainActive)
                {
                    _kaDoublePressTimer = 0f;
                }
            }
            return;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            if (_navStack.Count > 0)
            {
                DonChanScene.PlayOneShot("don_full_combo");
                BeginBoxClose();
                _exitConfirmTimer = 0f;
            }
            else if (_exitConfirmTimer > 0f) _shouldExit = true;
            else _exitConfirmTimer = 3.0f;
            return;
        }

        // ==================================================================
        // 💡 スキップ発動後は SKIP_COOLDOWN秒、完全に入力を無視する（空白）。
        //    空白明け後 KA_DOUBLE_PRESS_WINDOW 秒以内にKa2が押されたら継続スキップ。
        //    それを過ぎたらチェーンは切れて、また最初のダブル押しからやり直し。
        // ==================================================================
        if (_skipPlayTimer < SKIP_COOLDOWN) return;

        bool kaUpPressed = Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressed(KeyboardKey.Left) || SettingsPanel.IsAnyKaUpPressed();
        bool kaDownPressed = Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressed(KeyboardKey.Right) || SettingsPanel.IsAnyKaDownPressed();
        bool kaPressed = kaUpPressed || kaDownPressed;

        if (kaPressed)
        {
            if (_skipChainActive)
            {
                if (_skipPlayTimer <= SKIP_COOLDOWN + KA_DOUBLE_PRESS_WINDOW)
                {
                    // 空白明け直後のウィンドウ内 → 継続スキップ
                    TriggerSkip(kaUpPressed);
                    return;
                }
                // ウィンドウを過ぎた → チェーン解除、以降は通常のダブル押し判定に戻す
                _skipChainActive = false;
            }

            if (_kaDoublePressTimer <= KA_DOUBLE_PRESS_WINDOW)
            {
                // ダブル押し確定 → 7曲スキップ（今回の方向で決める）
                TriggerSkip(kaUpPressed);
                return;
            }

            // 1回目（またはウィンドウ外）→ 通常の1曲移動 ＆ タイマー開始
            _kaDoublePressTimer = 0f;
            _skipChainActive = false;
            if (kaUpPressed)
            {
                int previousCursor = _cursor;
                _cursor = (_cursor - 1 + _currentEntries.Count) % _currentEntries.Count;
                BeginSongScroll(previousCursor, -1);
                _boxAnimTimer = 0f; _exitConfirmTimer = 0f;
                PlayKaSound();
                PlayFocusSoundIfBox();
                UpdateBgForFocus();
            }
            else
            {
                int previousCursor = _cursor;
                _cursor = (_cursor + 1) % _currentEntries.Count;
                BeginSongScroll(previousCursor, 1);
                _boxAnimTimer = 0f; _exitConfirmTimer = 0f;
                PlayKaSound();
                PlayFocusSoundIfBox();
                UpdateBgForFocus();
            }
        }

        if (SettingsPanel.IsAnyDonPressed() || Raylib.IsKeyPressed(KeyboardKey.Enter))
        {
            _exitConfirmTimer = 0f;
            PlayDonSound();
            var entry = _currentEntries[_cursor];

            if (entry is BoxEntry boxEntry)
            {
                // 💡 即座にフォルダへ入らず、「1秒待機→0.5秒でXスケール0→Songの箱に切替→0.5秒でXスケールを元に戻す」演出を開始する
                _boxTransitionActive = true;
                _boxTransitionTimer = 0f;
                _boxTransitionEntry = boxEntry;
                _boxTransitionSwitched = false;
                _boxOpenTimer = 0f;
                StartBarSelectFlash(); // 💡 Nijiiro同様、フォルダを開く瞬間にストロボ演出を再生する
                DonChanScene.PlayOneShot("don_full_combo");
            }
            else if (entry is ReturnEntry)
            {
                DonChanScene.PlayOneShot("don_full_combo");
                BeginBoxClose();
            }
            else if (entry is SongEntry songEntry)
            {
                _selectedSong = songEntry.Song;
                _selectedSongGenre = !string.IsNullOrEmpty(songEntry.Song.Genre)
                    ? songEntry.Song.Genre
                    : songEntry.OwnerDef?.Genre ?? _currentDirGenre;
                // 💡 停止せずに再生中のデモをそのまま難易度選択画面へ引き継ぐ（頭出しし直さず途切れなく続ける）
                MusicTrack handoffTrack = DetachPreviewTrack(_selectedSong);
                _pendingPreviewSong = null;
                DiffSelectScene.Prepare(_selectedSong, handoffTrack);
                _songConfirmed = true;
                _boxAnimTimer = 0f;
            }
        }
    }

    static Texture2D _texBackBar;
    static bool _backBarLoaded = false;

    static void LoadReturnEntryAssets()
    {
        // 💡 他のバーと同じく bar.png 1枚のみを読み込み、GetBarSourceRects/GetBarDimensionsに
        //    角丸の厚み分を自動でスライスさせる（backUp.png/backDown.pngは使わない）
        string backDir = Path.Combine(_skinRoot, "0.Songs", "category", "other");
        string barPath = Path.Combine(backDir, "bar.png");
        if (!File.Exists(barPath)) barPath = Path.Combine(backDir, "back.png"); // 互換用フォールバック

        if (File.Exists(barPath)) { _texBackBar = Raylib.LoadTexture(barPath); _backBarLoaded = _texBackBar.Id != 0; }
    }

    static void UnloadReturnEntryAssets()
    {
        if (_backBarLoaded) { Raylib.UnloadTexture(_texBackBar); _backBarLoaded = false; }
    }

    static (Texture2D up, Texture2D mid, Texture2D down, bool loaded, Color tint) GetSlotVisual3Part(NavEntry entry)
    {
        // 💡 bar.png 1枚管理：up/mid/downは全て同じテクスチャを指す（描画時にGetBarSourceRects/
        //    GetBarDimensionsが角丸の厚み分を自動でスライスする）
        Texture2D up = _texDefBar;
        Texture2D mid = _texDefBar;
        Texture2D down = _texDefBar;
        bool loaded = _defBarLoaded;
        Color tint = Color.White;

        if (entry is ReturnEntry)
        {
            if (_backBarLoaded)
            {
                return (_texBackBar, _texBackBar, _texBackBar, true, Color.White);
            }
            tint = new Color((byte)255, (byte)100, (byte)100, (byte)255);
        }
        else if (entry is BoxEntry be)
        {
            var def = be.Def;
            tint = def.BoxColor;

            if (def.BarLoaded) { up = def.TexBar; mid = def.TexBar; down = def.TexBar; loaded = true; }
        }
        else if (entry is SongEntry se && se.OwnerDef != null)
        {
            var def = se.OwnerDef;
            tint = def.BoxColor;

            if (def.BarLoaded) { up = def.TexBar; mid = def.TexBar; down = def.TexBar; loaded = true; }
        }

        return (up, mid, down, loaded, tint);
    }

    static float GetSlotTargetY(int slotIndex, float listCenterY, float curBoxH_sel, float baseH_unsel, float gapSize, float selGapExtra = 0f)
    {
        if (slotIndex < 0)
        {
            float firstTopCenter = listCenterY - (curBoxH_sel / 2f) - (baseH_unsel / 2f) - gapSize - selGapExtra;
            return firstTopCenter + (slotIndex + 1) * (baseH_unsel + gapSize);
        }
        else if (slotIndex > 0)
        {
            float firstBottomCenter = listCenterY + (curBoxH_sel / 2f) + (baseH_unsel / 2f) + gapSize + selGapExtra;
            return firstBottomCenter + (slotIndex - 1) * (baseH_unsel + gapSize);
        }
        return listCenterY;
    }

    static void TriggerSkip(bool kaUp)
    {
        _kaDoublePressTimer = float.MaxValue;
        _skipChainActive = true;
        int previousCursor = _cursor;
        int dir = kaUp ? -SKIP_COUNT : SKIP_COUNT;
        _cursor = ((_cursor + dir) % _currentEntries.Count + _currentEntries.Count) % _currentEntries.Count;
        BeginSongScroll(previousCursor, Math.Sign(dir));
        _boxAnimTimer = 0f; _exitConfirmTimer = 0f;
        PlayFocusSoundIfBox();
        UpdateBgForFocus();
        if (_sndSkipLoaded) { _sndSkip.Volume = SettingsPanel.EffectiveSeVolume; _sndSkip.Play(); }
        _skipPlayTimer = 0f;
    }

    static bool IsSongScrollActive => _songScrollTimer < SONG_SCROLL_DURATION;

    // OpenTaiko の fNowScrollAnime と同じ ease-out sine。
    static float GetSongScrollProgress()
    {
        if (!IsSongScrollActive) return 1f;
        float normalized = Math.Clamp(_songScrollTimer / SONG_SCROLL_DURATION, 0f, 1f);
        return (float)Math.Sin(normalized * Math.PI / 2.0);
    }

    static void BeginSongScroll(int previousCursor, int direction)
    {
        _songScrollFromCursor = previousCursor;
        _songScrollTimer = 0f;
        _lastMoveDir = direction;
    }

    static void ResetSongScroll()
    {
        _songScrollTimer = 99f;
        _songScrollFromCursor = _cursor;
        _lastMoveDir = 0;
    }

    // ==================================================================
    // 💡 選択バーの「展開」演出タイミング調整用パラメータ
    //    （選択してから何秒待って、何秒かけて展開するかを外部から変更可能）
    // ==================================================================
    public static float SelectAnimWaitSeconds = 0.5f;   // 選択してから展開が始まるまでの待ち時間（秒）
    public static float SelectAnimGrowDuration = 0.1f;  // 展開（拡大）にかかる時間（秒）
    private static float GROW_START => SelectAnimWaitSeconds;
    private static float GROWTH_DURATION => SelectAnimGrowDuration;

    public static float SubtitleFadeDuration = 0.25f;   // サブタイトルのフェードイン＆上昇にかかる時間（秒）
    public static float SubtitleRiseDistance = 15f;     // サブタイトルが下から上に上がる距離（px、1280x720基準）

    static int _lastMoveDir = 0;

    static float GetSelectionFocusProgress(float t)
    {
        if (t < GROW_START) return 0f;
        if (t < GROW_START + GROWTH_DURATION)
        {
            float p = Math.Clamp((t - GROW_START) / GROWTH_DURATION, 0f, 1f);
            return (float)Math.Sin(p * Math.PI / 2.0);
        }
        return 1f;
    }

    static float GetSlotMidScale(int i, float t)
    {
        if (i == 0)
        {
            return UNSELECTED_MID_SCALE
                + (SelectedMidGrowScale - UNSELECTED_MID_SCALE) * GetSelectionFocusProgress(t);
        }
        return UNSELECTED_MID_SCALE;
    }

    static void DrawSongSelectMain()
    {
        int sw = Program.VirtualWidth;
        int sh = Program.VirtualHeight;

        if (_currentEntries.Count == 0)
        {
            string emptyMsg = _navStack.Count > 0 ? "このフォルダは空です" : $"{_songsRoot}/ に TJA やフォルダが見つかりません";
            G.DrawTextWithOutline16(G.Font, emptyMsg, new Vector2(sw * 0.4f, sh * 0.5f), 28, Color.White, Color.Black);
            var (evx, evy, evw, evh) = Songs.GetViewport();
            DonChanScene.X = DonChanX;
            DonChanScene.BottomY = DonChanBottomY;
            DonChanScene.Width = DonChanWidth;
            DonChanScene.Draw(evx, evy, (float)evw / 1280f);

            NamePlate.GlobalX = NamePlateGlobalX;
            NamePlate.GlobalY = NamePlateGlobalY;
            NamePlate.Draw(evx, evy, (float)evw / 1280f);

            // 💡 Demo画面下部と同じ「フリープレイ」表示。SongSelectではフリープレイ時のみ表示する
            DrawFreePlayBottomText(evx, evy, evw, evh);
            return;
        }

        var (vx, vy, vw, vh) = Songs.GetViewport();

        var focusedEntry = _currentEntries[_cursor];
        if (focusedEntry is BoxEntry bgBoxEntry && bgBoxEntry.Def.BgLoaded)
        {
            // 💡 カテゴリ背景(background.png)と同じく、縦フィット＋横スクロールのタイル張りで描画する。
            //    以前は1枚をビューポート全体に引き伸ばすだけだったため、タイル柄の画像が
            //    巨大に拡大され、スクロールアニメーションも効かなかった。
            DrawTiledVerticalFitBackground(bgBoxEntry.Def.TexBg, vx, vy, vw, vh, _bgScrollX, 255);
        }

        float scaleFactor = (float)vw / 2000;
        float listCenterY = vy + vh * 0.5f;
        float listCenterX = vx + vw * 0.5f;

        int visibleSideTop = 5;
        int visibleSideBottom = 5;

        float t = _boxAnimTimer;
        float selectionFocusProgress = GetSelectionFocusProgress(t);

        float gap = 7.5f * scaleFactor;
        float selGapExtra = SELECTED_GAP_EXTRA * scaleFactor; // 💡 選択中⇔非選択の境目だけ追加で空ける隙間

        // 💡 スコアランク/王冠アイコンは縦積みにすると非選択バーの高さをはみ出すことがあり、
        //    ループ内でそのまま描画すると後から描かれる隣のバーに絵が隠されてしまう。
        //    そのため描画は一旦キューに貯めておき、全バーを描き終えた後にまとめて最前面へ描画する。
        var pendingBestIcons = new List<(SongData Song, float SlotCenterX, float Iy, float BarW, float TotalHUnsel, float TotalHFull, float ScaleFactor, float Progress, byte Alpha)>();

        bool songScrollActive = IsSongScrollActive;
        float songScrollProgress = GetSongScrollProgress();
        int oldCursor = Math.Clamp(_songScrollFromCursor, 0, _currentEntries.Count - 1);
        var oldFocusVisual = GetSlotVisual3Part(_currentEntries[oldCursor]);
        var oldFocusDims = GetBarDimensions(oldFocusVisual.up, oldFocusVisual.mid, oldFocusVisual.down, oldFocusVisual.loaded, scaleFactor);
        float oldFocusUpH = oldFocusDims.upH;
        float oldFocusMidH = oldFocusDims.midH * SelectedMidGrowScale;
        float oldFocusDownH = oldFocusDims.downH;
        float oldFocusFullH = oldFocusUpH + oldFocusMidH + oldFocusDownH;

        var newFocusVisual = GetSlotVisual3Part(_currentEntries[_cursor]);
        var newFocusDims = GetBarDimensions(newFocusVisual.up, newFocusVisual.mid, newFocusVisual.down, newFocusVisual.loaded, scaleFactor);
        float newFocusUpH = newFocusDims.upH;
        float newFocusMidH = newFocusDims.midH * SelectedMidGrowScale;
        float newFocusDownH = newFocusDims.downH;
        float newFocusFullH = newFocusUpH + newFocusMidH + newFocusDownH;

        for (int i = -visibleSideTop; i <= visibleSideBottom; i++)
        {
            int idx = (_cursor + i + _currentEntries.Count * 100) % _currentEntries.Count;
            bool isCurrent = (i == 0);
            bool visualCurrent = isCurrent && !songScrollActive;
            NavEntry entry = _currentEntries[idx];

            float slotCenterX = listCenterX + i * (DIAGONAL_OFFSET_PER_STEP * scaleFactor);

            var (texUp, texMid, texDown, isLoaded, tintColor) = GetSlotVisual3Part(entry);

            var barDims = GetBarDimensions(texUp, texMid, texDown, isLoaded, scaleFactor);
            float upH = barDims.upH;
            float midH_base = barDims.midH;
            float downH = barDims.downH;
            float barW = barDims.barW;

            int oldRelativeSlot = songScrollActive ? i + _lastMoveDir : i;
            bool outgoingDuringScroll = songScrollActive && oldRelativeSlot == 0;
            float outgoingProgress = outgoingDuringScroll ? 1f - songScrollProgress : 0f;
            float midScale;
            if (outgoingDuringScroll)
            {
                // 旧カーソルのバーだけ、移動中に選択状態から通常状態へ縮める。
                midScale = UNSELECTED_MID_SCALE
                    + (SelectedMidGrowScale - UNSELECTED_MID_SCALE) * outgoingProgress;
            }
            else if (isCurrent)
            {
                // 新しいカーソルのバーは中央へ到着してから展開を始める。
                midScale = songScrollActive ? UNSELECTED_MID_SCALE : GetSlotMidScale(0, t);
            }
            else
            {
                midScale = UNSELECTED_MID_SCALE;
            }
            // 💡 フォルダ開閉演出中は、選択バーのmidScaleを常に最大(SelectedMidGrowScale)に固定する。
            //    演出中に_boxAnimTimerが0からやり直されると一瞬UNSELECTED_MID_SCALEになってしまうのを防ぐ。
            if (isCurrent && (_boxTransitionActive || _boxCloseActive)) midScale = SelectedMidGrowScale;
            float currentMidH = midH_base * midScale;

            float totalH_unsel = upH + (midH_base * UNSELECTED_MID_SCALE) + downH;

            float oldX = listCenterX + oldRelativeSlot * (DIAGONAL_OFFSET_PER_STEP * scaleFactor);
            float newX = listCenterX + i * (DIAGONAL_OFFSET_PER_STEP * scaleFactor);
            float oldY = GetSlotTargetY(oldRelativeSlot, listCenterY, oldFocusFullH, totalH_unsel, gap, selGapExtra);
            float newY = GetSlotTargetY(i, listCenterY, newFocusFullH, totalH_unsel, gap, selGapExtra);
            float layoutProgress = songScrollActive ? songScrollProgress : 1f;
            slotCenterX = oldX + (newX - oldX) * layoutProgress;
            float iy = oldY + (newY - oldY) * layoutProgress;

            // 💡 BOX展開演出：フォルダを開いたときのみ、中心以外のバーを外側へ散らしてから収束させる
            var genreBoxAnimation = GetReferenceGenreBoxAnimation(i, _boxOpenTimer);
            float opacity = genreBoxAnimation.Opacity;
            float boxContentOpacity = GetBoxAnimationContentOpacity(_boxOpenTimer);
            float boxCenterHorizontalScale = visualCurrent
                ? GetBoxCenterHorizontalScale(_boxOpenTimer)
                : 1f;
#if false
            float boxOpenScatter01 = 0f;
            if (boxOpenScatter01 > 0f)
            {
                float dirSign = Math.Sign(i);
                float absI = Math.Abs(i);
                float arc = (float)Math.Sin(boxOpenScatter01 * Math.PI * 0.5);
                // 散開フェーズ中はフェードアウト、収束フェーズ中はフェードイン
                float scatterProgress = Math.Min(1f, _boxOpenTimer / BOX_OPEN_SCATTER);
                float returnProgress = _boxOpenTimer > BOX_OPEN_SCATTER + BOX_OPEN_HOLD
                    ? Math.Min(1f, (_boxOpenTimer - BOX_OPEN_SCATTER - BOX_OPEN_HOLD) / BOX_OPEN_RETURN)
                    : 0f;
                opacity = _boxOpenTimer < BOX_OPEN_SCATTER + BOX_OPEN_HOLD
                    ? Math.Max(0f, 1f - scatterProgress)
                    : returnProgress;
                float scatterX = dirSign * (30f + 18f * absI) * scaleFactor * arc;
                float scatterY = dirSign * (280f + 120f * absI) * scaleFactor * arc;
            }
#endif
            slotCenterX -= genreBoxAnimation.X * scaleFactor;
            iy -= genreBoxAnimation.Y * scaleFactor;

            float barTotalH = upH + currentMidH + downH;
            // 💡 展開アニメーション中はcurrentMidHが小さいため、これに連動させるとクラウン／スコアランクの
            //    アイコンまで一緒に縮んでしまう。アイコンサイズは常に「展開完了後の最終サイズ」基準で
            //    計算したいので、アニメーションに影響されない完全展開時の高さを別途用意する。
            float barTotalHFull = upH + midH_base + downH;

            // 💡 選択中バーにのみ Selected.png を、バーより背面・背景より前面に描画する
            //    展開アニメーションが完了してから表示する（展開途中では出さない）
            // 選択枠は移動完了後に突然出すのではなく、中央バーの高さ変化に追従させる。
            bool selBarAnimFinished = true;
            // 💡 Box(ジャンル)決定演出中、またはフォルダを閉じる演出中は、選択中バーの描画全体を
            //    このバーの中心を軸にXスケールだけ変換してまとめて縮小/復元する
            // 参照側は中央バーを横方向に潰さず、周囲のバーだけを開閉させる。
            if (_barSelectLoaded && (visualCurrent || outgoingDuringScroll) && selBarAnimFinished)
            {
                // 💡 Nijiiroの BarAnimeCount 相当（このバーへのフォーカス/箱展開の進み具合、0〜1）
                float selectedFocusProgress = visualCurrent ? 1f : outgoingProgress;
                float focusEnvelope = boxContentOpacity * selectedFocusProgress;

                float selW = barW * _selectedWidthScale
                    * (visualCurrent ? boxCenterHorizontalScale : 1f);
                float selH = barTotalH * _selectedHeightScale;
                float selOffX = _selectedOffsetX * scaleFactor;
                float selOffY = _selectedOffsetY * scaleFactor;
                float destCenterX = slotCenterX + selOffX;
                float destCenterY = iy + selOffY;

                // レイヤー1: 箱展開に追従する「成長」ハイライト（点滅シーケンス中は0.7〜1.0秒でフェードアウト）
                byte growA = GetGrowingLayerOpacity(focusEnvelope);
                DrawBarSelectSlice(0, destCenterX, destCenterY, selW, selH, new Color((byte)255, (byte)255, (byte)255, growA));

                // レイヤー2: 点滅終了後、常時ゆっくり明滅を繰り返す「呼吸」ハイライト
                byte breatheA = GetBreathingLayerOpacity(focusEnvelope);
                DrawBarSelectSlice(1, destCenterX, destCenterY, selW, selH, new Color((byte)255, (byte)255, (byte)255, breatheA));

                // レイヤー3: フォルダを開閉した瞬間だけ入る、8段階の素早いストロボ点滅
                byte strobeA = (byte)Math.Clamp(GetFlashStrobeOpacity() * focusEnvelope, 0f, 255f);
                DrawBarSelectSlice(2, destCenterX, destCenterY, selW, selH, new Color((byte)255, (byte)255, (byte)255, strobeA));
            }

            float drawnBarW = barW * boxCenterHorizontalScale;
            if (isLoaded)
            {
                byte alphaVal = (byte)(opacity * 255);
                Color boxColor = new Color((byte)tintColor.R, (byte)tintColor.G, (byte)tintColor.B, alphaVal);

                var (srcUp, srcMid, srcDown) = GetBarSourceRects(texUp, texMid, texDown);

                Rectangle destUp = new Rectangle(slotCenterX, iy - currentMidH / 2f - upH / 2f, drawnBarW, upH);
                Raylib.DrawTexturePro(texUp, srcUp, destUp, new Vector2(drawnBarW / 2f, upH / 2f), 0f, boxColor);

                Rectangle destMid = new Rectangle(slotCenterX, iy, drawnBarW, currentMidH);
                Raylib.DrawTexturePro(texMid, srcMid, destMid, new Vector2(drawnBarW / 2f, currentMidH / 2f), 0f, boxColor);

                Rectangle destDown = new Rectangle(slotCenterX, iy + currentMidH / 2f + downH / 2f, drawnBarW, downH);
                Raylib.DrawTexturePro(texDown, srcDown, destDown, new Vector2(drawnBarW / 2f, downH / 2f), 0f, boxColor);
            }
            else
            {
                Raylib.DrawRectangle((int)(slotCenterX - drawnBarW / 2f), (int)(iy - barTotalH / 2f), (int)drawnBarW, (int)barTotalH, new Color((byte)255, (byte)255, (byte)255, (byte)(visualCurrent ? 60 : 30)));
            }

            // 💡 オーバーレイ画像（Overlay.png）を描画する（「もどる」エントリには適用しない）
            if (_overlayLoaded && !(entry is ReturnEntry)) // 💡 非選択スロットにもオーバーレイを適用
            {
                bool isSong = (entry is SongEntry);
                float overlayFocusProgress = visualCurrent ? 1f : outgoingProgress;
                float overW = barW * (visualCurrent ? boxCenterHorizontalScale : 1f);
                // 選択中はアニメーション（barTotalH）に追従し、非選択時は変動しない固定サイズ（totalH_unsel）を適用
                float overH = totalH_unsel
                    + (barTotalH - totalH_unsel) * overlayFocusProgress;

                float overScaleX = 1f;
                float overScaleY = 1f;
                float overOffX = 0f;
                float overOffY = 0f;

                if (isSong)
                {
                    overScaleX = _songOverlayWidthScaleUnsel
                        + (_songOverlayWidthScaleSel - _songOverlayWidthScaleUnsel) * overlayFocusProgress;
                    overScaleY = _songOverlayHeightScaleUnsel
                        + (_songOverlayHeightScaleSel - _songOverlayHeightScaleUnsel) * overlayFocusProgress;
                    overOffX = (_songOverlayOffsetXUnsel
                        + (_songOverlayOffsetXSel - _songOverlayOffsetXUnsel) * overlayFocusProgress) * scaleFactor;
                    overOffY = (_songOverlayOffsetYUnsel
                        + (_songOverlayOffsetYSel - _songOverlayOffsetYUnsel) * overlayFocusProgress) * scaleFactor;
                }
                else
                {
                    overScaleX = _genreOverlayWidthScaleUnsel
                        + (_genreOverlayWidthScaleSel - _genreOverlayWidthScaleUnsel) * overlayFocusProgress;
                    overScaleY = _genreOverlayHeightScaleUnsel
                        + (_genreOverlayHeightScaleSel - _genreOverlayHeightScaleUnsel) * overlayFocusProgress;
                    overOffX = (_genreOverlayOffsetXUnsel
                        + (_genreOverlayOffsetXSel - _genreOverlayOffsetXUnsel) * overlayFocusProgress) * scaleFactor;
                    overOffY = (_genreOverlayOffsetYUnsel
                        + (_genreOverlayOffsetYSel - _genreOverlayOffsetYUnsel) * overlayFocusProgress) * scaleFactor;
                }

                float finalOverW = overW * overScaleX;
                float finalOverH = overH * overScaleY;

                byte alphaVal = (byte)(boxContentOpacity * 255);
                Color overlayColor = new Color((byte)255, (byte)255, (byte)255, alphaVal);

                Rectangle srcOver = new Rectangle(0, 0, _texOverlay.Width, _texOverlay.Height);
                Rectangle destOver = new Rectangle(slotCenterX + overOffX, iy + overOffY, finalOverW, finalOverH);

                // 💡 縁（丸み部分）が引き伸ばしで歪まないよう、9パッチ描画を使用
                //    ボーダー幅はテクスチャの短辺の25%を自動採用
                int overlayBorder = (int)(Math.Min(_texOverlay.Width, _texOverlay.Height) * 0.25f);
                NPatchInfo overlayNPatch = new NPatchInfo
                {
                    Source = srcOver,
                    Left = overlayBorder,
                    Top = overlayBorder,
                    Right = overlayBorder,
                    Bottom = overlayBorder,
                    Layout = NPatchLayout.NinePatch
                };

                Raylib.DrawTextureNPatch(_texOverlay, overlayNPatch, destOver, new Vector2(finalOverW / 2f, finalOverH / 2f), 0f, overlayColor);
            }

            string title = GetEntryDisplayTitle(entry);

            // 💡 ジャンル(Box)か曲(Song)かで、箱の大きさに対する文字サイズ比率をそれぞれ個別に反映する
            bool isGenreEntry = entry is BoxEntry;
            float titleBaseSize = isGenreEntry ? GenreTitleFontSize : SongTitleFontSize;
            float titleFocusProgress = 0f;
            if (outgoingDuringScroll)
            {
                // 画面外へ移る旧曲の文字は、バーと同じ進行で選択状態を解除する。
                titleFocusProgress = outgoingProgress;
            }
            else if (!songScrollActive && isCurrent)
            {
                titleFocusProgress = selectionFocusProgress;
            }

            float titleScaleFactor = _titleFontScaleUnsel
                + (1f - _titleFontScaleUnsel) * titleFocusProgress;
            float fontS_orig = titleBaseSize * scaleFactor * titleScaleFactor;

            // 和文だけHTML相当の詰めを適用する。測定・縮小・描画には同じ値を使う。
            float titleLetterSpacingEm = G.GetRecommendedTitleLetterSpacingEm(title);

            Vector2 tSize_orig = Raylib.MeasureTextEx(G.Font, title, fontS_orig, 0);
            float titleWidthOrig = G.MeasureTextWithOutline16Width(
                G.Font, title, fontS_orig, titleLetterSpacingEm);

            float maxAllowedWidth = barW * 0.72f;
            float fontS = fontS_orig;
            float measuredW = titleWidthOrig;

            // 字間はem指定のため、フォントサイズと同じ比率で拡縮できる。
            if (titleWidthOrig > maxAllowedWidth)
            {
                float ratio = maxAllowedWidth / titleWidthOrig;
                fontS = fontS_orig * ratio;
                measuredW = maxAllowedWidth;
            }

            // BOX（ジャンル）の時だけ左に30pxずらす場合
            float genreXOffset = isGenreEntry ? 10f * scaleFactor : 0f;
            float charX = slotCenterX - measuredW / 2f + genreXOffset;

            // 💡 選択中（現在地：i == 0）の曲名／ジャンル名のみ、目標座標へとアニメーション移動させる
            float offsetYStart = isGenreEntry ? GENRE_TITLE_OFFSET_Y_START : TITLE_OFFSET_Y_START;
            float offsetYTarget = isGenreEntry ? GENRE_TITLE_OFFSET_Y_TARGET : TITLE_OFFSET_Y_TARGET;

            float currentTitleOffsetY = 0f;
            if (visualCurrent)
            {
                float titleProgress = 0f;
                if (t < GROW_START)
                {
                    titleProgress = 0f;
                }
                else if (t < GROW_START + GROWTH_DURATION)
                {
                    float localRatio = (t - GROW_START) / GROWTH_DURATION;
                    titleProgress = (float)Math.Sin(localRatio * Math.PI / 2.0); // イージング（サイン補間）
                }
                else
                {
                    titleProgress = 1f;
                }

                currentTitleOffsetY = offsetYStart + (offsetYTarget - offsetYStart) * titleProgress;
            }
            else
            {
                currentTitleOffsetY = offsetYStart
                    + (offsetYTarget - offsetYStart) * titleFocusProgress;
            }

            float titleScale = fontS / fontS_orig;
            float actualTitleH = tSize_orig.Y * titleScale;
            float charY = iy - actualTitleH / 2f + currentTitleOffsetY * scaleFactor;

            if (entry is SongEntry curSongEntry)
            {
                if (visualCurrent)
                {
                    // 💡 4つの独立画像をメインにして自動スケーリング描画
                    // 💡 BOX展開演出中は、Nijiiroのレベル数字フェードと同様にフェードアウト→フェードインさせる
                    float normalSelectionProgress = visualCurrent ? selectionFocusProgress : outgoingProgress;
                    float normalSelectionAlpha = IsBoxAnimationActive ? 1f : normalSelectionProgress;
                    byte diffMarksAlpha = (byte)(boxContentOpacity * normalSelectionAlpha * 255f);
                    DrawDifficultyMarksForSongHorizontal(curSongEntry.Song, slotCenterX, iy + 75f * scaleFactor, scaleFactor, selBarAnimFinished, barW, diffMarksAlpha);
                }
                // 💡 ベストスコアJSONからクリアタイプ・スコアランクアイコンを描画（選択中・非選択中ともに表示）
                //    非選択→選択に切り替わった瞬間に大きさ・位置が一気に変わらないよう、
                //    BestIconGrowDuration秒かけて非選択時の見た目から選択時の見た目へアニメーションさせる。
                //    実際の描画はループ終了後にまとめて行う（隣のバーに隠れるのを防ぐため）。
                float iconProgress = 0f;
                if (outgoingDuringScroll)
                {
                    iconProgress = outgoingProgress;
                }
                else if (visualCurrent)
                {
                    if (t < GROW_START) iconProgress = 0f;
                    else if (t < GROW_START + BestIconGrowDuration)
                        iconProgress = (float)Math.Sin((t - GROW_START) / BestIconGrowDuration * Math.PI / 2.0); // イージング（サイン補間）
                    else iconProgress = 1f;
                }
                // 💡 BOX展開演出中：選択中バーの王冠／スコアランクアイコンをフェードアウト→フェードインさせる
                byte iconAlpha = 255;
                if (visualCurrent)
                {
                    iconAlpha = (byte)(boxContentOpacity * 255f);
                }
                pendingBestIcons.Add((curSongEntry.Song, slotCenterX, iy, barW, totalH_unsel, barTotalHFull, scaleFactor, iconProgress, iconAlpha));
            }
            else if ((visualCurrent || outgoingDuringScroll) && entry is BoxEntry curBoxEntry)
            {
                float decorationProgress = visualCurrent ? 1f : outgoingProgress;
                float decorationTime = visualCurrent ? t : GROW_START + GROWTH_DURATION + 1f;
                DrawBoxDecorations(curBoxEntry.Def, slotCenterX, iy, barW, barTotalH, scaleFactor, vx, vy, vw, vh, decorationTime, (byte)(boxContentOpacity * decorationProgress * 255f));
            }

            Color foreCol = Color.White;
            Color backCol = Color.Black;
            if (entry is BoxEntry titleBoxEntry)
            {
                foreCol = titleBoxEntry.Def.ForeColor;
                backCol = titleBoxEntry.Def.HasBackColor
                    ? titleBoxEntry.Def.BackColor
                    : GetGenreOutlineColor(titleBoxEntry.Def.Genre) ?? titleBoxEntry.Def.BackColor;
            }
            else if (entry is SongEntry titleSongEntry)
            {
                var ownerDef = titleSongEntry.OwnerDef;
                if (ownerDef != null && ownerDef.HasBackColor)
                {
                    backCol = ownerDef.BackColor;
                }
                else
                {
                    backCol = GetGenreOutlineColor(titleSongEntry.Song.Genre)
                        ?? GetGenreOutlineColor(ownerDef?.Genre)
                        ?? Color.Black;
                }
            }
            else if (entry is ReturnEntry)
            {
                foreCol = new Color((byte)0xFF, (byte)0xFF, (byte)0xFF, (byte)255); // #FFFFFF
                backCol = new Color((byte)0x42, (byte)0x28, (byte)0x06, (byte)255); // #422806
            }

            // 💡 曲タイトルを描画（ジャンル/曲それぞれ個別の縁取りの太さを適用）
            float titleOutlineThickness = (isGenreEntry ? GenreTitleOutlineThickness : SongTitleOutlineThickness) * scaleFactor;
            Color titleForeCol = foreCol;
            Color titleBackCol = backCol;
            if (visualCurrent && IsBoxAnimationActive)
            {
                byte titleAlpha = (byte)(boxContentOpacity * 255f);
                titleForeCol = new Color(foreCol.R, foreCol.G, foreCol.B, titleAlpha);
                titleBackCol = new Color(backCol.R, backCol.G, backCol.B, titleAlpha);
            }
            G.DrawTextWithOutline16(
                G.Font, title, new Vector2(charX, charY), fontS,
                titleForeCol, titleBackCol, titleOutlineThickness,
                0f, titleLetterSpacingEm);

            // 💡 曲名の下に小さくサブタイトルを描画（選択中かつTJAファイルにSubtitleが存在する場合のみ）
            //    展開開始と同時にフェードイン＋下からふわっと上に上がる演出
            string subSongEntryDisplaySubtitle = entry is SongEntry subSongEntryForCheck
                ? (!string.IsNullOrEmpty(subSongEntryForCheck.Song.SubtitleJP) ? subSongEntryForCheck.Song.SubtitleJP : subSongEntryForCheck.Song.Subtitle)
                : null;
            if ((visualCurrent || outgoingDuringScroll)
                && entry is SongEntry subSongEntry
                && !string.IsNullOrEmpty(subSongEntryDisplaySubtitle))
            {
                float subProgress;
                if (outgoingDuringScroll) subProgress = outgoingProgress;
                else if (t < GROW_START) subProgress = 0f;
                else if (t < GROW_START + SubtitleFadeDuration)
                    subProgress = (float)Math.Sin((t - GROW_START) / SubtitleFadeDuration * Math.PI / 2.0); // イージング（サイン補間）
                else
                    subProgress = 1f;

                if (subProgress > 0f)
                {
                    string subtitle = subSongEntryDisplaySubtitle;
                    float subFontS_orig = fontS * 0.55f; // メインフォントよりひと回り小さく描画

                    Vector2 subSize_orig = Raylib.MeasureTextEx(G.Font, subtitle, subFontS_orig, 0);
                    float subFontS = subFontS_orig;
                    float subMeasuredW = subSize_orig.X;

                    if (subSize_orig.X > maxAllowedWidth)
                    {
                        float ratio = maxAllowedWidth / subSize_orig.X;
                        subFontS = subFontS_orig * ratio;
                        subMeasuredW = maxAllowedWidth;
                    }

                    float subX = slotCenterX - subMeasuredW / 2f;
                    // 💡 完了時のY位置に、未完了分だけ下からのオフセットを足して「上に上がる」動きを表現
                    float subRiseOffset = outgoingDuringScroll
                        ? 0f
                        : SubtitleRiseDistance * scaleFactor * (1f - subProgress);
                    float subY = charY + actualTitleH + 8f * scaleFactor + subRiseOffset;

                    byte subAlpha = (byte)(subProgress * 255);
                    Color subForeCol = new Color(foreCol.R, foreCol.G, foreCol.B, subAlpha);
                    Color subBackCol = new Color(backCol.R, backCol.G, backCol.B, subAlpha);

                    G.DrawTextWithOutline16(G.Font, subtitle, new Vector2(subX, subY), subFontS, subForeCol, subBackCol);
                }
            }

        }

        // 💡 全バー描画後にスコアランク／王冠アイコンをまとめて最前面に描画（隣バーによる隠れを防止）
        foreach (var icon in pendingBestIcons)
        {
            DrawBestScoreIcons(icon.Song, icon.SlotCenterX, icon.Iy, icon.BarW, icon.TotalHUnsel, icon.TotalHFull, icon.ScaleFactor, icon.Progress, icon.Alpha);
        }

        if (_headerLoaded)
        {
            float aspect = (float)_texHeader.Width / _texHeader.Height;
            float headerW = vw;
            float headerH = headerW / aspect;
            float hx = vx;
            float hy = vy;
            Raylib.DrawTexturePro(_texHeader, new Rectangle(0, 0, _texHeader.Width, _texHeader.Height), new Rectangle(hx, hy, headerW, headerH), Vector2.Zero, 0f, Color.White);
        }

        if (_footerLoaded)
        {
            float aspect = (float)_texFooter.Width / _texFooter.Height;
            float footerW = vw;
            float footerH = footerW / aspect;
            float fx = vx;
            float fy = vy + vh - footerH;
            Raylib.DrawTexturePro(_texFooter, new Rectangle(0, 0, _texFooter.Width, _texFooter.Height), new Rectangle(fx, fy, footerW, footerH), Vector2.Zero, 0f, Color.White);
        }
        if (_footerAnimLoaded)
        {
            float aspect = (float)_texFooteranim.Width / _texFooteranim.Height;
            float footerW = vw;
            float footerH = footerW / aspect;
            float fx = vx;
            float fy = vy + vh - footerH;
            Raylib.DrawTexturePro(_texFooteranim, new Rectangle(0, 0, _texFooteranim.Width, _texFooteranim.Height), new Rectangle(fx, fy, footerW, footerH), Vector2.Zero, 0f, Color.White);
        }

        DrawScorePanel(vx, vy, vw, vh, scaleFactor);

        if (_exitConfirmTimer > 0f)
        {
            float noticeFS = 26f * scaleFactor;
            string noticeText = "もう一度 ESC キーを押すとゲームを終了します";
            Vector2 noticeSize = Raylib.MeasureTextEx(G.Font, noticeText, noticeFS, 0);
            float nx = vx + (vw - noticeSize.X) / 2f;
            float ny = vy + 50f * scaleFactor;
            float padX = 24f * scaleFactor, padY = 12f * scaleFactor;
            Raylib.DrawRectangleRounded(new Rectangle(nx - padX, ny - padY, noticeSize.X + padX * 2f, noticeSize.Y + padY * 2f), 0.4f, 6, new Color((byte)0, (byte)0, (byte)0, (byte)200));
            G.DrawTextWithOutline16(G.Font, noticeText, new Vector2(nx, ny), noticeFS, Color.Yellow, Color.Black);
        }
        DonChanScene.X = DonChanX;
        DonChanScene.BottomY = DonChanBottomY;
        DonChanScene.Width = DonChanWidth;
        DonChanScene.Draw(vx, vy, scaleFactor);

        // どんちゃん(X=-200, BottomY=1100, Width=860 → 中心X≒230, 下端Y=1100)の真下に配置
        NamePlate.GlobalX = NamePlateGlobalX;
        NamePlate.GlobalY = NamePlateGlobalY;
        NamePlate.Draw(vx, vy, scaleFactor);

        // 操作ガイドアニメーション (Anime.aup2) を最前面に描画（元の縦横比を維持）
        if (_animeGuide != null)
        {
            float guideW = AnimeGuideWidth * scaleFactor;
            float guideH = (_animeGuide.SceneWidth > 0f)
                ? guideW * (_animeGuide.SceneHeight / _animeGuide.SceneWidth)
                : guideW;
            float guideX = vx + vw * AnimeGuideCenterX - guideW / 2f;
            float guideY = vy + vh * AnimeGuideCenterY - guideH / 2f;
            _animeGuide.Draw(guideX, guideY, guideW, guideH, _animeGuideTime, Color.White);
        }

        // 💡 Demo画面下部と同じ「フリープレイ」表示。SongSelectではフリープレイ時のみ表示する
        DrawFreePlayBottomText(vx, vy, vw, vh, scaleFactor);
    }

    private static void DrawFreePlayBottomText(int evx, int evy, int evw, int evh)
    {
        throw new NotImplementedException();
    }

    // ==================================================================
    // 💡 下部の「フリープレイ」表示（Demo画面と同様の見た目）。
    //    SongSelectではコイン投入モードの案内は不要なため、フリープレイ時のみ描画する。
    //    ⚠️ Demo画面の基準値(フォント40 / 縁取り8 / マージン50)はHD(1280x720)基準で作られているが、
    //       SongSelectはFHD(1920x1080)基準のため、Demoの基準値をそのまま2倍にしたうえで
    //       SongSelect側のscaleFactorを掛けて実ピクセルに変換する。
    // ==================================================================
    private const float BOTTOM_STATUS_FONT_SIZE_HD = 40f;      // Demo画面のBOTTOM_FONT_SIZEそのまま(HD基準)
    private const float BOTTOM_STATUS_OUTLINE_HD = 8f;         // Demo画面のOUTLINE_THICKNESSそのまま(HD基準)
    private const float BOTTOM_STATUS_MARGIN_Y_HD = 50f;       // Demo画面のBOTTOM_SINGLE_Y(1030)を画面下端からの距離に換算した値(HD基準)
    private const float HD_TO_FHD_SCALE = 1f;                  // HD→FHD(SongSelect基準)への倍率

    private static void DrawFreePlayBottomText(float vx, float vy, float vw, float vh, float scaleFactor)
    {
        if (!Coin.FreePlay) return;

        float fontSize = BOTTOM_STATUS_FONT_SIZE_HD * HD_TO_FHD_SCALE * scaleFactor;
        float outline = BOTTOM_STATUS_OUTLINE_HD * HD_TO_FHD_SCALE * scaleFactor;
        float marginY = BOTTOM_STATUS_MARGIN_Y_HD * HD_TO_FHD_SCALE * scaleFactor;

        float centerX = vx + vw * 0.5f;
        float y = vy + vh - marginY;

        DrawSongSelectBottomOutline("フリープレイ", centerX, y, fontSize, outline);
    }

    private static void DrawSongSelectBottomOutline(string text, float centerX, float y, float fontSize, float outline)
    {
        Vector2 size = Raylib.MeasureTextEx(G.Font, text, fontSize, 0);
        float x = centerX - size.X * 0.5f;
        G.DrawTextWithOutline16(G.Font, text, new Vector2(x, y), fontSize, Color.White, Color.Black, outline, 0);
    }

    private const float ONI_HOLD = 4.0f; // 💡 5秒待機のうち4.0秒を完全に維持する
    private const float ONI_FADE = 0.5f; // 💡 フェードインとフェードアウトはそれぞれ0.5秒ずつに設定

    /// <summary>裏譜面がある曲の「おに」⇔「裏おに」を、保持(4.0s)→なめらかなフェード(0.5s)→保持…で繰り返すための、裏側(4番)の不透明度(0〜1)を返す。</summary>
    static float GetUraOniFadeAlpha()
    {
        float cycle = ONI_HOLD * 2f + ONI_FADE * 2f;
        float phase = (float)(Raylib.GetTime() % cycle);

        if (phase < ONI_HOLD) return 0f;
        phase -= ONI_HOLD;
        if (phase < ONI_FADE) return phase / ONI_FADE;
        phase -= ONI_FADE;
        if (phase < ONI_HOLD) return 1f;
        phase -= ONI_HOLD;
        return 1f - (phase / ONI_FADE);
    }

    /// <summary>
    /// 0.png, 1.png, 2.png, 3.png, 4.png自体を直接配置・表示します。
    /// 一番右側（右から1番目の仮想5番目枠）は完全に描画せず、右から2番目（インデックス3 of the 4 slots）に、おに（3.png）と裏おに（4.png）の見た目（テクスチャ＆対応する星の数）が5秒ごとに交互に切り替わります。
    /// </summary>
    static void DrawDifficultyMarksForSongHorizontal(SongData song, float drawX, float drawY, float scaleFactor, bool selectAnimFinished, float slotWidth, byte globalAlpha = 255)
    {
        if (!selectAnimFinished) return;
        if (globalAlpha == 0) return;

        bool hasUra = song.Levels[4] > 0;

        // 💡 4つのスロットを並べる横幅（選択されたバーの幅に完全に追従させて潰れを防ぐ）
        float totalW = slotWidth * 0.82f;
        float barSpacing = 8f * scaleFactor;

        // 各スロット（0.png〜4.png）のオリジナルアスペクト比は 200x100
        float barW = (totalW - (3f * barSpacing)) / 4f;
        float barH = barW * 0.5f; // 高さもアスペクト比を完璧に維持

        float panelLeft = drawX - (totalW / 2f);
        float barY = drawY;

        // 💡 4つのスロットを描画（左から1番目：かんたん, 2番目：ふつう, 3番目：むずかしい, 4番目：おに/おに裏5秒交互）
        for (int b = 0; b < 4; b++)
        {
            float barCenterX = panelLeft + (barW / 2f) + b * (barW + barSpacing);

            int texIndex = b;
            byte alphaVal = 255;

            // 💡 右から2番目（4番目・スロット3）の位置：おにと裏おにをふわーっと交互切り替え（0.5秒フェードイン・フェードアウト ➡ 4.0秒維持 = 片方につき5秒）
            if (b == 3)
            {
                if (hasUra)
                {
                    double time = Raylib.GetTime();
                    double cycle = 10.0; // おに5秒 + 裏おに5秒 の合計10秒サイクル
                    double tLocal = time % 5.0; // 各表示時間のローカルタイム (0.0 〜 5.0)

                    // 0.5秒でフェードイン、4.0秒維持、0.5秒でフェードアウト
                    float alpha = 1.0f;
                    if (tLocal < 0.5)
                    {
                        // 0.5秒かけてふわーっとフェードイン
                        double ratio = tLocal / 0.5;
                        alpha = (float)Math.Sin(ratio * Math.PI / 2.0);
                    }
                    else if (tLocal >= 4.5)
                    {
                        // 0.5秒かけてふわーっとフェードアウト
                        double ratio = (5.0 - tLocal) / 0.5;
                        alpha = (float)Math.Sin(ratio * Math.PI / 2.0);
                    }

                    alphaVal = (byte)(alpha * 255);
                    texIndex = (time % cycle >= 5.0) ? 4 : 3; // 5秒経ったら見た目の画像を裏おに(4.png)に切り替え
                }
                else
                {
                    texIndex = 3; // 通常おに固定
                    alphaVal = 255;
                }
            }

            alphaVal = (byte)((alphaVal / 255f) * globalAlpha);

            // 💡 スロット画像の描画
            if (_diffBarsLoaded[texIndex])
            {
                Texture2D tex = _texDiffBars[texIndex];
                Rectangle srcBar = new Rectangle(0, 0, tex.Width, tex.Height);
                Rectangle destBar = new Rectangle(barCenterX - barW / 2f, barY - barH / 2f, barW, barH);

                bool isAvailable = song.Levels[texIndex] > 0;

                if (isAvailable)
                {
                    Color drawColor = new Color((byte)255, (byte)255, (byte)255, alphaVal);
                    Raylib.DrawTexturePro(tex, srcBar, destBar, Vector2.Zero, 0f, drawColor);
                    DrawDifficultyNumberForSlot(song.Levels[texIndex], barCenterX, barY, barW, barH, scaleFactor, alphaVal);
                }
                else
                {
                    // 未収録の難易度は綺麗に暗転マスク（グレーアウト）もフェードに追従
                    byte inactiveAlpha = (byte)(160f * (alphaVal / 255f));
                    Color inactiveColor = new Color((byte)40, (byte)40, (byte)40, inactiveAlpha);
                    Raylib.DrawTexturePro(tex, srcBar, destBar, Vector2.Zero, 0f, inactiveColor);
                }
            }
        }
    }

    /// <summary>
    /// 0.png〜4.png難易度枠の右側エリア（黒背景）内に、難易度数値を「左揃え」でスプライトシートから切り出し描画します。
    /// </summary>
    static void DrawDifficultyNumberForSlot(int level, float barCenterX, float barY, float barW, float barH, float scaleFactor, byte alphaVal)
    {
        if (level > 0 && _starLoaded)
        {
            // 💡 スプライトシートは 0123456789 の順に10等分されている
            float charW = _texStar.Width / 10f;
            float charH = _texStar.Height;

            // 💡 ★10以上の場合は個別に横幅スケールを縮小させて見やすく調整可能にする
            float wScale = (level >= 10) ? _diffNumWidthScale10Max : 1.0f;

            float drawW = charW * scaleFactor * _diffNumScale * wScale;
            float drawH = charH * scaleFactor * _diffNumScale;

            string lvlStr = level.ToString();
            int len = lvlStr.Length;

            // 💡 スロット画像（200x100想定）の右側エリアの左側境界付近を起点とし、_diffNumOffsetX で微調整できるようにします
            float startX = barCenterX + barW * 0.12f + (_diffNumOffsetX * scaleFactor);

            for (int i = 0; i < len; i++)
            {
                int d = lvlStr[i] - '0';
                if (d < 0 || d > 9) continue;

                float sx = startX + i * (drawW + _diffNumSpacing * scaleFactor);
                float sy = barY - (drawH / 2f) + (_diffNumOffsetY * scaleFactor);

                Rectangle src = new Rectangle(d * charW, 0, charW, charH);
                Rectangle dest = new Rectangle(sx, sy, drawW, drawH);

                Raylib.DrawTexturePro(
                    _texStar,
                    src,
                    dest,
                    Vector2.Zero,
                    0f,
                    new Color((byte)255, (byte)255, (byte)255, alphaVal)
                );
            }
        }
    }

    // ==================================================================
    // 💡 ベストスコアJSONを読み込んで (BestClearType, BestRankIndex) を返す。
    //    ファイルが無い/不正な場合は (-1, -1) を返す。
    //    難易度ごとに scores_Kantan.json / scores_Futsuu.json / ... が存在するので全難易度分をまとめて返す。
    //    戻り値: (clearType[5], rankIndex[5])  インデックスは 0=かんたん 1=ふつう 2=むずかしい 3=おに 4=裏おに
    // ==================================================================
    static readonly string[] _difficultyJsonNames = { "かんたん", "ふつう", "むずかしい", "おに", "うらおに" };

    static (int[] clearTypes, int[] rankIndexes) LoadBestScoreRecord(SongData song)
    {
        var clearTypes = new int[5];
        var rankIndexes = new int[5];
        for (int d = 0; d < 5; d++) { clearTypes[d] = -1; rankIndexes[d] = -1; }

        string dir = Path.GetDirectoryName(song.TjaPath);
        if (string.IsNullOrEmpty(dir)) return (clearTypes, rankIndexes);

        // 💡 JSON側のキー（曲名）と一致するものだけを採用する。
        //    以前は「1ファイルに1曲だけ入っている」前提でforeachの最初の要素を無条件に使っていたため、
        //    同じフォルダ内の別の曲を選んでも常に同じスコアが表示されてしまっていた。
        string titleJP = song.TitleJP;
        string titleEN = song.Title;

        for (int d = 0; d < 5; d++)
        {
            string jsonPath = Path.Combine(dir, $"scores_{_difficultyJsonNames[d]}.json");
            if (!File.Exists(jsonPath)) continue;
            try
            {
                string json = File.ReadAllText(jsonPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                // jsonは { "曲名": { BestScore, BestRankIndex, BestClearType, ... } } 形式
                foreach (var songProp in doc.RootElement.EnumerateObject())
                {
                    string key = songProp.Name;
                    bool matches = (!string.IsNullOrEmpty(titleJP) && key == titleJP)
                                || (!string.IsNullOrEmpty(titleEN) && key == titleEN);
                    if (!matches) continue;

                    var obj = songProp.Value;
                    if (obj.TryGetProperty("BestClearType", out var ct))
                        clearTypes[d] = ct.GetInt32();
                    if (obj.TryGetProperty("BestRankIndex", out var ri))
                        rankIndexes[d] = ri.GetInt32();
                    break; // 名前が一致した曲を採用したら終了
                }
            }
            catch { /* 読み込み失敗は無視 */ }
        }
        return (clearTypes, rankIndexes);
    }

    // ==================================================================
    // 💡 難易度5本分のクリアタイプ・スコアランクアイコンをバー上に描画する。
    //    ClearType_Symbol.png: 縦5行(難易度) × 横3列(クリア/FC/全良)
    //    ScoreRank_Symbol.png: 縦5行(難易度) × 横7列(50万/60万/…/100万)
    //    isCurrent=true のとき選択中パラメータ、false のとき非選択パラメータを使用。
    //    位置・大きさは BestIcon* 系パラメータで調整可能。
    // ==================================================================
    static void DrawBestScoreIcons(SongData song, float centerX, float centerY, float barW, float totalHUnsel, float totalHFull, float scaleFactor, float progress, byte alpha = 255)
    {
        if (!_clearTypeSymbolLoaded && !_scoreRankSymbolLoaded) return;
        if (alpha == 0) return;

        var (clearTypes, rankIndexes) = LoadBestScoreRecord(song);

        // 💡 大きさは常に固定ピクセルサイズ（選択中・非選択中で変化しない）。
        //    barTotalHはYオフセットの基準としてのみ使用する。
        float barTotalH = totalHUnsel + (totalHFull - totalHUnsel) * progress;
        float offsetYRatio = BestIconOffsetYUnsel + (BestIconOffsetYSel - BestIconOffsetYUnsel) * progress;

        float iconH = BestIconHeightPx * scaleFactor;
        float crownCellW = _clearTypeSymbolLoaded ? (float)_texClearTypeSymbol.Width / 3f : 1f;
        float crownCellH = _clearTypeSymbolLoaded ? (float)_texClearTypeSymbol.Height / 5f : 1f;
        float rankCellW = _scoreRankSymbolLoaded ? (float)_texScoreRankSymbol.Width / 7f : 1f;
        float rankCellH = _scoreRankSymbolLoaded ? (float)_texScoreRankSymbol.Height / 5f : 1f;

        float crownDrawH = iconH;
        float crownDrawW = _clearTypeSymbolLoaded ? crownDrawH * (crownCellW / crownCellH) : iconH;
        float rankDrawH = iconH;
        float rankDrawW = _scoreRankSymbolLoaded ? rankDrawH * (rankCellW / rankCellH) : iconH;

        float slotW = barW / 5f;
        float startX = centerX - barW / 2f + BestIconOffsetX * scaleFactor;
        float iconY = centerY + barTotalH * offsetYRatio;

        // 💡 JSONに記録が存在する難易度の中で、最も高い難易度（おに裏 > おに > むずかしい > ふつう > かんたん）を特定する
        int bestDiffWithRecord = -1;
        for (int d = 4; d >= 0; d--)
        {
            // 王冠(ct)が 0〜2（クリア/FC/全良）または スコアランク(ri)が 0〜6（50万〜100万）のいずれかがあれば「記録あり」とみなす
            if (clearTypes[d] >= 0 && clearTypes[d] <= 2 || rankIndexes[d] >= 0 && rankIndexes[d] <= 6)
            {
                bestDiffWithRecord = d;
                break;
            }
        }

        if (bestDiffWithRecord != -1)
        {
            int ct = clearTypes[bestDiffWithRecord];
            int ri = rankIndexes[bestDiffWithRecord];

            float slotCX = startX + slotW * 3 + slotW * 0.5f; // 💡 常に「おに」のスロット位置に描画

            // クラウン（ClearType）アイコン
            if (_clearTypeSymbolLoaded && ct >= 0 && ct <= 2)
            {
                Rectangle src = new Rectangle(ct * crownCellW, bestDiffWithRecord * crownCellH, crownCellW, crownCellH);
                Rectangle dest = new Rectangle(slotCX - crownDrawW * 0.5f, iconY, crownDrawW, crownDrawH);
                Raylib.DrawTexturePro(_texClearTypeSymbol, src, dest, Vector2.Zero, 0f, new Color((byte)255, (byte)255, (byte)255, alpha));
            }

            // スコアランクアイコン
            if (_scoreRankSymbolLoaded && ri >= 0 && ri <= 6)
            {
                float rankY = iconY + crownDrawH + BestIconSpacing * scaleFactor;
                Rectangle src = new Rectangle(ri * rankCellW, bestDiffWithRecord * rankCellH, rankCellW, rankCellH);
                Rectangle dest = new Rectangle(slotCX - rankDrawW * 0.5f, rankY, rankDrawW, rankDrawH);
                Raylib.DrawTexturePro(_texScoreRankSymbol, src, dest, Vector2.Zero, 0f, new Color((byte)255, (byte)255, (byte)255, alpha));
            }
        }
    }

    static void DrawBoxDecorations(BoxDef def, float centerX, float centerY, float barW, float totalH, float scaleFactor, float vx, float vy, float vw, float vh, float t, byte boxAnimationAlpha = 255)
    {
        Color decorationColor = new Color((byte)255, (byte)255, (byte)255, boxAnimationAlpha);
        if (def.BoxPopLoaded)
        {
            float popH = totalH * 0.4f;
            float popAspect = (float)def.TexBoxPop.Width / def.TexBoxPop.Height;
            float popW = popH * popAspect;
            Rectangle srcPop = new Rectangle(0, 0, def.TexBoxPop.Width, def.TexBoxPop.Height);
            Raylib.DrawTexturePro(def.TexBoxPop, srcPop, new Rectangle(centerX - barW / 2f - popW * 0.6f, centerY - popH / 2f, popW, popH), Vector2.Zero, 0f, decorationColor);
            Raylib.DrawTexturePro(def.TexBoxPop, srcPop, new Rectangle(centerX + barW / 2f + popW * 0.6f, centerY - popH / 2f, popW, popH), Vector2.Zero, 0f, decorationColor);
        }

        if (def.UseBarDecoration)
        {
            Color boxDecorationColor = new Color(def.BoxColor.R, def.BoxColor.G, def.BoxColor.B, boxAnimationAlpha);
            Raylib.DrawRectangleLinesEx(new Rectangle(centerX - barW / 2f, centerY - totalH / 2f, barW, totalH), 3f, boxDecorationColor);
        }

        // 💡 説明文（Explanation）の位置調整用パラメータ（ここを変更するだけで簡単に微調整できます）
        float infoX = centerX + _explanationOffsetX * scaleFactor;
        float infoY = centerY - totalH / 2f + 20f * scaleFactor + _explanationOffsetY * scaleFactor;
        if (!string.IsNullOrEmpty(def.Explanation))
        {
            // 💡 展開開始と同時にフェードイン＋下からふわっと上に上がる演出（サブタイトルと同じ挙動）
            float expProgress;
            if (t < GROW_START) expProgress = 0f;
            else if (t < GROW_START + SubtitleFadeDuration)
                expProgress = (float)Math.Sin((t - GROW_START) / SubtitleFadeDuration * Math.PI / 2.0); // イージング（サイン補間）
            else
                expProgress = 1f;

            if (expProgress > 0f)
            {
                float expRiseOffset = SubtitleRiseDistance * scaleFactor * (1f - expProgress);
                byte expAlpha = (byte)(expProgress * boxAnimationAlpha);

                Color expOutlineColor = GetGenreOutlineColor(def.Genre) ?? def.BackColor;
                Color expForeCol = new Color(def.ForeColor.R, def.ForeColor.G, def.ForeColor.B, expAlpha);
                Color expOutlineColAlpha = new Color(expOutlineColor.R, expOutlineColor.G, expOutlineColor.B, expAlpha);

                var expLines = def.ExplanationLines;
                for (int li = 0; li < expLines.Length; li++)
                {
                    float lineFontSize = _explanationFontSize * scaleFactor;
                    Vector2 lineSize = Raylib.MeasureTextEx(G.FontSub, expLines[li], lineFontSize, 0);
                    float lineX = infoX - lineSize.X / 2f; // 💡 中央揃え
                    G.DrawTextWithOutline16(
                        G.FontSub,
                        expLines[li],
                        new Vector2(lineX, infoY + li * _explanationLineSpacing * scaleFactor - expRiseOffset),
                        lineFontSize,
                        expForeCol,
                        expOutlineColAlpha,
                        8f // ← 💡 ここに太さ（thickness）を数値で指定（デフォルトは 7f）[cite: 1, 2]
                    );
                }
            }
        }
    }
}
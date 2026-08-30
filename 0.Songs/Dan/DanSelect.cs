using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Raylib_cs;
using NAudio.Wave;
using NAudio.Vorbis; // 💡 .ogg再生用(NAudio単体ではoggを直接読めないためNAudio.Vorbisを併用)
using TaikoNauts.Core.Taiko.Charts; // 💡 TJA.Load / TJA.Title(Enso.csと同じ曲名取得手段)

/// <summary>
/// 段位道場選択画面
/// タイトル画面で「段位道場」が選択された状態でDon1/Don2(KeyLeft/KeyRight)が押されると
/// Program.cs側からこの画面に遷移してくる。
/// 遷移直後にLumen/5.Dan/DanIn/Anime.aup2を1回再生し、再生が終わったら
/// Lumen/5.Dan/DanSelect/Bg.pngをずっと表示し続ける。
/// さらにBg.png表示中は画面上部にLumen/5.Dan/DanSelect/MiniPlate.png(段位認定プレート)を並べ、
/// 各プレートに段位名を縦書きで割り当てて表示する。
/// Songs/以下を自動探索したdan.jsonの"title"と段位名が一致したプレートだけ有効(存在する段位)として扱い、
/// 一致しないプレートは色を50%暗くする(透明度は変えない)。
/// 選択はSettingsPanel.KeyEdgeL/KeyEdgeR(Ka1/Ka2)で行う。
/// </summary>
public static class DanSelectScene
{
    private static bool _initialized;

    // 💡 段位道場導入アニメーション(Lumen/5.Dan/DanIn/Anime.aup2)
    private static Aup2Anim _introAnim;
    private static float _animTimer;
    private static bool _playingIntro;

    // 💡 アニメ再生終了後にずっと表示する背景(Lumen/5.Dan/DanSelect/Bg.png)
    private static Texture2D _bgTex;

    // 💡 DanIn(Anime.aup2)再生完了後にループ再生するBGM(Lumen/2.sound/BGM/DanSelect.ogg)
    private static WaveOutEvent _bgmOutput;
    private static VorbisWaveReader _bgmReader;
    private static LoopStream _bgmLoop;
    private const string BGM_PATH = "Lumen/2.sound/BGM/DanSelect.ogg";

    // ==================================================================
    // 💡 MiniPlate.png(段位認定プレート)関連
    //     1枚の画像に「黄・青・赤・銀・金・緑」の6色が横一列(6分割)で並んでいるので、
    //     色ごとにソース矩形(切り出し範囲)を分けて使う。
    // ==================================================================
    private static Texture2D _miniPlateTex;
    private const int PLATE_COLOR_COUNT = 6;
    private static int _plateSrcW; // 💡 MiniPlate.png読み込み後、Width/6で自動計算
    private static int _plateSrcH; // 💡 MiniPlate.png読み込み後、Heightをそのまま使用

    // 💡 選択中プレートの前面に重ねる枠エフェクト(Lumen/5.Dan/DanSelect/Plate_Effect.png)
    private static Texture2D _plateEffectTex;

    // 💡 MiniPlate.png内の色の並び順(左から順に0,1,2,3,4,5)
    private enum PlateColor { Yellow = 0, Blue = 1, Red = 2, Silver = 3, Gold = 4, Green = 5 }

    // 💡 実際に画面に並べる順番・枚数(黄5・青5・赤5・銀4・金1・緑1 = 全21枚)
    //     並び順を変えたい場合はここを書き換える
    private static readonly PlateColor[] _plateOrder = BuildPlateOrder();

    private static PlateColor[] BuildPlateOrder()
    {
        var list = new List<PlateColor>();
        for (int i = 0; i < 5; i++) list.Add(PlateColor.Yellow);
        for (int i = 0; i < 5; i++) list.Add(PlateColor.Blue);
        for (int i = 0; i < 5; i++) list.Add(PlateColor.Red);
        for (int i = 0; i < 3; i++) list.Add(PlateColor.Silver);
        list.Add(PlateColor.Gold);
        list.Add(PlateColor.Green);
        return list.ToArray();
    }

    // 💡 プレート列の表示位置・大きさ調整用パラメータ(基準:1280x720。好きな値に書き換えてください)
    //     PLATE_START_X/Yは1枚目(先頭=黄色1枚目)の左上座標
    private static float PLATE_START_X = 100f;
    private static float PLATE_START_Y = 10f;
    private static float PLATE_SCALE = 0.67f;   // プレート1枚の拡大率(原寸=MiniPlate.png切り出しサイズ)
    private static float PLATE_SPACING = -8f;   // プレート間の隙間(px, 拡大後基準)

    // ==================================================================
    // 💡 段位名(プレートに縦書きで割り当てる名前。_plateOrderの先頭から順に対応)
    //     五級～達人で19個。_plateOrder(21枚)のうち最後の2枚(金・緑)には現状割り当てが無い。
    // ==================================================================
    // ==================================================================
    // 💡 段位名(プレートに縦書きで割り当てる名前)。_plateOrder(21枚)と1:1で対応する。
    //     黄5(五級~一級)・青5+赤5(初段~十段)・銀4のうち3枚(玄人・名人・超人、4枚目は未使用)
    //     ・金1(達人)・緑1(外伝)
    // ==================================================================
    private static readonly string[] _rankNames =
    {
        "五級", "四級", "三級", "二級", "一級",                                   // 黄(5)
        "初段", "二段", "三段", "四段", "五段", "六段", "七段", "八段", "九段", "十段", // 青+赤(10)
        "玄人", "名人", "超人",                                               // 銀(4。4枚目は未使用)
        "達人",                                                                  // 金(1)
        "外伝",                                                                  // 緑(1)
    };

    // 💡 各段位に対応するdan.jsonが見つかったかどうか(見つからない場合はプレートを50%暗くする)
    private static readonly bool[] _rankAvailable = new bool[_rankNames.Length];
    // 💡 各段位に対応するdan.jsonの_danFilesでのインデックス(見つからなければ-1)
    private static readonly int[] _rankDanFileIndex = new int[_rankNames.Length];

    // 💡 「存在しない段位」を示す暗さ(50%)。透明度(アルファ)は変えず、RGBを半分にすることで暗くする
    private static readonly Color DARKEN_TINT = new Color((byte)128, (byte)128, (byte)128, (byte)255);

    // 💡 段位名(縦書き)の表示位置・大きさ調整用パラメータ(基準:1280x720。好きな値に書き換えてください)
    //     RANKTEXT_OFFSET_Yはプレート左上からの縦オフセット
    private static float RANKTEXT_OFFSET_Y = 45f;
    private static float RANKTEXT_FONT_SIZE = 24f;
    private static float RANKTEXT_LINE_HEIGHT = 24f;

    // 💡 現在選択中の段位(_rankNamesのインデックス)。SettingsPanel.KeyEdgeL/KeyEdgeR(Ka1/Ka2)で移動する
    private static int _selectedRankIndex;

    // ==================================================================
    // 💡 Panel.png(段位道場の枠・土台パネル)関連
    // ==================================================================
    private static Texture2D _panelTex;

    // 💡 Panel.pngの表示位置・大きさ調整用パラメータ(基準:1280x720。好きな値に書き換えてください)
    //     PANEL_X/Yは中心座標
    private static float PANEL_X = 725f;
    private static float PANEL_Y = 415f;
    private static float PANEL_SCALE = 0.67f;

    // ==================================================================
    // 💡 dan.json関連(段位の定義ファイル)。Songs/以下を再帰的に自動探索する。
    // ==================================================================
    private const string DAN_JSON_SEARCH_ROOT = "Songs";

    private sealed class DanSongJson
    {
        public string path { get; set; }
        public int difficulty { get; set; }
        public string genre { get; set; }
        public bool isHidden { get; set; }
    }

    private sealed class DanJson
    {
        public string title { get; set; }
        public int danIndex { get; set; }
        public string danPlatePath { get; set; }
        public string danPanelSidePath { get; set; }
        public string danTitlePlatePath { get; set; }
        public string danMiniPlatePath { get; set; }
        public string danMiniPlateText { get; set; }
        public List<DanSongJson> danSongs { get; set; }
    }

    private sealed class DanFileInfo
    {
        public string Path;
        public string Title;
    }

    // 💡 Songs/以下で見つかった全dan.jsonの一覧(パスとtitleだけ先に読んでおく軽量スキャン)
    private static readonly List<DanFileInfo> _danFiles = new();

    // 💡 現在選択中の段位のdan.jsonから読み取った、画面に表示する曲名の一覧(danSongsの順番通り)
    private static readonly List<string> _songTitles = new();
    // 💡 曲名と対になる難易度(danSongsのdifficulty。0=簡単/1=普通/2=難しい/3=鬼/4=裏鬼)。_songTitlesと1:1で対応する
    private static readonly List<int> _songDifficulties = new();

    // ==================================================================
    // 💡 段位モード演奏用データ(Don1/Don2確定時にProgram.cs側へ渡す)。
    //     _songTitlesと1:1で対応し、非表示(isHidden)の曲も実際に演奏するため含める。
    // ==================================================================
    private static readonly List<string> _danSongFullPaths = new(); // 💡 dan.jsonのフォルダ基準で解決したTJAの実パス
    private static readonly List<TaikoNauts.Core.Taiko.Charts.Course> _danSongCourses = new(); // 💡 difficultyをCourseに変換したもの
    private static readonly List<string> _danSongGenres = new();
    // 💡 各曲のノーツ数(Don/Ka)。Gauge.Init()の_unit計算を「3曲合計」基準にするために使う。
    private static readonly List<int> _danSongNoteCounts = new();

    // 💡 SettingsPanelのDon1/Don2(KeyLeft/KeyRight)が押されて段位が確定した瞬間にtrueになる。
    //     Program.cs側で読み取ったら必ずResetConfirm()を呼んでfalseに戻すこと。
    private static bool _confirmed;
    public static bool Confirmed => _confirmed;

    // 💡 確定時にProgram.cs側へ渡す、選択中段位の演奏データ
    public static IReadOnlyList<string> SongPaths => _danSongFullPaths;
    public static IReadOnlyList<TaikoNauts.Core.Taiko.Charts.Course> SongCourses => _danSongCourses;
    public static IReadOnlyList<string> SongGenres => _danSongGenres;
    public static IReadOnlyList<int> SongNoteCounts => _danSongNoteCounts;
    // 💡 DanResultScene表示用に曲名一覧も公開する(danSongsの並び順通り)
    public static IReadOnlyList<string> SongTitles => _songTitles;

    // 💡 選択中段位のconditions(Exam.cs解析済み)。Enso.Init()側がMiniTaiko.SetDanConditions()に渡すため公開する。
    private static List<Exam.Condition> _selectedConditions = new();
    public static IReadOnlyList<Exam.Condition> SelectedConditions => _selectedConditions;

    // 💡 選択中段位のconditionGauge(魂ゲージの合格閾値)。DanResultScene側の魂ゲージ条件バー表示用に公開する。
    private static Exam.ConditionGauge _selectedGauge;
    public static Exam.ConditionGauge SelectedGauge => _selectedGauge;

    /// <summary>
    /// 選択中段位の全曲(1~3曲)を合計したノーツ数。Gauge.Init()の_unit計算に渡し、
    /// 「3曲通してゲージが正しく満タンまで上がる」ようにするための値。
    /// </summary>
    public static int TotalNoteCount
    {
        get
        {
            int sum = 0;
            for (int i = 0; i < _danSongNoteCounts.Count; i++) sum += _danSongNoteCounts[i];
            return sum;
        }
    }

    public static string SelectedDanTitle =>
        (_selectedRankIndex >= 0 && _selectedRankIndex < _rankNames.Length) ? _rankNames[_selectedRankIndex] : "";

    /// <summary>
    /// 現在TJA.Load()済みの譜面から、Don/Kaノーツの総数を数える(Gauge.csと同じ数え方)。
    /// </summary>
    private static int CountNotes()
    {
        int count = 0;
        var chips = TJA.Chips;
        if (chips == null) return 0;

        for (int i = 0; i < chips.Count; i++)
        {
            var t = chips[i]._noteType;
            if (t == NoteType.Don || t == NoteType.Ka || t == NoteType.DON || t == NoteType.KA)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Program.cs側がConfirmedを読み取って処理した後に呼び、確定フラグを戻す。
    /// </summary>
    public static void ResetConfirm()
    {
        _confirmed = false;
    }

    // 💡 曲名リストの表示位置・大きさ調整用パラメータ(基準:1280x720。好きな値に書き換えてください)
    private static float SONGLIST_X = 400f;
    private static float SONGLIST_Y = 172.5f;
    private static float SONGLIST_LINE_HEIGHT = 75f;
    private static float SONGLIST_FONT_SIZE = 28f;

    // ==================================================================
    // 💡 Song_CourseSymbol.png(難易度アイコン)関連
    //     1枚の画像に「花(簡単)・双葉(普通)・満開(難しい)・鬼面(鬼)・裏鬼面(裏鬼)」の5種類が
    //     横一列(5分割)で並んでいるので、difficultyの値(0~4)をそのまま列インデックスとして使う。
    // ==================================================================
    private static Texture2D _courseSymbolTex;
    private const int COURSE_SYMBOL_COUNT = 5;
    private static int _courseSymbolSrcW; // 💡 Song_CourseSymbol.png読み込み後、Width/5で自動計算
    private static int _courseSymbolSrcH; // 💡 Song_CourseSymbol.png読み込み後、Heightをそのまま使用

    // 💡 難易度アイコンの表示位置・大きさ調整用パラメータ(基準:1280x720。好きな値に書き換えてください)
    //     曲名の左側に、曲名と同じ行の高さで並べる想定。
    private static float COURSESYMBOL_OFFSET_X = -65f; // 💡 曲名(SONGLIST_X)からのXオフセット(負の値で左側)
    private static float COURSESYMBOL_OFFSET_Y = -12.5f;    // 💡 曲名の行(SONGLIST_Y + i*LINE_HEIGHT)からのYオフセット
    private static float COURSESYMBOL_SCALE = 0.67f;

    // 💡 曲名の文字色・縁取り色・縁取り太さ(px, フォントサイズ基準ではなく実ピクセル値)
    private static readonly Color SONGLIST_TEXT_COLOR = Color.White;
    private static readonly Color SONGLIST_OUTLINE_COLOR = new Color((byte)0x26, (byte)0x26, (byte)0x26, (byte)255);
    private static float SONGLIST_OUTLINE_THICKNESS = 4.5f;

    // ==================================================================
    // 💡 合格条件(dan.jsonの"conditions")表示関連。判定ロジック自体はExam.csに切り出し済みで、
    //    ここでは選択中の段位のconditionsを読み込み、画面表示用の文字列(_conditionTexts)に変換するだけ。
    //    Condition_Bar.png(土台バー)は条件の種類数ぶんしか並べない(個別条件でも1本)。
    //    全曲共通の条件は1個(Overall)のバッジ、曲ごとに個別の条件は1行の中に1st/2nd/3rdのバッジを
    //    左から右へ並べて表示する。
    // ==================================================================
    private sealed class ConditionEntry
    {
        public ConditionContentRow Row; // 💡 このバッジで使うCondition_Content.pngの行
        public string Value;            // 💡 このバッジに表示する数値文字列(例:「18未満」)
    }

    private static readonly List<(string Label, List<ConditionEntry> Entries)> _conditionTexts = new();
    // 💡 Condition_Bar.png表示用の「条件の種類数」。個別条件(1行内に複数バッジ)も1個として数える。
    private static int _conditionCount;

    // 💡 conditions.type ごとの表示名(日本語)
    private static readonly Dictionary<Exam.ConditionType, string> _conditionTypeLabels = new()
    {
        { Exam.ConditionType.Great, "良の数" },
        { Exam.ConditionType.Good, "可の数" },
        { Exam.ConditionType.Miss, "不可の数" },
        { Exam.ConditionType.Roll, "連打数" },
        { Exam.ConditionType.Hit, "たたけた数" },
        { Exam.ConditionType.Score, "スコア" },
        { Exam.ConditionType.MaxCombo, "コンボ数" },
    };

    // 💡 conditions.type ごとの判定方向(Exam.MeetsThresholdと同じ規則。表示用にこちらでも保持)
    private static readonly HashSet<Exam.ConditionType> _lessThanTypes = new()
    {
        Exam.ConditionType.Good, Exam.ConditionType.Miss,
    };

    // 💡 条件リストの表示位置・大きさ調整用パラメータ(基準:1280x720。好きな値に書き換えてください)
    //     ラベル(「良の数」等)はCONDITIONLABEL_X/Yで固定位置に描画する。
    //     数値部分(「7以上」等)はCondition_Content.pngのバッジ位置(土台)を基準に、
    //     CONDITIONVALUE_OFFSET_X/Yぶんだけずらした位置に描画する(バッジの上に乗せるイメージ)。
    private static float CONDITIONLABEL_X = 590f;
    private static float CONDITIONLABEL_Y = 418f;
    private static float CONDITIONLABEL_LINE_HEIGHT = 87.5f;
    private static float CONDITIONLABEL_FONT_SIZE = 15f;

    // 💡 数値テキストの位置 = バッジ(Condition_Content.png)のdest.X/Y + このオフセット
    private static float CONDITIONVALUE_OFFSET_X = 150f;
    private static float CONDITIONVALUE_OFFSET_Y = 37.5f;
    private static float CONDITIONVALUE_FONT_SIZE = 18f;

    // 💡 条件1つにつき1枚表示するバー画像(Lumen/5.Dan/DanSelect/Condition_Bar.png)。
    //    conditionGauge(魂ゲージ)分は含まず、_conditionTexts(=conditionsのみ)の行数ぶんだけ並べる。
    private static Texture2D _conditionBarTex;
    private static float CONDITIONBAR_X = 475f;
    private static float CONDITIONBAR_Y = 407.5f;
    private static float CONDITIONBAR_SCALE = 0.67f;
    private static float CONDITIONBAR_LINE_HEIGHT = 87.5f;

    // ==================================================================
    // 💡 Condition_Content.png(条件バーの中身。全体条件/個別条件で見た目が違う)関連
    //    1枚の画像に「全体条件・1曲目個別・2曲目個別・3曲目個別」の4種類が縦に並んでいるので、
    //    Height/4で行を分割し、行ごとにソース矩形(切り出し範囲)を分けて使う。
    //    行0=全体条件(渦マーク) / 行1=1st / 行2=2nd / 行3=3rd
    // ==================================================================
    private static Texture2D _conditionContentTex;
    private const int CONDITION_CONTENT_ROW_COUNT = 4;
    private static int _conditionContentSrcW; // 💡 Condition_Content.png読み込み後、Widthをそのまま使用
    private static int _conditionContentSrcH; // 💡 Condition_Content.png読み込み後、Height/4で自動計算

    // 💡 Condition_Content.png内の行の並び順(上から順に0,1,2,3)
    private enum ConditionContentRow { Overall = 0, Song1st = 1, Song2nd = 2, Song3rd = 3 }

    // 💡 個別条件(1st/2nd/3rd)のバッジを1行内で左から右へ並べる際の、バッジ1個あたりの占有幅(px, 拡大後基準)
    //    バッジ画像自体の幅より広めに取ることで、バッジ+数値ぶんの間隔を確保する
    private static float CONDITIONCONTENT_ITEM_WIDTH = 220f;

    // 💡 Condition_Content.png(バッジ)の位置・大きさをCondition_Bar.png(土台)とは独立して調整するためのパラメータ。
    //    バッジの最終的な位置 = (CONDITIONBAR_X + CONDITIONCONTENT_OFFSET_X, CONDITIONBAR_Y + CONDITIONCONTENT_OFFSET_Y)
    private static float CONDITIONCONTENT_OFFSET_X = 10f;
    private static float CONDITIONCONTENT_OFFSET_Y = 30f;
    private static float CONDITIONCONTENT_SCALE = 0.67f;

    // 💡 「良の数」等ラベルの縁取り色・太さ(px, フォントサイズ基準ではなく実ピクセル値)
    private static readonly Color CONDITIONLABEL_OUTLINE_COLOR = new Color((byte)0x4B, (byte)0x3C, (byte)0x33, (byte)255);
    private static float CONDITIONLABEL_OUTLINE_THICKNESS = 5.5f;

    /// <summary>
    /// アプリ起動時に一度だけ呼ぶ(テクスチャ・aup2の読み込み)
    /// </summary>
    public static void Init()
    {
        if (_initialized) return;

        _introAnim = Aup2Anim.Load("Lumen/5.Dan/DanIn/Anime.aup2");
        if (_introAnim == null)
        {
            System.Console.WriteLine("[DanSelectScene] アニメ読み込み失敗: Lumen/5.Dan/DanIn/Anime.aup2");
        }

        _bgTex = Raylib.LoadTexture("Lumen/5.Dan/DanSelect/Bg.png");

        _miniPlateTex = Raylib.LoadTexture("Lumen/5.Dan/DanSelect/MiniPlate.png");
        if (_miniPlateTex.Id != 0)
        {
            _plateSrcW = _miniPlateTex.Width / PLATE_COLOR_COUNT; // 💡 6色分割(1色あたりの幅)
            _plateSrcH = _miniPlateTex.Height;
        }

        _panelTex = Raylib.LoadTexture("Lumen/5.Dan/DanSelect/Panel.png");

        _plateEffectTex = Raylib.LoadTexture("Lumen/5.Dan/DanSelect/Plate_Effect.png");

        _conditionBarTex = Raylib.LoadTexture("Lumen/5.Dan/DanSelect/Condition_Bar.png");

        _conditionContentTex = Raylib.LoadTexture("Lumen/5.Dan/DanSelect/Condition_Content.png");
        if (_conditionContentTex.Id != 0)
        {
            _conditionContentSrcW = _conditionContentTex.Width;
            _conditionContentSrcH = _conditionContentTex.Height / CONDITION_CONTENT_ROW_COUNT; // 💡 縦4分割(1行あたりの高さ)
        }

        _courseSymbolTex = Raylib.LoadTexture("Lumen/5.Dan/DanSelect/Song_CourseSymbol.png");
        if (_courseSymbolTex.Id != 0)
        {
            _courseSymbolSrcW = _courseSymbolTex.Width / COURSE_SYMBOL_COUNT; // 💡 5分割(1難易度あたりの幅)
            _courseSymbolSrcH = _courseSymbolTex.Height;
        }

        ScanDanJsonFiles();
        LoadSongsForSelectedRank();

        _initialized = true;
    }

    /// <summary>
    /// Songs/フォルダ以下を再帰的に探索してdan.jsonを自動検出し、
    /// それぞれの"title"を_rankNamesと突き合わせて、どの段位が実在するかを判定する。
    /// </summary>
    private static void ScanDanJsonFiles()
    {
        _danFiles.Clear();
        for (int i = 0; i < _rankAvailable.Length; i++)
        {
            _rankAvailable[i] = false;
            _rankDanFileIndex[i] = -1;
        }

        if (!Directory.Exists(DAN_JSON_SEARCH_ROOT))
        {
            System.Console.WriteLine($"[DanSelectScene] 検索フォルダが見つかりません: {DAN_JSON_SEARCH_ROOT}");
            return;
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var paths = Directory.GetFiles(DAN_JSON_SEARCH_ROOT, "dan.json", SearchOption.AllDirectories);

        foreach (var path in paths)
        {
            try
            {
                string json = File.ReadAllText(path);
                var dan = JsonSerializer.Deserialize<DanJson>(json, options);
                if (dan == null || string.IsNullOrEmpty(dan.title)) continue;

                _danFiles.Add(new DanFileInfo { Path = path, Title = dan.title });

                int rankIndex = Array.IndexOf(_rankNames, dan.title);
                if (rankIndex >= 0)
                {
                    _rankAvailable[rankIndex] = true;
                    _rankDanFileIndex[rankIndex] = _danFiles.Count - 1;
                }
                else
                {
                    System.Console.WriteLine($"[DanSelectScene] dan.jsonのtitle「{dan.title}」が段位名リストと一致しません: {path}");
                }
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"[DanSelectScene] dan.json読み込み失敗({path}): {ex.Message}");
            }
        }

        // 💡 初期選択は「実在する」最初の段位にする(見つからなければ0番目のまま)
        _selectedRankIndex = 0;
        for (int i = 0; i < _rankAvailable.Length; i++)
        {
            if (_rankAvailable[i])
            {
                _selectedRankIndex = i;
                break;
            }
        }
    }

    /// <summary>
    /// 現在選択中の段位(_selectedRankIndex)に対応するdan.jsonから、
    /// danSongsに書かれた各TJAファイルのタイトルを取得して_songTitlesに詰める。
    /// 曲名の取得方法はEnso.csと同じくTJA.Load()→TJA.Titleを使う。
    /// </summary>
    private static void LoadSongsForSelectedRank()
    {
        _songTitles.Clear();
        _songDifficulties.Clear();
        _danSongFullPaths.Clear();
        _danSongCourses.Clear();
        _danSongGenres.Clear();
        _danSongNoteCounts.Clear();
        _conditionTexts.Clear();
        _conditionCount = 0;
        _selectedConditions.Clear();

        if (_selectedRankIndex < 0 || _selectedRankIndex >= _rankDanFileIndex.Length) return;

        int fileIndex = _rankDanFileIndex[_selectedRankIndex];
        if (fileIndex < 0) return; // 💡 この段位に対応するdan.jsonが存在しない

        string danJsonPath = _danFiles[fileIndex].Path;

        DanJson dan;
        string json;
        try
        {
            json = File.ReadAllText(danJsonPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            dan = JsonSerializer.Deserialize<DanJson>(json, options);
        }
        catch (System.Exception ex)
        {
            System.Console.WriteLine($"[DanSelectScene] dan.json読み込み失敗({danJsonPath}): {ex.Message}");
            return;
        }

        if (dan?.danSongs == null) return;

        // 💡 danSongs内のpathはdan.jsonがあるフォルダからの相対パス
        string danDir = Path.GetDirectoryName(Path.GetFullPath(danJsonPath)) ?? ".";

        foreach (var song in dan.danSongs)
        {
            string tjaPath = Path.Combine(danDir, song.path ?? "");
            string title = song.path ?? "";
            var course = (TaikoNauts.Core.Taiko.Charts.Course)song.difficulty;
            int noteCount = 0;

            if (File.Exists(tjaPath))
            {
                // 💡 非表示曲でもタイトル表示以外(実パス・コース・ジャンル・ノーツ数)は必要になるので、
                //    isHiddenに関わらずTJAは読み込んでおく。段位モードで実際に演奏するコースと
                //    同じ譜面を読ませるため、difficultyから変換したcourseを指定してLoadする。
                TJA.Load(tjaPath, course);
                if (TJA.IsLoaded)
                {
                    if (!string.IsNullOrEmpty(TJA.Title)) title = TJA.Title;
                    noteCount = CountNotes();
                }
            }
            else
            {
                System.Console.WriteLine($"[DanSelectScene] TJAが見つかりません: {tjaPath}");
            }

            // 💡 表示用の曲名リストは、非表示指定なら常に「???」にする
            _songTitles.Add(song.isHidden ? "？？？" : title);
            _songDifficulties.Add(song.difficulty);

            // 💡 段位モード演奏用データは、非表示かどうかに関わらず常に積んでおく
            _danSongFullPaths.Add(tjaPath);
            _danSongCourses.Add(course);
            _danSongGenres.Add(song.genre ?? "");
            _danSongNoteCounts.Add(noteCount);
        }

        // 💡 合格条件(conditions)をExam.csで解析し、画面表示用の文字列に変換する
        _selectedConditions.Clear();
        _selectedGauge = default;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var (gauge, conditions) = Exam.LoadFromDanJsonRoot(doc.RootElement, dan.danSongs.Count);
            _selectedGauge = gauge;
            Console.WriteLine($"[DanSelectScene] conditions解析: {danJsonPath} → {conditions.Count}件");
            _selectedConditions.AddRange(conditions);
            BuildConditionTexts(conditions, dan.danSongs.Count);
        }
        catch (System.Exception ex)
        {
            System.Console.WriteLine($"[DanSelectScene] conditions解析失敗({danJsonPath}): {ex.Message}");
        }
    }

    /// <summary>
    /// Exam.csで解析したconditionsを、画面表示用の文字列(_conditionTexts)に変換する。
    /// 全曲で閾値が同じ条件は1行(「〇〇以上」等)、曲ごとに閾値が異なる条件は曲数ぶんの行に分けて出す。
    /// </summary>
    private static void BuildConditionTexts(List<Exam.Condition> conditions, int songCount)
    {
        _conditionTexts.Clear();

        foreach (var cond in conditions)
        {
            string label = _conditionTypeLabels.TryGetValue(cond.Type, out var l) ? l : cond.Type.ToString();
            string unit = _lessThanTypes.Contains(cond.Type) ? "未満" : "以上";

            bool sameForAllSongs = true;
            for (int i = 1; i < cond.ThresholdsPerSong.Length; i++)
            {
                if (cond.ThresholdsPerSong[i].Red != cond.ThresholdsPerSong[0].Red ||
                    cond.ThresholdsPerSong[i].Gold != cond.ThresholdsPerSong[0].Gold)
                {
                    sameForAllSongs = false;
                    break;
                }
            }

            var entries = new List<ConditionEntry>();

            if (sameForAllSongs)
            {
                // 💡 全曲共通の条件 → バッジ1個(Overall)
                var th = cond.ThresholdsPerSong.Length > 0 ? cond.ThresholdsPerSong[0] : default;
                entries.Add(new ConditionEntry { Row = ConditionContentRow.Overall, Value = $"{th.Red}{unit}" });
            }
            else
            {
                // 💡 曲ごとの個別条件 → 1行の中に1st/2nd/3rdのバッジを左から右へ並べる(バーは増やさない)
                for (int i = 0; i < cond.ThresholdsPerSong.Length; i++)
                {
                    var th = cond.ThresholdsPerSong[i];
                    // 💡 4曲目以降は画像が無いので3曲目分の見た目を使い回す
                    int rowIndex = Math.Min(i + 1, (int)ConditionContentRow.Song3rd);
                    entries.Add(new ConditionEntry { Row = (ConditionContentRow)rowIndex, Value = $"{th.Red}{unit}" });
                }
            }

            _conditionTexts.Add((label, entries));
        }

        _conditionCount = _conditionTexts.Count; // 💡 条件の種類数ぶんだけバーを並べる(個別条件でも1本)
    }

    /// <summary>
    /// タイトル画面からこの画面に遷移してくるたびに呼ぶ(再生状態を先頭からリセット)
    /// </summary>
    public static void Enter()
    {
        _animTimer = 0f;
        _playingIntro = _introAnim != null;
        _confirmed = false;
    }

    /// <summary>
    /// 別の画面(タイトル等)へ抜ける際に呼ぶ
    /// </summary>
    public static void ResetState()
    {
        _animTimer = 0f;
        _playingIntro = false;
        StopBgm();
    }

    /// <summary>
    /// Lumen/2.sound/BGM/DanSelect.oggをループ再生する(DanIn再生完了直後に呼ばれる)。
    /// </summary>
    private static void PlayBgm()
    {
        StopBgm(); // 💡 念のため既存の再生を止めてから開始する

        try
        {
            _bgmReader = new VorbisWaveReader(BGM_PATH);
            _bgmLoop = new LoopStream(_bgmReader);
            _bgmOutput = new WaveOutEvent();
            _bgmOutput.Init(_bgmLoop);
            _bgmOutput.Play();
        }
        catch (Exception e)
        {
            System.Console.WriteLine($"[DanSelectScene] BGM再生失敗: {BGM_PATH} ({e.Message})");
        }
    }

    /// <summary>
    /// 再生中のBGMを停止し、関連リソースを解放する。
    /// </summary>
    private static void StopBgm()
    {
        _bgmOutput?.Stop();
        _bgmOutput?.Dispose();
        _bgmOutput = null;

        _bgmLoop?.Dispose();
        _bgmLoop = null;

        _bgmReader?.Dispose();
        _bgmReader = null;
    }

    /// <summary>
    /// 内部のWaveStreamを末尾まで再生したら先頭に戻し、ループ再生させるためのラッパー。
    /// </summary>
    private sealed class LoopStream : WaveStream
    {
        private readonly WaveStream _source;

        public LoopStream(WaveStream source)
        {
            _source = source;
        }

        public override WaveFormat WaveFormat => _source.WaveFormat;
        public override long Length => _source.Length;

        public override long Position
        {
            get => _source.Position;
            set => _source.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = _source.Read(buffer, offset + totalRead, count - totalRead);
                if (read == 0)
                {
                    if (_source.Position == 0) break; // 💡 空ファイル等で無限ループしないための保険
                    _source.Position = 0;
                    continue;
                }
                totalRead += read;
            }
            return totalRead;
        }
    }

    public static void Update(float dt)
    {
        if (_playingIntro)
        {
            _animTimer += dt;
            if (_animTimer >= (float)_introAnim.Duration)
            {
                // 💡 アニメ再生完了 → 以降はBg.pngを表示したまま段位選択操作を受け付ける
                _playingIntro = false;
                PlayBgm();
            }
        }
        else
        {
            UpdateSelection();
        }
    }

    /// <summary>
    /// SettingsPanel.KeyEdgeL(Ka1)/KeyEdgeR(Ka2)で選択中の段位(プレート)を左右に移動する
    /// </summary>
    private static void UpdateSelection()
    {
        if (_rankNames.Length == 0) return;

        int newIndex = _selectedRankIndex;
        if (AnyKeyPressed(SettingsPanel.KeyEdgeL))
        {
            newIndex = (_selectedRankIndex - 1 + _rankNames.Length) % _rankNames.Length;
        }
        else if (AnyKeyPressed(SettingsPanel.KeyEdgeR))
        {
            newIndex = (_selectedRankIndex + 1) % _rankNames.Length;
        }

        if (newIndex != _selectedRankIndex)
        {
            _selectedRankIndex = newIndex;
            LoadSongsForSelectedRank();
            return; // 💡 移動した直後の入力で誤って確定しないよう、この回はここで終える
        }

        // 💡 SettingsPanelのDon1(KeyLeft)/Don2(KeyRight)で、実在する段位のみ確定できる
        bool hasRank = _selectedRankIndex >= 0 && _selectedRankIndex < _rankAvailable.Length;
        // 746行目付近
        if (!_confirmed && hasRank && _rankAvailable[_selectedRankIndex] && _danSongFullPaths.Count > 0)
        {
            if (AnyKeyPressed(SettingsPanel.KeyLeft) || AnyKeyPressed(SettingsPanel.KeyRight))
            {
                _confirmed = true;
                StopBgm(); // 💡 ここに追記：決定した瞬間にBGMを停止する
            }
        }
    }

    private static bool AnyKeyPressed(List<int> keys)
    {
        if (keys == null) return false;
        for (int i = 0; i < keys.Count; i++)
        {
            if (Raylib.IsKeyPressed((KeyboardKey)keys[i])) return true;
        }
        return false;
    }

    public static void Draw()
    {
        int sw = Program.VirtualWidth;
        int sh = Program.VirtualHeight;

        Raylib.ClearBackground(Color.Black);

        if (_playingIntro && _introAnim != null)
        {
            _introAnim.Draw(0, 0, sw, sh, _animTimer, Color.White);
        }
        else
        {
            if (_bgTex.Id != 0)
            {
                Raylib.DrawTexturePro(_bgTex,
                    new Rectangle(0, 0, _bgTex.Width, _bgTex.Height),
                    new Rectangle(0, 0, sw, sh),
                    new Vector2(0, 0), 0f, Color.White);
            }

            DrawPanel(sw, sh);
            DrawPlates(sw, sh);
            DrawSongTitles(sw, sh);
            DrawConditions(sw, sh);
        }
    }

    /// <summary>
    /// Panel.png(段位道場の枠・土台パネル)を中心座標基準で描画する
    /// </summary>
    private static void DrawPanel(int sw, int sh)
    {
        if (_panelTex.Id == 0) return;

        float refScale = sw / 1280f; // 💡 1280x720基準の座標をそのまま使うためのスケール
        float w = _panelTex.Width * PANEL_SCALE * refScale;
        float h = _panelTex.Height * PANEL_SCALE * refScale;
        Raylib.DrawTexturePro(_panelTex,
            new Rectangle(0, 0, _panelTex.Width, _panelTex.Height),
            new Rectangle(PANEL_X * refScale, PANEL_Y * refScale, w, h),
            new Vector2(w / 2f, h / 2f), 0f, Color.White);
    }

    /// <summary>
    /// MiniPlate.pngを6色に分割しながら、_plateOrderの順番通りに画面上部へ横一列に並べて描画する。
    /// 各プレートには対応する段位名を縦書きで重ね、実在しない段位は色を50%暗くする。
    /// </summary>
    private static void DrawPlates(int sw, int sh)
    {
        if (_miniPlateTex.Id == 0 || _plateSrcW <= 0 || _plateSrcH <= 0) return;

        float refScale = sw / 1280f; // 💡 1280x720基準の座標をそのまま使うためのスケール
        float plateW = _plateSrcW * PLATE_SCALE * refScale;
        float plateH = _plateSrcH * PLATE_SCALE * refScale;
        float spacing = PLATE_SPACING * refScale;
        float startX = PLATE_START_X * refScale;
        float startY = PLATE_START_Y * refScale;

        Rectangle selectedDest = default;
        for (int i = 0; i < _plateOrder.Length; i++)
        {
            int colorIndex = (int)_plateOrder[i];
            Rectangle src = new Rectangle(colorIndex * _plateSrcW, 0, _plateSrcW, _plateSrcH);
            Rectangle dest = new Rectangle(startX + i * (plateW + spacing), startY, plateW, plateH);

            bool hasRank = i < _rankNames.Length;
            bool available = hasRank && _rankAvailable[i];

            // 💡 「存在しない段位」は透明度でなく色そのものを50%暗くして表現する
            Color plateTint = (hasRank && !available) ? DARKEN_TINT : Color.White;
            Raylib.DrawTexturePro(_miniPlateTex, src, dest, Vector2.Zero, 0f, plateTint);

            if (hasRank)
            {
                DrawVerticalRankLabel(_rankNames[i], dest, refScale, available);
            }

            if (i == _selectedRankIndex)
            {
                selectedDest = dest;
            }
        }

        // 💡 選択中プレートの枠エフェクトを、全プレート・段位名より前面(最後)に描画する
        if (_plateEffectTex.Id != 0)
        {
            Raylib.DrawTexturePro(_plateEffectTex,
                new Rectangle(0, 0, _plateEffectTex.Width, _plateEffectTex.Height),
                selectedDest, Vector2.Zero, 0f, Color.White);
        }
    }

    /// <summary>
    /// 1枚のプレート(dest)の上に、段位名(text)を縦書きで1文字ずつ並べて描画する
    /// </summary>
    private static void DrawVerticalRankLabel(string text, Rectangle dest, float refScale, bool available)
    {
        if (string.IsNullOrEmpty(text)) return;

        float fontSize = RANKTEXT_FONT_SIZE * refScale;
        float lineHeight = RANKTEXT_LINE_HEIGHT * refScale;
        float centerX = dest.X + dest.Width / 2f;
        float startY = dest.Y + RANKTEXT_OFFSET_Y * refScale;

        // 💡 実在しない段位も含めて文字色は常に黒
        Color textColor = Color.Black;

        for (int i = 0; i < text.Length; i++)
        {
            string ch = text[i].ToString();
            Vector2 size = Raylib.MeasureTextEx(G.Font, ch, fontSize, 0);
            Vector2 pos = new Vector2(centerX - size.X / 2f, startY + i * lineHeight);
            G.DrawTextWithOutline16(G.Font, ch, pos, fontSize, textColor, textColor, 0);
        }
    }

    /// <summary>
    /// 選択中の段位のdan.jsonから取得した曲名(_songTitles)を縦に並べて表示する(とりあえずの仮表示)
    /// </summary>
    private static void DrawSongTitles(int sw, int sh)
    {
        if (_songTitles.Count == 0) return;

        float refScale = sw / 1280f; // 💡 1280x720基準の座標をそのまま使うためのスケール
        float x = SONGLIST_X * refScale;
        float y = SONGLIST_Y * refScale;
        float lineHeight = SONGLIST_LINE_HEIGHT * refScale;
        float fontSize = SONGLIST_FONT_SIZE * refScale;

        for (int i = 0; i < _songTitles.Count; i++)
        {
            string title = _songTitles[i];
            if (string.IsNullOrEmpty(title)) continue;

            Vector2 pos = new Vector2(x, y + i * lineHeight);
            G.DrawTextWithOutline16(G.Font, title, pos, fontSize, SONGLIST_TEXT_COLOR, SONGLIST_OUTLINE_COLOR, SONGLIST_OUTLINE_THICKNESS * refScale, 0);

            // 💡 曲名の左側に、difficulty(0~4)に対応するコース記号(Song_CourseSymbol.png)を描画する
            if (i < _songDifficulties.Count)
            {
                DrawCourseSymbol(_songDifficulties[i], x, y + i * lineHeight, refScale);
            }
        }
    }

    /// <summary>
    /// Song_CourseSymbol.png(5分割:0=簡単/1=普通/2=難しい/3=鬼/4=裏鬼)から
    /// difficultyに対応する1コマを切り出して、曲名の左側(基準位置+オフセット)に描画する。
    /// difficultyが範囲外(非表示曲の-1など)の場合は何も描画しない。
    /// </summary>
    private static void DrawCourseSymbol(int difficulty, float songX, float songY, float refScale)
    {
        if (_courseSymbolTex.Id == 0 || _courseSymbolSrcW <= 0 || _courseSymbolSrcH <= 0) return;
        if (difficulty < 0 || difficulty >= COURSE_SYMBOL_COUNT) return;

        Rectangle src = new Rectangle(difficulty * _courseSymbolSrcW, 0, _courseSymbolSrcW, _courseSymbolSrcH);

        float w = _courseSymbolSrcW * COURSESYMBOL_SCALE * refScale;
        float h = _courseSymbolSrcH * COURSESYMBOL_SCALE * refScale;
        float destX = songX + COURSESYMBOL_OFFSET_X * refScale;
        float destY = songY + COURSESYMBOL_OFFSET_Y * refScale;

        Rectangle dest = new Rectangle(destX, destY, w, h);
        Raylib.DrawTexturePro(_courseSymbolTex, src, dest, Vector2.Zero, 0f, Color.White);
    }

    /// <summary>
    /// 選択中の段位の合格条件(_conditionTexts)を縦に並べて表示する(とりあえずの仮表示)。
    /// 各条件の背景として、条件の数だけCondition_Bar.pngを並べる(conditionGaugeぶんは含まない)。
    /// </summary>
    private static void DrawConditions(int sw, int sh)
    {
        if (_conditionTexts.Count == 0) return;

        float refScale = sw / 1280f; // 💡 1280x720基準の座標をそのまま使うためのスケール

        DrawConditionBars(sw, sh, refScale);

        float labelX = CONDITIONLABEL_X * refScale;
        float labelY = CONDITIONLABEL_Y * refScale;
        float labelLineHeight = CONDITIONLABEL_LINE_HEIGHT * refScale;
        float labelFontSize = CONDITIONLABEL_FONT_SIZE * refScale;

        float valueOffsetX = CONDITIONVALUE_OFFSET_X * refScale;
        float valueOffsetY = CONDITIONVALUE_OFFSET_Y * refScale;
        float valueFontSize = CONDITIONVALUE_FONT_SIZE * refScale;

        // 💡 バッジ(Condition_Content.png)自体の基準位置。DrawConditionBarsと同じ計算式にしておくことで
        //    バッジの上に数値がぴったり乗るようにする
        float badgeBaseX = CONDITIONBAR_X * refScale;
        float badgeBaseY = CONDITIONBAR_Y * refScale;
        float lineHeight = CONDITIONBAR_LINE_HEIGHT * refScale;
        float itemSpacing = CONDITIONCONTENT_ITEM_WIDTH * refScale;

        for (int i = 0; i < _conditionTexts.Count; i++)
        {
            var (label, entries) = _conditionTexts[i];

            if (!string.IsNullOrEmpty(label))
            {
                // 💡 「良の数」等のラベルはCONDITIONLABEL_Xを中心として中央ぞろえで描画する
                Vector2 labelSize = Raylib.MeasureTextEx(G.Font, label, labelFontSize, 0);
                Vector2 labelPos = new Vector2(labelX - labelSize.X / 2f, labelY + i * labelLineHeight);
                G.DrawTextWithOutline16(G.Font, label, labelPos, labelFontSize, Color.White, CONDITIONLABEL_OUTLINE_COLOR, CONDITIONLABEL_OUTLINE_THICKNESS * refScale, 0);
            }

            // 💡 全体条件はバッジ1個ぶんの位置に、個別条件は1st/2nd/3rdぶん左から右へ間隔をあけて数値を描画する。
            //    数値位置 = そのバッジ(Condition_Content.png)のdest.X/Y + CONDITIONVALUE_OFFSET_X/Y
            for (int e = 0; e < entries.Count; e++)
            {
                string value = entries[e].Value;
                if (string.IsNullOrEmpty(value)) continue;

                float badgeX = badgeBaseX + e * itemSpacing;
                float badgeY = badgeBaseY + i * lineHeight;
                Vector2 valuePos = new Vector2(badgeX + valueOffsetX, badgeY + valueOffsetY);
                G.DrawTextWithOutline16(G.Font, value, valuePos, valueFontSize, Color.White, Color.Black, valueFontSize * 0.2f, 0);
            }
        }
    }

    /// <summary>
    /// _conditionTexts(=conditionsのみ、conditionGaugeは含まない)の行数ぶんだけ
    /// バー画像を縦に並べて描画する。
    /// Condition_Bar.png(土台の背景バー)を敷いた上に、Condition_Content.png(全体条件/1曲目/2曲目/3曲目の
    /// バッジ部分)を行ごとに切り出して重ねて表示する。どちらも読み込めている前提で両方描画する。
    /// </summary>
    private static void DrawConditionBars(int sw, int sh, float refScale)
    {
        if (_conditionCount == 0) return;

        float x = CONDITIONBAR_X * refScale;
        float y = CONDITIONBAR_Y * refScale;
        float lineHeight = CONDITIONBAR_LINE_HEIGHT * refScale;

        // 💡 1. 土台の背景バー(Condition_Bar.png)を行数ぶん並べる
        if (_conditionBarTex.Id != 0)
        {
            float barW = _conditionBarTex.Width * CONDITIONBAR_SCALE * refScale;
            float barH = _conditionBarTex.Height * CONDITIONBAR_SCALE * refScale;

            for (int i = 0; i < _conditionCount; i++)
            {
                Rectangle dest = new Rectangle(x, y + i * lineHeight, barW, barH);
                Raylib.DrawTexturePro(_conditionBarTex,
                    new Rectangle(0, 0, _conditionBarTex.Width, _conditionBarTex.Height),
                    dest, Vector2.Zero, 0f, Color.White);
            }
        }

        // 💡 2. その上にCondition_Content.png(全体条件/1st/2nd/3rdバッジ)を重ねる。
        //    個別条件の行は、バーを増やさず同じ行の中でバッジを左から右へ並べる。
        //    位置はCONDITIONCONTENT_OFFSET_X/Yでバー(土台)からずらせる。大きさはCONDITIONCONTENT_SCALEで別調整できる。
        if (_conditionContentTex.Id != 0 && _conditionContentSrcW > 0 && _conditionContentSrcH > 0)
        {
            float contentW = _conditionContentSrcW * CONDITIONCONTENT_SCALE * refScale;
            float contentH = _conditionContentSrcH * CONDITIONCONTENT_SCALE * refScale;
            float itemSpacing = CONDITIONCONTENT_ITEM_WIDTH * refScale;
            float contentBaseX = x + CONDITIONCONTENT_OFFSET_X * refScale;
            float contentBaseY = y + CONDITIONCONTENT_OFFSET_Y * refScale;

            for (int i = 0; i < _conditionTexts.Count; i++)
            {
                var (_, entries) = _conditionTexts[i];

                for (int e = 0; e < entries.Count; e++)
                {
                    int rowIndex = (int)entries[e].Row;
                    Rectangle src = new Rectangle(0, rowIndex * _conditionContentSrcH, _conditionContentSrcW, _conditionContentSrcH);
                    Rectangle dest = new Rectangle(contentBaseX + e * itemSpacing, contentBaseY + i * lineHeight, contentW, contentH);
                    Raylib.DrawTexturePro(_conditionContentTex, src, dest, Vector2.Zero, 0f, Color.White);
                }
            }
        }
    }
}
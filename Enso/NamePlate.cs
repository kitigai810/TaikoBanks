using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Raylib_cs;

/// <summary>
/// プレイヤーのネームプレート表示。
/// PlayData.json (Name/Dan/Title/IsGold/DanFrame) の内容に応じて、以下の3パターンを自動判定して描画する。
///
///   ① 段位なし・称号なし … 名前のみ　　　　　　　　: Base.png + 1P.png + 名前テキスト
///   ② 段位なし・称号あり … 称号+名前　　　　　　　 : Base.png + 1P.png + Name.png(1番上=称号台) + 称号/名前テキスト
///   ③ 段位あり(称号も込み) … 称号+名前+段位　　　　: Base.png + 1P.png + Name.png(全部) + RankGold.png(金合格時の段位文字) + 称号/名前/段位テキスト
///
/// 描画順序(奥→手前): Base.png → Name.png(該当スライス) → 各テキスト(段位文字は金合格ならRankGoldグラデーション) → 1P.png
/// (ユーザー仕様の「(全面)1P.png 文字,Name.png Base.pngです」を奥から手前の順に並べ替えたもの)
///
/// 各パターンごとに位置(X,Y)・大きさ(Scale)・テキストのフォントサイズ・縁取りの太さ・色を
/// すべて個別のpublicフィールドとして持たせてあるので、自由に調整できる。
/// 座標はすべて「中心基準」(X,Yがその要素の中心になるように描画する)。
/// </summary>
public static class NamePlate
{
    // ============================================================
    // PlayData.json の内容
    // ============================================================
    private class PlayData
    {
        public bool IsGold { get; set; }
        public int DanFrame { get; set; } // Name.png上から3,4,5番目のどれを使うか(3=銀,4=金,5=虹 など、素材依存)
        public string Name { get; set; } = "";
        public string Dan { get; set; } = "";
        public string Title { get; set; } = "";
    }

    // 読み込むJSONのパス。呼び出し側で変更したい場合はここを書き換える。
    public static string DataPath = "Data/PlayData.json";
    public const string NormalDataPath = "Data/PlayData.json";
    public const string GuestDataPath = "Data/guestPlayData.json";
    /// <summary>2Pネームプレート専用のプレイヤーデータ。</summary>
    public const string P2DataPath = "Data/PlayData2.json";

    private static PlayData _data = new();
    private static PlayData _p2Data = new();

    /// <summary>通常プレイヤーのPlayData.jsonを表示対象にする。</summary>
    public static void UseNormalPlayerData()
    {
        DataPath = NormalDataPath;
        LoadPlayerData();
    }

    /// <summary>タイトルのF/Jショートカット用guestPlayData.jsonを表示対象にする。</summary>
    public static void UseGuestPlayerData()
    {
        DataPath = GuestDataPath;
        LoadPlayerData();
    }

    /// <summary>設定画面から名前・称号・段位名を編集してここで保存する。</summary>
    public static void SavePlayerData(string name, string title, string dan, bool isGold = false, int danFrame = 3)
    {
        try
        {
            _data.Name = name;
            _data.Title = title;
            _data.Dan = dan;
            _data.IsGold = isGold;
            _data.DanFrame = danFrame;

            string dir = Path.GetDirectoryName(DataPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // 既存JSONを読んで上書きマージ（他フィールドを保持）
            var dict = new System.Collections.Generic.Dictionary<string, object>();
            if (File.Exists(DataPath))
            {
                try
                {
                    string existing = File.ReadAllText(DataPath);
                    using var doc2 = JsonDocument.Parse(existing);
                    foreach (var prop in doc2.RootElement.EnumerateObject())
                        dict[prop.Name] = prop.Value.Clone();
                }
                catch { }
            }
            dict["Name"] = name;
            dict["Title"] = title;
            dict["Dan"] = dan;
            dict["IsGold"] = isGold;
            dict["DanFrame"] = danFrame;

            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(DataPath, System.Text.Json.JsonSerializer.Serialize(dict, opts));
        }
        catch (Exception ex)
        {
            Console.WriteLine("[NamePlate] SavePlayerData Error: " + ex.Message);
        }
    }

    /// <summary>2P用データをData/PlayData2.jsonへ保存する。1P/guestのDataPathには触れない。</summary>
    public static void SavePlayerDataP2(string name, string title, string dan, bool isGold = false, int danFrame = 3)
    {
        try
        {
            _p2Data.Name = name;
            _p2Data.Title = title;
            _p2Data.Dan = dan;
            _p2Data.IsGold = isGold;
            _p2Data.DanFrame = danFrame;

            string dir = Path.GetDirectoryName(P2DataPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var dict = new System.Collections.Generic.Dictionary<string, object>();
            if (File.Exists(P2DataPath))
            {
                try
                {
                    string existing = File.ReadAllText(P2DataPath);
                    using var doc2 = JsonDocument.Parse(existing);
                    foreach (var prop in doc2.RootElement.EnumerateObject())
                        dict[prop.Name] = prop.Value.Clone();
                }
                catch { }
            }
            dict["Name"] = name;
            dict["Title"] = title;
            dict["Dan"] = dan;
            dict["IsGold"] = isGold;
            dict["DanFrame"] = danFrame;

            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(P2DataPath, System.Text.Json.JsonSerializer.Serialize(dict, opts));
        }
        catch (Exception ex)
        {
            Console.WriteLine("[NamePlate] SavePlayerDataP2 Error: " + ex.Message);
        }
    }

    // 現在の値をそのまま公開（設定画面でテキストフィールドの初期値として使う）
    public static string CurrentName => _data.Name;
    public static string CurrentTitle => _data.Title;
    public static string CurrentDan => _data.Dan;
    public static bool CurrentIsGold => _data.IsGold;
    public static int CurrentDanFrame => _data.DanFrame;
    public static string CurrentP2Name => _p2Data.Name;
    public static string CurrentP2Title => _p2Data.Title;
    public static string CurrentP2Dan => _p2Data.Dan;
    public static bool CurrentP2IsGold => _p2Data.IsGold;
    public static int CurrentP2DanFrame => _p2Data.DanFrame;

    public static void LoadPlayerData(bool reloadFonts = true)
    {
        _data = new PlayData();
        if (!File.Exists(DataPath)) return;

        try
        {
            string json = File.ReadAllText(DataPath);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("IsGold", out var ig)) _data.IsGold = ig.GetBoolean();
            if (root.TryGetProperty("DanFrame", out var df)) _data.DanFrame = df.GetInt32();
            if (root.TryGetProperty("Name", out var nm)) _data.Name = nm.GetString() ?? "";
            if (root.TryGetProperty("Dan", out var dn)) _data.Dan = dn.GetString() ?? "";
            if (root.TryGetProperty("Title", out var tt)) _data.Title = tt.GetString() ?? "";
        }
        catch (Exception ex)
        {
            Console.WriteLine("[NamePlate] LoadPlayerData Error: " + ex.Message);
        }

        // 💡 名前・段位・称号にPlayData.json保存時点で新しく使われた文字があっても表示崩れしないよう、
        //    プレイヤーデータを読み込むたびにメインFont(G.Font)を最新の文字セットで再ロードする
        if (reloadFonts)
        {
            try { G.InitializeFonts(SongSelectScene.SongsRoot); }
            catch (Exception ex) { Console.WriteLine("[NamePlate] Font reload Error: " + ex.Message); }
        }
    }

    /// <summary>Data/PlayData2.jsonを読み込み、2Pネームプレート専用状態を更新する。</summary>
    public static void LoadPlayerDataP2(bool reloadFonts = true)
    {
        _p2Data = new PlayData();
        if (!File.Exists(P2DataPath)) return;

        try
        {
            string json = File.ReadAllText(P2DataPath);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("IsGold", out var ig)) _p2Data.IsGold = ig.GetBoolean();
            if (root.TryGetProperty("DanFrame", out var df)) _p2Data.DanFrame = df.GetInt32();
            if (root.TryGetProperty("Name", out var nm)) _p2Data.Name = nm.GetString() ?? "";
            if (root.TryGetProperty("Dan", out var dn)) _p2Data.Dan = dn.GetString() ?? "";
            if (root.TryGetProperty("Title", out var tt)) _p2Data.Title = tt.GetString() ?? "";
        }
        catch (Exception ex)
        {
            Console.WriteLine("[NamePlate] LoadPlayerDataP2 Error: " + ex.Message);
        }

        if (reloadFonts)
        {
            try { G.InitializeFonts(SongSelectScene.SongsRoot); }
            catch (Exception ex) { Console.WriteLine("[NamePlate] P2 Font reload Error: " + ex.Message); }
        }
    }

    // ============================================================
    // テクスチャ
    // ============================================================
    private const string BasePath = "Lumen/Common/NamePlate/";

    private static Texture2D _baseTex;   // Base.png
    private static Texture2D _iconTex;   // 1P.png
    private static Texture2D _p2IconTex; // 2P.png
    private static Texture2D _nameTex;   // Name.png (5分割して使う)
    private static Texture2D _goldTex;   // RankGold.png (金合格時の段位文字グラデーション用)
    private static bool _loaded;

    // 称号台(V3_TitlePlate)に重ねて再生するaup2アニメーション
    private const string TitleAnimePath = "Lumen/Common/NamePlate/Title/1/Anime.aup2";
    private static Aup2Anim _titleAnime;
    private static double _titleAnimeStartTime;

    private const int NAME_SLICE_COUNT = 5;
    private static float NameSliceH => _nameTex.Height / (float)NAME_SLICE_COUNT;

    // Name.png・Base.png の設計上の基準サイズ。
    // 素材が差し替わってピクセル数が変わっても、この基準サイズに合わせて自動スケールする。
    public static float RefNameW = 275f;
    public static float RefNameSliceH = 80f;
    public static float RefBaseW = 338f;
    public static float RefBaseH = 80f;
    public static float RefTitleAnimeH = 38f; // Anime.aup2の縦サイズ。縦が長い場合はここを小さくする

    // Name.png のスライス番号(0始まり)
    private const int SLICE_TITLE_PLATE = 0; // 称号の土台
    private const int SLICE_DAN_PLATE = 1; // 段位がある時の土台
    // 段位の枠(銀/金/虹など)は SLICE_DAN_FRAME_BASE + (DanFrame-3) → DanFrame=3,4,5 → index 2,3,4
    private const int SLICE_DAN_FRAME_BASE = 2;

    // ゴールド文字描画用シェーダー(段位文字のマスクにRankGold.pngのグラデーションを流し込む)
    private const string GoldTextFs = @"#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
uniform sampler2D texture0; // 文字を白で焼き込んだマスク
uniform sampler2D goldTex;  // RankGold.png
out vec4 finalColor;
void main()
{
    vec4 mask = texture(texture0, fragTexCoord);
    vec4 gold = texture(goldTex, fragTexCoord);
    finalColor = vec4(gold.rgb, mask.a * gold.a) * fragColor;
}";
    private static Shader _goldShader;
    private static int _goldShaderLoc;
    private static bool _goldShaderLoaded;

    // 💡 金グラデーション文字用マスクのキャッシュ。
    private static RenderTexture2D _goldMask;
    private static bool _goldMaskLoaded;
    private static string _goldMaskKey = "";

    public static void Init()
    {
        if (!_loaded)
        {
            _baseTex = Raylib.LoadTexture(BasePath + "Base.png");
            _iconTex = Raylib.LoadTexture(BasePath + "1P.png");
            _p2IconTex = Raylib.LoadTexture(BasePath + "2P.png");
            _nameTex = Raylib.LoadTexture(BasePath + "Name.png");
            _goldTex = Raylib.LoadTexture(BasePath + "RankGold.png");

            if (_baseTex.Id == 0) Console.WriteLine("[NamePlate] Base.png の読み込みに失敗しました: " + BasePath + "Base.png");
            if (_iconTex.Id == 0) Console.WriteLine("[NamePlate] 1P.png の読み込みに失敗しました: " + BasePath + "1P.png");
            if (_p2IconTex.Id == 0) Console.WriteLine("[NamePlate] 2P.png の読み込みに失敗しました: " + BasePath + "2P.png（1Pアイコンで代用します）");
            if (_nameTex.Id == 0) Console.WriteLine("[NamePlate] Name.png の読み込みに失敗しました: " + BasePath + "Name.png");
            if (_goldTex.Id == 0) Console.WriteLine("[NamePlate] RankGold.png の読み込みに失敗しました: " + BasePath + "RankGold.png");

            _titleAnime = Aup2Anim.Load(TitleAnimePath);
            if (_titleAnime == null) Console.WriteLine("[NamePlate] Anime.aup2 の読み込みに失敗しました: " + TitleAnimePath);
            _titleAnimeStartTime = Raylib.GetTime();

            VramProbe.Track("NamePlate.Init (all)");
            _loaded = true;
        }
        if (!_goldShaderLoaded)
        {
            _goldShader = Raylib.LoadShaderFromMemory(null, GoldTextFs);
            _goldShaderLoc = Raylib.GetShaderLocation(_goldShader, "goldTex");
            _goldShaderLoaded = true;
        }
        LoadPlayerData(reloadFonts: false);
        LoadPlayerDataP2(reloadFonts: false);
    }

    public static void Unload()
    {
        if (_loaded)
        {
            if (_baseTex.Id != 0) Raylib.UnloadTexture(_baseTex);
            if (_iconTex.Id != 0) Raylib.UnloadTexture(_iconTex);
            if (_p2IconTex.Id != 0) Raylib.UnloadTexture(_p2IconTex);
            if (_nameTex.Id != 0) Raylib.UnloadTexture(_nameTex);
            if (_goldTex.Id != 0) Raylib.UnloadTexture(_goldTex);
            _titleAnime?.Dispose();
            _titleAnime = null;
            _loaded = false;
        }
        if (_goldShaderLoaded)
        {
            Raylib.UnloadShader(_goldShader);
            _goldShaderLoaded = false;
        }
        if (_goldMaskLoaded)
        {
            Raylib.UnloadRenderTexture(_goldMask);
            _goldMaskLoaded = false;
            _goldMaskKey = "";
        }
    }

    // ============================================================
    // 全体位置調整（① ② ③ すべての要素にまとめて加算・乗算される）
    // ============================================================
    public static float GlobalX = -35f;
    public static float GlobalY = 450f;
    public static float GlobalScale = 1f;

    // 2Pは下段レーン用に独立して調整可能。実機の見た目に合わせて変更する。
    public static float P2GlobalX = -35f;
    public static float P2GlobalY = 550f;
    public static float P2GlobalScale = 1f;

    // ============================================================
    // ① 段位なし・称号なし（名前のみ）
    // ============================================================
    public static float V1_BaseX = 200f, V1_BaseY = 40f, V1_BaseScale = 1f;
    public static float V1_IconX = 70f, V1_IconY = 40f, V1_IconScale = 1f;

    public static float V1_NameCenterX = 210f;
    public static float V1_NameOffsetX = 0f;
    public static float V1_NameY = 40f;
    public static float V1_NameFontSize = 32f;
    public static float V1_NameOutline = 5f;
    public static Color V1_NameColor = Color.White;
    public static Color V1_NameOutlineColor = Color.Black;

    // ============================================================
    // ② 段位なし・称号あり（称号＋名前）
    // ============================================================
    public static float V2_BaseX = 200f, V2_BaseY = 40f, V2_BaseScale = 1f;
    public static float V2_IconX = 70f, V2_IconY = 40f, V2_IconScale = 1f;

    public static float V2_TitlePlateX = 200f, V2_TitlePlateY = 35f, V2_TitlePlateScale = 1f;

    public static float V2_TitleAnimeScale = 1.25f;
    public static float V2_TitleAnimeOffsetX = 0f;
    public static float V2_TitleAnimeOffsetY = -12.5f;

    public static float V2_TitleNameCenterX = 200f;

    public static float V2_TitleOffsetX = 10f;
    public static float V2_TitleY = 21f;
    public static float V2_TitleFontSize = 20f;
    public static float V2_TitleOutline = 0f;
    public static Color V2_TitleColor = Color.Black;
    public static Color V2_TitleOutlineColor = Color.Black;

    public static float V2_NameOffsetX = 20f;
    public static float V2_NameY = 55f;
    public static float V2_NameFontSize = 24f;
    public static float V2_NameOutline = 8f;
    public static Color V2_NameColor = Color.White;
    public static Color V2_NameOutlineColor = Color.Black;

    // ============================================================
    // ③ 段位あり（称号＋名前＋段位）… Name.png を全部使う
    // ============================================================
    public static float V3_BaseX = 200f, V3_BaseY = 40f, V3_BaseScale = 1f;
    public static float V3_IconX = 70f, V3_IconY = 40f, V3_IconScale = 1f;

    // Name.png 1番上：称号台
    public static float V3_TitlePlateX = 200f, V3_TitlePlateY = 35f, V3_TitlePlateScale = 1f;

    // Name.png 2番目：段位台
    public static float V3_DanPlateX = 200f, V3_DanPlateY = 77.5f, V3_DanPlateScale = 1f;

    // Name.png 3,4,5番目：段位の枠(DanFrameで選択)。段位台(77.5f)に重なるように一致
    public static float V3_DanFrameX = 190f, V3_DanFrameY = 75f, V3_DanFrameScale = 1f;

    public static float V3_TitleAnimeScale = 1.25f;
    public static float V3_TitleAnimeOffsetX = 0f;
    public static float V3_TitleAnimeOffsetY = -12.5f;

    public static float V3_TitleNameCenterX = 200f;

    public static float V3_TitleOffsetX = 10f;
    public static float V3_TitleY = 21f;
    public static float V3_TitleFontSize = 20f;
    public static float V3_TitleOutline = 0f;
    public static Color V3_TitleColor = Color.Black;
    public static Color V3_TitleOutlineColor = Color.Black;

    public static float V3_NameOffsetX = 60f;
    public static float V3_NameY = 55f;
    public static float V3_NameFontSize = 24f;
    public static float V3_NameOutline = 8f;
    public static Color V3_NameColor = Color.White;
    public static Color V3_NameOutlineColor = Color.Black;

    public static float V3_DanX = 137.5f, V3_DanY = 57.5f;
    public static float V3_DanFontSize = 24f;
    public static float V3_DanOutline = 8f;
    public static Color V3_DanColor = Color.White;
    public static Color V3_DanOutlineColor = Color.Black;

    // ============================================================
    // 文字数オーバー時の自動縮小設定
    // 名前が NameShrinkLength より多い／称号が TitleShrinkLength より多い場合、
    // 該当テキストのフォントサイズに *ShrinkScale を掛けて小さく表示する。
    // ============================================================
    public static int NameShrinkLength = 7;
    public static float NameShrinkPerChar = 0.125f; // しきい値を1文字超えるごとにこの割合ずつ縮小
    public static float NameShrinkMinScale = 0.5f; // これ以上は縮小しない下限
    public static int TitleShrinkLength = 12;
    public static float TitleShrinkPerChar = 0.125f;
    public static float TitleShrinkMinScale = 0.5f;

    // ============================================================
    // 描画
    // ============================================================

    public static void Draw(float vx, float vy, float s, byte alpha = 255)
    {
        DrawData(_data, false, GlobalX, GlobalY, GlobalScale, vx, vy, s, alpha);
    }

    /// <summary>Data/PlayData2.jsonの内容を2Pアイコン付きで下段へ描画する。</summary>
    public static void DrawP2(float vx, float vy, float s, byte alpha = 255)
    {
        DrawData(_p2Data, true, P2GlobalX, P2GlobalY, P2GlobalScale, vx, vy, s, alpha);
    }

    private static void DrawData(PlayData data, bool isP2, float globalX, float globalY, float globalScale,
        float vx, float vy, float s, byte alpha)
    {
        if (!_loaded) return;

        PlayData previousData = _data;
        bool previousDrawingP2 = _drawingP2;
        byte previousAlpha = _alpha;
        _data = data;
        _drawingP2 = isP2;
        _alpha = alpha;

        try
        {
            float gs = s * globalScale;
            float gvx = vx + globalX * s;
            float gvy = vy + globalY * s;

            bool hasDan = !string.IsNullOrEmpty(_data.Dan);
            bool hasTitle = !string.IsNullOrEmpty(_data.Title);

            if (hasDan) DrawVariant3(gvx, gvy, gs);
            else if (hasTitle) DrawVariant2(gvx, gvy, gs);
            else DrawVariant1(gvx, gvy, gs);
        }
        finally
        {
            _data = previousData;
            _drawingP2 = previousDrawingP2;
            _alpha = previousAlpha;
        }
    }

    private static byte _alpha = 255;
    private static bool _drawingP2;
    private static Texture2D ActiveIconTex => _drawingP2 && _p2IconTex.Id != 0 ? _p2IconTex : _iconTex;

    private static Color WithAlpha(Color c)
    {
        return new Color(c.R, c.G, c.B, (byte)(c.A * _alpha / 255));
    }

    // ---- ① 名前のみ ----
    private static void DrawVariant1(float vx, float vy, float s)
    {
        DrawBase(V1_BaseX, V1_BaseY, V1_BaseScale, vx, vy, s);
        DrawText(_data.Name, V1_NameCenterX + V1_NameOffsetX, V1_NameY, V1_NameFontSize, V1_NameOutline,
            V1_NameColor, V1_NameOutlineColor, false, vx, vy, s, isName: true);
        DrawCentered(ActiveIconTex, V1_IconX, V1_IconY, V1_IconScale, vx, vy, s);
    }

    // ---- ② 称号＋名前 ----
    private static void DrawVariant2(float vx, float vy, float s)
    {
        DrawBase(V2_BaseX, V2_BaseY, V2_BaseScale, vx, vy, s);
        DrawNameSlice(SLICE_TITLE_PLATE, V2_TitlePlateX, V2_TitlePlateY, V2_TitlePlateScale, vx, vy, s);

        DrawTitleAnime(V2_TitlePlateX + V2_TitleAnimeOffsetX, V2_TitlePlateY + V2_TitleAnimeOffsetY,
            V2_TitlePlateScale * V2_TitleAnimeScale, vx, vy, s);

        DrawText(_data.Title, V2_TitleNameCenterX + V2_TitleOffsetX, V2_TitleY, V2_TitleFontSize, V2_TitleOutline,
            V2_TitleColor, V2_TitleOutlineColor, false, vx, vy, s, isTitle: true);
        DrawText(_data.Name, V2_TitleNameCenterX + V2_NameOffsetX, V2_NameY, V2_NameFontSize, V2_NameOutline,
            V2_NameColor, V2_NameOutlineColor, false, vx, vy, s, isName: true);

        DrawCentered(ActiveIconTex, V2_IconX, V2_IconY, V2_IconScale, vx, vy, s);
    }

    // ---- ③ 称号＋名前＋段位 ----
    private static void DrawVariant3(float vx, float vy, float s)
    {
        DrawBase(V3_BaseX, V3_BaseY, V3_BaseScale, vx, vy, s);

        DrawNameSlice(SLICE_TITLE_PLATE, V3_TitlePlateX, V3_TitlePlateY, V3_TitlePlateScale, vx, vy, s);
        DrawNameSlice(SLICE_DAN_PLATE, V3_DanPlateX, V3_DanPlateY, V3_DanPlateScale, vx, vy, s);

        int frameSlice = SLICE_DAN_FRAME_BASE + Math.Clamp(_data.DanFrame - 3, 0, 2);
        DrawNameSlice(frameSlice, V3_DanFrameX, V3_DanFrameY, V3_DanFrameScale, vx, vy, s);

        DrawTitleAnime(V3_TitlePlateX + V3_TitleAnimeOffsetX, V3_TitlePlateY + V3_TitleAnimeOffsetY,
            V3_TitlePlateScale * V3_TitleAnimeScale, vx, vy, s);

        DrawText(_data.Title, V3_TitleNameCenterX + V3_TitleOffsetX, V3_TitleY, V3_TitleFontSize, V3_TitleOutline,
            V3_TitleColor, V3_TitleOutlineColor, false, vx, vy, s, isTitle: true);
        DrawText(_data.Name, V3_TitleNameCenterX + V3_NameOffsetX, V3_NameY, V3_NameFontSize, V3_NameOutline,
            V3_NameColor, V3_NameOutlineColor, false, vx, vy, s, isName: true);
        DrawText(_data.Dan, V3_DanX, V3_DanY, V3_DanFontSize, V3_DanOutline,
            V3_DanColor, V3_DanOutlineColor, _data.IsGold, vx, vy, s);

        DrawCentered(ActiveIconTex, V3_IconX, V3_IconY, V3_IconScale, vx, vy, s);
    }

    // ============================================================
    // 描画ヘルパー
    // ============================================================

    private static void DrawBase(float x, float y, float userScale, float vx, float vy, float s)
    {
        if (_baseTex.Id == 0) return;
        float w = RefBaseW * userScale;
        float h = RefBaseH * userScale;
        var src = new Rectangle(0, 0, _baseTex.Width, _baseTex.Height);
        var dest = new Rectangle(x * s + vx, y * s + vy, w * s, h * s);
        var origin = new Vector2(w * s / 2f, h * s / 2f);
        Raylib.DrawTexturePro(_baseTex, src, dest, origin, 0f, WithAlpha(Color.White));
    }

    private static void DrawCentered(Texture2D tex, float x, float y, float scale, float vx, float vy, float s)
    {
        if (tex.Id == 0) return;
        float w = tex.Width * scale;
        float h = tex.Height * scale;
        var src = new Rectangle(0, 0, tex.Width, tex.Height);
        var dest = new Rectangle(x * s + vx, y * s + vy, w * s, h * s);
        var origin = new Vector2(w * s / 2f, h * s / 2f);
        Raylib.DrawTexturePro(tex, src, dest, origin, 0f, WithAlpha(Color.White));
    }

    private static void DrawNameSlice(int sliceIndex, float x, float y, float scale, float vx, float vy, float s)
    {
        if (_nameTex.Id == 0) return;
        float sliceH = NameSliceH;
        var src = new Rectangle(0, sliceIndex * sliceH, _nameTex.Width, sliceH);
        float w = RefNameW * scale;
        float h = RefNameSliceH * scale;
        var dest = new Rectangle(x * s + vx, y * s + vy, w * s, h * s);
        var origin = new Vector2(w * s / 2f, h * s / 2f);
        Raylib.DrawTexturePro(_nameTex, src, dest, origin, 0f, WithAlpha(Color.White));
    }

    private static void DrawTitleAnime(float x, float y, float scale, float vx, float vy, float s)
    {
        if (_titleAnime == null) return;

        try
        {
            if (_titleAnime.Duration <= 0) return;

            float w = RefNameW * scale * s;
            float h = RefTitleAnimeH * scale * s;
            float centerX = x * s + vx;
            float centerY = y * s + vy;
            float left = centerX - w / 2f;
            float top = centerY - h / 2f;

            double elapsed = Raylib.GetTime() - _titleAnimeStartTime;
            double seconds = elapsed % _titleAnime.Duration;
            if (seconds < 0) seconds += _titleAnime.Duration;

            _titleAnime.Draw(left, top, w, h, (float)seconds, WithAlpha(Color.White));
        }
        catch (Exception ex)
        {
            Console.WriteLine("[NamePlate] Anime.aup2 描画エラー: " + ex.Message);
        }
    }

    /// <summary>半角ASCII英数字かどうか(全角英数字は含めない)</summary>
    private static bool IsAsciiAlnum(int codepoint)
    {
        return (codepoint >= '0' && codepoint <= '9')
            || (codepoint >= 'A' && codepoint <= 'Z')
            || (codepoint >= 'a' && codepoint <= 'z');
    }

    /// <summary>
    /// プレイヤー名だけを対象に、半角ASCII英数字とそれ以外の連続runへ分割する。
    /// 半角ASCII → FontNameLatin(font/dom-bold-bt.ttf)、それ以外(全角英数字・記号・かな・カタカナ・漢字) → G.Font(JP64.png)。
    /// </summary>
    private static List<(bool isAscii, string text)> SplitNameLatinRuns(string text)
    {
        var runs = new List<(bool, string)>();
        var buffer = new System.Text.StringBuilder();
        bool? currentIsAscii = null;

        foreach (var rune in text.EnumerateRunes())
        {
            bool isAscii = IsAsciiAlnum(rune.Value);
            if (currentIsAscii.HasValue && isAscii != currentIsAscii.Value)
            {
                runs.Add((currentIsAscii.Value, buffer.ToString()));
                buffer.Clear();
            }
            currentIsAscii = isAscii;
            buffer.Append(char.ConvertFromUtf32(rune.Value));
        }

        if (currentIsAscii.HasValue) runs.Add((currentIsAscii.Value, buffer.ToString()));
        return runs;
    }

    private static float MeasureNameLatinSplitWidth(string text, float fontSize, float letterSpacingEm)
    {
        var runs = SplitNameLatinRuns(text);
        float total = 0f;
        float spacingPx = letterSpacingEm * fontSize;
        for (int i = 0; i < runs.Count; i++)
        {
            var (isAscii, runText) = runs[i];
            Font runFont = isAscii ? G.FontNameLatin : G.Font;
            total += G.MeasureTextWithOutline16Width(runFont, runText, fontSize, letterSpacingEm);
            if (i + 1 < runs.Count) total += spacingPx;
        }
        return total;
    }

    private static void DrawNameLatinSplit(string text, Vector2 topLeft, float fontSize,
        Color fillColor, Color outlineColor, float outlinePx, float letterSpacingEm)
    {
        var runs = SplitNameLatinRuns(text);
        float penX = topLeft.X;
        float spacingPx = letterSpacingEm * fontSize;
        for (int i = 0; i < runs.Count; i++)
        {
            var (isAscii, runText) = runs[i];
            Font runFont = isAscii ? G.FontNameLatin : G.Font;
            var pos = new Vector2(penX, topLeft.Y);
            G.DrawTextWithOutline16(runFont, runText, pos, fontSize, fillColor, outlineColor, outlinePx, 0f, letterSpacingEm);

            penX += G.MeasureTextWithOutline16Width(runFont, runText, fontSize, letterSpacingEm);
            if (i + 1 < runs.Count) penX += spacingPx;
        }
    }

    private static void DrawText(string text, float x, float y, float fontSize, float outline,
        Color fillColor, Color outlineColor, bool useGoldGradient, float vx, float vy, float s, bool isName = false, bool isTitle = false)
    {
        if (string.IsNullOrEmpty(text)) return;

        Font font = G.Font;

        // 💡 名前が NameShrinkLength 文字より多い、または称号が TitleShrinkLength 文字より多い場合、
        //    超過した文字数が増えるほどフォントサイズをどんどん小さくする(下限あり)。
        if (isName && text.Length > NameShrinkLength)
        {
            int over = text.Length - NameShrinkLength;
            float scale = Math.Max(NameShrinkMinScale, 1f - over * NameShrinkPerChar);
            fontSize *= scale;
        }
        if (isTitle && text.Length > TitleShrinkLength)
        {
            int over = text.Length - TitleShrinkLength;
            float scale = Math.Max(TitleShrinkMinScale, 1f - over * TitleShrinkPerChar);
            fontSize *= scale;
        }

        float scaledFontSize = fontSize * s;
        float outlinePx = outline * s;
        float letterSpacingEm = !isName && outlinePx > 0f
            ? G.GetRecommendedTitleLetterSpacingEm(text)
            : 0f;

        // 💡 名前(isName)だけは半角ASCII英数字→FontNameLatin(dom-bold-bt.ttf)、
        //    それ以外(全角英数字・記号・かな・カタカナ・漢字)→G.Font(JP64.png)に振り分ける。
        //    称号・段位は従来どおりG.Font(JP64.png)のみ。
        // 💡 アウトライン0(outlinePx<=0)のときもRaylib.MeasureTextEx/DrawTextExを直接
        //    呼んではいけない。これらはG.FontのJP64スプライトアトラス経由の描画を認識せず、
        //    素のRaylibデフォルトフォント(日本語グリフ無し・見つからない文字は"?"のような
        //    四角グリフになる)を見てしまう。V2/V3のTitleはOutline=0fなので、まさにこの
        //    パスを踏んで文字化けしていた。常にG.MeasureTextWithOutline16Width /
        //    G.DrawTextWithOutline16(thickness=0)経由にしてJP64アトラスへ描画する。
        float textW = isName
            ? MeasureNameLatinSplitWidth(text, scaledFontSize, letterSpacingEm)
            : G.MeasureTextWithOutline16Width(font, text, scaledFontSize, letterSpacingEm);
        float textH = Raylib.MeasureTextEx(font, text, scaledFontSize, 0).Y;

        float centerX = x * s + vx;
        float centerY = y * s + vy;
        Vector2 topLeft = new Vector2(centerX - textW / 2f, centerY - textH / 2f);

        if (isName)
        {
            DrawNameLatinSplit(text, topLeft, scaledFontSize, WithAlpha(fillColor), WithAlpha(outlineColor), outlinePx, letterSpacingEm);
            return;
        }

        if (!useGoldGradient || _goldTex.Id == 0 || !_goldShaderLoaded)
        {
            G.DrawTextWithOutline16(font, text, topLeft, scaledFontSize, WithAlpha(fillColor), WithAlpha(outlineColor), outlinePx, 0f, letterSpacingEm);
            return;
        }

        DrawGoldGradientText(font, text, topLeft, new Vector2(textW, textH), scaledFontSize, outlinePx, outlineColor, letterSpacingEm);
    }

    private static float MeasureOutline16Width(Font font, string text, float fontSize)
    {
        float width = 0f;
        foreach (char c in text)
        {
            string cs = c.ToString();
            float charWidth = Raylib.MeasureTextEx(font, cs, fontSize, 0).X;
            bool isKana = c >= 0x3000 && c <= 0x30FF;
            width += charWidth * (isKana ? 0.88f : 1.00f);
        }
        return width;
    }

    private static void DrawGoldGradientText(Font font, string text, Vector2 topLeft, Vector2 textSize,
        float scaledFontSize, float outlinePx, Color outlineColor, float letterSpacingEm)
    {
        int texW = Math.Max(1, (int)MathF.Ceiling(textSize.X + outlinePx * 2f));
        int texH = Math.Max(1, (int)MathF.Ceiling(textSize.Y + outlinePx * 2f));
        Vector2 drawOrigin = new Vector2(outlinePx, outlinePx);

        if (outlinePx > 0f)
            G.DrawTextWithOutline16(font, text, topLeft, scaledFontSize, WithAlpha(outlineColor), WithAlpha(outlineColor), outlinePx, 0f, letterSpacingEm);

        string key = $"{text}\u0001{scaledFontSize}\u0001{outlinePx}\u0001{letterSpacingEm}\u0001{texW}\u0001{texH}";
        if (!_goldMaskLoaded || _goldMaskKey != key)
        {
            if (_goldMaskLoaded)
                Raylib.UnloadRenderTexture(_goldMask);

            _goldMask = Raylib.LoadRenderTexture(texW, texH);
            _goldMaskLoaded = true;
            _goldMaskKey = key;
            VramProbe.Track($"NamePlate.GoldMask REGEN (key={key})");

            // 💡 仮想スクリーン(Program._virtualScreen)のBeginTextureModeの中から
            //    _goldMaskへBeginTextureModeをネストするとEndTextureMode後に
            //    描画ターゲットが仮想スクリーンに戻らないため、一度終了して復帰する。
            Raylib.EndTextureMode();
            Raylib.BeginTextureMode(_goldMask);
            Raylib.ClearBackground(new Color((byte)0, (byte)0, (byte)0, (byte)0));
            // 💡 ここも以前はRaylib.DrawTextExを直接呼んでいたため、JP64スプライトアトラスを
            //    素通りしてデフォルトフォントで描画されており、段位(Dan)が金合格でゴールド
            //    グラデーション表示になる場合だけ文字化けする原因になっていた。
            //    G.DrawTextWithOutline16(thickness=0)経由にしてJP64アトラスへ描画する。
            G.DrawTextWithOutline16(font, text, drawOrigin, scaledFontSize, Color.White, Color.White, 0f, 0f, letterSpacingEm);
            Raylib.EndTextureMode();
            Program.BeginVirtualFrame();
        }

        Raylib.SetShaderValueTexture(_goldShader, _goldShaderLoc, _goldTex);

        var src = new Rectangle(0, 0, _goldMask.Texture.Width, -_goldMask.Texture.Height);
        var dest = new Rectangle(topLeft.X - outlinePx, topLeft.Y - outlinePx, texW, texH);

        Raylib.BeginShaderMode(_goldShader);
        Raylib.DrawTexturePro(_goldMask.Texture, src, dest, Vector2.Zero, 0f, WithAlpha(Color.White));
        Raylib.EndShaderMode();
    }
}
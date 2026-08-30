using HarfBuzzSharp;
using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Xml.Linq;
using Buffer = HarfBuzzSharp.Buffer; // System.BufferやRaylib_csとの衝突回避
// プロジェクトのどこかに global using HarfBuzzSharp; がある環境でも
// 「RayFont」が必ずRaylib_cs側に解決されるよう明示エイリアスしておく(保険)
using RayFont = Raylib_cs.Font;

/// <summary>
/// フォントの初期化・管理・アウトライン付きテキスト描画を担当する静的クラス。
/// 曲データ(TJA/box.def)をスキャンして実際に使われている文字だけをアトラス化することで、
/// 数千曲規模でもメインFontのVRAM使用量を抑える設計になっている。
/// </summary>
public static class G
{
    // ------------------------------------------------------------------
    // フォント本体
    // ------------------------------------------------------------------
    public static RayFont FontEn;
    public static RayFont FontJp;
    public static RayFont FontSong;
    public static RayFont Font;
    public static RayFont FontUi;
    public static RayFont FontSub;
    // SongLoadヒント専用。font/hint.otfを優先し、無い場合は共通Fontへフォールバックする。
    public static RayFont FontHint;

    // ------------------------------------------------------------------
    // プレイヤー名専用フォント(文字種でフォントを切り替えて描画する)
    //   ひらがな                   → font/JP64.xml + font/JP64.png
    //   半角英数字・記号(ASCII)     → font/dom-bold-bt.ttf
    //   カタカナ・漢字・それ以外    → font/DF.ttf
    // ------------------------------------------------------------------
    public static RayFont FontNameHiragana;
    public static RayFont FontNameLatin;
    public static RayFont FontNameOther;

    /// <summary>通常フォントの追加文字間。字詰めを無効化するため0。</summary>
    private const float GlyphGap = 0f;

    // ------------------------------------------------------------------
    // メインフォント用スプライトアトラス (font/JP64.xml + font/JP64.png)
    // ------------------------------------------------------------------
    // 既存コードが RayFont を引数に取る公開APIを維持できるよう、メインフォントは
    // RaylibのデフォルトFontを識別子として保持し、実際の描画だけを本アトラスへ振り替える。
    // XMLの fontPoint は切り出し画像を fontSize として描画するときの基準サイズである。
    private const float SpriteReferenceFontSize = 64f;
    // スプライトグリフ間の追加ギャップは入れない。各グリフ本来の幅だけで送る。
    private const float SpriteGlyphGap = 0f;

    private readonly struct SpriteGlyph
    {
        public readonly int OffsetU;
        public readonly int OffsetV;
        public readonly int Width;
        public readonly int Height;

        public SpriteGlyph(int offsetU, int offsetV, int width, int height)
        {
            OffsetU = offsetU;
            OffsetV = offsetV;
            Width = width;
            Height = height;
        }
    }

    private sealed class SpriteFontAtlas
    {
        public readonly Texture2D Texture;
        public readonly int TextureWidth;
        public readonly int TextureHeight;
        public readonly int FontSize;
        public readonly int FontPoint;
        public readonly int FixedHalfWidth;
        public readonly Dictionary<int, SpriteGlyph> Glyphs;

        public SpriteFontAtlas(
            Texture2D texture, int textureWidth, int textureHeight, int fontSize,
            int fontPoint, int fixedHalfWidth, Dictionary<int, SpriteGlyph> glyphs)
        {
            Texture = texture;
            TextureWidth = textureWidth;
            TextureHeight = textureHeight;
            FontSize = fontSize;
            FontPoint = fontPoint;
            FixedHalfWidth = fixedHalfWidth;
            Glyphs = glyphs;
        }
    }

    private static SpriteFontAtlas _mainSpriteFont;
    private static uint _mainSpriteProxyTextureId;

    /// <summary>16方向(22.5度刻み)の縁取りオフセット単位ベクトル</summary>
    private static Vector2[] _outlineOffsets;

    // ------------------------------------------------------------------
    // HarfBuzzによる実シェーピング(CSSの font-feature-settings:"palt" 相当を本物のOpenType
    // GPOS/GSUBテーブル解決で再現する)。RaylibのFontはラスタライズ・描画専用として残し、
    // 字送り位置の計算だけHarfBuzzに任せる二重構成。
    // ------------------------------------------------------------------
    private static readonly Dictionary<uint, HarfBuzzSharp.Font> _hbFonts = new();       // Raylib RayFont.Texture.Id -> HarfBuzz RayFont
    private static readonly Dictionary<uint, HarfBuzzSharp.Face> _hbFaces = new();       // Dispose管理用
    private static readonly Dictionary<uint, HarfBuzzSharp.Blob> _hbBlobs = new();       // Dispose管理用
    private static readonly Dictionary<uint, int> _hbUnitsPerEm = new();

    private struct ShapedGlyph
    {
        public int Codepoint;   // Raylib側描画用の元Unicodeコードポイント(clusterから逆引き)
        public float XAdvance;
        public float XOffset;
        public float YOffset;
    }

    // シェーピング結果のキャッシュ。
    // 💡 キーは (texId, text) のみ。fontSize/letterSpacingEmはキーに含めない。
    //    HarfBuzzのシェーピング結果はupem基準の相対値であり、描画サイズに依存しないため、
    //    フルスクリーン切り替えや解像度変化でscaleが変わるたびに別エントリが増殖する問題を防ぐ。
    //    XAdvance/XOffset/YOffsetはupem生値で保存し、呼び出し側でscale/letterSpacingを掛ける。
    private static readonly Dictionary<(uint texId, string text), ShapedGlyph[]> _shapeCache = new();

    /// <summary>
    /// Raylibでロードした .otf/.ttf と同じファイルをHarfBuzz用にも読み込み、
    /// Raylib RayFont.Texture.Idをキーに紐付けます。InitializeFonts内で各フォントごとに呼んでください。
    /// </summary>
    private static void RegisterHarfBuzzFont(RayFont raylibFont, string path)
    {
        if (!System.IO.File.Exists(path)) return;

        var blob = HarfBuzzSharp.Blob.FromFile(path);
        var face = new HarfBuzzSharp.Face(blob, 0);
        var hbFont = new HarfBuzzSharp.Font(face);

        int upem = face.UnitsPerEm > 0 ? face.UnitsPerEm : 1000;
        // xScale/yScaleを明示的にupemへ固定する。ここを省略するとHarfBuzzSharpのバージョンによって
        // デフォルトscaleが0や不定値のままになり、XAdvanceの単位が食い違って字送りが
        // 文字ごとにバラバラになる(漢字だけ極端に広く、かな/カナは普通に見える、等の症状が出る)。
        hbFont.SetScale(upem, upem);
        hbFont.SetFunctionsOpenType();

        uint key = raylibFont.Texture.Id;
        _hbBlobs[key] = blob;
        _hbFaces[key] = face;
        _hbFonts[key] = hbFont;
        _hbUnitsPerEm[key] = upem;
    }

    // ------------------------------------------------------------------
    // スキャンキャッシュ(songsRoot配下のTJA/box.def走査結果)
    // songsRootが変わらない限りディスクI/Oを丸ごとスキップするためのキャッシュ。
    // ------------------------------------------------------------------
    private static string _scanCacheRoot = null;
    private static HashSet<int> _scanCacheMain = null;   // メインFont用(TJA/box.def本体)
    private static HashSet<int> _scanCacheSub = null;    // サブFont用(EXPLANATION行のみ)

    /// <summary>
    /// 曲データが増減した場合など、明示的にスキャンキャッシュを破棄して次回InitializeFonts時に
    /// 再スキャンさせたい場合に呼ぶ。
    /// SongSelectScene.ReloadSongs() や SettingsPanel の「曲再読み込み」ボタン後に
    /// G.InitializeFonts(songsRoot) を呼び直す前にこれを実行することで、
    /// 新しい曲のタイトル文字・PlayData.json の変更・title.cfg の変更がすべて反映される。
    /// 通常の起動〜プレイフローでは呼ぶ必要はない。
    /// </summary>
    public static void InvalidateSongScanCache()
    {
        _scanCacheRoot = null;
        _scanCacheMain = null;
        _scanCacheSub = null;
    }

    /// <summary>
    /// フォントの初期化と日本語文字コードの読み込みを行います。
    /// </summary>
    /// <param name="songsRoot">
    /// TJA/box.def/PlayData.jsonをスキャンして使用文字を絞り込むための曲データルート。
    /// SongSelectScene.SongsRoot を渡してください。
    /// </param>
    public static void InitializeFonts(string songsRoot)
    {
        // 名前保存後などに再呼び出しされた場合、古いフォントテクスチャをアンロードしてから作り直す
        UnloadFonts();

        int[] codepoints = CreateMainFontCodepoints(songsRoot);
        // Lumen/03.SongLoad/hint.jsonの全文字列を追加してからhint.otfをアトラス化する。
        // これによりヒントデータの日本語・記号・絵文字相当のUnicode文字も欠字にならない。
        int[] hintCodepoints = CreateHintFontCodepoints(codepoints);

        // 診断用ログ: 実際に何文字ぶんロードしているか、推定メモリ量(64px,RGBA想定=最大16KB/文字)
        long estimatedBytes = (long)codepoints.Length * 64L * 64L * 4L;
        Console.WriteLine(
            $"[G.InitializeFonts] メインFont コードポイント数: {codepoints.Length} 文字 / " +
            $"推定アトラスメモリ: 最大 {estimatedBytes / 1024.0 / 1024.0:0.0} MB " +
            "(64px RGBA換算・実際はグリフ形状に応じてパッキングされるためこれより小さいのが通常)");

        // メインフォントは font/JP64.xml + font/JP64.png のスプライトアトラスを使用する。
        // 公開APIが RayFont を引数に取るため、Font には識別用のデフォルトFontを置き、
        // DrawTextWithOutline16 / DrawTextWithOutlineHB / MeasureTextWithOutline16Width 内で
        // このスプライトアトラスへ分岐する。
        string spriteXmlPath = ResolveFontAssetPath("JP64.xml");
        string spritePngPath = ResolveFontAssetPath("JP64.png");
        _mainSpriteFont = TryLoadSpriteFont(spriteXmlPath, spritePngPath);

        Font = Raylib.GetFontDefault();
        _mainSpriteProxyTextureId = Font.Texture.Id;
        FontEn = Font;
        FontJp = Font;
        FontSong = Font;

        if (_mainSpriteFont == null)
        {
            Console.Error.WriteLine(
                "[G.InitializeFonts] font/JP64.xml または font/JP64.png を読み込めません。" +
                "メインフォントはRaylibのデフォルトFontへフォールバックします。");
        }
        else
        {
            Raylib.SetTextureFilter(_mainSpriteFont.Texture, TextureFilter.Bilinear);
            Console.WriteLine(
                $"[G.InitializeFonts] スプライトフォントを読み込みました: " +
                $"{_mainSpriteFont.Glyphs.Count} glyphs / " +
                $"{_mainSpriteFont.TextureWidth}x{_mainSpriteFont.TextureHeight}");
        }

        // 設定UI用フォント(Noto Sans JP)。無ければ共通フォントを流用
        FontUi = System.IO.File.Exists("font/NotoSansJP.ttf")
            ? Raylib.LoadFontEx("font/NotoSansJP.ttf", 64, codepoints, codepoints.Length)
            : Font;

        // サブフォント(font/SubFont.otf)はbox.defのExplanation(説明文)に加え、
        // SettingsPanelのUI全体でも使用するため、メインFontと同じcodepointsでロードする。
        // Explanation専用に絞り込んでいた時代の subCodepoints は不要になった。
        FontSub = System.IO.File.Exists("font/SubFont.otf")
            ? Raylib.LoadFontEx("font/SubFont.otf", 64, codepoints, codepoints.Length)
            : Font;

        // SongLoadヒント専用フォント。exe実行ディレクトリの font/hint.otf を使う。
        // 相対カレントディレクトリには依存せず、hint.jsonに含まれる全コードポイントでロードする。
        string hintFontPath = System.IO.Path.Combine(AppContext.BaseDirectory, "font", "hint.otf");
        FontHint = System.IO.File.Exists(hintFontPath)
            ? Raylib.LoadFontEx(hintFontPath, 64, hintCodepoints, hintCodepoints.Length)
            : Font;

        // プレイヤー名専用フォント3種。メインFont用に集めたcodepointsを文字種で振り分けて渡すことで、
        // 過去に保存された名前の文字も含めて文字化け(欠字)しないようにする。
        int[] nameLatinCps = FilterCodepoints(codepoints, IsAsciiPrintable);
        int[] nameOtherCps = FilterCodepoints(codepoints, cp => !IsHiragana(cp) && !IsAsciiPrintable(cp));

        // ひらがな名もメインFontと同じJP64スプライトで描画する。
        // Font はスプライト描画を識別するためのRaylibデフォルトFontである。
        FontNameHiragana = Font;
        FontNameLatin = System.IO.File.Exists("font/dom-bold-bt.ttf")
            ? Raylib.LoadFontEx("font/dom-bold-bt.ttf", 64, nameLatinCps, nameLatinCps.Length)
            : Font;
        FontNameOther = System.IO.File.Exists("font/DF.ttf")
            ? Raylib.LoadFontEx("font/DF.ttf", 64, nameOtherCps, nameOtherCps.Length)
            : Font;

        Raylib.SetTextureFilter(FontEn.Texture, TextureFilter.Bilinear);
        Raylib.SetTextureFilter(FontJp.Texture, TextureFilter.Bilinear);
        Raylib.SetTextureFilter(FontSong.Texture, TextureFilter.Bilinear);
        Raylib.SetTextureFilter(Font.Texture, TextureFilter.Bilinear);
        Raylib.SetTextureFilter(FontUi.Texture, TextureFilter.Bilinear);
        Raylib.SetTextureFilter(FontSub.Texture, TextureFilter.Bilinear);
        Raylib.SetTextureFilter(FontHint.Texture, TextureFilter.Bilinear);
        Raylib.SetTextureFilter(FontNameHiragana.Texture, TextureFilter.Bilinear);
        Raylib.SetTextureFilter(FontNameLatin.Texture, TextureFilter.Bilinear);
        Raylib.SetTextureFilter(FontNameOther.Texture, TextureFilter.Bilinear);

        // スプライトフォントにはOpenTypeのGSUB/GPOSテーブルが存在しないため、
        // メインFontはHarfBuzz登録せずXMLのグリフ幅で字送りする。
        // それ以外のOTF/TTFフォントは従来どおりHarfBuzzに登録する。
        if (System.IO.File.Exists("font/NotoSansJP.ttf"))
            RegisterHarfBuzzFont(FontUi, "font/NotoSansJP.ttf");
        if (System.IO.File.Exists("font/SubFont.otf"))
            RegisterHarfBuzzFont(FontSub, "font/SubFont.otf");
        if (System.IO.File.Exists(hintFontPath))
            RegisterHarfBuzzFont(FontHint, hintFontPath);
        if (System.IO.File.Exists("font/dom-bold-bt.ttf"))
            RegisterHarfBuzzFont(FontNameLatin, "font/dom-bold-bt.ttf");
        if (System.IO.File.Exists("font/DF.ttf"))
            RegisterHarfBuzzFont(FontNameOther, "font/DF.ttf");

        // 16方向(22.5度刻み)の縁取り方向を単位ベクトルであらかじめ計算
        _outlineOffsets = new Vector2[16];
        for (int i = 0; i < 16; i++)
        {
            float angle = (float)(i * 22.5 * Math.PI / 180.0);
            _outlineOffsets[i] = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
        }
    }

    /// <summary>読み込んだフォントのメモリを解放します</summary>
    public static void UnloadFonts()
    {
        if (Font.Texture.Id == 0) return; // 初回InitializeFonts前は何もしない

        // FontはRaylibのデフォルトFontをスプライト描画の識別子として借用している。
        // デフォルトFontはUnloadFontしてはいけないため、スプライト画像だけを解放する。
        if (_mainSpriteFont != null && _mainSpriteFont.Texture.Id != 0)
            Raylib.UnloadTexture(_mainSpriteFont.Texture);
        _mainSpriteFont = null;
        _mainSpriteProxyTextureId = 0;

        if (FontUi.Texture.Id != 0 && FontUi.Texture.Id != Font.Texture.Id)
            Raylib.UnloadFont(FontUi);
        if (FontSub.Texture.Id != 0 && FontSub.Texture.Id != Font.Texture.Id)
            Raylib.UnloadFont(FontSub);
        if (FontHint.Texture.Id != 0 && FontHint.Texture.Id != Font.Texture.Id)
            Raylib.UnloadFont(FontHint);

        // プレイヤー名専用フォント3種(Font流用時はTexture.Idが重複するので二重解放しないようガード)
        var unloadedNameFontIds = new HashSet<uint>();
        void UnloadNameFontOnce(RayFont f)
        {
            if (f.Texture.Id == 0 || f.Texture.Id == Font.Texture.Id) return;
            if (!unloadedNameFontIds.Add(f.Texture.Id)) return;
            Raylib.UnloadFont(f);
        }
        UnloadNameFontOnce(FontNameHiragana);
        UnloadNameFontOnce(FontNameLatin);
        UnloadNameFontOnce(FontNameOther);

        // テクスチャIDが再利用されるとキャッシュが古いアトラスを指したままになるため破棄
        _atlasCpuCache.Clear();
        _glyphTrimCache.Clear();

        // HarfBuzz側もFont/Face/Blobの順でDisposeしてから破棄(逆順だとネイティブ側で解放エラーになりうる)
        foreach (var f in _hbFonts.Values) f.Dispose();
        foreach (var f in _hbFaces.Values) f.Dispose();
        foreach (var b in _hbBlobs.Values) b.Dispose();
        _hbFonts.Clear();
        _hbFaces.Clear();
        _hbBlobs.Clear();
        _hbUnitsPerEm.Clear();
        _shapeCache.Clear();
    }

    // ------------------------------------------------------------------
    // スプライトフォントの読み込み・描画
    // ------------------------------------------------------------------

    private static string ResolveFontAssetPath(string fileName)
    {
        string applicationPath = System.IO.Path.Combine(AppContext.BaseDirectory, "font", fileName);
        return System.IO.File.Exists(applicationPath)
            ? applicationPath
            : System.IO.Path.Combine("font", fileName);
    }

    private static SpriteFontAtlas TryLoadSpriteFont(string xmlPath, string pngPath)
    {
        if (!System.IO.File.Exists(xmlPath) || !System.IO.File.Exists(pngPath))
            return null;

        Texture2D texture = default;
        try
        {
            var document = XDocument.Load(xmlPath);
            XElement fontElement = document.Root?.Element("font");
            if (fontElement == null)
                throw new InvalidOperationException("font 要素が見つかりません。");

            int AttributeInt(string name, int fallback = 0)
            {
                string value = (string)fontElement.Attribute(name);
                return int.TryParse(value, out int parsed) ? parsed : fallback;
            }

            int textureWidth = AttributeInt("texWidth");
            int textureHeight = AttributeInt("texHeight");
            int fontSize = AttributeInt("fontSize", SpriteReferenceFontSize > 0 ? (int)SpriteReferenceFontSize : 64);
            int fontPoint = AttributeInt("fontPoint", fontSize);
            int fixedHalfWidth = AttributeInt("fixedHalfWidth", Math.Max(1, fontPoint / 2));
            if (fontPoint <= 0)
                throw new InvalidOperationException("fontPoint は1以上である必要があります。");

            var glyphs = new Dictionary<int, SpriteGlyph>();
            foreach (XElement glyphElement in fontElement.Elements("glyph"))
            {
                int GlyphAttributeInt(string name, int fallback = 0)
                {
                    string value = (string)glyphElement.Attribute(name);
                    return int.TryParse(value, out int parsed) ? parsed : fallback;
                }

                int index = GlyphAttributeInt("index", -1);
                int width = GlyphAttributeInt("width");
                int height = GlyphAttributeInt("height");
                if (index < 0 || width <= 0 || height <= 0)
                    continue;

                glyphs[index] = new SpriteGlyph(
                    GlyphAttributeInt("offsetU"),
                    GlyphAttributeInt("offsetV"),
                    width,
                    height);
            }

            if (glyphs.Count == 0)
                throw new InvalidOperationException("有効な glyph 要素がありません。");

            texture = Raylib.LoadTexture(pngPath);
            if (texture.Id == 0)
                throw new InvalidOperationException("PNGテクスチャをロードできませんでした。");

            // XMLのサイズ指定が無い場合でも実画像の寸法を使えるようにする。
            if (textureWidth <= 0) textureWidth = texture.Width;
            if (textureHeight <= 0) textureHeight = texture.Height;

            return new SpriteFontAtlas(
                texture, textureWidth, textureHeight, fontSize, fontPoint, fixedHalfWidth, glyphs);
        }
        catch (Exception exception)
        {
            if (texture.Id != 0)
                Raylib.UnloadTexture(texture);
            Console.Error.WriteLine($"[G.TryLoadSpriteFont] {exception.Message}");
            return null;
        }
    }

    private static bool UsesMainSpriteFont(RayFont font)
        => _mainSpriteFont != null
            && _mainSpriteFont.Texture.Id != 0
            && font.Texture.Id == _mainSpriteProxyTextureId;

    private static float GetSpriteScale(float fontSize)
        => fontSize / Math.Max(1, _mainSpriteFont.FontPoint);

    private static float GetSpriteOutlineScale(float fontSize)
        => fontSize / Math.Max(1, _mainSpriteFont.FontPoint);

    private static bool TryDrawSpriteCodepoint(
        int codepoint, Vector2 position, float fontSize, Color tint, float horizontalScale)
    {
        if (_mainSpriteFont == null || !_mainSpriteFont.Glyphs.TryGetValue(codepoint, out SpriteGlyph glyph))
            return false;

        float scale = GetSpriteScale(fontSize);
        Rectangle source = new Rectangle(glyph.OffsetU, glyph.OffsetV, glyph.Width, glyph.Height);
        Rectangle destination = new Rectangle(
            position.X,
            position.Y,
            glyph.Width * scale * horizontalScale,
            glyph.Height * scale);
        Raylib.DrawTexturePro(_mainSpriteFont.Texture, source, destination, Vector2.Zero, 0f, tint);
        return true;
    }

    private static float GetSpriteAdvance(int codepoint, float fontSize, float horizontalScale)
    {
        float sourceWidth = _mainSpriteFont.Glyphs.TryGetValue(codepoint, out SpriteGlyph glyph)
            ? glyph.Width
            : _mainSpriteFont.FixedHalfWidth;
        return (sourceWidth * horizontalScale + SpriteGlyphGap) * GetSpriteScale(fontSize);
    }

    private static void DrawSpriteTextWithOutline(
        string text, Vector2 position, float fontSize, Color textColor, Color outlineColor,
        float thickness, float letterSpacingEm)
    {
        if (string.IsNullOrEmpty(text) || _mainSpriteFont == null)
            return;

        float scaledThickness = thickness * GetSpriteOutlineScale(fontSize);
        float letterSpacingPx = letterSpacingEm * fontSize;

        void DrawPass(Color color, bool outline)
        {
            float penX = position.X;
            float penY = position.Y;
            bool hasGlyphOnLine = false;
            foreach (var rune in text.EnumerateRunes())
            {
                int codepoint = rune.Value;
                if (codepoint == '\r')
                    continue;
                if (codepoint == '\n')
                {
                    penX = position.X;
                    penY += fontSize * 1.25f;
                    hasGlyphOnLine = false;
                    continue;
                }

                if (hasGlyphOnLine)
                    penX += letterSpacingPx;

                float horizontalScale = GetGlyphHorizontalScale(codepoint);
                Vector2 glyphPosition = new Vector2(penX, penY);
                if (outline)
                {
                    foreach (Vector2 offset in _outlineOffsets)
                        TryDrawSpriteCodepoint(codepoint, glyphPosition + offset * scaledThickness,
                            fontSize, color, horizontalScale);
                }
                else
                {
                    TryDrawSpriteCodepoint(codepoint, glyphPosition, fontSize, color, horizontalScale);
                }

                penX += GetSpriteAdvance(codepoint, fontSize, horizontalScale);
                hasGlyphOnLine = true;
            }
        }

        DrawPass(outlineColor, true);
        DrawPass(textColor, false);
    }

    private static float MeasureSpriteTextWidth(string text, float fontSize, float letterSpacingEm)
    {
        if (string.IsNullOrEmpty(text) || _mainSpriteFont == null)
            return 0f;

        float currentLine = 0f;
        float widestLine = 0f;
        float letterSpacingPx = letterSpacingEm * fontSize;
        bool hasGlyphOnLine = false;

        foreach (var rune in text.EnumerateRunes())
        {
            int codepoint = rune.Value;
            if (codepoint == '\r')
                continue;
            if (codepoint == '\n')
            {
                widestLine = MathF.Max(widestLine, currentLine);
                currentLine = 0f;
                hasGlyphOnLine = false;
                continue;
            }

            if (hasGlyphOnLine)
                currentLine += letterSpacingPx;
            currentLine += GetSpriteAdvance(codepoint, fontSize, GetGlyphHorizontalScale(codepoint));
            hasGlyphOnLine = true;
        }

        return MathF.Max(widestLine, currentLine);
    }

    // ------------------------------------------------------------------
    // グリフ実測(左余白・インク幅)キャッシュ
    // GlyphInfo側の advance 値は信用せず、実際に画面に出るアトラスをCPU側に読み戻して
    // ピクセル単位でインクの範囲を測る。これにより「[ 活 ]→[活]」のように字面まわりの
    // 無駄な空白を検出して詰めて描画できる(HTML側の font-feature-settings:"palt" 相当)。
    // ------------------------------------------------------------------
    private static readonly Dictionary<uint, Image> _atlasCpuCache = new();
    private static readonly Dictionary<(uint texId, int codepoint), (float leftTrim, float inkWidth)> _glyphTrimCache = new();

    private static unsafe Image GetAtlasCpuImage(RayFont font)
    {
        if (_atlasCpuCache.TryGetValue(font.Texture.Id, out var cached))
            return cached;

        Image atlas = Raylib.LoadImageFromTexture(font.Texture);
        Raylib.ImageFormat(&atlas, PixelFormat.UncompressedR8G8B8A8); // フォーマットを固定して読み方を統一
        _atlasCpuCache[font.Texture.Id] = atlas;
        return atlas;
    }

    /// <summary>
    /// アトラス上の実ピクセルをスキャンして、その文字の「左側の余白」と「インクの実幅」を求めます。
    /// </summary>
    private static unsafe (float leftTrim, float inkWidth) GetGlyphTrim(RayFont font, int codepoint)
    {
        var key = (font.Texture.Id, codepoint);
        if (_glyphTrimCache.TryGetValue(key, out var cached))
            return cached;

        (float, float) result = (0f, font.BaseSize * 0.5f);
        int index = Raylib.GetGlyphIndex(font, codepoint);

        if (index >= 0 && font.Recs != null)
        {
            Rectangle rec = font.Recs[index];
            int rx = (int)rec.X, ry = (int)rec.Y, rw = (int)rec.Width, rh = (int)rec.Height;

            if (rw > 0 && rh > 0)
            {
                Image atlas = GetAtlasCpuImage(font);
                int atlasW = atlas.Width;
                byte* data = (byte*)atlas.Data; // RGBA8, 4byte/px

                int minX = -1, maxX = -1;
                for (int x = 0; x < rw; x++)
                {
                    bool hasInk = false;
                    for (int y = 0; y < rh; y++)
                    {
                        int idx = ((ry + y) * atlasW + (rx + x)) * 4;
                        if (data[idx + 3] > 15) { hasInk = true; break; }
                    }
                    if (hasInk)
                    {
                        if (minX < 0) minX = x;
                        maxX = x;
                    }
                }

                result = minX >= 0
                    ? (minX, maxX - minX + 1)
                    : (0f, rw); // インクが無い(スペース等)場合はそのままの幅を使う
            }
        }

        _glyphTrimCache[key] = result;
        return result;
    }

    // ------------------------------------------------------------------
    // 描画
    // ------------------------------------------------------------------

    /// <summary>半角ASCII英数字かどうか。全角英数字は意図的に除外する。</summary>
    private static bool IsLatinAlphanumeric(int codepoint)
    {
        return (codepoint >= '0' && codepoint <= '9')
            || (codepoint >= 'A' && codepoint <= 'Z')
            || (codepoint >= 'a' && codepoint <= 'z');
    }

    private static float GetGlyphHorizontalScale(int codepoint)
        => 1f;

    /// <summary>
    /// 通常フォントまたはJP64スプライトから、指定した横倍率で1グリフを描画する。
    /// 縦方向はfontSizeどおりに保つため、半角ASCIIだけを細くできる。
    /// </summary>
    private static unsafe void DrawTextCodepointScaledX(
        RayFont font, int codepoint, Vector2 position, float fontSize, Color tint, float horizontalScale)
    {
        if (UsesMainSpriteFont(font))
        {
            TryDrawSpriteCodepoint(codepoint, position, fontSize, tint, horizontalScale);
            return;
        }

        if (horizontalScale == 1f)
        {
            Raylib.DrawTextCodepoint(font, codepoint, position, fontSize, tint);
            return;
        }

        int index = Raylib.GetGlyphIndex(font, codepoint);
        if (index < 0 || font.Recs == null || font.Glyphs == null)
            return;

        float scale = fontSize / font.BaseSize;
        float padding = font.GlyphPadding;
        Rectangle rec = font.Recs[index];
        Raylib_cs.GlyphInfo glyph = font.Glyphs[index];
        Rectangle src = new Rectangle(
            rec.X - padding,
            rec.Y - padding,
            rec.Width + 2f * padding,
            rec.Height + 2f * padding);
        Rectangle dst = new Rectangle(
            position.X + (glyph.OffsetX - padding) * scale * horizontalScale,
            position.Y + (glyph.OffsetY - padding) * scale,
            src.Width * scale * horizontalScale,
            src.Height * scale);
        Raylib.DrawTexturePro(font.Texture, src, dst, Vector2.Zero, 0f, tint);
    }

    /// <summary>通常フォントと欧文専用フォントを切り替える連続runを作る。全角英数字は通常フォントに残す。</summary>
    private static List<(RayFont font, string text)> SplitDisplayRuns(RayFont defaultFont, string text)
    {
        var runs = new List<(RayFont, string)>();
        if (string.IsNullOrEmpty(text)) return runs;

        bool canUseLatin = FontNameLatin.Texture.Id != 0
            && FontNameLatin.Texture.Id != defaultFont.Texture.Id;
        var buffer = new System.Text.StringBuilder();
        RayFont currentFont = defaultFont;
        bool hasRun = false;

        foreach (var rune in text.EnumerateRunes())
        {
            int sourceCodepoint = rune.Value;
            // 半角ASCIIだけを欧文フォントへ渡す。全角英数字は入力どおり通常フォントで描く。
            RayFont nextFont = canUseLatin && IsLatinAlphanumeric(sourceCodepoint)
                ? FontNameLatin
                : defaultFont;
            int drawCodepoint = sourceCodepoint;

            if (hasRun && nextFont.Texture.Id != currentFont.Texture.Id)
            {
                runs.Add((currentFont, buffer.ToString()));
                buffer.Clear();
            }
            currentFont = nextFont;
            hasRun = true;
            buffer.Append(char.ConvertFromUtf32(drawCodepoint));
        }

        if (hasRun) runs.Add((currentFont, buffer.ToString()));
        return runs;
    }

    private static bool NeedsLatinFallback(RayFont defaultFont, string text)
    {
        // Font.otf自体に半角ASCIIと全角英数字の別グリフが収録されている。
        // 通常テキストは別フォントへ切り替えず、入力コードポイントをそのままFont.otfへ渡す。
        return false;
    }

    /// <summary>互換性のため残す混在描画ヘルパー。通常テキストではFont.otfを優先するため使用しない。</summary>
    private static void DrawTextWithOutline16MixedLatin(
        RayFont defaultFont, string text, Vector2 position, float fontSize,
        Color textColor, Color outlineColor, float thickness,
        float outlineThickness, float letterSpacingEm)
    {
        var runs = SplitDisplayRuns(defaultFont, text);
        float penX = position.X;
        float letterSpacingPx = letterSpacingEm * fontSize;
        for (int i = 0; i < runs.Count; i++)
        {
            var (runFont, runText) = runs[i];
            DrawTextWithOutline16(runFont, runText, new Vector2(penX, position.Y), fontSize,
                textColor, outlineColor, thickness, outlineThickness, letterSpacingEm);
            penX += MeasureTextWithOutline16Width(runFont, runText, fontSize, letterSpacingEm);
            if (i + 1 < runs.Count) penX += letterSpacingPx;
        }
    }

    /// <summary>
    /// 16方向の黒い縁取りを付けて文字列を描画します。
    /// 内部ではHarfBuzzで正式にシェーピング(palt/kern込み)した字送りを使うため、
    /// 呼び出し側のコードは一切変更不要でHTML版に近い詰まり方になります。
    /// HarfBuzzフォントが未登録(RegisterHarfBuzzFont未実行/ファイル無し)の場合のみ、
    /// 旧ink-trimヒューリスティック版(DrawTextWithOutline16Legacy)に自動フォールバックします。
    /// </summary>
    /// <param name="letterSpacingEm">
    /// CSSの letter-spacing:-0.05em 相当。fontSize基準のem単位で、負値にするほど字間が詰まる
    /// (HTML側の font-feature-settings:"palt" + letter-spacing:-0.05em を模倣)。デフォルト0で従来動作と同じ。
    /// </param>
    public static void DrawTextWithOutline16(RayFont font, string text, Vector2 position, float fontSize, Color textColor, Color outlineColor, float thickness = 7f, float oUTLINE_THICKNESS = 0, float letterSpacingEm = 0f)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (UsesMainSpriteFont(font))
        {
            DrawSpriteTextWithOutline(text, position, fontSize, textColor, outlineColor, thickness, letterSpacingEm);
            return;
        }
        if (NeedsLatinFallback(font, text))
        {
            DrawTextWithOutline16MixedLatin(font, text, position, fontSize, textColor, outlineColor,
                thickness, oUTLINE_THICKNESS, letterSpacingEm);
            return;
        }

        var glyphs = ShapeText(font, text, fontSize, letterSpacingEm);
        if (glyphs.Length == 0)
        {
            // HarfBuzz未登録時のみ旧実装にフォールバック
            DrawTextWithOutline16Legacy(font, text, position, fontSize, textColor, outlineColor, thickness, oUTLINE_THICKNESS, letterSpacingEm);
            return;
        }

        float scale = fontSize / font.BaseSize;
        float scaledThickness = thickness * scale;
        float letterSpacingPx = letterSpacingEm * fontSize; // 💡 呼び出し側でpx変換
        int upem = _hbUnitsPerEm.TryGetValue(font.Texture.Id, out var upemVal) ? upemVal : font.BaseSize;
        float upemScale = fontSize / upem;

        // 1. 縁取りパス(全グリフぶん先に描く)
        float penX = position.X;
        foreach (var g in glyphs)
        {
            // 💡 upem生値 → px変換(ShapeTextはupem生値で保存するようになったため)
            Vector2 basePos = new Vector2(penX + g.XOffset * upemScale, position.Y + g.YOffset * upemScale);
            float horizontalScale = GetGlyphHorizontalScale(g.Codepoint);
            foreach (var offset in _outlineOffsets)
                DrawTextCodepointScaledX(font, g.Codepoint, basePos + offset * scaledThickness, fontSize, outlineColor, horizontalScale);
            penX += g.XAdvance * upemScale * horizontalScale + letterSpacingPx;
        }

        // 2. 本体パス
        penX = position.X;
        foreach (var g in glyphs)
        {
            Vector2 basePos = new Vector2(penX + g.XOffset * upemScale, position.Y + g.YOffset * upemScale);
            float horizontalScale = GetGlyphHorizontalScale(g.Codepoint);
            DrawTextCodepointScaledX(font, g.Codepoint, basePos, fontSize, textColor, horizontalScale);
            penX += g.XAdvance * upemScale * horizontalScale + letterSpacingPx;
        }
    }

    /// <summary>
    /// [旧実装] アトラス実測(ink-trim)ベースの16方向縁取り描画。
    /// 通常はDrawTextWithOutline16(HarfBuzzフォールバック込み)を使ってください。
    /// HarfBuzzフォント自体が読み込めない環境向けの保険としてのみ残しています。
    /// </summary>
    public static void DrawTextWithOutline16Legacy(RayFont font, string text, Vector2 position, float fontSize, Color textColor, Color outlineColor, float thickness = 7f, float oUTLINE_THICKNESS = 0, float letterSpacingEm = 0f)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (UsesMainSpriteFont(font))
        {
            DrawSpriteTextWithOutline(text, position, fontSize, textColor, outlineColor, thickness, letterSpacingEm);
            return;
        }

        float scale = fontSize / font.BaseSize;
        float scaledThickness = thickness * scale;
        float letterSpacingPx = letterSpacingEm * fontSize;

        Vector2 currentPos = position;

        var enumerator = text.EnumerateRunes();
        while (enumerator.MoveNext())
        {
            int cp = enumerator.Current.Value;
            string codepointStr = enumerator.Current.ToString();

            float horizontalScale = GetGlyphHorizontalScale(cp);

            // ink-trimによる字詰めは行わず、グリフ本来の位置から描画する。
            Vector2 drawPos = new Vector2(currentPos.X, currentPos.Y);

            // 1. 周囲16方向にフチを描画
            foreach (var offset in _outlineOffsets)
            {
                DrawTextCodepointScaledX(font, cp, drawPos + offset * scaledThickness, fontSize, outlineColor, horizontalScale);
            }

            // 2. 中央にメインの文字を描画
            DrawTextCodepointScaledX(font, cp, drawPos, fontSize, textColor, horizontalScale);

            // 3. フォント本来のAdvanceXだけで進める。追加の字詰め・ギャップは入れない。
            int glyphIndex = Raylib.GetGlyphIndex(font, cp);
            float advance = glyphIndex >= 0
                ? Raylib.GetGlyphInfo(font, cp).AdvanceX * scale * horizontalScale
                : font.BaseSize * 0.5f * scale;
            currentPos.X += advance + letterSpacingPx;
        }
    }

    /// <summary>
    /// HarfBuzzで実シェーピングした結果を取得します。
    /// 戻り値の XAdvance/XOffset/YOffset は upem 単位の生値です。
    /// 呼び出し側で (fontSize / upem) を掛けてピクセル値に変換してください。
    /// letterSpacingEm も呼び出し側で加算してください。
    /// キャッシュキーは (texId, text) のみで、fontSize/letterSpacingEm には依存しません。
    /// </summary>
    private static ShapedGlyph[] ShapeText(RayFont raylibFont, string text, float fontSize, float letterSpacingEm)
    {
        uint key = raylibFont.Texture.Id;
        var cacheKey = (key, text); // 💡 fontSize/letterSpacingEmをキーから除去
        if (_shapeCache.TryGetValue(cacheKey, out var cached)) return cached;

        if (!_hbFonts.TryGetValue(key, out var hbFont))
        {
            // HarfBuzzフォント未登録(RegisterHarfBuzzFontを呼んでいない)場合は空配列を返し、
            // 呼び出し側で従来のDrawTextWithOutline16にフォールバックできるようにする。
            return Array.Empty<ShapedGlyph>();
        }

        int upem = _hbUnitsPerEm[key];

        using var buffer = new Buffer();
        buffer.AddUtf16(text);
        buffer.GuessSegmentProperties();

        // palt／kernによる自動的な字詰め・カーニングは使用しない。
        var features = Array.Empty<Feature>();
        hbFont.Shape(buffer, features);

        var infos = buffer.GetGlyphInfoSpan();
        var positions = buffer.GetGlyphPositionSpan();

        // 💡 upem生値で保存。scale/letterSpacingは呼び出し側で掛ける。
        var result = new ShapedGlyph[infos.Length];
        for (int i = 0; i < infos.Length; i++)
        {
            int clusterIdx = (int)infos[i].Cluster;
            int cp = clusterIdx < text.Length ? char.ConvertToUtf32(text, clusterIdx) : text[Math.Max(0, text.Length - 1)];

            result[i] = new ShapedGlyph
            {
                Codepoint = cp,
                XAdvance = positions[i].XAdvance,  // upem生値
                XOffset = positions[i].XOffset,   // upem生値
                YOffset = -positions[i].YOffset,  // upem生値(Y軸反転済み)
            };
        }

        _shapeCache[cacheKey] = result;

        if (DebugLogShaping)
        {
            float scale = fontSize / upem;
            System.Diagnostics.Debug.WriteLine($"[G.ShapeText] \"{text}\" upem={upem} fontSize={fontSize} scale={scale:0.###}");
            for (int i = 0; i < result.Length; i++)
            {
                var g = result[i];
                string ch = char.ConvertFromUtf32(g.Codepoint);
                System.Diagnostics.Debug.WriteLine($"  [{i}] '{ch}' (U+{g.Codepoint:X4}) rawXAdvance={positions[i].XAdvance} -> XAdvance={g.XAdvance * scale:0.##}px XOffset={g.XOffset * scale:0.##} YOffset={g.YOffset * scale:0.##}");
            }
        }

        return result;
    }

    /// <summary>
    /// 曲名用の追加字間。字詰めは行わないため常に0を返します。
    /// </summary>
    public static float GetRecommendedTitleLetterSpacingEm(string text)
    {
        return 0f;
    }

    /// <summary>
    /// DrawTextWithOutline16と同一のHarfBuzz字送りで文字列幅を測定します。
    /// 文字間はグリフ間にのみ加算し、末尾には加算しません。
    /// </summary>
    public static float MeasureTextWithOutline16Width(RayFont font, string text, float fontSize, float letterSpacingEm = 0f)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        if (UsesMainSpriteFont(font))
            return MeasureSpriteTextWidth(text, fontSize, letterSpacingEm);
        if (NeedsLatinFallback(font, text))
        {
            var runs = SplitDisplayRuns(font, text);
            float total = 0f;
            float runBoundarySpacingPx = letterSpacingEm * fontSize;
            for (int i = 0; i < runs.Count; i++)
            {
                var (runFont, runText) = runs[i];
                total += MeasureTextWithOutline16Width(runFont, runText, fontSize, letterSpacingEm);
                if (i + 1 < runs.Count) total += runBoundarySpacingPx;
            }
            return total;
        }

        var glyphs = ShapeText(font, text, fontSize, letterSpacingEm);
        float letterSpacingPx = letterSpacingEm * fontSize;
        if (glyphs.Length == 0)
            return Raylib.MeasureTextEx(font, text, fontSize, letterSpacingPx).X;

        int upem = _hbUnitsPerEm.TryGetValue(font.Texture.Id, out var upemVal) ? upemVal : font.BaseSize;
        float upemScale = fontSize / upem;
        float width = 0f;
        for (int i = 0; i < glyphs.Length; i++)
        {
            width += glyphs[i].XAdvance * upemScale * GetGlyphHorizontalScale(glyphs[i].Codepoint);
            if (i + 1 < glyphs.Length) width += letterSpacingPx;
        }
        return width;
    }

    /// <summary>trueにするとShapeTextが1文字ごとのXAdvance等をConsoleへ出力する(原因調査用)。</summary>
    public static bool DebugLogShaping = false;

    /// <summary>
    /// HarfBuzzの実シェーピング結果を使って16方向縁取り付きテキストを描画します。
    /// DrawTextWithOutline16のink-trimヒューリスティックと違い、OTF側のGPOS/palt/kernを
    /// そのまま解決するため、HTML(webフォントのCSS palt)描画とほぼ一致する字送りになります。
    /// HarfBuzzフォントが未登録の場合は自動的にDrawTextWithOutline16へフォールバックします。
    /// </summary>
    public static void DrawTextWithOutlineHB(RayFont font, string text, Vector2 position, float fontSize, Color textColor, Color outlineColor, float thickness = 7f, float letterSpacingEm = 0f)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (UsesMainSpriteFont(font))
        {
            DrawSpriteTextWithOutline(text, position, fontSize, textColor, outlineColor, thickness, letterSpacingEm);
            return;
        }
        if (NeedsLatinFallback(font, text))
        {
            DrawTextWithOutline16MixedLatin(font, text, position, fontSize, textColor, outlineColor,
                thickness, 0f, letterSpacingEm);
            return;
        }

        var glyphs = ShapeText(font, text, fontSize, letterSpacingEm);
        if (glyphs.Length == 0)
        {
            // フォールバック(HarfBuzz未登録時)
            DrawTextWithOutline16(font, text, position, fontSize, textColor, outlineColor, thickness, 0f, letterSpacingEm);
            return;
        }

        float scale = fontSize / font.BaseSize;
        float scaledThickness = MathF.Min(thickness * scale, fontSize * 0.12f);
        int upem = _hbUnitsPerEm.TryGetValue(font.Texture.Id, out var upemVal) ? upemVal : font.BaseSize;
        float upemScale = fontSize / upem;
        float letterSpacingPx = letterSpacingEm * fontSize; // 💡 呼び出し側でpx変換

        // 1. 縁取りパス
        float penX = position.X;
        foreach (var g in glyphs)
        {
            Vector2 basePos = new Vector2(penX + g.XOffset * upemScale, position.Y + g.YOffset * upemScale);
            float horizontalScale = GetGlyphHorizontalScale(g.Codepoint);
            foreach (var offset in _outlineOffsets)
            {
                DrawTextCodepointScaledX(font, g.Codepoint, basePos + offset * scaledThickness, fontSize, outlineColor, horizontalScale);
            }
            penX += g.XAdvance * upemScale * horizontalScale + letterSpacingPx;
        }

        // 2. 本体パス
        penX = position.X;
        foreach (var g in glyphs)
        {
            Vector2 basePos = new Vector2(penX + g.XOffset * upemScale, position.Y + g.YOffset * upemScale);
            float horizontalScale = GetGlyphHorizontalScale(g.Codepoint);
            DrawTextCodepointScaledX(font, g.Codepoint, basePos, fontSize, textColor, horizontalScale);
            penX += g.XAdvance * upemScale * horizontalScale + letterSpacingPx;
        }
    }

    // ------------------------------------------------------------------
    // プレイヤー名: 文字種judgeとフォント振り分け描画
    // ------------------------------------------------------------------

    /// <summary>U+3041〜U+309F(ひらがな、小書き文字・繰り返し記号を含む)かどうか</summary>
    private static bool IsHiragana(int cp) => cp >= 0x3041 && cp <= 0x309F;

    /// <summary>半角ASCII印字可能文字(英数字・記号・スペース)かどうか</summary>
    private static bool IsAsciiPrintable(int cp) => cp >= 0x20 && cp <= 0x7E;

    /// <summary>
    /// sourceの中からpredicateに一致するコードポイントだけを抜き出す。
    /// 未知の文字が来ても最低限の表示崩れが起きないよう、半角ASCII基本文字は常に含めておく。
    /// </summary>
    private static int[] FilterCodepoints(int[] source, Func<int, bool> predicate)
    {
        var set = new HashSet<int>();
        foreach (var cp in source)
            if (predicate(cp)) set.Add(cp);
        for (int i = 32; i < 127; i++) set.Add(i);

        var result = new List<int>(set);
        result.Sort();
        return result.ToArray();
    }

    /// <summary>
    /// プレイヤー名の1文字(コードポイント)に対して使用するフォントを判定する。
    ///   ひらがな                → FontNameHiragana (font/JP64.xml + font/JP64.png)
    ///   半角英数字・記号(ASCII)  → FontNameLatin (font/dom-bold-bt.ttf)
    ///   カタカナ・漢字・それ以外 → FontNameOther (font/DF.ttf)
    /// </summary>
    private static RayFont SelectNameFont(int cp)
    {
        if (IsHiragana(cp)) return FontNameHiragana;
        if (IsAsciiPrintable(cp)) return FontNameLatin;
        return FontNameOther;
    }

    /// <summary>同じフォントが連続する区間ごとにtextを分割する(フォント切り替え描画・幅計測の共通処理)</summary>
    private static List<(RayFont font, string text)> SplitNameRuns(string text)
    {
        var runs = new List<(RayFont, string)>();
        int idx = 0;
        while (idx < text.Length)
        {
            int cp = char.ConvertToUtf32(text, idx);
            int len = char.IsSurrogatePair(text, idx) ? 2 : 1;
            RayFont font = SelectNameFont(cp);

            int runStart = idx;
            int runIdx = idx + len;
            while (runIdx < text.Length)
            {
                int cp2 = char.ConvertToUtf32(text, runIdx);
                if (SelectNameFont(cp2).Texture.Id != font.Texture.Id) break;
                runIdx += char.IsSurrogatePair(text, runIdx) ? 2 : 1;
            }

            runs.Add((font, text.Substring(runStart, runIdx - runStart)));
            idx = runIdx;
        }
        return runs;
    }

    /// <summary>ShapeText結果からHarfBuzzシェーピング済みの描画幅(px)を求める。未登録フォントならink-trim幅で代替。</summary>
    private static float MeasureShapedTextWidth(RayFont font, string text, float fontSize, float letterSpacingEm)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        if (UsesMainSpriteFont(font))
            return MeasureSpriteTextWidth(text, fontSize, letterSpacingEm);

        var glyphs = ShapeText(font, text, fontSize, letterSpacingEm);
        float letterSpacingPx = letterSpacingEm * fontSize;

        if (glyphs.Length == 0)
        {
            float scale = fontSize / font.BaseSize;
            float scaledThickness = 7f * scale;
            float w = 0f;
            var enumerator = text.EnumerateRunes();
            while (enumerator.MoveNext())
            {
                int codepoint = enumerator.Current.Value;
                int glyphIndex = Raylib.GetGlyphIndex(font, codepoint);
                float advance = glyphIndex >= 0
                    ? Raylib.GetGlyphInfo(font, codepoint).AdvanceX * scale
                    : font.BaseSize * 0.5f * scale;
                w += advance + letterSpacingPx;
            }
            return w;
        }

        int upem = _hbUnitsPerEm.TryGetValue(font.Texture.Id, out var upemVal) ? upemVal : font.BaseSize;
        float upemScale = fontSize / upem;
        float total = 0f;
        foreach (var g in glyphs) total += g.XAdvance * upemScale + letterSpacingPx;
        return total;
    }

    /// <summary>
    /// プレイヤー名を文字種ごとにフォントを切り替えながら16方向縁取り付きで描画する。
    /// ひらがな→FontNameHiragana、半角英数記号→FontNameLatin、それ以外(カタカナ・漢字等)→FontNameOther。
    /// </summary>
    public static void DrawTextWithOutlineNameMultiFont(string text, Vector2 position, float fontSize, Color textColor, Color outlineColor, float thickness = 7f, float letterSpacingEm = 0f)
    {
        if (string.IsNullOrEmpty(text)) return;

        float penX = position.X;
        foreach (var (font, run) in SplitNameRuns(text))
        {
            DrawTextWithOutline16(font, run, new Vector2(penX, position.Y), fontSize, textColor, outlineColor, thickness, 0f, letterSpacingEm);
            penX += MeasureShapedTextWidth(font, run, fontSize, letterSpacingEm);
        }
    }

    /// <summary>DrawTextWithOutlineNameMultiFontと同じ文字種振り分けで、描画全体の幅(px)を計測する。</summary>
    public static float MeasureNameMultiFontWidth(string text, float fontSize, float letterSpacingEm = 0f)
    {
        if (string.IsNullOrEmpty(text)) return 0f;

        float total = 0f;
        foreach (var (font, run) in SplitNameRuns(text))
            total += MeasureShapedTextWidth(font, run, fontSize, letterSpacingEm);
        return total;
    }

    // ------------------------------------------------------------------
    // コードポイント収集
    // ------------------------------------------------------------------

    /// <summary>
    /// songsRoot配下の全box.defを再帰的に読み、#EXPLANATION: 行に登場する文字だけを
    /// 収集してFontSub用のコードポイント配列を作ります。
    /// ASCII基本文字(半角英数記号)は表示崩れ防止のため常に含めます。
    /// </summary>
    private static int[] CreateExplanationCodepoints(string songsRoot)
    {
        var set = new HashSet<int>();

        // 半角英数・基本記号は最低限つねに含めておく(改行・空文字列等の保険)
        for (int i = 32; i < 127; i++) set.Add(i);

        // FontSub(SubFont.otf)はSettingsPanelのUI全体でも使われるため、
        // SettingsPanelでDrawTextExに渡す文字列をすべてここに列挙する。
        // (CreateMainFontCodepointsのsystemTextsと同期して管理すること)
        string[] subSystemTexts =
        {
            // エラー画面
            "エラーが発生しています。",
            "係員をお呼びください。",
            "取扱説明書の指示に従ってください。",
            "発生",
            // 設定画面ヘッダー・フッター
            "設定",
            "F1 で閉じる",
            // ディスプレイ
            "ディスプレイ",
            "フルスクリーン",
            "ウィンドウサイズ",
            // サウンド
            "サウンド",
            "WASAPI",
            "ASIO",
            "DirectSound",
            "WASAPI 排他モード",
            "低遅延ですが他アプリの音が止まります",
            "ASIOドライバ",
            "見つかりません",
            "サンプルレート",
            "※WASAPI共有モードではデバイス側の形式が優先されます",
            "バッファサイズ",
            "※ASIOはドライバ設定のバッファが優先されます",
            // FPS
            "FPS上限",
            "無制限",
            // コイン
            "コイン",
            "フリープレイ",
            "コインを使わずに遊べます",
            "Coin(s)",
            // キー設定
            "キー設定",
            "左面 (Don)",
            "右面 (Don)",
            "左縁 (Ka)",
            "右縁 (Ka)",
            // 音量
            "音量",
            "マスター",
            "BGM",
            "SE",
            // タイトルテキスト
            "タイトルテキスト",
            "ガイド文字",
            // ネームプレート
            "ネームプレート",
            "プレイヤー名",
            "称号",
            "段位名",
            "金合格（段位）",
            "段位フレーム (3〜5)",
            // 曲検索
            "曲検索",
            "検索キーワード",
            "検索",
            // 曲リスト
            "曲リスト",
            "曲を再読み込み",
            "✔ 再読み込み完了",
            // シーン移動
            "シーン移動",
            "デモへ",
            "タイトルへ",
            "選曲画面へ",
            // VRAM表示
            "VRAM表示 (F8)",
            "概要のみ",
            "テクスチャ詳細",
            "所有者別",
            "種別ごと",
            "完全非表示",
            // Ensoのジャンル表示(Enso.cs GenreDisplayNameMapと同期して管理すること)
            // Ensoではこの文字列を G.FontSub で描画するため、FontSub側のコードポイントにも必要。
            "特集ポップスキッズアニメボーカロイドゲームミュージックバラエティクラシックナムコオリジナル",
        };
        foreach (var s in subSystemTexts) AddCodepoints(set, s);

        // キャッシュ済みならディスクI/Oを一切行わず即返す
        EnsureScanCache(songsRoot);
        if (_scanCacheSub != null) set.UnionWith(_scanCacheSub);

        var result = new List<int>(set);
        result.Sort();
        return result.ToArray();
    }

    /// <summary>
    /// songsRoot配下の*.tja/box.defを1回のディレクトリ走査でまとめてスキャンし、
    /// メインFont用・サブFont(Explanation)用の2つのコードポイント集合をキャッシュする。
    /// 直前と同じsongsRootであればディスクI/Oを丸ごとスキップする。
    /// </summary>
    private static void EnsureScanCache(string songsRoot)
    {
        if (_scanCacheRoot == songsRoot && _scanCacheMain != null && _scanCacheSub != null) return;

        var mainSet = new HashSet<int>();
        var subSet = new HashSet<int>();

        try
        {
            if (System.IO.Directory.Exists(songsRoot))
            {
                // 1回のEnumerateFileSystemEntriesでファイル種別ごとに振り分けて処理し、
                // I/O往復とstring[]アロケーションを削減する。
                foreach (var path in System.IO.Directory.EnumerateFileSystemEntries(
                    songsRoot, "*", System.IO.SearchOption.AllDirectories))
                {
                    if (System.IO.Directory.Exists(path))
                    {
                        // ディレクトリ名(ジャンル/曲フォルダ名等)もタイトル表示に使われる文字として収集
                        AddCodepoints(mainSet, System.IO.Path.GetFileName(path));
                        continue;
                    }

                    string fileName = System.IO.Path.GetFileName(path);

                    if (fileName.Equals("box.def", StringComparison.OrdinalIgnoreCase))
                    {
                        ScanBoxDef(path, mainSet, subSet);
                    }
                    else if (path.EndsWith(".tja", StringComparison.OrdinalIgnoreCase))
                    {
                        AddCodepoints(mainSet, System.IO.Path.GetFileNameWithoutExtension(path));
                        ScanTja(path, mainSet);
                    }
                }
            }
        }
        catch
        {
            // スキャンに失敗した場合はここまでの収集分のみで継続(例外で起動を止めない)
        }

        _scanCacheRoot = songsRoot;
        _scanCacheMain = mainSet;
        _scanCacheSub = subSet;

        // 1000曲規模のスキャンで一時的に大量のstring/List等がアロケートされるため、
        // ここで明示的にGCを走らせて即座に回収する(スキャンはsongsRoot固定なら起動時1回だけ)。
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
    }

    /// <summary>box.def 1ファイルをストリーミング読み込みしてTITLE/EXPLANATIONを収集</summary>
    private static void ScanBoxDef(string path, HashSet<int> mainSet, HashSet<int> subSet)
    {
        try
        {
            // ReadAllLines(全行一括アロケート)ではなくReadLines(1行ずつストリーミング)で
            // 大量曲数でのGC負荷/ワーキングセット膨張を防ぐ
            foreach (var raw in System.IO.File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || !line.StartsWith("#")) continue;
                int colonIdx = line.IndexOf(':');
                if (colonIdx == -1) continue;

                string key = line.Substring(1, colonIdx - 1).Trim().ToUpperInvariant();
                if (key == "TITLE")
                {
                    AddCodepoints(mainSet, line.Substring(colonIdx + 1).Trim());
                }
                else if (key == "EXPLANATION")
                {
                    string val = line.Substring(colonIdx + 1).Trim().Replace("\\n", "\n");
                    AddCodepoints(subSet, val);
                }
            }
        }
        catch { /* このファイルはスキップして続行 */ }
    }

    /// <summary>*.tja 1ファイルをストリーミング読み込みしてTITLE/TITLEJA/SUBTITLEを収集</summary>
    private static void ScanTja(string path, HashSet<int> mainSet)
    {
        try
        {
            foreach (var raw in System.IO.File.ReadLines(path))
            {
                string trimmed = raw.Trim();
                int colonIdx = trimmed.IndexOf(':');
                if (colonIdx == -1) continue;

                string key = trimmed.Substring(0, colonIdx).Trim().ToUpperInvariant();
                if (key != "TITLE" && key != "TITLEJA" && key != "SUBTITLE") continue;

                string val = trimmed.Substring(colonIdx + 1).Trim();
                if (key == "SUBTITLE" && (val.StartsWith("--") || val.StartsWith("++")))
                    val = val.Substring(2);

                AddCodepoints(mainSet, val);
            }
        }
        catch { /* このファイルはスキップして続行 */ }
    }

    /// <summary>
    /// songsRoot配下の全*.tja(TITLE/TITLEJA/SUBTITLE)、box.def(TITLE)、
    /// Data/PlayData.json(Name/Title/Dan)、および画面上の固定システム文言から
    /// 使用文字を収集し、メインFont用のコードポイント配列を作ります。
    ///
    /// ※プレイヤー名は名前入力画面で後から自由入力されるため、PlayData.json保存後は
    ///   G.InitializeFonts(songsRoot) を呼び直して新しい文字を反映してください
    ///   (NamePlate.LoadPlayerData() 経由で自動的に呼ばれます)。
    /// </summary>
    private static int[] CreateMainFontCodepoints(string songsRoot, string playDataJsonPath = "Data/PlayData.json")
    {
        var set = new HashSet<int>();

        // ASCII基本文字
        for (int i = 32; i < 127; i++) set.Add(i);

        // 固定システム文言(ハードコードされた画面文言。新しく追加したらここにも追記すること)
        string[] systemTexts =
        {
            // 基本文字セット(記号・かな・漢字頻出セット)
            "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわをんがぎぐげござじずぜぞだぢづでどばびぶべぼぱぴぷぺぽぁぃぅぇぉっゃゅょゎアイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヲンガギグゲゴザジズゼゾダヂヅデドバビブベボパピプペポァィゥェォッャュョヮヴ",
            "未満以上未実装音符位置調整特殊オプションオートBPM固定",
            "一二三四五六七八九十百千万人外伝段級初中上鬼裏",
            ",.:、。：・？「」【】『』（）〔〕《》〈〉…‥・゛゜ー−−―~〜！？！＠＃＄％＾＆＊（）＿＋＝｛｝｜＜＞？　",
            "@[]:;./,^-pー＾＠「」：；。・、♡★☆゛〇●◎○〇入数",
            "死復活チャレンジ達成失敗クリアランクスコア連打回数秒倍良可不正解合格不合格",
            // 選曲・結果画面
            "このフォルダは空です",
            "/ に TJA やフォルダが見つかりません",
            "不明な曲",
            "もどる",
            "未分類",
            "曲名サブタイトルジャンルフォルダ",
            // Enso(演奏画面)のジャンル表示(Enso.cs GenreDisplayNameMapと同期して管理すること)
            // box.defのGENRE:はTITLE/EXPLANATIONと違いスキャン対象外のため、ここに明記しないと
            // 該当グリフがアトラスに含まれず、文字が表示されない(欠字)ことになる。
            "特集ポップスキッズアニメボーカロイドゲームミュージックバラエティクラシックナムコオリジナル",
            // タイトル画面
            "太鼓をたたいてスタート！",
            "れぐが女と言ったらスタート！",
            // TitleGuideText の現在値(起動時に動的に追加、null の場合は空文字扱い)
            TitleScene.TitleGuideText ?? "",
            // コイン・モード
            "フリープレイ",
            "1人プレイ",
            "2人プレイ",
            "コインをいれてね！",
            "コイン",
            "Coin(s)",
            // 設定画面 (SettingsPanel)
            "設定",
            "ディスプレイ",
            "フルスクリーン",
            "ウィンドウサイズ",
            "サウンド",
            "WASAPI 排他モード",
            "低遅延ですが他アプリの音が止まります",
            "ASIOドライバ",
            "見つかりません",
            "サンプルレート",
            "※WASAPI共有モードではデバイス側の形式が優先されます",
            "バッファサイズ",
            "※ASIOはドライバ設定のバッファが優先されます",
            "FPS上限",
            "無制限",
            "コイン",
            "コインを使わずに遊べます",
            "F1 で閉じる",
            "キー設定",
            "左面 (Don)",
            "右面 (Don)",
            "左縁 (Ka)",
            "右縁 (Ka)",
            "音量",
            "マスター",
            "タイトルテキスト",
            "ガイド文字",
            "ネームプレート",
            "プレイヤー名",
            "称号",
            "段位名",
            "金合格（段位）",
            "段位フレーム (3〜5)",
            "曲検索",
            "検索キーワード",
            "検索",
            "曲リスト",
            "曲を再読み込み",
            "✔ 再読み込み完了",
            "シーン移動",
            "デモへ",
            "タイトルへ",
            "選曲画面へ",
            "VRAM表示 (F8)",
            "WASAPI",
            "ASIO",
            "DirectSound",
            // エラー画面
            "エラーが発生しています。",
            "係員をお呼びください。",
            "取扱説明書の指示に従ってください。",
            "発生",
            // ネームプレートUIに出る可能性のある固定文字列
            "1P",
            "段",
            "☆",
            "★",
            // 記号補完
            "ms%FPS",
            // VRAM表示パネル(DrawVramOverlay / DrawVramDetailPanel)
            "概要のみ",
            "テクスチャ詳細",
            "所有者別",
            "種別ごと",
            "完全非表示",
            "合計参照ユニーク所有者種別",
            "連打で非表示スクロール",
            "ユニークテクスチャ上位全件表示",
            "フォーマット推定参照元",
            "フィールド配列枚数クラス",
        };
        foreach (var s in systemTexts) AddCodepoints(set, s);

        // 実際のディスクI/O(ReadLines等)はEnsureScanCache内でsongsRootごとに1回だけ行い、
        // 2回目以降はキャッシュ済みのHashSetをコピーするだけにする。
        EnsureScanCache(songsRoot);
        var scannedSet = new HashSet<int>(_scanCacheMain ?? new HashSet<int>());

        // Data/PlayData.json — 全文字列フィールドを収集(Name/Title/Dan以外に将来追加されても対応)
        try
        {
            if (!string.IsNullOrEmpty(playDataJsonPath) && System.IO.File.Exists(playDataJsonPath))
            {
                string json = System.IO.File.ReadAllText(playDataJsonPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                CollectAllStringsFromJson(doc.RootElement, scannedSet);
            }
        }
        catch { /* 未作成/壊れている場合は無視して継続 */ }

        // title.cfg — TitleGuideText の保存値(起動時に設定を上書き変更した文字列)
        try
        {
            const string titleCfgPath = "title.cfg";
            if (System.IO.File.Exists(titleCfgPath))
                AddCodepoints(scannedSet, System.IO.File.ReadAllText(titleCfgPath).Trim());
        }
        catch { }

        // スキャンで集めた文字(漢字・かな・記号すべて)をそのまま採用
        foreach (var cp in scannedSet) set.Add(cp);

        var result = new List<int>(set);
        result.Sort();
        return result.ToArray();
    }

    /// <summary>
    /// JsonElement を再帰的にたどり、文字列値をすべて set へ追加します。
    /// PlayData.json のように構造が将来変わっても自動追従できます。
    /// </summary>
    private static void CollectAllStringsFromJson(System.Text.Json.JsonElement el, HashSet<int> set)
    {
        switch (el.ValueKind)
        {
            case System.Text.Json.JsonValueKind.String:
                AddCodepoints(set, el.GetString() ?? "");
                break;
            case System.Text.Json.JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                    CollectAllStringsFromJson(prop.Value, set);
                break;
            case System.Text.Json.JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    CollectAllStringsFromJson(item, set);
                break;
        }
    }

    /// <summary>
    /// Lumen/03.SongLoad/hint.json内のすべての文字列値をUTF-8で収集し、
    /// hint.otfのアトラス用コードポイントを作成します。JSONの配列・入れ子・
    /// 将来追加される任意フィールドも再帰的に走査するため、ヒント件数や構造に制限はありません。
    /// </summary>
    private static int[] CreateHintFontCodepoints(int[] fallbackCodepoints)
    {
        var set = new HashSet<int>();

        // ASCIIと改行は常に含める。UTF-8のJSONはEncoding.UTF8で明示して読む。
        for (int cp = 32; cp < 127; cp++) set.Add(cp);
        set.Add('\n');
        foreach (int cp in fallbackCodepoints) set.Add(cp);

        string hintJsonPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Lumen", "03.SongLoad", "hint.json");
        try
        {
            if (System.IO.File.Exists(hintJsonPath))
            {
                string json = System.IO.File.ReadAllText(hintJsonPath, System.Text.Encoding.UTF8);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                CollectAllStringsFromJson(doc.RootElement, set);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[G.CreateHintFontCodepoints] hint.jsonを読み込めませんでした: {ex.Message}");
        }

        var result = new List<int>(set);
        result.Sort();
        return result.ToArray();
    }

    /// <summary>文字列中の各文字(サロゲートペア考慮)をコードポイントとしてsetへ追加します</summary>
    private static void AddCodepoints(HashSet<int> set, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        int idx = 0;
        while (idx < text.Length)
        {
            int cp = char.ConvertToUtf32(text, idx);
            set.Add(cp);
            idx += char.IsSurrogatePair(text, idx) ? 2 : 1;
        }
    }
}
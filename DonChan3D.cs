using System;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using Raylib_cs;

public static unsafe class DonChan3D
{
    private static Model _model;
    private static Texture2D _texFace;
    private static Texture2D _texFaceHead;
    private static Model _bodyModel;
    private static bool _bodyLoaded = false;
    private static int _bodyId = 0;
    private static Model _headModel;
    private static bool _headLoaded = false;
    private static int _headId = 0;

    public static int BodyId => _bodyId;
    public static int HeadId => _headId;
    private static int _faceId = 0;
    public static int FaceId => _faceId;
    private static Image _faceSheetImage;
    private static bool _faceSheetLoaded = false;
    private static int _faceFrameSize = 0;
    private static int _faceFrameCount = 1;
    private static int _currentFaceFrame = -1;

    private static float _faceFrameManualOffset = 0f;
    private const float FaceFrameManualStep = 0.5f;

    private static int _faceMatIdxCostume = 0;
    private static int _faceMatIdxHead = 0;
    private static float _headFaceScaleFactor = 1.0f;
    private static float _headFaceScaleManualOverride = 0f;
    private static float _cosFaceScaleManualOverride = 1f;

    private static List<int> _recolorMatIdxCostume = new();
    private static List<int> _recolorMatIdxBody = new();
    private static List<int> _recolorMatIdxHead = new();

    private static Dictionary<int, Image> _origImgCostume = new();
    private static Dictionary<int, Image> _origImgBody = new();
    private static Dictionary<int, Image> _origImgHead = new();

    // ==================================================================
    // 💡 メモリ計測用デバッグヘルパー（VRAM使用量の概算をコンソールに出力する）
    //    実際のGPU内部フォーマットや圧縮有無まではRaylib越しに正確には取れないため、
    //    あくまで「だいたいこのくらい」という目安として使うこと。
    // ==================================================================
    private static int GetBytesPerPixel(PixelFormat fmt)
    {
        switch (fmt)
        {
            case PixelFormat.UncompressedGrayscale: return 1;
            case PixelFormat.UncompressedGrayAlpha: return 2;
            case PixelFormat.UncompressedR5G6B5: return 2;
            case PixelFormat.UncompressedR8G8B8: return 3;
            case PixelFormat.UncompressedR5G5B5A1: return 2;
            case PixelFormat.UncompressedR4G4B4A4: return 2;
            case PixelFormat.UncompressedR8G8B8A8: return 4;
            case PixelFormat.UncompressedR32: return 4;
            case PixelFormat.UncompressedR32G32B32: return 12;
            case PixelFormat.UncompressedR32G32B32A32: return 16;
            // 💡 DXT/ETC/ASTC等の圧縮フォーマットはここに来ることは稀（GLBは大抵RGBA8）
            //    圧縮フォーマットの場合は概算が大きくズレるので、fmtの値をコンソールで確認すること。
            default: return 4;
        }
    }

    private static long TextureBytes(Texture2D t)
    {
        if (t.Id == 0) return 0;
        int bpp = GetBytesPerPixel(t.Format);
        long baseSize = (long)t.Width * t.Height * bpp;
        if (t.Mipmaps > 1) baseSize = (long)(baseSize * 1.333); // ミップマップ分の概算加算
        return baseSize;
    }

    private static long ImageBytes(Image img)
    {
        if (img.Data == null) return 0;
        int bpp = GetBytesPerPixel(img.Format);
        return (long)img.Width * img.Height * bpp;
    }

    private static string MB(long bytes) => $"{bytes / 1024f / 1024f:0.0}MB";

    /// <summary>指定モデルの全マテリアル・全テクスチャマップのVRAM使用量を計測してログ出力する。</summary>
    private static long LogModelTextureMemory(Model mdl, string label)
    {
        long total = 0;
        if (mdl.MaterialCount == 0)
        {
            Console.WriteLine($"[mem][{label}] マテリアルなし");
            return 0;
        }

        for (int m = 0; m < mdl.MaterialCount; m++)
        {
            // 💡 MaterialMapIndex は Albedo〜Brdf まで十数種類あるので全部走査する
            int mapCount = Enum.GetValues(typeof(MaterialMapIndex)).Length;
            for (int mapIdx = 0; mapIdx < mapCount; mapIdx++)
            {
                Texture2D tex = mdl.Materials[m].Maps[mapIdx].Texture;
                if (tex.Id == 0) continue;
                long bytes = TextureBytes(tex);
                if (bytes == 0) continue;
                total += bytes;
                Console.WriteLine($"[mem][{label}] mat{m} map={(MaterialMapIndex)mapIdx} {tex.Width}x{tex.Height} fmt={tex.Format} mip={tex.Mipmaps} => {MB(bytes)}");
            }
        }
        Console.WriteLine($"[mem][{label}] --- モデル合計: {MB(total)} (MeshCount={mdl.MeshCount}, MaterialCount={mdl.MaterialCount}) ---");
        return total;
    }

    /// <summary>CPU側にキャッシュしているImage(Dictionary<int, Image>)群のメモリ使用量を計測してログ出力する。</summary>
    private static long LogImageCacheMemory(Dictionary<int, Image> cache, string label)
    {
        long total = 0;
        foreach (var kv in cache)
        {
            long bytes = ImageBytes(kv.Value);
            total += bytes;
            Console.WriteLine($"[mem][{label}] idx={kv.Key} {kv.Value.Width}x{kv.Value.Height} fmt={kv.Value.Format} => {MB(bytes)}");
        }
        Console.WriteLine($"[mem][{label}] --- Imageキャッシュ合計: {MB(total)} (件数={cache.Count}) ---");
        return total;
    }

    /// <summary>
    /// DonChan3Dが現在保持している主要リソース（モデルテクスチャ・Imageキャッシュ・顔テクスチャ・
    /// レンダーバッファ・アニメーション数）の概算メモリ使用量を一括でコンソール出力する。
    /// F7などデバッグ用キーに割り当てて随時呼べるようにしておくと便利。
    /// </summary>
    public static void LogMemoryReport()
    {
        Console.WriteLine("========== [DonChan3D] メモリレポート開始 ==========");
        long total = 0;

        if (_loaded) total += LogModelTextureMemory(_model, "costume(_model)");
        if (_bodyLoaded) total += LogModelTextureMemory(_bodyModel, "body(_bodyModel)");
        if (_headLoaded) total += LogModelTextureMemory(_headModel, "head(_headModel)");

        total += LogImageCacheMemory(_origImgCostume, "cache:costume");
        total += LogImageCacheMemory(_origImgBody, "cache:body");
        total += LogImageCacheMemory(_origImgHead, "cache:head");

        if (_faceSheetLoaded)
        {
            long fsBytes = ImageBytes(_faceSheetImage);
            total += fsBytes;
            Console.WriteLine($"[mem][faceSheetImage(CPU)] {_faceSheetImage.Width}x{_faceSheetImage.Height} fmt={_faceSheetImage.Format} => {MB(fsBytes)}");
        }

        long texFaceBytes = TextureBytes(_texFace);
        long texFaceHeadBytes = TextureBytes(_texFaceHead);
        total += texFaceBytes + texFaceHeadBytes;
        Console.WriteLine($"[mem][_texFace] {_texFace.Width}x{_texFace.Height} => {MB(texFaceBytes)}");
        Console.WriteLine($"[mem][_texFaceHead] {_texFaceHead.Width}x{_texFaceHead.Height} => {MB(texFaceHeadBytes)}");

        if (_renderBuffer.Texture.Id != 0)
        {
            // 💡 RenderTextureはcolor+depthバッファを持つ。depthは概算4byte/pixelとして加算
            long colorBytes = TextureBytes(_renderBuffer.Texture);
            long depthBytes = (long)_renderBuffer.Texture.Width * _renderBuffer.Texture.Height * 4;
            total += colorBytes + depthBytes;
            Console.WriteLine($"[mem][_renderBuffer] {_renderBuffer.Texture.Width}x{_renderBuffer.Texture.Height} color={MB(colorBytes)} depth概算={MB(depthBytes)}");
        }

        Console.WriteLine($"[mem][_animationsPtr] アニメーション数={_animationCount} (※ボーン行列データはここではカウント対象外)");

        Console.WriteLine($"========== [DonChan3D] 合計概算: {MB(total)} ==========");
    }

    private static (int faceIdx, List<int> recolorIdx, List<int> additiveIdx, List<int> forceOpaqueIdx) ParseGlbMaterialInfo(string path, Model mdl)
    {
        var recolorIdx = new List<int>();
        var additiveIdx = new List<int>();
        var forceOpaqueIdx = new List<int>();
        int faceIdx = -1;

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 20) throw new Exception("file too short for glb");

            uint magic = BitConverter.ToUInt32(bytes, 0);
            if (magic != 0x46546C67u) throw new Exception("not a glb file");

            uint jsonChunkLen = BitConverter.ToUInt32(bytes, 12);
            uint jsonChunkType = BitConverter.ToUInt32(bytes, 16);
            if (jsonChunkType != 0x4E4F534Au) throw new Exception("first chunk is not JSON");

            string json = System.Text.Encoding.UTF8.GetString(bytes, 20, (int)jsonChunkLen);
            using JsonDocument doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("materials", out JsonElement materials))
            {
                int glbIdx = 0;
                foreach (JsonElement mat in materials.EnumerateArray())
                {
                    int raylibIdx = glbIdx + 1;
                    if (raylibIdx < mdl.MaterialCount)
                    {
                        if (mat.TryGetProperty("extras", out JsonElement extras) &&
                            extras.TryGetProperty("shaderType", out JsonElement shaderType))
                        {
                            string shader = shaderType.GetString() ?? "";
                            if (shader == "taikoEffectChangeColors") recolorIdx.Add(raylibIdx);
                            else if (shader == "taikoEffectFace" && faceIdx == -1) faceIdx = raylibIdx;
                        }

                        // YataiDON (parse_glb_material_indices) と同じく、マテリアル名から
                        // 加算合成用（_aa_add）と強制不透明用（_color_s_cus_、ただし_a_abを除く）を検出する。
                        if (mat.TryGetProperty("name", out JsonElement nameEl) && nameEl.ValueKind == JsonValueKind.String)
                        {
                            string nameLower = (nameEl.GetString() ?? "").ToLowerInvariant();
                            if (nameLower.Contains("_aa_add"))
                                additiveIdx.Add(raylibIdx);
                            else if (nameLower.Contains("_color_s_cus_") && !nameLower.Contains("_a_ab"))
                                forceOpaqueIdx.Add(raylibIdx);
                        }
                    }
                    glbIdx++;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DonChan3D] glTF extras parse failed ({ex.Message})");
        }

        if (faceIdx == -1)
        {
            faceIdx = DetectFaceMaterialIndex(mdl);
            Console.WriteLine($"[DonChan3D] face material fallback index={faceIdx}");
        }
        else
        {
            Console.WriteLine($"[DonChan3D] face material (glTF extras) index={faceIdx}, recolor materials=[{string.Join(",", recolorIdx)}]");
        }

        // YataiDON同様、顔マテリアルは加算合成対象から除外する。
        additiveIdx.RemoveAll(idx => idx == faceIdx);

        if (additiveIdx.Count > 0 || forceOpaqueIdx.Count > 0)
            Console.WriteLine($"[DonChan3D] additive materials=[{string.Join(",", additiveIdx)}], force-opaque materials=[{string.Join(",", forceOpaqueIdx)}]");

        return (faceIdx, recolorIdx, additiveIdx, forceOpaqueIdx);
    }

    private static unsafe void ApplyAdditiveAndForceOpaque(Model mdl, List<int> additiveIdx, List<int> forceOpaqueIdx)
    {
        // additive: 頂点/マテリアルの拡散色を白に固定し、加算合成先のテクスチャ色をそのまま出す。
        foreach (int idx in additiveIdx)
        {
            if (idx < 0 || idx >= mdl.MaterialCount) continue;
            var map = mdl.Materials[idx].Maps[(int)MaterialMapIndex.Albedo];
            map.Color = Color.White;
            mdl.Materials[idx].Maps[(int)MaterialMapIndex.Albedo] = map;
        }

        // force_opaque: テクスチャのアルファチャンネルを255に強制し、半透明抜けを防ぐ。
        foreach (int idx in forceOpaqueIdx)
        {
            if (idx < 0 || idx >= mdl.MaterialCount) continue;
            var map = mdl.Materials[idx].Maps[(int)MaterialMapIndex.Albedo];
            if (map.Texture.Id == 0) continue;

            Image img = Raylib.LoadImageFromTexture(map.Texture);
            Raylib.ImageFormat(ref img, PixelFormat.UncompressedR8G8B8A8);
            byte* px = (byte*)img.Data;
            int pixelCount = img.Width * img.Height;
            for (int p = 0; p < pixelCount; p++) px[p * 4 + 3] = 255;

            Raylib.UnloadTexture(map.Texture);
            map.Texture = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
            VramProbe.Track("DonChan3D.ApplyAdditiveAndForceOpaque");
            mdl.Materials[idx].Maps[(int)MaterialMapIndex.Albedo] = map;
        }
    }

    private static int DetectFaceMaterialIndex(Model mdl)
    {
        int best = 0;
        long bestArea = long.MaxValue;
        bool found = false;

        for (int i = 0; i < mdl.MaterialCount; i++)
        {
            Texture2D tex = mdl.Materials[i].Maps[(int)MaterialMapIndex.Albedo].Texture;
            if (tex.Id == 0 || (tex.Width <= 1 && tex.Height <= 1)) continue;
            long area = (long)tex.Width * tex.Height;
            if (area < bestArea)
            {
                bestArea = area;
                best = i;
                found = true;
            }
        }

        Console.WriteLine($"[DonChan3D] face material detected: index={best} (texture area={(found ? bestArea.ToString() : "n/a")})");
        return best;
    }

    private static readonly Color[] _sharedColorPalette = new Color[]
    {
        new Color((byte)248,(byte)72,(byte)40,(byte)255),  new Color((byte)104,(byte)192,(byte)192,(byte)255), new Color((byte)220,(byte)21,(byte)0,(byte)255),   new Color((byte)248,(byte)240,(byte)224,(byte)255),
        new Color((byte)0,(byte)150,(byte)135,(byte)255),  new Color((byte)0,(byte)191,(byte)135,(byte)255),   new Color((byte)0,(byte)255,(byte)154,(byte)255),  new Color((byte)102,(byte)255,(byte)194,(byte)255),
        new Color((byte)255,(byte)255,(byte)255,(byte)255),
        new Color((byte)105,(byte)0,(byte)0,(byte)255),    new Color((byte)255,(byte)0,(byte)0,(byte)255),     new Color((byte)255,(byte)102,(byte)102,(byte)255), new Color((byte)255,(byte)179,(byte)179,(byte)255),
        new Color((byte)0,(byte)188,(byte)194,(byte)255),  new Color((byte)0,(byte)247,(byte)255,(byte)255),   new Color((byte)102,(byte)250,(byte)255,(byte)255), new Color((byte)179,(byte)253,(byte)255,(byte)255),
        new Color((byte)228,(byte)228,(byte)228,(byte)255),
        new Color((byte)153,(byte)56,(byte)0,(byte)255),   new Color((byte)255,(byte)94,(byte)0,(byte)255),    new Color((byte)255,(byte)158,(byte)120,(byte)255), new Color((byte)255,(byte)207,(byte)179,(byte)255),
        new Color((byte)0,(byte)81,(byte)153,(byte)255),   new Color((byte)0,(byte)136,(byte)255,(byte)255),   new Color((byte)102,(byte)184,(byte)255,(byte)255), new Color((byte)179,(byte)219,(byte)255,(byte)255),
        new Color((byte)185,(byte)185,(byte)185,(byte)255),
        new Color((byte)179,(byte)119,(byte)0,(byte)255),  new Color((byte)255,(byte)170,(byte)0,(byte)255),   new Color((byte)255,(byte)204,(byte)102,(byte)255), new Color((byte)255,(byte)226,(byte)179,(byte)255),
        new Color((byte)0,(byte)12,(byte)128,(byte)255),   new Color((byte)0,(byte)25,(byte)255,(byte)255),    new Color((byte)102,(byte)117,(byte)255,(byte)255), new Color((byte)179,(byte)186,(byte)255,(byte)255),
        new Color((byte)133,(byte)133,(byte)133,(byte)255),

        new Color((byte)179,(byte)155,(byte)0,(byte)255),  new Color((byte)255,(byte)221,(byte)0,(byte)255),   new Color((byte)255,(byte)255,(byte)0,(byte)255),   new Color((byte)255,(byte)255,(byte)113,(byte)255),
        new Color((byte)43,(byte)0,(byte)128,(byte)255),   new Color((byte)85,(byte)0,(byte)255,(byte)255),    new Color((byte)153,(byte)102,(byte)255,(byte)255), new Color((byte)204,(byte)179,(byte)255,(byte)255),
        new Color((byte)80,(byte)80,(byte)80,(byte)255),

        new Color((byte)56,(byte)161,(byte)0,(byte)255),   new Color((byte)120,(byte)201,(byte)0,(byte)255),   new Color((byte)179,(byte)255,(byte)0,(byte)255),   new Color((byte)220,(byte)255,(byte)138,(byte)255),
        new Color((byte)97,(byte)0,(byte)128,(byte)255),   new Color((byte)196,(byte)0,(byte)255,(byte)255),   new Color((byte)220,(byte)102,(byte)255,(byte)255), new Color((byte)237,(byte)179,(byte)255,(byte)255),
        new Color((byte)35,(byte)35,(byte)35,(byte)255),

        new Color((byte)0,(byte)102,(byte)0,(byte)255),    new Color((byte)0,(byte)184,(byte)0,(byte)255),     new Color((byte)0,(byte)255,(byte)0,(byte)255),     new Color((byte)138,(byte)255,(byte)158,(byte)255),
        new Color((byte)153,(byte)0,(byte)89,(byte)255),   new Color((byte)255,(byte)0,(byte)149,(byte)255),   new Color((byte)255,(byte)102,(byte)191,(byte)255), new Color((byte)255,(byte)179,(byte)223,(byte)255),
        new Color((byte)0,(byte)0,(byte)0,(byte)255),
    };
    public const int SharedColorPaletteCols = 9;
    public const int SharedColorPaletteRows = 7;
    public static Color[] SharedColorPalette => _sharedColorPalette;

    private static int _bodyColorIndex = 0;
    private static int _faceColorIndex = 8;
    private static int _rimColorIndex = 8;
    public static Color BodyColor => _sharedColorPalette[_bodyColorIndex];
    public static Color FaceColor => _sharedColorPalette[_faceColorIndex];
    public static Color RimColor => _sharedColorPalette[_rimColorIndex];

    public enum DrawMode { All, CosOnly, BodyOnly, HeadOnly, BodyHeadOnly }
    public static DrawMode CurrentDrawMode = DrawMode.All;

    private static ModelAnimation* _animationsPtr = null;
    private static int _animationCount = 0;

    private static bool _loaded = false;
    private static int _costumeId = 0;

    private static int _idleAnimIndex = -1;
    private static int _currentAnimIndex = -1;
    private static int _currentAnimFrame = 0;
    // 💡 アニメーション再生FPS。デフォルト120だが外部(Title画面のエントリー演出など)から
    //    PlaybackFpsを書き換えることでアニメーションごとに個別の速度を指定できる。
    //    設定パネルの「パフォーマンス」タブ(SettingsPanel.DonChanFps)からもここへ直接反映される。
    public static float PlaybackFps = 120f;

    // 💡 設定パネル「パフォーマンス」タブの画質設定。Lowだと内部レンダーバッファを半解像度で
    //    確保し、DrawTexturePro側の拡大表示はそのままに3D描画コストだけを下げる。
    //    エクスポート(画像書き出し)時は常にHigh相当の解像度を強制する(DrawSimple()の引数参照)。
    public enum QualityLevel { Low, High }
    public static QualityLevel RenderQuality = QualityLevel.High;
    private static double _animFrameAccumulator = 0.0;
    public static bool LoopAnimation = true;
    public static bool IsCurrentAnimFinished { get; private set; } = false;
    private static Camera3D _camera3D;

    public static float CamPosX;
    public static float CamPosY;
    public static float CamPosZ;
    public static float TarPosY;
    public static float CamFovY;

    public static float ModelRotX = 0f;
    public static float ModelRotY = 17.0f;
    public static float ModelRotZ = 0f;

    private static RenderTexture2D _renderBuffer;
    // 仮想スクリーンのBeginTextureModeの中からDrawSimpleが呼ばれるときtrueにセットする
    public static bool _insideVirtualFrame = false;

    /// <summary>
    /// 3Dモデルを描き込むレンダーバッファ(_renderBuffer)を指定サイズで確保する。
    /// 以前は常に820x599固定で描画してからDrawTexturePro側で拡大していたため、
    /// プレビュー領域がそれより大きい場合にテクスチャが引き伸ばされて解像度が
    /// 低く見える問題があった。描画先(dest)のピクセルサイズに合わせて
    /// レンダーバッファ自体を確保し直すことで、等倍描画に近づけて解像度を保つ。
    /// エクスポート時は820x599を明示的に渡しているため、書き出しサイズはこれまで通り。
    /// サイズが変わらない場合は何もしない（毎フレーム再確保しない）。
    /// </summary>
    private static void EnsureRenderBufferSize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (_renderBuffer.Id != 0 && _renderBuffer.Texture.Width == width && _renderBuffer.Texture.Height == height)
            return;

        if (_renderBuffer.Id != 0) Raylib.UnloadRenderTexture(_renderBuffer);

        _renderBuffer = Raylib.LoadRenderTexture(width, height);
        VramProbe.Track("DonChan3D.renderBuffer (resize)");
        // 💡 ここでラップモードを明示していないとデフォルトのREPEATのままになり、
        //    頭がフレーム上端で途切れた時にバイリニア補間がテクスチャ下端(v=1)を
        //    巻き込んでしまい、「頭の残骸が下に出る」ように見える。Clampで防止する。
        Raylib.SetTextureWrap(_renderBuffer.Texture, TextureWrap.Clamp);
        Raylib.SetTextureFilter(_renderBuffer.Texture, TextureFilter.Bilinear);
    }

    private static Shader _outlinePassShader;
    private static bool _outlinePassShaderLoaded = false;
    private static int _outlinePassSizeLoc;
    private static int _outlinePassThicknessLoc;
    private static Shader _outlineShader;
    private static bool _outlineShaderLoaded = false;
    private static int _outlineThicknessLoc;
    private static Material _outlineMaterial;
    private static bool _outlineMaterialReady = false;
    private const int RL_CULL_FACE_FRONT = 0;
    private const int RL_CULL_FACE_BACK = 1;

    private const int GL_SRC_ALPHA = 0x0302;
    private const int GL_ONE_MINUS_SRC_ALPHA = 0x0303;
    private const int GL_ONE = 1;
    private const int GL_FUNC_ADD = 0x8006;
    private static bool _customBlendConfigured = false;

    private static bool _isExporting = false;
    private static bool _isBatchMode = false;
    private static int _exportFrameCounter = 0;
    private static int _exportTotalFrames = 0;

    public static bool IsExporting => _isExporting;
    public static int CostumeId => _costumeId;
    public static string ExportProgress => $"{_exportFrameCounter} / {_exportTotalFrames}";

    public static string CurrentAnimName
    {
        get
        {
            if (_loaded && _currentAnimIndex >= 0 && _currentAnimIndex < _animationCount && _animationsPtr != null)
            {
                return _animationsPtr[_currentAnimIndex].NameToString();
            }
            return "None";
        }
    }

    public static bool IsLoaded => _loaded;

    /// <summary>
    /// 共有3Dモデルへ適用する再生状態。モデル/テクスチャ/シェーダーは共有し、
    /// アニメーション選択・フレーム・ループ状態だけをプレイヤーごとに保持する。
    /// </summary>
    public sealed class PlaybackState
    {
        internal int AnimationIndex = -1;
        internal int AnimationFrame;
        internal double FrameAccumulator;
        public bool LoopAnimation = true;
        public bool IsFinished { get; internal set; }
        public string CurrentAnimationName =>
            _loaded && AnimationIndex >= 0 && AnimationIndex < _animationCount && _animationsPtr != null
                ? _animationsPtr[AnimationIndex].NameToString()
                : "None";
    }

    public static PlaybackState CreatePlaybackState()
    {
        return new PlaybackState
        {
            AnimationIndex = _animationCount > 0 ? _idleAnimIndex : -1,
            AnimationFrame = 0,
            FrameAccumulator = 0.0,
            LoopAnimation = true,
            IsFinished = false,
        };
    }

    public static bool SetAnimationByName(PlaybackState state, string name)
    {
        if (state == null || !_loaded || _animationCount == 0 || _isExporting || string.IsNullOrWhiteSpace(name)) return false;
        string needle = name.ToLowerInvariant();
        for (int i = 0; i < _animationCount; i++)
        {
            if (GetAnimationNameById(i).ToLowerInvariant().Contains(needle))
            {
                state.AnimationIndex = i;
                state.AnimationFrame = 0;
                state.FrameAccumulator = 0.0;
                state.IsFinished = false;
                return true;
            }
        }
        return false;
    }

    /// <summary>共有モデルへ直接適用する前の、プレイヤー専用アニメーション時刻を更新する。</summary>
    public static void UpdatePlayback(PlaybackState state)
    {
        if (state == null || !_loaded || _isExporting || _animationsPtr == null || _animationCount == 0) return;
        if (state.AnimationIndex < 0 || state.AnimationIndex >= _animationCount)
        {
            state.AnimationIndex = _idleAnimIndex;
            state.AnimationFrame = 0;
            state.FrameAccumulator = 0.0;
        }

        int total = _animationsPtr[state.AnimationIndex].KeyFrameCount;
        if (total > 1)
        {
            float dtClamped = Math.Min(Raylib.GetFrameTime(), 0.1f);
            state.FrameAccumulator += dtClamped * PlaybackFps;
            int steps = (int)state.FrameAccumulator;
            if (steps > 0)
            {
                state.FrameAccumulator -= steps;
                if (state.LoopAnimation)
                {
                    int loopLen = Math.Max(1, total - 1);
                    state.AnimationFrame = (state.AnimationFrame + steps) % loopLen;
                }
                else
                {
                    state.AnimationFrame = Math.Min(state.AnimationFrame + steps, total - 1);
                }
            }
        }
        state.IsFinished = !state.LoopAnimation && total > 0 && state.AnimationFrame >= total - 1;
    }

    /// <summary>指定された再生状態のポーズを共有3Dモデルへ適用する。描画直前に呼ぶ。</summary>
    private static void ApplyPlaybackState(PlaybackState state)
    {
        if (state == null || !_loaded || _animationsPtr == null || state.AnimationIndex < 0 || state.AnimationIndex >= _animationCount) return;

        int savedIndex = _currentAnimIndex;
        int savedFrame = _currentAnimFrame;
        _currentAnimIndex = state.AnimationIndex;
        _currentAnimFrame = state.AnimationFrame;

        bool needCos = CurrentDrawMode == DrawMode.CosOnly || CurrentDrawMode == DrawMode.All;
        bool needBody = CurrentDrawMode == DrawMode.BodyOnly || CurrentDrawMode == DrawMode.BodyHeadOnly || CurrentDrawMode == DrawMode.All;
        bool needHead = CurrentDrawMode == DrawMode.HeadOnly || CurrentDrawMode == DrawMode.BodyHeadOnly || CurrentDrawMode == DrawMode.All;

        if (needCos) Raylib.UpdateModelAnimation(_model, _animationsPtr[state.AnimationIndex], state.AnimationFrame);
        if (needBody && _bodyLoaded) Raylib.UpdateModelAnimation(_bodyModel, _animationsPtr[state.AnimationIndex], state.AnimationFrame);
        if (needHead && _headLoaded) Raylib.UpdateModelAnimation(_headModel, _animationsPtr[state.AnimationIndex], state.AnimationFrame);
        if (needCos) ApplyFaceTextureToModel();
        if (needHead) ApplyFaceTextureToHead();

        _currentAnimIndex = savedIndex;
        _currentAnimFrame = savedFrame;
    }

    /// <summary>共有モデルを1Pのグローバル再生状態へ復元する。2P専用描画の直後に必ず呼ぶ。</summary>
    private static void RestoreGlobalPlaybackPose()
    {
        if (!_loaded || _animationsPtr == null || _currentAnimIndex < 0 || _currentAnimIndex >= _animationCount) return;

        bool needCos = CurrentDrawMode == DrawMode.CosOnly || CurrentDrawMode == DrawMode.All;
        bool needBody = CurrentDrawMode == DrawMode.BodyOnly || CurrentDrawMode == DrawMode.BodyHeadOnly || CurrentDrawMode == DrawMode.All;
        bool needHead = CurrentDrawMode == DrawMode.HeadOnly || CurrentDrawMode == DrawMode.BodyHeadOnly || CurrentDrawMode == DrawMode.All;
        if (needCos) Raylib.UpdateModelAnimation(_model, _animationsPtr[_currentAnimIndex], _currentAnimFrame);
        if (needBody && _bodyLoaded) Raylib.UpdateModelAnimation(_bodyModel, _animationsPtr[_currentAnimIndex], _currentAnimFrame);
        if (needHead && _headLoaded) Raylib.UpdateModelAnimation(_headModel, _animationsPtr[_currentAnimIndex], _currentAnimFrame);
        if (needCos) ApplyFaceTextureToModel();
        if (needHead) ApplyFaceTextureToHead();
    }

    /// <summary>プレイヤー専用の再生状態を適用して共有モデルを描画する。描画後は必ず1Pポーズへ復元する。</summary>
    public static void DrawSimple(Rectangle dest, PlaybackState state)
    {
        if (!_loaded) return;
        ApplyPlaybackState(state);
        try
        {
            DrawSimple(dest, forceHighQuality: false);
        }
        finally
        {
            // 共有モデルのボーン行列は実体なので、インデックスだけでなく1Pポーズそのものを復元する。
            RestoreGlobalPlaybackPose();
        }
    }

    public static void Init()
    {
        if (_loaded) return;

        var cfg = ReadConfigFull();
        _costumeId = cfg.Dons;
        _bodyId = cfg.BodyId;
        _headId = cfg.HeadId;
        _faceColorIndex = Math.Clamp(cfg.FaceColorIndex, 0, _sharedColorPalette.Length - 1);
        _bodyColorIndex = Math.Clamp(cfg.BodyColorIndex, 0, _sharedColorPalette.Length - 1);
        _rimColorIndex = Math.Clamp(cfg.RimColorIndex, 0, _sharedColorPalette.Length - 1);
        if (Enum.TryParse<DrawMode>(cfg.DrawMode, out var savedMode)) CurrentDrawMode = savedMode;

        LoadAnimationsOnce();
        LoadModelOnly();
        LoadBodyModel(_bodyId);
        LoadHeadModel(_headId);

        CamPosX = 0.0f; CamPosY = 0.2f; CamPosZ = 10.0f; TarPosY = 0.15f; CamFovY = 0.5f;

        _camera3D = new Camera3D
        {
            Position = new Vector3(CamPosX, CamPosY, CamPosZ),
            Target = new Vector3(0f, TarPosY, 0f),
            Up = new Vector3(0f, 1f, 0f),
            FovY = CamFovY,
            Projection = CameraProjection.Orthographic
        };

        EnsureRenderBufferSize(820, 599);

        if (!_customBlendConfigured)
        {
            Rlgl.SetBlendFactorsSeparate(
                GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA,
                GL_ONE, GL_ONE_MINUS_SRC_ALPHA,
                GL_FUNC_ADD, GL_FUNC_ADD);
            _customBlendConfigured = true;
        }

        // YataiDON (shader/outline.vs) と同じ、ローカル空間で頂点を法線方向に
        // 単純に膨らませてからmvpを掛けるだけの手法。matNormal等の複雑な変換は行わない。
        // これはメッシュ自身のアニメーション変形（Squash&Stretch含む）にそのまま追従するため、
        // 不均一スケールでも縁が破綻したり、太さがじわじわ変動したりしない。
        // 足元などメッシュが結合している箇所は頂点法線の長さが完全に1に
        // 正規化されていないことがあり、そのままだと押し出し量＝縁の太さが
        // 場所によってブレる。normalizeしてから押し出すことで太さを均一にする。
        const string outlineVs =
            "#version 330\n" +
            "in vec3 vertexPosition;\n" +
            "in vec3 vertexNormal;\n" +
            "uniform mat4 mvp;\n" +
            "uniform float outlineThickness;\n" +
            "void main() {\n" +
            "    vec3 n = normalize(vertexNormal);\n" +
            "    vec3 extruded = vertexPosition + n * outlineThickness;\n" +
            "    gl_Position = mvp * vec4(extruded, 1.0);\n" +
            "}\n";
        const string outlineFs =
            "#version 330\n" +
            "out vec4 finalColor;\n" +
            "void main() { finalColor = vec4(0.05, 0.05, 0.05, 1.0); }\n";

        _outlineShader = Raylib.LoadShaderFromMemory(outlineVs, outlineFs);
        _outlineShaderLoaded = _outlineShader.Id != 0;
        if (_outlineShaderLoaded)
        {
            _outlineThicknessLoc = Raylib.GetShaderLocation(_outlineShader, "outlineThickness");
            ApplyOutlineThickness();

            _outlineMaterial = Raylib.LoadMaterialDefault();
            _outlineMaterial.Shader = _outlineShader;
            _outlineMaterialReady = true;
        }

        // 従来の8点サンプリングから、均等な16方向（円形状）のアルファ値サンプリングに切り替え、
        // 斜め方向のジャギーやカクつきを完全に打ち消し、滑らかで自然な縁取りにします。
        const string outlinePassFs =
            "#version 330\n" +
            "in vec2 fragTexCoord;\n" +
            "out vec4 fragColor;\n" +
            "uniform sampler2D texture0;\n" +
            "uniform vec2 texSize;\n" +
            "uniform float outlineThickness;\n" +
            "void main() {\n" +
            "    vec2 uv = fragTexCoord;\n" +
            "    vec2 texel = 1.0 / texSize;\n" +
            "    vec4 center = texture(texture0, uv);\n" +
            "    if (center.a > 0.001) { fragColor = vec4(center.rgb, 1.0); return; }\n" +
            "    float accum = 0.0;\n" +
            "    // 16方向（22.5度刻み）のサンプリングオフセット（三角関数で事前に計算された円形配置）\n" +
            "    vec2 offsets[16] = vec2[](\n" +
            "        vec2( 1.0000,  0.0000), vec2( 0.9239,  0.3827), vec2( 0.7071,  0.7071), vec2( 0.3827,  0.9239),\n" +
            "        vec2( 0.0000,  1.0000), vec2(-0.3827,  0.9239), vec2(-0.7071,  0.7071), vec2(-0.9239,  0.3827),\n" +
            "        vec2(-1.0000,  0.0000), vec2(-0.9239, -0.3827), vec2(-0.7071, -0.7071), vec2(-0.3827, -0.9239),\n" +
            "        vec2( 0.0000, -1.0000), vec2( 0.3827, -0.9239), vec2( 0.7071, -0.7071), vec2( 0.9239, -0.3827)\n" +
            "    );\n" +
            "    for (int i = 0; i < 16; ++i) {\n" +
            "        vec2 sampleUv = uv + offsets[i] * texel * outlineThickness;\n" +
            "        accum += texture(texture0, sampleUv).a;\n" +
            "    }\n" +
            "    if (accum <= 0.0) { discard; }\n" +
            "    // 輪郭に少しでもかかっていれば不透明にし、じわじわ透明になるグラデーションをなくす\n" +
            "    fragColor = vec4(0.05, 0.05, 0.05, 1.0);\n" +
            "}\n";

        _outlinePassShader = Raylib.LoadShaderFromMemory(null, outlinePassFs);
        _outlinePassShaderLoaded = _outlinePassShader.Id != 0;
        if (_outlinePassShaderLoaded)
        {
            _outlinePassSizeLoc = Raylib.GetShaderLocation(_outlinePassShader, "texSize");
            _outlinePassThicknessLoc = Raylib.GetShaderLocation(_outlinePassShader, "outlineThickness");
            float texW = 820f, texH = 599f;
            float* ts = stackalloc float[2] { texW, texH };
            Raylib.SetShaderValue(_outlinePassShader, _outlinePassSizeLoc, ts, ShaderUniformDataType.Vec2);

            // 🖊️ 縁取り変更⑤（今回）: ポストプロセス方式（_outlinePassShader＝外側の縁取り）の太さ。
            //    「もう少し太く」という要望で前回3.0f→5.0fにしたが、実際には太すぎたとのことなので、
            //    今まで(3.0f)と前回(5.0f)を踏まえて、そのだいたい半分程度の2.5fまで下げる。
            //    ここを小さくするほど外側の輪郭が細く、大きくするほど太くなる。
            float passThickness = 3.5f;
            Raylib.SetShaderValue(_outlinePassShader, _outlinePassThicknessLoc, &passThickness, ShaderUniformDataType.Float);
        }
    }

    // 🖊️ 縁取り変更⑥（今回）: メッシュ押し出し方式（_outlineShader）の太さ。
    //    前回0.0035f→0.006fにしたが、こちらは単位が「ローカル空間の押し出し量」でスクリーン座標の
    //    passThickness（texel単位）とは尺度が違うため、前回の変更は体感的にはあまり太くなっていなかった。
    //    上の外側の縁取り（passThickness=2.5f）と見た目の太さが揃うように、こちらは前回よりもう一段
    //    太くして0.01fにする。単位系が違うため完全な数値一致はできないが、見た目のバランスを優先した値。
    //    実際に描画して細い/太いと感じたら、この値とpassThicknessを両方見ながら微調整すること。
    private static float _outlineThickness = 0.0018f;
    public static float OutlineThickness
    {
        get => _outlineThickness;
        set { _outlineThickness = value; ApplyOutlineThickness(); }
    }

    // 🖊️ 縁取り変更⑦（今回）: 上のOutlineThicknessと同じ意味で、足専用に用意されている値。
    //    以前はこの変数がどこの描画処理にも参照されておらず（保持のみ）実質未使用だったため、
    //    足の縁取りが常に体・顔と同じ太さで固定されてしまっていた。
    //    DrawModelMeshesOutline呼び出し時にこの値を一時的にシェーダーへ適用することで解消する。
    private static float _bodyOutlineThickness = 0.005f; // 足専用の初期値
    public static float BodyOutlineThickness
    {
        get => _bodyOutlineThickness;
        set { _bodyOutlineThickness = value; }
    }

    private static void ApplyOutlineThickness()
    {
        SetOutlineShaderThickness(_outlineThickness);
    }

    // 🖊️ 縁取り変更⑨（今回）: 指定した太さの値を一時的にシェーダーへ設定するための共通ヘルパー。
    //    足専用の太さ(_bodyOutlineThickness)を使う描画の前後で呼び出すことで、
    //    グローバルな_outlineThicknessを壊さずに一時的な太さ切り替えができる。
    private static void SetOutlineShaderThickness(float thickness)
    {
        if (!_outlineShaderLoaded) return;
        // YataiDON (chara_3d.cpp) と同じくローカル空間での押し出しなので、
        // CamFovYによる補正は不要（クリップ空間押し出し方式の名残だった）。
        // 生の値をそのままシェーダーに渡す。
        Raylib.SetShaderValue(_outlineShader, _outlineThicknessLoc, &thickness, ShaderUniformDataType.Float);
    }

    private static void LoadAnimationsOnce()
    {
        string animPath = "Don/animations.glb";
        if (File.Exists(animPath) && _animationsPtr == null)
        {
            int count = 0;
            _animationsPtr = Raylib.LoadModelAnimations(animPath, ref count);
            VramProbe.Track("DonChan3D.LoadModelAnimations");
            _animationCount = count;
            if (_animationsPtr != null && _animationCount > 0)
            {
                for (int i = 0; i < _animationCount; i++)
                {
                    string name = _animationsPtr[i].NameToString();
                    if (name.Contains("idle", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("loop", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("normal", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_idleAnimIndex < 0) _idleAnimIndex = i;
                    }
                }
                if (_idleAnimIndex < 0) _idleAnimIndex = 0;
            }
        }
    }

    private static void UnloadFaceTexture()
    {
        if (_texFace.Id > 0)
        {
            Raylib.UnloadTexture(_texFace);
            _texFace.Id = 0;
        }
        if (_texFaceHead.Id > 0)
        {
            Raylib.UnloadTexture(_texFaceHead);
            _texFaceHead.Id = 0;
        }
        // 💡 実テクスチャを破棄したので、次回のUpdateFaceCropでは必ず新規LoadTextureFromImageから
        //    やり直させる（UpdateTextureで存在しないテクスチャを更新しようとするのを防ぐ）。
        _texFaceAllocSize = 0;
        _texFaceHeadAllocSize = 0;
    }

    // 💡 [VRAM肥大化修正] 表情フレームが切り替わるたびにUnloadTexture→LoadTextureFromImageを
    //    繰り返すと、サイズが同じであってもOpenGLドライバ側でテクスチャオブジェクトの
    //    生成/破棄が頻発し、ドライバのメモリプールがフラグメンテーションを起こして
    //    実際の使用量以上にVRAMを確保し続けてしまう(観測: 演奏中に専用GPUメモリが
    //    1.5GB→10GB超まで単調増加し、演奏をやめても戻らない)。
    //    対策として、テクスチャオブジェクト自体は最初の1回だけ確保し、以後は
    //    Raylib.UpdateTexture()でピクセル内容だけを差し替える(GLオブジェクトの再生成なし)。
    //    サイズが変わった場合(顔素材の解像度が変わった場合)のみ例外的に再確保する。
    private static int _texFaceAllocSize = 0;
    private static int _texFaceHeadAllocSize = 0;

    private static unsafe void UploadFaceTexture(ref Texture2D tex, ref int allocSize, Image img)
    {
        // Raylib.UpdateTexture()でピクセルを直接書き換えられるよう、フォーマットをテクスチャ側と
        // 完全一致させておく(元素材がRGBA以外の場合があるため明示的に変換する)。
        if (img.Format != PixelFormat.UncompressedR8G8B8A8)
            Raylib.ImageFormat(ref img, PixelFormat.UncompressedR8G8B8A8);

        if (tex.Id == 0 || allocSize != img.Width || tex.Height != img.Height)
        {
            // 初回、またはサイズが変わった場合のみGLテクスチャオブジェクトを(再)生成する。
            if (tex.Id != 0) Raylib.UnloadTexture(tex);
            tex = Raylib.LoadTextureFromImage(img);
            Raylib.SetTextureFilter(tex, TextureFilter.Bilinear);
            allocSize = img.Width;
        }
        else
        {
            // サイズ据え置きなら、GLオブジェクトは使い回してピクセル内容だけ差し替える。
            Raylib.UpdateTexture(tex, img.Data);
        }
    }

    private static unsafe void EnsureMeshNormals(Model mdl, string label)
    {
        for (int i = 0; i < mdl.MeshCount; i++)
        {
            var mesh = mdl.Meshes[i];
            if (mesh.Normals != null || mesh.VertexCount <= 0 || mesh.Vertices == null) continue;

            Console.WriteLine($"[DonChan3D] {label} mesh[{i}] に法線が無いため生成します（verts={mesh.VertexCount}）");

            float[] normals = new float[mesh.VertexCount * 3];

            void AccumulateTriangle(int i0, int i1, int i2)
            {
                if (i0 >= mesh.VertexCount || i1 >= mesh.VertexCount || i2 >= mesh.VertexCount) return;
                Vector3 p0 = new Vector3(mesh.Vertices[i0 * 3], mesh.Vertices[i0 * 3 + 1], mesh.Vertices[i0 * 3 + 2]);
                Vector3 p1 = new Vector3(mesh.Vertices[i1 * 3], mesh.Vertices[i1 * 3 + 1], mesh.Vertices[i1 * 3 + 2]);
                Vector3 p2 = new Vector3(mesh.Vertices[i2 * 3], mesh.Vertices[i2 * 3 + 1], mesh.Vertices[i2 * 3 + 2]);
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                if (n.LengthSquared() < 1e-12f) return;
                n = Vector3.Normalize(n);
                foreach (int idx in stackalloc int[] { i0, i1, i2 })
                {
                    normals[idx * 3 + 0] += n.X;
                    normals[idx * 3 + 1] += n.Y;
                    normals[idx * 3 + 2] += n.Z;
                }
            }

            if (mesh.Indices != null && mesh.TriangleCount > 0)
            {
                for (int t = 0; t < mesh.TriangleCount; t++)
                    AccumulateTriangle(mesh.Indices[t * 3], mesh.Indices[t * 3 + 1], mesh.Indices[t * 3 + 2]);
            }
            else
            {
                for (int t = 0; t + 2 < mesh.VertexCount; t += 3)
                    AccumulateTriangle(t, t + 1, t + 2);
            }

            for (int v = 0; v < mesh.VertexCount; v++)
            {
                Vector3 n = new Vector3(normals[v * 3], normals[v * 3 + 1], normals[v * 3 + 2]);
                n = (n.LengthSquared() > 1e-12f) ? Vector3.Normalize(n) : new Vector3(0, 1, 0);
                normals[v * 3 + 0] = n.X; normals[v * 3 + 1] = n.Y; normals[v * 3 + 2] = n.Z;
            }

            mesh.AllocNormals();
            fixed (float* src = normals)
            {
                Buffer.MemoryCopy(src, mesh.Normals, mesh.VertexCount * 3 * sizeof(float), mesh.VertexCount * 3 * sizeof(float));
            }
            Raylib.UpdateMeshBuffer(mesh, 2, mesh.Normals, mesh.VertexCount * 3 * sizeof(float), 0);
            mdl.Meshes[i] = mesh;
        }

        // これにより、アニメーション再生時に頂点法線がボーンの動きに同期して正しくスキン変形され、
        // 頂点押し出しによるアウトラインがアニメーション中に捻れたり歪んで「じわーっと太さが変わる」のを完全に防ぎます。
        for (int i = 0; i < mdl.MeshCount; i++)
        {
            var mesh = mdl.Meshes[i];
            if (mesh.AnimVertices != null && mesh.AnimNormals == null && mesh.Normals != null)
            {
                int size = mesh.VertexCount * 3 * sizeof(float);
                mesh.AnimNormals = (float*)Raylib.MemAlloc((uint)size);
                Buffer.MemoryCopy(mesh.Normals, mesh.AnimNormals, size, size);
                mdl.Meshes[i] = mesh;
                Console.WriteLine($"[DonChan3D] {label} mesh[{i}] の AnimNormals 用メモリを確保・コピーしました。");
            }
        }
    }

    private static unsafe void LogMeshDiagnostics(string label, Model mdl)
    {
        for (int i = 0; i < mdl.MeshCount; i++)
        {
            var mesh = mdl.Meshes[i];
            bool hasNormals = mesh.Normals != null;
            bool hasAnimNormals = mesh.AnimNormals != null;
            int matIdx = (i < mdl.MeshCount) ? mdl.MeshMaterial[i] : -1;
            Console.WriteLine($"[DonChan3D][mesh-diag] {label} mesh[{i}] verts={mesh.VertexCount} normals={hasNormals} animNormals={hasAnimNormals} material={matIdx}");
        }
    }

    private static void LoadModelOnly()
    {
        UnloadFaceTexture();
        if (_loaded && _model.MeshCount > 0) Raylib.UnloadModel(_model);

        string modelPath = $"Don/cos/{_costumeId}.glb";
        if (!File.Exists(modelPath)) modelPath = "Don/cos/0.glb";
        if (!File.Exists(modelPath)) return;
        _model = Raylib.LoadModel(modelPath);
        VramProbe.Track($"DonChan3D.LoadModelOnly({modelPath})");

        EnsureMeshNormals(_model, "costume");

        ClearVertexColorsToWhite(_model);

        var (faceIdx, recolorIdx, additiveIdx, forceOpaqueIdx) = ParseGlbMaterialInfo(modelPath, _model);
        _faceMatIdxCostume = faceIdx;
        _recolorMatIdxCostume = recolorIdx;
        ApplyAdditiveAndForceOpaque(_model, additiveIdx, forceOpaqueIdx);
        CacheOriginalRecolorImages(_model, _recolorMatIdxCostume, _origImgCostume);
        ApplyDonColorsToAllModels();

        LoadFaceSheet();

        ApplyFaceTextureToModel();

        Console.WriteLine($"[DonChan3D] Costume MeshCount={_model.MeshCount}");
        LogMeshDiagnostics("costume", _model);
        LogFaceMeshCandidates(_model, _faceMatIdxCostume, "cos");

        if (_currentAnimIndex < 0 || _currentAnimIndex >= _animationCount)
        {
            _currentAnimIndex = (_animationCount > 0) ? _idleAnimIndex : -1;
            _currentAnimFrame = 0;
        }
        _loaded = true;

    }

    private static void LoadBodyModel(int id)
    {
        if (_bodyLoaded && _bodyModel.MeshCount > 0) Raylib.UnloadModel(_bodyModel);
        _bodyLoaded = false;

        string path = $"Don/body/{id}.glb";
        if (!File.Exists(path))
        {
            Console.WriteLine($"[DonChan3D] body model not found: {path}");
            return;
        }

        _bodyModel = Raylib.LoadModel(path);

        EnsureMeshNormals(_bodyModel, "body");

        ClearVertexColorsToWhite(_bodyModel);
        _bodyId = id;
        _bodyLoaded = true;

        var (_, recolorIdx, additiveIdx, forceOpaqueIdx) = ParseGlbMaterialInfo(path, _bodyModel);
        _recolorMatIdxBody = recolorIdx;
        ApplyAdditiveAndForceOpaque(_bodyModel, additiveIdx, forceOpaqueIdx);
        CacheOriginalRecolorImages(_bodyModel, _recolorMatIdxBody, _origImgBody);
        ApplyDonColorsToAllModels();

        Console.WriteLine($"[DonChan3D] Body loaded: {path} (MeshCount={_bodyModel.MeshCount})");
        LogMeshDiagnostics("body", _bodyModel);
        LogAllMeshBoundingBoxes("body", _bodyModel);

    }

    private static void LoadHeadModel(int id)
    {
        if (_headLoaded && _headModel.MeshCount > 0) Raylib.UnloadModel(_headModel);
        _headLoaded = false;

        string path = $"Don/head/{id}.glb";
        if (!File.Exists(path))
        {
            Console.WriteLine($"[DonChan3D] head model not found: {path}");
            return;
        }

        _headModel = Raylib.LoadModel(path);

        EnsureMeshNormals(_headModel, "head");

        ClearVertexColorsToWhite(_headModel);
        _headId = id;
        _headLoaded = true;

        var (faceIdx, recolorIdx, additiveIdx, forceOpaqueIdx) = ParseGlbMaterialInfo(path, _headModel);
        _faceMatIdxHead = faceIdx;
        _recolorMatIdxHead = recolorIdx;
        ApplyAdditiveAndForceOpaque(_headModel, additiveIdx, forceOpaqueIdx);
        CacheOriginalRecolorImages(_headModel, _recolorMatIdxHead, _origImgHead);
        ApplyDonColorsToAllModels();

        ApplyFaceTextureToHead();
        Console.WriteLine($"[DonChan3D] Head loaded: {path} (MeshCount={_headModel.MeshCount})");
        LogFaceMeshCandidates(_headModel, _faceMatIdxHead, "head");
        ComputeHeadFaceScaleCorrection();

    }

    private static void ComputeHeadFaceScaleCorrection()
    {
        if (_headFaceScaleManualOverride > 0f)
        {
            _headFaceScaleFactor = _headFaceScaleManualOverride;
            return;
        }

        _headFaceScaleFactor = 1.0f;

        if (_faceMatIdxHead < 0) return;
        int headMeshIdx = -1;
        for (int i = 0; i < _headModel.MeshCount; i++)
        {
            if (_headModel.MeshMaterial[i] == _faceMatIdxHead) { headMeshIdx = i; break; }
        }
        if (headMeshIdx == -1) return;

        BoundingBox headBB = Raylib.GetMeshBoundingBox(_headModel.Meshes[headMeshIdx]);
        float headSize = MathF.Max(headBB.Max.X - headBB.Min.X, headBB.Max.Y - headBB.Min.Y);

        if (_model.MeshCount == 0 || _faceMatIdxCostume < 0) return;
        int cosMeshIdx = -1;
        for (int i = 0; i < _model.MeshCount; i++)
        {
            if (_model.MeshMaterial[i] == _faceMatIdxCostume) { cosMeshIdx = i; break; }
        }
        if (cosMeshIdx == -1 || headSize <= 0.0001f) return;

        BoundingBox cosBB = Raylib.GetMeshBoundingBox(_model.Meshes[cosMeshIdx]);
        float cosSize = MathF.Max(cosBB.Max.X - cosBB.Min.X, cosBB.Max.Y - cosBB.Min.Y);
        if (cosSize <= 0.0001f) return;

        _headFaceScaleFactor = cosSize / headSize;

        Console.WriteLine($"[DonChan3D] Head face scale correction: {_headFaceScaleFactor:F4} (headSize={headSize:F4}, cosSize={cosSize:F4})");
    }
    public static bool IsHeadFaceScaleManual => _headFaceScaleManualOverride > 0f;
    public static float HeadFaceScaleManualOverride => _headFaceScaleManualOverride;
    public static float HeadFaceScaleFactor => _headFaceScaleFactor;

    public static void SetHeadFaceScaleManual(float value)
    {
        _headFaceScaleManualOverride = MathF.Max(0.01f, value);
        _headFaceScaleFactor = _headFaceScaleManualOverride;
    }

    public static void ClearHeadFaceScaleManual()
    {
        _headFaceScaleManualOverride = 0f;
        ComputeHeadFaceScaleCorrection();
    }

    public static float CosFaceScale => _cosFaceScaleManualOverride;

    public static void SetCosFaceScale(float value)
    {
        _cosFaceScaleManualOverride = Math.Clamp(value, 0.05f, 3.0f);
        _currentFaceFrame = -1;
    }

    public static void ResetCosFaceScale()
    {
        _cosFaceScaleManualOverride = 1.0f;
        _currentFaceFrame = -1;
    }
    private static void LogFaceMeshCandidates(Model mdl, int faceMatIdx, string tag)
    {
        if (faceMatIdx < 0) return;
        for (int i = 0; i < mdl.MeshCount; i++)
        {
            if (mdl.MeshMaterial[i] != faceMatIdx) continue;
            BoundingBox bb = Raylib.GetMeshBoundingBox(mdl.Meshes[i]);
            float w = bb.Max.X - bb.Min.X;
            float h = bb.Max.Y - bb.Min.Y;
            float d = bb.Max.Z - bb.Min.Z;
            Console.WriteLine($"[DonChan3D][FaceMeshCheck:{tag}] meshIndex={i} vertexCount={mdl.Meshes[i].VertexCount} bbox=({w:F4},{h:F4},{d:F4})");
        }
    }

    // 🖊️ 足だけ縁取り太さを変えるため、まずどのメッシュインデックスが足なのか特定するための一時デバッグ出力。
    //    minY（bbox.Min.Y）が小さい＝地面に近い＝足である可能性が高いので、そこも合わせて出力する。
    private static void LogAllMeshBoundingBoxes(string tag, Model mdl)
    {
        for (int i = 0; i < mdl.MeshCount; i++)
        {
            BoundingBox bb = Raylib.GetMeshBoundingBox(mdl.Meshes[i]);
            float w = bb.Max.X - bb.Min.X;
            float h = bb.Max.Y - bb.Min.Y;
            float d = bb.Max.Z - bb.Min.Z;
            int matIdx = mdl.MeshMaterial[i];
            Console.WriteLine($"[DonChan3D][AllMeshBBox:{tag}] meshIndex={i} matIdx={matIdx} vertexCount={mdl.Meshes[i].VertexCount} bbox=({w:F4},{h:F4},{d:F4}) minY={bb.Min.Y:F4} maxY={bb.Max.Y:F4}");
        }
    }

    private static void LoadFaceSheet()
    {
        if (_faceSheetLoaded) { Raylib.UnloadImage(_faceSheetImage); _faceSheetLoaded = false; }

        string facePath = $@"Don\Face\{_faceId}.png";
        if (!File.Exists(facePath)) facePath = @"Don\Face\0.png";
        if (!File.Exists(facePath)) return;

        _faceSheetImage = Raylib.LoadImage(facePath);
        VramProbe.Track("DonChan3D.LoadFaceSheet");
        _faceSheetLoaded = true;

        _faceFrameSize = _faceSheetImage.Width;
        _faceFrameCount = (_faceFrameSize > 0) ? Math.Max(1, _faceSheetImage.Height / _faceFrameSize) : 1;
        _currentFaceFrame = -1;
    }

    private class FaceKeyframe { public float Start, End; public int Frame; }
    private class FaceAnimDef { public List<FaceKeyframe> Keys = new(); public bool Loop; }

    private static readonly Dictionary<string, FaceAnimDef> _faceAnimTable = new();
    private static bool _faceAnimTableLoaded = false;
    private static readonly Dictionary<string, string> _faceAnimAlias = new()
    {
        { "don_bind", "don_wait_loop" },
        { "don_fukkatu_loop", "don_kusu_loop" },
        { "don_fukkatu_start", "don_kusu_in" },
        { "don_general_jump", "don_swing02" },
        { "don_general_loop", "don_norm_loop" },
    };

    private static readonly Dictionary<string, int[]> _folderFaceOverride = new()
    {
        { "result_loop", new[] { 4 } },
        { "songselect_loop", new[] { 4, 0 } },
        { "result_failed_in", new[] { 10 } },
        { "result_failed_loop", new[] { 10 } },
        { "normal", new[] { 4, 0 } },
        { "gogostart", new[] { 4, 0 } },
        { "miss_loop", new[] { 10, 11 } },
        { "gogo", new[] { 4, 0 } },
        { "entry_loop", new[] { 2 } },
        { "failed_loop", new[] { 10 } },
        { "clearin", new[] { 4, 0 } },
        { "combocut", new[] { 4, 0 } },
        { "balloon_broke", new[] { 6 } },
        { "balloon_miss", new[] { 10, 11 } },
        { "clear", new[] { 4, 0 } },
        { "10combo", new[] { 4, 0 } },
        { "10combomax", new[] { 0 } },
        { "balloon_breaking", new[] { 7 } },
        { "songselect_wait_loop", new[] { 0 } },
        { "soulin", new[] { 7, 4, 0 } },
        { "result_clear_loop", new[] { 7, 4, 0 } },
    };

    private static void LoadFaceAnimTable()
    {
        _faceAnimTableLoaded = true;
        _faceAnimTable.Clear();

        string path = "Don/animation.json";
        if (!File.Exists(path))
        {
            Console.WriteLine("[DonChan3D] Don/animation.json not found, falling back to basic mouth movement logic");
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            using JsonDocument doc = JsonDocument.Parse(json);

            foreach (JsonElement entry in doc.RootElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("type", out JsonElement typeEl) || typeEl.GetString() != "texture_change") continue;
                if (!entry.TryGetProperty("comment", out JsonElement commentEl)) continue;

                string comment = commentEl.GetString() ?? "";
                const string suffix = " face";
                if (!comment.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;

                string name = comment.Substring(0, comment.Length - suffix.Length).ToLowerInvariant();
                name = name.Replace("1p_", "_").Replace("2p_", "_");

                var def = new FaceAnimDef();
                if (entry.TryGetProperty("loop", out JsonElement loopEl) &&
                    (loopEl.ValueKind == JsonValueKind.True)) def.Loop = true;

                if (entry.TryGetProperty("textures", out JsonElement texturesEl))
                {
                    foreach (JsonElement seg in texturesEl.EnumerateArray())
                    {
                        var parts = seg.EnumerateArray().ToArray();
                        if (parts.Length < 3) continue;
                        def.Keys.Add(new FaceKeyframe
                        {
                            Start = parts[0].GetSingle(),
                            End = parts[1].GetSingle(),
                            Frame = parts[2].GetInt32()
                        });
                    }
                }

                if (def.Keys.Count > 0 && !_faceAnimTable.ContainsKey(name))
                    _faceAnimTable[name] = def;
            }

            Console.WriteLine($"[DonChan3D] face animation table loaded: {_faceAnimTable.Count} entries from {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DonChan3D] Don/animation.json failed to read: {ex.Message}");
        }
    }

    private static int GetFaceFrameForCurrentState()
    {
        if (_faceFrameCount <= 1) return 0;

        int autoFrame = GetAutoFaceFrame();

        if (_faceFrameManualOffset != 0f)
        {
            float raw = autoFrame + _faceFrameManualOffset;
            int shifted = (int)MathF.Round(raw, MidpointRounding.AwayFromZero);
            shifted = ((shifted % _faceFrameCount) + _faceFrameCount) % _faceFrameCount;
            return shifted;
        }

        return autoFrame;
    }

    private static int GetAutoFaceFrame()
    {
        int totalFramesForOverride = (_currentAnimIndex >= 0 && _currentAnimIndex < _animationCount && _animationsPtr != null)
            ? _animationsPtr[_currentAnimIndex].KeyFrameCount : 1;
        int animFrameNow = _isExporting ? _exportFrameCounter : _currentAnimFrame;
        float progress01ForOverride = totalFramesForOverride > 1 ? (float)animFrameNow / (totalFramesForOverride - 1) : 0f;

        string folderKey = GetMappedFolderName().Split(',')[0].Trim();
        if (_folderFaceOverride.TryGetValue(folderKey, out int[] overrideFrames) && overrideFrames.Length > 0)
        {
            int overrideIdx = Math.Clamp((int)(progress01ForOverride * overrideFrames.Length), 0, overrideFrames.Length - 1);
            return Math.Clamp(overrideFrames[overrideIdx], 0, _faceFrameCount - 1);
        }

        if (!_faceAnimTableLoaded) LoadFaceAnimTable();

        string animName = CurrentAnimName.ToLowerInvariant();
        bool mirror = animName.Contains("mirror");
        string baseName = mirror ? animName.Replace("_mirror", "") : animName;
        if (_faceAnimAlias.TryGetValue(baseName, out string alias)) baseName = alias;

        if (_faceAnimTable.TryGetValue(baseName, out FaceAnimDef def) && def.Keys.Count > 0)
        {
            int totalFrames = (_currentAnimIndex >= 0 && _currentAnimIndex < _animationCount && _animationsPtr != null)
                ? _animationsPtr[_currentAnimIndex].KeyFrameCount : 1;
            float progress01 = totalFrames > 1 ? (float)animFrameNow / (totalFrames - 1) : 0f;
            float t = progress01 * 1000f;

            for (int i = 0; i < def.Keys.Count; i++)
            {
                var k = def.Keys[i];
                bool isLast = (i == def.Keys.Count - 1);
                if (t >= k.Start && (t < k.End || isLast))
                    return Math.Clamp(k.Frame, 0, _faceFrameCount - 1);
            }
            return Math.Clamp(def.Keys[def.Keys.Count - 1].Frame, 0, _faceFrameCount - 1);
        }

        return Math.Clamp((animFrameNow / 6) % 2, 0, _faceFrameCount - 1);
    }

    public static int FaceFrameCount => _faceFrameCount;
    public static int CurrentFaceFrame => _currentFaceFrame;

    public static bool IsFaceFrameManual => _faceFrameManualOffset != 0f;
    public static float FaceFrameManualOffset => _faceFrameManualOffset;

    public static void NextFaceFrameManual()
    {
        if (_faceFrameCount <= 1) return;
        _faceFrameManualOffset += FaceFrameManualStep;
        float half = _faceFrameCount / 2f;
        while (_faceFrameManualOffset > half) _faceFrameManualOffset -= _faceFrameCount;
        while (_faceFrameManualOffset < -half) _faceFrameManualOffset += _faceFrameCount;
        Console.WriteLine($"[DonChan3D] manual face offset={_faceFrameManualOffset:0.###}  anim={CurrentAnimName}");
    }

    public static void PrevFaceFrameManual()
    {
        if (_faceFrameCount <= 1) return;
        _faceFrameManualOffset -= FaceFrameManualStep;
        float half = _faceFrameCount / 2f;
        while (_faceFrameManualOffset > half) _faceFrameManualOffset -= _faceFrameCount;
        while (_faceFrameManualOffset < -half) _faceFrameManualOffset += _faceFrameCount;
        Console.WriteLine($"[DonChan3D] manual face offset={_faceFrameManualOffset:0.###}  anim={CurrentAnimName}");
    }

    public static void ClearFaceFrameManual()
    {
        _faceFrameManualOffset = 0f;
    }

    private static void UpdateFaceCrop()
    {
        if (!_faceSheetLoaded)
        {
            LoadFaceSheet();
        }

        if (!_faceSheetLoaded) return;

        int frame = GetFaceFrameForCurrentState();
        if (frame == _currentFaceFrame) return;

        // 💡 以前はここで毎回 UnloadFaceTexture() → LoadTextureFromImage()
        //    していたが、サイズが同じでもGLテクスチャオブジェクトの生成/破棄が
        //    頻発するとドライバ側でVRAMフラグメンテーションが起き、専用GPUメモリが
        //    単調増加し続ける原因になっていた。UnloadFaceTexture()は呼ばず、
        //    UploadFaceTexture()内でサイズが変わらない限りテクスチャオブジェクトを
        //    使い回し、ピクセル内容だけUpdateTexture()で差し替える。
        VramProbe.Track($"DonChan3D.UpdateFaceCrop REGEN (anim={CurrentAnimName}, frame={frame})");

        Rectangle crop = new Rectangle(0, frame * _faceFrameSize, _faceFrameSize, _faceFrameSize);

        if (_cosFaceScaleManualOverride != 1.0f)
        {
            Image cosCanvas = Raylib.GenImageColor(_faceFrameSize, _faceFrameSize, Color.Blank);
            int cosScaledSize = Math.Max(1, (int)MathF.Round(_faceFrameSize * _cosFaceScaleManualOverride));
            Image frameImgForCos = Raylib.ImageFromImage(_faceSheetImage, crop);
            Raylib.ImageResize(ref frameImgForCos, cosScaledSize, cosScaledSize);
            int cosOffset = (_faceFrameSize - cosScaledSize) / 2;
            Raylib.ImageDraw(ref cosCanvas, frameImgForCos,
                new Rectangle(0, 0, cosScaledSize, cosScaledSize),
                new Rectangle(cosOffset, cosOffset, cosScaledSize, cosScaledSize),
                Color.White);
            UploadFaceTexture(ref _texFace, ref _texFaceAllocSize, cosCanvas);
            Raylib.UnloadImage(frameImgForCos);
            Raylib.UnloadImage(cosCanvas);
        }
        else
        {
            Image frameImg0 = Raylib.ImageFromImage(_faceSheetImage, crop);
            UploadFaceTexture(ref _texFace, ref _texFaceAllocSize, frameImg0);
            Raylib.UnloadImage(frameImg0);
        }

        try
        {
            Image headCanvas = Raylib.GenImageColor(_faceFrameSize, _faceFrameSize, Color.Blank);
            int scaledSize = Math.Max(1, (int)MathF.Round(_faceFrameSize * _headFaceScaleFactor));
            Image frameImgForHead = Raylib.ImageFromImage(_faceSheetImage, crop);
            Raylib.ImageResize(ref frameImgForHead, scaledSize, scaledSize);
            int offset = (_faceFrameSize - scaledSize) / 2;
            Raylib.ImageDraw(ref headCanvas, frameImgForHead,
                new Rectangle(0, 0, scaledSize, scaledSize),
                new Rectangle(offset, offset, scaledSize, scaledSize),
                Color.White);
            UploadFaceTexture(ref _texFaceHead, ref _texFaceHeadAllocSize, headCanvas);
            Raylib.UnloadImage(frameImgForHead);
            Raylib.UnloadImage(headCanvas);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DonChan3D] Head face texture generation failed, falling back to unscaled: {ex.Message}");
            Image fallbackImg = Raylib.ImageFromImage(_faceSheetImage, crop);
            UploadFaceTexture(ref _texFaceHead, ref _texFaceHeadAllocSize, fallbackImg);
            Raylib.UnloadImage(fallbackImg);
        }

        _currentFaceFrame = frame;
    }

    private static void ApplyFaceTextureToModel()
    {
        UpdateFaceCrop();
        if (_model.MeshCount == 0 || _texFace.Id == 0) return;

        if (_model.MaterialCount > _faceMatIdxCostume)
        {
            _model.Materials[_faceMatIdxCostume].Maps[(int)MaterialMapIndex.Albedo].Texture = _texFace;
            _model.Materials[_faceMatIdxCostume].Maps[(int)MaterialMapIndex.Albedo].Color = Color.White;
        }
    }

    private static void ApplyFaceTextureToHead()
    {
        UpdateFaceCrop();
        if (!_headLoaded || _headModel.MeshCount == 0 || _texFaceHead.Id == 0) return;

        if (_headModel.MaterialCount > _faceMatIdxHead)
        {
            _headModel.Materials[_faceMatIdxHead].Maps[(int)MaterialMapIndex.Albedo].Texture = _texFaceHead;
            _headModel.Materials[_faceMatIdxHead].Maps[(int)MaterialMapIndex.Albedo].Color = Color.White;
        }
    }

    // 🖊️ 全GLB共通の「線画(黒線)」太さ調整。テクスチャに焼き込まれた黒線をピクセル単位で
    //    膨張(太く)/収縮(細く)させる。500個以上あるGLBを1つずつ編集せずに済むよう、
    //    RecolorTexture（全モデル・全テクスチャが必ず通る場所）に仕込む。
    //    範囲は-3〜3px程度に制限（大きすぎると処理が重くなる・絵が壊れるため）。
    private static int _lineThicknessAdjust = 0;
    public static int LineThicknessAdjust
    {
        get => _lineThicknessAdjust;
        set
        {
            _lineThicknessAdjust = Math.Clamp(value, -3, 3);
            ApplyDonColorsToAllModels();
        }
    }

    private static unsafe Texture2D RecolorTexture(Image source, Color body, Color face, Color rim)
    {
        Image img = Raylib.ImageCopy(source);
        Raylib.ImageFormat(ref img, PixelFormat.UncompressedR8G8B8A8);

        int w = img.Width, h = img.Height;
        byte* pixels = (byte*)img.Data;
        int total = w * h;

        // 元の線画判定（黒っぽい/彩度が低いピクセル＝線画）
        bool[] isLine = new bool[total];
        for (int i = 0; i < total; i++)
        {
            float r = pixels[i * 4 + 0] / 255f;
            float g = pixels[i * 4 + 1] / 255f;
            float b = pixels[i * 4 + 2] / 255f;
            float strongest = MathF.Max(r, MathF.Max(g, b));
            float weakest = MathF.Min(r, MathF.Min(g, b));
            isLine[i] = strongest <= 0.05f || (strongest - weakest) <= 0.08f;
        }

        bool[] finalMask = isLine;
        int adjust = _lineThicknessAdjust;
        if (adjust != 0)
        {
            finalMask = new bool[total];
            int radius = Math.Abs(adjust);
            bool thicken = adjust > 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    bool result = isLine[idx];
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= h) continue;
                        int rowBase = ny * w;
                        bool hitNeighbor = false;
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int nx = x + dx;
                            if (nx < 0 || nx >= w) continue;
                            bool neighborIsLine = isLine[rowBase + nx];
                            if (thicken && neighborIsLine) { result = true; hitNeighbor = true; break; }
                            if (!thicken && !neighborIsLine) { result = false; hitNeighbor = true; break; }
                        }
                        if (hitNeighbor) break;
                    }
                    finalMask[idx] = result;
                }
            }
        }

        for (int i = 0; i < total; i++)
        {
            if (finalMask[i])
            {
                // 線画として扱うピクセル。膨張で新たに線になった箇所は黒で塗る。
                if (!isLine[i])
                {
                    pixels[i * 4 + 0] = 13;
                    pixels[i * 4 + 1] = 13;
                    pixels[i * 4 + 2] = 13;
                }
                continue;
            }

            Color outc;
            if (isLine[i])
            {
                // 収縮によって線画から外れたピクセル。元の色情報が失われているため
                // bodyカラーで近似的に塗る（境界付近でわずかに色味が寄る場合がある）。
                outc = body;
            }
            else
            {
                float r = pixels[i * 4 + 0] / 255f;
                float g = pixels[i * 4 + 1] / 255f;
                float b = pixels[i * 4 + 2] / 255f;
                if (b > r && b >= g) outc = rim;
                else if (g > r && g > b) outc = face;
                else outc = body;
            }

            pixels[i * 4 + 0] = outc.R;
            pixels[i * 4 + 1] = outc.G;
            pixels[i * 4 + 2] = outc.B;
        }

        Texture2D tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        VramProbe.Track("DonChan3D.RecolorTexture");
        return tex;
    }

    private static void ApplyDonColorsToModel(Model mdl, List<int> recolorIndices, Dictionary<int, Image> origImages,
                                               Color body, Color face, Color rim)
    {
        foreach (int idx in recolorIndices)
        {
            if (idx >= mdl.MaterialCount || !origImages.TryGetValue(idx, out Image src)) continue;

            Texture2D oldTex = mdl.Materials[idx].Maps[(int)MaterialMapIndex.Albedo].Texture;
            Texture2D newTex = RecolorTexture(src, body, face, rim);

            mdl.Materials[idx].Maps[(int)MaterialMapIndex.Albedo].Texture = newTex;
            mdl.Materials[idx].Maps[(int)MaterialMapIndex.Albedo].Color = Color.White;

            if (oldTex.Id != 0 && oldTex.Id != newTex.Id) Raylib.UnloadTexture(oldTex);
        }
    }

    private static void ApplyDonColorsToAllModels()
    {
        Color body = BodyColor, face = FaceColor, rim = RimColor;
        if (_loaded) ApplyDonColorsToModel(_model, _recolorMatIdxCostume, _origImgCostume, body, face, rim);
        if (_bodyLoaded) ApplyDonColorsToModel(_bodyModel, _recolorMatIdxBody, _origImgBody, body, face, rim);
        if (_headLoaded) ApplyDonColorsToModel(_headModel, _recolorMatIdxHead, _origImgHead, body, face, rim);
    }

    private static void CacheOriginalRecolorImages(Model mdl, List<int> recolorIndices, Dictionary<int, Image> cache)
    {
        foreach (var kv in cache) Raylib.UnloadImage(kv.Value);
        cache.Clear();

        foreach (int idx in recolorIndices)
        {
            if (idx >= mdl.MaterialCount) continue;
            Texture2D tex = mdl.Materials[idx].Maps[(int)MaterialMapIndex.Albedo].Texture;
            if (tex.Id == 0) continue;
            Image img = Raylib.LoadImageFromTexture(tex);
            Raylib.ImageFormat(ref img, PixelFormat.UncompressedR8G8B8A8);
            cache[idx] = img;
        }
    }

    public static void SetFaceId(int id)
    {
        if (id < 0) id = 0;
        _faceId = id;
        LoadFaceSheet();
        ApplyFaceTextureToModel();
        ApplyFaceTextureToHead();
    }

    public static void NextFaceColor()
    {
        _faceColorIndex = (_faceColorIndex + 1) % _sharedColorPalette.Length;
        ApplyDonColorsToAllModels();
    }

    public static void PrevFaceColor()
    {
        _faceColorIndex = (_faceColorIndex - 1 + _sharedColorPalette.Length) % _sharedColorPalette.Length;
        ApplyDonColorsToAllModels();
    }

    public static void NextBodyColor()
    {
        _bodyColorIndex = (_bodyColorIndex + 1) % _sharedColorPalette.Length;
        ApplyDonColorsToAllModels();
    }

    public static void PrevBodyColor()
    {
        _bodyColorIndex = (_bodyColorIndex - 1 + _sharedColorPalette.Length) % _sharedColorPalette.Length;
        ApplyDonColorsToAllModels();
    }

    public static void NextRimColor()
    {
        _rimColorIndex = (_rimColorIndex + 1) % _sharedColorPalette.Length;
        ApplyDonColorsToAllModels();
    }

    public static void PrevRimColor()
    {
        _rimColorIndex = (_rimColorIndex - 1 + _sharedColorPalette.Length) % _sharedColorPalette.Length;
        ApplyDonColorsToAllModels();
    }

    public static int BodyColorIndex => _bodyColorIndex;
    public static int FaceColorIndex => _faceColorIndex;
    public static int RimColorIndex => _rimColorIndex;

    public static void SetBodyColorIndex(int idx)
    {
        if (idx < 0 || idx >= _sharedColorPalette.Length) return;
        _bodyColorIndex = idx;
        ApplyDonColorsToAllModels();
    }

    public static void SetFaceColorIndex(int idx)
    {
        if (idx < 0 || idx >= _sharedColorPalette.Length) return;
        _faceColorIndex = idx;
        ApplyDonColorsToAllModels();
    }

    public static void SetRimColorIndex(int idx)
    {
        if (idx < 0 || idx >= _sharedColorPalette.Length) return;
        _rimColorIndex = idx;
        ApplyDonColorsToAllModels();
    }

    private static void ClearVertexColorsToWhite(Model mdl)
    {
        if (mdl.MeshCount == 0) return;
        for (int i = 0; i < mdl.MeshCount; i++)
        {
            Mesh mesh = mdl.Meshes[i];
            if (mesh.Colors != null)
            {
                int totalBytes = mesh.VertexCount * 4;
                for (int v = 0; v < totalBytes; v++) mesh.Colors[v] = 255;
                Raylib.UpdateMeshBuffer(mesh, 3, mesh.Colors, totalBytes, 0);
            }
        }
    }

    public static void Update()
    {
        if (!_loaded) return;

        double __nowForMemReport = Raylib.GetTime();
        if (Raylib.IsKeyPressed(KeyboardKey.F7))
        {
            LogMemoryReport();
        }
        VramProbe.Tick(__nowForMemReport);

        _camera3D.Position = new Vector3(CamPosX, CamPosY, CamPosZ);
        _camera3D.Target = new Vector3(0f, TarPosY, 0f);
        _camera3D.FovY = CamFovY;

        if (_currentAnimIndex >= 0 && _currentAnimIndex < _animationCount && _animationsPtr != null)
        {
            int total = _animationsPtr[_currentAnimIndex].KeyFrameCount;
            int frameToApply = _currentAnimFrame;

            if (_isExporting) frameToApply = _exportFrameCounter;
            else
            {
                if (total > 1)
                {
                    float dtClamped = Math.Min(Raylib.GetFrameTime(), 0.1f);
                    _animFrameAccumulator += dtClamped * PlaybackFps;
                    int steps = (int)_animFrameAccumulator;
                    if (steps > 0)
                    {
                        _animFrameAccumulator -= steps;
                        if (LoopAnimation)
                        {
                            int loopLen = Math.Max(1, total - 1); // 最終フレームは先頭フレームの複製なのでループ長から除外
                            _currentAnimFrame = (_currentAnimFrame + steps) % loopLen;
                        }
                        else
                        {
                            _currentAnimFrame = Math.Min(_currentAnimFrame + steps, total - 1);
                        }
                    }
                }
                frameToApply = _currentAnimFrame;
                IsCurrentAnimFinished = !LoopAnimation && total > 0 && _currentAnimFrame >= total - 1;
            }

            // 🖊️ 縁取り変更⑪修正: CosOnlyは「衣装のみ」を意味するモードなので、
            //    needBody/needHeadにCosOnlyを含めると着せ替え時に頭・体が衣装と共存してしまう。
            //    そのためCosOnlyはneedCosのみに限定し、needBody/needHeadからは除外する。
            bool needCos = CurrentDrawMode == DrawMode.CosOnly || CurrentDrawMode == DrawMode.All;
            bool needBody = CurrentDrawMode == DrawMode.BodyOnly || CurrentDrawMode == DrawMode.BodyHeadOnly || CurrentDrawMode == DrawMode.All;
            bool needHead = CurrentDrawMode == DrawMode.HeadOnly || CurrentDrawMode == DrawMode.BodyHeadOnly || CurrentDrawMode == DrawMode.All;

            if (needCos) Raylib.UpdateModelAnimation(_model, _animationsPtr[_currentAnimIndex], frameToApply);
            if (needBody && _bodyLoaded) Raylib.UpdateModelAnimation(_bodyModel, _animationsPtr[_currentAnimIndex], frameToApply);
            if (needHead && _headLoaded) Raylib.UpdateModelAnimation(_headModel, _animationsPtr[_currentAnimIndex], frameToApply);

            if (needCos) ApplyFaceTextureToModel();
            if (needHead) ApplyFaceTextureToHead();

            if (_isExporting)
            {
                DrawSimple();
                ExecuteFrameExport();
                _exportFrameCounter++;

                if (_exportFrameCounter >= _exportTotalFrames)
                {
                    if (_isBatchMode) MoveToNextBatchAnimation();
                    else _isExporting = false;
                }
            }
        }
    }

    private static void ResetRenderBuffersForExport()
    {
        if (_currentAnimIndex < 0 || _animationsPtr == null) return;

        Raylib.UpdateModelAnimation(_model, _animationsPtr[_currentAnimIndex], 0);
        if (_bodyLoaded) Raylib.UpdateModelAnimation(_bodyModel, _animationsPtr[_currentAnimIndex], 0);
        if (_headLoaded) Raylib.UpdateModelAnimation(_headModel, _animationsPtr[_currentAnimIndex], 0);

        ApplyFaceTextureToModel();
        ApplyFaceTextureToHead();

        Raylib.BeginTextureMode(_renderBuffer);
        Raylib.ClearBackground(Color.Blank);
        Raylib.EndTextureMode();

        DrawSimple();
    }

    public static void StartExport()
    {
        if (!_loaded || _currentAnimIndex < 0 || _isExporting || _animationsPtr == null) return;
        if (GetMappedFolderName() == "skip") return;

        _isBatchMode = false;
        _exportTotalFrames = _animationsPtr[_currentAnimIndex].KeyFrameCount;

        _exportFrameCounter = 0;
        _currentAnimFrame = 0;

        ResetRenderBuffersForExport();
        _isExporting = true;
    }

    public static void StartBatchExport()
    {
        if (!_loaded || _animationCount == 0 || _isExporting || _animationsPtr == null) return;
        _isBatchMode = true; _isExporting = true; _currentAnimIndex = -1;
        MoveToNextBatchAnimation();
    }

    private static void MoveToNextBatchAnimation()
    {
        while (true)
        {
            _currentAnimIndex++;
            if (_currentAnimIndex >= _animationCount)
            {
                _isExporting = false; _isBatchMode = false;
                _currentAnimIndex = _idleAnimIndex;
                break;
            }

            string name = GetAnimationNameById(_currentAnimIndex);
            if (name.Contains("mirror", StringComparison.OrdinalIgnoreCase)) continue;

            string folderName = GetMappedFolderName();
            if (folderName != "skip")
            {
                _exportTotalFrames = _animationsPtr[_currentAnimIndex].KeyFrameCount;
                _exportFrameCounter = 0; _currentAnimFrame = 0;
                ResetRenderBuffersForExport();
                break;
            }
        }
    }

    // 🖊️ 縁取り変更⑧（今回）: forceDepthDisabled パラメータを追加。
    //    trueを渡すと、そのモデルの全メッシュを「顔と同じ深度テスト無効化パス」で描画する。
    //    体(_bodyModel＝足)を呼ぶときにtrueを渡すことで、足にも顔の外側と同じ縁取り方式が使われる。
    private static void DrawModelMeshesOutline(Model mdl, Matrix4x4 transform, int faceMatIdx, int skipMeshIndex = -1, bool forceDepthDisabled = false, float? overrideThickness = null)
    {
        if (!_outlineMaterialReady || mdl.MaterialCount == 0) return;

        // 🖊️ 縁取り変更⑨（今回）: overrideThicknessが指定されている場合（足など）は、
        //    描画直前にシェーダーの太さを一時的に差し替え、描画後に元の太さへ戻す。
        //    これにより足だけ別の太さ（_bodyOutlineThickness）で縁取りできるようになる。
        bool needThicknessOverride = overrideThickness.HasValue;
        if (needThicknessOverride) SetOutlineShaderThickness(overrideThickness.Value);

        Rlgl.SetCullFace(RL_CULL_FACE_FRONT);
        try
        {
            // 顔以外のメッシュ（体・頭本体など）はこれまで通り通常の深度テストで縁取りを描く。
            // 🖊️ 縁取り変更⑧（今回）: forceDepthDisabled=trueのとき（足）は、このパスは使わず
            //    全メッシュを下の深度テスト無効パスへ回すので、ここはスキップする。
            if (!forceDepthDisabled)
            {
                for (int i = 0; i < mdl.MeshCount; i++)
                {
                    if (i == skipMeshIndex) continue;
                    int matIdx = mdl.MeshMaterial[i];
                    if (matIdx == faceMatIdx) continue;

                    Raylib.DrawMesh(mdl.Meshes[i], _outlineMaterial, transform);
                }
            }

            // 🖊️ 縁取り変更④: 以前はここで faceMatIdx のメッシュ（顔）を丸ごとcontinueして
            //    スキップしていたため、顔の周りだけ縁取りが一切付かなかった。
            //    顔メッシュは頭部メッシュとほぼ同じ位置に重なって配置されており、
            //    DrawModelMeshes側の顔描画（下のignoreDepthTestForFace）と同様に
            //    深度テストを無効化しないと、押し出した縁取りが頭部メッシュの奥に隠れて
            //    表示されない（またはZファイティングする）。そのため顔メッシュ用に
            //    深度テストを切った状態で縁取りを描画するパスを追加し、顔にも縁取りが付くようにした。
            // 🖊️ 縁取り変更⑧（今回）: forceDepthDisabled=trueのとき（足＝_bodyModel）は
            //    matIdxに関係なく全メッシュをここで描画する。足は体の他パーツ（コスチューム等）と
            //    近い位置に重なっていて、顔と同様に通常の深度テストだと縁取りが隠れやすいため、
            //    顔の外側の縁取りと同じ「深度テスト無効化」方式を適用した。
            // 🖊️ 縁取り変更⑫（今回）: 顔メッシュの縁取りが常に最前面に表示され、
            //    髪や衣装より手前に突き抜けてしまう問題を修正。
            //    forceDepthDisabled=true（足など、意図的に深度無視したいパーツ）の時だけ
            //    深度テストを無効化し、通常の顔の縁取りは深度テストを有効なまま描画することで、
            //    髪や帽子など本来手前にあるべきメッシュにきちんと隠れるようにする。
            Rlgl.DisableDepthMask();
            if (forceDepthDisabled) Rlgl.DisableDepthTest();
            try
            {
                for (int i = 0; i < mdl.MeshCount; i++)
                {
                    if (i == skipMeshIndex) continue;
                    int matIdx = mdl.MeshMaterial[i];
                    if (!forceDepthDisabled && matIdx != faceMatIdx) continue;

                    Raylib.DrawMesh(mdl.Meshes[i], _outlineMaterial, transform);
                }
            }
            finally
            {
                if (forceDepthDisabled) Rlgl.EnableDepthTest();
                Rlgl.EnableDepthMask();
            }
        }
        finally
        {
            Rlgl.SetCullFace(RL_CULL_FACE_BACK);
            // 🖊️ 縁取り変更⑨（今回）: 太さを一時的に差し替えていた場合は、他の描画に影響しないよう
            //    グローバルな_outlineThicknessに必ず戻す。
            if (needThicknessOverride) SetOutlineShaderThickness(_outlineThickness);
        }
    }

    private static void DrawModelMeshesExcludingFace(Model mdl, Matrix4x4 transform, int faceMatIdx, int skipMeshIndex = -1)
    {
        for (int i = 0; i < mdl.MeshCount; i++)
        {
            if (i == skipMeshIndex) continue;
            int matIdx = mdl.MeshMaterial[i];
            if (matIdx == faceMatIdx) continue;
            Raylib.DrawMesh(mdl.Meshes[i], mdl.Materials[matIdx], transform);
        }
    }

    private static void DrawModelMeshes(Model mdl, Matrix4x4 transform, int faceMatIdx, int skipMeshIndex = -1, bool ignoreDepthTestForFace = true)
    {
        for (int i = 0; i < mdl.MeshCount; i++)
        {
            if (i == skipMeshIndex) continue;
            int matIdx = mdl.MeshMaterial[i];
            if (matIdx == faceMatIdx) continue;
            Raylib.DrawMesh(mdl.Meshes[i], mdl.Materials[matIdx], transform);
        }

        Rlgl.DisableDepthMask();

        if (ignoreDepthTestForFace) Rlgl.DisableDepthTest();
        try
        {
            for (int i = 0; i < mdl.MeshCount; i++)
            {
                if (i == skipMeshIndex) continue;
                int matIdx = mdl.MeshMaterial[i];
                if (matIdx != faceMatIdx) continue;
                Raylib.DrawMesh(mdl.Meshes[i], mdl.Materials[matIdx], transform);
            }
        }
        finally
        {
            if (ignoreDepthTestForFace) Rlgl.EnableDepthTest();
            Rlgl.EnableDepthMask();
        }
    }

    public static void DrawSimple()
    {
        // エクスポート(画像書き出し)用の呼び出し。画質設定に関わらず常にフル解像度で描く。
        DrawSimple(new Rectangle(0, 0, 820, 599), forceHighQuality: true);
    }


    public static void DrawSimple(Rectangle dest) => DrawSimple(dest, forceHighQuality: false);

    public static void DrawSimple(Rectangle dest, bool forceHighQuality)
    {
        if (!_loaded) return;

        float baseScale = 0.93f;
        // 💡 カメラ(CamFovY等)には一切触れず、モデル側のスケールだけを少し縮小して
        //    頭上に余白を作る。CreateTranslationで足元の位置も軽く下げて、
        //    縮小した分で全体が浮き上がって見えないようにする。
        float baseOffsetY = -0.01f;

        float rotRadY = -ModelRotY * MathF.PI / 180f;
        Matrix4x4 meshTransform = Matrix4x4.CreateScale(baseScale)
            * Matrix4x4.CreateRotationY(rotRadY)
            * Matrix4x4.CreateTranslation(0f, baseOffsetY, 0f);


        bool drawCos = (CurrentDrawMode == DrawMode.CosOnly || CurrentDrawMode == DrawMode.All);
        // 🖊️ 縁取り変更⑪修正: CosOnlyは衣装のみを表示するモードなので、
        //    drawBody/drawHeadからは除外し、着せ替え中に頭・体が二重表示されないようにする。
        bool drawBody = (CurrentDrawMode == DrawMode.BodyOnly || CurrentDrawMode == DrawMode.BodyHeadOnly || CurrentDrawMode == DrawMode.All);
        bool drawHead = (CurrentDrawMode == DrawMode.HeadOnly || CurrentDrawMode == DrawMode.BodyHeadOnly || CurrentDrawMode == DrawMode.All);

        // 描画先(dest)のピクセルサイズに合わせてレンダーバッファを確保し直す。
        // エクスポート時はDrawSimple()経由で常に820x599が渡ってくるので出力サイズは変わらない。
        // 画質設定がLowの場合は内部レンダーバッファを半解像度にして3D描画コストを下げる
        // (表示先のdestサイズ自体は変えないので、拡大表示される見た目のサイズは変わらない)。
        float qScale = (!forceHighQuality && RenderQuality == QualityLevel.Low) ? 0.5f : 1f;
        EnsureRenderBufferSize(
            (int)MathF.Round(dest.Width * qScale),
            (int)MathF.Round(dest.Height * qScale));

        // 💡 仮想スクリーンのBeginTextureModeの中からネストしてBeginTextureModeを呼ぶと
        //    EndTextureMode後に描画ターゲットが仮想スクリーンに戻らないため、一度退避して復帰する。
        // _insideVirtualFrameはDraw()から呼ばれるときにtrueにセットされる。
        if (_insideVirtualFrame) Raylib.EndTextureMode();
        Raylib.BeginTextureMode(_renderBuffer);
        Raylib.ClearBackground(Color.Blank);
        Raylib.BeginBlendMode(BlendMode.Alpha);
        Raylib.BeginMode3D(_camera3D);
        _model.Transform = Matrix4x4.Identity;

        // 頂点法線がボーンアニメーションに完全同期し、かつクリップ空間での一定量押し出しとなったため、
        // アニメーション時の縦横伸縮ポーズ（Squash & Stretch）でもチラつきや太さの変動が一切なく綺麗に実体化されます。
        if (drawCos) DrawModelMeshesOutline(_model, meshTransform, _faceMatIdxCostume);
        // 🖊️ 縁取り変更⑧（今回）: 足(_bodyModel)にも顔の外側と同じ「深度テスト無効化」方式の
        //    縁取りが付くように forceDepthDisabled: true を渡す。
        // 🖊️ 縁取り変更⑨（今回）: さらに overrideThickness に _bodyOutlineThickness を渡すことで、
        //    足専用の太さがちゃんとシェーダーに反映されるようにする（以前は未使用だった）。
        if (drawBody && _bodyLoaded) DrawModelMeshesOutline(_bodyModel, meshTransform, -1, forceDepthDisabled: true, overrideThickness: _bodyOutlineThickness);
        if (drawHead && _headLoaded) DrawModelMeshesOutline(_headModel, meshTransform, _faceMatIdxHead);

        if (drawCos) DrawModelMeshes(_model, meshTransform, _faceMatIdxCostume, ignoreDepthTestForFace: false);
        if (drawBody && _bodyLoaded) DrawModelMeshes(_bodyModel, meshTransform, -1);
        if (drawHead && _headLoaded) DrawModelMeshes(_headModel, meshTransform, _faceMatIdxHead, ignoreDepthTestForFace: false);

        Raylib.EndMode3D();
        Raylib.EndBlendMode();
        Raylib.EndTextureMode();
        if (_insideVirtualFrame) Program.BeginVirtualFrame();
        Rectangle src = new Rectangle(0, 0, _renderBuffer.Texture.Width, -_renderBuffer.Texture.Height);

        Raylib.BeginBlendMode(BlendMode.CustomSeparate);
        if (_outlinePassShaderLoaded)
        {
            Raylib.BeginShaderMode(_outlinePassShader);
            Raylib.DrawTexturePro(_renderBuffer.Texture, src, dest, Vector2.Zero, 0f, Color.White);
            Raylib.EndShaderMode();
        }
        else
        {
            Raylib.DrawTexturePro(_renderBuffer.Texture, src, dest, Vector2.Zero, 0f, Color.White);
        }
        Raylib.EndBlendMode();
    }

    public static void SetBodyId(int id)
    {
        if (id < 0) id = 0;
        LoadBodyModel(id);
    }

    public static void SetHeadId(int id)
    {
        if (id < 0) id = 0;
        LoadHeadModel(id);
    }

    public static void ChangeCustomPart(int partType, bool next) { }
    public static void NextCostume() { if (_isExporting) return; _costumeId++; LoadModelOnly(); }
    public static void PrevCostume() { if (_isExporting) return; _costumeId--; if (_costumeId < 0) _costumeId = 0; LoadModelOnly(); }
    public static void SetCostumeId(int id)
    {
        if (_isExporting || id < 0 || id == _costumeId) return;
        _costumeId = id;
        LoadModelOnly();
    }

    private static void ExecuteFrameExport()
    {
        string folderName = GetMappedFolderName();
        if (folderName == "skip") return;

        string[] folders = folderName.Split(',');
        foreach (var folder in folders)
        {
            string dirPath = Path.Combine("Don", "Export", folder.Trim());
            if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);

            Image img = Raylib.LoadImageFromTexture(_renderBuffer.Texture);
            Raylib.ImageFlipVertical(ref img);

            string fileName = Path.Combine(dirPath, $"{_exportFrameCounter}.png");
            Raylib.ExportImage(img, fileName);
            Raylib.UnloadImage(img);
        }
    }

    public static string GetMappedFolderName()
    {
        string animName = CurrentAnimName.ToLower();
        if (animName.Contains("don_balloon_failure")) return "balloon_miss";
        if (animName.Contains("don_balloon_loop")) return "balloon_breaking";
        if (animName.Contains("don_balloon_success")) return "balloon_broke";
        if (animName.Contains("don_combo_max") || animName.Contains("don_full_combo")) return "10combomax";
        if (animName.Contains("don_combo")) return "10combo";
        if (animName.Contains("don_entry_loop")) return "entry_loop";
        if (animName.Contains("don_full_gage")) return "soulin";
        if (animName.Contains("don_result_failure_loop")) return "result_failed_loop";
        if (animName.Contains("don_result_failure")) return "result_failed_in";
        if (animName.Contains("don_result_full_loop")) return "result_clear_loop";
        if (animName.Contains("don_miss6")) return "failed_loop";
        if (animName.Contains("don_miss_normal")) return "combocut";
        if (animName.Contains("don_miss")) return "miss_loop";
        if (animName.Contains("don_norm_down")) return "failed_down";
        if (animName.Contains("don_norm_up")) return "clearin";
        if (animName.Contains("don_norm_loop")) return "clear";
        if (animName.Contains("don_normal")) return "normal";
        if (animName.Contains("don_sabi_start")) return "gogostart";
        if (animName.Contains("don_sabi")) return "gogo";
        if (animName.Contains("don_entry_select_loop")) return "songselect_wait_loop";
        if (animName.Contains("don_result_clear_loop")) return "songselect_loop,dan_select_in";
        if (animName.Contains("don_select_loop")) return "result_loop";
        return "skip";
    }

    public static int GetAnimationCount() => _animationCount;
    public static int GetCurrentAnimationIndex() => _currentAnimIndex;
    public static string GetAnimationNameById(int id) { if (id >= 0 && id < _animationCount && _animationsPtr != null) return _animationsPtr[id].NameToString(); return "Unknown"; }
    public static void NextAnimation() { if (!_loaded || _animationCount == 0 || _isExporting) return; _currentAnimIndex = (_currentAnimIndex + 1) % _animationCount; _currentAnimFrame = 0; _animFrameAccumulator = 0.0; IsCurrentAnimFinished = false; }
    public static void PrevAnimation() { if (!_loaded || _animationCount == 0 || _isExporting) return; _currentAnimIndex = (_currentAnimIndex - 1 + _animationCount) % _animationCount; _currentAnimFrame = 0; _animFrameAccumulator = 0.0; IsCurrentAnimFinished = false; }

    public static bool SetAnimationByName(string name)
    {
        if (!_loaded || _animationCount == 0 || _isExporting || string.IsNullOrWhiteSpace(name)) return false;
        string needle = name.ToLowerInvariant();
        for (int i = 0; i < _animationCount; i++)
        {
            if (GetAnimationNameById(i).ToLowerInvariant().Contains(needle))
            {
                _currentAnimIndex = i;
                _currentAnimFrame = 0;
                _animFrameAccumulator = 0.0;
                IsCurrentAnimFinished = false;
                return true;
            }
        }
        return false;
    }

    public static void Unload()
    {
        if (_outlinePassShaderLoaded) { Raylib.UnloadShader(_outlinePassShader); _outlinePassShaderLoaded = false; _outlinePassShader = default; }
        if (_outlineShaderLoaded) { Raylib.UnloadShader(_outlineShader); _outlineShaderLoaded = false; _outlineShader = default; }
        if (_renderBuffer.Id != 0) { Raylib.UnloadRenderTexture(_renderBuffer); _renderBuffer = default; }
        UnloadFaceTexture();
        if (_faceSheetLoaded)
        {
            Raylib.UnloadImage(_faceSheetImage);
            _faceSheetLoaded = false;
        }
        if (_loaded && _model.MeshCount > 0) Raylib.UnloadModel(_model);
        if (_bodyLoaded && _bodyModel.MeshCount > 0) Raylib.UnloadModel(_bodyModel);
        if (_headLoaded && _headModel.MeshCount > 0) Raylib.UnloadModel(_headModel);
        _loaded = false; _model = default;
        _bodyLoaded = false; _bodyModel = default;
        _headLoaded = false; _headModel = default;

        foreach (var kv in _origImgCostume) Raylib.UnloadImage(kv.Value);
        foreach (var kv in _origImgBody) Raylib.UnloadImage(kv.Value);
        foreach (var kv in _origImgHead) Raylib.UnloadImage(kv.Value);
        _origImgCostume.Clear(); _origImgBody.Clear(); _origImgHead.Clear();
    }

    private class SavedConfig
    {
        public int Dons { get; set; } = 0;
        public int BodyId { get; set; } = 0;
        public int HeadId { get; set; } = 0;
        public int FaceId { get; set; } = 0;
        public string DrawMode { get; set; } = "CosOnly";
        public int FaceColorIndex { get; set; } = 8;
        public int BodyColorIndex { get; set; } = 0;
        public int RimColorIndex { get; set; } = 8;
    }

    private static SavedConfig ReadConfigFull()
    {
        try
        {
            if (File.Exists("Don/Config.json"))
            {
                string json = File.ReadAllText("Don/Config.json");
                var cfg = JsonSerializer.Deserialize<SavedConfig>(json);
                if (cfg != null) return cfg;
            }
        }
        catch (Exception ex) { Console.WriteLine("[DonChan3D] ReadConfig Error: " + ex.Message); }
        return new SavedConfig();
    }

    public static void WriteConfig()
    {
        try
        {
            string dir = "Don";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var data = new SavedConfig
            {
                Dons = _costumeId,
                BodyId = _bodyId,
                HeadId = _headId,
                FaceId = _faceId,
                DrawMode = CurrentDrawMode.ToString(),
                FaceColorIndex = _faceColorIndex,
                BodyColorIndex = _bodyColorIndex,
                RimColorIndex = _rimColorIndex,
            };
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(Path.Combine(dir, "Config.json"), JsonSerializer.Serialize(data, options));
        }
        catch (Exception ex) { Console.WriteLine("[DonChan3D] WriteConfig Error: " + ex.Message); }
    }

    public static void LoadConfig()
    {
        var cfg = ReadConfigFull();

        if (Enum.TryParse<DrawMode>(cfg.DrawMode, out var mode)) CurrentDrawMode = mode;

        _faceColorIndex = Math.Clamp(cfg.FaceColorIndex, 0, _sharedColorPalette.Length - 1);
        _bodyColorIndex = Math.Clamp(cfg.BodyColorIndex, 0, _sharedColorPalette.Length - 1);
        _rimColorIndex = Math.Clamp(cfg.RimColorIndex, 0, _sharedColorPalette.Length - 1);

        if (_loaded)
        {
            SetCostumeId(cfg.Dons);
            SetBodyId(cfg.BodyId);
            SetHeadId(cfg.HeadId);
            SetFaceId(cfg.FaceId);
            ApplyDonColorsToAllModels();
        }
        else
        {
            _costumeId = cfg.Dons;
            _bodyId = cfg.BodyId;
            _headId = cfg.HeadId;
            _faceId = cfg.FaceId;
        }
    }
}
using System;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;
using Raylib_cs;

public static unsafe class DonChan3D
{
    // --- costume（完全に独立した衣装モデル） ---
    private static Model _model;
    private static Texture2D _texFace;
    private static Texture2D _texFaceHead; // Head単体表示用：顔絵を縮小し透明余白で中央配置したもの

    // --- body（素体） ---
    private static Model _bodyModel;
    private static bool _bodyLoaded = false;
    private static int _bodyId = 0;

    // --- head（素体） ---
    private static Model _headModel;
    private static bool _headLoaded = false;
    private static int _headId = 0;

    public static int BodyId => _bodyId;
    public static int HeadId => _headId;

    // --- face（表情差分 & 色） ---
    private static int _faceId = 0;
    public static int FaceId => _faceId;

    // face_XXXXXX.png は縦に複数コマが並んだスプライトシート（YataiDON仕様）
    // 各コマは正方形（一辺=画像の幅）で、縦に frameCount 個並んでいる
    private static Image _faceSheetImage;       // CPUメモリ上に保持する元画像（コマ切り出し用）
    private static bool _faceSheetLoaded = false;
    private static int _faceFrameSize = 0;       // 1コマの一辺のピクセル数
    private static int _faceFrameCount = 1;      // シート内のコマ数
    private static int _currentFaceFrame = -1;   // 現在描画中のコマ番号（変化時のみ切り出し直す）
    private static bool _currentFaceMirrored = false;

    // コマ対応表を確認・調整するための手動オーバーライド（[ / ] キー）
    private static float _faceFrameManualOffset = 0f; // 0 = オフセット無し。自動（アニメ連動）の結果にこの値を足しずらす（小数OK）
    private const float FaceFrameManualStep = 0.5f;   // [ / ] 1回あたりのずらし量

    // 顔テクスチャ（目・口の絵）が割り当てられているマテリアル番号
    // モデルごとに異なる（衣装モデルは複数マテリアルを持つ）ため、
    // ロード直後に「元の基本テクスチャが最小サイズ＝顔用プレースホルダー」を自動検出する
    private static int _faceMatIdxCostume = 0;
    private static int _faceMatIdxHead = 0;
    // Head単体エクスポート時、顔プレートが縁取り込みのサイズで作られているため描画時に縮小補正する
    private static float _headFaceScaleFactor = 1.0f;
    private static float _headFaceScaleManualOverride = 0f; // 0 = 自動計算を使う。>0ならその値に固定し、衣装変更でも再計算しない
    private static float _cosFaceScaleManualOverride = 1f; // CosOnly側の顔サイズ手動倍率（1.0=原寸）

    // 色分け対象（extras.shaderType == "taikoEffectChangeColors"）のマテリアル番号一覧
    private static List<int> _recolorMatIdxCostume = new();
    private static List<int> _recolorMatIdxBody = new();
    private static List<int> _recolorMatIdxHead = new();

    // 塗り替え元の「無着色オリジナル画像」をCPU側にキャッシュしておく
    // （毎回オリジナルから塗り直す。塗った後のテクスチャを再度塗るとどんどん劣化するため）
    private static Dictionary<int, Image> _origImgCostume = new();
    private static Dictionary<int, Image> _origImgBody = new();
    private static Dictionary<int, Image> _origImgHead = new();

    // 本家(YataiDON)方式：glbのJSONチャンクを直接パースし、
    // materials[i].extras.shaderType == "taikoEffectFace" と "taikoEffectChangeColors" を判定する。
    // raylibはデフォルトマテリアルをindex 0に追加するため、glTF側のインデックスに+1したものが対応する。
    private static (int faceIdx, List<int> recolorIdx) ParseGlbMaterialInfo(string path, Model mdl)
    {
        var recolorIdx = new List<int>();
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
                    int raylibIdx = glbIdx + 1; // raylibの先頭デフォルトマテリアル分オフセット
                    if (raylibIdx < mdl.MaterialCount &&
                        mat.TryGetProperty("extras", out JsonElement extras) &&
                        extras.TryGetProperty("shaderType", out JsonElement shaderType))
                    {
                        string shader = shaderType.GetString() ?? "";
                        if (shader == "taikoEffectChangeColors") recolorIdx.Add(raylibIdx);
                        else if (shader == "taikoEffectFace" && faceIdx == -1) faceIdx = raylibIdx;
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
            faceIdx = DetectFaceMaterialIndex(mdl); // フォールバック（サイズ推測）
            Console.WriteLine($"[DonChan3D] face material fallback index={faceIdx}");
        }
        else
        {
            Console.WriteLine($"[DonChan3D] face material (glTF extras) index={faceIdx}, recolor materials=[{string.Join(",", recolorIdx)}]");
        }

        return (faceIdx, recolorIdx);
    }

    private static int DetectFaceMaterialIndex(Model mdl)
    {
        int best = 0;
        long bestArea = long.MaxValue;
        bool found = false;

        for (int i = 0; i < mdl.MaterialCount; i++)
        {
            Texture2D tex = mdl.Materials[i].Maps[(int)MaterialMapIndex.Albedo].Texture;
            // テクスチャ未割り当てのマテリアルはraylibの1x1デフォルト白テクスチャが入るため除外する
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

    // 本家の色選択パレット（7行×9列＝63色）。body/face/rimすべてこの共通パレットから選ぶ。
    private static readonly Color[] _sharedColorPalette = new Color[]
    {
        // row0
        new Color((byte)248,(byte)72,(byte)40,(byte)255),  new Color((byte)104,(byte)192,(byte)192,(byte)255), new Color((byte)220,(byte)21,(byte)0,(byte)255),   new Color((byte)248,(byte)240,(byte)224,(byte)255),
        new Color((byte)0,(byte)150,(byte)135,(byte)255),  new Color((byte)0,(byte)191,(byte)135,(byte)255),   new Color((byte)0,(byte)255,(byte)154,(byte)255),  new Color((byte)102,(byte)255,(byte)194,(byte)255),
        new Color((byte)255,(byte)255,(byte)255,(byte)255),
        // row1
        new Color((byte)105,(byte)0,(byte)0,(byte)255),    new Color((byte)255,(byte)0,(byte)0,(byte)255),     new Color((byte)255,(byte)102,(byte)102,(byte)255), new Color((byte)255,(byte)179,(byte)179,(byte)255),
        new Color((byte)0,(byte)188,(byte)194,(byte)255),  new Color((byte)0,(byte)247,(byte)255,(byte)255),   new Color((byte)102,(byte)250,(byte)255,(byte)255), new Color((byte)179,(byte)253,(byte)255,(byte)255),
        new Color((byte)228,(byte)228,(byte)228,(byte)255),
        // row2
        new Color((byte)153,(byte)56,(byte)0,(byte)255),   new Color((byte)255,(byte)94,(byte)0,(byte)255),    new Color((byte)255,(byte)158,(byte)120,(byte)255), new Color((byte)255,(byte)207,(byte)179,(byte)255),
        new Color((byte)0,(byte)81,(byte)153,(byte)255),   new Color((byte)0,(byte)136,(byte)255,(byte)255),   new Color((byte)102,(byte)184,(byte)255,(byte)255), new Color((byte)179,(byte)219,(byte)255,(byte)255),
        new Color((byte)185,(byte)185,(byte)185,(byte)255),
        // row3
        new Color((byte)179,(byte)119,(byte)0,(byte)255),  new Color((byte)255,(byte)170,(byte)0,(byte)255),   new Color((byte)255,(byte)204,(byte)102,(byte)255), new Color((byte)255,(byte)226,(byte)179,(byte)255),
        new Color((byte)0,(byte)12,(byte)128,(byte)255),   new Color((byte)0,(byte)25,(byte)255,(byte)255),    new Color((byte)102,(byte)117,(byte)255,(byte)255), new Color((byte)179,(byte)186,(byte)255,(byte)255),
        new Color((byte)133,(byte)133,(byte)133,(byte)255),
        // row4
        new Color((byte)179,(byte)155,(byte)0,(byte)255),  new Color((byte)255,(byte)221,(byte)0,(byte)255),   new Color((byte)255,(byte)255,(byte)0,(byte)255),   new Color((byte)255,(byte)255,(byte)113,(byte)255),
        new Color((byte)43,(byte)0,(byte)128,(byte)255),   new Color((byte)85,(byte)0,(byte)255,(byte)255),    new Color((byte)153,(byte)102,(byte)255,(byte)255), new Color((byte)204,(byte)179,(byte)255,(byte)255),
        new Color((byte)80,(byte)80,(byte)80,(byte)255),
        // row5
        new Color((byte)56,(byte)161,(byte)0,(byte)255),   new Color((byte)120,(byte)201,(byte)0,(byte)255),   new Color((byte)179,(byte)255,(byte)0,(byte)255),   new Color((byte)220,(byte)255,(byte)138,(byte)255),
        new Color((byte)97,(byte)0,(byte)128,(byte)255),   new Color((byte)196,(byte)0,(byte)255,(byte)255),   new Color((byte)220,(byte)102,(byte)255,(byte)255), new Color((byte)237,(byte)179,(byte)255,(byte)255),
        new Color((byte)35,(byte)35,(byte)35,(byte)255),
        // row6
        new Color((byte)0,(byte)102,(byte)0,(byte)255),    new Color((byte)0,(byte)184,(byte)0,(byte)255),     new Color((byte)0,(byte)255,(byte)0,(byte)255),     new Color((byte)138,(byte)255,(byte)158,(byte)255),
        new Color((byte)153,(byte)0,(byte)89,(byte)255),   new Color((byte)255,(byte)0,(byte)149,(byte)255),   new Color((byte)255,(byte)102,(byte)191,(byte)255), new Color((byte)255,(byte)179,(byte)223,(byte)255),
        new Color((byte)0,(byte)0,(byte)0,(byte)255),
    };
    public const int SharedColorPaletteCols = 9;
    public const int SharedColorPaletteRows = 7;
    public static Color[] SharedColorPalette => _sharedColorPalette;

    private static int _bodyColorIndex = 0;
    private static int _faceColorIndex = 8;  // デフォルトは白（row0の右端）
    private static int _rimColorIndex = 8;   // デフォルトは白
    public static Color BodyColor => _sharedColorPalette[_bodyColorIndex];
    public static Color FaceColor => _sharedColorPalette[_faceColorIndex];
    public static Color RimColor => _sharedColorPalette[_rimColorIndex];

    // --- デバッグ用の表示モード ---
    public enum DrawMode { All, CosOnly, BodyOnly, HeadOnly, BodyHeadOnly }
    public static DrawMode CurrentDrawMode = DrawMode.All;

    private static ModelAnimation* _animationsPtr = null;
    private static int _animationCount = 0;

    private static bool _loaded = false;
    private static int _costumeId = 0;

    private static int _idleAnimIndex = -1;
    private static int _currentAnimIndex = -1;
    private static int _currentAnimFrame = 0;
    public static float PlaybackFps = 120f;
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
    // 縁取りポストプロセス（_outlinePassShader）を合成した「最終出力」バッファ。
    // これをエクスポート＆画面表示の両方で読み取ることで、書き出し画像にも
    // 縁取りが確実に反映されるようにする。
    private static RenderTexture2D _renderBufferFinal;
    // 仮想スクリーンのBeginTextureModeの中からDrawSimpleが呼ばれるときtrueにセットする
    public static bool _insideVirtualFrame = false;

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

    // --- 高速化：PNGのエンコード＆書き込みはバックグラウンドスレッドで行い、
    //     メインループ（GPU描画）をディスクI/Oでブロックしないようにする ---
    private struct ExportWriteJob
    {
        public Image Image;
        public List<string> FilePaths;
    }
    private static readonly BlockingCollection<ExportWriteJob> _exportWriteQueue = new(boundedCapacity: 64);
    private static Thread[] _exportWriteWorkers;
    private static bool _exportWorkersStarted = false;
    private static int _pendingExportWrites = 0;
    private static readonly HashSet<string> _createdExportDirs = new();
    private const int NormalModeFps = 120; // 通常操作時のFPS（書き出し終了後に復元）

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

    private static void EnsureRenderBufferSize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (_renderBuffer.Id != 0 && _renderBuffer.Texture.Width == width && _renderBuffer.Texture.Height == height)
            return;

        if (_renderBuffer.Id != 0) Raylib.UnloadRenderTexture(_renderBuffer);
        if (_renderBufferFinal.Id != 0) Raylib.UnloadRenderTexture(_renderBufferFinal);

        _renderBuffer = Raylib.LoadRenderTexture(width, height);
        Raylib.SetTextureWrap(_renderBuffer.Texture, TextureWrap.Clamp);
        Raylib.SetTextureFilter(_renderBuffer.Texture, TextureFilter.Bilinear);

        _renderBufferFinal = Raylib.LoadRenderTexture(width, height);
        Raylib.SetTextureWrap(_renderBufferFinal.Texture, TextureWrap.Clamp);
        Raylib.SetTextureFilter(_renderBufferFinal.Texture, TextureFilter.Bilinear);
    }

    // 🖊️ メッシュ押し出し方式（_outlineShader）の太さ（ローカル空間単位）
    private static float _outlineThickness = 0.0018f;
    public static float OutlineThickness
    {
        get => _outlineThickness;
        set { _outlineThickness = value; ApplyOutlineThickness(); }
    }

    // 足専用の縁取り太さ
    private static float _bodyOutlineThickness = 0.005f;
    public static float BodyOutlineThickness
    {
        get => _bodyOutlineThickness;
        set { _bodyOutlineThickness = value; }
    }

    private static void ApplyOutlineThickness()
    {
        SetOutlineShaderThickness(_outlineThickness);
    }

    private static void SetOutlineShaderThickness(float thickness)
    {
        if (!_outlineShaderLoaded) return;
        Raylib.SetShaderValue(_outlineShader, _outlineThicknessLoc, &thickness, ShaderUniformDataType.Float);
    }

    public static void Init()
    {
        _costumeId = ReadConfig();
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

        // 頂点を法線方向に押し出してからmvpを掛けるアウトラインシェーダー（normalizeで太さを均一化）
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

        // 16方向（22.5度刻み）サンプリングによるポストプロセス外縁取りシェーダー
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
            float passThickness = 3.5f;
            Raylib.SetShaderValue(_outlinePassShader, _outlinePassThicknessLoc, &passThickness, ShaderUniformDataType.Float);
        }

        StartApiServer();
    }

    private static void LoadAnimationsOnce()
    {
        string animPath = "Don/animations.glb";
        if (File.Exists(animPath) && _animationsPtr == null)
        {
            int count = 0;
            _animationsPtr = Raylib.LoadModelAnimations(animPath, ref count);
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
    }

    // 法線データが無いメッシュ（脚など単純ジオメトリでNORMALが省略されている場合がある）に
    // 面法線を自前生成して補う。無いままだと輪郭シェーダーの押し出し量が常に0になり、輪郭が一切付かない。
    private static unsafe void EnsureMeshNormals(Model mdl, string label)
    {
        for (int i = 0; i < mdl.MeshCount; i++)
        {
            var mesh = mdl.Meshes[i];
            if (mesh.Normals != null || mesh.VertexCount <= 0 || mesh.Vertices == null) continue;

            Console.WriteLine($"[DonChan3D] {label} mesh[{i}] に法線が無いため生成します（verts={mesh.VertexCount}）");

            float[] normals = new float[mesh.VertexCount * 3]; // 0初期化済み

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
    }

    // 各メッシュの頂点数・法線有無・所属マテリアルをログ出力する（輪郭が付かないメッシュの切り分け用）
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
        EnsureMeshNormals(_model, "costume");
        ClearVertexColorsToWhite(_model);

        var (faceIdx, recolorIdx) = ParseGlbMaterialInfo(modelPath, _model);
        _faceMatIdxCostume = faceIdx;
        _recolorMatIdxCostume = recolorIdx;
        CacheOriginalRecolorImages(_model, _recolorMatIdxCostume, _origImgCostume);
        ApplyDonColorsToAllModels();

        LoadFaceSheet(false);

        ApplyFaceTextureToModel();

        Console.WriteLine($"[DonChan3D] Costume MeshCount={_model.MeshCount}");
        LogMeshDiagnostics("costume", _model);
        LogFaceMeshCandidates(_model, _faceMatIdxCostume, "cos");

        // 衣装を変えても今再生中のアニメ・フレームはそのまま維持する
        // （idleへ強制リセットすると頭ボーンの姿勢が変わり、顔が大きく/小さく見えることがあるため）
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

        var (_, recolorIdx) = ParseGlbMaterialInfo(path, _bodyModel);
        _recolorMatIdxBody = recolorIdx;
        CacheOriginalRecolorImages(_bodyModel, _recolorMatIdxBody, _origImgBody);
        ApplyDonColorsToAllModels();

        Console.WriteLine($"[DonChan3D] Body loaded: {path} (MeshCount={_bodyModel.MeshCount})");
        LogMeshDiagnostics("body", _bodyModel);
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

        var (faceIdx, recolorIdx) = ParseGlbMaterialInfo(path, _headModel);
        _faceMatIdxHead = faceIdx;
        _recolorMatIdxHead = recolorIdx;
        CacheOriginalRecolorImages(_headModel, _recolorMatIdxHead, _origImgHead);
        ApplyDonColorsToAllModels();

        ApplyFaceTextureToHead();
        Console.WriteLine($"[DonChan3D] Head loaded: {path} (MeshCount={_headModel.MeshCount})");
        LogFaceMeshCandidates(_headModel, _faceMatIdxHead, "head");
        ComputeHeadFaceScaleCorrection();
    }

    // Head単体の顔メッシュはcosの顔メッシュより縁取り分だけ大きく作られているため、
    // 両者のバウンディングボックスサイズを比較して縮小倍率を求める。
    private static void ComputeHeadFaceScaleCorrection()
    {
        // 手動固定中は衣装変更で再ロードされても上書きしない
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

        if (_model.MeshCount == 0 || _faceMatIdxCostume < 0) return; // cos未ロード時は補正なし
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

    // --- 顔スケール手動固定（Up/Downで衣装を変えても崩れないようにする） ---
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
        ComputeHeadFaceScaleCorrection(); // 自動計算に戻す
    }

    // --- CosOnly側の顔サイズ手動倍率（衣装ごとにメッシュサイズが違っても見た目を揃える用） ---
    public static float CosFaceScale => _cosFaceScaleManualOverride;

    public static void SetCosFaceScale(float value)
    {
        _cosFaceScaleManualOverride = Math.Clamp(value, 0.05f, 3.0f);
        _currentFaceFrame = -1; // 強制的に切り出し直させる
    }

    public static void ResetCosFaceScale()
    {
        _cosFaceScaleManualOverride = 1.0f;
        _currentFaceFrame = -1;
    }

    // faceMatIdxと同じマテリアルを持つメッシュが複数無いか・サイズはどうかを確認するための一時デバッグ用
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

    // Don/face/face_XXXXXX.png を読み込む（YataiDONと同じ命名規則）
    // ミラーアニメ再生中は face_XXXXXXr.png（存在すれば）を使う
    private static void LoadFaceSheet(bool mirrored)
    {
        if (_faceSheetLoaded) { Raylib.UnloadImage(_faceSheetImage); _faceSheetLoaded = false; }

        string suffix = mirrored ? "r" : "";
        string facePath = $@"Don\face\face_{_faceId:D6}{suffix}.png";
        if (mirrored && !File.Exists(facePath)) facePath = $@"Don\face\face_{_faceId:D6}.png"; // ミラー版が無ければ通常版で代用
        if (!File.Exists(facePath)) facePath = @"Don\face\face_000000.png";
        if (!File.Exists(facePath)) return;

        _faceSheetImage = Raylib.LoadImage(facePath);
        _faceSheetLoaded = true;
        _currentFaceMirrored = mirrored;

        // シートは縦積みの正方形コマ（幅=1コマの一辺）
        _faceFrameSize = _faceSheetImage.Width;
        _faceFrameCount = (_faceFrameSize > 0) ? Math.Max(1, _faceSheetImage.Height / _faceFrameSize) : 1;
        _currentFaceFrame = -1; // 強制的に切り出し直させる
    }

    // --- 本家 animation.json 由来の表情コマ対応表 ---
    private class FaceKeyframe { public float Start, End; public int Frame; }
    private class FaceAnimDef { public List<FaceKeyframe> Keys = new(); public bool Loop; }

    private static readonly Dictionary<string, FaceAnimDef> _faceAnimTable = new();
    private static bool _faceAnimTableLoaded = false;

    // 本家アニメ名 と こちらのDon/animations.glb側の名前が食い違うものだけの対応表
    // （本家 chara_3d.cpp の FACE_ANIM_IDS[] で個別に割り当てられていたもの）
    private static readonly Dictionary<string, string> _faceAnimAlias = new()
    {
        { "don_bind", "don_wait_loop" },
        { "don_fukkatu_loop", "don_kusu_loop" },
        { "don_fukkatu_start", "don_kusu_in" },
        { "don_general_jump", "don_swing02" },
        { "don_general_loop", "don_norm_loop" },
    };

    // 実際に出力（Export）して目視確認したコマ列。animation.json側は汎用HUD用の使い回しで
    // プレースホルダー（固定コマ・ループのみ等）になっている項目が多く信用できないため、
    // ここに載っているフォルダ名（GetMappedFolderName()の戻り値）はこちらを優先する。
    // 値が複数ある場合はアニメ進行度（0〜100%）に応じて等分割で順番に切り替える。
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

    // エクスポート先ごとの必要枚数。アニメーション本体のKeyFrameCountとは独立に
    // 出力枚数を合わせる（同一アニメーションから複数フォルダへ出す場合にも対応）。
    private static readonly Dictionary<string, int> _exportFrameCountOverride = new(StringComparer.OrdinalIgnoreCase)
    {
        { "10combo", 175 }, { "10combomax", 255 },
        { "balloon_breaking", 39 }, { "balloon_broke", 123 }, { "balloon_miss", 95 },
        { "clear", 236 }, { "clearin", 175 }, { "combocut", 235 },
        { "dan_select_in", 275 }, { "entry_loop", 180 }, { "failed_down", 175 },
        { "failed_loop", 117 }, { "gogo", 478 }, { "gogostart", 215 },
        { "miss_loop", 117 }, { "normal", 471 },
        { "result_clear_loop", 156 }, { "result_failed_in", 56 },
        { "result_failed_loop", 117 }, { "songselect_loop", 236 },
        { "songselect_wait_loop", 134 }, { "soulin", 279 },
    };

    // Don/animation.json（本家 Graphics/global/animation.json 相当）を読み込む
    private static void LoadFaceAnimTable()
    {
        _faceAnimTableLoaded = true;
        _faceAnimTable.Clear();

        string path = "Don/animation.json";
        if (!File.Exists(path))
        {
            Console.WriteLine("[DonChan3D] Don/animation.json が見つからないため、口パクの簡易ロジックにフォールバックします");
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

                // "don_balloon1P_failure face" -> "don_balloon_failure"（1P/2P表記を除去して正規化）
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
            Console.WriteLine($"[DonChan3D] Don/animation.json の読み込みに失敗: {ex.Message}");
        }
    }

    // アニメーション状態から表示すべき表情コマ番号を決める
    // animation.json の対応表を最優先し、無ければ簡易な口パクにフォールバックする
    // [ / ] キーで手動確認・上書き可能
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

    // 自動判定（animation.json対応表 / 実測オーバーライド / 簡易口パク）でのコマ番号
    private static int GetAutoFaceFrame()
    {
        int totalFramesForOverride = (_currentAnimIndex >= 0 && _currentAnimIndex < _animationCount && _animationsPtr != null)
            ? _animationsPtr[_currentAnimIndex].KeyFrameCount : 1;
        int animFrameNow = _isExporting ? _exportFrameCounter : _currentAnimFrame;
        float progress01ForOverride = totalFramesForOverride > 1 ? (float)animFrameNow / (totalFramesForOverride - 1) : 0f;

        // 実測で確定済みのコマ列があれば最優先（GetMappedFolderName()のフォルダ名で引く）
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
            float t = progress01 * 1000f; // JSONは0〜1000スケール（=0〜100.0%）

            for (int i = 0; i < def.Keys.Count; i++)
            {
                var k = def.Keys[i];
                bool isLast = (i == def.Keys.Count - 1);
                if (t >= k.Start && (t < k.End || isLast))
                    return Math.Clamp(k.Frame, 0, _faceFrameCount - 1);
            }
            return Math.Clamp(def.Keys[def.Keys.Count - 1].Frame, 0, _faceFrameCount - 1);
        }

        // 対応表に無いアニメ（koshibai等の未収録データ）は簡易な口パクにフォールバック
        return Math.Clamp((animFrameNow / 6) % 2, 0, _faceFrameCount - 1);
    }

    // --- 手動コマ送り（確認用） ---
    public static int FaceFrameCount => _faceFrameCount;
    public static int CurrentFaceFrame => _currentFaceFrame;
    // 0以外なら手動オフセット中（自動判定にこの値を足しずらす＝アニメには追従し続ける）
    public static bool IsFaceFrameManual => _faceFrameManualOffset != 0f;
    public static float FaceFrameManualOffset => _faceFrameManualOffset;

    public static void NextFaceFrameManual()
    {
        if (_faceFrameCount <= 1) return;
        _faceFrameManualOffset += FaceFrameManualStep;
        // -N/2〜+N/2の範囲に正規化（floatのままmodする）
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

    // 現在の状態（表情コマ・ミラー有無）に応じてテクスチャを切り出し直す
    private static void UpdateFaceCrop()
    {
        bool wantMirror = CurrentAnimName.Contains("mirror", StringComparison.OrdinalIgnoreCase);

        if (!_faceSheetLoaded || wantMirror != _currentFaceMirrored)
        {
            LoadFaceSheet(wantMirror);
        }

        if (!_faceSheetLoaded) return;

        int frame = GetFaceFrameForCurrentState();
        if (frame == _currentFaceFrame) return; // コマが変わっていなければ切り出し直さない

        UnloadFaceTexture();

        Rectangle crop = new Rectangle(0, frame * _faceFrameSize, _faceFrameSize, _faceFrameSize);

        if (_cosFaceScaleManualOverride != 1.0f)
        {
            // Head側と同じ手法：絵柄だけ縮小して透明余白付きで中央に配置＝メッシュ側のUVは変えずに見た目だけ縮小できる
            Image cosCanvas = Raylib.GenImageColor(_faceFrameSize, _faceFrameSize, Color.Blank);
            int cosScaledSize = Math.Max(1, (int)MathF.Round(_faceFrameSize * _cosFaceScaleManualOverride));
            Image frameImgForCos = Raylib.ImageFromImage(_faceSheetImage, crop);
            Raylib.ImageResize(ref frameImgForCos, cosScaledSize, cosScaledSize);
            int cosOffset = (_faceFrameSize - cosScaledSize) / 2;
            Raylib.ImageDraw(ref cosCanvas, frameImgForCos,
                new Rectangle(0, 0, cosScaledSize, cosScaledSize),
                new Rectangle(cosOffset, cosOffset, cosScaledSize, cosScaledSize),
                Color.White);
            _texFace = Raylib.LoadTextureFromImage(cosCanvas);
            Raylib.UnloadImage(frameImgForCos);
            Raylib.UnloadImage(cosCanvas);
        }
        else
        {
            Image frameImg0 = Raylib.ImageFromImage(_faceSheetImage, crop);
            _texFace = Raylib.LoadTextureFromImage(frameImg0);
            Raylib.UnloadImage(frameImg0);
        }
        Raylib.SetTextureFilter(_texFace, TextureFilter.Bilinear);

        // Head単体表示用：顔プレートがcosより縁取り分だけ大きく作られているため、
        // メッシュ側は触らず、絵柄だけ_headFaceScaleFactor倍に縮小して透明余白付きで中央に置く。
        // ここが失敗しても表情アニメーション自体は止めない（等倍のcos用テクスチャにフォールバック）。
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
            _texFaceHead = Raylib.LoadTextureFromImage(headCanvas);
            Raylib.SetTextureFilter(_texFaceHead, TextureFilter.Bilinear);
            Raylib.UnloadImage(frameImgForHead);
            Raylib.UnloadImage(headCanvas);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DonChan3D] Head face texture generation failed, falling back to unscaled: {ex.Message}");
            Image fallbackImg = Raylib.ImageFromImage(_faceSheetImage, crop);
            _texFaceHead = Raylib.LoadTextureFromImage(fallbackImg); // 等倍でフォールバック（別テクスチャとして確保）
            Raylib.SetTextureFilter(_texFaceHead, TextureFilter.Bilinear);
            Raylib.UnloadImage(fallbackImg);
        }

        _currentFaceFrame = frame; // ここは常に更新する（Head側の生成に失敗してもコマ送りは継続させる）
    }

    // 顔の表情テクスチャは常に白（無着色）で貼る＝目や口の色を潰さないため
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

    // 本家 recolor_texture() の移植：RGBのどれが一番強いかでピクセルを分類し、
    // 青が強い＝rim（縁取り）、緑が強い＝face（顔プレート）、それ以外＝body として塗り替える。
    // 元テクスチャはRGBをそのまま「どの部位か」を示すマスクとして使っている点に注意（見た目の色ではない）。
    private static unsafe Texture2D RecolorTexture(Image source, Color body, Color face, Color rim)
    {
        Image img = Raylib.ImageCopy(source);
        Raylib.ImageFormat(ref img, PixelFormat.UncompressedR8G8B8A8);

        byte* pixels = (byte*)img.Data;
        int total = img.Width * img.Height;

        for (int i = 0; i < total; i++)
        {
            float r = pixels[i * 4 + 0] / 255f;
            float g = pixels[i * 4 + 1] / 255f;
            float b = pixels[i * 4 + 2] / 255f;

            float strongest = MathF.Max(r, MathF.Max(g, b));
            float weakest = MathF.Min(r, MathF.Min(g, b));
            if (strongest <= 0.05f || (strongest - weakest) <= 0.08f) continue; // 無彩色（縫い目の黒線等）は塗り替えない

            Color outc;
            if (b > r && b >= g) outc = rim;
            else if (g > r && g > b) outc = face;
            else outc = body;

            pixels[i * 4 + 0] = outc.R;
            pixels[i * 4 + 1] = outc.G;
            pixels[i * 4 + 2] = outc.B;
        }

        Texture2D tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
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

    // costume / body / head すべてに現在のbody/face/rim色を適用する
    private static void ApplyDonColorsToAllModels()
    {
        Color body = BodyColor, face = FaceColor, rim = RimColor;
        if (_loaded) ApplyDonColorsToModel(_model, _recolorMatIdxCostume, _origImgCostume, body, face, rim);
        if (_bodyLoaded) ApplyDonColorsToModel(_bodyModel, _recolorMatIdxBody, _origImgBody, body, face, rim);
        if (_headLoaded) ApplyDonColorsToModel(_headModel, _recolorMatIdxHead, _origImgHead, body, face, rim);
    }

    // モデルロード直後に、色分け対象マテリアルの「無着色オリジナル画像」をCPUにキャッシュしておく
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

    // 顔ID変更（F3の数字入力から呼ばれる）
    public static void SetFaceId(int id)
    {
        if (id < 0) id = 0;
        _faceId = id;
        LoadFaceSheet(_currentFaceMirrored);
        ApplyFaceTextureToModel();
        ApplyFaceTextureToHead();
    }

    // 顔色パレット送り（F4）
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

    // 体色パレット送り（F6）
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

    // 縁取り(リム)色パレット送り（F7）
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

    // パレットグリッドから直接クリックで色を選ぶ用
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

        ProcessApiQueue();

        _camera3D.Position = new Vector3(CamPosX, CamPosY, CamPosZ);
        _camera3D.Target = new Vector3(0f, TarPosY, 0f);
        _camera3D.FovY = CamFovY;

        if (_currentAnimIndex >= 0 && _currentAnimIndex < _animationCount && _animationsPtr != null)
        {
            int total = _animationsPtr[_currentAnimIndex].KeyFrameCount;
            int frameToApply = _currentAnimFrame;

            if (_isExporting) frameToApply = GetExportSourceFrame(_exportFrameCounter, total, _exportTotalFrames);
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
                            int loopLen = Math.Max(1, total - 1);
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

            Raylib.UpdateModelAnimation(_model, _animationsPtr[_currentAnimIndex], frameToApply);
            if (_bodyLoaded) Raylib.UpdateModelAnimation(_bodyModel, _animationsPtr[_currentAnimIndex], frameToApply);
            if (_headLoaded) Raylib.UpdateModelAnimation(_headModel, _animationsPtr[_currentAnimIndex], frameToApply);

            ApplyFaceTextureToModel();
            ApplyFaceTextureToHead();

            if (_isExporting)
            {
                DrawSimple();
                ExecuteFrameExport();
                _exportFrameCounter++;

                if (_exportFrameCounter >= _exportTotalFrames)
                {
                    if (_isBatchMode) MoveToNextBatchAnimation();
                    else { _isExporting = false; Raylib.SetTargetFPS(NormalModeFps); }
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
        string mappedFolders = GetMappedFolderName();
        if (mappedFolders == "skip") return;

        ClearMappedExportFolders(mappedFolders);

        _isBatchMode = false;
        _exportTotalFrames = GetExportFrameCount(mappedFolders, _animationsPtr[_currentAnimIndex].KeyFrameCount);

        _exportFrameCounter = 0;
        _currentAnimFrame = 0;

        _createdExportDirs.Clear();
        Raylib.SetTargetFPS(0); // 書き出し中はFPS上限を解除して最速で回す

        ResetRenderBuffersForExport();
        _isExporting = true;
    }

    public static void StartBatchExport()
    {
        if (!_loaded || _animationCount == 0 || _isExporting || _animationsPtr == null) return;
        _createdExportDirs.Clear();
        Raylib.SetTargetFPS(0); // 書き出し中はFPS上限を解除して最速で回す
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
                Raylib.SetTargetFPS(NormalModeFps);
                break;
            }

            string name = GetAnimationNameById(_currentAnimIndex);
            if (name.Contains("mirror", StringComparison.OrdinalIgnoreCase)) continue;

            string folderName = GetMappedFolderName();
            if (folderName != "skip")
            {
                _exportTotalFrames = GetExportFrameCount(folderName, _animationsPtr[_currentAnimIndex].KeyFrameCount);
                _exportFrameCounter = 0; _currentAnimFrame = 0;
                ResetRenderBuffersForExport();
                break;
            }
        }
    }

    private static void DrawModelMeshesOutline(Model mdl, Matrix4x4 transform, int faceMatIdx, int skipMeshIndex = -1, bool forceDepthDisabled = false, float? overrideThickness = null)
    {
        if (!_outlineMaterialReady || mdl.MaterialCount == 0) return;

        bool needThicknessOverride = overrideThickness.HasValue;
        if (needThicknessOverride) SetOutlineShaderThickness(overrideThickness.Value);

        Rlgl.SetCullFace(RL_CULL_FACE_FRONT);
        try
        {
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
            if (needThicknessOverride) SetOutlineShaderThickness(_outlineThickness);
        }
    }

    private static void DrawModelMeshesExcludingFace(Model mdl, Matrix4x4 transform, int faceMatIdx, int skipMeshIndex = -1)
    {
        for (int i = 0; i < mdl.MeshCount; i++)
        {
            if (i == skipMeshIndex) continue;
            int matIdx = mdl.MeshMaterial[i];
            if (matIdx == faceMatIdx) continue; // 輪郭用の深度書き込みに顔メッシュを混ぜない
            Raylib.DrawMesh(mdl.Meshes[i], mdl.Materials[matIdx], transform);
        }
    }

    private static void DrawModelMeshes(Model mdl, Matrix4x4 transform, int faceMatIdx, int skipMeshIndex = -1, bool ignoreDepthTestForFace = true)
    {
        // 通常メッシュを先に描き、顔マテリアルを持つメッシュは後回しにする
        // （深度が同じ/近いメッシュに埋もれて顔が消えるのを防ぐ）
        for (int i = 0; i < mdl.MeshCount; i++)
        {
            if (i == skipMeshIndex) continue;
            int matIdx = mdl.MeshMaterial[i];
            if (matIdx == faceMatIdx) continue;
            Raylib.DrawMesh(mdl.Meshes[i], mdl.Materials[matIdx], transform);
        }

        Rlgl.DisableDepthMask();  // 顔は最前面に上書き合成（デプス書き込みなし）
        // depthテストの無視は「衣装に埋め込まれた顔」でのみ有効にする。
        // bodyheadモードの頭部モデルは、髪の毛/フードに隠れる部分がdepthテストで
        // 正しく隠れることで顔プレートが正しいサイズに見えているため、無効化すると
        // 隠れるべき部分まで全部前面に出てきて顔が異常に大きく見えてしまう。
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
        // エクスポート用。常にフル解像度で描く。
        DrawSimple(new Rectangle(0, 0, 820, 599), forceHighQuality: true);
    }

    public static void DrawSimple(Rectangle dest) => DrawSimple(dest, forceHighQuality: false);

    public static void DrawSimple(Rectangle dest, bool forceHighQuality)
    {
        if (!_loaded) return;

        float baseScale = 0.93f;
        float baseOffsetY = -0.01f;

        float rotRadY = -ModelRotY * MathF.PI / 180f;
        Matrix4x4 meshTransform = Matrix4x4.CreateScale(baseScale)
            * Matrix4x4.CreateRotationY(rotRadY)
            * Matrix4x4.CreateTranslation(0f, baseOffsetY, 0f);

        bool drawCos = (CurrentDrawMode == DrawMode.CosOnly || CurrentDrawMode == DrawMode.All);
        bool drawBody = (CurrentDrawMode == DrawMode.BodyOnly || CurrentDrawMode == DrawMode.BodyHeadOnly || CurrentDrawMode == DrawMode.All);
        bool drawHead = (CurrentDrawMode == DrawMode.HeadOnly || CurrentDrawMode == DrawMode.BodyHeadOnly || CurrentDrawMode == DrawMode.All);

        float qScale = (!forceHighQuality && RenderQuality == QualityLevel.Low) ? 0.5f : 1f;
        EnsureRenderBufferSize(
            (int)MathF.Round(dest.Width * qScale),
            (int)MathF.Round(dest.Height * qScale));

        if (_insideVirtualFrame) Raylib.EndTextureMode();
        Raylib.BeginTextureMode(_renderBuffer);
        Raylib.ClearBackground(Color.Blank);
        Raylib.BeginBlendMode(BlendMode.Alpha);
        Raylib.BeginMode3D(_camera3D);
        _model.Transform = Matrix4x4.Identity;

        if (drawCos) DrawModelMeshesOutline(_model, meshTransform, _faceMatIdxCostume);
        if (drawBody && _bodyLoaded) DrawModelMeshesOutline(_bodyModel, meshTransform, -1, forceDepthDisabled: true, overrideThickness: _bodyOutlineThickness);
        if (drawHead && _headLoaded) DrawModelMeshesOutline(_headModel, meshTransform, _faceMatIdxHead);

        if (drawCos) DrawModelMeshes(_model, meshTransform, _faceMatIdxCostume, ignoreDepthTestForFace: false);
        if (drawBody && _bodyLoaded) DrawModelMeshes(_bodyModel, meshTransform, -1);
        if (drawHead && _headLoaded) DrawModelMeshes(_headModel, meshTransform, _faceMatIdxHead, ignoreDepthTestForFace: false);

        Raylib.EndMode3D();
        Raylib.EndBlendMode();
        Raylib.EndTextureMode();
        // NOTE: Program.BeginVirtualFrameが実装されていれば有効にする
        // if (_insideVirtualFrame) Program.BeginVirtualFrame();
        Rectangle src = new Rectangle(0, 0, _renderBuffer.Texture.Width, -_renderBuffer.Texture.Height);

        // 縁取りポストプロセスは「今たまたま束縛されているフレームバッファ」に直接描くのではなく、
        // 専用の_renderBufferFinalに一度焼き込む。こうすることでエクスポート時に読み取る
        // テクスチャにも縁取りが必ず反映される（画面プレビューはその結果を再度貼るだけ）。
        Raylib.BeginTextureMode(_renderBufferFinal);
        Raylib.ClearBackground(Color.Blank);
        Raylib.BeginBlendMode(BlendMode.CustomSeparate);
        if (_outlinePassShaderLoaded)
        {
            Raylib.BeginShaderMode(_outlinePassShader);
            Raylib.DrawTexturePro(_renderBuffer.Texture, src, new Rectangle(0, 0, _renderBuffer.Texture.Width, _renderBuffer.Texture.Height), Vector2.Zero, 0f, Color.White);
            Raylib.EndShaderMode();
        }
        else
        {
            Raylib.DrawTexturePro(_renderBuffer.Texture, src, new Rectangle(0, 0, _renderBuffer.Texture.Width, _renderBuffer.Texture.Height), Vector2.Zero, 0f, Color.White);
        }
        Raylib.EndBlendMode();
        Raylib.EndTextureMode();

        // 合成済みの最終テクスチャを実際の表示先（画面など）へ描画する
        Rectangle finalSrc = new Rectangle(0, 0, _renderBufferFinal.Texture.Width, -_renderBufferFinal.Texture.Height);
        Raylib.DrawTexturePro(_renderBufferFinal.Texture, finalSrc, dest, Vector2.Zero, 0f, Color.White);
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

    // エクスポート先のルートパス： backfiles/Export/model{costumeId}
    // （現在のcostumeIdはエクスポート中は変更不可なので、そのまま使ってよい）
    public static string GetExportRootDir() => Path.Combine("backfiles", "Export", $"model{_costumeId}");

    private static void EnsureExportWorkersStarted()
    {
        if (_exportWorkersStarted) return;
        _exportWorkersStarted = true;

        int workerCount = Math.Clamp(Environment.ProcessorCount - 1, 2, 6);
        _exportWriteWorkers = new Thread[workerCount];
        for (int i = 0; i < workerCount; i++)
        {
            var t = new Thread(ExportWriteWorkerLoop) { IsBackground = true, Name = $"ExportWriter{i}" };
            t.Start();
            _exportWriteWorkers[i] = t;
        }
    }

    // バックグラウンドワーカー：PNGのエンコードとディスク書き込みだけを行う（GPU操作は一切しない）
    private static void ExportWriteWorkerLoop()
    {
        foreach (var job in _exportWriteQueue.GetConsumingEnumerable())
        {
            try
            {
                string firstPath = job.FilePaths[0];
                Raylib.ExportImage(job.Image, firstPath);

                // 同じフレームを複数フォルダへ出す場合は再エンコードせずファイルコピーで済ませる
                for (int i = 1; i < job.FilePaths.Count; i++)
                {
                    try { File.Copy(firstPath, job.FilePaths[i], overwrite: true); }
                    catch (Exception exCopy) { Console.WriteLine($"[Export] copy failed: {exCopy.Message}"); }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Export] write failed: {ex.Message}");
            }
            finally
            {
                Raylib.UnloadImage(job.Image);
                Interlocked.Decrement(ref _pendingExportWrites);
            }
        }
    }

    private static void EnsureExportDirCreated(string dirPath)
    {
        if (_createdExportDirs.Contains(dirPath)) return;
        Directory.CreateDirectory(dirPath);
        _createdExportDirs.Add(dirPath);
    }

    // キュー中の書き込みが全部終わるまで待つ（zip作成やフォルダ削除の前に必須）
    private static void WaitForExportWritesToFinish()
    {
        while (Interlocked.CompareExchange(ref _pendingExportWrites, 0, 0) > 0)
        {
            Thread.Sleep(2);
        }
    }

    private static void ClearMappedExportFolders(string mappedFolders)
    {
        WaitForExportWritesToFinish();
        string exportRoot = GetExportRootDir();
        foreach (string folder in mappedFolders.Split(','))
        {
            string trimmedFolder = folder.Trim();
            if (trimmedFolder.Length == 0) continue;
            string dirPath = Path.Combine(exportRoot, trimmedFolder);
            if (Directory.Exists(dirPath)) Directory.Delete(dirPath, recursive: true);
        }
    }

    private static void ExecuteFrameExport()
    {
        string folderName = GetMappedFolderName();
        if (folderName == "skip") return;

        EnsureExportWorkersStarted();

        string exportRoot = GetExportRootDir();
        string[] folders = folderName.Split(',');
        var filePaths = new List<string>(folders.Length);
        foreach (var folder in folders)
        {
            string trimmedFolder = folder.Trim();
            if (_exportFrameCountOverride.TryGetValue(trimmedFolder, out int folderFrameCount) &&
                _exportFrameCounter >= folderFrameCount)
                continue;

            string dirPath = Path.Combine(exportRoot, trimmedFolder);
            EnsureExportDirCreated(dirPath);
            filePaths.Add(Path.Combine(dirPath, $"{_exportFrameCounter}.png"));
        }

        if (filePaths.Count == 0) return;

        // GPUからの読み出し（LoadImageFromTexture）はメインスレッドで1回だけ行い、
        // エンコード＆書き込みはバックグラウンドスレッドへ渡してすぐ次のフレームへ進む
        // ※縁取りポストプロセスまで合成済みの_renderBufferFinalから読み取る
        Image img = Raylib.LoadImageFromTexture(_renderBufferFinal.Texture);
        Raylib.ImageFlipVertical(ref img);

        Interlocked.Increment(ref _pendingExportWrites);
        _exportWriteQueue.Add(new ExportWriteJob { Image = img, FilePaths = filePaths });
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
        if (string.Equals(animName, "don_result_failure_loop", StringComparison.OrdinalIgnoreCase)) return "result_failed_loop";
        if (string.Equals(animName, "don_result_failure", StringComparison.OrdinalIgnoreCase)) return "result_failed_in";
        if (string.Equals(animName, "don_result_clear_loop", StringComparison.OrdinalIgnoreCase)) return "songselect_loop,dan_select_in";
        if (string.Equals(animName, "don_result_clear", StringComparison.OrdinalIgnoreCase)) return "result_clear_loop";
        if (string.Equals(animName, "don_result_loop", StringComparison.OrdinalIgnoreCase)) return "result_loop";
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
        return "skip";
    }

    private static int GetExportFrameCount(string mappedFolders, int sourceFrameCount)
    {
        int fallback = Math.Max(1, sourceFrameCount);
        int result = 0;
        foreach (string folder in mappedFolders.Split(','))
        {
            if (_exportFrameCountOverride.TryGetValue(folder.Trim(), out int count))
                result = Math.Max(result, count);
        }
        return result > 0 ? result : fallback;
    }

    private static int GetExportSourceFrame(int outputFrame, int sourceFrameCount, int outputFrameCount)
    {
        if (sourceFrameCount <= 1 || outputFrameCount <= 1) return 0;
        // モーションデータは60f基準だが、再生時間は120f基準にする。
        // 出力枚数は変えず、120fの時間軸へ均等配置する（例: 155枚/120f）。
        const float ExportTimeScale = 2.0f;
        float virtualFrame = outputFrame * (sourceFrameCount * ExportTimeScale - 1f) / (outputFrameCount - 1f);
        int sourceFrame = (int)MathF.Floor(virtualFrame / ExportTimeScale);
        return Math.Clamp(sourceFrame, 0, sourceFrameCount - 1);
    }

    public static int GetAnimationCount() => _animationCount;
    public static int GetCurrentAnimationIndex() => _currentAnimIndex;
    public static string GetAnimationNameById(int id) { if (id >= 0 && id < _animationCount && _animationsPtr != null) return _animationsPtr[id].NameToString(); return "Unknown"; }
    public static void NextAnimation() { if (!_loaded || _animationCount == 0 || _isExporting) return; _currentAnimIndex = (_currentAnimIndex + 1) % _animationCount; _currentAnimFrame = 0; _animFrameAccumulator = 0.0; IsCurrentAnimFinished = false; }
    public static void PrevAnimation() { if (!_loaded || _animationCount == 0 || _isExporting) return; _currentAnimIndex = (_currentAnimIndex - 1 + _animationCount) % _animationCount; _currentAnimFrame = 0; _animFrameAccumulator = 0.0; IsCurrentAnimFinished = false; }

    // 名前（部分一致・大文字小文字無視）でアニメーションを切り替える。見つからなければfalseを返す。
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
                return true;
            }
        }
        return false;
    }

    // ==========================================================================
    // どんちゃん生成HTTP API（常駐サーバー。ウィンドウ表示と並行して裏で受け付ける）
    //   GET http://localhost:5309/donchan?costume=1&facecolor=2&bodycolor=3&anim=don_normal&frame=0
    //   すべてのクエリパラメータは省略可（省略時は現在の状態のまま）。
    //   レスポンス: image/png（1枚の静止画）
    // ==========================================================================
    private const int ApiPort = 5309;
    private static HttpListener _apiListener;
    private static bool _apiStarted = false;
    private static readonly ConcurrentQueue<ApiJob> _apiJobQueue = new();

    private sealed class ApiJob
    {
        public int? Costume;
        public int? FaceColorIndex;
        public int? BodyColorIndex;
        public int? RimColorIndex;
        public string DrawModeStr;
        public string AnimName;
        public int? Frame;
        public readonly ManualResetEventSlim Ready = new(false);
        public byte[] ResultPng;
        public string Error;
    }

    // 全アニメーション×全フレームを一括生成してzipにまとめて返すジョブ
    private static readonly ConcurrentQueue<BatchApiJob> _apiBatchJobQueue = new();
    private static BatchApiJob _activeBatchJob = null;

    private sealed class BatchApiJob
    {
        public int? Costume;
        public int? FaceColorIndex;
        public int? BodyColorIndex;
        public int? RimColorIndex;
        public string DrawModeStr;
        public readonly ManualResetEventSlim Ready = new(false);
        public byte[] ResultZip;
        public string Error;
    }

    private static void StartApiServer()
    {
        if (_apiStarted) return;
        try
        {
            _apiListener = new HttpListener();
            _apiListener.Prefixes.Add($"http://localhost:{ApiPort}/donchan/");
            _apiListener.Start();
            _apiStarted = true;

            var thread = new Thread(ApiListenLoop) { IsBackground = true, Name = "DonsAPI" };
            thread.Start();

            Console.WriteLine($"[DonsAPI] 起動しました");
            Console.WriteLine($"[DonsAPI]  1枚生成: http://localhost:{ApiPort}/donchan?costume=&facecolor=&bodycolor=&rimcolor=&drawmode=&anim=&frame=");
            Console.WriteLine($"[DonsAPI]  全生成zip: http://localhost:{ApiPort}/donchan/batch?costume=&facecolor=&bodycolor=&rimcolor=&drawmode=");
            Console.WriteLine($"[DonsAPI]  進捗確認: http://localhost:{ApiPort}/donchan/progress");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[DonsAPI ERROR] 起動失敗: " + ex.Message);
        }
    }

    private static void ApiListenLoop()
    {
        while (_apiStarted)
        {
            HttpListenerContext ctx;
            try { ctx = _apiListener.GetContext(); }
            catch { break; } // Stop()されたらここで抜ける

            try
            {
                // ブラウザ（file://やlocalhostの静的パネル）からfetch/imgで叩けるようにCORSを許可する
                ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
                ctx.Response.AddHeader("Access-Control-Allow-Methods", "GET, OPTIONS");
                ctx.Response.AddHeader("Access-Control-Allow-Headers", "*");

                if (ctx.Request.HttpMethod == "OPTIONS")
                {
                    ctx.Response.StatusCode = 204;
                    ctx.Response.OutputStream.Close();
                    continue;
                }

                bool isBatch = ctx.Request.Url.AbsolutePath.TrimEnd('/').EndsWith("/batch", StringComparison.OrdinalIgnoreCase);
                bool isProgress = ctx.Request.Url.AbsolutePath.TrimEnd('/').EndsWith("/progress", StringComparison.OrdinalIgnoreCase);
                var q = ctx.Request.QueryString;

                if (isProgress)
                {
                    bool exporting = _isExporting || _activeBatchJob != null;
                    int animIndex = _currentAnimIndex;
                    int animTotal = GetAnimationCount();
                    string animName = (animIndex >= 0 && animIndex < animTotal) ? GetAnimationNameById(animIndex) : "";
                    string json = "{"
                        + "\"exporting\":" + (exporting ? "true" : "false") + ","
                        + "\"frame\":" + _exportFrameCounter + ","
                        + "\"totalFrames\":" + _exportTotalFrames + ","
                        + "\"animIndex\":" + Math.Max(0, animIndex) + ","
                        + "\"animTotal\":" + animTotal + ","
                        + "\"animName\":\"" + animName.Replace("\"", "'") + "\""
                        + "}";
                    byte[] pbody = System.Text.Encoding.UTF8.GetBytes(json);
                    ctx.Response.ContentType = "application/json; charset=utf-8";
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentLength64 = pbody.Length;
                    ctx.Response.OutputStream.Write(pbody, 0, pbody.Length);
                    ctx.Response.OutputStream.Close();
                    continue;
                }

                if (isBatch)
                {
                    var bjob = new BatchApiJob
                    {
                        Costume = TryParseInt(q["costume"]),
                        FaceColorIndex = TryParseInt(q["facecolor"]),
                        BodyColorIndex = TryParseInt(q["bodycolor"]),
                        RimColorIndex = TryParseInt(q["rimcolor"]),
                        DrawModeStr = q["drawmode"],
                    };

                    _apiBatchJobQueue.Enqueue(bjob);

                    if (!bjob.Ready.Wait(TimeSpan.FromMinutes(10)))
                    {
                        WriteApiError(ctx, 504, "timeout: バッチ生成が終わりませんでした");
                        continue;
                    }

                    if (bjob.Error != null)
                    {
                        WriteApiError(ctx, 400, bjob.Error);
                        continue;
                    }

                    ctx.Response.ContentType = "application/zip";
                    ctx.Response.AddHeader("Content-Disposition", "attachment; filename=\"donchan_export.zip\"");
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentLength64 = bjob.ResultZip.Length;
                    ctx.Response.OutputStream.Write(bjob.ResultZip, 0, bjob.ResultZip.Length);
                    ctx.Response.OutputStream.Close();
                    continue;
                }

                var job = new ApiJob
                {
                    Costume = TryParseInt(q["costume"]),
                    FaceColorIndex = TryParseInt(q["facecolor"]),
                    BodyColorIndex = TryParseInt(q["bodycolor"]),
                    RimColorIndex = TryParseInt(q["rimcolor"]),
                    DrawModeStr = q["drawmode"],
                    AnimName = q["anim"],
                    Frame = TryParseInt(q["frame"]),
                };

                _apiJobQueue.Enqueue(job);

                if (!job.Ready.Wait(TimeSpan.FromSeconds(10)))
                {
                    WriteApiError(ctx, 504, "timeout: メインループが処理しませんでした");
                    continue;
                }

                if (job.Error != null)
                {
                    WriteApiError(ctx, 400, job.Error);
                    continue;
                }

                ctx.Response.ContentType = "image/png";
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentLength64 = job.ResultPng.Length;
                ctx.Response.OutputStream.Write(job.ResultPng, 0, job.ResultPng.Length);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                try { WriteApiError(ctx, 500, ex.Message); } catch { }
            }
        }
    }

    private static void WriteApiError(HttpListenerContext ctx, int status, string message)
    {
        byte[] body = System.Text.Encoding.UTF8.GetBytes(message);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        ctx.Response.ContentLength64 = body.Length;
        ctx.Response.OutputStream.Write(body, 0, body.Length);
        ctx.Response.OutputStream.Close();
    }

    private static int? TryParseInt(string s) => int.TryParse(s, out int v) ? v : (int?)null;

    private static bool TryParseDrawMode(string s, out DrawMode mode)
    {
        switch ((s ?? "").ToLowerInvariant())
        {
            case "cos": mode = DrawMode.CosOnly; return true;
            case "bodyhead": mode = DrawMode.BodyHeadOnly; return true;
            case "body": mode = DrawMode.BodyOnly; return true;
            case "head": mode = DrawMode.HeadOnly; return true;
            case "all": mode = DrawMode.All; return true;
            default: mode = DrawMode.All; return false;
        }
    }

    // メインループ（Update()）から毎フレーム呼ばれる。
    private static void ProcessApiQueue()
    {
        // 進行中のバッチジョブがあれば完了チェックのみ行う
        if (_activeBatchJob != null)
        {
            if (!_isExporting) FinishActiveBatchJob();
            return;
        }

        if (_isExporting) return; // （手動エクスポート中などは何もしない）

        // バッチ生成の新規リクエストがあれば単発より優先して開始する
        if (_apiBatchJobQueue.TryDequeue(out BatchApiJob bjob))
        {
            StartBatchApiJob(bjob);
            return;
        }

        ProcessSingleApiJob();
    }

    private static void StartBatchApiJob(BatchApiJob bjob)
    {
        try
        {
            if (bjob.Costume.HasValue) SetCostumeId(bjob.Costume.Value);
            if (bjob.FaceColorIndex.HasValue) SetFaceColorIndex(bjob.FaceColorIndex.Value);
            if (bjob.BodyColorIndex.HasValue) SetBodyColorIndex(bjob.BodyColorIndex.Value);
            if (bjob.RimColorIndex.HasValue) SetRimColorIndex(bjob.RimColorIndex.Value);
            if (TryParseDrawMode(bjob.DrawModeStr, out DrawMode mode)) CurrentDrawMode = mode;

            if (!_loaded || _animationCount == 0 || _animationsPtr == null)
            {
                bjob.Error = "アニメーションが読み込まれていません";
                bjob.Ready.Set();
                return;
            }

            _activeBatchJob = bjob;

            // 前回のエクスポートの非同期書き込みが残っていたら、削除前に完了を待つ
            WaitForExportWritesToFinish();

            string exportDir = GetExportRootDir();
            if (Directory.Exists(exportDir)) Directory.Delete(exportDir, recursive: true);

            StartBatchExport(); // 完了すると _isExporting が自動的にfalseに戻る
        }
        catch (Exception ex)
        {
            bjob.Error = ex.Message;
            bjob.Ready.Set();
            _activeBatchJob = null;
        }
    }

    private static void FinishActiveBatchJob()
    {
        var bjob = _activeBatchJob;
        _activeBatchJob = null;
        try
        {
            // 全フレームの非同期書き込みが完了するまで待ってからzip化する
            WaitForExportWritesToFinish();

            string exportDir = GetExportRootDir();
            if (!Directory.Exists(exportDir) || Directory.GetFiles(exportDir, "*", SearchOption.AllDirectories).Length == 0)
            {
                bjob.Error = "出力対象のアニメーションがありませんでした";
                return;
            }

            string tmpZip = Path.Combine(Path.GetTempPath(), $"donsapi_batch_{Guid.NewGuid():N}.zip");
            ZipFile.CreateFromDirectory(exportDir, tmpZip, CompressionLevel.Optimal, includeBaseDirectory: false);
            bjob.ResultZip = File.ReadAllBytes(tmpZip);
            File.Delete(tmpZip);
        }
        catch (Exception ex)
        {
            bjob.Error = ex.Message;
        }
        finally
        {
            bjob.Ready.Set();
        }
    }

    private static void ProcessSingleApiJob()
    {
        if (!_apiJobQueue.TryDequeue(out ApiJob job)) return;

        try
        {
            if (job.Costume.HasValue) SetCostumeId(job.Costume.Value);
            if (job.FaceColorIndex.HasValue) SetFaceColorIndex(job.FaceColorIndex.Value);
            if (job.BodyColorIndex.HasValue) SetBodyColorIndex(job.BodyColorIndex.Value);
            if (job.RimColorIndex.HasValue) SetRimColorIndex(job.RimColorIndex.Value);
            if (TryParseDrawMode(job.DrawModeStr, out DrawMode mode)) CurrentDrawMode = mode;
            if (!string.IsNullOrWhiteSpace(job.AnimName)) SetAnimationByName(job.AnimName);

            if (_currentAnimIndex < 0 || _animationsPtr == null)
            {
                job.Error = "アニメーションが読み込まれていません";
                return;
            }

            int total = _animationsPtr[_currentAnimIndex].KeyFrameCount;
            int frame = job.Frame.HasValue ? Math.Clamp(job.Frame.Value, 0, Math.Max(0, total - 1)) : 0;
            _currentAnimFrame = frame;

            Raylib.UpdateModelAnimation(_model, _animationsPtr[_currentAnimIndex], frame);
            if (_bodyLoaded) Raylib.UpdateModelAnimation(_bodyModel, _animationsPtr[_currentAnimIndex], frame);
            if (_headLoaded) Raylib.UpdateModelAnimation(_headModel, _animationsPtr[_currentAnimIndex], frame);

            ApplyFaceTextureToModel();
            ApplyFaceTextureToHead();
            DrawSimple();

            job.ResultPng = CaptureRenderBufferPng();
        }
        catch (Exception ex)
        {
            job.Error = ex.Message;
        }
        finally
        {
            job.Ready.Set();
        }
    }

    private static byte[] CaptureRenderBufferPng()
    {
        Image img = Raylib.LoadImageFromTexture(_renderBufferFinal.Texture);
        Raylib.ImageFlipVertical(ref img);

        string tmp = Path.Combine(Path.GetTempPath(), $"donsapi_{Guid.NewGuid():N}.png");
        Raylib.ExportImage(img, tmp);
        Raylib.UnloadImage(img);

        byte[] bytes = File.ReadAllBytes(tmp);
        File.Delete(tmp);
        return bytes;
    }

    private static void StopApiServer()
    {
        if (!_apiStarted) return;
        _apiStarted = false;
        try { _apiListener.Stop(); _apiListener.Close(); } catch { }
    }

    public static void Unload()
    {
        StopApiServer();

        // 残っているエクスポート書き込みを完了させてからスレッドを止める
        if (_exportWorkersStarted)
        {
            WaitForExportWritesToFinish();
            _exportWriteQueue.CompleteAdding();
            foreach (var t in _exportWriteWorkers) t.Join(2000);
        }

        if (_outlinePassShaderLoaded) { Raylib.UnloadShader(_outlinePassShader); _outlinePassShaderLoaded = false; _outlinePassShader = default; }
        if (_outlineShaderLoaded) { Raylib.UnloadShader(_outlineShader); _outlineShaderLoaded = false; _outlineShader = default; }
        if (_renderBuffer.Id != 0) { Raylib.UnloadRenderTexture(_renderBuffer); _renderBuffer = default; }
        if (_renderBufferFinal.Id != 0) { Raylib.UnloadRenderTexture(_renderBufferFinal); _renderBufferFinal = default; }
        UnloadFaceTexture();
        if (_faceSheetLoaded) { Raylib.UnloadImage(_faceSheetImage); _faceSheetLoaded = false; }
        if (_loaded && _model.MeshCount > 0) Raylib.UnloadModel(_model);
        if (_bodyLoaded && _bodyModel.MeshCount > 0) Raylib.UnloadModel(_bodyModel);
        if (_headLoaded && _headModel.MeshCount > 0) Raylib.UnloadModel(_headModel);

        foreach (var kv in _origImgCostume) Raylib.UnloadImage(kv.Value);
        foreach (var kv in _origImgBody) Raylib.UnloadImage(kv.Value);
        foreach (var kv in _origImgHead) Raylib.UnloadImage(kv.Value);
        _origImgCostume.Clear(); _origImgBody.Clear(); _origImgHead.Clear();
    }

    private static int ReadConfig() { try { if (File.Exists("Don/Config.json")) { using var doc = JsonDocument.Parse(File.ReadAllText("Don/Config.json")); return doc.RootElement.GetProperty("Dons").GetInt32(); } } catch { } return 0; }
}

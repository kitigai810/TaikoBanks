using Raylib_cs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

/// <summary>
/// 演奏画面の背景で踊る「ダンサー」演出。
/// Lumen/1.Enso/Dancer/ 以下のフォルダをプログラムで検索し、各フォルダが
///   In/0.png, In/1.png, ...   ： 出現時に1回だけ再生するアニメ
///   Loop/0.png, Loop/1.png, ...： 出現後にループし続けるアニメ
/// を持つ「キャラクター」とみなしてランダムに割り当てる。
///
/// スロットは合計5つ:
///   0,1,2 … 演奏開始(ゲージ0)から出現
///   3     … 魂ゲージがクリアラインに達する少し手前で出現
///   4     … 魂ゲージがクリアラインに達した瞬間に出現
///
/// 各スロットの位置・大きさは配列 X[]/Y[]/Scale[] で自由に変更可能。
/// アニメーション速度は曲のBPMに連動する(1コマ = 1拍 / FramesPerBeat)。
/// </summary>
public static class Dancer
{
    private const string BaseDir = "Lumen/1.Enso/Dancer";
    private const int SLOT_COUNT = 5;

    // ---- 表示位置・大きさ調整（1920x1080基準）。X,Yは足元中心の座標。自由に変更可 ----
    public static readonly float[] X = { 920f, 620f, 1220f, 320f, 1520f };
    public static readonly float[] Y = { 760f, 760f, 760f, 760f, 760f };
    public static readonly float[] Scale = { 1f, 1f, 1f, 1f, 1f };

    // 5体まとめて動かす/拡大したいときはここを変更（各スロットの値に加算・乗算される）
    public static float GlobalOffsetX = 0f;
    public static float GlobalOffsetY = 320f;
    public static float GlobalScale = 1f;

    // スロット3(4体目)：ゲージ全体(0〜100%)の何%で出現させるか
    public static float FourthAppearGaugePercent = 40f;

    // ループアニメの速度：1コマ = 1拍 / FramesPerBeat（大きいほどコマ送りが速い）
    public static float FramesPerBeat = 2f;
    private const float DefaultBpm = 120f;

    // 💡 設定パネル「パフォーマンス」タブ(SettingsPanel.DancerFps)から反映される上限FPS。
    //    BPM連動のコマ送り間隔(loopFrameSec/inFrameSec)がこれより短くなる(=更新が速すぎる)場合に、
    //    この値を下限のコマ間隔として使うことで、テクスチャ切り替え頻度に上限をかける。
    public static float TargetFps = 120f;

    // 💡 設定パネル「パフォーマンス」タブの画質設定。Lowはテクスチャフィルタを最近傍(Point)にして
    //    サンプリング負荷を下げ、Highは滑らかなBilinearを使う。設定変更時はキャッシュ済みの
    //    全テクスチャに対して即座に ApplyQuality() で反映する(再読み込みは行わない)。
    public enum QualityLevel { Low, High }
    private static QualityLevel _quality = QualityLevel.High;
    public static QualityLevel Quality
    {
        get => _quality;
        set
        {
            if (_quality == value) return;
            _quality = value;
            ApplyQuality();
        }
    }

    // --- ★追加: 登場（In）アクションに何拍かけるか（デフォルト：4拍 ＝ 1小節） ---
    // 120枚のIn画像が、BPM変化に合わせて常にこの拍数で再生し終えるようになります。
    public static float TargetBeatsForIn = 4f;

    // 出現時のIn(1回だけ再生)アニメの速度：BPM連動が機能しない場合のデフォルト秒数。
    public static float InFrameSec = 0.01f;

    private class Slot
    {
        public string CharName;
        public Texture2D[] InFrames = Array.Empty<Texture2D>();
        public Texture2D[] LoopFrames = Array.Empty<Texture2D>();
        public double AppearSec = -1.0;
    }

    // キャラ名 → (In, Loop)フレーム一式のキャッシュ。
    private class CharFrames
    {
        public Texture2D[] InFrames = Array.Empty<Texture2D>();
        public Texture2D[] LoopFrames = Array.Empty<Texture2D>();
    }

    private static readonly Dictionary<string, CharFrames> _frameCache = new();

    private static readonly Slot[] _slots = new Slot[SLOT_COUNT];
    private static bool _loaded;
    private static float _bpm = DefaultBpm;
    private static int _bpmSearchIndex;

    public static void Init()
    {
        // 💡 修正: ここで Unload() は呼ばない。
        //    以前はUnload()で_frameCacheごと毎回テクスチャを解放していたため、
        //    曲を始めるたび(F2リトライ・段位道場の曲間も含む)に同じキャラでも
        //    毎回100枚超のPNGを読み直しており、それが体感の重さの正体だった。
        //    ここではスロットの状態だけ作り直し、テクスチャキャッシュ(_frameCache)は
        //    Enso.Unload()経由でDancer.Unload()が呼ばれるまで保持し続ける。
        for (int i = 0; i < SLOT_COUNT; i++)
            _slots[i] = null;

        var pool = FindCharacters();
        Shuffle(pool);

        for (int i = 0; i < SLOT_COUNT; i++)
        {
            _slots[i] = new Slot();
            if (pool.Count == 0) continue;

            // フォルダ数が足りない場合は使い回す（できる限り重複させない）
            string name = pool[i % pool.Count];
            _slots[i].CharName = name;

            if (!_frameCache.TryGetValue(name, out var frames))
            {
                frames = new CharFrames
                {
                    InFrames = LoadFrames(name, "In"),
                    LoopFrames = LoadFrames(name, "Loop"),
                };
                _frameCache[name] = frames;
            }

            _slots[i].InFrames = frames.InFrames;
            _slots[i].LoopFrames = frames.LoopFrames;
        }

        // 最初の3体はゲージ0(演奏開始)から出現
        for (int i = 0; i < 3 && i < SLOT_COUNT; i++)
            _slots[i].AppearSec = 0.0;

        _bpm = FindInitialBpm();
        _bpmSearchIndex = 0;
        _loaded = true;
    }

    /// <summary>
    /// 全キャラクターのIn/Loopフレームを事前に読み込んで _frameCache に入れておく。
    /// タイトル画面や選曲画面など、演奏開始の"待ち時間"が無いタイミングで
    /// 一度呼んでおくと、以後の Init() は毎回この読み込みコストを払わずに済む。
    /// 曲開始のたびに一瞬重くなる問題への対策として、余裕があるタイミングで呼ぶこと。
    /// 何度呼んでも安全(未キャッシュのキャラだけ読み込む)。
    /// </summary>
    public static void Preload()
    {
        foreach (var name in FindCharacters())
        {
            if (_frameCache.ContainsKey(name)) continue;

            var frames = new CharFrames
            {
                InFrames = LoadFrames(name, "In"),
                LoopFrames = LoadFrames(name, "Loop"),
            };
            _frameCache[name] = frames;
        }
    }

    public static void Unload()
    {
        foreach (var frames in _frameCache.Values)
        {
            foreach (var t in frames.InFrames) if (t.Id != 0) Raylib.UnloadTexture(t);
            foreach (var t in frames.LoopFrames) if (t.Id != 0) Raylib.UnloadTexture(t);
        }
        _frameCache.Clear();

        for (int i = 0; i < SLOT_COUNT; i++)
            _slots[i] = null;

        _loaded = false;
    }

    /// <summary>毎フレーム呼ぶ。出現トリガーの判定と、曲中のBPM変化への追従を行う。</summary>
    public static void Update(double nowSec)
    {
        if (!_loaded) return;

        UpdateBpm(nowSec);

        var s3 = _slots[3];
        if (s3 != null && s3.AppearSec < 0)
        {
            if (Gauge.Value >= FourthAppearGaugePercent)
                s3.AppearSec = nowSec;
        }

        var s4 = _slots[4];
        if (s4 != null && s4.AppearSec < 0 && Gauge.IsClear)
            s4.AppearSec = nowSec;
    }

    /// <summary>
    /// nowSec時点で鳴っているBPMを更新する。
    /// </summary>
    private static void UpdateBpm(double nowSec)
    {
        var chips = TJA.Chips;
        if (chips == null || chips.Count == 0) return;

        while (_bpmSearchIndex < chips.Count && TJA.ChipSec(chips[_bpmSearchIndex]) <= nowSec)
        {
            if (chips[_bpmSearchIndex]._bpm > 0) _bpm = (float)chips[_bpmSearchIndex]._bpm;
            _bpmSearchIndex++;
        }
    }

    public static void Draw(float vx, float vy, float s, double nowSec)
    {
        if (!_loaded) return;

        float loopFrameSec = (_bpm > 0f && FramesPerBeat > 0f) ? 60f / _bpm / FramesPerBeat : 0.15f;
        // パフォーマンス設定のFPS上限を下回らないようコマ間隔をクランプする
        if (TargetFps > 0f) loopFrameSec = Math.Max(loopFrameSec, 1f / TargetFps);

        for (int i = 0; i < SLOT_COUNT; i++)
        {
            var slot = _slots[i];
            if (slot == null || slot.AppearSec < 0) continue;

            double elapsed = nowSec - slot.AppearSec;
            if (elapsed < 0) continue;

            // --- ★修正: 登場アクション(In)をBPMおよびフレーム数に基づいて自動で動的計算 ---
            float inFrameSec = InFrameSec > 0f ? InFrameSec : 0.05f;
            if (_bpm > 0f && slot.InFrames.Length > 0)
            {
                // Inフレーム数(120枚等)が設定した拍数（TargetBeatsForIn）にぴったり収まるコマ間隔を計算
                inFrameSec = (float)((60.0 / _bpm) * TargetBeatsForIn / slot.InFrames.Length);
            }
            if (TargetFps > 0f) inFrameSec = Math.Max(inFrameSec, 1f / TargetFps);
            // ---------------------------------------------------------------------------

            Texture2D tex;
            if (slot.InFrames.Length > 0)
            {
                double inDur = slot.InFrames.Length * inFrameSec;
                if (elapsed < inDur)
                {
                    int idx = Math.Min((int)(elapsed / inFrameSec), slot.InFrames.Length - 1);
                    tex = slot.InFrames[idx];
                }
                else
                {
                    // --- ★修正: elapsed - inDur ではなく nowSec でコマ計算することで全員の踊りを同期 ---
                    tex = LoopFrame(slot, nowSec, loopFrameSec);
                }
            }
            else
            {
                // --- ★修正: In無しの場合も nowSec で同期 ---
                tex = LoopFrame(slot, nowSec, loopFrameSec);
            }

            if (tex.Id == 0) continue;

            float scale = Scale[i] * GlobalScale;
            float w = tex.Width * scale;
            float h = tex.Height * scale;
            float x = X[i] + GlobalOffsetX;
            float y = Y[i] + GlobalOffsetY;
            var src = new Rectangle(0, 0, tex.Width, tex.Height);
            var dest = new Rectangle(x * s + vx, y * s + vy, w * s, h * s);
            var origin = new Vector2(w * s / 2f, h * s); // 足元中心基準
            Raylib.DrawTexturePro(tex, src, dest, origin, 0f, Color.White);
        }
    }

    private static Texture2D LoopFrame(Slot slot, double loopElapsed, float frameSec)
    {
        if (slot.LoopFrames.Length == 0) return default;
        if (loopElapsed < 0) loopElapsed = 0;
        int idx = (int)(loopElapsed / frameSec) % slot.LoopFrames.Length;
        return slot.LoopFrames[idx];
    }

    private static float FindInitialBpm()
    {
        var chips = TJA.Chips;
        if (chips != null)
        {
            for (int i = 0; i < chips.Count; i++)
            {
                if (chips[i]._bpm > 0) return (float)chips[i]._bpm;
            }
        }
        return DefaultBpm;
    }

    private static List<string> FindCharacters()
    {
        var list = new List<string>();
        if (!Directory.Exists(BaseDir)) return list;

        foreach (var dir in Directory.GetDirectories(BaseDir))
        {
            string inDir = Path.Combine(dir, "In");
            string loopDir = Path.Combine(dir, "Loop");
            if (Directory.Exists(inDir) || Directory.Exists(loopDir))
                list.Add(Path.GetFileName(dir));
        }
        return list;
    }

    private static Texture2D[] LoadFrames(string charName, string sub)
    {
        string dir = Path.Combine(BaseDir, charName, sub);
        var list = new List<Texture2D>();
        var filter = _quality == QualityLevel.Low ? TextureFilter.Point : TextureFilter.Bilinear;
        for (int i = 0; File.Exists(Path.Combine(dir, $"{i}.png")); i++)
        {
            var tex = Raylib.LoadTexture(Path.Combine(dir, $"{i}.png"));
            Raylib.SetTextureFilter(tex, filter);
            list.Add(tex);
        }
        VramProbe.Track($"Dancer.LoadFrames({charName}/{sub})");
        return list.ToArray();
    }

    /// <summary>
    /// 画質設定(Quality)が変更されたとき、既に読み込み済みの全テクスチャ(_frameCache)に対して
    /// テクスチャフィルタだけを即座に切り替える。読み直しは行わないので軽量。
    /// </summary>
    private static void ApplyQuality()
    {
        var filter = _quality == QualityLevel.Low ? TextureFilter.Point : TextureFilter.Bilinear;
        foreach (var frames in _frameCache.Values)
        {
            foreach (var t in frames.InFrames) if (t.Id != 0) Raylib.SetTextureFilter(t, filter);
            foreach (var t in frames.LoopFrames) if (t.Id != 0) Raylib.SetTextureFilter(t, filter);
        }
    }

    private static readonly Random _rng = new();

    private static void Shuffle(List<string> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
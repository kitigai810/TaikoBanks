using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Raylib_cs;
using TaikoNauts.Core.Taiko.Charts;

/// <summary>
/// 演奏画面の魂ゲージ。内部的に10000点満点（本家仕様）で処理します。
/// 50セグメント (21px幅, 200点につき1セグメント) で 0〜10000点 を表現し、
/// 難易度ごとにクリアライン（6000 / 7000 / 8000点）の位置と素材 (40/30/20) が変わります。
/// </summary>
public static class Gauge
{
    private const float FRAME_X = 725f, FRAME_Y = 204f;
    private const float FILL_X = 738f, FILL_Y = 249f;
    private const float NIJI_X = 732f, NIJI_Y = 210f;
    private const float SEN_X = 732f, SEN_Y = 216f;
    private const float TAMASHI_X = 1793f, TAMASHI_Y = 207f;

    // クリアテキスト (Gauge_Clear_Text.png: 上半分=未クリア / 下半分=クリア)
    private const float KURIA_Y = 211.5f;
    private const float KURIA_SCALE = 1f;
    private const float KURIA_X_OFFSET = 0f;

    private const int SEGMENTS = 50;
    private const float SEG_W = 21f;
    private const float FLASH_OFFSET_X = -6f;

    private const double FLASH_IN = 0.13;
    private const double FLASH_OUT = 0.16;
    private const double NIJI_FRAME_SEC = 0.1;
    private const double TAMASHI_BLINK_SEC = 0.1;

    // 10000点満点
    private const double MAX_GAUGE = 10000.0;

    private struct Flash
    {
        public float X;
        public int Kind;
        public double StartSec;
    }

    private static Texture2D _frameTex;
    // 2PはMigi側のフレーム・ゲージ片を使う。座標は反転するが、画像自体は通常向き。
    private static Texture2D _p2FrameTex;
    private static Texture2D _pHidariTex, _pMigiTex, _pCUeTex, _pCTex;
    private static Texture2D _senTex, _tamashiTex, _tamashiLTex;
    private static Texture2D _fHidariTex, _fCInTex, _fCTex;
    private static Texture2D[] _nijiTex = Array.Empty<Texture2D>();
    private static Texture2D _kuriaTex;
    private static bool _loaded;
    // 💡 直前にロードしたテクスチャ構成のキー(frameFile/senFile/nijiSubDirは全て同じ値=g or "Dan")。
    //    同じ構成ならF2リトライ・曲の再スタートでテクスチャを読み直さず使い回す。
    private static string _loadedAssetKey;

    private static readonly List<Flash> _flashes = new();
    private static readonly List<Flash> _p2Flashes = new();

    private static double _gauge;
    private static double _p2Gauge;
    private static bool _p2RevealActive;
    private static double _p2RevealStartSec;

    /// <summary>2P魂ゲージ全体を上下に微調整するオフセット。正の値で下へ移動。</summary>
    public static float P2GaugeYOffset = 50f;
    private static int _goodValue;
    private static int _okValue;
    private static int _badValue;
    private static int _clearSegment;
    private static int _clearThreshold;

    private const double REVEAL_DURATION_SEC = 3.0;
    private static bool _revealActive;
    private static double _revealStartSec;

    public static double Value => _gauge / 100.0; // 0〜100%表示
    public static double P2Value => _p2Gauge / 100.0;
    public static bool IsRainbow => _gauge >= MAX_GAUGE;
    public static bool IsP2Rainbow => _p2Gauge >= MAX_GAUGE;
    public static bool IsClear => _gauge >= _clearThreshold;
    public static bool IsP2Clear => _p2Gauge >= _clearThreshold;
    public static double ClearGaugePercent => _clearThreshold / 100.0;

    private static int FilledSegments => Math.Clamp((int)(_gauge / 200.0), 0, SEGMENTS);

    public static void StartReveal(double nowSec)
    {
        _revealActive = true;
        _revealStartSec = nowSec;
    }

    public static void StartP2Reveal(double nowSec)
    {
        _p2RevealActive = true;
        _p2RevealStartSec = nowSec;
    }

    private static double GetDisplayGauge(double nowSec)
    {
        if (!_revealActive) return _gauge;

        double t = REVEAL_DURATION_SEC > 0
            ? Math.Clamp((nowSec - _revealStartSec) / REVEAL_DURATION_SEC, 0.0, 1.0)
            : 1.0;

        return _gauge * t;
    }

    private static int GetDisplayFilledSegments(double nowSec) =>
        Math.Clamp((int)(GetDisplayGauge(nowSec) / 200.0), 0, SEGMENTS);

    private static double GetP2DisplayGauge(double nowSec)
    {
        if (!_p2RevealActive) return _p2Gauge;
        double t = REVEAL_DURATION_SEC > 0
            ? Math.Clamp((nowSec - _p2RevealStartSec) / REVEAL_DURATION_SEC, 0.0, 1.0)
            : 1.0;
        return _p2Gauge * t;
    }

    private static int GetP2DisplayFilledSegments(double nowSec) =>
        Math.Clamp((int)(GetP2DisplayGauge(nowSec) / 200.0), 0, SEGMENTS);

    public static void Init(int? totalNoteCountOverride = null, bool resetGauge = true)
    {
        // 💡 修正: ここではテクスチャを解放しない(Unload()は呼ばない)。
        //    以前は毎回Unload()→固定10数枚+虹演出フォルダの全PNGを読み直しており、
        //    F2リトライや曲の再スタートのたびに一瞬重くなる原因になっていた。
        //    テクスチャ構成が変わらない限り(同じコース/段位モード)使い回す。
        if (resetGauge) _gauge = 0;
        _flashes.Clear();
        _revealActive = false;

        Course course = TJA.SelectedCourseType;
        int level = 5;

        // TjaChartParser.cs で格納されている実フィールド名 _level を参照する。
        // リフレクション、または動的型（dynamic）経由でのアクセスにより、
        // 異なるアセンブリや非推奨アクセスであっても安全に値を取り出せるようにフォールバック化します。
        try
        {
            if (TJA.SelectedCourse != null)
            {
                // dynamicキャストにより、構造体/クラスの内部に _level フィールドがあれば
                // コンパイルエラーを回避してランタイムに直接値を取得します
                dynamic selected = TJA.SelectedCourse;
                level = (int)selected._level;
            }
        }
        catch
        {
            // dynamicでの取得に失敗した場合は、Courseの難易度設定から自動でレベルを推測します
            level = course switch
            {
                Course.Easy => 3,
                Course.Normal => 5,
                Course.Hard => 7,
                Course.Oni => 9,   // ★9（基本ランク判定を安全に機能させる）
                Course.Edit => 10, // ★10（裏おに用基本ランク）
                _ => 5
            };
        }

        // かんたん: 6000点 / ふつう・むずかしい: 7000点 / おに: 8000点でノルマ
        if (course == Course.Easy)
        {
            _clearThreshold = 6000;
            _clearSegment = 30;
        }
        else if (course == Course.Normal || course == Course.Hard)
        {
            _clearThreshold = 7000;
            _clearSegment = 35;
        }
        else
        {
            _clearThreshold = 8000;
            _clearSegment = 40;
        }

        bool danMode = Enso.DanMode;
        string g = course switch
        {
            Course.Easy => "40",
            Course.Normal or Course.Hard => "30",
            _ => "20",
        };
        string frameFile = danMode ? "Dan" : g;
        string senFile = danMode ? "Dan" : g;

        if (_loadedAssetKey != frameFile)
        {
            UnloadTextures();

            _frameTex = Raylib.LoadTexture($"Lumen/1.Enso/Memori/Hidari/{frameFile}.png");
            _p2FrameTex = Raylib.LoadTexture($"Lumen/1.Enso/Memori/Migi/{frameFile}.png");
            _pHidariTex = Raylib.LoadTexture("Lumen/1.Enso/Memori/p/Hidari.png");
            _pMigiTex = Raylib.LoadTexture("Lumen/1.Enso/Memori/p/Migi.png");
            _pCUeTex = Raylib.LoadTexture("Lumen/1.Enso/Memori/p/C_Ue.png");
            _pCTex = Raylib.LoadTexture("Lumen/1.Enso/Memori/p/C.png");
            _senTex = Raylib.LoadTexture($"Lumen/1.Enso/Memori/Sen/{senFile}.png");
            _tamashiTex = Raylib.LoadTexture("Lumen/1.Enso/Memori/Tamashi/Tamashi.png");
            _tamashiLTex = Raylib.LoadTexture("Lumen/1.Enso/Memori/Tamashi/l.png");
            _fHidariTex = Raylib.LoadTexture("Lumen/1.Enso/Memori/f/Hidari.png");
            _fCInTex = Raylib.LoadTexture("Lumen/1.Enso/Memori/f/C_In.png");
            _fCTex = Raylib.LoadTexture("Lumen/1.Enso/Memori/f/C.png");

            _kuriaTex = Raylib.LoadTexture("Lumen/1.Enso/Memori/Kuria/Gauge_Clear_Text.png");

            string nijiSubDir = danMode ? "Dan" : g;
            string nijiDir = Path.Combine(AppContext.BaseDirectory, "Lumen", "1.Enso", "Memori", "Niji", nijiSubDir);
            if (Directory.Exists(nijiDir))
            {
                var files = Directory.GetFiles(nijiDir, "*.png");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                _nijiTex = new Texture2D[files.Length];
                for (int i = 0; i < files.Length; i++)
                    _nijiTex[i] = Raylib.LoadTexture(files[i]);
            }
            else
            {
                _nijiTex = Array.Empty<Texture2D>();
            }

            _loadedAssetKey = frameFile;
        }

        // ノーツ数のカウント
        int total = 0;
        var chips = TJA.Chips;
        if (chips != null)
        {
            for (int i = 0; i < chips.Count; i++)
            {
                var t = chips[i]._noteType;
                if (t == NoteType.Don || t == NoteType.Ka || t == NoteType.DON || t == NoteType.KA)
                    total++;
            }
        }

        int noteCountForUnit = totalNoteCountOverride ?? total;

        _goodValue = 0;
        _okValue = 0;
        _badValue = 0;

        if (noteCountForUnit > 0)
        {
            if (course == Course.Oni || course == Course.Edit)
            {
                if (level >= 9)
                {
                    // おに★9〜10: 基本コンボランク
                    _goodValue = GetOniBaseRank(noteCountForUnit);
                }
                else if (level == 8)
                {
                    // おに★8: 基本コンボランク + 1
                    _goodValue = GetOniBaseRank(noteCountForUnit) + 1;
                }
                else
                {
                    // おに★1〜7 (平均魂到達率 約70.733% から逆算)
                    double soulRate = 0.70733;
                    int s = (int)Math.Ceiling(noteCountForUnit * soulRate);
                    _goodValue = (int)Math.Ceiling(10000.0 / s);
                }

                double okRate = 0.5;
                double badRate = (level >= 8) ? -2.0 : -1.6;

                _okValue = (int)Math.Round(_goodValue * okRate);
                _badValue = (int)Math.Round(_goodValue * badRate);
            }
            else if (course == Course.Hard) // むずかしい
            {
                double soulRate = 0.68728; // ★6〜8
                double badRate = -1.25;    // ★5〜8

                if (level <= 2) { soulRate = 0.77294; badRate = -0.75; }
                else if (level == 3) { soulRate = 0.72495; badRate = -1.0; }
                else if (level == 4) { soulRate = 0.69098; badRate = -1.17; }
                else if (level == 5) { soulRate = 0.67476; badRate = -1.25; }

                int s = (int)Math.Ceiling(noteCountForUnit * soulRate);
                _goodValue = (int)Math.Ceiling(10000.0 / s);
                _okValue = (int)Math.Round(_goodValue * 0.75);
                _badValue = (int)Math.Round(_goodValue * badRate);
            }
            else if (course == Course.Normal) // ふつう
            {
                double soulRate = 0.75; // ★5〜7
                double badRate = -1.0;  // ★5〜7

                if (level <= 2) { soulRate = 0.65591; badRate = -0.75; }
                else if (level == 3) { soulRate = 0.69510; badRate = -0.75; }
                else if (level == 4) { soulRate = 0.70304; badRate = -0.75; }

                int s = (int)Math.Ceiling(noteCountForUnit * soulRate);
                _goodValue = (int)Math.Ceiling(10000.0 / s);
                _okValue = (int)Math.Round(_goodValue * 0.75);
                _badValue = (int)Math.Round(_goodValue * badRate);
            }
            else // かんたん
            {
                double soulRate = 0.73333; // ★4〜5
                if (level == 1) { soulRate = 0.60; }
                else if (level <= 3) { soulRate = 0.63333; }

                int s = (int)Math.Ceiling(noteCountForUnit * soulRate);
                _goodValue = (int)Math.Ceiling(10000.0 / s);
                _okValue = (int)Math.Round(_goodValue * 0.75);
                _badValue = (int)Math.Round(_goodValue * -0.5);
            }
        }

        // 不可の点数は減少（負の値）として扱います
        if (_badValue > 0) _badValue = -_badValue;

        VramProbe.Track("Gauge.Init (all, per-song)");
        _loaded = true;
    }

    /// <summary>
    /// 2P魂ゲージを初期化する。増減量・クリアラインは1Pと共通で、値と演出だけを独立保持する。
    /// Gauge.Init の直後に呼ぶ。
    /// </summary>
    public static void InitP2(bool resetGauge = true)
    {
        if (resetGauge) _p2Gauge = 0;
        _p2Flashes.Clear();
        _p2RevealActive = false;
    }

    /// <summary>総コンボ数に応じたおに★9〜10の「基本ランク」を返します。</summary>
    private static int GetOniBaseRank(int combo)
    {
        if (combo >= 1582) return 8;
        if (combo >= 1396) return 9;
        if (combo >= 1257) return 10;
        if (combo >= 1141) return 11;
        if (combo >= 1052) return 12;
        if (combo >= 973) return 13;
        if (combo >= 905) return 14;
        if (combo >= 847) return 15;
        if (combo >= 796) return 16;
        if (combo >= 750) return 17;
        if (combo >= 709) return 18;
        if (combo >= 673) return 19;
        if (combo >= 641) return 20;
        if (combo >= 610) return 21;
        if (combo >= 583) return 22;
        if (combo >= 559) return 23;
        if (combo >= 536) return 24;
        if (combo >= 516) return 25;
        if (combo >= 497) return 26;
        if (combo >= 477) return 27;
        if (combo >= 465) return 28;
        if (combo >= 448) return 29;
        if (combo >= 437) return 30;
        if (combo >= 417) return 31;
        if (combo >= 408) return 32;
        if (combo >= 393) return 33;
        if (combo >= 381) return 34;
        if (combo >= 375) return 35;
        if (combo >= 364) return 36;
        if (combo >= 356) return 37;
        if (combo >= 341) return 38;
        if (combo >= 334) return 39;
        if (combo >= 326) return 40;
        if (combo >= 317) return 41;
        if (combo >= 316) return 42;
        if (combo >= 311) return 43;
        if (combo >= 298) return 44;
        if (combo >= 290) return 45;
        if (combo >= 281) return 46;
        if (combo >= 280) return 47;
        if (combo >= 272) return 48;
        if (combo >= 265) return 49;
        if (combo >= 258) return 50;
        if (combo >= 252) return 51;
        if (combo >= 247) return 52;
        return 53;
    }

    public static void Unload()
    {
        if (!_loaded) return;

        UnloadTextures();
        _loadedAssetKey = null;

        _flashes.Clear();
        _p2Flashes.Clear();
        _loaded = false;
    }

    private static void UnloadTextures()
    {
        UnloadTex(ref _frameTex);
        UnloadTex(ref _p2FrameTex);
        UnloadTex(ref _pHidariTex);
        UnloadTex(ref _pMigiTex);
        UnloadTex(ref _pCUeTex);
        UnloadTex(ref _pCTex);
        UnloadTex(ref _senTex);
        UnloadTex(ref _tamashiTex);
        UnloadTex(ref _tamashiLTex);
        UnloadTex(ref _fHidariTex);
        UnloadTex(ref _fCInTex);
        UnloadTex(ref _fCTex);
        UnloadTex(ref _kuriaTex);
        for (int i = 0; i < _nijiTex.Length; i++)
            UnloadTex(ref _nijiTex[i]);
        _nijiTex = Array.Empty<Texture2D>();
    }

    public static void Update(float dt)
    {
    }

    public static void AddGood(double nowSec) => Add(_goodValue, nowSec);
    public static void AddOk(double nowSec) => Add(_okValue, nowSec);
    public static void AddBad() => _gauge = Math.Max(0.0, _gauge + _badValue); // 減算
    public static void AddMiss() => AddBad();

    public static void AddGoodP2(double nowSec) => AddP2(_goodValue, nowSec);
    public static void AddOkP2(double nowSec) => AddP2(_okValue, nowSec);
    public static void AddBadP2() => _p2Gauge = Math.Max(0.0, _p2Gauge + _badValue);
    public static void AddMissP2() => AddBadP2();

    private static void Add(double amount, double nowSec)
    {
        int before = FilledSegments;
        _gauge = Math.Min(MAX_GAUGE, _gauge + amount);
        int after = FilledSegments;
        if (after <= before) return;

        int tip = Math.Clamp(after, 1, SEGMENTS);
        _flashes.Add(new Flash
        {
            X = FILL_X + (tip - 1) * SEG_W + FLASH_OFFSET_X,
            Kind = SegKind(tip),
            StartSec = nowSec,
        });
    }

    private static void AddP2(double amount, double nowSec)
    {
        int before = Math.Clamp((int)(_p2Gauge / 200.0), 0, SEGMENTS);
        _p2Gauge = Math.Min(MAX_GAUGE, _p2Gauge + amount);
        int after = Math.Clamp((int)(_p2Gauge / 200.0), 0, SEGMENTS);
        if (after <= before) return;

        int tip = Math.Clamp(after, 1, SEGMENTS);
        _p2Flashes.Add(new Flash
        {
            X = FILL_X + (tip - 1) * SEG_W + FLASH_OFFSET_X,
            Kind = SegKind(tip),
            StartSec = nowSec,
        });
    }

    private static int SegKind(int idx) =>
        idx < _clearSegment ? 0 : idx == _clearSegment ? 1 : 2;

    public static void Draw(float vx, float vy, float s, double nowSec)
    {
        if (!_loaded) return;

        DrawTex(_frameTex, FRAME_X, FRAME_Y, vx, vy, s, 255);

        float fillBottom = FILL_Y + _pHidariTex.Height;
        int filled = GetDisplayFilledSegments(nowSec);
        for (int i = 1; i <= filled; i++)
        {
            int kind = SegKind(i);
            var tex = kind switch { 0 => _pHidariTex, 1 => Enso.DanMode ? _pCTex : _pCUeTex, _ => _pCTex };
            DrawTex(tex, FILL_X + (i - 1) * SEG_W, fillBottom - tex.Height, vx, vy, s, 255);
        }

        bool displayRainbow = GetDisplayGauge(nowSec) >= MAX_GAUGE;
        if (displayRainbow && _nijiTex.Length > 0)
        {
            int frame = (int)(Math.Max(0.0, nowSec) / NIJI_FRAME_SEC) % _nijiTex.Length;
            DrawTex(_nijiTex[frame], NIJI_X, NIJI_Y, vx, vy, s, 255);
        }

        DrawTex(_senTex, SEN_X, SEN_Y, vx, vy, s, 128);

        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            var f = _flashes[i];
            double el = nowSec - f.StartSec;
            if (el >= FLASH_IN + FLASH_OUT)
            {
                _flashes.RemoveAt(i);
                continue;
            }
            if (el < 0) continue;

            double rate = el < FLASH_IN ? el / FLASH_IN : 1.0 - (el - FLASH_IN) / FLASH_OUT;
            var tex = f.Kind switch { 0 => _fHidariTex, 1 => Enso.DanMode ? _fCTex : _fCInTex, _ => _fCTex };
            float pieceH = f.Kind == 0 ? _pHidariTex.Height : _pCTex.Height;
            float y = fillBottom - pieceH - (tex.Height - pieceH) / 2f;
            DrawTex(tex, f.X, y, vx, vy, s, (byte)(Math.Clamp(rate, 0.0, 1.0) * 255));
        }

        DrawKuriaText(vx, vy, s, nowSec);

        DrawTex(_tamashiTex, TAMASHI_X, TAMASHI_Y, vx, vy, s, 255);
        if (displayRainbow)
        {
            double t = Math.Max(0.0, nowSec) % TAMASHI_BLINK_SEC / TAMASHI_BLINK_SEC;
            DrawTex(_tamashiLTex, TAMASHI_X, TAMASHI_Y, vx, vy, s, (byte)(0.5 * (1.0 - t) * 255));
        }
    }

    /// <summary>
    /// Migi側の素材を通常向きで使い、反転した座標配置で2P専用魂ゲージを下段に描画する。
    /// </summary>
    public static void DrawP2(float vx, float vy, float s, double nowSec)
    {
        if (!_loaded) return;

        Texture2D frame = _p2FrameTex.Id != 0 ? _p2FrameTex : _frameTex;
        DrawTexP2(frame, FRAME_X, FRAME_Y, vx, vy, s, 255);

        Texture2D p2FillTex = _pMigiTex.Id != 0 ? _pMigiTex : _pHidariTex;
        float fillBottom = FILL_Y + p2FillTex.Height;
        int filled = GetP2DisplayFilledSegments(nowSec);
        for (int i = 1; i <= filled; i++)
        {
            int kind = SegKind(i);
            var tex = kind switch { 0 => p2FillTex, 1 => Enso.DanMode ? _pCTex : _pCUeTex, _ => _pCTex };
            DrawTexP2(tex, FILL_X + (i - 1) * SEG_W, fillBottom - tex.Height, vx, vy, s, 255);
        }

        bool displayRainbow = GetP2DisplayGauge(nowSec) >= MAX_GAUGE;
        if (displayRainbow && _nijiTex.Length > 0)
        {
            int frameIndex = (int)(Math.Max(0.0, nowSec) / NIJI_FRAME_SEC) % _nijiTex.Length;
            DrawTexP2(_nijiTex[frameIndex], NIJI_X, NIJI_Y, vx, vy, s, 255);
        }

        DrawTexP2(_senTex, SEN_X, SEN_Y, vx, vy, s, 128);

        for (int i = _p2Flashes.Count - 1; i >= 0; i--)
        {
            var f = _p2Flashes[i];
            double el = nowSec - f.StartSec;
            if (el >= FLASH_IN + FLASH_OUT)
            {
                _p2Flashes.RemoveAt(i);
                continue;
            }
            if (el < 0) continue;

            double rate = el < FLASH_IN ? el / FLASH_IN : 1.0 - (el - FLASH_IN) / FLASH_OUT;
            var tex = f.Kind switch { 0 => _fHidariTex, 1 => Enso.DanMode ? _fCTex : _fCInTex, _ => _fCTex };
            float pieceH = f.Kind == 0 ? p2FillTex.Height : _pCTex.Height;
            float y = fillBottom - pieceH - (tex.Height - pieceH) / 2f;
            DrawTexP2(tex, f.X, y, vx, vy, s, (byte)(Math.Clamp(rate, 0.0, 1.0) * 255));
        }

        DrawKuriaTextP2(vx, vy, s, nowSec);
        // Tamashiは可読性を保つため、2Pでも通常向きで描画する。
        DrawTexP2Upright(_tamashiTex, TAMASHI_X, TAMASHI_Y, vx, vy, s, 255);
        if (displayRainbow)
        {
            double t = Math.Max(0.0, nowSec) % TAMASHI_BLINK_SEC / TAMASHI_BLINK_SEC;
            DrawTexP2Upright(_tamashiLTex, TAMASHI_X, TAMASHI_Y, vx, vy, s, (byte)(0.5 * (1.0 - t) * 255));
        }
    }

    private static void DrawKuriaTextP2(float vx, float vy, float s, double nowSec)
    {
        if (_kuriaTex.Id == 0 || Enso.DanMode) return;

        float kuriaX = FILL_X + (_clearSegment - 1.5f) * SEG_W + KURIA_X_OFFSET;
        bool displayClear = GetP2DisplayFilledSegments(nowSec) >= _clearSegment;
        int halfH = _kuriaTex.Height / 2;
        // 可読文字は通常向きのまま、反転後の座標へ配置する。
        int srcY = displayClear ? halfH : 0;
        var src = new Rectangle(0, srcY, _kuriaTex.Width, halfH);
        float destY = MirrorP2Y(KURIA_Y, halfH);
        var dest = new Rectangle(
            kuriaX * s + vx, destY * s + vy,
            _kuriaTex.Width * KURIA_SCALE * s, halfH * KURIA_SCALE * s);
        Raylib.DrawTexturePro(_kuriaTex, src, dest, Vector2.Zero, 0f, Color.White);
    }

    private static void DrawKuriaText(float vx, float vy, float s, double nowSec)
    {
        if (_kuriaTex.Id == 0) return;
        if (Enso.DanMode) return;

        float kuriaX = FILL_X + (_clearSegment - 1.5f) * SEG_W + KURIA_X_OFFSET;

        bool displayClear = GetDisplayFilledSegments(nowSec) >= _clearSegment;
        int halfH = _kuriaTex.Height / 2;
        var src = new Rectangle(0, displayClear ? halfH : 0, _kuriaTex.Width, halfH);
        var dest = new Rectangle(
            kuriaX * s + vx, KURIA_Y * s + vy,
            _kuriaTex.Width * KURIA_SCALE * s, halfH * KURIA_SCALE * s);

        Raylib.DrawTexturePro(_kuriaTex, src, dest, Vector2.Zero, 0f, Color.White);
    }

    private static void DrawTex(Texture2D tex, float x, float y, float vx, float vy, float s, byte alpha)
    {
        if (tex.Id == 0) return;
        var src = new Rectangle(0, 0, tex.Width, tex.Height);
        var dest = new Rectangle(x * s + vx, y * s, tex.Width * s, tex.Height * s);
        dest.Y += vy;
        Raylib.DrawTexturePro(tex, src, dest, Vector2.Zero, 0f,
            new Color((byte)255, (byte)255, (byte)255, alpha));
    }

    // 1P判定ラインを鏡面にして2P判定ラインへ移した際の、反転配置後の矩形上端を返す。
    private static float MirrorP2Y(float sourceTopY, float sourceHeight) =>
        EnsoP2.LANE_Y + (Enso.LANE_Y - (sourceTopY + sourceHeight)) + P2GaugeYOffset;

    private static void DrawTexP2(Texture2D tex, float x, float y, float vx, float vy, float s, byte alpha)
    {
        if (tex.Id == 0) return;
        // Tamashi・Kuria以外は、反転後の2Pレーン配置に合わせて上下反転する。
        var src = new Rectangle(0, tex.Height, tex.Width, -tex.Height);
        float destY = MirrorP2Y(y, tex.Height);
        var dest = new Rectangle(x * s + vx, destY * s + vy, tex.Width * s, tex.Height * s);
        Raylib.DrawTexturePro(tex, src, dest, Vector2.Zero, 0f,
            new Color((byte)255, (byte)255, (byte)255, alpha));
    }

    /// <summary>2P座標に配置しつつ、文字・記号を通常向きで描画する。</summary>
    private static void DrawTexP2Upright(Texture2D tex, float x, float y, float vx, float vy, float s, byte alpha)
    {
        if (tex.Id == 0) return;
        var src = new Rectangle(0, 0, tex.Width, tex.Height);
        float destY = MirrorP2Y(y, tex.Height);
        var dest = new Rectangle(x * s + vx, destY * s + vy, tex.Width * s, tex.Height * s);
        Raylib.DrawTexturePro(tex, src, dest, Vector2.Zero, 0f,
            new Color((byte)255, (byte)255, (byte)255, alpha));
    }

    private static void UnloadTex(ref Texture2D tex)
    {
        if (tex.Id != 0) Raylib.UnloadTexture(tex);
        tex = default;
    }
}
using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

public static class FlyingNotes
{
    private const int StartX = 0, StartY = 1, EndX = 2, EndY = 3, Sine = 4,
        ArcSkew = 5, HLaunchPow = 6, HLandPow = 7, OffX = 8, OffY = 9, Duration = 10;

    private static readonly float[] _p =
    {
        645f, 396f, 1863f, 258f, 341f, 1.05f, 1.41f, 1.18f, -29f, -10f, 0.45f,
    };

    private const double END_HOLD = 0.16;
    private const double L_FADE_IN = 0.16;
    private const double L_FADE_OUT = 0.15;

    public const double BalloonHoldSec = 0.32;

    // 同時に画面上に存在できるフライノーツの上限(超過分は古い方から間引く)
    private const int MAX_FLYING_NOTES = 16;

    // --- 追加・変更項目 ---
    // 爆発エフェクトの総フレーム数（画像から19分割）
    private const int EXPLOSION_FRAMES = 19;
    // 爆発全体の再生時間（秒）※速度を調整したい場合はここを変更してください（例: 0.3秒で全再生）
    private const double EXPLOSION_DURATION = 0.35;
    private static Texture2D _explosionTex;
    // --------------------

    public static float FlightDuration => _p[Duration];

    /// <summary>2Pフライノーツ軌道全体のY座標を上下に微調整するオフセット</summary>
    public static float P2FlightYOffset = 50f;

    public static Vector2 FlightPos(float t, float hitX) => Pos(t, hitX);
    public static Vector2 FlightPosP2(float t, float hitX) => PosForPlayer(t, hitX, isP2: true);

    private struct Note
    {
        public double SpawnSec;
        public int Kind;
        public float HitX;
        public bool IsBalloon;
        public bool IsP2;
    }

    private static readonly List<Note> _notes = new();
    private static readonly List<Note> _p2Notes = new();
    private static readonly Texture2D[] _wsTex = new Texture2D[4];
    private static readonly Texture2D[] _lTex = new Texture2D[2];
    private static readonly Texture2D[] _kTex = new Texture2D[13];
    private static bool _loaded;

    private static readonly int[] KStartFrames = { 4, 7, 10, 13, 17, 20, 22, 25, 28 };

    private static readonly Vector2[] KPositions =
    {
        new(677f, 275f), new(782f, 164f), new(901f, 73f),
        new(1038f, 11f), new(1184f, -22f), new(1334f, -25f),
        new(1482f, 4f), new(1620f, 62f), new(1741f, 150f),
    };

    private static readonly string[] WsPaths =
    {
        "Lumen/1.Enso/notes/don/ws.png",
        "Lumen/1.Enso/notes/katsu/ws.png",
        "Lumen/1.Enso/notes/don_/ws.png",
        "Lumen/1.Enso/notes/katsu_/ws.png",
    };

    public static void Init()
    {
        _notes.Clear();
        _p2Notes.Clear();

        if (!_loaded)
        {
            for (int i = 0; i < 4; i++)
                _wsTex[i] = Raylib.LoadTexture(WsPaths[i]);
            _lTex[0] = Raylib.LoadTexture("Lumen/1.Enso/Memori/l/s.png");
            _lTex[1] = Raylib.LoadTexture("Lumen/1.Enso/Memori/l/s_.png");
            for (int i = 0; i < _kTex.Length; i++)
                _kTex[i] = Raylib.LoadTexture($"Lumen/1.Enso/Effect/k/{i + 1:00}.png");

            // 爆発テクスチャの読み込みを追加
            _explosionTex = Raylib.LoadTexture("Lumen/1.Enso/Effect/fly/1P_Explosion.png");

            VramProbe.Track("FlyingNotes.Init (all)");
            _loaded = true;
        }
    }

    public static void Unload()
    {
        if (_loaded)
        {
            for (int i = 0; i < 4; i++)
                if (_wsTex[i].Id != 0) Raylib.UnloadTexture(_wsTex[i]);
            for (int i = 0; i < 2; i++)
                if (_lTex[i].Id != 0) Raylib.UnloadTexture(_lTex[i]);
            for (int i = 0; i < _kTex.Length; i++)
                if (_kTex[i].Id != 0) Raylib.UnloadTexture(_kTex[i]);

            // 爆発テクスチャの解放を追加
            if (_explosionTex.Id != 0) Raylib.UnloadTexture(_explosionTex);

            _loaded = false;
        }

        _notes.Clear();
        _p2Notes.Clear();
    }

    public static void Spawn(bool isKa, bool isBig, double nowSec, bool isBalloon = false)
    {
        SpawnTo(_notes, isKa, isBig, nowSec, Enso.HitX, isBalloon, isP2: false);
    }

    /// <summary>2Pの打鍵から、上下反転した下段用フライノーツを生成する。</summary>
    public static void SpawnP2(bool isKa, bool isBig, double nowSec, bool isBalloon = false)
    {
        SpawnTo(_p2Notes, isKa, isBig, nowSec, EnsoP2.HitX, isBalloon, isP2: true);
    }

    private static void SpawnTo(List<Note> target, bool isKa, bool isBig, double nowSec,
        float hitX, bool isBalloon, bool isP2)
    {
        // 高密度譜面でもプレイヤーごとに最大数を守り、片方の発生で他方を間引かない。
        while (target.Count >= MAX_FLYING_NOTES)
            target.RemoveAt(0);

        target.Add(new Note
        {
            SpawnSec = nowSec,
            Kind = (isKa ? 1 : 0) + (isBig ? 2 : 0),
            HitX = hitX,
            IsBalloon = isBalloon,
            IsP2 = isP2,
        });
    }

    public static void Update(double nowSec)
    {
        RemoveExpired(_notes, nowSec);
        RemoveExpired(_p2Notes, nowSec);
    }

    private static void RemoveExpired(List<Note> notes, double nowSec)
    {
        // 爆発エフェクトが最後まで再生されるように、削除判定までの猶予時間を延長する。
        notes.RemoveAll(n => nowSec - n.SpawnSec >=
            _p[Duration] + Math.Max(n.IsBalloon ? BalloonHoldSec : END_HOLD + L_FADE_OUT, EXPLOSION_DURATION));
    }

    private static Vector2 Pos(float t, float hitX)
    {
        float mX = _p[EndX] - _p[StartX];
        float mY = _p[EndY] - _p[StartY];
        float hv = (1f - t) * MathF.Pow(t, _p[HLaunchPow]) + t * (1f - MathF.Pow(1f - t, _p[HLandPow]));
        float ap = MathF.Pow(t, _p[ArcSkew]);
        float x = _p[StartX] + mX * hv + _p[OffX] + (hitX - 618f);
        float y = _p[StartY] + mY * t - _p[Sine] * MathF.Sin(MathF.PI * ap) + _p[OffY];
        return new Vector2(x, y);
    }

    private static Vector2 PosForPlayer(float t, float hitX, bool isP2)
    {
        Vector2 pos = Pos(t, hitX);
        if (isP2)
        {
            // 1Pの判定ライン(Enso.LANE_Y)を鏡面とし、下段2Pレーンへ上下反転する。
            pos.Y = EnsoP2.LANE_Y + (Enso.LANE_Y - pos.Y) + P2FlightYOffset;
        }
        return pos;
    }

    private static void DrawTex(Texture2D tex, Vector2 pos, float vx, float vy, float s, Color tint)
    {
        if (tex.Id == 0) return;

        float w = tex.Width * s;
        float h = tex.Height * s;
        // 2Pもテクスチャ自体は1Pと同じ向きを保つ。
        var src = new Rectangle(0, 0, tex.Width, tex.Height);
        var dest = new Rectangle(pos.X * s + vx, pos.Y * s + vy, w, h);
        Raylib.DrawTexturePro(tex, src, dest, new Vector2(w / 2f, h / 2f), 0f, tint);
    }

    // --- 追加: 爆発エフェクトの描画メソッド ---
    private static void DrawExplosionEffect(float hitX, double le, float vx, float vy, float s, bool isP2)
    {
        // 爆発の再生時間（0.35秒）＋余韻の時間（例：0.15秒）で合計0.5秒間存在させる
        const double FADE_OUT_START = 0.35;
        const double TOTAL_LIFETIME = 0.5;

        if (_explosionTex.Id == 0 || le < 0 || le >= TOTAL_LIFETIME) return;

        // 1. アニメーション処理（0.35秒まで）
        float progress = (float)(Math.Min(le, FADE_OUT_START) / FADE_OUT_START);
        int frame = (int)(progress * (EXPLOSION_FRAMES - 1));

        // 2. フェードアウト処理（0.35秒〜0.5秒の間）
        float alpha = 1.0f;
        if (le > FADE_OUT_START)
        {
            alpha = 1.0f - (float)((le - FADE_OUT_START) / (TOTAL_LIFETIME - FADE_OUT_START));
        }

        float frameWidth = (float)_explosionTex.Width / EXPLOSION_FRAMES;
        // 2Pでも爆発テクスチャの向きは1Pと共通にする。
        var src = new Rectangle(frame * frameWidth, 0, frameWidth, _explosionTex.Height);
        Vector2 endPos = PosForPlayer(1f, hitX, isP2);

        // 加算合成(Additive)しません。通常合成で描画する。
        Color tint = new Color((byte)255, (byte)255, (byte)255, (byte)(255 * alpha));
        Raylib.DrawTexturePro(_explosionTex, src, new Rectangle(endPos.X * s + vx, endPos.Y * s + vy, frameWidth * s, _explosionTex.Height * s), new Vector2(frameWidth * s / 2f, _explosionTex.Height * s / 2f), 0f, tint);
    }

    // 風船: 着地後 BalloonHoldSec かけて黄色の l をフェードインし、ノーツごと(虹と同時に)消える
    private static void DrawBalloonFlight(int kind, float hitX, double elapsed, float vx, float vy, float s, bool isP2)
    {
        double dur = _p[Duration];
        if (elapsed >= dur + BalloonHoldSec) return;

        float t = Math.Min((float)(elapsed / dur), 1f);
        if (elapsed < dur)
        {
            DrawTex(_wsTex[kind], PosForPlayer(t, hitX, isP2), vx, vy, s, Color.White);
        }

        double le = elapsed - dur;
        if (le >= 0)
        {
            float u = (float)(le / BalloonHoldSec);
            var tint = new Color((byte)0xFF, (byte)0xF8, (byte)0, (byte)(0xFF * 0.8f * u));
            DrawTex(_lTex[kind >= 2 ? 1 : 0], PosForPlayer(1f, hitX, isP2), vx, vy, s, tint);

            // 風船が割れて消えるタイミングで爆発エフェクトを描画
            DrawExplosionEffect(hitX, le, vx, vy, s, isP2);
        }
    }

    private static void DrawFlight(int kind, float hitX, double elapsed, float vx, float vy, float s, bool isP2)
    {
        double dur = _p[Duration];

        if (elapsed < dur + END_HOLD)
        {
            float t = Math.Min((float)(elapsed / dur), 1f);
            DrawTex(_wsTex[kind], PosForPlayer(t, hitX, isP2), vx, vy, s, Color.White);
        }

        double le = elapsed - dur;
        if (le >= 0)
        {
            // 通常ノーツの着地・消失時に爆発エフェクトを描画
            DrawExplosionEffect(hitX, le, vx, vy, s, isP2);

            if (le < L_FADE_IN + L_FADE_OUT)
            {
                Color tint;
                if (le < L_FADE_IN)
                {
                    float u = (float)(le / L_FADE_IN);
                    tint = new Color((byte)255, (byte)(0xDD + (0xFF - 0xDD) * u), (byte)(0xFF * u), (byte)(0xFF * u));
                }
                else
                {
                    float v = (float)((le - L_FADE_IN) / L_FADE_OUT);
                    tint = new Color((byte)255, (byte)255, (byte)255, (byte)(0xFF * (1f - v)));
                }
                DrawTex(_lTex[kind >= 2 ? 1 : 0], PosForPlayer(1f, hitX, isP2), vx, vy, s, tint);
            }
        }
    }

    private static void DrawKEffects(float hitX, double elapsed, float vx, float vy, float s, bool isP2)
    {
        int frame = (int)(elapsed * 60.0);
        Raylib.BeginBlendMode(BlendMode.Additive);
        for (int i = 0; i < KStartFrames.Length; i++)
        {
            int f = frame - KStartFrames[i];
            if (f < 0 || f >= _kTex.Length) continue;
            var pos = new Vector2(KPositions[i].X + (hitX - 618f), KPositions[i].Y);
            if (isP2) pos.Y = EnsoP2.LANE_Y + (Enso.LANE_Y - pos.Y) + P2FlightYOffset;
            DrawTex(_kTex[f], pos, vx, vy, s, Color.White);
        }
        Raylib.EndBlendMode();
    }

    private static void DrawList(List<Note> notes, float vx, float vy, float s, double nowSec)
    {
        foreach (var n in notes)
        {
            double elapsed = nowSec - n.SpawnSec;
            if (elapsed < 0) continue;
            if (n.IsBalloon) DrawBalloonFlight(n.Kind, n.HitX, elapsed, vx, vy, s, n.IsP2);
            else DrawFlight(n.Kind, n.HitX, elapsed, vx, vy, s, n.IsP2);
            if (n.Kind >= 2) DrawKEffects(n.HitX, elapsed, vx, vy, s, n.IsP2);
        }
    }

    public static void Draw(float vx, float vy, float s, double nowSec)
    {
        if (!_loaded) return;
        DrawList(_notes, vx, vy, s, nowSec);
    }

    /// <summary>上下反転した2P用フライノーツを下段レーンに描画する。</summary>
    public static void DrawP2(float vx, float vy, float s, double nowSec)
    {
        if (!_loaded) return;
        DrawList(_p2Notes, vx, vy, s, nowSec);
    }
}
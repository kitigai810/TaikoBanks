using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

public enum EnsoEffectType { None, Good, GoodBig, Ok, OkBig }
public enum EnsoJudgeType { None, Good, Ok, Bad }

/// <summary>
/// 各種判定エフェクト、判定文字スライド、ゴーゴータイム背景、爆発、判定枠の炎などのグラフィックを統合的に処理します。
/// </summary>
public static class EnsoEffect
{
    // ---- 💡 判定枠の炎エフェクト（fire.png）表示用調整パラメータ ----

    /// <summary>ゴーゴー炎エフェクトの表示サイズスケール倍率（数値を大きくすると炎が巨大化します）</summary>
    public static float FireScale = 1f;

    /// <summary>判定枠からの左右方向の表示Xオフセット（+で右、-で左へ移動）</summary>
    public static float FireXOffset = 0f;

    /// <summary>判定枠（LANE_Y）からの上下方向の表示Yオフセット（+で下、-で上へ移動）</summary>
    public static float FireYOffset = 0f;

    // いつもどおりの7分割（横1列に7フレーム並んでいる想定）
    private const int FIRE_FRAMES = 7;


    private struct EffectInstance
    {
        public EnsoEffectType Effect;
        public double SpawnSec;
        public float HitX;
        public float LaneY;
        public bool IsRoll;
    }

    private struct JudgeInstance
    {
        public EnsoJudgeType Judge;
        public double SpawnSec;
        public float HitX;
        public float LaneY;
    }

    private static readonly List<EffectInstance> _effects = new();
    private static readonly List<JudgeInstance> _judgeTexts = new();

    // 判定枠中心に描画されるエフェクト素材
    private static Texture2D _texEffectGood;
    private static Texture2D _texEffectGoodBig;
    private static Texture2D _texEffectOk;
    private static Texture2D _texEffectOkBig;
    private static Texture2D _texJudgeGood;
    private static Texture2D _texJudgeOk;
    private static Texture2D _texJudgeBad;

    // 判定枠上に加算合成で表示する連番アニメーション素材 (Effect/l/)
    private static Texture2D[] _lGood = new Texture2D[4];
    private static Texture2D[] _lGoodBig = new Texture2D[4];
    private static Texture2D[] _lOk = new Texture2D[4];
    private static Texture2D[] _lOkBig = new Texture2D[4];

    // 大判定バースト (各 _ フォルダの _.png)
    private static Texture2D _lGoodBigBurst;
    private static Texture2D _lOkBigBurst;

    // 連番アニメーション: 130msで4枚、最後の30msでフェードアウト
    private const double L_DURATION = 0.13;
    private const double L_FADE = 0.03;
    private const double L_SCALE_IN = 0.03;   // 大音符のみ最初の30msで60%→100%

    // 判定枠中心エフェクト: 310msでフェードアウト
    private const double F_DURATION = 0.31;

    // 判定文字: 60msでy216→235に移動、150ms待機、80msでフェードアウト
    private const double JUDGE_MOVE = 0.06;
    private const double JUDGE_HOLD = 0.15;
    private const double JUDGE_FADE = 0.08;
    private const double JUDGE_DURATION = JUDGE_MOVE + JUDGE_HOLD + JUDGE_FADE;
    private const float JUDGE_X = 618f;
    private const float JUDGE_Y_START = 216f;
    private const float JUDGE_Y_END = 235f;

    // バースト: 80msで60%→100%(イージング)、その後30msでフェードアウト
    private const double BURST_SCALE_DUR = 0.08;
    private const double BURST_FADE = 0.03;
    private const double BURST_DURATION = BURST_SCALE_DUR + BURST_FADE;

    // ゴーゴー演出用
    private static Texture2D _texExplosion;
    private static Texture2D _texFire;

    private static double _gogoExplosionActiveSec = -1.0;
    private const double GOGO_EXPLOSION_DURATION = 1.0;

    private static bool _loaded;

    public static void Init()
    {
        if (_loaded) return;

        _texEffectGood = Raylib.LoadTexture("Lumen/1.Enso/Effect/f/ryo.png");
        _texEffectGoodBig = Raylib.LoadTexture("Lumen/1.Enso/Effect/f/ryo_.png");
        _texEffectOk = Raylib.LoadTexture("Lumen/1.Enso/Effect/f/ka.png");
        _texEffectOkBig = Raylib.LoadTexture("Lumen/1.Enso/Effect/f/ka_.png");
        _texJudgeGood = Raylib.LoadTexture("Lumen/1.Enso/Effect/h/ryo.png");
        _texJudgeOk = Raylib.LoadTexture("Lumen/1.Enso/Effect/h/ka.png");
        _texJudgeBad = Raylib.LoadTexture("Lumen/1.Enso/Effect/h/huka.png");

        for (int i = 0; i < 4; i++)
        {
            _lGood[i] = Raylib.LoadTexture($"Lumen/1.Enso/Effect/l/ryo/{i}.png");
            _lGoodBig[i] = Raylib.LoadTexture($"Lumen/1.Enso/Effect/l/ryo_/{i}.png");
            _lOk[i] = Raylib.LoadTexture($"Lumen/1.Enso/Effect/l/ka/{i}.png");
            _lOkBig[i] = Raylib.LoadTexture($"Lumen/1.Enso/Effect/l/ka_/{i}.png");
        }
        _lGoodBigBurst = Raylib.LoadTexture("Lumen/1.Enso/Effect/l/ryo_/_.png");
        _lOkBigBurst = Raylib.LoadTexture("Lumen/1.Enso/Effect/l/ka_/_.png");

        _texExplosion = Raylib.LoadTexture("Lumen/1.Enso/Effect/explosion.jpg");
        _texFire = Raylib.LoadTexture("Lumen/1.Enso/Effect/GoGo/Fire.png");

        VramProbe.Track("EnsoEffect.Init (all)");
        _loaded = true;
    }

    public static void Unload()
    {
        if (!_loaded) return;

        if (_texEffectGood.Id != 0) Raylib.UnloadTexture(_texEffectGood);
        if (_texEffectGoodBig.Id != 0) Raylib.UnloadTexture(_texEffectGoodBig);
        if (_texEffectOk.Id != 0) Raylib.UnloadTexture(_texEffectOk);
        if (_texEffectOkBig.Id != 0) Raylib.UnloadTexture(_texEffectOkBig);
        if (_texJudgeGood.Id != 0) Raylib.UnloadTexture(_texJudgeGood);
        if (_texJudgeOk.Id != 0) Raylib.UnloadTexture(_texJudgeOk);
        if (_texJudgeBad.Id != 0) Raylib.UnloadTexture(_texJudgeBad);

        for (int i = 0; i < 4; i++)
        {
            if (_lGood[i].Id != 0) Raylib.UnloadTexture(_lGood[i]);
            if (_lGoodBig[i].Id != 0) Raylib.UnloadTexture(_lGoodBig[i]);
            if (_lOk[i].Id != 0) Raylib.UnloadTexture(_lOk[i]);
            if (_lOkBig[i].Id != 0) Raylib.UnloadTexture(_lOkBig[i]);
        }
        if (_lGoodBigBurst.Id != 0) Raylib.UnloadTexture(_lGoodBigBurst);
        if (_lOkBigBurst.Id != 0) Raylib.UnloadTexture(_lOkBigBurst);

        if (_texExplosion.Id != 0) Raylib.UnloadTexture(_texExplosion);
        if (_texFire.Id != 0) Raylib.UnloadTexture(_texFire);

        _effects.Clear();
        _judgeTexts.Clear();
        _gogoExplosionActiveSec = -1.0;
        _loaded = false;
    }

    /// <summary>
    /// 1P既存呼び出し用。レーン座標は1Pの判定ラインに設定する。
    /// </summary>
    public static void AddHitEffect(EnsoEffectType effect, EnsoJudgeType judge, double nowSec, float hitX, bool isRoll = false)
    {
        AddHitEffect(effect, judge, nowSec, hitX, Enso.LANE_Y, isRoll);
    }

    /// <summary>
    /// 任意プレイヤーのレーン座標に打撃エフェクトと判定文字を追加する。
    /// テクスチャは共有し、生成時に座標だけを保持するため1P/2Pの表示状態は混ざらない。
    /// </summary>
    public static void AddHitEffect(EnsoEffectType effect, EnsoJudgeType judge, double nowSec,
        float hitX, float laneY, bool isRoll = false)
    {
        if (effect != EnsoEffectType.None)
        {
            _effects.Add(new EffectInstance
            {
                Effect = effect,
                SpawnSec = nowSec,
                HitX = hitX,
                LaneY = laneY,
                IsRoll = isRoll
            });
        }

        if (judge != EnsoJudgeType.None)
        {
            _judgeTexts.Add(new JudgeInstance
            {
                Judge = judge,
                SpawnSec = nowSec,
                HitX = hitX,
                LaneY = laneY
            });
        }
    }

    public static void TriggerGoGoExplosion(double nowSec)
    {
        _gogoExplosionActiveSec = nowSec;
    }

    public static void Update(double nowSec)
    {
        _effects.RemoveAll(e => nowSec - e.SpawnSec >= F_DURATION);
        _judgeTexts.RemoveAll(j => nowSec - j.SpawnSec >= JUDGE_DURATION);
    }

    // 判定枠中心エフェクト (Effect/f) をノーツより後ろに描画する
    public static void DrawBehindNotes(float vx, float vy, float s, float LANE_Y, double nowSec)
    {
        if (!_loaded) return;

        foreach (var eff in _effects)
        {
            double elapsed = nowSec - eff.SpawnSec;
            if (elapsed < 0 || elapsed >= F_DURATION) continue;

            Texture2D effTex = eff.Effect switch
            {
                EnsoEffectType.Good => _texEffectGood,
                EnsoEffectType.GoodBig => _texEffectGoodBig,
                EnsoEffectType.Ok => _texEffectOk,
                EnsoEffectType.OkBig => _texEffectOkBig,
                _ => default
            };

            if (effTex.Id == 0) continue;

            float hx = eff.HitX * s + vx;
            float hy = eff.LaneY * s + vy;
            float w = effTex.Width * s;
            float h = effTex.Height * s;

            byte alpha = (byte)(Math.Clamp(1.0 - elapsed / F_DURATION, 0.0, 1.0) * 255);

            Rectangle src = new Rectangle(0, 0, effTex.Width, effTex.Height);
            Rectangle dest = new Rectangle(hx, hy, w, h);
            Vector2 origin = new Vector2(w / 2f, h / 2f);

            Raylib.DrawTexturePro(effTex, src, dest, origin, 0f,
                new Color((byte)255, (byte)255, (byte)255, alpha));
        }
    }

    // 判定枠上の連番アニメーション (Effect/l/ 加算合成) を Haikei_H より上に描画する
    // 130msで4枚、最後の30msでフェードアウト。大音符は最初の30msで60%から拡大
    public static void DrawLAnimation(float vx, float vy, float s, float LANE_Y, double nowSec)
    {
        if (!_loaded) return;

        foreach (var eff in _effects)
        {
            if (eff.IsRoll) continue;

            double elapsed = nowSec - eff.SpawnSec;
            if (elapsed < 0 || elapsed >= L_DURATION) continue;

            Texture2D[] frames = eff.Effect switch
            {
                EnsoEffectType.Good => _lGood,
                EnsoEffectType.GoodBig => _lGoodBig,
                EnsoEffectType.Ok => _lOk,
                EnsoEffectType.OkBig => _lOkBig,
                _ => null
            };

            if (frames == null) continue;

            int frameIndex = (int)(elapsed / L_DURATION * 4);
            if (frameIndex > 3) frameIndex = 3;
            Texture2D tex = frames[frameIndex];
            if (tex.Id == 0) continue;

            bool isBig = eff.Effect == EnsoEffectType.GoodBig || eff.Effect == EnsoEffectType.OkBig;

            float scale = 1f;
            if (isBig && elapsed < L_SCALE_IN)
                scale = 0.6f + 0.4f * (float)(elapsed / L_SCALE_IN);

            byte alpha = 255;
            if (elapsed >= L_DURATION - L_FADE)
                alpha = (byte)(Math.Clamp((L_DURATION - elapsed) / L_FADE, 0.0, 1.0) * 255);

            float hx = eff.HitX * s + vx;
            float hy = eff.LaneY * s + vy;
            float w = tex.Width * s * scale;
            float h = tex.Height * s * scale;

            Rectangle src = new Rectangle(0, 0, tex.Width, tex.Height);
            Rectangle dest = new Rectangle(hx, hy, w, h);
            Vector2 origin = new Vector2(w / 2f, h / 2f);

            Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.DrawTexturePro(tex, src, dest, origin, 0f,
                new Color((byte)255, (byte)255, (byte)255, alpha));
            Raylib.EndBlendMode();
        }
    }

    // 判定文字 (Effect/h/) を Effect/l より上に描画する
    // 中心x618固定、y216→235に60msで移動、150ms待機後80msでフェードアウト
    public static void DrawJudgeTexts(float vx, float vy, float s, double nowSec)
    {
        if (!_loaded) return;

        foreach (var jt in _judgeTexts)
        {
            double elapsed = nowSec - jt.SpawnSec;
            if (elapsed < 0 || elapsed >= JUDGE_DURATION) continue;

            Texture2D judgeTex = jt.Judge switch
            {
                EnsoJudgeType.Good => _texJudgeGood,
                EnsoJudgeType.Ok => _texJudgeOk,
                EnsoJudgeType.Bad => _texJudgeBad,
                _ => default
            };

            if (judgeTex.Id == 0) continue;

            float moveT = (float)Math.Clamp(elapsed / JUDGE_MOVE, 0.0, 1.0);
            float judgeYStart = jt.LaneY + (JUDGE_Y_START - Enso.LANE_Y);
            float judgeYEnd = jt.LaneY + (JUDGE_Y_END - Enso.LANE_Y);
            float jx = jt.HitX * s + vx;
            float jy = (judgeYStart + (judgeYEnd - judgeYStart) * moveT) * s + vy;

            byte jAlpha = 255;
            if (elapsed >= JUDGE_MOVE + JUDGE_HOLD)
                jAlpha = (byte)(Math.Clamp((JUDGE_DURATION - elapsed) / JUDGE_FADE, 0.0, 1.0) * 255);

            float w = judgeTex.Width * s;
            float h = judgeTex.Height * s;

            Rectangle src = new Rectangle(0, 0, judgeTex.Width, judgeTex.Height);
            Rectangle dest = new Rectangle(jx, jy, w, h);
            Vector2 origin = new Vector2(w / 2f, 0f);

            Raylib.DrawTexturePro(judgeTex, src, dest, origin, 0f,
                new Color((byte)255, (byte)255, (byte)255, jAlpha));
        }
    }

    /// <summary>
    /// 💡 GoGoTime 継続中の炎アニメーション (Fire.png) のみの描画処理。
    /// 横一列に7枚に分割されたいつもの分割アニメーションとしてループ再生します。
    /// </summary>
    public static void DrawFire(float vx, float vy, float s, float LANE_Y, float currentHitX, double nowSec, bool isGoGo)
    {
        if (!_loaded) return;

        if (isGoGo && _texFire.Id != 0)
        {
            // 7分割の横方向ループ処理
            double loopDuration = 0.5; // ループを1周させる全体の秒数（0.5秒）
            double fireElapsed = nowSec % loopDuration;
            int frame = (int)((fireElapsed / loopDuration) * FIRE_FRAMES);
            frame = Math.Clamp(frame, 0, FIRE_FRAMES - 1);

            float frameW = (float)_texFire.Width / FIRE_FRAMES;
            float frameH = (float)_texFire.Height;

            float w = frameW * s * FireScale;
            float h = frameH * s * FireScale;

            // 横一列に切り出す
            Rectangle src = new Rectangle(frame * frameW, 0f, frameW, frameH);

            float destX = (currentHitX + FireXOffset) * s + vx;
            float destY = (LANE_Y + FireYOffset) * s + vy;
            Rectangle dest = new Rectangle(destX, destY, w, h);
            Vector2 origin = new Vector2(w / 2f, h / 2f);

            // 一番下で目立たせるために加算合成（Additive）で描画
            Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.DrawTexturePro(_texFire, src, dest, origin, 0f, Color.White);
            Raylib.EndBlendMode();
        }
    }

    /// <summary>
    /// 各種判定エフェクト、文字、ゴーゴー爆発エフェクトの描画
    /// </summary>
    public static void Draw(float vx, float vy, float s, float LANE_Y, float currentHitX, double nowSec, bool isGoGo)
    {
        if (!_loaded) return;

        // 1. 判定エフェクトの描画
        foreach (var eff in _effects)
        {
            double elapsed = nowSec - eff.SpawnSec;
            if (elapsed < 0 || elapsed >= F_DURATION) continue;

            float hx = eff.HitX * s + vx;
            float hy = eff.LaneY * s + vy;

            // 大判定バースト (_.png / 加算なし)
            // 80msで60%→100%にイージング付きで拡大し、その後30msでフェードアウト
            if (elapsed < BURST_DURATION)
            {
                Texture2D burstTex = eff.Effect switch
                {
                    EnsoEffectType.GoodBig => _lGoodBigBurst,
                    EnsoEffectType.OkBig => _lOkBigBurst,
                    _ => default
                };

                if (burstTex.Id != 0)
                {
                    float scale = 1f;
                    if (elapsed < BURST_SCALE_DUR)
                    {
                        float t = (float)(elapsed / BURST_SCALE_DUR);
                        float eased = 1f - (1f - t) * (1f - t) * (1f - t);
                        scale = 0.6f + 0.4f * eased;
                    }

                    byte alpha = 255;
                    if (elapsed >= BURST_SCALE_DUR)
                        alpha = (byte)(Math.Clamp((BURST_DURATION - elapsed) / BURST_FADE, 0.0, 1.0) * 255);

                    float w = burstTex.Width * s * scale;
                    float h = burstTex.Height * s * scale;

                    Rectangle src = new Rectangle(0, 0, burstTex.Width, burstTex.Height);
                    Rectangle dest = new Rectangle(hx, hy, w, h);
                    Vector2 origin = new Vector2(w / 2f, h / 2f);

                    Raylib.DrawTexturePro(burstTex, src, dest, origin, 0f,
                        new Color((byte)255, (byte)255, (byte)255, alpha));
                }
            }
        }

        // 2. GoGo開始時の大爆発エフェクトの描画 (explosion.jpg)
        if (_gogoExplosionActiveSec >= 0 && _loaded && _texExplosion.Id != 0)
        {
            double elapsedG = nowSec - _gogoExplosionActiveSec;
            if (elapsedG >= 0 && elapsedG < GOGO_EXPLOSION_DURATION)
            {
                int cols = 6;
                int rows = 5;
                int totalFrames = 30;

                int frame = (int)((elapsedG / GOGO_EXPLOSION_DURATION) * totalFrames);
                frame = Math.Clamp(frame, 0, totalFrames - 1);

                int col = frame % cols;
                int row = frame / cols;

                float frameW = _texExplosion.Width / (float)cols;
                float frameH = _texExplosion.Height / (float)rows;

                float hx = currentHitX * s + vx;
                float hy = LANE_Y * s + vy;

                // 爆発を大きく見せるためのスケーリング
                float sizeScale = 2.0f;
                float w = frameW * s * sizeScale;
                float h = frameH * s * sizeScale;

                Rectangle src = new Rectangle(col * frameW, row * frameH, frameW, frameH);
                Rectangle dest = new Rectangle(hx, hy, w, h);
                Vector2 origin = new Vector2(w / 2f, h / 2f);

                // 黒背景の爆発素材を綺麗に抜くために加算合成（Additive Blend）を使用
                Raylib.BeginBlendMode(BlendMode.Additive);
                Raylib.DrawTexturePro(_texExplosion, src, dest, origin, 0f, Color.White);
                Raylib.EndBlendMode();
            }
        }
    }
}
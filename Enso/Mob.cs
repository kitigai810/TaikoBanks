using System;
using System.IO;
using Raylib_cs;

/// <summary>
/// 魂ゲージの状態に応じてアニメーションを制御するMobクラス。
/// </summary>
public static class Mob
{
    private static Aup2Anim _anim;
    private static float _currentFrame;
    private static float _lastSoulValue = 0f;
    private static bool _isEnding = false;
    private static bool _active = false;

    // 60fps換算の精密なフレーム設定
    private const float INTRO_END_FRAME = 10f;   // 0.16秒 = 10フレーム（登場演出）
    private const float LOOP_END_FRAME = 93f;    // 1.55秒 = 93フレーム（ループ折り返し位置）

    public static void Init()
    {
        // 💡 修正: 同じファイルを毎回ディスクから読み直さない。
        //    以前はF2リトライのたびにDispose→再Loadしており、リークは無くなっても
        //    「毎回ファイルI/O+テクスチャアップロードし直す」重さは残っていた。
        //    未ロード時だけ読み込み、以降はアニメ状態だけリセットして使い回す。
        if (_anim == null)
        {
            string animPath = Path.Combine(AppContext.BaseDirectory, "Lumen", "1.Enso", "Mob", "1", "Anime.aup2");
            _anim = Aup2Anim.Load(animPath);
        }

        _currentFrame = 0f;
        _lastSoulValue = 0f;
        _isEnding = false;
        _active = (_anim != null);
    }

    public static void Update(float soulValue, float deltaTime)
    {
        if (!_active) return;

        // 魂ゲージが100%になったとき
        if (soulValue >= 100f)
        {
            if (_lastSoulValue < 100f)
            {
                // 新しく100%に到達：登場（0f）から再生開始
                _currentFrame = 0f;
                _isEnding = false;
            }
            else if (!_isEnding)
            {
                // 100%維持中：順方向にフレーム進行
                _currentFrame += deltaTime * 60f;

                // イントロ（10f）終了後、LOOP_END_FRAME（93f）までの間をループ
                if (_currentFrame >= LOOP_END_FRAME)
                {
                    float loopLen = LOOP_END_FRAME - INTRO_END_FRAME; // 83フレーム分（約1.39秒）
                    if (loopLen > 0.1f)
                    {
                        float excess = _currentFrame - LOOP_END_FRAME;
                        _currentFrame = INTRO_END_FRAME + (excess % loopLen);
                    }
                    else
                    {
                        _currentFrame = INTRO_END_FRAME;
                    }
                }
            }
        }
        // 魂ゲージが100%から減少したとき（退場処理）
        else if (_lastSoulValue >= 100f && soulValue < 100f)
        {
            // 登場演出（10f〜0f）を逆再生してハケる
            _isEnding = true;

            // スムーズに逆再生へ繋ぐため、現在フレームをループ開始位置（10f）に合わせる
            if (_currentFrame >= INTRO_END_FRAME)
            {
                _currentFrame = INTRO_END_FRAME;
            }
        }

        // 逆再生退場アニメーション中
        if (_isEnding)
        {
            // 登場時と同じ速度（0.16秒）で逆方向にフレームを戻す
            _currentFrame -= deltaTime * 60f;

            // 完全に画面外に戻ったら、非表示＆状態初期化
            if (_currentFrame <= 0f)
            {
                _currentFrame = 0f;
                _isEnding = false;
            }
        }

        _lastSoulValue = soulValue;
    }

    public static void Draw(float vx, float vy, float s)
    {
        if (!_active || _anim == null) return;

        // 100%維持しているか、または退場逆再生中のみ描画
        if (_lastSoulValue >= 100f || _isEnding)
        {
            float w = _anim.SceneWidth * s;
            float h = _anim.SceneHeight * s;

            // Aup2Anim.Draw は秒指定なので、フレーム数を秒に変換して渡す
            float seconds = _currentFrame / 60f;
            _anim.Draw(vx, vy, w, h, seconds, Color.White);
        }
    }

    public static void Unload()
    {
        _anim?.Dispose();
        _anim = null;
        _active = false;
        _currentFrame = 0f;
        _lastSoulValue = 0f;
        _isEnding = false;
    }

    private static float GetMaxFrame()
    {
        if (_anim == null) return 0f;
        return (float)(_anim.Duration * 60f) - 1f;
    }
}
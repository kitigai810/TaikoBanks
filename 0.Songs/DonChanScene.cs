using System;
using Raylib_cs;

/// <summary>
/// SongSelect / Result 画面など、Enso以外の場所で「どんちゃん」を1体だけ表示するための軽量ラッパー。
/// DonChanEnso（演奏画面用、コンボ/ゴーゴー/風船などのTier管理付き）と違い、
/// こちらは「今どのアニメを流すか」を呼び出し側が毎フレーム needle 文字列で直接指定するだけのシンプル版。
///
/// DonChan3D 自体は DonChanEnso / Kisekae とも共有される常駐リソースなので、
/// ここでは Init() は呼ぶが Unload() は呼ばない（呼ぶと他画面が使用中のリソースを失う）。
/// </summary>
public static class DonChanScene
{
    // ---- 表示位置・大きさ（呼び出し側の画面基準に合わせて自由に変更可能） ----
    public static float X = 40f;
    public static float BottomY = 900f;
    public static float Width = 1200f;
    public static float CamPosZ = 26f;
    public static float CamFovY = 0.66f;
    // DonChan3Dのレンダーバッファは820x599固定なのでその比率に合わせる
    private const float BufAspect = 599f / 820f;

    private static bool _initialized;
    private static string _appliedNeedle;

    // 💡 決定演出などで一度だけ再生したいアニメーション用のフラグ。
    //    これがtrueの間は、Update(needle)側の「毎フレーム強制的にneedleへ戻す」処理を止めておく。
    private static bool _oneShotActive;

    public static void Init()
    {
        DonChan3D.CamPosX = 0f;
        DonChan3D.CamPosY = 0f;
        DonChan3D.CamPosZ = CamPosZ;
        DonChan3D.CamFovY = CamFovY;
        DonChan3D.TarPosY = 0f;
        DonChan3D.ModelRotX = 181.25f;
        DonChan3D.ModelRotY = 27.5f;
        DonChan3D.ModelRotZ = 0f;
        DonChan3D.Init(); // 既にロード済みなら内部でスキップされる（多重呼び出し安全）
        // DrawSimple()はCosOnly/BodyHeadOnlyの時しか実際には描画しないため、明示的に設定する
        // （Allのままだと何も表示されない＝今回の「表示されない」の直接原因）
        //DonChan3D.CurrentDrawMode = DonChan3D.DrawMode.CosOnly;

        _initialized = true;
        _appliedNeedle = null;
    }

    /// <summary>
    /// 毎フレーム呼ぶ。needle には DonChan3D.GetMappedFolderName() が対応させている
    /// アニメ名の手掛かり（例: "don_result_clear_loop"）をそのまま渡す。常にループ再生。
    ///
    /// DonChan3D は Enso(DonChanEnso) とも共有している常駐リソースなので、演奏画面から戻ってきた直後は
    /// 「別のアニメが再生中」「単発再生モード(LoopAnimation=false)のまま止まっている」ことがある。
    /// 自前のキャッシュ(_appliedNeedle)だけで判断せず、実際に今流れているアニメ名も毎フレーム確認して
    /// ズレていたら強制的に正しい状態へ戻す（＝これが無いと「演奏から戻るとアニメが戻らない/固まる」原因になる）。
    /// </summary>
    public static void Update(string needle)
    {
        if (!_initialized) return;

        // 💡 EnsoやKisekaeがCurrentDrawModeを書き換えたまま演奏を終えると、共有リソースである
        //    DonChan3D側の状態がAll等に残り続けてこの画面で何も描画されなくなる
        //    (DrawSimpleはCosOnly/BodyHeadOnlyの時しか描かない)。毎フレーム強制的にCosOnlyへ
        //    戻すことで、どのシーンから戻ってきても復帰できるようにする。
        //DonChan3D.CurrentDrawMode = DonChan3D.DrawMode.CosOnly;

        DonChan3D.CamPosZ = CamPosZ;
        DonChan3D.CamFovY = CamFovY;

        // 💡 ワンショット再生中は、needleとの不一致を検知して強制的に戻す通常ロジックをスキップする。
        //    再生が終わったら通常のneedle駆動に復帰する。
        if (_oneShotActive)
        {
            if (DonChan3D.IsCurrentAnimFinished)
            {
                _oneShotActive = false;
                _appliedNeedle = null; // 次フレームで通常needleへ確実に切り替わるようキャッシュを捨てる
            }
            else
            {
                DonChan3D.Update();
                return;
            }
        }

        string actualNeedle = DonChan3D.GetAnimationNameById(DonChan3D.GetCurrentAnimationIndex())?.ToLowerInvariant() ?? "";
        bool mismatched = string.IsNullOrEmpty(needle) || !actualNeedle.Contains(needle.ToLowerInvariant());

        if (needle != _appliedNeedle || mismatched || !DonChan3D.LoopAnimation)
        {
            DonChan3D.LoopAnimation = true; // Ensoの単発アニメでfalseのまま止まっていた場合も強制的に戻す
            if (DonChan3D.SetAnimationByName(needle)) _appliedNeedle = needle;
        }

        DonChan3D.Update();
    }

    /// <summary>
    /// 決定演出などで、通常のneedleループ再生を一時中断して指定アニメーションを1回だけ再生する。
    /// 再生終了（IsCurrentAnimFinished）を検知すると自動的に通常のUpdate(needle)駆動へ戻る。
    /// </summary>
    public static void PlayOneShot(string needle)
    {
        if (!_initialized || string.IsNullOrWhiteSpace(needle)) return;

        DonChan3D.LoopAnimation = false;
        if (DonChan3D.SetAnimationByName(needle))
        {
            _oneShotActive = true;
            _appliedNeedle = needle;
        }
    }

    public static void Draw(float vx, float vy, float s)
    {
        if (!_initialized) return;

        float w = Width * s;
        float h = w * BufAspect;
        float dx = X * s + vx;
        float dy = BottomY * s + vy - h;

        var dest = new Rectangle(dx, dy, w, h);
        try
        {
            // 💡 このDraw()はProgram.cs側のBeginTextureMode(_virtualScreen)の中から呼ばれる。
            //    DrawSimple()内部はBeginTextureMode(_renderBuffer)→EndTextureMode()という
            //    ネストしたレンダーターゲット切り替えを行うが、_insideVirtualFrame=falseのままだと
            //    最後に_virtualScreenへ描画対象を戻す処理がスキップされ、このフレームの残り全部の
            //    描画がおかしくなる(色化け・以降の要素が消える等)。DonChanEnso.csと同様に、
            //    呼び出し前後でこのフラグを明示的に管理する。
            DonChan3D._insideVirtualFrame = true;
            DonChan3D.DrawSimple(dest);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[DonChanScene] DrawSimple ERROR: " + ex);
        }
        finally
        {
            DonChan3D._insideVirtualFrame = false;
        }
    }
}
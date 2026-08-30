using Raylib_cs;
using System.Numerics;
using System.Collections.Generic;

/// <summary>
/// タイトル画面(選曲画面より前に表示)
/// F または J キーを押すと次の画面(選曲)へ進む
/// </summary>
public static class TitleScene
{
    public static bool Confirmed { get; private set; }

    // 💡 Confirmed=true時点で「段位道場」が選択されていたかどうか(Program.cs側の遷移先分岐用)
    public static bool DanMode { get; private set; }

    // 💡 常時ループするBGM本体(Title.ogg)。シーン中ずっと鳴り続ける
    internal static MusicTrack? _musicTitle;
    private static bool _musicTitleLoaded;

    // 💡 EntryStartBar(最初の画面)専用のループSE
    internal static MusicTrack? _seStart;
    private static bool _seStartLoaded;

    // 💡 Account_Clear.aup2再生時の単発SE
    internal static MusicTrack? _musicBanaClear;
    private static bool _musicBanaClearLoaded;

    // 💡 TaikoSelect2.aup2ループ中のBGM
    internal static MusicTrack? _musicSelectPlaySide;
    private static bool _musicSelectPlaySideLoaded;

    // 💡 ModeBar_enso.png表示時: SelectMode.ogg(頭出し・非ループ)→終了検知→EnsoGame.ogg(ループ)
    internal static MusicTrack? _musicSelectMode;
    private static bool _musicSelectModeLoaded;
    // EntryStartBarでF/Jスキップした直後に鳴らすJoin.ogg
    internal static MusicTrack? _musicJoin;
    private static bool _musicJoinLoaded;
    private static bool _modeBarPlayingJoin;

    internal static MusicTrack? _musicEnsoGame;
    private static bool _musicEnsoGameLoaded;
    private static bool _modeBarPlayingIntro;
    private static bool _ignoreModeBarInputThisFrame;

    private static bool _initialized;

    private static float _blinkCounter;

    private static Texture2D _bgTex;
    private static Texture2D _modeBarTex;
    private static Texture2D _modeBarFlashTex;
    private static Texture2D _ensoCharaLTex;
    private static Texture2D _ensoCharaRTex;
    private static Texture2D _ensoTex;
    private static Texture2D _overlayTex;
    // 💡 段位道場(ModeBar_dan状態)専用のテクスチャ。演奏ゲーム(ModeBar_enso)と2択で切り替える
    //     Ensochara1_L/R.png↔Danchara2_L/R.png、Enso.png↔Dan.pngがそれぞれ対応する
    private static Texture2D _modeBarDanTex;
    private static Texture2D _danCharaLTex;
    private static Texture2D _danCharaRTex;
    private static Texture2D _danTex;
    // 💡 「演奏ゲーム」「段位道場」の2択のうち、現在どちらが選択(展開)されているか
    //     false=演奏ゲームが展開・段位道場が細いバー / true=段位道場が展開・演奏ゲームが細いバー
    private static bool _danSelected;
    private static bool _showingModeBar;
    private static bool _modeBarFadingOut;
    private static float _modeBarFadeTimer;
    private const float MODEBAR_FADE_DURATION = 0.5f; // 💡 暗転にかける時間(秒)
    private static Texture2D _entryStartBarTex;
    private static Texture2D _buttonLeftTex;
    private static Texture2D _buttonRightTex;
    private static Texture2D _buttonCancelTex;
    // 💡 選択中(フラッシュ)状態のテクスチャ・通常ボタン画像の背面に重ねて表示する
    //     Left/Rightが選択された時: Button_Cancel_Flash.png を共用(背面)
    //     Cancelが選択された時: Button_Cancel_FlashBase.png(背面)
    //     通常のボタン画像(Button_Left/Right/Cancel.png)は選択有無に関わらず常に前面に表示
    private static Texture2D _buttonFlashTex;
    private static Texture2D _buttonCancelBaseTex;
    private static Texture2D[] _btnTexCache;
    private static Texture2D[] _btnFlashTexCache;

    // 💡 TaikoSelect2画面でのボタン選択状態(0=Left, 1=Right, 2=Cancel)
    private static int _select2ChoiceIndex;

    // 💡 ボタン画像(TaikoSelect2ループ中のみ表示)の位置・拡大率調整用パラメータ
    private static float BTN_LEFT_X = 450f;
    private static float BTN_LEFT_Y = 550f;
    private static float BTN_LEFT_SCALE = 0.67f;
    private static float BTN_RIGHT_X = 850f;
    private static float BTN_RIGHT_Y = 550f;
    private static float BTN_RIGHT_SCALE = 0.67f;
    private static float BTN_CANCEL_X = 650f;
    private static float BTN_CANCEL_Y = 550f;
    private static float BTN_CANCEL_SCALE = 0.67f;

    // 💡 ネームプレート(TaikoSelect2ループ中のみ表示)の位置・大きさ調整用パラメータ(基準:1280x720)
    private static float NAMEPLATE_X = 525f;
    private static float NAMEPLATE_Y = 85f;
    private static float NAMEPLATE_SCALE = 0.67f;

    // 💡 どんちゃん(エントリー演出 don_entry_loop)の位置・大きさ・再生FPS調整用パラメータ
    //     FPSはこの画面専用の値(DonChan3D本体のPlaybackFpsを再生開始時に書き換えて使う)
    //     大きさはDONCHAN_ENTRY_SCALE一つで縦横比を保ったまま一括調整できる(基準サイズ×SCALE)
    private static float DONCHAN_ENTRY_X = 640f;
    private static float DONCHAN_ENTRY_Y = 400f;
    private static float DONCHAN_ENTRY_BASE_WIDTH = 400f;
    private static float DONCHAN_ENTRY_BASE_HEIGHT = 292f;
    private static float DONCHAN_ENTRY_SCALE = 2f;
    private static float DONCHAN_ENTRY_FPS = 60f;

    // ==================================================================
    // 💡 どんちゃん(DonChan)とネームプレート(NamePlate)の表示位置調整用パラメータ
    //    （数値を直接書き換えるだけで調整できます）
    //    ※ModeBar_enso.png画面(Left/Right決定後)専用の位置
    // ==================================================================
    public static float DonChanX = -220f;           // どんちゃんの左端X

    public static float DonChanBottomY = 900f;    // どんちゃんの下端Y
    public static float DonChanWidth = 740f;      // どんちゃんの幅

    public static float NamePlateGlobalX = 40f;   // ModeBar画面でのネームプレートX(NamePlate.Draw()に直接渡す・共有GlobalX/Yは使わない)
    public static float NamePlateGlobalY = 315f;  // ModeBar画面でのネームプレートY
    public static float NamePlateGlobalScale = 0.67f; // ModeBar画面でのネームプレート拡大率

    // 💡 ModeBar_enso.png(演奏ゲーム)の位置・大きさ調整用パラメータ。展開時(選択中)と細いバー(非選択)の2状態分ある
    //     縦方向を9-patch(3分割)で伸ばすので、上下の角が細長く潰れず綺麗に伸縮する
    private static float MODEBAR_X = 302.5f;       // 展開時のX
    private static float MODEBAR_Y = 224.5f;       // 展開時のY
    private static float MODEBAR_WIDTH = 1025f;
    private static float MODEBAR_HEIGHT = 417.5f;
    private static float MODEBAR_SCALE = 0.655f; // 💡 展開時の全体の大きさを一括調整(縦横比はWIDTH:HEIGHTのまま拡縮)
    // 💡 非選択時(細いバー)のX,Y位置・高さ(幅は展開時と共通:MODEBAR_WIDTH*MODEBAR_SCALE)
    private static float MODEBAR_THIN_X = 275f;
    private static float MODEBAR_THIN_Y = 100f;
    private static float MODEBAR_THIN_HEIGHT = 120f;
    //     上下左右バラバラに指定できる9-patch境界(px, 原寸基準)。角の丸み・端の形が潰れないよう
    //     実際の画像を見ながら「角の絵柄が入っている範囲」より少し広めに調整してください
    //     ※ModeBar_enso.png実測値(1000x180px、角の丸みは端から約60〜65px)に基づく値
    private static int MODEBAR_BORDER_LEFT = 75;
    private static int MODEBAR_BORDER_RIGHT = 70;
    private static int MODEBAR_BORDER_TOP = 65;
    private static int MODEBAR_BORDER_BOTTOM = 65;

    // 💡 ModeBar_dan.png(段位道場)の位置・大きさ調整用パラメータ。ModeBar_enso.pngと全く同じ形式(9-patch)で調整する
    private static float MODEBAR_DAN_X = 302.5f;   // 展開時のX
    private static float MODEBAR_DAN_Y = 224.5f;     // 展開時のY
    private static float MODEBAR_DAN_WIDTH = 1025f;
    private static float MODEBAR_DAN_HEIGHT = 417.5f;
    private static float MODEBAR_DAN_SCALE = 0.655f; // 💡 展開時の全体の大きさを一括調整(縦横比はWIDTH:HEIGHTのまま拡縮)
    // 💡 非選択時(細いバー)のX,Y位置・高さ(幅は展開時と共通:MODEBAR_DAN_WIDTH*MODEBAR_DAN_SCALE)
    private static float MODEBAR_DAN_THIN_X = 350f;
    private static float MODEBAR_DAN_THIN_Y = 500f;
    private static float MODEBAR_DAN_THIN_HEIGHT = 120f;
    //     上下左右バラバラに指定できる9-patch境界(px, 原寸基準)。実際のModeBar_dan.png画像を見ながら調整してください
    private static int MODEBAR_DAN_BORDER_LEFT = 75;
    private static int MODEBAR_DAN_BORDER_RIGHT = 70;
    private static int MODEBAR_DAN_BORDER_TOP = 65;
    private static int MODEBAR_DAN_BORDER_BOTTOM = 65;



    // 💡 ModeBar_Flash.png(帯の光る縁取り)の位置・大きさ調整用パラメータ
    //     ModeBar_ensoと同じく9-patchで伸縮する(実測1200x300px、角の光暈は端から約100〜110px)
    private static float MODEBAR_FLASH_X = 252.5f;
    private static float MODEBAR_FLASH_Y = 192.5f;
    private static float MODEBAR_FLASH_WIDTH = 1180f;
    private static float MODEBAR_FLASH_HEIGHT = 515f;
    private static float MODEBAR_FLASH_SCALE = 0.655f;
    private static float MODEBAR_FLASH_SIZE_SCALE = 1.045f; // 💡 中心位置を保ったまま全体の大きさだけ調整(1.0が基準)
    private static int MODEBAR_FLASH_BORDER_LEFT = 105;
    private static int MODEBAR_FLASH_BORDER_RIGHT = 105;
    private static int MODEBAR_FLASH_BORDER_TOP = 105;
    private static int MODEBAR_FLASH_BORDER_BOTTOM = 100;

    // 💡 Ensochara1_L.png / Ensochara1_R.png(帯の左右に立つキャラ)・Enso.png(円のマーク等)
    //     それぞれ中心座標(X,Y)と大きさ(SCALE)だけで調整できる(原寸の縦横比のまま拡縮)
    private static float ENSOCHARA_L_X = 382.5f;
    private static float ENSOCHARA_L_Y = 365f;
    private static float ENSOCHARA_L_SCALE = 0.655f;
    private static float ENSOCHARA_R_X = 895f;
    private static float ENSOCHARA_R_Y = 372.5f;
    private static float ENSOCHARA_R_SCALE = 0.655f;
    private static float ENSO_X = 640f;
    private static float ENSO_Y = 290f;
    private static float ENSO_SCALE = 0.655f;

    // 💡 Overlay.png(帯の上に重ねる装飾)。ModeBar_ensoと同じく9-patchで伸縮する
    //     ※実測値(980x160px)に基づく境界値。画像端から丸みの終わる位置までの絶対距離で計測
    private static float OVERLAY_X = 302.5f;
    private static float OVERLAY_Y = 312.5f;
    private static float OVERLAY_WIDTH = 1025f;
    private static float OVERLAY_HEIGHT = 275f;
    private static float OVERLAY_SCALE = 0.655f;
    private static int OVERLAY_BORDER_LEFT = 105;
    private static int OVERLAY_BORDER_RIGHT = 105;
    private static int OVERLAY_BORDER_TOP = 50;
    private static int OVERLAY_BORDER_BOTTOM = 50;

    // 💡 ModeBar画面(帯・どんちゃん・ネームプレート全部まとめて)の全体スケール
    //     1.0が基準、小さくすると全部まとめて縮小&原点(0,0)寄りに移動する
    private static float MODEBAR_GROUP_SCALE = 1.0f;

    // 💡 Pキーで再生するAUP2演出(Account_Connecting→Account_Clear→TaikoSelect2ループの順で再生)
    private static Aup2Anim _connectingAnim;
    private static Aup2Anim _clearAnim;
    private static Aup2Anim _taikoSelect2Anim;
    private static bool _playingConnecting;
    private static bool _playingClear;
    private static bool _playingTaikoSelect2Loop;
    private static float _animTimer;
    // 💡 TaikoSelect2ループ開始からの経過時間(モジュロなし・フェード判定専用)
    private static float _select2PhaseTimer;
    // 💡 TaikoSelect2ループ開始直後、sin波で透明度0%→100%にフェードインする時間(秒)
    //     ※120fps基準で0.64秒(60fps基準0.32秒相当)
    private const float SELECT2_FADE_DURATION = 0.32f;

    // 💡 EntryStartBar(スタートバー)の位置調整用パラメータ(1本目・2本目それぞれ中心座標)
    private static float BAR1_X = 640f;
    private static float BAR1_Y = 450f;
    private static float BAR1_SCALE = 0.67f; // 1本目の拡大率(1.0=原寸)
    private static float BAR2_X = 640f;
    private static float BAR2_Y = 325f;
    private static float BAR2_SCALE = 0.67f; // 2本目の拡大率(1.0=原寸)

    // 💡 EntryStartBarに重ねる「一人プレイ」「二人プレイ」ラベルの位置・文字サイズ調整用パラメータ
    //     縁取り白・本体色黒。X,Yは中央基準(BAR1/BAR2と同じ座標系:1280x720)
    private static float ENTRY_TEXT1_X = 375f;
    private static float ENTRY_TEXT1_Y = 325f;
    private static float ENTRY_TEXT1_SIZE = 36f;
    private static string ENTRY_TEXT1_STRING = "１人プレイ";
    private static float ENTRY_TEXT2_X = 375f;
    private static float ENTRY_TEXT2_Y = 450f;
    private static float ENTRY_TEXT2_SIZE = 36f;
    private static string ENTRY_TEXT2_STRING = "２人プレイ";
    private const float ENTRY_TEXT_OUTLINE_THICKNESS = 6f;

    // 💡 ガイドテキスト「太鼓をたたいてスタート!」の位置調整用パラメータ(1個目・2個目)
    private static float GUIDE1_X = 700f;
    private static float GUIDE1_Y = 307.5f;
    private static float GUIDE2_X = 700f;
    private static float GUIDE2_Y = 432.5f;
    private static float GUIDE_SIZE = 36f; // 文字サイズ(基準:1280x720)

    /// <summary>タイトル画面に表示するガイドテキスト。設定画面から変更可能。</summary>
    public static string TitleGuideText = "太鼓をたたいてスタート！";

    private static bool _animationsPreloaded;

    public static void PreloadAnimations()
    {
        if (_animationsPreloaded) return;

        _connectingAnim = Aup2Anim.Load("Lumen/-1.Title/Bana/Account_Connecting.aup2");
        _clearAnim = Aup2Anim.Load("Lumen/-1.Title/Bana/Account_Clear.aup2");
        _taikoSelect2Anim = Aup2Anim.Load("Lumen/-1.Title/TaikoSelect/TaikoSelect2.aup2");
        _animationsPreloaded = true;
    }

    public static void Init()
    {
        if (_initialized) return;

        _bgTex = Raylib.LoadTexture("Lumen/-1.Title/Background.png");
        _modeBarTex = Raylib.LoadTexture("Lumen/-1.Title/Mode/ModeBar_enso.png");
        _modeBarFlashTex = Raylib.LoadTexture("Lumen/-1.Title/Mode/ModeBar_Flash.png");
        _ensoCharaLTex = Raylib.LoadTexture("Lumen/-1.Title/Mode/Ensochara1_L.png");
        _ensoCharaRTex = Raylib.LoadTexture("Lumen/-1.Title/Mode/Ensochara1_R.png");
        _ensoTex = Raylib.LoadTexture("Lumen/-1.Title/Mode/Enso.png");
        _overlayTex = Raylib.LoadTexture("Lumen/-1.Title/Mode/Overlay.png");
        _modeBarDanTex = Raylib.LoadTexture("Lumen/-1.Title/Mode/ModeBar_dan.png");
        _danCharaLTex = Raylib.LoadTexture("Lumen/-1.Title/Mode/Danchara2_L.png");
        _danCharaRTex = Raylib.LoadTexture("Lumen/-1.Title/Mode/Danchara2_R.png");
        _danTex = Raylib.LoadTexture("Lumen/-1.Title/Mode/Dan.png");
        _entryStartBarTex = Raylib.LoadTexture("Lumen/-1.Title/Start/EntryStartBar.png");

        PreloadAnimations();

        _buttonLeftTex = Raylib.LoadTexture("Lumen/-1.Title/TaikoSelect/Button_Left.png");
        _buttonRightTex = Raylib.LoadTexture("Lumen/-1.Title/TaikoSelect/Button_Right.png");
        _buttonCancelTex = Raylib.LoadTexture("Lumen/-1.Title/TaikoSelect/Button_Cancel.png");
        _buttonFlashTex = Raylib.LoadTexture("Lumen/-1.Title/TaikoSelect/Button_Cancel_Flash.png");
        _buttonCancelBaseTex = Raylib.LoadTexture("Lumen/-1.Title/TaikoSelect/Button_Cancel_FlashBase.png");

        NamePlate.Init(); // 💡 TaikoSelect2ループ中に表示するネームプレート
        DonChan3D.Init(); // 💡 TaikoSelect2ループ突入時にdon_entry_loopアニメーションを再生するどんちゃん

        _musicTitle = MusicTrack.Load("Lumen/2.sound/BGM/Title.ogg");
        _musicTitleLoaded = _musicTitle.Loaded;
        if (_musicTitleLoaded)
        {
            _musicTitle.Volume = SettingsPanel.EffectiveBgmVolume;
            _musicTitle.Looping = true;
            _musicTitle.Play();
        }
        else
        {
            // 💡 MusicTrackはファイル未検出/AudioEngine未初期化時に例外を出さず黙ってLoaded=falseになるため、
            //    無音のまま気づけない事故を防ぐためここでログを出す。
            System.Console.WriteLine($"[TitleScene] BGM読み込み失敗: Lumen/2.sound/BGM/Title.ogg (AudioEngine.IsReady={AudioEngine.IsReady}, File.Exists={System.IO.File.Exists("Lumen/2.sound/BGM/Title.ogg")})");
        }

        _seStart = MusicTrack.Load("Lumen/2.sound/Title/Start.ogg");
        _seStartLoaded = _seStart.Loaded;
        if (_seStartLoaded)
        {
            _seStart.Volume = SettingsPanel.EffectiveBgmVolume;
            _seStart.Looping = true;
            // 💡 ここでは再生しない。Demo画面中に漏れないよう、ResumeBgm()(Demo終了時)で
            //    _musicTitleと一緒に再生開始する。
        }

        _musicBanaClear = MusicTrack.Load("Lumen/2.sound/Title/BanaClear.ogg");
        _musicBanaClearLoaded = _musicBanaClear.Loaded;
        if (_musicBanaClearLoaded) _musicBanaClear.Volume = SettingsPanel.EffectiveBgmVolume;

        _musicSelectPlaySide = MusicTrack.Load("Lumen/2.sound/Title/SelectPlaySide.ogg");
        _musicSelectPlaySideLoaded = _musicSelectPlaySide.Loaded;
        if (_musicSelectPlaySideLoaded) _musicSelectPlaySide.Volume = SettingsPanel.EffectiveBgmVolume;

        _musicJoin = MusicTrack.Load("Lumen/2.sound/Title/Join.ogg");
        _musicJoinLoaded = _musicJoin.Loaded;
        if (_musicJoinLoaded) _musicJoin.Volume = SettingsPanel.EffectiveBgmVolume;

        _musicSelectMode = MusicTrack.Load("Lumen/2.sound/Title/SelectMode.ogg");
        _musicSelectModeLoaded = _musicSelectMode.Loaded;
        if (_musicSelectModeLoaded) _musicSelectMode.Volume = SettingsPanel.EffectiveBgmVolume;

        _musicEnsoGame = MusicTrack.Load("Lumen/2.sound/Title/EnsoGame.ogg");
        _musicEnsoGameLoaded = _musicEnsoGame.Loaded;
        if (_musicEnsoGameLoaded) _musicEnsoGame.Volume = SettingsPanel.EffectiveBgmVolume;

        Confirmed = false;
        _blinkCounter = 0f;
        _initialized = true;
    }

    /// <summary>
    /// 💡 Demoシーン再生中などにTitle BGMを一時停止するために呼ぶ(SongSelectScene.PauseMenuBgm()と同じ用途)
    /// </summary>
    public static void PauseBgm()
    {
        if (_musicTitleLoaded) _musicTitle!.Pause();
        if (_seStartLoaded) _seStart!.Pause();
    }

    /// <summary>
    /// 💡 Demoシーンからタイトルへ遷移した際にBGMを再開するために呼ぶ
    /// </summary>
    public static void ResumeBgm()
    {
        if (_musicTitleLoaded) _musicTitle!.Play();
        if (_seStartLoaded) _seStart!.Play();
    }

    /// <summary>
    /// 再度タイトル画面に戻ってきた際にフラグをリセットする
    /// </summary>
    public static void ResetState()
    {
        Confirmed = false;
        DanMode = false;
        _blinkCounter = 0f;
        _playingConnecting = false;
        _playingClear = false;
        _playingTaikoSelect2Loop = false;
        _showingModeBar = false;
        _modeBarFadingOut = false;
        _modeBarFadeTimer = 0f;
        _animTimer = 0f;
        _select2PhaseTimer = 0f;
        _select2ChoiceIndex = 0;
        _modeBarPlayingIntro = false;
        _modeBarPlayingJoin = false;
        _danSelected = false;
        if (_seStartLoaded) _seStart!.Stop();
        if (_musicJoinLoaded) _musicJoin!.Stop();

        if (_musicBanaClearLoaded) _musicBanaClear!.Stop();
        if (_musicSelectPlaySideLoaded) _musicSelectPlaySide!.Stop();
        if (_musicSelectModeLoaded) _musicSelectMode!.Stop();
        if (_musicEnsoGameLoaded) _musicEnsoGame!.Stop();
    }

    public static void Update(float dt)
    {
        _blinkCounter += dt;
        _ignoreModeBarInputThisFrame = false;

        // EntryStartBar.png表示中のF/Jは、アカウント演出を待たずに
        // ModeBar_enso.pngへ進むショートカットとして扱う。
        if (!_playingConnecting && !_playingClear && !_playingTaikoSelect2Loop && !_showingModeBar &&
            (Raylib.IsKeyPressed(KeyboardKey.F) || Raylib.IsKeyPressed(KeyboardKey.J)))
        {
            // F/J中は通常のPlayData.jsonを変更せず、guestPlayData.jsonを表示する。
            NamePlate.UseGuestPlayerData();
            BeginModeBar(playJoin: true);
            // F/Jで遷移した同じフレームのキー入力をModeBarの操作として再利用しない。
            _ignoreModeBarInputThisFrame = true;
        }

        // EntryStartBarまたはModeBar表示中のPで、Account_Connecting→Account_Clear→
        // TaikoSelect2の再選択演出を開始する。
        if (Raylib.IsKeyPressed(KeyboardKey.P) && !_playingConnecting && !_playingClear && !_playingTaikoSelect2Loop && _connectingAnim != null)
        {
            _playingConnecting = true;
            _playingClear = false;
            _playingTaikoSelect2Loop = false;
            _showingModeBar = false;
            _modeBarFadingOut = false;
            _modeBarPlayingIntro = false;
            _modeBarPlayingJoin = false;
            _animTimer = 0f;

            // PキーではguestPlayData.jsonから通常のPlayData.jsonへ戻す。
            // そのため元のJSON内容は一切上書きされない。
            NamePlate.UseNormalPlayerData();

            // ModeBarから戻る場合は、その画面用の音声を止める。
            if (_musicJoinLoaded) _musicJoin!.Stop();
            if (_musicSelectModeLoaded) _musicSelectMode!.Stop();
            if (_musicEnsoGameLoaded) _musicEnsoGame!.Stop();
            if (_seStartLoaded) _seStart!.Stop();
        }

        if (_playingConnecting)
        {
            _animTimer += dt;
            if (_animTimer >= (float)_connectingAnim.Duration)
            {
                _playingConnecting = false;
                if (_clearAnim != null)
                {
                    _playingClear = true;
                    _animTimer = 0f;

                    // 💡 Account_Clear.aup2再生開始と同時にBanaClear.oggを単発再生
                    if (_musicBanaClearLoaded)
                    {
                        _musicBanaClear!.Looping = false;
                        _musicBanaClear.Play();
                    }
                }
            }
        }
        else if (_playingClear)
        {
            _animTimer += dt;
            if (_animTimer >= (float)_clearAnim.Duration)
            {
                _playingClear = false;
                if (_taikoSelect2Anim != null)
                {
                    _playingTaikoSelect2Loop = true;
                    _animTimer = 0f;
                    _select2PhaseTimer = 0f; // 💡 フェード判定用タイマーもリセット

                    // 💡 TaikoSelect2.aup2ループ開始と同時にSelectPlaySide.oggをループ再生
                    if (_musicSelectPlaySideLoaded)
                    {
                        _musicSelectPlaySide!.Looping = true;
                        _musicSelectPlaySide.Play();
                    }

                    // 💡 どんちゃん登場演出(don_entry_in)をこの画面専用のFPSでループ再生
                    if (DonChan3D.IsLoaded)
                    {
                        DonChan3D.PlaybackFps = DONCHAN_ENTRY_FPS;
                        DonChan3D.LoopAnimation = true;
                        DonChan3D.SetAnimationByName("don_entry_loop");
                    }
                }
            }
        }
        else if (_playingTaikoSelect2Loop)
        {
            _animTimer += dt;
            float loopDuration = (float)_taikoSelect2Anim.Duration;
            if (loopDuration > 0f && _animTimer >= loopDuration)
            {
                _animTimer %= loopDuration; // 💡 ずっとループさせる
            }

            // 💡 開始からSELECT2_FADE_DURATION秒まではsin波フェード判定用に加算し、
            //     それ以降は不透明ループになるだけなので値が増え続けても問題ない
            _select2PhaseTimer += dt;

            // 💡 ボタン選択操作(青カ:KeyEdgeL/KeyEdgeRで選択移動、赤ドン:KeyLeft/KeyRightで決定)
            //     Ka1=L(左)キー→左へ移動、Ka2=R(右)キー→右へ移動
            if (AnyKeyPressed(SettingsPanel.KeyEdgeL))
            {
                _select2ChoiceIndex = (_select2ChoiceIndex + 1) % 3; // 💡 左へ移動(Right→Cancel→Left→Rightの順で巡回)
            }
            if (AnyKeyPressed(SettingsPanel.KeyEdgeR))
            {
                _select2ChoiceIndex = (_select2ChoiceIndex + 2) % 3; // 💡 右へ移動(Left→Cancel→Right→Leftの順で巡回)
            }
            if (AnyKeyPressed(SettingsPanel.KeyLeft) || AnyKeyPressed(SettingsPanel.KeyRight))
            {
                ConfirmSelect2Choice(); // 💡 決定
            }

            // 💡 どんちゃん(don_entry_loop)のアニメーションフレームを更新(ループ再生)
            if (DonChan3D.IsLoaded) DonChan3D.Update();
        }
        else if (_showingModeBar)
        {
            // 💡 ModeBar_enso.png表示中もどんちゃんはループ再生させ続ける
            if (DonChan3D.IsLoaded) DonChan3D.Update();

            // 💡 Ka2キー(SettingsPanel.KeyEdgeR)を押すと「段位道場」を選択(展開)、
            //     Ka1キー(SettingsPanel.KeyEdgeL)を押すと「演奏ゲーム」を選択(展開)に戻す
            if (!_modeBarFadingOut && !_ignoreModeBarInputThisFrame)
            {
                // F/JでModeBarへ入った後は、D=演奏ゲーム、K=段位道場。
                // 設定側のEdgeキーも従来どおり利用できる。
                bool keyboardDan = Raylib.IsKeyPressed(KeyboardKey.K);
                bool keyboardEnso = Raylib.IsKeyPressed(KeyboardKey.D);
                if (keyboardDan || AnyKeyPressed(SettingsPanel.KeyEdgeR))
                {
                    _danSelected = true;
                }
                else if (keyboardEnso || AnyKeyPressed(SettingsPanel.KeyEdgeL))
                {
                    _danSelected = false;
                }
            }

            // F/Jスキップ時はJoin.ogg終了後にSelectMode.oggへ切り替える。
            if (_modeBarPlayingJoin && _musicJoinLoaded && _musicJoin!.Ended)
            {
                _modeBarPlayingJoin = false;
                if (_musicSelectModeLoaded)
                {
                    _musicSelectMode!.Looping = false;
                    _musicSelectMode.Play();
                    _modeBarPlayingIntro = true;
                }
                else if (_musicEnsoGameLoaded)
                {
                    _musicEnsoGame!.Looping = true;
                    _musicEnsoGame.Play();
                }
            }

            // 💡 SelectMode.ogg(頭出し)再生終了を検知したらEnsoGame.oggのループ再生に切り替える
            //    (SongSelectScene の Start→Loop 切り替えと同じ方式)
            if (_modeBarPlayingIntro && _musicSelectModeLoaded && _musicSelectMode!.Ended)

            {
                _modeBarPlayingIntro = false;
                if (_musicEnsoGameLoaded)
                {
                    _musicEnsoGame!.Looping = true;
                    _musicEnsoGame.Play();
                }
            }

            if (!_modeBarFadingOut)
            {
                // 💡 赤ドン(Don1/Don2 = KeyLeft/KeyRight)のどちらかを押したら暗転を開始
                // D/KはModeBarのモード切替専用。Pは上段でAccount演出を開始するため、
                // ここではD/K/Pを決定キーとして扱わない。
                bool directModeKey = Raylib.IsKeyPressed(KeyboardKey.D) || Raylib.IsKeyPressed(KeyboardKey.K);
                bool pKey = Raylib.IsKeyPressed(KeyboardKey.P);
                if (!_ignoreModeBarInputThisFrame && !directModeKey && !pKey &&
                    (AnyKeyPressed(SettingsPanel.KeyLeft) || AnyKeyPressed(SettingsPanel.KeyRight)))
                {
                    _modeBarFadingOut = true;
                    _modeBarFadeTimer = 0f;
                }

            }
            else
            {
                _modeBarFadeTimer += dt;
                if (_modeBarFadeTimer >= MODEBAR_FADE_DURATION)
                {
                    // 💡 暗転完了 → 選曲画面へ(Program.cs側がConfirmedを見てSongSelectに遷移する)
                    if (_musicTitleLoaded) _musicTitle!.Stop();
                    if (_musicSelectModeLoaded) _musicSelectMode!.Stop();
                    if (_musicEnsoGameLoaded) _musicEnsoGame!.Stop();
                    DanMode = _danSelected; // 💡 段位道場が選択されていたかをここで確定(ResetState()で戻される前にProgram.cs側が読む)
                    Confirmed = true;
                }
            }
        }
    }

    private static void BeginModeBar(bool playJoin = false)
    {
        _playingConnecting = false;
        _playingClear = false;
        _playingTaikoSelect2Loop = false;
        _showingModeBar = true;
        _modeBarFadingOut = false;
        _modeBarFadeTimer = 0f;
        _danSelected = false;
        _modeBarPlayingJoin = false;
        _modeBarPlayingIntro = false;

        if (_seStartLoaded) _seStart!.Stop();
        if (playJoin && _musicJoinLoaded)
        {
            _musicJoin!.Looping = false;
            _musicJoin.Play();
            _modeBarPlayingJoin = true;
        }
        else if (_musicSelectModeLoaded)
        {
            _musicSelectMode!.Looping = false;
            _musicSelectMode.Play();
            _modeBarPlayingIntro = true;
        }
        else if (_musicEnsoGameLoaded)
        {
            _musicEnsoGame!.Looping = true;
            _musicEnsoGame.Play();
            _modeBarPlayingIntro = false;
        }
    }

    /// <summary>
    /// TaikoSelect2画面でLeft/Right/Cancelのいずれかが決定された時の処理
    /// </summary>
    private static void ConfirmSelect2Choice()

    {
        switch (_select2ChoiceIndex)
        {
            case 0: // Left決定
            case 1: // Right決定
                // 再選択時はData/PlayData.jsonを読み直し、SongSelect等でも
                // NamePlate.Draw()が同じプレイヤーデータを参照できるようにする。
                NamePlate.LoadPlayerData();

                // 💡 aup2ループを終了し、ModeBar_enso.pngを表示する(どんちゃん・ネームプレートはDraw側で専用パラメータを直接使う)
                _playingTaikoSelect2Loop = false;
                _showingModeBar = true;
                _danSelected = false; // 💡 表示開始時は必ず「演奏ゲーム」が選択(展開)された状態から始める

                // 💡 TaikoSelect2ループ中のSelectPlaySide.oggを止め、SelectMode.oggの頭出し再生を開始
                if (_musicSelectPlaySideLoaded) _musicSelectPlaySide!.Stop();
                if (_musicSelectModeLoaded)
                {
                    _musicSelectMode!.Looping = false;
                    _musicSelectMode.Play();
                    _modeBarPlayingIntro = true;
                }
                else if (_musicEnsoGameLoaded)
                {
                    // 💡 SelectMode.ogg未ロード時はEnsoGame.oggを直接ループ再生
                    _musicEnsoGame!.Looping = true;
                    _musicEnsoGame.Play();
                    _modeBarPlayingIntro = false;
                }
                break;
            case 2: // Cancel決定
                // 💡 EntryStartBar.pngの状態(最初の画面)に戻る
                _playingConnecting = false;
                _playingClear = false;
                _playingTaikoSelect2Loop = false;
                _showingModeBar = false;
                _select2ChoiceIndex = 0;

                // 💡 TaikoSelect2ループ中のSelectPlaySide.oggを止め、Start.oggのループ(SE)を再開
                if (_musicSelectPlaySideLoaded) _musicSelectPlaySide!.Stop();
                if (_seStartLoaded)
                {
                    _seStart!.Looping = true;
                    _seStart.Play();
                }
                break;
        }
    }

    private static bool AnyKeyPressed(List<int> keys)
    {
        if (keys == null) return false;
        for (int i = 0; i < keys.Count; i++)
        {
            if (Raylib.IsKeyPressed((KeyboardKey)keys[i])) return true;
        }
        return false;
    }

    public static void Draw()
    {
        int sw = Program.VirtualWidth;
        int sh = Program.VirtualHeight;
        float scale = sw / 1280f;

        Raylib.ClearBackground(Color.Black);

        if (_bgTex.Id != 0)
        {
            // 画面いっぱいに背景を敷き詰める(アスペクト比は保持しつつ幅基準で拡大)
            float bgAspect = (float)_bgTex.Width / _bgTex.Height;
            float destW = sw;
            float destH = destW / bgAspect;
            float destY = (sh - destH) / 2f;

            Raylib.DrawTexturePro(_bgTex,
                new Rectangle(0, 0, _bgTex.Width, _bgTex.Height),
                new Rectangle(0, destY, destW, destH),
                Vector2.Zero, 0f, Color.White);
        }

        bool isPlayingAup2 = _playingConnecting || _playingClear || _playingTaikoSelect2Loop || _showingModeBar;

        // EntryStartBar(縦に2個・位置は上の調整用パラメータで変更可能)
        // 💡 Pキーの演出中は非表示
        if (_entryStartBarTex.Id != 0 && !isPlayingAup2)
        {
            DrawEntryStartBar(BAR1_X * scale, BAR1_Y * scale, scale * BAR1_SCALE);
            DrawEntryStartBar(BAR2_X * scale, BAR2_Y * scale, scale * BAR2_SCALE);

            DrawEntryStartBarLabel(ENTRY_TEXT1_STRING, ENTRY_TEXT1_X * scale, ENTRY_TEXT1_Y * scale, ENTRY_TEXT1_SIZE * scale);
            DrawEntryStartBarLabel(ENTRY_TEXT2_STRING, ENTRY_TEXT2_X * scale, ENTRY_TEXT2_Y * scale, ENTRY_TEXT2_SIZE * scale);
        }

        // 点滅するガイドテキスト「太鼓をたたいてスタート!」
        // 2秒待機 → 0.25秒で消える → 0.25秒で復活 → 繰り返し
        // 💡 Pキーの演出中は非表示
        if (!isPlayingAup2)
        {
            const float WAIT_SEC = 2.0f;
            const float FADE_OUT_SEC = 0.25f;
            const float FADE_IN_SEC = 0.25f;
            const float CYCLE_SEC = WAIT_SEC + FADE_OUT_SEC + FADE_IN_SEC;

            float cycleT = _blinkCounter % CYCLE_SEC;
            float animAlpha;
            if (cycleT < WAIT_SEC)
            {
                animAlpha = 1f;
            }
            else if (cycleT < WAIT_SEC + FADE_OUT_SEC)
            {
                float p = (cycleT - WAIT_SEC) / FADE_OUT_SEC; // 0→1
                animAlpha = 0.5f + 0.5f * (float)System.Math.Cos(p * System.Math.PI); // 1→0
            }
            else
            {
                float p = (cycleT - WAIT_SEC - FADE_OUT_SEC) / FADE_IN_SEC; // 0→1
                animAlpha = 0.5f - 0.5f * (float)System.Math.Cos(p * System.Math.PI); // 0→1
            }
            byte textAlpha = (byte)(255 * animAlpha);

            string guideText = TitleGuideText;
            float guideSize = GUIDE_SIZE * scale;
            Vector2 guideMeasure = Raylib.MeasureTextEx(G.Font, guideText, guideSize, 2);
            Color guideColor = new Color((byte)255, (byte)255, (byte)255, textAlpha);
            Color guideOutlineColor = new Color((byte)0, (byte)0, (byte)0, textAlpha);

            G.DrawTextWithOutline16(G.Font, guideText,
                new Vector2(GUIDE1_X * scale - guideMeasure.X / 2f, GUIDE1_Y * scale), guideSize, guideColor, guideOutlineColor);
            G.DrawTextWithOutline16(G.Font, guideText,
                new Vector2(GUIDE2_X * scale - guideMeasure.X / 2f, GUIDE2_Y * scale), guideSize, guideColor, guideOutlineColor);
        }

        // 💡 Pキーの演出中は周りを少し暗くする(暗さはDIM_ALPHAで調整可能・aup2自体には影響しない)
        //     ただしTaikoSelect2ループ中は暗転させない
        const byte DIM_ALPHA = 120; // 0=暗くしない, 255=真っ黒
        if (_playingConnecting || _playingClear)
        {
            Raylib.DrawRectangle(0, 0, sw, sh, new Color((byte)0, (byte)0, (byte)0, DIM_ALPHA));
        }

        // Pキーで再生されるAUP2演出(Account_Connecting → Account_Clear → TaikoSelect2ループ)
        if (_playingConnecting && _connectingAnim != null)
        {
            _connectingAnim.Draw(0, 0, sw, sh, _animTimer, Color.White);
        }
        else if (_playingClear && _clearAnim != null)
        {
            _clearAnim.Draw(0, 0, sw, sh, _animTimer, Color.White);
        }
        else if (_playingTaikoSelect2Loop && _taikoSelect2Anim != null)
        {
            // 💡 ループ開始からSELECT2_FADE_DURATION秒間はsin波で透明度0%→100%にフェードイン、
            //     それ以降はsin波を使わない通常の不透明ループにする
            byte select2Alpha;
            if (_select2PhaseTimer < SELECT2_FADE_DURATION)
            {
                float p = _select2PhaseTimer / SELECT2_FADE_DURATION; // 0→1
                float a = 0.5f - 0.5f * (float)System.Math.Cos(p * System.Math.PI); // 0→1
                select2Alpha = (byte)(255 * a);
            }
            else
            {
                select2Alpha = 255; // 💡 sin波を使わない通常ループ(不透明)
            }
            Color select2Color = new Color((byte)255, (byte)255, (byte)255, select2Alpha);

            _taikoSelect2Anim.Draw(0, 0, sw, sh, _animTimer, select2Color);

            // 💡 TaikoSelect2ループ中のみボタン表示(位置・拡大率は上のパラメータで調整可能)
            // 💡 ボタンも同じくフェードイン(透明→不透明)させる
            // 💡 選択中はフラッシュ用テクスチャを背面に重ねて表示する(通常のボタン画像は常に前面に表示)
            //     フラッシュ用画像も対応するボタンと同じ scale * BTN_x_SCALE を適用する
            Texture2D[] btnTex = _btnTexCache ??= new Texture2D[3];
            Texture2D[] btnFlashTex = _btnFlashTexCache ??= new Texture2D[3];
            btnTex[0] = _buttonLeftTex; btnTex[1] = _buttonRightTex; btnTex[2] = _buttonCancelTex;
            btnFlashTex[0] = _buttonFlashTex; btnFlashTex[1] = _buttonFlashTex; btnFlashTex[2] = _buttonCancelBaseTex;
            float[] btnX = { BTN_LEFT_X, BTN_RIGHT_X, BTN_CANCEL_X };
            float[] btnY = { BTN_LEFT_Y, BTN_RIGHT_Y, BTN_CANCEL_Y };
            float[] btnScale = { BTN_LEFT_SCALE, BTN_RIGHT_SCALE, BTN_CANCEL_SCALE };
            for (int i = 0; i < 3; i++)
            {
                if (_select2ChoiceIndex == i && btnFlashTex[i].Id != 0)
                    DrawButton(btnFlashTex[i], btnX[i] * scale, btnY[i] * scale, scale * btnScale[i], select2Color);
                if (btnTex[i].Id != 0)
                    DrawButton(btnTex[i], btnX[i] * scale, btnY[i] * scale, scale * btnScale[i], select2Color);
            }

            // 💡 どんちゃん登場演出(don_entry_loop・位置はDONCHAN_ENTRY_X/Y、大きさはDONCHAN_ENTRY_SCALEで縦横比を保ったまま一括調整可能)
            //     ネームプレートより先に描画することで、ネームプレートが前面に来るようにする
            if (DonChan3D.IsLoaded)
            {
                float dcW = DONCHAN_ENTRY_BASE_WIDTH * DONCHAN_ENTRY_SCALE * scale;
                float dcH = DONCHAN_ENTRY_BASE_HEIGHT * DONCHAN_ENTRY_SCALE * scale;
                Rectangle donChanDest = new Rectangle(
                    DONCHAN_ENTRY_X * scale - dcW / 2f,
                    DONCHAN_ENTRY_Y * scale - dcH / 2f,
                    dcW, dcH);
                // 💡 このDraw()はProgram.cs側のBeginTextureMode(_virtualScreen)内から呼ばれるため、
                //    DrawSimple()内部のネストしたレンダーターゲット切り替えを正しく戻すには
                //    _insideVirtualFrameを明示的に管理する必要がある(未設定だとこのフレームの
                //    残り全部の描画が壊れる)。
                DonChan3D._insideVirtualFrame = true;
                DonChan3D.DrawSimple(donChanDest);
                DonChan3D._insideVirtualFrame = false;
            }

            // 💡 ネームプレート表示(位置はNAMEPLATE_X/Y、大きさはNAMEPLATE_SCALEで調整可能。NamePlate内部のGlobalX/Y/Scaleにも別途加算される)
            // 💡 ボタン等と同じフェードイン(透明→不透明)のアルファ値を適用する
            NamePlate.Draw(NAMEPLATE_X * scale, NAMEPLATE_Y * scale, scale * NAMEPLATE_SCALE, select2Alpha);
        }
        else if (_showingModeBar)
        {
            float gs = MODEBAR_GROUP_SCALE; // 💡 帯・どんちゃん・ネームプレートまとめての全体スケール

            // 💡 帯の光る縁取り(ModeBar_Flash.png)。帯より先に描画して背面に敷く。9-patchで縦横とも伸縮する
            //     現在選択(展開)されている方のバー(演奏ゲーム/段位道場)の位置に追従する
            //     (MODEBAR_FLASH_X/YはEnso基準のオフセットとして扱い、選択中バーの位置に足し込む)
            if (_modeBarFlashTex.Id != 0)
            {
                NPatchInfo flashPatch = new NPatchInfo
                {
                    Source = new Rectangle(0, 0, _modeBarFlashTex.Width, _modeBarFlashTex.Height),
                    Left = MODEBAR_FLASH_BORDER_LEFT,
                    Right = MODEBAR_FLASH_BORDER_RIGHT,
                    Top = MODEBAR_FLASH_BORDER_TOP,
                    Bottom = MODEBAR_FLASH_BORDER_BOTTOM,
                    Layout = NPatchLayout.NinePatch
                };
                float baseBarX = _danSelected ? MODEBAR_DAN_X : MODEBAR_X;
                float baseBarY = _danSelected ? MODEBAR_DAN_Y : MODEBAR_Y;
                float flashOffsetX = MODEBAR_FLASH_X - MODEBAR_X;
                float flashOffsetY = MODEBAR_FLASH_Y - MODEBAR_Y;
                float flashBaseX = baseBarX + flashOffsetX;
                float flashBaseY = baseBarY + flashOffsetY;

                float fW = MODEBAR_FLASH_WIDTH * MODEBAR_FLASH_SCALE * MODEBAR_FLASH_SIZE_SCALE * gs * scale;
                float fH = MODEBAR_FLASH_HEIGHT * MODEBAR_FLASH_SCALE * MODEBAR_FLASH_SIZE_SCALE * gs * scale;
                float fBaseW = MODEBAR_FLASH_WIDTH * MODEBAR_FLASH_SCALE * gs * scale;
                float fBaseH = MODEBAR_FLASH_HEIGHT * MODEBAR_FLASH_SCALE * gs * scale;
                float fCenterX = flashBaseX * gs * scale + fBaseW / 2f;
                float fCenterY = flashBaseY * gs * scale + fBaseH / 2f;
                Rectangle flashDest = new Rectangle(fCenterX - fW / 2f, fCenterY - fH / 2f, fW, fH);
                Raylib.DrawTextureNPatch(_modeBarFlashTex, flashPatch, flashDest, Vector2.Zero, 0f, Color.White);
            }

            // 💡 Left/Right決定後の画面。aup2は表示しない。
            //     「演奏ゲーム」(ModeBar_enso.png)と「段位道場」(ModeBar_dan.png)の2択で、
            //     選択中(_danSelected)の方だけ展開表示、もう片方は細いバー表示になる
            //     (どちらもX・全体幅は展開時と共通。Yと高さだけ展開/細いバーで変わる)
            //     テクスチャが未ロードでもどんちゃん・ネームプレートは表示する
            float ensoW = MODEBAR_WIDTH * MODEBAR_SCALE * gs * scale;
            float ensoExpandedH = MODEBAR_HEIGHT * MODEBAR_SCALE * gs * scale;
            float ensoThinH = MODEBAR_THIN_HEIGHT * gs * scale;
            float ensoExpandedX = MODEBAR_X * gs * scale;
            float ensoThinX = MODEBAR_THIN_X * gs * scale;
            float ensoExpandedY = MODEBAR_Y * gs * scale;
            float ensoThinY = MODEBAR_THIN_Y * gs * scale;

            float danW = MODEBAR_DAN_WIDTH * MODEBAR_DAN_SCALE * gs * scale;
            float danExpandedH = MODEBAR_DAN_HEIGHT * MODEBAR_DAN_SCALE * gs * scale;
            float danThinH = MODEBAR_DAN_THIN_HEIGHT * gs * scale;
            float danExpandedX = MODEBAR_DAN_X * gs * scale;
            float danThinX = MODEBAR_DAN_THIN_X * gs * scale;
            float danExpandedY = MODEBAR_DAN_Y * gs * scale;
            float danThinY = MODEBAR_DAN_THIN_Y * gs * scale;

            // 💡 ModeBar_enso.png本体(演奏ゲーム。選択中なら展開、非選択なら細いバー)
            if (_modeBarTex.Id != 0)
            {
                NPatchInfo mbPatch = new NPatchInfo
                {
                    Source = new Rectangle(0, 0, _modeBarTex.Width, _modeBarTex.Height),
                    Left = MODEBAR_BORDER_LEFT,
                    Right = MODEBAR_BORDER_RIGHT,
                    Top = MODEBAR_BORDER_TOP,
                    Bottom = MODEBAR_BORDER_BOTTOM,
                    Layout = NPatchLayout.NinePatch
                };
                Rectangle mbDest = _danSelected
                    ? new Rectangle(ensoThinX, ensoThinY, ensoW, ensoThinH)
                    : new Rectangle(ensoExpandedX, ensoExpandedY, ensoW, ensoExpandedH);
                Raylib.DrawTextureNPatch(_modeBarTex, mbPatch, mbDest, Vector2.Zero, 0f, Color.White);
            }

            // 💡 ModeBar_dan.png本体(段位道場。選択中なら展開、非選択なら細いバー)
            if (_modeBarDanTex.Id != 0)
            {
                NPatchInfo danPatch = new NPatchInfo
                {
                    Source = new Rectangle(0, 0, _modeBarDanTex.Width, _modeBarDanTex.Height),
                    Left = MODEBAR_DAN_BORDER_LEFT,
                    Right = MODEBAR_DAN_BORDER_RIGHT,
                    Top = MODEBAR_DAN_BORDER_TOP,
                    Bottom = MODEBAR_DAN_BORDER_BOTTOM,
                    Layout = NPatchLayout.NinePatch
                };
                Rectangle danDest = _danSelected
                    ? new Rectangle(danExpandedX, danExpandedY, danW, danExpandedH)
                    : new Rectangle(danThinX, danThinY, danW, danThinH);
                Raylib.DrawTextureNPatch(_modeBarDanTex, danPatch, danDest, Vector2.Zero, 0f, Color.White);
            }


            // 💡 帯の上に重ねるOverlay.png(9-patchで伸縮)。キャラクターより背面になるようここで描画
            //     Flashと同様、OVERLAY_X/YはEnso基準のオフセットとして扱い、選択中バーの位置に追従させる
            if (_overlayTex.Id != 0)
            {
                NPatchInfo overlayPatch = new NPatchInfo
                {
                    Source = new Rectangle(0, 0, _overlayTex.Width, _overlayTex.Height),
                    Left = OVERLAY_BORDER_LEFT,
                    Right = OVERLAY_BORDER_RIGHT,
                    Top = OVERLAY_BORDER_TOP,
                    Bottom = OVERLAY_BORDER_BOTTOM,
                    Layout = NPatchLayout.NinePatch
                };
                float ovBaseX = (_danSelected ? MODEBAR_DAN_X : MODEBAR_X) + (OVERLAY_X - MODEBAR_X);
                float ovBaseY = (_danSelected ? MODEBAR_DAN_Y : MODEBAR_Y) + (OVERLAY_Y - MODEBAR_Y);
                float ovW = OVERLAY_WIDTH * OVERLAY_SCALE * gs * scale;
                float ovH = OVERLAY_HEIGHT * OVERLAY_SCALE * gs * scale;
                Rectangle overlayDest = new Rectangle(ovBaseX * gs * scale, ovBaseY * gs * scale, ovW, ovH);
                Raylib.DrawTextureNPatch(_overlayTex, overlayPatch, overlayDest, Vector2.Zero, 0f, Color.White);
            }

            // 💡 帯の左右のキャラ・中央の丸マーク(それぞれENSOCHARA_L/R・ENSO_のX,Y,SCALEで調整)
            //     展開中(選択中)の項目のキャラだけ表示する(細いバー側は非表示)
            //     演奏ゲーム選択中: Ensochara1_L/R.png・Enso.png / 段位道場選択中: Danchara2_L/R.png・Dan.png
            //     位置もOVERLAYと同様、Ensoの帯位置を基準としたオフセットとして扱い、選択中バーの位置に追従させる
            Texture2D charaLTex = _danSelected ? _danCharaLTex : _ensoCharaLTex;
            Texture2D charaRTex = _danSelected ? _danCharaRTex : _ensoCharaRTex;
            Texture2D markTex = _danSelected ? _danTex : _ensoTex;
            float charaBaseX = _danSelected ? MODEBAR_DAN_X : MODEBAR_X;
            float charaBaseY = _danSelected ? MODEBAR_DAN_Y : MODEBAR_Y;
            float charaLX = charaBaseX + (ENSOCHARA_L_X - MODEBAR_X);
            float charaLY = charaBaseY + (ENSOCHARA_L_Y - MODEBAR_Y);
            float charaRX = charaBaseX + (ENSOCHARA_R_X - MODEBAR_X);
            float charaRY = charaBaseY + (ENSOCHARA_R_Y - MODEBAR_Y);
            float markPosX = charaBaseX + (ENSO_X - MODEBAR_X);
            float markPosY = charaBaseY + (ENSO_Y - MODEBAR_Y);
            if (charaLTex.Id != 0)
                DrawButton(charaLTex, charaLX * gs * scale, charaLY * gs * scale, ENSOCHARA_L_SCALE * gs * scale, Color.White);
            if (charaRTex.Id != 0)
                DrawButton(charaRTex, charaRX * gs * scale, charaRY * gs * scale, ENSOCHARA_R_SCALE * gs * scale, Color.White);
            if (markTex.Id != 0)
                DrawButton(markTex, markPosX * gs * scale, markPosY * gs * scale, ENSO_SCALE * gs * scale, Color.White);

            // 💡 どんちゃん(この画面専用のDonChanX/DonChanBottomY/DonChanWidthで位置・大きさを調整。縦横比は基準サイズに準拠)
            //     ネームプレートより先に描画することで、ネームプレートが前面に来るようにする
            if (DonChan3D.IsLoaded)
            {
                float dcAspect = DONCHAN_ENTRY_BASE_HEIGHT / DONCHAN_ENTRY_BASE_WIDTH;
                float dcW = DonChanWidth * gs * scale;
                float dcH = dcW * dcAspect;
                Rectangle donChanDest = new Rectangle(
                    DonChanX * gs * scale,
                    DonChanBottomY * gs * scale - dcH,
                    dcW, dcH);
                DonChan3D._insideVirtualFrame = true;
                DonChan3D.DrawSimple(donChanDest);
                DonChan3D._insideVirtualFrame = false;
            }

            // 💡 ネームプレート(この画面専用のNamePlateGlobalX/Y/Scaleを直接Draw()に渡す。共有のNamePlate.GlobalX/Yは触らない)
            NamePlate.Draw(NamePlateGlobalX * gs * scale, NamePlateGlobalY * gs * scale, scale * NamePlateGlobalScale * gs, 255);

            // 💡 Donキー(KeyLeft/KeyRight)を押した後の暗転演出(選曲画面へ遷移する前のフェードアウト)
            if (_modeBarFadingOut)
            {
                float p = System.Math.Clamp(_modeBarFadeTimer / MODEBAR_FADE_DURATION, 0f, 1f);
                byte fadeAlpha = (byte)(255 * p);
                Raylib.DrawRectangle(0, 0, sw, sh, new Color((byte)0, (byte)0, (byte)0, fadeAlpha));
            }
        }

        // 💡 Demo画面下部と同じ「フリープレイ／〇コイン」表示
        DrawFreePlayOrCoinBottomText(sw, sh);
    }

    // ==================================================================
    // 💡 下部の「フリープレイ／〇コイン」表示（Demo画面と同様の見た目）
    // ==================================================================
    private const float BOTTOM_STATUS_FONT_SIZE = 40f;
    private const float BOTTOM_STATUS_OUTLINE_THICKNESS = 8f;
    private const float BOTTOM_STATUS_Y = 1030f; // 💡 1920x1080基準の座標(Demo画面と同じ位置)

    private static void DrawFreePlayOrCoinBottomText(int sw, int sh)
    {
        float refScale = sw / 1920f; // 💡 1920x1080基準の座標をそのまま使うためのスケール
        float y = BOTTOM_STATUS_Y * refScale;
        float centerX = sw * 0.5f;
        float fontSize = BOTTOM_STATUS_FONT_SIZE * refScale;
        float outline = BOTTOM_STATUS_OUTLINE_THICKNESS * refScale;

        string text = Coin.FreePlay ? "フリープレイ"
            : Coin.NoCoinInserted ? "コインをいれてね！"
            : $"{Coin.InsertedCount} コイン";

        Vector2 size = Raylib.MeasureTextEx(G.Font, text, fontSize, 0);
        float x = centerX - size.X * 0.5f;
        G.DrawTextWithOutline16(G.Font, text, new Vector2(x, y), fontSize, Color.White, Color.Black, outline, 0);
    }

    // 💡 EntryStartBar上の「一人プレイ／二人プレイ」ラベル描画(中心座標基準・縁取り白/本体黒)
    private static void DrawEntryStartBarLabel(string text, float centerX, float centerY, float fontSize)
    {
        Vector2 size = Raylib.MeasureTextEx(G.Font, text, fontSize, 0);
        float x = centerX - size.X * 0.5f;
        float y = centerY - size.Y * 0.5f;
        G.DrawTextWithOutline16(G.Font, text, new Vector2(x, y), fontSize, Color.Black, Color.White, ENTRY_TEXT_OUTLINE_THICKNESS, 0);
    }

    private static void DrawEntryStartBar(float centerX, float centerY, float scale)
    {
        float w = _entryStartBarTex.Width * scale;
        float h = _entryStartBarTex.Height * scale;
        Raylib.DrawTexturePro(_entryStartBarTex,
            new Rectangle(0, 0, _entryStartBarTex.Width, _entryStartBarTex.Height),
            new Rectangle(centerX, centerY, w, h),
            new Vector2(w / 2f, h / 2f), 0f, Color.White);
    }

    private static void DrawButton(Texture2D tex, float centerX, float centerY, float scale, Color color)
    {
        float w = tex.Width * scale;
        float h = tex.Height * scale;
        Raylib.DrawTexturePro(tex,
            new Rectangle(0, 0, tex.Width, tex.Height),
            new Rectangle(centerX, centerY, w, h),
            new Vector2(w / 2f, h / 2f), 0f, color);
    }
}
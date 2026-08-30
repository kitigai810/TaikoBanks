using Raylib_cs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;

public static class DiffSelectScene
{
    // ==================================================================
    // 💡 どんちゃん(DonChan)とネームプレート(NamePlate)の表示位置調整用パラメータ
    // ==================================================================
    public static float DonChanX = -225f;         // どんちゃんの左端X
    public static float DonChanBottomY = 1350f;   // どんちゃんの下端Y
    public static float DonChanWidth = 980f;      // どんちゃんの幅

    public static float NamePlateGlobalX = 30f;   // ネームプレート全体のX
    public static float NamePlateGlobalY = 970f; // ネームプレート全体のY

    private static Texture2D[] _texDiffTower = new Texture2D[5];
    private static bool[] _diffTowerLoaded = new bool[5];

    // 難易度別半透明背景 (Lumen/0.Songs/diff/backDiff/0~4.png) 0:かんたん 1:ふつう 2:むずかしい 3:おに 4:うらおに
    private static Texture2D[] _texBackDiff = new Texture2D[5];
    private static bool[] _backDiffLoaded = new bool[5];

    // ジャンル名 -> (category内フォルダ名, テーマカラー)
    private static readonly Dictionary<string, (string folder, Color? color)> _genreMap = new()
    {
        { "特集", ("0.other", null) },
        { "ポップス", ("1.pops", new Color((byte)0x1f, (byte)0x4e, (byte)0x4a, (byte)255)) },
        { "キッズ", ("2.kids", new Color((byte)0x9a, (byte)0x39, (byte)0x13, (byte)255)) },
        { "アニメ", ("3.anime", new Color((byte)0xa1, (byte)0x15, (byte)0x4f, (byte)255)) },
        { "ボーカロイド", ("4.vocalo", new Color((byte)0x29, (byte)0x35, (byte)0x46, (byte)255)) },
        { "ボーカロイド曲", ("4.vocalo", new Color((byte)0x29, (byte)0x35, (byte)0x46, (byte)255)) },
        { "ゲームミュージック", ("5.game", new Color((byte)0x4c, (byte)0x1e, (byte)0x79, (byte)255)) },
        { "バラエティ", ("6.variety", new Color((byte)0x18, (byte)0x4b, (byte)0x18, (byte)255)) },
        { "クラシック", ("7.classic", new Color((byte)0x65, (byte)0x42, (byte)0x2f, (byte)255)) },
        { "ナムコオリジナル", ("8.namco", new Color((byte)0xa6, (byte)0x1a, (byte)0x07, (byte)255)) }
    };

    // ジャンルフォルダ名 -> 読み込み済み diff.png テクスチャ
    private static readonly Dictionary<string, Texture2D> _diffGenreTexCache = new();

    private static Texture2D _texCurrentDiff;
    private static bool _currentDiffLoaded = false;
    private static Color _currentGenreColor = Color.White;

    // ジャンル名 -> backs フォルダ内の画像ファイル名 (拡張子なし)
    private static readonly Dictionary<string, string> _genreBackFileMap = new()
    {
        { "特集", "0" },
        { "ポップス", "ポップス" },
        { "キッズ", "キッズ" },
        { "アニメ", "アニメ" },
        { "ボーカロイド", "ボーカロイド" },
        { "ボーカロイド曲", "ボーカロイド" },
        { "ゲームミュージック", "ゲームミュージック" },
        { "バラエティ", "バラエティ" },
        { "クラシック", "クラシック" },
        { "ナムコオリジナル", "ナムコオリジナル" }
    };

    private static readonly Dictionary<string, Texture2D> _backTexCache = new();

    private static Texture2D _texCurrentBg;
    private static bool _currentBgLoaded = false;

    private static int _diffCursor = 3;
    private static int _rightPressCount = 0;
    private static bool _uraOniSelected = false;

    private enum SideSelect { None, Setting, Back }
    private static SideSelect _sideSelect = SideSelect.None;

    private static readonly string[] _diffNames = { "かんたん", "ふつう", "むずかしい", "おに", "うらおに" };
    private static readonly Color[] _courseColors = new Color[]
    {
        new Color(255, 165, 0, 255),
        new Color(100, 200, 100, 255),
        Color.LightGray,
        new Color(255, 20, 147, 255),
        Color.Purple,
    };

    public static bool IsDifficultyConfirmed { get; private set; }
    public static bool IsRenderRequested { get; private set; }
    public static bool BackToSongSelect { get; private set; }
    public static SongSelectScene.SongData CurrentSong { get; private set; }
    public static int ChosenCourseIndex { get; private set; }
    public static string[] DiffNames => _diffNames;

    public static int CurrentSelectionIndex => (_diffCursor == 3 && _uraOniSelected) ? 4 : _diffCursor;

    // タワー調整用
    private static float _towerHeightRatio = 0.75f;
    private static float _towerGapPx = 20f;
    public static float TowerCenterX = 0.55f;
    public static float TowerCenterY = 0.55f;
    public static float TowerScale = 0.45f;

    // Back.png / Setting.png 調整用
    private static Texture2D _texBack;
    private static bool _backLoaded = false;
    private static Texture2D _texSetting;
    private static bool _settingLoaded = false;
    public static float BackSettingScale = 0.3f;
    public static float BackSettingGapPx = 10f;
    public static float BackSettingOffsetY = -85f;
    public static float CursorEffectSmallScale = 1.4f;

    // ジャンル背景 (category/diff.png) 調整用
    public static float BgScale = 0.67f;
    public static float BgOffsetX = 0f;
    public static float BgOffsetY = -60f;
    public static float BgHeightStretch = 1f;

    // ジャンル背景2 (backs) 画面全体の背景 調整用
    public static float Bg2Scale = 1.0f;
    public static float Bg2OffsetX = 0f;
    public static float Bg2OffsetY = 0f;
    public static float Bg2ScrollSpeed = 40f;
    private static float _bg2ScrollX = 0f;

    // 難易度別半透明背景 (backDiff) 調整用
    public static float BackDiffScale = 0.3f;
    public static float BackDiffOffsetX = -512.5f;
    public static float BackDiffOffsetY = -75f;
    public static float BackDiffAlpha = 0.5f;

    // ヘッダー画像 (Lumen/0.Songs/header.png)
    private static Texture2D _texHeader;
    private static bool _headerLoaded = false;
    public static float HeaderScale = 1.0f;
    public static float HeaderOffsetX = 0f;
    public static float HeaderOffsetY = 0f;

    // カーソル枠 (Cursor_Effect.png) 関連
    private static Texture2D _texCursorEffect;
    private static bool _cursorEffectLoaded = false;
    private static int _cursorEffectFrameW = 0;
    private static int _cursorEffectFrameH = 0;
    public static float CursorEffectScale = 1.15f;

    private static Texture2D _texCursorEffectSmall;
    private static bool _cursorEffectSmallLoaded = false;

    private static Texture2D _tex1PDiff;
    private static bool _1pDiffLoaded = false;
    public static float OnePDiffScale = 0.375f;
    public static float OnePDiffOffsetX = 0f;
    public static float OnePDiffOffsetY = -145f;
    public static float OnePDiffDropDistance = 15f;
    public static float OnePDiffDropDuration = 0.2f;
    private static float _1pDiffAnimTimer = 999f;
    private static int _1pDiffPrevSelIndex = -1;

    // 操作説明アニメーション
    private static Aup2Anim _controlGuideAnim;
    private static float _controlGuideTimer = 0f;
    public static float ControlGuideX = 0f;
    public static float ControlGuideY = 285f;
    public static float ControlGuideWidth = 335f;
    public static float ControlGuideHeight = 83.3f;

    // 難易度LEVEL数字
    private static Texture2D _texLevelNumber;
    private static bool _levelNumberLoaded = false;
    private static int _levelNumberFrameW = 0;
    private static int _levelNumberFrameH = 0;

    public static bool DrawSelectedLevelMain = false;
    public static float LevelNumberCenterX = 0.85f;
    public static float LevelNumberCenterY = 0.75f;
    public static float LevelNumberScale = 1.0f;
    public static float LevelNumberSpacing = 0f;

    public static bool DrawLevelOnEachTower = true;
    public static float TowerLevelOffsetX = 10f;
    public static float TowerLevelOffsetY = 60f;
    public static float TowerLevelScale = 0.75f;
    public static float TowerLevelSpacing = -3f;

    // 星 (star.png)
    private static Texture2D _texStar;
    private static bool _starLoaded = false;
    public static bool DrawStarsOnEachTower = true;
    public static float TowerStarOffsetX = 0f;
    public static float TowerStarOffsetY = 80f;
    public static float TowerStarScale = 0.38f;
    public static float TowerStarSpacing = -5f;
    public static int TowerStarGridSize = 10;

    // 曲名・サブタイトル
    public static float TitleCenterX = 0.5f;
    public static float TitleCenterY = 0.175f;
    public static float SubtitleOffsetY = 0.06f;

    private static float _confirmBlinkTimer = 0f;
    private static bool _confirmBlinkShowRight = true;
    private const float ConfirmBlinkInterval = 0.15f;

    // 演奏オプションパネル (Lumen/Ensooption/Background.png)
    private static Texture2D _texOptionBg;
    private static bool _optionBgLoaded = false;
    public static float OptionBgX = 142.5f;
    public static float OptionBgY = 515f;
    public static float OptionBgScale = 0.67f;

    public static float OptionTitleOffsetX = 0f;
    public static float OptionTitleOffsetY = -165f;
    public static float OptionTitleFontSize = 22f;
    private const float OptionSlideDuration = 0.25f;

    private static Texture2D _texOptionOverray;
    private static bool _optionOverrayLoaded = false;
    public static float OverrayCenterX = 142.5f;
    public static float OverrayStartY = 395f;
    public static float OverraySpacingY = 40f;

    private static Texture2D _texOptionOverray2;
    private static bool _optionOverray2Loaded = false;
    public static float Overray2OffsetX = -125f;
    public static float Overray2OffsetY = 0f;
    public static float Overray2Scale = 0.67f;

    private static Texture2D _texOptionArrow;
    private static bool _optionArrowLoaded = false;
    public static float OptionArrowScale = 0.67f;
    public static float OptionArrowGapX = -10f;
    public static float OptionArrowOffsetY = 0f;

    public static float OverrayLabelOffsetX = -115f;
    public static float OverrayLabelFontSize = 17.25f;
    public static float OverrayValueOffsetX = 52.5f;

    private enum OptionPanelState { Hidden, SlidingIn, Shown, SlidingOut }
    private static OptionPanelState _optionState = OptionPanelState.Hidden;
    private static float _optionSlideTimer = 0f;

    private static int _optionCursor = 0;
    private static float _optionScrollSpeed = 1.0f;
    private static int _optionDoron = 0;
    private static int _optionAbekobe = 0;
    private static int _optionRandom = 0;

    public static float SpeedMultiplier => _optionScrollSpeed;
    public static bool DoronEnabled => _optionDoron != 0;
    public static bool AbekobeEnabled => _optionAbekobe != 0;
    public static int RandomMode => _optionRandom;

    // 特殊オプションパネル
    private static OptionPanelState _specialOptionState = OptionPanelState.Hidden;
    private static float _specialOptionSlideTimer = 0f;
    private static bool _pendingSpecialOption = false;

    private static int _specialCursor = 0;
    private static int _optionAuto = 0;
    private static int _optionBpmFixIndex = 0;

    private static readonly float[] _playbackSpeedValues =
    {
        0.5f, 1.0f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.7f, 1.8f, 1.9f, 2.0f, 3.0f, 4.0f, 5.0f
    };
    private static int _optionPlaybackSpeedIndex = 1;

    public static bool AutoEnabled => _optionAuto != 0;
    public static int BpmFixValue => (_optionBpmFixIndex == 0) ? 0 : 90 + _optionBpmFixIndex * 10;
    public static float PlaybackSpeed => _playbackSpeedValues[_optionPlaybackSpeedIndex];

    internal static MusicTrack _musicPreview;
    private static bool _previewLoaded;

    public static void Init()
    {
        ResetState();

        NamePlate.Init();

        string[] diffFileNames = { "0Easy.png", "1Normal.png", "2Hard.png", "3Oni.png", "4Edit.png" };
        for (int i = 0; i < 5; i++)
        {
            string path = $"Lumen/0.Songs/diff/{diffFileNames[i]}";
            if (File.Exists(path))
            {
                _texDiffTower[i] = Raylib.LoadTexture(path);
                _diffTowerLoaded[i] = _texDiffTower[i].Id != 0;
            }
            else
            {
                _diffTowerLoaded[i] = false;
            }
        }

        for (int i = 0; i < 5; i++)
        {
            string backDiffPath = $"Lumen/0.Songs/diff/backDiff/{i}.png";
            if (File.Exists(backDiffPath))
            {
                _texBackDiff[i] = Raylib.LoadTexture(backDiffPath);
                _backDiffLoaded[i] = _texBackDiff[i].Id != 0;
            }
            else
            {
                _backDiffLoaded[i] = false;
            }
        }

        string backPath = "Lumen/0.Songs/diff/Back.png";
        if (File.Exists(backPath))
        {
            _texBack = Raylib.LoadTexture(backPath);
            _backLoaded = _texBack.Id != 0;
        }

        string settingPath = "Lumen/0.Songs/diff/Setting.png";
        if (File.Exists(settingPath))
        {
            _texSetting = Raylib.LoadTexture(settingPath);
            _settingLoaded = _texSetting.Id != 0;
        }

        string headerPath = "Lumen/0.Songs/header.png";
        if (File.Exists(headerPath))
        {
            _texHeader = Raylib.LoadTexture(headerPath);
            _headerLoaded = _texHeader.Id != 0;
        }

        string cursorPath = "Lumen/0.Songs/diff/Cursor_Effect.png";
        if (File.Exists(cursorPath))
        {
            _texCursorEffect = Raylib.LoadTexture(cursorPath);
            _cursorEffectLoaded = _texCursorEffect.Id != 0;
            if (_cursorEffectLoaded)
            {
                _cursorEffectFrameW = _texCursorEffect.Width / 2;
                _cursorEffectFrameH = _texCursorEffect.Height;
            }
        }

        string cursorSmallPath = "Lumen/0.Songs/diff/Cursor_Effect_Small.png";
        if (File.Exists(cursorSmallPath))
        {
            _texCursorEffectSmall = Raylib.LoadTexture(cursorSmallPath);
            _cursorEffectSmallLoaded = _texCursorEffectSmall.Id != 0;
        }

        string onePDiffPath = "Lumen/0.Songs/diff/1PDiff.png";
        if (File.Exists(onePDiffPath))
        {
            _tex1PDiff = Raylib.LoadTexture(onePDiffPath);
            _1pDiffLoaded = _tex1PDiff.Id != 0;
        }

        string levelNumberPath = "Lumen/0.Songs/diff/Difficulty_Level_Number.png";
        if (File.Exists(levelNumberPath))
        {
            _texLevelNumber = Raylib.LoadTexture(levelNumberPath);
            _levelNumberLoaded = _texLevelNumber.Id != 0;
            if (_levelNumberLoaded)
            {
                _levelNumberFrameW = _texLevelNumber.Width / 10;
                _levelNumberFrameH = _texLevelNumber.Height;
            }
        }

        string starPath = "Lumen/0.Songs/diff/star.png";
        if (File.Exists(starPath))
        {
            _texStar = Raylib.LoadTexture(starPath);
            _starLoaded = _texStar.Id != 0;
        }

        string optionBgPath = "Lumen/Ensooption/Background.png";
        if (File.Exists(optionBgPath))
        {
            _texOptionBg = Raylib.LoadTexture(optionBgPath);
            _optionBgLoaded = _texOptionBg.Id != 0;
        }

        string optionOverrayPath = "Lumen/Ensooption/Option_Overray1.png";
        if (File.Exists(optionOverrayPath))
        {
            _texOptionOverray = Raylib.LoadTexture(optionOverrayPath);
            _optionOverrayLoaded = _texOptionOverray.Id != 0;
        }

        string optionOverray2Path = "Lumen/Ensooption/Option_Overray2.png";
        if (File.Exists(optionOverray2Path))
        {
            _texOptionOverray2 = Raylib.LoadTexture(optionOverray2Path);
            _optionOverray2Loaded = _texOptionOverray2.Id != 0;
        }

        string optionArrowPath = "Lumen/Ensooption/Option_Arrow.png";
        if (File.Exists(optionArrowPath))
        {
            _texOptionArrow = Raylib.LoadTexture(optionArrowPath);
            _optionArrowLoaded = _texOptionArrow.Id != 0;
        }

        string controlGuidePath = "Lumen/0.Songs/diff/ControlGuide/Anime.aup2";
        if (File.Exists(controlGuidePath))
        {
            _controlGuideAnim = Aup2Anim.Load(controlGuidePath);
        }
    }

    public static void Prepare(SongSelectScene.SongData song, MusicTrack existingPreviewTrack = null)
    {
        ResetState();
        CurrentSong = song;
        _rightPressCount = 0;
        _uraOniSelected = false;
        _diffCursor = 3;
        _sideSelect = SideSelect.None;
        _confirmBlinkTimer = 0f;
        _confirmBlinkShowRight = true;
        _1pDiffPrevSelIndex = -1;
        _1pDiffAnimTimer = 999f;
        _controlGuideTimer = 0f;
        _optionState = OptionPanelState.Hidden;
        _optionSlideTimer = 0f;
        _optionCursor = 0;
        _optionScrollSpeed = 1.0f;
        _optionDoron = 0;
        _optionAbekobe = 0;
        _optionRandom = 0;
        _specialOptionState = OptionPanelState.Hidden;
        _specialOptionSlideTimer = 0f;
        _pendingSpecialOption = false;
        _specialCursor = 0;
        _optionAuto = 0;
        _optionBpmFixIndex = 0;
        _optionPlaybackSpeedIndex = 1;

        string genre = song?.Genre;
        if (string.IsNullOrEmpty(genre))
            genre = FindGenreFromBoxDef(GetTjaPath(song));

        LoadGenreDiffTexture(genre);
        LoadGenreBackTexture(genre);

        StartPreviewForSong(song, existingPreviewTrack);
    }

    private static void StartPreviewForSong(SongSelectScene.SongData song, MusicTrack existingPreviewTrack = null)
    {
        StopPreview();

        if (song == null || string.IsNullOrEmpty(song.WavePath))
        {
            existingPreviewTrack?.Dispose();
            return;
        }

        if (existingPreviewTrack != null && existingPreviewTrack.Loaded)
        {
            _musicPreview = existingPreviewTrack;
            _previewLoaded = true;
            _musicPreview.Looping = true;
            _musicPreview.Volume = SettingsPanel.EffectiveBgmVolume;
            if (!_musicPreview.IsPlaying) _musicPreview.Play(Math.Max(0.0, song.DemoStart));
            ApplyPreviewSpeed();
            return;
        }

        _musicPreview = MusicTrack.Load(song.WavePath);
        _previewLoaded = _musicPreview.Loaded;
        if (_previewLoaded)
        {
            _musicPreview.Looping = true;
            _musicPreview.Volume = SettingsPanel.EffectiveBgmVolume;
            _musicPreview.Play(Math.Max(0.0, song.DemoStart));
            ApplyPreviewSpeed();
        }
        else
        {
            _musicPreview = null;
        }
    }

    private static void StopPreview()
    {
        _musicPreview?.Dispose();
        _musicPreview = null;
        _previewLoaded = false;
    }

    private static void ApplyPreviewSpeed()
    {
        if (_musicPreview == null || !_previewLoaded) return;
        float speed = PlaybackSpeed;
        if (speed <= 0f) speed = 1.0f;

        try
        {
            var type = _musicPreview.GetType();
            var method = type.GetMethod("SetPitch") ?? type.GetMethod("SetSpeed") ?? type.GetMethod("SetPlaybackSpeed");
            if (method != null) { method.Invoke(_musicPreview, new object[] { speed }); return; }

            var prop = type.GetProperty("Pitch") ?? type.GetProperty("Speed") ?? type.GetProperty("PlaybackSpeed");
            if (prop != null && prop.CanWrite) { prop.SetValue(_musicPreview, Convert.ChangeType(speed, prop.PropertyType)); return; }

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var musicField = type.GetField("Music", flags) ?? type.GetField("_music", flags) ?? type.GetField("RaylibMusic", flags);
            if (musicField != null)
            {
                var rm = musicField.GetValue(_musicPreview);
                if (rm is Raylib_cs.Music m) Raylib.SetMusicPitch(m, speed);
            }
        }
        catch { }
    }

    private static string GetTjaPath(object song)
    {
        if (song == null) return null;
        var field = song.GetType().GetField("TjaPath");
        if (field != null) return field.GetValue(song) as string;
        var prop = song.GetType().GetProperty("TjaPath");
        if (prop != null) return prop.GetValue(song) as string;
        return null;
    }

    private static string FindGenreFromBoxDef(string tjaPath)
    {
        if (string.IsNullOrEmpty(tjaPath)) return null;

        try
        {
            string dir = Path.GetDirectoryName(tjaPath);
            while (!string.IsNullOrEmpty(dir))
            {
                string boxDefPath = Path.Combine(dir, "box.def");
                if (File.Exists(boxDefPath))
                {
                    foreach (var line in File.ReadAllLines(boxDefPath))
                    {
                        string trimmed = line.Trim().TrimStart('#');
                        int colonIdx = trimmed.IndexOf(':');
                        if (colonIdx == -1) continue;

                        string key = trimmed.Substring(0, colonIdx).Trim().ToUpper();
                        string val = trimmed.Substring(colonIdx + 1).Trim();
                        if (key == "GENRE")
                            return val;
                    }
                    return null;
                }
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch { }

        return null;
    }

    private static void LoadGenreDiffTexture(string genreName)
    {
        _currentDiffLoaded = false;
        _currentGenreColor = Color.White;

        if (string.IsNullOrEmpty(genreName) || !_genreMap.TryGetValue(genreName, out var info))
        {
            _genreMap.TryGetValue("特集", out info);
        }

        _currentGenreColor = info.color ?? Color.White;

        if (_diffGenreTexCache.TryGetValue(info.folder, out var cachedDiffTex))
        {
            _texCurrentDiff = cachedDiffTex;
            _currentDiffLoaded = cachedDiffTex.Id != 0;
            return;
        }

        string diffPath = $"Lumen/0.Songs/category/{info.folder}/diff.png";
        if (File.Exists(diffPath))
        {
            var tex = Raylib.LoadTexture(diffPath);
            _diffGenreTexCache[info.folder] = tex;
            _texCurrentDiff = tex;
            _currentDiffLoaded = tex.Id != 0;
        }
    }

    private static void LoadGenreBackTexture(string genreName)
    {
        _currentBgLoaded = false;

        if (string.IsNullOrEmpty(genreName) || !_genreBackFileMap.TryGetValue(genreName, out var fileName))
        {
            _genreBackFileMap.TryGetValue("特集", out fileName);
        }

        if (_backTexCache.TryGetValue(fileName, out var cachedTex))
        {
            _texCurrentBg = cachedTex;
            _currentBgLoaded = cachedTex.Id != 0;
            return;
        }

        string path = $"Lumen/0.Songs/backs/{fileName}.png";
        if (File.Exists(path))
        {
            var tex = Raylib.LoadTexture(path);
            _backTexCache[fileName] = tex;
            _texCurrentBg = tex;
            _currentBgLoaded = tex.Id != 0;
        }
    }

    public static void ResetState()
    {
        IsDifficultyConfirmed = false;
        IsRenderRequested = false;
        BackToSongSelect = false;
    }

    public static void Unload()
    {
        StopPreview();

        for (int i = 0; i < 5; i++)
            if (_diffTowerLoaded[i]) { Raylib.UnloadTexture(_texDiffTower[i]); _diffTowerLoaded[i] = false; }

        for (int i = 0; i < 5; i++)
            if (_backDiffLoaded[i]) { Raylib.UnloadTexture(_texBackDiff[i]); _backDiffLoaded[i] = false; }

        if (_cursorEffectLoaded) { Raylib.UnloadTexture(_texCursorEffect); _cursorEffectLoaded = false; }
        if (_cursorEffectSmallLoaded) { Raylib.UnloadTexture(_texCursorEffectSmall); _cursorEffectSmallLoaded = false; }
        if (_1pDiffLoaded) { Raylib.UnloadTexture(_tex1PDiff); _1pDiffLoaded = false; }
        if (_levelNumberLoaded) { Raylib.UnloadTexture(_texLevelNumber); _levelNumberLoaded = false; }
        if (_headerLoaded) { Raylib.UnloadTexture(_texHeader); _headerLoaded = false; }
        if (_backLoaded) { Raylib.UnloadTexture(_texBack); _backLoaded = false; }
        if (_settingLoaded) { Raylib.UnloadTexture(_texSetting); _settingLoaded = false; }

        if (_starLoaded) { Raylib.UnloadTexture(_texStar); _starLoaded = false; }
        if (_optionBgLoaded) { Raylib.UnloadTexture(_texOptionBg); _optionBgLoaded = false; }
        if (_optionOverrayLoaded) { Raylib.UnloadTexture(_texOptionOverray); _optionOverrayLoaded = false; }

        _controlGuideAnim?.Dispose();
        _controlGuideAnim = null;

        foreach (var tex in _diffGenreTexCache.Values)
            if (tex.Id != 0) Raylib.UnloadTexture(tex);
        _diffGenreTexCache.Clear();
        _currentDiffLoaded = false;

        foreach (var tex in _backTexCache.Values)
            if (tex.Id != 0) Raylib.UnloadTexture(tex);

        NamePlate.Unload();
        _backTexCache.Clear();
        _currentBgLoaded = false;
    }

    public static void Update(float dt)
    {
        DonChanScene.Update("don_result_clear_loop");

        _bg2ScrollX += Bg2ScrollSpeed * dt;

        if (_1pDiffAnimTimer < OnePDiffDropDuration)
            _1pDiffAnimTimer += dt;

        if (_controlGuideAnim != null && _controlGuideAnim.Duration > 0)
        {
            _controlGuideTimer += dt;
            if (_controlGuideTimer >= _controlGuideAnim.Duration)
                _controlGuideTimer %= (float)_controlGuideAnim.Duration;
        }

        if (_optionState == OptionPanelState.SlidingIn || _optionState == OptionPanelState.SlidingOut)
        {
            _optionSlideTimer += dt;
            if (_optionSlideTimer >= OptionSlideDuration)
            {
                _optionSlideTimer = OptionSlideDuration;
                _optionState = (_optionState == OptionPanelState.SlidingIn)
                    ? OptionPanelState.Shown
                    : OptionPanelState.Hidden;

                if (_optionState == OptionPanelState.Hidden && _pendingSpecialOption)
                {
                    _pendingSpecialOption = false;
                    _specialOptionState = OptionPanelState.SlidingIn;
                    _specialOptionSlideTimer = 0f;
                }
            }
        }

        if (_specialOptionState == OptionPanelState.SlidingIn || _specialOptionState == OptionPanelState.SlidingOut)
        {
            _specialOptionSlideTimer += dt;
            if (_specialOptionSlideTimer >= OptionSlideDuration)
            {
                _specialOptionSlideTimer = OptionSlideDuration;
                _specialOptionState = (_specialOptionState == OptionPanelState.SlidingIn)
                    ? OptionPanelState.Shown
                    : OptionPanelState.Hidden;
            }
        }

        if (IsDifficultyConfirmed)
        {
            _confirmBlinkTimer += dt;
            if (_confirmBlinkTimer >= ConfirmBlinkInterval)
            {
                _confirmBlinkTimer -= ConfirmBlinkInterval;
                _confirmBlinkShowRight = !_confirmBlinkShowRight;
            }
            return;
        }

        if (_optionState == OptionPanelState.Shown || _optionState == OptionPanelState.SlidingIn || _optionState == OptionPanelState.SlidingOut)
        {
            if (_optionState == OptionPanelState.Shown)
            {
                if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Escape))
                {
                    _optionState = OptionPanelState.SlidingOut;
                    _optionSlideTimer = 0f;
                    _pendingSpecialOption = true;
                }

                bool leftPressed = Raylib.IsKeyPressed(KeyboardKey.D) || Raylib.IsKeyPressed(KeyboardKey.Left) || SettingsPanel.IsVkPressed(SettingsPanel.KeyKa1);
                bool rightPressed = Raylib.IsKeyPressed(KeyboardKey.K) || Raylib.IsKeyPressed(KeyboardKey.Right) || SettingsPanel.IsVkPressed(SettingsPanel.KeyKa2);

                if (leftPressed || rightPressed)
                {
                    bool changed = false;
                    switch (_optionCursor)
                    {
                        case 0:
                            if (leftPressed)
                            {
                                if (_optionScrollSpeed > 2.05f) _optionScrollSpeed -= 0.5f;
                                else if (_optionScrollSpeed > 1.05f) _optionScrollSpeed -= 0.1f;
                                if (_optionScrollSpeed < 1.0f) _optionScrollSpeed = 1.0f;
                            }
                            else
                            {
                                if (_optionScrollSpeed >= 2.0f) _optionScrollSpeed += 0.5f;
                                else _optionScrollSpeed += 0.1f;
                            }
                            changed = true;
                            break;
                        case 1:
                            _optionDoron = 1 - _optionDoron;
                            changed = true;
                            break;
                        case 2:
                            _optionAbekobe = 1 - _optionAbekobe;
                            changed = true;
                            break;
                        case 3:
                            if (leftPressed) _optionRandom = (_optionRandom - 1 + 3) % 3;
                            else _optionRandom = (_optionRandom + 1) % 3;
                            changed = true;
                            break;
                    }
                    if (changed) SongSelectScene.PlayKaSound();
                }

                bool donPressed =
                    Raylib.IsKeyPressed(KeyboardKey.F) || Raylib.IsKeyPressed(KeyboardKey.J) ||
                    SettingsPanel.IsVkPressed(SettingsPanel.KeyDon1) || SettingsPanel.IsVkPressed(SettingsPanel.KeyDon2);
                if (donPressed)
                {
                    SongSelectScene.PlayDonSound();
                    if (_optionCursor == 7)
                    {
                        _optionState = OptionPanelState.SlidingOut;
                        _optionSlideTimer = 0f;
                        _optionCursor = 0;
                        _pendingSpecialOption = true;
                    }
                    else
                    {
                        _optionCursor = (_optionCursor + 1) % 8;
                    }
                }
            }
            return;
        }

        if (_specialOptionState == OptionPanelState.Shown || _specialOptionState == OptionPanelState.SlidingIn || _specialOptionState == OptionPanelState.SlidingOut)
        {
            if (_specialOptionState == OptionPanelState.Shown)
            {
                if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Escape))
                {
                    _specialOptionState = OptionPanelState.SlidingOut;
                    _specialOptionSlideTimer = 0f;
                }

                bool leftPressed = Raylib.IsKeyPressed(KeyboardKey.D) || Raylib.IsKeyPressed(KeyboardKey.Left) || SettingsPanel.IsVkPressed(SettingsPanel.KeyKa1);
                bool rightPressed = Raylib.IsKeyPressed(KeyboardKey.K) || Raylib.IsKeyPressed(KeyboardKey.Right) || SettingsPanel.IsVkPressed(SettingsPanel.KeyKa2);

                if (leftPressed || rightPressed)
                {
                    bool changed = false;
                    switch (_specialCursor)
                    {
                        case 0:
                            _optionAuto = 1 - _optionAuto;
                            changed = true;
                            break;
                        case 1:
                            if (leftPressed)
                            {
                                if (_optionBpmFixIndex > 0) _optionBpmFixIndex--;
                            }
                            else
                            {
                                if (_optionBpmFixIndex < 25) _optionBpmFixIndex++;
                            }
                            changed = true;
                            break;
                        case 2:
                            if (leftPressed)
                            {
                                if (_optionPlaybackSpeedIndex > 0) _optionPlaybackSpeedIndex--;
                            }
                            else
                            {
                                if (_optionPlaybackSpeedIndex < _playbackSpeedValues.Length - 1) _optionPlaybackSpeedIndex++;
                            }
                            changed = true;
                            ApplyPreviewSpeed();
                            break;
                    }
                    if (changed) SongSelectScene.PlayKaSound();
                }

                bool donPressed =
                    Raylib.IsKeyPressed(KeyboardKey.F) || Raylib.IsKeyPressed(KeyboardKey.J) ||
                    SettingsPanel.IsVkPressed(SettingsPanel.KeyDon1) || SettingsPanel.IsVkPressed(SettingsPanel.KeyDon2);
                if (donPressed)
                {
                    SongSelectScene.PlayDonSound();
                    if (_specialCursor == 2)
                    {
                        _specialOptionState = OptionPanelState.SlidingOut;
                        _specialOptionSlideTimer = 0f;
                        _specialCursor = 0;
                    }
                    else
                    {
                        _specialCursor = (_specialCursor + 1) % 3;
                    }
                }
            }
            return;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.D) || Raylib.IsKeyPressed(KeyboardKey.Left) || SettingsPanel.IsVkPressed(SettingsPanel.KeyKa1))
        {
            if (_sideSelect == SideSelect.Back)
            {
            }
            else if (_sideSelect == SideSelect.Setting)
            {
                _sideSelect = SideSelect.Back;
                _rightPressCount = 0;
            }
            else if (_diffCursor == 0)
            {
                _sideSelect = SideSelect.Setting;
                _rightPressCount = 0;
            }
            else
            {
                _diffCursor = (_diffCursor - 1 + 4) % 4;
                _rightPressCount = 0;
            }
            SongSelectScene.PlayKaSound();
        }
        if (Raylib.IsKeyPressed(KeyboardKey.K) || Raylib.IsKeyPressed(KeyboardKey.Right) || SettingsPanel.IsVkPressed(SettingsPanel.KeyKa2))
        {
            if (_sideSelect == SideSelect.Back)
            {
                _sideSelect = SideSelect.Setting;
            }
            else if (_sideSelect == SideSelect.Setting)
            {
                _sideSelect = SideSelect.None;
                _diffCursor = 0;
            }
            else if (_diffCursor == 3)
            {
                _rightPressCount++;
                if (_rightPressCount >= 10)
                {
                    _uraOniSelected = !_uraOniSelected;
                    _rightPressCount = 0;
                }
            }
            else
            {
                _diffCursor = (_diffCursor + 1) % 4;
            }
            SongSelectScene.PlayKaSound();
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            BackToSongSelect = true;
            StopPreview();
        }

        if (Raylib.IsKeyPressed(KeyboardKey.F) || Raylib.IsKeyPressed(KeyboardKey.J) || Raylib.IsKeyPressed(KeyboardKey.Enter)
            || SettingsPanel.IsVkPressed(SettingsPanel.KeyDon1) || SettingsPanel.IsVkPressed(SettingsPanel.KeyDon2))
        {
            if (_sideSelect == SideSelect.Back)
            {
                BackToSongSelect = true;
                StopPreview();
            }
            else if (_sideSelect == SideSelect.Setting)
            {
                _optionState = OptionPanelState.SlidingIn;
                _optionSlideTimer = 0f;
            }
            else if (CurrentSong != null)
            {
                ChosenCourseIndex = (_diffCursor == 3 && _uraOniSelected) ? 4 : _diffCursor;
                IsDifficultyConfirmed = true;
                SongSelectScene.PlayDonSound();
                StopPreview();
            }
        }

        if (Raylib.IsKeyPressed(KeyboardKey.F5))
        {
            if (CurrentSong != null)
            {
                ChosenCourseIndex = CurrentSelectionIndex;
                IsDifficultyConfirmed = true;
                IsRenderRequested = true;
                StopPreview();
            }
        }

        int currentSelIndex = (_sideSelect != SideSelect.None) ? -10 - (int)_sideSelect
            : (_diffCursor == 3 && _uraOniSelected) ? 4 : _diffCursor;
        if (currentSelIndex != _1pDiffPrevSelIndex)
        {
            _1pDiffPrevSelIndex = currentSelIndex;
            _1pDiffAnimTimer = 0f;
        }
    }

    private static void DrawTiledVerticalFitBackground(Texture2D tex, float vx, float vy, float vw, float vh, float scrollX, byte alpha)
    {
        if (tex.Height <= 0) return;
        float fitScale = vh / tex.Height;
        float scaledW = tex.Width * fitScale;
        if (scaledW <= 0f) return;

        float offset = scrollX % scaledW;
        if (offset < 0) offset += scaledW;

        Color tint = new Color((byte)255, (byte)255, (byte)255, alpha);
        Rectangle src = new Rectangle(0, 0, tex.Width, tex.Height);

        for (float x = vx - offset; x < vx + vw; x += scaledW)
        {
            Raylib.DrawTexturePro(tex, src, new Rectangle(x, vy, scaledW, vh), Vector2.Zero, 0f, tint);
        }
    }

    private static void DrawLevelNumber(int levelValue, float centerX, float centerY, float baseScale, float spacingPx, float systemScale)
    {
        if (!_levelNumberLoaded || _texLevelNumber.Id == 0 || levelValue <= 0) return;

        string levelStr = levelValue.ToString();

        float digitH = _levelNumberFrameH * baseScale * systemScale;
        float digitW = _levelNumberFrameW * baseScale * systemScale;
        float spacing = spacingPx * systemScale;
        float totalW = levelStr.Length * digitW + (levelStr.Length - 1) * spacing;

        float startX = centerX - totalW / 2f;
        float startY = centerY - digitH / 2f;

        for (int i = 0; i < levelStr.Length; i++)
        {
            char c = levelStr[i];
            if (c < '0' || c > '9') continue;
            int digit = c - '0';

            Rectangle digitSrc = new Rectangle(digit * _levelNumberFrameW, 0, _levelNumberFrameW, _levelNumberFrameH);
            Rectangle digitDest = new Rectangle(startX + i * (digitW + spacing), startY, digitW, digitH);
            Raylib.DrawTexturePro(_texLevelNumber, digitSrc, digitDest, Vector2.Zero, 0f, Color.White);
        }
    }

    private static void DrawStars(int levelValue, float centerX, float centerY, float baseScale, float spacingPx, float systemScale)
    {
        if (!_starLoaded || _texStar.Id == 0 || levelValue <= 0) return;

        float starH = _texStar.Height * baseScale * systemScale;
        float starW = _texStar.Width * baseScale * systemScale;
        float spacing = spacingPx * systemScale;

        float totalGridW = TowerStarGridSize * starW + (TowerStarGridSize - 1) * spacing;
        float startX = centerX - totalGridW / 2f;
        float startY = centerY - starH / 2f;

        Rectangle src = new Rectangle(0, 0, _texStar.Width, _texStar.Height);

        for (int i = 0; i < levelValue; i++)
        {
            float drawX = startX + i * (starW + spacing);
            Rectangle dest = new Rectangle(drawX, startY, starW, starH);
            Raylib.DrawTexturePro(_texStar, src, dest, Vector2.Zero, 0f, Color.White);
        }
    }

    public static void Draw()
    {
        var (vx, vy, vw, vh) = Songs.GetViewport();
        float scale = vw / 1280f;

        if (_currentBgLoaded && _texCurrentBg.Id != 0)
        {
            DrawTiledVerticalFitBackground(_texCurrentBg, vx + Bg2OffsetX * scale, vy + Bg2OffsetY * scale, vw, vh * Bg2Scale, _bg2ScrollX, 255);
        }

        {
            int backDiffIdx = (_diffCursor == 3 && _uraOniSelected) ? 4 : _diffCursor;
            if (backDiffIdx >= 0 && backDiffIdx < 5 && _backDiffLoaded[backDiffIdx] && _texBackDiff[backDiffIdx].Id != 0)
            {
                var tex = _texBackDiff[backDiffIdx];
                float bdW = vw * BackDiffScale;
                float bdH = (tex.Width > 0) ? bdW * ((float)tex.Height / tex.Width) : vh * BackDiffScale;
                float bdX = vx + (vw - bdW) / 2f + BackDiffOffsetX * scale;
                float bdY = vy + (vh - bdH) / 2f + BackDiffOffsetY * scale;

                Rectangle bdSrc = new Rectangle(0, 0, tex.Width, tex.Height);
                Rectangle bdDest = new Rectangle(bdX, bdY, bdW, bdH);
                byte bdAlpha = (byte)Math.Clamp(BackDiffAlpha * 255f, 0f, 255f);
                Raylib.DrawTexturePro(tex, bdSrc, bdDest, Vector2.Zero, 0f, new Color((byte)255, (byte)255, (byte)255, bdAlpha));
            }
        }

        if (_currentDiffLoaded && _texCurrentDiff.Id != 0)
        {
            float bgW = vw * BgScale;
            float bgH = ((_texCurrentDiff.Width > 0) ? bgW * ((float)_texCurrentDiff.Height / _texCurrentDiff.Width) : vh * BgScale) * BgHeightStretch;
            float bgX = vx + (vw - bgW) / 2f + BgOffsetX * scale;
            float bgY = vy + (vh - bgH) / 2f + BgOffsetY * scale;

            Rectangle diffSrc = new Rectangle(0, 0, _texCurrentDiff.Width, _texCurrentDiff.Height);
            Rectangle diffDest = new Rectangle(bgX, bgY, bgW, bgH);
            Raylib.DrawTexturePro(_texCurrentDiff, diffSrc, diffDest, Vector2.Zero, 0f, Color.White);
        }

        if (_headerLoaded && _texHeader.Id != 0)
        {
            float headerW = vw * HeaderScale;
            float headerH = (_texHeader.Width > 0) ? headerW * ((float)_texHeader.Height / _texHeader.Width) : 0f;
            float headerX = vx + (vw - headerW) / 2f + HeaderOffsetX * scale;
            float headerY = vy + HeaderOffsetY * scale;

            Rectangle headerSrc = new Rectangle(0, 0, _texHeader.Width, _texHeader.Height);
            Rectangle headerDest = new Rectangle(headerX, headerY, headerW, headerH);
            Raylib.DrawTexturePro(_texHeader, headerSrc, headerDest, Vector2.Zero, 0f, Color.White);
        }

        if (CurrentSong != null)
        {
            string titleText = !string.IsNullOrEmpty(CurrentSong.TitleJP) ? CurrentSong.TitleJP : (CurrentSong.Title ?? "");
            string subText = !string.IsNullOrEmpty(CurrentSong.SubtitleJP) ? CurrentSong.SubtitleJP : (CurrentSong.Subtitle ?? "");

            float maxW = vw * 0.9f;
            float titleFS = 42f * scale;
            float titleLetterSpacingEm = G.GetRecommendedTitleLetterSpacingEm(titleText);
            if (titleText.Length > 0)
            {
                float titleWidth = G.MeasureTextWithOutline16Width(G.Font, titleText, titleFS, titleLetterSpacingEm);
                if (titleWidth > maxW) titleFS *= maxW / titleWidth;
            }
            float subFS = titleFS * 0.6f;

            float centerX = vx + vw * TitleCenterX;
            float centerY = vy + vh * TitleCenterY;

            float titleMeasuredW = G.MeasureTextWithOutline16Width(G.Font, titleText, titleFS, titleLetterSpacingEm);
            float titleMeasuredH = Raylib.MeasureTextEx(G.Font, titleText, titleFS, 0f).Y;
            Vector2 titlePos = new Vector2(centerX - titleMeasuredW / 2f, centerY - titleMeasuredH / 2f);
            G.DrawTextWithOutline16(G.Font, titleText, titlePos, titleFS, Color.White, Color.Black, 7f, 0f, titleLetterSpacingEm);

            if (!string.IsNullOrEmpty(subText))
            {
                float subLetterSpacingEm = G.GetRecommendedTitleLetterSpacingEm(subText);
                float subCenterY = centerY + vh * SubtitleOffsetY;
                float subMeasuredW = G.MeasureTextWithOutline16Width(G.Font, subText, subFS, subLetterSpacingEm);
                float subMeasuredH = Raylib.MeasureTextEx(G.Font, subText, subFS, 0f).Y;
                Vector2 subPos = new Vector2(centerX - subMeasuredW / 2f, subCenterY - subMeasuredH / 2f);
                G.DrawTextWithOutline16(G.Font, subText, subPos, subFS, Color.White, Color.Black, 7f, 0f, subLetterSpacingEm);
            }

        }

        if (DrawSelectedLevelMain && _levelNumberLoaded && _texLevelNumber.Id != 0 && CurrentSong != null)
        {
            int levelDiffIdx = (_diffCursor == 3 && _uraOniSelected) ? 4 : _diffCursor;
            if (levelDiffIdx >= 0 && levelDiffIdx < 5)
            {
                int levelValue = CurrentSong.Levels[levelDiffIdx];
                if (levelValue > 0)
                {
                    float levelCenterX = vx + vw * LevelNumberCenterX;
                    float levelCenterY = vy + vh * LevelNumberCenterY;
                    DrawLevelNumber(levelValue, levelCenterX, levelCenterY, LevelNumberScale, LevelNumberSpacing, scale);
                }
            }
        }

        int slot3Idx = (_uraOniSelected) ? 4 : 3;
        int[] drawIndices = new[] { 0, 1, 2, slot3Idx };

        const int visibleCount = 4;
        float towerGap = _towerGapPx * scale;
        float baseH = vh * _towerHeightRatio * TowerScale;
        float towerAreaCenterX = vx + vw * TowerCenterX;
        float towerAreaCenterY = vy + vh * TowerCenterY;

        float baseW = baseH;
        for (int i = 0; i < 5; i++)
        {
            if (_diffTowerLoaded[i] && _texDiffTower[i].Id != 0)
            {
                baseW = baseH * ((float)_texDiffTower[i].Width / _texDiffTower[i].Height);
                break;
            }
        }

        float totalW = visibleCount * baseW + (visibleCount - 1) * towerGap;
        float startX = towerAreaCenterX - totalW / 2f;

        float bsGap = BackSettingGapPx * scale;
        float bsY = towerAreaCenterY - baseH / 2f + BackSettingOffsetY * scale;

        float settingH = baseH * BackSettingScale;
        float settingW = (_settingLoaded && _texSetting.Height > 0)
            ? settingH * ((float)_texSetting.Width / _texSetting.Height)
            : settingH;

        float backH = baseH * BackSettingScale;
        float backW = (_backLoaded && _texBack.Height > 0)
            ? backH * ((float)_texBack.Width / _texBack.Height)
            : backH;

        float settingX = startX - bsGap - settingW;
        float backX = settingX - bsGap - backW;

        Rectangle settingDest = new Rectangle(settingX, bsY + (baseH - settingH) / 2f, settingW, settingH);
        Rectangle backDest = new Rectangle(backX, bsY + (baseH - backH) / 2f, backW, backH);

        var sideSlots = new (bool loaded, Texture2D tex, Rectangle dest, SideSelect kind)[]
        {
            (_backLoaded, _texBack, backDest, SideSelect.Back),
            (_settingLoaded, _texSetting, settingDest, SideSelect.Setting),
        };

        foreach (var s in sideSlots)
        {
            if (!s.loaded || s.tex.Id == 0) continue;

            if (_sideSelect == s.kind && _cursorEffectSmallLoaded && _texCursorEffectSmall.Id != 0)
            {
                float csW = s.dest.Width * CursorEffectSmallScale;
                float csH = s.dest.Height * CursorEffectSmallScale;
                Rectangle cursorSmallDest = new Rectangle(
                    s.dest.X + (s.dest.Width - csW) / 2f,
                    s.dest.Y + (s.dest.Height - csH) / 2f,
                    csW, csH);
                Rectangle cursorSmallSrc = new Rectangle(0, 0, _texCursorEffectSmall.Width, _texCursorEffectSmall.Height);
                Raylib.DrawTexturePro(_texCursorEffectSmall, cursorSmallSrc, cursorSmallDest, Vector2.Zero, 0f, Color.White);
            }

            Rectangle src = new Rectangle(0, 0, s.tex.Width, s.tex.Height);
            Raylib.DrawTexturePro(s.tex, src, s.dest, Vector2.Zero, 0f, Color.White);
        }

        Rectangle onePDiffDest = (_sideSelect == SideSelect.Back) ? backDest
            : (_sideSelect == SideSelect.Setting) ? settingDest
            : default;

        for (int slot = 0; slot < visibleCount; slot++)
        {
            int diffIdx = drawIndices[slot];
            bool hasLevel = (CurrentSong != null && CurrentSong.Levels[diffIdx] > 0);
            bool isSelected = (diffIdx == 3 || diffIdx == 4) ? (_diffCursor == 3) : (_diffCursor == diffIdx);
            bool showTowerCursorRing = isSelected && _sideSelect == SideSelect.None;

            float drawX = startX + slot * (baseW + towerGap);
            float drawY = towerAreaCenterY - baseH / 2f;
            Rectangle dest = new Rectangle(drawX, drawY, baseW, baseH);

            if (showTowerCursorRing && _cursorEffectLoaded && _texCursorEffect.Id != 0)
            {
                bool showRightFrame = IsDifficultyConfirmed && _confirmBlinkShowRight;
                int frameIdx = showRightFrame ? 1 : 0;

                float cursorH = baseH * CursorEffectScale;
                float cursorW = cursorH * ((float)_cursorEffectFrameW / _cursorEffectFrameH);
                Rectangle cursorDest = new Rectangle(
                    dest.X + (dest.Width - cursorW) / 2f,
                    dest.Y + (dest.Height - cursorH) / 2f,
                    cursorW, cursorH);

                Rectangle cursorSrc = new Rectangle(frameIdx * _cursorEffectFrameW, 0, _cursorEffectFrameW, _cursorEffectFrameH);
                Raylib.DrawTexturePro(_texCursorEffect, cursorSrc, cursorDest, Vector2.Zero, 0f, Color.White);
            }

            if (_diffTowerLoaded[diffIdx] && _texDiffTower[diffIdx].Id != 0)
            {
                Color drawColor = hasLevel ? Color.White : new Color((byte)128, (byte)128, (byte)128, (byte)255);
                Rectangle src = new Rectangle(0, 0, _texDiffTower[diffIdx].Width, _texDiffTower[diffIdx].Height);
                Raylib.DrawTexturePro(_texDiffTower[diffIdx], src, dest, Vector2.Zero, 0f, drawColor);
            }

            if (DrawLevelOnEachTower && hasLevel && CurrentSong != null)
            {
                int levelValue = CurrentSong.Levels[diffIdx];
                float lvlCenterX = dest.X + dest.Width / 2f + TowerLevelOffsetX * scale;
                float lvlCenterY = dest.Y + dest.Height / 2f + TowerLevelOffsetY * scale;
                DrawLevelNumber(levelValue, lvlCenterX, lvlCenterY, TowerLevelScale, TowerLevelSpacing, scale);
            }

            if (DrawStarsOnEachTower && hasLevel && CurrentSong != null && _starLoaded)
            {
                int levelValue = CurrentSong.Levels[diffIdx];
                float starCenterX = dest.X + dest.Width / 2f + TowerStarOffsetX * scale;
                float starCenterY = dest.Y + dest.Height / 2f + TowerStarOffsetY * scale;
                DrawStars(levelValue, starCenterX, starCenterY, TowerStarScale, TowerStarSpacing, scale);
            }

            if (showTowerCursorRing && isSelected)
                onePDiffDest = dest;
        }

        if (_1pDiffLoaded && _tex1PDiff.Id != 0 && onePDiffDest.Width > 0 && onePDiffDest.Height > 0)
        {
            float curScale = OnePDiffScale;
            float curOffsetX = OnePDiffOffsetX;
            float curOffsetY = OnePDiffOffsetY;

            float pH = baseH * curScale;
            float pW = pH * ((float)_tex1PDiff.Width / _tex1PDiff.Height);

            float dropProgress = (OnePDiffDropDuration > 0f)
                ? Math.Clamp(_1pDiffAnimTimer / OnePDiffDropDuration, 0f, 1f) : 1f;
            float easedProgress = 1f - (1f - dropProgress) * (1f - dropProgress);
            float dropOffsetY = (1f - easedProgress) * -OnePDiffDropDistance * scale;

            float onePDiffY = towerAreaCenterY - baseH / 2f;

            Rectangle pDest = new Rectangle(
                onePDiffDest.X + (onePDiffDest.Width - pW) / 2f + curOffsetX * scale,
                onePDiffY + (baseH - pH) / 2f + curOffsetY * scale + dropOffsetY,
                pW, pH);

            Rectangle pSrc = new Rectangle(0, 0, _tex1PDiff.Width, _tex1PDiff.Height);
            Raylib.DrawTexturePro(_tex1PDiff, pSrc, pDest, Vector2.Zero, 0f, Color.White);
        }

        float donChanScale = vw / 2000f;

        DonChanScene.X = DonChanX;
        DonChanScene.BottomY = DonChanBottomY;
        DonChanScene.Width = DonChanWidth;
        DonChanScene.Draw(vx, vy, donChanScale);

        NamePlate.GlobalX = NamePlateGlobalX;
        NamePlate.GlobalY = NamePlateGlobalY;
        NamePlate.Draw(vx, vy, donChanScale);

        if (_optionState != OptionPanelState.Hidden)
        {
            float optCenterX = vx + OptionBgX * scale;
            float optCenterY = vy + OptionBgY * scale;

            float t = (_optionSlideTimer < OptionSlideDuration)
                ? _optionSlideTimer / OptionSlideDuration
                : 1f;

            float slideY;
            if (_optionState == OptionPanelState.SlidingIn)
            {
                float eased = 1f - (1f - t) * (1f - t);
                float offscreenY = vy + vh + (_optionBgLoaded ? _texOptionBg.Height * OptionBgScale * scale : 0f);
                slideY = optCenterY + (offscreenY - optCenterY) * (1f - eased);
            }
            else if (_optionState == OptionPanelState.SlidingOut)
            {
                float eased = t * t;
                float offscreenY = vy + vh + (_optionBgLoaded ? _texOptionBg.Height * OptionBgScale * scale : 0f);
                slideY = optCenterY + (offscreenY - optCenterY) * eased;
            }
            else
            {
                slideY = optCenterY;
            }

            float slideOffsetY = slideY - optCenterY;

            if (_optionBgLoaded && _texOptionBg.Id != 0)
            {
                float optW = _texOptionBg.Width * OptionBgScale * scale;
                float optH = _texOptionBg.Height * OptionBgScale * scale;
                Rectangle optSrc = new Rectangle(0, 0, _texOptionBg.Width, _texOptionBg.Height);
                Rectangle optDest = new Rectangle(optCenterX - optW / 2f, slideY - optH / 2f, optW, optH);
                Raylib.DrawTexturePro(_texOptionBg, optSrc, optDest, Vector2.Zero, 0f, Color.White);

                string optionTitleText = "演奏オプション";
                float titleFontSize = OptionTitleFontSize * scale;
                Vector2 titleSize = Raylib.MeasureTextEx(G.Font, optionTitleText, titleFontSize, 0f);
                Vector2 titlePos = new Vector2(
                    optCenterX + OptionTitleOffsetX * scale - titleSize.X / 2f,
                    slideY + OptionTitleOffsetY * scale - titleSize.Y / 2f);
                G.DrawTextWithOutline16(G.Font, optionTitleText, titlePos, titleFontSize, Color.White, Color.Black);
            }

            if (_optionOverrayLoaded && _texOptionOverray.Id != 0)
            {
                string[] optionLabels = { "はやさ", "ドロン", "あべこべ", "ランダム", "演奏スキップ", "音符位置調整", "ボイス", "音色" };
                const float overrayScale = 0.67f;
                float ovW = _texOptionOverray.Width * overrayScale * scale;
                float ovH = _texOptionOverray.Height * overrayScale * scale;
                Rectangle ovSrc = new Rectangle(0, 0, _texOptionOverray.Width, _texOptionOverray.Height);
                float ovCenterX = vx + OverrayCenterX * scale;
                float labelLeftX = ovCenterX + OverrayLabelOffsetX * scale;
                float valueCenterX = ovCenterX + OverrayValueOffsetX * scale;
                float labelFontSize = OverrayLabelFontSize * scale;

                bool panelShown = _optionState == OptionPanelState.Shown;

                float ov2W = _optionOverray2Loaded && _texOptionOverray2.Id != 0
                    ? _texOptionOverray2.Width * Overray2Scale * scale : 0f;
                float ov2H = _optionOverray2Loaded && _texOptionOverray2.Id != 0
                    ? _texOptionOverray2.Height * Overray2Scale * scale : 0f;
                float ov2Left = ovCenterX + ovW / 2f + Overray2OffsetX * scale;

                bool IsModified(int idx) => idx switch
                {
                    0 => MathF.Abs(_optionScrollSpeed - 1.0f) > 0.001f,
                    1 => _optionDoron != 0,
                    2 => _optionAbekobe != 0,
                    3 => _optionRandom != 0,
                    _ => false
                };

                Color highlightColor = new Color((byte)0xFE, (byte)0xFE, (byte)0x00, (byte)255);

                for (int i = 0; i < 8; i++)
                {
                    float ovCenterY = vy + (OverrayStartY + OverraySpacingY * i) * scale + slideOffsetY;
                    Rectangle ovDest = new Rectangle(ovCenterX - ovW / 2f, ovCenterY - ovH / 2f, ovW, ovH);

                    bool isCursor = (panelShown && i == _optionCursor);
                    bool isUnimplemented = (i >= 4);

                    Color ovColor = isCursor ? highlightColor : Color.White;
                    if (isUnimplemented) ovColor = new Color((byte)(ovColor.R / 2), (byte)(ovColor.G / 2), (byte)(ovColor.B / 2), (byte)255);

                    Raylib.DrawTexturePro(_texOptionOverray, ovSrc, ovDest, Vector2.Zero, 0f, ovColor);

                    if (isCursor)
                        Raylib.DrawRectangleRec(ovDest, new Color((byte)0xFE, (byte)0xFE, (byte)0x00, (byte)50));

                    if (_optionOverray2Loaded && _texOptionOverray2.Id != 0 && _optionBgLoaded)
                    {
                        float ov2CenterY = ovCenterY + Overray2OffsetY * scale;
                        Rectangle ov2Dest = new Rectangle(ov2Left, ov2CenterY - ov2H / 2f, ov2W, ov2H);
                        Rectangle ov2Src = new Rectangle(0, 0, _texOptionOverray2.Width, _texOptionOverray2.Height);

                        bool isModified = IsModified(i);
                        Color ov2Color = isModified ? highlightColor : Color.White;
                        if (isUnimplemented) ov2Color = new Color((byte)(ov2Color.R / 2), (byte)(ov2Color.G / 2), (byte)(ov2Color.B / 2), (byte)255);

                        Raylib.DrawTexturePro(_texOptionOverray2, ov2Src, ov2Dest, Vector2.Zero, 0f, ov2Color);

                        if (isModified && !isUnimplemented)
                            Raylib.DrawRectangleRec(ov2Dest, new Color((byte)0xFE, (byte)0xFE, (byte)0x00, (byte)50));
                    }
                }

                for (int i = 0; i < 8; i++)
                {
                    float ovCenterY = vy + (OverrayStartY + OverraySpacingY * i) * scale + slideOffsetY;
                    bool isCursor = (panelShown && i == _optionCursor);
                    bool isUnimplemented = (i >= 4);

                    string valText = "";
                    switch (i)
                    {
                        case 0: valText = _optionScrollSpeed.ToString("0.0"); break;
                        case 1: valText = (_optionDoron == 1) ? "する" : "しない"; break;
                        case 2: valText = (_optionAbekobe == 1) ? "する" : "しない"; break;
                        case 3: valText = (_optionRandom == 1) ? "きまぐれ" : (_optionRandom == 2) ? "でたらめ" : "なし"; break;
                        default: valText = "未実装"; break;
                    }

                    Color nameColor = isUnimplemented ? new Color((byte)128, (byte)128, (byte)128, (byte)255) : Color.White;
                    Vector2 nameSize = Raylib.MeasureTextEx(G.Font, optionLabels[i], labelFontSize, 0f);
                    Vector2 namePos = new Vector2(labelLeftX, ovCenterY - nameSize.Y / 2f);
                    G.DrawTextWithOutline16(G.Font, optionLabels[i], namePos, labelFontSize, nameColor, Color.Black);

                    Color valueColor = isUnimplemented ? new Color((byte)128, (byte)128, (byte)128, (byte)255) : Color.White;
                    Vector2 valueSize = Raylib.MeasureTextEx(G.Font, valText, labelFontSize, 0f);
                    Vector2 valuePos = new Vector2(valueCenterX - valueSize.X / 2f, ovCenterY - valueSize.Y / 2f);
                    G.DrawTextWithOutline16(G.Font, valText, valuePos, labelFontSize, valueColor, Color.Black);

                    if (isCursor && _optionArrowLoaded && _texOptionArrow.Id != 0 && _optionOverray2Loaded && _texOptionOverray2.Id != 0 && _optionBgLoaded)
                    {
                        float arrowW = _texOptionArrow.Width * OptionArrowScale * scale;
                        float arrowH = _texOptionArrow.Height * OptionArrowScale * scale;
                        float ov2CenterX = ov2Left + ov2W / 2f;
                        float ov2CenterY = ovCenterY + Overray2OffsetY * scale;
                        float arrowCenterY = ov2CenterY + OptionArrowOffsetY * scale;
                        Rectangle arrowSrc = new Rectangle(0, 0, _texOptionArrow.Width, _texOptionArrow.Height);

                        Color arrowColor = isUnimplemented
                            ? new Color((byte)128, (byte)128, (byte)128, (byte)255)
                            : Color.White;

                        float leftArrowCenterX = ov2CenterX - (ov2W / 2f + OptionArrowGapX * scale);
                        Rectangle leftArrowDest = new Rectangle(leftArrowCenterX - arrowW / 2f, arrowCenterY - arrowH / 2f, arrowW, arrowH);
                        Raylib.DrawTexturePro(_texOptionArrow, arrowSrc, leftArrowDest, Vector2.Zero, 0f, arrowColor);

                        float rightArrowCenterX = ov2CenterX + (ov2W / 2f + OptionArrowGapX * scale);
                        Rectangle rightArrowDest = new Rectangle(rightArrowCenterX - arrowW / 2f, arrowCenterY - arrowH / 2f, arrowW, arrowH);
                        Rectangle arrowSrcFlipped = new Rectangle(_texOptionArrow.Width, 0, -_texOptionArrow.Width, _texOptionArrow.Height);
                        Raylib.DrawTexturePro(_texOptionArrow, arrowSrcFlipped, rightArrowDest, Vector2.Zero, 0f, arrowColor);
                    }
                }
            }
        }

        if (_specialOptionState != OptionPanelState.Hidden)
        {
            float spCenterX = vx + OptionBgX * scale;
            float spCenterY = vy + OptionBgY * scale;

            float spT = (_specialOptionSlideTimer < OptionSlideDuration)
                ? _specialOptionSlideTimer / OptionSlideDuration
                : 1f;

            float spSlideY;
            if (_specialOptionState == OptionPanelState.SlidingIn)
            {
                float eased = 1f - (1f - spT) * (1f - spT);
                float offscreenY = vy + vh + (_optionBgLoaded ? _texOptionBg.Height * OptionBgScale * scale : 0f);
                spSlideY = spCenterY + (offscreenY - spCenterY) * (1f - eased);
            }
            else if (_specialOptionState == OptionPanelState.SlidingOut)
            {
                float eased = spT * spT;
                float offscreenY = vy + vh + (_optionBgLoaded ? _texOptionBg.Height * OptionBgScale * scale : 0f);
                spSlideY = spCenterY + (offscreenY - spCenterY) * eased;
            }
            else
            {
                spSlideY = spCenterY;
            }

            if (_optionBgLoaded && _texOptionBg.Id != 0)
            {
                float optW = _texOptionBg.Width * OptionBgScale * scale;
                float optH = _texOptionBg.Height * OptionBgScale * scale;
                Rectangle optSrc = new Rectangle(0, 0, _texOptionBg.Width, _texOptionBg.Height);
                Rectangle optDest = new Rectangle(spCenterX - optW / 2f, spSlideY - optH / 2f, optW, optH);
                Raylib.DrawTexturePro(_texOptionBg, optSrc, optDest, Vector2.Zero, 0f, Color.White);

                string specialTitleText = "特殊オプション";
                float spTitleFontSize = OptionTitleFontSize * scale;
                Vector2 spTitleSize = Raylib.MeasureTextEx(G.Font, specialTitleText, spTitleFontSize, 0f);
                Vector2 spTitlePos = new Vector2(
                    spCenterX + OptionTitleOffsetX * scale - spTitleSize.X / 2f,
                    spSlideY + OptionTitleOffsetY * scale - spTitleSize.Y / 2f);
                G.DrawTextWithOutline16(G.Font, specialTitleText, spTitlePos, spTitleFontSize, Color.White, Color.Black);
            }

            if (_optionOverrayLoaded && _texOptionOverray.Id != 0)
            {
                string[] specialLabels = { "オート", "BPM固定", "再生速度" };
                const float overrayScale = 0.67f;
                float ovW = _texOptionOverray.Width * overrayScale * scale;
                float ovH = _texOptionOverray.Height * overrayScale * scale;
                Rectangle ovSrc = new Rectangle(0, 0, _texOptionOverray.Width, _texOptionOverray.Height);
                float ovCenterX = vx + OverrayCenterX * scale;
                float labelLeftX = ovCenterX + OverrayLabelOffsetX * scale;
                float valueCenterX = ovCenterX + OverrayValueOffsetX * scale;
                float labelFontSize = OverrayLabelFontSize * scale;

                bool spPanelShown = _specialOptionState == OptionPanelState.Shown;

                float ov2W = _optionOverray2Loaded && _texOptionOverray2.Id != 0
                    ? _texOptionOverray2.Width * Overray2Scale * scale : 0f;
                float ov2H = _optionOverray2Loaded && _texOptionOverray2.Id != 0
                    ? _texOptionOverray2.Height * Overray2Scale * scale : 0f;
                float ov2Left = ovCenterX + ovW / 2f + Overray2OffsetX * scale;

                Color highlightColor = new Color((byte)0xFE, (byte)0xFE, (byte)0x00, (byte)255);
                float spSlideOffsetY = spSlideY - spCenterY;

                for (int i = 0; i < 3; i++)
                {
                    float ovCenterY = vy + (OverrayStartY + OverraySpacingY * i) * scale + spSlideOffsetY;
                    Rectangle ovDest = new Rectangle(ovCenterX - ovW / 2f, ovCenterY - ovH / 2f, ovW, ovH);

                    bool isCursor = (spPanelShown && i == _specialCursor);
                    bool isModified = (i == 0) ? (_optionAuto != 0)
                        : (i == 1) ? (_optionBpmFixIndex != 0)
                        : (_optionPlaybackSpeedIndex != 1);

                    Color ovColor = isCursor ? highlightColor : Color.White;
                    Raylib.DrawTexturePro(_texOptionOverray, ovSrc, ovDest, Vector2.Zero, 0f, ovColor);

                    if (isCursor)
                        Raylib.DrawRectangleRec(ovDest, new Color((byte)0xFE, (byte)0xFE, (byte)0x00, (byte)50));

                    if (_optionOverray2Loaded && _texOptionOverray2.Id != 0 && _optionBgLoaded)
                    {
                        float ov2CenterY = ovCenterY + Overray2OffsetY * scale;
                        Rectangle ov2Dest = new Rectangle(ov2Left, ov2CenterY - ov2H / 2f, ov2W, ov2H);
                        Rectangle ov2Src = new Rectangle(0, 0, _texOptionOverray2.Width, _texOptionOverray2.Height);

                        Color ov2Color = isModified ? highlightColor : Color.White;
                        Raylib.DrawTexturePro(_texOptionOverray2, ov2Src, ov2Dest, Vector2.Zero, 0f, ov2Color);

                        if (isModified)
                            Raylib.DrawRectangleRec(ov2Dest, new Color((byte)0xFE, (byte)0xFE, (byte)0x00, (byte)50));
                    }

                    Vector2 nameSize = Raylib.MeasureTextEx(G.Font, specialLabels[i], labelFontSize, 0f);
                    Vector2 namePos = new Vector2(labelLeftX, ovCenterY - nameSize.Y / 2f);
                    G.DrawTextWithOutline16(G.Font, specialLabels[i], namePos, labelFontSize, Color.White, Color.Black);

                    string valText = (i == 0)
                        ? ((_optionAuto == 1) ? "する" : "しない")
                        : (i == 1)
                            ? ((_optionBpmFixIndex == 0) ? "なし" : (90 + _optionBpmFixIndex * 10).ToString())
                            : (_playbackSpeedValues[_optionPlaybackSpeedIndex].ToString("0.0") + "倍");
                    Vector2 valueSize = Raylib.MeasureTextEx(G.Font, valText, labelFontSize, 0f);
                    Vector2 valuePos = new Vector2(valueCenterX - valueSize.X / 2f, ovCenterY - valueSize.Y / 2f);
                    G.DrawTextWithOutline16(G.Font, valText, valuePos, labelFontSize, Color.White, Color.Black);

                    if (isCursor && _optionArrowLoaded && _texOptionArrow.Id != 0 && _optionOverray2Loaded && _texOptionOverray2.Id != 0 && _optionBgLoaded)
                    {
                        float arrowW = _texOptionArrow.Width * OptionArrowScale * scale;
                        float arrowH = _texOptionArrow.Height * OptionArrowScale * scale;
                        float ov2CenterX = ov2Left + ov2W / 2f;
                        float ov2CenterY = ovCenterY + Overray2OffsetY * scale;
                        float arrowCenterY = ov2CenterY + OptionArrowOffsetY * scale;
                        Rectangle arrowSrc = new Rectangle(0, 0, _texOptionArrow.Width, _texOptionArrow.Height);

                        float leftArrowCenterX = ov2CenterX - (ov2W / 2f + OptionArrowGapX * scale);
                        Rectangle leftArrowDest = new Rectangle(leftArrowCenterX - arrowW / 2f, arrowCenterY - arrowH / 2f, arrowW, arrowH);
                        Raylib.DrawTexturePro(_texOptionArrow, arrowSrc, leftArrowDest, Vector2.Zero, 0f, Color.White);

                        float rightArrowCenterX = ov2CenterX + (ov2W / 2f + OptionArrowGapX * scale);
                        Rectangle rightArrowDest = new Rectangle(rightArrowCenterX - arrowW / 2f, arrowCenterY - arrowH / 2f, arrowW, arrowH);
                        Rectangle arrowSrcFlipped = new Rectangle(_texOptionArrow.Width, 0, -_texOptionArrow.Width, _texOptionArrow.Height);
                        Raylib.DrawTexturePro(_texOptionArrow, arrowSrcFlipped, rightArrowDest, Vector2.Zero, 0f, Color.White);
                    }
                }
            }
        }

        if (_controlGuideAnim != null)
        {
            float cgW = ControlGuideWidth * scale;
            float cgH = ControlGuideHeight * scale;
            float cgX = vx + vw / 2f + ControlGuideX * scale - cgW / 2f;
            float cgY = vy + vh / 2f + ControlGuideY * scale - cgH / 2f;
            _controlGuideAnim.Draw(cgX, cgY, cgW, cgH, _controlGuideTimer, Color.White);
        }
    }
}
using Raylib_cs;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using TaikoNauts.Core.Taiko.Charts;

/// <summary>
/// 演奏画面（Enso）用の音符テクスチャ描画およびアニメーションを管理するクラス。
/// notes/ 以下のフォルダごとに分割されたフレーム画像 (0.png, 1.png, ...) を使用する。
/// フォルダ名の末尾に _ が付くものは大音符。終端パーツは各フォルダの _.png。
/// </summary>
public static class NotesTexture
{
    // noteType → フォルダ名 (1=小ドン, 2=小カ, 3=大ドン, 4=大カ, 5=小連打始, 6=大連打始, 7=風船, 8=くすだま)
    private static readonly string[] FolderNames =
        { "", "don", "katsu", "don_", "katsu_", "roll", "roll_", "balloon", "balloon_" };

    private static Texture2D[][] _frames = new Texture2D[9][];
    private static Texture2D _drumrollTail;
    private static Texture2D _drumrollBigTail;
    private static Texture2D _balloonTail;

    // 風船UI (吹き出し・残り打数数字・膨らみアニメ)
    private static Texture2D _balloonBubble;
    private static Texture2D[] _balloonDigits = new Texture2D[10];
    private static Texture2D[] _balloonBig = new Texture2D[8];

    // 口唱歌 (m画像)
    private static Texture2D _seDon, _seDo, _seKo, _seKatsu, _seKa;
    private static Texture2D _seDonBig, _seKatsuBig;
    private static Texture2D _seRoll, _seRollBig, _seBalloon, _seKusudama, _seRollEnd;

    private static double _animationTime = 0.0;
    // 他のシステム(Effect/Gauge/MiniTaiko等)と同じく、多重ロードでGPUテクスチャが
    // 参照上書き→リークするのを防ぐガード。Load()が2回呼ばれてもUnload()なしでは再ロードしない。
    private static bool _loaded;

    private const string BasePath = "Lumen/1.Enso/notes/";

    public static void Load()
    {
        if (_loaded) return;

        for (int type = 1; type <= 8; type++)
        {
            var list = new List<Texture2D>();
            for (int frame = 0; File.Exists($"{BasePath}{FolderNames[type]}/{frame}.png"); frame++)
            {
                list.Add(Raylib.LoadTexture($"{BasePath}{FolderNames[type]}/{frame}.png"));
            }
            _frames[type] = list.ToArray();
        }

        _drumrollTail = Raylib.LoadTexture($"{BasePath}roll/_.png");
        _drumrollBigTail = Raylib.LoadTexture($"{BasePath}roll_/_.png");
        _balloonTail = Raylib.LoadTexture($"{BasePath}balloon/_.png");

        _balloonBubble = Raylib.LoadTexture($"{BasePath}balloon/hukidashi.png");
        for (int i = 0; i < 10; i++)
            _balloonDigits[i] = Raylib.LoadTexture($"{BasePath}balloon/n/{i}.png");
        for (int i = 0; i < 8; i++)
            _balloonBig[i] = Raylib.LoadTexture($"{BasePath}balloon/k/{i:00}.png");

        _seDon = Raylib.LoadTexture($"{BasePath}don/m/0.png");
        _seDo = Raylib.LoadTexture($"{BasePath}don/m/1.png");
        _seKo = Raylib.LoadTexture($"{BasePath}ko.png");
        _seKatsu = Raylib.LoadTexture($"{BasePath}katsu/m/0.png");
        _seKa = Raylib.LoadTexture($"{BasePath}katsu/m/1.png");
        _seDonBig = Raylib.LoadTexture($"{BasePath}don_/m.png");
        _seKatsuBig = Raylib.LoadTexture($"{BasePath}katsu_/m.png");
        _seRoll = Raylib.LoadTexture($"{BasePath}roll/m.png");
        _seRollBig = Raylib.LoadTexture($"{BasePath}roll_/m.png");
        _seBalloon = Raylib.LoadTexture($"{BasePath}balloon/m.png");
        _seKusudama = Raylib.LoadTexture($"{BasePath}balloon_/m.png");
        _seRollEnd = Raylib.LoadTexture($"{BasePath}rollend.png");

        VramProbe.Track("NotesTexture.Load (all)");
        _loaded = true;
    }

    public static void Update()
    {
        _animationTime += Raylib.GetFrameTime();
    }

    /// <summary>
    /// 音符を指定座標を中心に描画する。
    /// アニメーションはコンボ数に応じて変化する:
    /// 10未満=0固定(大音符は1固定) / 10以上=160msで0⇔1 / 50以上=80msで0⇔1 / 100以上=80msで0⇔2
    /// （大音符はアニメーション時は常に1⇔2）
    /// </summary>
    public static void DrawNote(int noteType, Vector2 position, float scale = 1f)
    {
        DrawNote(noteType, position, scale, Enso.Combo);
    }

    /// <summary>指定プレイヤーのコンボ値でフレームアニメーションを選択して音符を描画する。</summary>
    public static void DrawNote(int noteType, Vector2 position, float scale, int combo)
    {
        if (noteType < 1 || noteType > 8) return;

        Texture2D[] frames = _frames[noteType];
        if (frames == null || frames.Length == 0) return;
        int frameIndex = (noteType == 3 || noteType == 4) ? 1 : 0;

        if (combo >= 10)
        {
            double interval = combo >= 50 ? 0.08 : 0.16;
            bool alt = ((long)(_animationTime / interval) & 1) == 1;

            bool isBig = FolderNames[noteType].EndsWith("_");
            int frameA, frameB;
            if (isBig) { frameA = 1; frameB = 2; }
            else if (combo >= 100) { frameA = 0; frameB = 2; }
            else { frameA = 0; frameB = 1; }

            frameIndex = alt ? frameB : frameA;
        }

        if (frameIndex >= frames.Length) frameIndex = frames.Length - 1;
        Texture2D tex = frames[frameIndex];

        float w = tex.Width * scale;
        float h = tex.Height * scale;
        Rectangle sourceRect = new Rectangle(0, 0, tex.Width, tex.Height);
        Rectangle destRect = new Rectangle(position.X, position.Y, w, h);
        Vector2 origin = new Vector2(w / 2f, h / 2f);

        Raylib.DrawTexturePro(tex, sourceRect, destRect, origin, 0f, Color.White);
    }

    /// <summary>
    /// 口唱歌 (m画像) を上端中央基準で描画する。
    /// </summary>
    public static void DrawSeNote(SeNoteType se, bool isBigRoll, Vector2 position, float scale = 1f)
    {
        Texture2D tex = se switch
        {
            SeNoteType.Don => _seDon,
            SeNoteType.Do => _seDo,
            SeNoteType.Ko => _seKo,
            SeNoteType.Katsu => _seKatsu,
            SeNoteType.Ka => _seKa,
            SeNoteType.DON => _seDonBig,
            SeNoteType.KA => _seKatsuBig,
            SeNoteType.RollStart => isBigRoll ? _seRollBig : _seRoll,
            SeNoteType.Balloon => _seBalloon,
            SeNoteType.Kusudama => _seKusudama,
            SeNoteType.RollEnd => _seRollEnd,
            _ => default,
        };
        if (tex.Id == 0) return;

        float w = tex.Width * scale;
        float h = tex.Height * scale;
        Rectangle sourceRect = new Rectangle(0, 0, tex.Width, tex.Height);
        Rectangle destRect = new Rectangle(position.X, position.Y, w, h);
        Vector2 origin = new Vector2(w / 2f, 0f);

        Raylib.DrawTexturePro(tex, sourceRect, destRect, origin, 0f, Color.White);
    }

    /// <summary>
    /// 連打の口唱歌 (m画像) の右端1列を横方向に引き伸ばし、rollend画像の左端まで繋げて描画する。
    /// </summary>
    public static void DrawSeRollBody(bool isBigRoll, float headX, float endX, float y, float scale = 1f)
    {
        Texture2D tex = isBigRoll ? _seRollBig : _seRoll;
        if (tex.Id == 0 || _seRollEnd.Id == 0) return;

        float left = headX + tex.Width * scale / 2f;
        float right = endX - _seRollEnd.Width * scale / 2f;
        if (right <= left) return;

        float h = tex.Height * scale;
        Rectangle sourceRect = new Rectangle(tex.Width - 1, 0, 1, tex.Height);
        Rectangle destRect = new Rectangle(left, y, right - left, h);

        Raylib.DrawTexturePro(tex, sourceRect, destRect, Vector2.Zero, 0f, Color.White);
    }

    /// <summary>
    /// 連打ノーツの胴体部分を、終端画像の左端1列を横方向に引き伸ばして描画する。
    /// </summary>
    public static void DrawDrumrollBody(bool isBig, float x, float width, float centerY, float scale = 1f)
    {
        if (width <= 0) return;

        Texture2D tex = isBig ? _drumrollBigTail : _drumrollTail;
        float h = tex.Height * scale;

        Rectangle sourceRect = new Rectangle(0, 0, 1, tex.Height);
        Rectangle destRect = new Rectangle(x, centerY, width, h);
        Vector2 origin = new Vector2(0, h / 2f);

        Raylib.DrawTexturePro(tex, sourceRect, destRect, origin, 0f, Color.White);
    }

    // ヘッド音符のフレーム切り替えと同じ周期で交互フレーム側かを返す
    private static bool IsAltFrame(int combo)
    {
        if (combo < 10) return false;
        double interval = combo >= 50 ? 0.08 : 0.16;
        return ((long)(_animationTime / interval) & 1) == 1;
    }

    /// <summary>
    /// 風船本体 (_.png) をヘッド音符の右横に密着させて描画する。
    /// ヘッドのアニメーション周期に合わせて幅 90 ⇔ 原寸 で伸縮する。
    /// </summary>
    public static void DrawBalloonBody(Vector2 headCenter, float scale = 1f)
    {
        DrawBalloonBody(headCenter, scale, Enso.Combo);
    }

    /// <summary>指定プレイヤーのコンボ値に合わせて風船本体をアニメーション描画する。</summary>
    public static void DrawBalloonBody(Vector2 headCenter, float scale, int combo)
    {
        if (_balloonTail.Id == 0) return;

        Texture2D[] frames = _frames[7];
        float headHalf = (frames != null && frames.Length > 0 ? frames[0].Width : 0) * scale / 2f;

        Texture2D tex = _balloonTail;
        float w = (IsAltFrame(combo) ? 90f : tex.Width) * scale;
        float h = tex.Height * scale;

        Rectangle sourceRect = new Rectangle(0, 0, tex.Width, tex.Height);
        Rectangle destRect = new Rectangle(headCenter.X + headHalf, headCenter.Y, w, h);

        Raylib.DrawTexturePro(tex, sourceRect, destRect, new Vector2(0, h / 2f), 0f, Color.White);
    }

    /// <summary>吹き出し (hukidashi.png) を左上基準で描画する。</summary>
    public static void DrawBalloonBubble(Vector2 topLeft, float scale = 1f)
    {
        if (_balloonBubble.Id == 0) return;

        Rectangle sourceRect = new Rectangle(0, 0, _balloonBubble.Width, _balloonBubble.Height);
        Rectangle destRect = new Rectangle(topLeft.X, topLeft.Y,
            _balloonBubble.Width * scale, _balloonBubble.Height * scale);

        Raylib.DrawTexturePro(_balloonBubble, sourceRect, destRect, Vector2.Zero, 0f, Color.White);
    }

    public static void DrawBalloonNumber(int value, float centerX, float topY, float scale = 1f, float heightPx = 0f)
    {
        string digits = Math.Max(0, value).ToString();
        Texture2D first = _balloonDigits[digits[0] - '0'];
        if (first.Id == 0) return;

        float digitW = first.Width * scale;
        float advance = digitW - 25f * scale;
        float total = advance * (digits.Length - 1) + digitW;
        float x = centerX - total / 2f;

        foreach (char c in digits)
        {
            Texture2D tex = _balloonDigits[c - '0'];
            if (tex.Id != 0)
            {
                float nativeH = tex.Height * scale;
                float h = heightPx > 0f ? heightPx * scale : nativeH;
                float y = topY + nativeH - h;

                Rectangle sourceRect = new Rectangle(0, 0, tex.Width, tex.Height);
                Rectangle destRect = new Rectangle(x, y, tex.Width * scale, h);
                Raylib.DrawTexturePro(tex, sourceRect, destRect, Vector2.Zero, 0f, Color.White);
            }
            x += advance;
        }
    }

    /// <summary>膨らみアニメ (k/00-07.png) を左端中央基準で描画する。</summary>
    public static void DrawBalloonBig(int frame, Vector2 leftCenter, float scale = 1f, byte alpha = 255)
    {
        if (frame < 0 || frame > 7) return;
        Texture2D tex = _balloonBig[frame];
        if (tex.Id == 0) return;

        float w = tex.Width * scale;
        float h = tex.Height * scale;

        Rectangle sourceRect = new Rectangle(0, 0, tex.Width, tex.Height);
        Rectangle destRect = new Rectangle(leftCenter.X, leftCenter.Y, w, h);

        Raylib.DrawTexturePro(tex, sourceRect, destRect, new Vector2(0, h / 2f), 0f,
            new Color((byte)255, (byte)255, (byte)255, alpha));
    }

    /// <summary>
    /// 連打・風船の終端用テクスチャを、左端中央を基準に描画する。
    /// </summary>
    /// <param name="tailType">5=小連打終端, 6=大連打終端, 7=風船ひも</param>
    public static void DrawTail(int tailType, Vector2 position, float scale = 1f)
    {
        Texture2D tex;
        if (tailType == 5) tex = _drumrollTail;
        else if (tailType == 6) tex = _drumrollBigTail;
        else if (tailType == 7) tex = _balloonTail;
        else return;

        float w = tex.Width * scale;
        float h = tex.Height * scale;

        Rectangle sourceRect = new Rectangle(0, 0, tex.Width, tex.Height);
        Rectangle destRect = new Rectangle(position.X, position.Y, w, h);
        Vector2 origin = new Vector2(0, h / 2f);

        Raylib.DrawTexturePro(tex, sourceRect, destRect, origin, 0f, Color.White);
    }

    public static void Unload()
    {
        if (!_loaded) return;

        for (int type = 1; type <= 8; type++)
        {
            if (_frames[type] == null) continue;
            foreach (var tex in _frames[type])
            {
                if (tex.Id != 0) Raylib.UnloadTexture(tex);
            }
            _frames[type] = null;
        }
        if (_drumrollTail.Id != 0) Raylib.UnloadTexture(_drumrollTail);
        if (_drumrollBigTail.Id != 0) Raylib.UnloadTexture(_drumrollBigTail);
        if (_balloonTail.Id != 0) Raylib.UnloadTexture(_balloonTail);

        if (_balloonBubble.Id != 0) Raylib.UnloadTexture(_balloonBubble);
        _balloonBubble = default;
        for (int i = 0; i < 10; i++)
        {
            if (_balloonDigits[i].Id != 0) Raylib.UnloadTexture(_balloonDigits[i]);
            _balloonDigits[i] = default;
        }
        for (int i = 0; i < 8; i++)
        {
            if (_balloonBig[i].Id != 0) Raylib.UnloadTexture(_balloonBig[i]);
            _balloonBig[i] = default;
        }

        Texture2D[] seTextures =
        {
            _seDon, _seDo, _seKo, _seKatsu, _seKa,
            _seDonBig, _seKatsuBig, _seRoll, _seRollBig,
            _seBalloon, _seKusudama, _seRollEnd
        };
        foreach (var tex in seTextures)
        {
            if (tex.Id != 0) Raylib.UnloadTexture(tex);
        }
        _seDon = _seDo = _seKo = _seKatsu = _seKa = default;
        _seDonBig = _seKatsuBig = default;
        _seRoll = _seRollBig = _seBalloon = _seKusudama = _seRollEnd = default;

        _loaded = false;
    }
}

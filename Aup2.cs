using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using Raylib_cs;
using TaikoNauts.Core.Taiko.Helper;

public sealed class Aup2Anim : IDisposable
{
    sealed class Track
    {
        public float[] Values = { 0f };
        public string Easing = "";

        public static Track Constant(float v) => new() { Values = new[] { v } };

        public float Evaluate(float t, float[] bounds)
        {
            if (Values.Length == 1) return Values[0];
            int segs = Values.Length - 1;
            t = Math.Clamp(t, 0f, 1f);

            int seg;
            float lt;
            if (bounds != null && bounds.Length == segs - 1)
            {
                seg = 0;
                while (seg < bounds.Length && t >= bounds[seg]) seg++;
                float b0 = seg == 0 ? 0f : bounds[seg - 1];
                float b1 = seg == bounds.Length ? 1f : bounds[seg];
                lt = b1 > b0 ? (t - b0) / (b1 - b0) : 0f;
            }
            else
            {
                seg = Math.Min((int)(t * segs), segs - 1);
                lt = t * segs - seg;
            }

            float v0 = Values[seg], v1 = Values[seg + 1];
            switch (Easing)
            {
                case "瞬間移動": return v0;
                case "加減速移動": lt = lt * lt * (3f - 2f * lt); break;
            }
            return v0 + (v1 - v0) * lt;
        }
    }

    sealed class Obj
    {
        public int Layer;
        public int Index;
        public float FrameStart, FrameEnd;
        public float[] Bounds;
        public Texture2D Tex;
        public bool Additive;
        public Track X = Track.Constant(0), Y = Track.Constant(0);
        public Track Cx = Track.Constant(0), Cy = Track.Constant(0);
        public Track RotZ = Track.Constant(0);
        public Track Zoom = Track.Constant(100), Aspect = Track.Constant(0);
        public Track Alpha = Track.Constant(0);
        public Track ClipT = Track.Constant(0), ClipB = Track.Constant(0);
        public Track ClipL = Track.Constant(0), ClipR = Track.Constant(0);
        public bool IsGroup;
        public int TargetLayers;
        public bool FlipH, FlipV, InvertColor;
        public Track ZeAll, ZeX, ZeY;
    }

    readonly List<Obj> _objs = new();
    readonly List<Obj> _groups = new();
    readonly Dictionary<string, Texture2D> _texCache = new(StringComparer.OrdinalIgnoreCase);

    float _sceneW = 1920, _sceneH = 1080, _rate = 60;
    float _maxFrame;

    public double Duration => _rate > 0 ? (_maxFrame + 1) / _rate : 0;
    public float SceneWidth => _sceneW;
    public float SceneHeight => _sceneH;

    public static Aup2Anim Load(string path)
    {
        try
        {
            var anim = new Aup2Anim();
            anim.Parse(FileReader.ReadTextAuto(path), Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
            return anim._objs.Count > 0 ? anim : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// コードに直接埋め込んだaup2テキストを読み込む。
    /// 画像パス(「ファイル」キー)はbaseDirを基準に解決される。
    /// 例: baseDir = "Lumen/Demo" なら、埋め込みテキスト内の元の絶対パス
    ///     (例: C:/User/Unti/yarimasunele/xxx.png) は無視され、
    ///     ファイル名部分だけを使って Lumen/Demo/xxx.png を探す。
    /// </summary>
    public static Aup2Anim LoadFromText(string aup2Text, string baseDir)
    {
        try
        {
            var anim = new Aup2Anim();
            anim.Parse(aup2Text, Path.GetFullPath(baseDir));
            return anim._objs.Count > 0 ? anim : null;
        }
        catch
        {
            return null;
        }
    }

    void Parse(string text, string dir)
    {
        var lines = text
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

        int displayScene = 0, currentScene = -1;
        bool sceneRead = false;
        Obj obj = null;
        string effect = "";
        string pendingImage = null;

        void CommitObject()
        {
            if (obj == null) return;
            if (obj.IsGroup)
            {
                _groups.Add(obj);
            }
            else if (pendingImage != null)
            {
                string file = Path.Combine(dir, Path.GetFileName(pendingImage));
                if (File.Exists(file))
                {
                    string cacheKey = obj.InvertColor ? file + "#inv" : file;
                    if (!_texCache.TryGetValue(cacheKey, out var tex))
                    {
                        if (obj.InvertColor)
                        {
                            var img = Raylib.LoadImage(file);
                            Raylib.ImageColorInvert(ref img);
                            tex = Raylib.LoadTextureFromImage(img);
                            Raylib.UnloadImage(img);
                        }
                        else
                        {
                            tex = Raylib.LoadTexture(file);
                        }
                        _texCache[cacheKey] = tex;
                    }
                    if (tex.Id != 0)
                    {
                        obj.Tex = tex;
                        _objs.Add(obj);
                        _maxFrame = Math.Max(_maxFrame, obj.FrameEnd);
                    }
                }
            }
            obj = null;
            pendingImage = null;
        }

        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                string sec = line[1..^1];
                if (sec.StartsWith("scene."))
                {
                    CommitObject();
                    int.TryParse(sec[6..], out currentScene);
                    sceneRead = currentScene == displayScene;
                    effect = "";
                }
                else if (sec == "project")
                {
                    currentScene = -1;
                    effect = "";
                }
                else
                {
                    int dot = sec.IndexOf('.');
                    if (dot < 0)
                    {
                        CommitObject();
                        effect = "";
                        if (sceneRead && int.TryParse(sec, out int idx))
                            obj = new Obj { Index = idx };
                    }
                    else
                    {
                        effect = "";
                    }
                }
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            string key = line[..eq];
            string val = line[(eq + 1)..];

            if (currentScene == -1)
            {
                if (key == "display.scene") int.TryParse(val, out displayScene);
                continue;
            }

            if (obj == null)
            {
                if (!sceneRead) continue;
                if (key == "video.width") ParseF(val, ref _sceneW);
                else if (key == "video.height") ParseF(val, ref _sceneH);
                else if (key == "video.rate") ParseF(val, ref _rate);
                continue;
            }

            if (key == "effect.name")
            {
                effect = val;
                if (val == "グループ制御") obj.IsGroup = true;
                continue;
            }

            if (effect == "")
            {
                if (key == "layer") int.TryParse(val, out obj.Layer);
                else if (key == "frame")
                {
                    var nums = ParseFloats(val);
                    if (nums.Length >= 2)
                    {
                        obj.FrameStart = nums[0];
                        obj.FrameEnd = nums[^1];
                        if (nums.Length > 2 && obj.FrameEnd > obj.FrameStart)
                        {
                            obj.Bounds = new float[nums.Length - 2];
                            for (int i = 0; i < obj.Bounds.Length; i++)
                                obj.Bounds[i] = (nums[i + 1] - obj.FrameStart) / (obj.FrameEnd - obj.FrameStart);
                        }
                    }
                }
            }
            else if (effect == "画像ファイル")
            {
                if (key == "ファイル") pendingImage = val;
            }
            else if (effect == "標準描画")
            {
                switch (key)
                {
                    case "X": obj.X = ParseTrack(val); break;
                    case "Y": obj.Y = ParseTrack(val); break;
                    case "中心X": obj.Cx = ParseTrack(val); break;
                    case "中心Y": obj.Cy = ParseTrack(val); break;
                    case "Z軸回転": obj.RotZ = ParseTrack(val); break;
                    case "拡大率": obj.Zoom = ParseTrack(val); break;
                    case "縦横比": obj.Aspect = ParseTrack(val); break;
                    case "透明度": obj.Alpha = ParseTrack(val); break;
                    case "合成モード": obj.Additive = val == "加算"; break;
                }
            }
            else if (effect == "クリッピング")
            {
                switch (key)
                {
                    case "上": obj.ClipT = ParseTrack(val); break;
                    case "下": obj.ClipB = ParseTrack(val); break;
                    case "左": obj.ClipL = ParseTrack(val); break;
                    case "右": obj.ClipR = ParseTrack(val); break;
                }
            }
            else if (effect == "グループ制御")
            {
                switch (key)
                {
                    case "X": obj.X = ParseTrack(val); break;
                    case "Y": obj.Y = ParseTrack(val); break;
                    case "拡大率": obj.Zoom = ParseTrack(val); break;
                    case "対象レイヤー数": int.TryParse(val, out obj.TargetLayers); break;
                }
            }
            else if (effect == "拡大率")
            {
                switch (key)
                {
                    case "拡大率": obj.ZeAll = ParseTrack(val); break;
                    case "X": obj.ZeX = ParseTrack(val); break;
                    case "Y": obj.ZeY = ParseTrack(val); break;
                }
            }
            else if (effect == "反転")
            {
                switch (key)
                {
                    case "左右反転": obj.FlipH = val == "1"; break;
                    case "上下反転": obj.FlipV = val == "1"; break;
                    case "輝度反転":
                    case "色相反転": if (val == "1") obj.InvertColor = true; break;
                }
            }
        }
        CommitObject();

        _objs.Sort((a, b) => a.Layer != b.Layer ? a.Layer - b.Layer : a.Index - b.Index);
        _groups.Sort((a, b) => b.Layer - a.Layer);
    }

    static void ParseF(string s, ref float target)
    {
        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            target = v;
    }

    static float[] ParseFloats(string s)
    {
        var parts = s.Split(',');
        var list = new List<float>(parts.Length);
        foreach (var p in parts)
        {
            if (float.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                list.Add(v);
            else
                break;
        }
        return list.ToArray();
    }

    static Track ParseTrack(string s)
    {
        var parts = s.Split(',');
        var values = new List<float>(parts.Length);
        string easing = "";
        foreach (var p in parts)
        {
            string tok = p.Trim();
            if (float.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                values.Add(v);
            else
            {
                easing = tok;
                break;
            }
        }
        if (values.Count == 0) values.Add(0f);
        return new Track { Values = values.ToArray(), Easing = easing };
    }

    public void Draw(float x, float y, float w, float h, float seconds, Color tint)
    {
        if (_objs.Count == 0 || _sceneW <= 0 || _sceneH <= 0 || w <= 0 || h <= 0) return;

        float frame = seconds * _rate;
        float sx = w / _sceneW, sy = h / _sceneH;
        float centerX = x + w / 2f, centerY = y + h / 2f;

        Raylib.BeginScissorMode((int)x, (int)y, (int)w, (int)h);
        foreach (var o in _objs)
        {
            if (frame < o.FrameStart || frame >= o.FrameEnd + 1) continue;
            float t = o.FrameEnd > o.FrameStart
                ? Math.Min((frame - o.FrameStart) / (o.FrameEnd - o.FrameStart), 1f) : 0f;

            float alpha = 1f - o.Alpha.Evaluate(t, o.Bounds) / 100f;
            if (alpha <= 0f) continue;

            float zoom = o.Zoom.Evaluate(t, o.Bounds) / 100f;
            float aspect = o.Aspect.Evaluate(t, o.Bounds);
            float zx = zoom, zy = zoom;
            if (aspect > 0f) zx *= 1f - aspect / 100f;
            else if (aspect < 0f) zy *= 1f + aspect / 100f;

            if (o.ZeAll != null)
            {
                float z = o.ZeAll.Evaluate(t, o.Bounds) / 100f;
                zx *= z; zy *= z;
            }
            if (o.ZeX != null) zx *= o.ZeX.Evaluate(t, o.Bounds) / 100f;
            if (o.ZeY != null) zy *= o.ZeY.Evaluate(t, o.Bounds) / 100f;

            float posX = o.X.Evaluate(t, o.Bounds);
            float posY = o.Y.Evaluate(t, o.Bounds);

            foreach (var g in _groups)
            {
                if (o.Layer <= g.Layer) continue;
                if (g.TargetLayers > 0 && o.Layer > g.Layer + g.TargetLayers) continue;
                if (frame < g.FrameStart || frame >= g.FrameEnd + 1) continue;
                float gt = g.FrameEnd > g.FrameStart
                    ? Math.Min((frame - g.FrameStart) / (g.FrameEnd - g.FrameStart), 1f) : 0f;
                float gz = g.Zoom.Evaluate(gt, g.Bounds) / 100f;
                posX = g.X.Evaluate(gt, g.Bounds) + gz * posX;
                posY = g.Y.Evaluate(gt, g.Bounds) + gz * posY;
                zx *= gz; zy *= gz;
            }

            float clipL = o.ClipL.Evaluate(t, o.Bounds), clipR = o.ClipR.Evaluate(t, o.Bounds);
            float clipT = o.ClipT.Evaluate(t, o.Bounds), clipB = o.ClipB.Evaluate(t, o.Bounds);
            float srcW = o.Tex.Width - clipL - clipR;
            float srcH = o.Tex.Height - clipT - clipB;
            if (srcW <= 0f || srcH <= 0f) continue;

            float destW = srcW * zx * sx, destH = srcH * zy * sy;
            float px = centerX + (posX + (clipL - clipR) / 2f * zx) * sx;
            float py = centerY + (posY + (clipT - clipB) / 2f * zy) * sy;
            float ox = destW / 2f + o.Cx.Evaluate(t, o.Bounds) * zx * sx;
            float oy = destH / 2f + o.Cy.Evaluate(t, o.Bounds) * zy * sy;

            var color = new Color(tint.R, tint.G, tint.B, (byte)Math.Clamp(tint.A * alpha, 0f, 255f));

            if (o.Additive) Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.DrawTexturePro(o.Tex,
                new Rectangle(clipL, clipT, o.FlipH ? -srcW : srcW, o.FlipV ? -srcH : srcH),
                new Rectangle(px, py, destW, destH),
                new Vector2(ox, oy),
                o.RotZ.Evaluate(t, o.Bounds),
                color);
            if (o.Additive) Raylib.EndBlendMode();
        }
        Raylib.EndScissorMode();
    }

    public void Dispose()
    {
        foreach (var tex in _texCache.Values)
            if (tex.Id != 0) Raylib.UnloadTexture(tex);
        _texCache.Clear();
        _objs.Clear();
        _groups.Clear();
    }
}
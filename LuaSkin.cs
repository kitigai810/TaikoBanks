using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using MoonSharp.Interpreter;
using Raylib_cs;
using SkiaSharp;
using SkiaSharp.Skottie;
using SkiaSharp.Resources;

public sealed class LuaSkin : IDisposable
{
    Script _script;
    Table _state;
    string _baseDir = "";
    readonly Dictionary<int, Texture2D> _textures = new();
    int _nextHandle = 1;

    readonly Dictionary<int, LottieAnim> _lotties = new();
    int _nextLottie = 1;

    readonly Dictionary<int, Aup2Anim> _aup2s = new();
    int _nextAup2 = 1;

    sealed class LottieAnim
    {
        public Animation Anim;
        public Texture2D Tex;
        public int W, H;
        public bool HasTex;
        public float LastT = float.NaN;
        public SKBitmap Bmp;
        public byte[] Buffer;
    }

    public bool Ok { get; private set; }
    public bool Finished { get; private set; }
    public bool LoadRequested { get; private set; }
    public string Error { get; private set; } = "";

    public void Load(string luaPath)
    {
        Ok = false;
        Finished = false;
        LoadRequested = false;
        Error = "";
        UnloadTextures();

        try
        {
            _baseDir = Path.GetDirectoryName(Path.GetFullPath(luaPath)) ?? ".";
            _script = new Script(CoreModules.Preset_SoftSandbox);
            _state = new Table(_script);
            _script.Globals["state"] = _state;
            RegisterApi();

            string code = File.ReadAllText(luaPath);
            _script.DoString(code, null, Path.GetFileName(luaPath));
            Ok = true;
        }
        catch (Exception e)
        {
            Error = e.Message;
            _script = null;
        }
    }

    public void UpdateState(bool loaded, float time, int width, int height, SongMeta meta, bool cleared = false)
    {
        if (_state == null) return;
        _state["loaded"] = loaded;
        _state["cleared"] = cleared;
        _state["time"] = time;
        _state["width"] = width;
        _state["height"] = height;
        _state["title"] = meta?.Title ?? "";
        _state["subtitle"] = meta?.Subtitle ?? "";
        _state["genre"] = meta?.Genre ?? "";
        _state["difficulty"] = meta?.Difficulty ?? "";
        _state["level"] = meta?.Level ?? 0;
    }

    public void CallInit() => CallVoid("init");
    public void CallDraw() => CallVoid("draw");
    public void CallUpdate(float dt)
    {
        if (!Ok) return;
        var fn = _script.Globals.Get("update");
        if (fn.Type != DataType.Function) return;
        try { _script.Call(fn, DynValue.NewNumber(dt)); }
        catch (Exception e) { Ok = false; Error = e.Message; }
    }

    void CallVoid(string name)
    {
        if (!Ok) return;
        var fn = _script.Globals.Get(name);
        if (fn.Type != DataType.Function) return;
        try { _script.Call(fn); }
        catch (Exception e) { Ok = false; Error = e.Message; }
    }

    void RegisterApi()
    {
        _script.Globals["load_texture"] = DynValue.NewCallback((ctx, a) =>
            DynValue.NewNumber(LoadTexture(ArgStr(a, 0, ""))));

        _script.Globals["tex_width"] = DynValue.NewCallback((ctx, a) =>
            DynValue.NewNumber(_textures.TryGetValue((int)Arg(a, 0, 0), out var t) ? t.Width : 0));

        _script.Globals["tex_height"] = DynValue.NewCallback((ctx, a) =>
            DynValue.NewNumber(_textures.TryGetValue((int)Arg(a, 0, 0), out var t) ? t.Height : 0));

        _script.Globals["draw_rect"] = DynValue.NewCallback((ctx, a) =>
        {
            Raylib.DrawRectangleRec(
                new Rectangle(Arg(a, 0, 0), Arg(a, 1, 0), Arg(a, 2, 0), Arg(a, 3, 0)),
                ColorAt(a, 4));
            return DynValue.Nil;
        });

        _script.Globals["draw_text"] = DynValue.NewCallback((ctx, a) =>
        {
            Raylib.DrawTextEx(G.Font, ArgStr(a, 0, ""),
                new Vector2(Arg(a, 1, 0), Arg(a, 2, 0)), Arg(a, 3, 24), 2, ColorAt(a, 4));
            return DynValue.Nil;
        });

        _script.Globals["draw_texture"] = DynValue.NewCallback((ctx, a) =>
        {
            if (_textures.TryGetValue((int)Arg(a, 0, 0), out var tex))
            {
                float w = Arg(a, 3, tex.Width), h = Arg(a, 4, tex.Height);
                DrawPro(tex, new Rectangle(0, 0, tex.Width, tex.Height),
                    Arg(a, 1, 0), Arg(a, 2, 0), w, h, Arg(a, 5, 0), ColorAt(a, 6));
            }
            return DynValue.Nil;
        });

        _script.Globals["draw_texture_region"] = DynValue.NewCallback((ctx, a) =>
        {
            if (_textures.TryGetValue((int)Arg(a, 0, 0), out var tex))
            {
                var src = new Rectangle(Arg(a, 1, 0), Arg(a, 2, 0), Arg(a, 3, 0), Arg(a, 4, 0));
                DrawPro(tex, src, Arg(a, 5, 0), Arg(a, 6, 0), Arg(a, 7, 0), Arg(a, 8, 0),
                    Arg(a, 9, 0), ColorAt(a, 10));
            }
            return DynValue.Nil;
        });

        _script.Globals["finish"] = DynValue.NewCallback((ctx, a) =>
        {
            Finished = true;
            return DynValue.Nil;
        });

        _script.Globals["start_load"] = DynValue.NewCallback((ctx, a) =>
        {
            LoadRequested = true;
            return DynValue.Nil;
        });

        _script.Globals["begin_blend"] = DynValue.NewCallback((ctx, a) =>
        {
            var mode = ArgStr(a, 0, "").ToLowerInvariant() switch
            {
                "add" or "additive" => BlendMode.Additive,
                "multiply" => BlendMode.Multiplied,
                _ => BlendMode.Alpha
            };
            Raylib.BeginBlendMode(mode);
            return DynValue.Nil;
        });

        _script.Globals["end_blend"] = DynValue.NewCallback((ctx, a) =>
        {
            Raylib.EndBlendMode();
            return DynValue.Nil;
        });

        _script.Globals["load_lottie"] = DynValue.NewCallback((ctx, a) =>
            DynValue.NewNumber(LoadLottie(ArgStr(a, 0, ""))));

        _script.Globals["lottie_duration"] = DynValue.NewCallback((ctx, a) =>
            DynValue.NewNumber(_lotties.TryGetValue((int)Arg(a, 0, 0), out var l)
                ? l.Anim.Duration.TotalSeconds : 0));

        _script.Globals["draw_lottie"] = DynValue.NewCallback((ctx, a) =>
        {
            if (_lotties.TryGetValue((int)Arg(a, 0, 0), out var la))
            {
                float x = Arg(a, 1, 0), y = Arg(a, 2, 0), w = Arg(a, 3, 0), h = Arg(a, 4, 0);
                RenderLottie(la, (int)w, (int)h, Arg(a, 5, 0));
                if (la.HasTex)
                    DrawPro(la.Tex, new Rectangle(0, 0, la.W, la.H), x, y, w, h, Arg(a, 6, 0), ColorAt(a, 7));
            }
            return DynValue.Nil;
        });

        _script.Globals["load_aup2"] = DynValue.NewCallback((ctx, a) =>
            DynValue.NewNumber(LoadAup2(ArgStr(a, 0, ""))));

        _script.Globals["aup2_duration"] = DynValue.NewCallback((ctx, a) =>
            DynValue.NewNumber(_aup2s.TryGetValue((int)Arg(a, 0, 0), out var an)
                ? an.Duration : 0));

        _script.Globals["draw_aup2"] = DynValue.NewCallback((ctx, a) =>
        {
            if (_aup2s.TryGetValue((int)Arg(a, 0, 0), out var an))
                an.Draw(Arg(a, 1, 0), Arg(a, 2, 0), Arg(a, 3, 0), Arg(a, 4, 0),
                    Arg(a, 5, 0), ColorAt(a, 6));
            return DynValue.Nil;
        });
    }

    int LoadAup2(string rel)
    {
        if (string.IsNullOrEmpty(rel) || rel.Contains("..")) return 0;
        string full = Path.GetFullPath(Path.Combine(_baseDir, rel));
        if (!full.StartsWith(_baseDir, StringComparison.OrdinalIgnoreCase)) return 0;

        string file;
        if (Directory.Exists(full))
        {
            var files = Directory.GetFiles(full, "*.aup2");
            if (files.Length == 0) return 0;
            Array.Sort(files);
            file = files[0];
        }
        else if (File.Exists(full)) file = full;
        else return 0;

        var anim = Aup2Anim.Load(file);
        if (anim == null) return 0;
        int handle = _nextAup2++;
        _aup2s[handle] = anim;
        return handle;
    }

    int LoadLottie(string rel)
    {
        if (string.IsNullOrEmpty(rel) || rel.Contains("..")) return 0;
        string full = Path.GetFullPath(Path.Combine(_baseDir, rel));
        if (!full.StartsWith(_baseDir, StringComparison.OrdinalIgnoreCase)) return 0;

        string file;
        if (Directory.Exists(full))
        {
            var jsons = Directory.GetFiles(full, "*.json");
            if (jsons.Length == 0) return 0;
            Array.Sort(jsons);
            file = jsons[0];
        }
        else if (File.Exists(full)) file = full;
        else return 0;

        try
        {
            using var data = SKData.CreateCopy(File.ReadAllBytes(file));
            using var rp = new FileResourceProvider(Path.GetDirectoryName(file), true);
            var anim = Animation.CreateBuilder(AnimationBuilderFlags.None)
                .SetResourceProvider(rp)
                .Build(data);
            if (anim == null) return 0;
            int handle = _nextLottie++;
            _lotties[handle] = new LottieAnim { Anim = anim };
            return handle;
        }
        catch { return 0; }
    }

    static void RenderLottie(LottieAnim la, int w, int h, float seconds)
    {
        w = Math.Clamp(w, 1, 4096);
        h = Math.Clamp(h, 1, 4096);

        if (la.Bmp == null || la.W != w || la.H != h)
        {
            la.Bmp?.Dispose();
            la.Bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            la.Buffer = new byte[w * h * 4];
            if (la.HasTex) Raylib.UnloadTexture(la.Tex);
            Image img = Raylib.GenImageColor(w, h, Color.Blank);
            la.Tex = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
            la.HasTex = true;
            la.W = w;
            la.H = h;
            la.LastT = float.NaN;
        }

        if (la.LastT != seconds)
        {
            using (var canvas = new SKCanvas(la.Bmp))
            {
                canvas.Clear(SKColors.Transparent);
                la.Anim.SeekFrameTime(seconds, null);
                la.Anim.Render(canvas, new SKRect(0, 0, w, h));
            }
            Marshal.Copy(la.Bmp.GetPixels(), la.Buffer, 0, la.Buffer.Length);
            Raylib.UpdateTexture<byte>(la.Tex, la.Buffer);
            la.LastT = seconds;
        }
    }

    int LoadTexture(string rel)
    {
        if (string.IsNullOrEmpty(rel) || rel.Contains("..")) return 0;
        string full = Path.GetFullPath(Path.Combine(_baseDir, rel));
        if (!full.StartsWith(_baseDir, StringComparison.OrdinalIgnoreCase)) return 0;
        if (!File.Exists(full)) return 0;

        Texture2D tex = Raylib.LoadTexture(full);
        if (tex.Id == 0) return 0;
        int handle = _nextHandle++;
        _textures[handle] = tex;
        return handle;
    }

    static void DrawPro(Texture2D tex, Rectangle src, float x, float y, float w, float h, float rot, Color tint)
    {
        var dest = new Rectangle(x + w / 2f, y + h / 2f, w, h);
        Raylib.DrawTexturePro(tex, src, dest, new Vector2(w / 2f, h / 2f), rot, tint);
    }

    static float Arg(CallbackArguments a, int i, float def)
    {
        if (i < a.Count && a[i].Type == DataType.Number) return (float)a[i].Number;
        return def;
    }

    static string ArgStr(CallbackArguments a, int i, string def)
    {
        if (i >= a.Count || a[i].IsNil()) return def;
        return a[i].Type == DataType.String ? a[i].String : a[i].CastToString();
    }

    static Color ColorAt(CallbackArguments a, int i) => new Color(
        (int)Arg(a, i, 255), (int)Arg(a, i + 1, 255),
        (int)Arg(a, i + 2, 255), (int)Arg(a, i + 3, 255));

    void UnloadTextures()
    {
        foreach (var t in _textures.Values)
            if (t.Id != 0) Raylib.UnloadTexture(t);
        _textures.Clear();
        _nextHandle = 1;

        foreach (var l in _lotties.Values)
        {
            if (l.HasTex) Raylib.UnloadTexture(l.Tex);
            l.Bmp?.Dispose();
            l.Anim?.Dispose();
        }
        _lotties.Clear();
        _nextLottie = 1;

        foreach (var an in _aup2s.Values)
            an.Dispose();
        _aup2s.Clear();
        _nextAup2 = 1;
    }

    public void Dispose()
    {
        UnloadTextures();
        _script = null;
        _state = null;
        Ok = false;
    }
}

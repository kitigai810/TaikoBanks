using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

public static class Rainbow
{
    private const float DrawX = 570f, DrawY = -110f;
    private const float PivotX = 704f, PivotY = 799f;
    private const double TotalHoldSec = FlyingNotes.BalloonHoldSec;

    private const string WipeFs = @"#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform vec2 wipeCenter;
uniform float wipeAspect;
uniform float wipeThreshold;
out vec4 finalColor;
void main()
{
    vec4 tex = texture(texture0, fragTexCoord);
    float dx = (fragTexCoord.x - wipeCenter.x) * wipeAspect;
    float dy = wipeCenter.y - fragTexCoord.y;
    float ang = atan(dy, dx);
    tex.a *= smoothstep(wipeThreshold - 0.02, wipeThreshold + 0.02, ang);
    finalColor = tex * colDiffuse * fragColor;
}";

    private struct Burst
    {
        public double SpawnSec;
        public float HitX;
    }

    private static readonly List<Burst> _bursts = new();
    private static Texture2D _tex;
    private static Shader _shader;
    private static int _locCenter, _locAspect, _locThreshold;
    private static bool _loaded;

    public static void Init()
    {
        _bursts.Clear();

        if (!_loaded)
        {
            _tex = Raylib.LoadTexture("Lumen/1.Enso/Effect/Balloon/Niji.png");
            VramProbe.Track("Rainbow.Init (all)");
            _shader = Raylib.LoadShaderFromMemory(null, WipeFs);
            _locCenter = Raylib.GetShaderLocation(_shader, "wipeCenter");
            _locAspect = Raylib.GetShaderLocation(_shader, "wipeAspect");
            _locThreshold = Raylib.GetShaderLocation(_shader, "wipeThreshold");
            _loaded = true;
        }
    }

    public static void Unload()
    {
        if (_loaded)
        {
            if (_tex.Id != 0) Raylib.UnloadTexture(_tex);
            Raylib.UnloadShader(_shader);
            _loaded = false;
        }

        _bursts.Clear();
    }

    public static void Spawn(double nowSec)
    {
        _bursts.Add(new Burst { SpawnSec = nowSec, HitX = Enso.HitX });
    }

    public static void Update(double nowSec)
    {
        _bursts.RemoveAll(b => nowSec - b.SpawnSec >= FlyingNotes.FlightDuration + TotalHoldSec);
    }

    public static void Draw(float vx, float vy, float s, double nowSec)
    {
        if (!_loaded || _tex.Id == 0) return;

        foreach (var b in _bursts)
        {
            double elapsed = nowSec - b.SpawnSec;
            if (elapsed < 0) continue;
            DrawOne(b.HitX, elapsed, vx, vy, s);
        }
    }

    private static void DrawOne(float hitX, double elapsed, float vx, float vy, float s)
    {
        double flight = FlyingNotes.FlightDuration;
        double landElapsed = elapsed - flight;
        if (landElapsed >= TotalHoldSec) return;

        // 飛びノーツの現在角度で放射状にカット(左=表示/右=非表示)。着地後は終点で固定。
        float t = Math.Min((float)(elapsed / flight), 1f);
        // 1Pフライノーツの軌道に追従して出現ワイプを進める。
        Vector2 note = FlyingNotes.FlightPos(t, hitX);
        float baseX = DrawX + (hitX - 618f);
        float baseY = DrawY;
        float pxTex = note.X - baseX;
        float pyTex = note.Y - baseY;
        float noteAngle = MathF.Atan2(PivotY - pyTex, pxTex - PivotX);

        // 着地後は、出現完了時のワイプ境界を固定して保持する。
        // 虹の画像本体・ワイプ境界とも消滅時には回転させない。

        Raylib.SetShaderValue(_shader, _locCenter,
            new Vector2(PivotX / _tex.Width, PivotY / _tex.Height), ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_shader, _locAspect,
            (float)_tex.Width / _tex.Height, ShaderUniformDataType.Float);
        // 出現中はノーツの飛行角度に追従してワイプし、着地後は最後の境界で固定する。
        Raylib.SetShaderValue(_shader, _locThreshold, noteAngle, ShaderUniformDataType.Float);

        var src = new Rectangle(0, 0, _tex.Width, _tex.Height);
        var dest = new Rectangle((baseX + PivotX) * s + vx, (baseY + PivotY) * s + vy, _tex.Width * s, _tex.Height * s);
        var origin = new Vector2(PivotX * s, PivotY * s);
        Raylib.BeginShaderMode(_shader);
        Raylib.DrawTexturePro(_tex, src, dest, origin, 0f, Color.White);
        Raylib.EndShaderMode();
    }
}

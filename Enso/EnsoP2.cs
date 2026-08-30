using Raylib_cs;
using System;
using System.Diagnostics;
using System.Numerics;
using TaikoNauts.Core.Taiko.Charts;

/// <summary>
/// 2P側の演奏ロジック。1Pと同じ譜面を独立判定で流し、レーン・スコア・コンボ・
/// ミニ太鼓UI・入力フラッシュを2P用の状態として表示する。
///
/// 設計方針:
///   ・ノーツ配列(Chip[])はTJA.Chipsと同じ参照を共有する(1Pと同じ譜面を流すだけなので複製不要)。
///     ただし「どのノーツを叩いたか」はChip側のフィールドを書き換えず、
///     このクラス専用の _isHit/_isFailed 配列(インデックス対応)で独立管理する。
///     → 1Pが叩いても2Pの判定状態に影響しない(逆も同様)。
///   ・判定ウィンドウ・スコア計算式はEnso本体のJudgeNoteをそのまま踏襲。
///   ・入力はDrumInput2(仮キー: X=左縁 C=左面 Z=右面 V=右縁)。
/// </summary>
public static class EnsoP2
{
    // ---- レーン位置(2P=下側)。1P(Enso.LANE_Y=386, LANE_TOP=276)は変更しない前提で、
    //      2Pをその下に配置。実際の見た目に合わせて調整してください。----
    public static float HitX = 618f;
    public const float LANE_X = 498f;
    public const float LANE_TOP = 535f;
    public const float LANE_Y = 645f;
    private const float LANE_RIGHT = 1920f;

    private const double WIN_PERFECT = 0.025;
    private const double WIN_GOOD = 0.075;
    private const double WIN_BAD = 0.100;

    private const double NOTE_LOOKAHEAD_SEC = 20.0;
    private const double ROLL_HIT_INTERVAL = 1.0 / 60.0;
    // 1PのSEノート基準(489)と判定ライン(386)の差分を、2Pレーンにもそのまま適用する。
    private const float SE_NOTE_Y_OFFSET = 103f;
    private const float BALLOON_STOP_OFFSET = 12f;

    private static Chip[] _chips = Array.Empty<Chip>();
    private static int[] _noteLayers = { 0 };
    private static bool[] _isHit = Array.Empty<bool>();
    private static bool[] _isFailed = Array.Empty<bool>();
    // 2P AIが各ノーツを既に試行したか。打ち漏らし／誤打をフレームごとに再抽選しないために使う。
    private static bool[] _aiAttempted = Array.Empty<bool>();
    // 2P AIの予定打鍵時刻。譜面時刻の少し前後を狙うことで、機械的な即時打鍵を避ける。
    private static double[] _aiTargetHitSec = Array.Empty<double>();

    private static int _firstActiveIndex;
    private static int _activeRollIndex = -1;
    private static double _rollNextHitSec;

    private static int _perfect, _good, _bad, _miss, _combo, _maxCombo, _score;
    private static int _scorePerNote;
    private static int _rollHits;
    private static int _p2BalloonHits;
    private static double _p2BalloonPopSec = -1.0;

    private static Texture2D _laneTex, _hitTex;
    private static bool _laneAssetsLoaded;

    public static int Score => _score;
    public static int Combo => _combo;
    public static int MaxCombo => _maxCombo;
    public static int Perfect => _perfect;
    public static int Good => _good;
    public static int Bad => _bad;
    public static int Miss => _miss;
    public static int RollHits => _rollHits;
    public static bool IsBalloonActive => _activeRollIndex >= 0
        && _chips.Length > _activeRollIndex
        && _chips[_activeRollIndex]._noteType == NoteType.BalloonStart;

    public static void Init(int scorePerNote)
    {
        _scorePerNote = scorePerNote;

        var src = TJA.Chips;
        int n = src?.Count ?? 0;
        _chips = new Chip[n];
        _isHit = new bool[n];
        _isFailed = new bool[n];
        _aiAttempted = new bool[n];
        _aiTargetHitSec = new double[n];
        Array.Fill(_aiTargetHitSec, double.NaN);
        for (int i = 0; i < n; i++) _chips[i] = src[i];

        var layerSet = new System.Collections.Generic.SortedSet<int>();
        for (int i = 0; i < n; i++) layerSet.Add(_chips[i]._layer);
        if (layerSet.Count == 0) layerSet.Add(0);
        _noteLayers = new int[layerSet.Count];
        layerSet.CopyTo(_noteLayers);

        _firstActiveIndex = 0;
        _activeRollIndex = -1;
        _rollNextHitSec = 0;
        _perfect = _good = _bad = _miss = _combo = _maxCombo = _score = 0;
        _rollHits = 0;
        _p2BalloonHits = 0;
        _p2BalloonPopSec = -1.0;
        MiniTaiko.ResetP2();

        if (!_laneAssetsLoaded)
        {
            _laneTex = Raylib.LoadTexture("Lumen/1.Enso/Lane/Lane.png");
            _hitTex = Raylib.LoadTexture("Lumen/1.Enso/Lane/h.png");
            VramProbe.Track("EnsoP2.Init (lane)");
            _laneAssetsLoaded = true;
        }
    }

    public static void Start()
    {
        DrumInput2.Clear();
        DrumInput2.Enabled = true;
    }

    public static void Stop()
    {
        DrumInput2.Enabled = false;
    }

    public static void Unload()
    {
        if (_laneAssetsLoaded)
        {
            if (_laneTex.Id != 0) Raylib.UnloadTexture(_laneTex);
            if (_hitTex.Id != 0) Raylib.UnloadTexture(_hitTex);
            _laneAssetsLoaded = false;
        }
        _chips = Array.Empty<Chip>();
        _isHit = Array.Empty<bool>();
        _isFailed = Array.Empty<bool>();
        _aiAttempted = Array.Empty<bool>();
        _aiTargetHitSec = Array.Empty<double>();
    }

    public static void Update(double nowSec, double speed, int aiLevel)
    {
        aiLevel = Math.Clamp(aiLevel, 0, 10);
        bool fullAuto = aiLevel >= 10;
        DrumInput2.SoundEnabled = !fullAuto;

        if (fullAuto)
        {
            DrumInput2.Clear();
            AutoPlay(nowSec, 10);
        }
        else
        {
            long frameTicks = Stopwatch.GetTimestamp();
            if (DrumInput2.TryDequeueRateLimited(out var hit))
            {
                double hitSec = nowSec - ((frameTicks - hit.Ticks) / (double)Stopwatch.Frequency) * speed;
                if (hitSec >= nowSec - 0.5)
                    HandleHit(hit.IsDon, hit.IsLeft, hitSec, nowSec);
            }

            // AIレベル1〜9は2Pの手動入力を維持しながら不足分を補助する。
            if (aiLevel > 0)
                AutoPlay(nowSec, aiLevel);
        }

        CheckRollEnd(nowSec);
        CheckMiss(nowSec);
    }

    private static void HandleHit(bool isDon, bool isLeft, double hitSec, double nowSec)
    {
        // 1Pと同様に、正誤を問わず有効入力は打鍵音とミニ太鼓の面／縁フラッシュへ反映する。
        if (DrumInput2.SoundEnabled)
        {
            Enso.PlayP2DrumSound(isDon);
            MiniTaiko.TriggerP2Side(isDon, isLeft, false);
        }

        if (_activeRollIndex >= 0)
        {
            var chip = _chips[_activeRollIndex];
            bool matchesRoll = chip._noteType != NoteType.BalloonStart || isDon;
            if (!matchesRoll) return;

            _score += 100;
            _rollHits++;
            bool isBigRoll = chip._noteType == NoteType.RollBigStart;
            EnsoEffect.AddHitEffect(isBigRoll ? EnsoEffectType.GoodBig : EnsoEffectType.Good,
                EnsoJudgeType.None, nowSec, HitX, LANE_Y, isRoll: true);
            if (chip._noteType != NoteType.BalloonStart)
                FlyingNotes.SpawnP2(!isDon, isBigRoll, nowSec);
            else
                CountP2BalloonHit(nowSec);
            if (isBigRoll)
                MiniTaiko.TriggerP2Side(isDon, isLeft, true);
            return;
        }

        JudgeNote(isDon ? 0 : 1, hitSec);
    }

    /// <summary>
    /// 2P専用AI打鍵。レベル10は譜面時刻どおりの完全AUTO、1〜9は人間らしいばらつきを持つ。
    /// 中間レベルでは打ち漏らし・面縁の取り違え・判定範囲内の早遅ズレを発生させる。
    /// </summary>
    private static void AutoPlay(double nowSec, int aiLevel)
    {
        bool perfectAuto = aiLevel >= 10;
        float skill = Math.Clamp(aiLevel, 0, 10) / 10f;

        if (_activeRollIndex >= 0)
        {
            if (nowSec >= _rollNextHitSec)
            {
                // 連打の打鍵自体は必ず行い、低レベルでは間隔だけを少し揺らす。
                float rhythm = perfectAuto ? 1f : 0.88f + Random.Shared.NextSingle() * 0.24f;
                _rollNextHitSec = nowSec + ROLL_HIT_INTERVAL * rhythm;

                var roll = _chips[_activeRollIndex];
                _score += 100;
                _rollHits++;
                bool isBigRoll = roll._noteType == NoteType.RollBigStart;
                EnsoEffect.AddHitEffect(isBigRoll ? EnsoEffectType.GoodBig : EnsoEffectType.Good,
                    EnsoJudgeType.None, nowSec, HitX, LANE_Y, isRoll: true);
                if (roll._noteType != NoteType.BalloonStart)
                    FlyingNotes.SpawnP2(false, isBigRoll, nowSec);
                else
                    CountP2BalloonHit(nowSec);
                Enso.PlayP2DrumSound(true);
                MiniTaiko.TriggerP2Don(isBigRoll);
                }
            return;
        }

        int chipCount = _chips.Length;
        while (_activeRollIndex < 0)
        {
            int hitIndex = -1;
            // 完全AUTO以外は、わずかに早い打鍵もできるよう判定許容範囲内の先読みを行う。
            double lookAheadSec = perfectAuto ? 0.0 : 0.095;
            for (int i = _firstActiveIndex; i < chipCount; i++)
            {
                var chip = _chips[i];
                if (!IsNote(chip) || _isHit[i] || chip._noteType == NoteType.RollEnd) continue;
                if (TJA.ChipSec(chip) > nowSec + lookAheadSec) break;
                hitIndex = i;
                break;
            }

            if (hitIndex < 0) break;

            var hitChip = _chips[hitIndex];
            double chipSec = TJA.ChipSec(hitChip);
            double inputSec = chipSec;
            if (!perfectAuto)
            {
                if (_aiAttempted[hitIndex]) break;

                // ノーツごとに一度だけ、打つ時刻を決める。打ち漏らしは発生させない。
                if (double.IsNaN(_aiTargetHitSec[hitIndex]))
                {
                    // 低レベルほど早叩き／遅叩きが大きくなるが、必ず判定範囲内に収める。
                    double maxTimingError = 0.014 + 0.070 * (1.0 - skill);
                    _aiTargetHitSec[hitIndex] = chipSec
                        + (Random.Shared.NextDouble() * 2.0 - 1.0) * maxTimingError;
                }

                inputSec = _aiTargetHitSec[hitIndex];
                if (nowSec < inputSec) break; // 予定時刻まで自然に待つ。
                _aiAttempted[hitIndex] = true;
            }

            bool isKa = hitChip._noteType == NoteType.Ka || hitChip._noteType == NoteType.KA;

            Enso.PlayP2DrumSound(!isKa);
            if (isKa) MiniTaiko.TriggerP2Kat(false); else MiniTaiko.TriggerP2Don(false);
            JudgeNote(isKa ? 1 : 0, inputSec);
        }
    }

    private static void JudgeNote(int inputType, double inputSec)
    {
        double nowSec = inputSec;
        int oldestIdx = -1;

        int chipCount = _chips.Length;
        for (int i = _firstActiveIndex; i < chipCount; i++)
        {
            var chip = _chips[i];
            if (!IsNote(chip) || _isHit[i]) continue;
            if (chip._noteType == NoteType.RollEnd) continue;

            double chipSec = TJA.ChipSec(chip);

            if (chip._noteType == NoteType.BalloonStart && chip._rollEnd != null)
            {
                if (nowSec > TJA.ChipSec(chip._rollEnd)) continue;
                if (chipSec - nowSec > WIN_BAD) break;
            }
            else
            {
                if (nowSec - chipSec > WIN_BAD) continue;
                if (chipSec - nowSec > WIN_BAD) break;
            }

            bool chipIsDon = chip._noteType == NoteType.Don || chip._noteType == NoteType.DON;
            bool chipIsKa = chip._noteType == NoteType.Ka || chip._noteType == NoteType.KA;
            bool chipIsRoll = chip._noteType == NoteType.RollStart
                            || chip._noteType == NoteType.RollBigStart
                            || chip._noteType == NoteType.BalloonStart;

            bool matches;
            if (chipIsRoll)
                matches = chip._noteType != NoteType.BalloonStart || inputType == 0;
            else if (inputType == 0)
                matches = chipIsDon;
            else
                matches = chipIsKa;

            if (!matches) continue;

            oldestIdx = i;
            break;
        }

        if (oldestIdx < 0) return;

        var oldest = _chips[oldestIdx];
        bool isKaNote = oldest._noteType == NoteType.Ka || oldest._noteType == NoteType.KA;
        bool isRoll = oldest._noteType == NoteType.RollStart
                   || oldest._noteType == NoteType.RollBigStart
                   || oldest._noteType == NoteType.BalloonStart;

        _isHit[oldestIdx] = true;

        if (isRoll)
        {
            _activeRollIndex = oldestIdx;
            _rollNextHitSec = nowSec + ROLL_HIT_INTERVAL;
            _rollHits = 1;
            if (oldest._noteType == NoteType.BalloonStart)
            {
                _p2BalloonHits = 0;
                RendaPop.ForceHideP2();
            }
            else
            {
                RendaPop.ShowP2(nowSec);
            }
            _score += 100;
            bool isBigRoll = oldest._noteType == NoteType.RollBigStart;
            EnsoEffect.AddHitEffect(isBigRoll ? EnsoEffectType.GoodBig : EnsoEffectType.Good,
                EnsoJudgeType.None, nowSec, HitX, LANE_Y, isRoll: true);
            if (oldest._noteType != NoteType.BalloonStart)
                FlyingNotes.SpawnP2(false, isBigRoll, nowSec);
            return;
        }

        bool isBigNote = oldest._noteType == NoteType.DON || oldest._noteType == NoteType.KA;
        double oldestSec = TJA.ChipSec(oldest);
        double diff = Math.Abs(oldestSec - nowSec);

        if (diff <= WIN_PERFECT)
        {
            _perfect++;
            _score += _scorePerNote;
            EnsoEffect.AddHitEffect(isBigNote ? EnsoEffectType.GoodBig : EnsoEffectType.Good,
                EnsoJudgeType.Good, nowSec, HitX, LANE_Y);
            Gauge.AddGoodP2(nowSec);
            FlyingNotes.SpawnP2(isKaNote, isBigNote, nowSec);
            DonChanEnso.OnP2ComboRecovered();
            _combo++;
            if (_combo > _maxCombo) _maxCombo = _combo;
            DonChanEnso.OnP2ComboMilestone(_combo, Gauge.IsP2Clear);
            ComboPop.CheckMilestoneP2(_combo, nowSec);
        }
        else if (diff <= WIN_GOOD)
        {
            _good++;
            _score += (int)(Math.Ceiling((_scorePerNote / 2.0) / 10.0) * 10.0);
            EnsoEffect.AddHitEffect(isBigNote ? EnsoEffectType.OkBig : EnsoEffectType.Ok,
                EnsoJudgeType.Ok, nowSec, HitX, LANE_Y);
            Gauge.AddOkP2(nowSec);
            FlyingNotes.SpawnP2(isKaNote, isBigNote, nowSec);
            DonChanEnso.OnP2ComboRecovered();
            _combo++;
            if (_combo > _maxCombo) _maxCombo = _combo;
            DonChanEnso.OnP2ComboMilestone(_combo, Gauge.IsP2Clear);
            ComboPop.CheckMilestoneP2(_combo, nowSec);
        }
        else
        {
            _bad++;
            Gauge.AddBadP2();
            DonChanEnso.OnP2ComboBreak();
            EnsoEffect.AddHitEffect(EnsoEffectType.None, EnsoJudgeType.Bad, nowSec, HitX, LANE_Y);
            _combo = 0;
        }
    }

    private static void CheckRollEnd(double nowSec)
    {
        if (_activeRollIndex < 0) return;
        var chip = _chips[_activeRollIndex];
        if (chip._rollEnd == null) { _activeRollIndex = -1; return; }

        double endSec = TJA.ChipSec(chip._rollEnd);
        if (nowSec >= endSec)
        {
            if (chip._noteType == NoteType.BalloonStart)
            {
                int target = Math.Max(1, chip._playerBalloonCount);
                if (_p2BalloonHits < target) DonChanEnso.OnP2BalloonMiss();
            }
            else
            {
                RendaPop.HideP2(nowSec);
            }
            _activeRollIndex = -1;
        }
    }

    private static void CountP2BalloonHit(double nowSec)
    {
        if (_activeRollIndex < 0 || _activeRollIndex >= _chips.Length) return;
        var chip = _chips[_activeRollIndex];
        if (chip._noteType != NoteType.BalloonStart) return;

        _p2BalloonHits++;
        int target = Math.Max(1, chip._playerBalloonCount);
        if (_p2BalloonHits >= target)
        {
            DonChanEnso.OnP2BalloonBroke();
            _p2BalloonPopSec = nowSec;
            FlyingNotes.SpawnP2(false, true, nowSec, isBalloon: true);
            _activeRollIndex = -1;
        }
    }

    private static void CheckMiss(double nowSec)
    {
        int chipCount = _chips.Length;
        for (int i = _firstActiveIndex; i < chipCount; i++)
        {
            var chip = _chips[i];
            if (!IsNote(chip) || _isHit[i]) continue;
            if (chip._noteType == NoteType.RollEnd) continue;
            if (chip._noteType == NoteType.BalloonStart) continue;

            bool isRollType = chip._noteType == NoteType.RollStart
                           || chip._noteType == NoteType.RollBigStart
                           || chip._noteType == NoteType.Kusudama;

            double chipSec = TJA.ChipSec(chip);
            if (nowSec - chipSec > WIN_BAD)
            {
                if (isRollType)
                {
                    _isHit[i] = true;
                    continue;
                }
                _isHit[i] = true;
                _isFailed[i] = true;
                _miss++;
                Gauge.AddMissP2();
                DonChanEnso.OnP2ComboBreak();
                _combo = 0;
            }
        }

        AdvanceFirstActiveIndex();
    }

    private static void AdvanceFirstActiveIndex()
    {
        int n = _chips.Length;
        while (_firstActiveIndex < n)
        {
            var c = _chips[_firstActiveIndex];
            if (IsNote(c) && !_isHit[_firstActiveIndex]) break;
            _firstActiveIndex++;
        }
    }

    private static bool IsNote(Chip chip) =>
        chip._noteType != NoteType.None && chip._noteType != NoteType.Measure;

    private static float NoteX(double noteSec, double nowSec, double scroll, double bpm, float s, float vx)
    {
        double delta = noteSec - nowSec;

        int bpmFix = DiffSelectScene.BpmFixValue;
        if (bpmFix > 0)
        {
            bpm = bpmFix;
            scroll = 1.0;
        }

        double speed = (LANE_RIGHT - HitX) * bpm / 240.0;
        speed *= DiffSelectScene.SpeedMultiplier;
        return (float)(HitX * s + vx + delta * speed * scroll * s);
    }

    public static void Draw(float vx, float vy, float s, double nowSec)
    {
        DrawLane(vx, vy, s);
        DrawNotes(vx, vy, s, nowSec);
    }

    public static void DrawLane(float vx, float vy, float s)
    {
        if (_laneAssetsLoaded && _laneTex.Id != 0)
        {
            float lx = LANE_X * s + vx;
            float ly = LANE_TOP * s + vy;
            var srcR = new Rectangle(0, 0, _laneTex.Width, _laneTex.Height);
            var dstR = new Rectangle(lx, ly, _laneTex.Width * s, _laneTex.Height * s);
            Raylib.DrawTexturePro(_laneTex, srcR, dstR, Vector2.Zero, 0f, Color.White);
        }

        float hx = HitX * s + vx;
        float hy = LANE_Y * s + vy;
        if (_laneAssetsLoaded && _hitTex.Id != 0)
        {
            float w = _hitTex.Width * s;
            float h = _hitTex.Height * s;
            var srcR = new Rectangle(0, 0, _hitTex.Width, _hitTex.Height);
            var dstR = new Rectangle(hx, hy, w, h);
            var origin = new Vector2(w / 2f, h / 2f);
            Raylib.DrawTexturePro(_hitTex, srcR, dstR, origin, 0f, Color.White);
        }
    }

    public static void DrawNotes(float vx, float vy, float s, double nowSec)
    {
        int chipCount = _chips.Length;
        int startIndex = Math.Max(0, _firstActiveIndex - 10);
        float laneY = LANE_Y * s + vy;
        float seY = (LANE_Y + SE_NOTE_Y_OFFSET) * s + vy;
        float rightEdge = vx + LANE_RIGHT * s;
        bool doron = DiffSelectScene.DoronEnabled;

        // 連打／大連打／風船は、ヘッド・胴体・終端とSEノート帯をまとめて描く。
        // _firstActiveIndexより前の長尺ノーツも、終端が画面内に残る間は描画対象にする。
        var rollDrawList = new System.Collections.Generic.List<(float NoteX, float EndX, int Index, int Layer)>();
        for (int i = 0; i < chipCount; i++)
        {
            var chip = _chips[i];
            bool isRollType = chip._noteType == NoteType.RollStart
                           || chip._noteType == NoteType.RollBigStart
                           || chip._noteType == NoteType.BalloonStart;
            if (!isRollType || chip._rollEnd == null) continue;

            double headSec = TJA.ChipSec(chip);
            float noteX = chip._noteType == NoteType.BalloonStart
                ? BalloonNoteX(chip, nowSec, s, vx)
                : NoteX(headSec, nowSec, chip._scroll.Real, chip._bpm, s, vx);
            float endX = NoteX(TJA.ChipSec(chip._rollEnd), nowSec, chip._scroll.Real, chip._bpm, s, vx);
            if (endX < vx - 200f * s || noteX > rightEdge + 200f * s) continue;
            rollDrawList.Add((noteX, endX, i, chip._layer));
        }
        rollDrawList.Sort((a, b) => b.NoteX.CompareTo(a.NoteX));

        foreach (int layer in _noteLayers)
            foreach (var (noteX, endX, index, chipLayer) in rollDrawList)
            {
                if (chipLayer != layer) continue;
                var chip = _chips[index];

                if (chip._noteType == NoteType.BalloonStart)
                {
                    // 開始ヘッドは判定済みでも、アクティブな風船中は本体を残して表示する。
                    if (_isHit[index] && index != _activeRollIndex) continue;
                    if (!doron && noteX >= vx - 400f * s && noteX <= rightEdge + 200f * s)
                        NotesTexture.DrawBalloonBody(new Vector2(noteX, laneY), s, _combo);
                    continue;
                }

                bool isBig = chip._noteType == NoteType.RollBigStart;
                float bodyStartX = Math.Max(noteX, vx);
                float bodyWidth = Math.Max(0f, endX - bodyStartX);
                if (!doron)
                {
                    NotesTexture.DrawDrumrollBody(isBig, bodyStartX, bodyWidth, laneY, s);
                    NotesTexture.DrawTail(isBig ? 6 : 5, new Vector2(endX, laneY), s);
                }

                // SEノートはドロン時にも表示し、開始から終端までの帯を1Pと同じように接続する。
                NotesTexture.DrawSeRollBody(isBig, noteX, endX, seY, s);
                NotesTexture.DrawSeNote(chip._rollEnd._seNoteType, isBig, new Vector2(endX, seY), s);
            }

        // ヘッドは連打系を先に、単音を後に描画する。各グループはレイヤーとX降順で揃える。
        var headDrawList = new System.Collections.Generic.List<(float X, int Index, int Layer)>();
        for (int i = startIndex; i < chipCount; i++)
        {
            var chip = _chips[i];
            if (!IsNote(chip) || chip._noteType == NoteType.RollEnd) continue;
            bool isRollHead = chip._noteType == NoteType.RollStart
                           || chip._noteType == NoteType.RollBigStart
                           || chip._noteType == NoteType.BalloonStart
                           || chip._noteType == NoteType.Kusudama;
            if (_isHit[i] && !_isFailed[i] && !isRollHead) continue;
            if (chip._noteType == NoteType.BalloonStart && _isHit[i] && i != _activeRollIndex) continue;

            double chipSec = TJA.ChipSec(chip);
            if (chipSec - nowSec > NOTE_LOOKAHEAD_SEC) break;
            float x = chip._noteType == NoteType.BalloonStart
                ? BalloonNoteX(chip, nowSec, s, vx)
                : NoteX(chipSec, nowSec, chip._scroll.Real, chip._bpm, s, vx);
            if (x < vx - 200f * s || x > rightEdge + 200f * s) continue;

            headDrawList.Add((x, i, chip._layer));
        }
        // OpenTaikoと同じく、チャート順を逆から描画して重なり順を統一する。
        headDrawList.Sort((a, b) => b.Index.CompareTo(a.Index));

        foreach (var (x, index, chipLayer) in headDrawList)
        {
            var chip = _chips[index];
            if (!doron) DrawNote(chip._noteType, x, laneY, s);
            bool isRollHead = chip._noteType == NoteType.RollStart
                           || chip._noteType == NoteType.RollBigStart
                           || chip._noteType == NoteType.BalloonStart
                           || chip._noteType == NoteType.Kusudama;
            NotesTexture.DrawSeNote(chip._seNoteType, isRollHead && chip._noteType == NoteType.RollBigStart,
                new Vector2(x, seY), s);
        }
    }

    /// <summary>2P風船の本体・残数・成功フレームを下段へ表示する。</summary>
    public static void DrawBalloonUI(float vx, float vy, float s, double nowSec)
    {
        const float BubbleX = 601f, BubbleY = 310f;
        const float NumberX = 735f, NumberY = 366f;
        const float BigX = 650f, BigY = 643f;
        const float NumberHeight = 86f;

        if (_activeRollIndex >= 0 && _activeRollIndex < _chips.Length)
        {
            var chip = _chips[_activeRollIndex];
            if (chip._noteType == NoteType.BalloonStart)
            {
                int required = Math.Max(1, chip._playerBalloonCount);
                int remain = Math.Max(0, required - _p2BalloonHits);
                int frame = Math.Min(6, _p2BalloonHits * 7 / required);
                float bigX = (long)(nowSec * 1000.0 / 50.0) % 2 == 0 ? BigX : BigX + 4f;
                NotesTexture.DrawBalloonBubble(new Vector2(BubbleX * s + vx, BubbleY * s + vy), s);
                NotesTexture.DrawBalloonNumber(remain, NumberX * s + vx, NumberY * s + vy, s, NumberHeight);
                NotesTexture.DrawBalloonBig(frame, new Vector2(bigX * s + vx, BigY * s + vy), s);
                return;
            }
        }

        if (_p2BalloonPopSec >= 0)
        {
            double elapsedMs = (nowSec - _p2BalloonPopSec) * 1000.0;
            if (elapsedMs < 110.0)
            {
                byte alpha = (byte)(255.0 * (1.0 - elapsedMs / 110.0));
                NotesTexture.DrawBalloonBig(7, new Vector2(BigX * s + vx, BigY * s + vy), s, alpha);
            }
            else _p2BalloonPopSec = -1.0;
        }
    }

    private static float BalloonNoteX(Chip chip, double nowSec, float s, float vx)
    {
        double headSec = TJA.ChipSec(chip);
        double endSec = chip._rollEnd != null ? Math.Max(headSec, TJA.ChipSec(chip._rollEnd)) : headSec;
        float stopX = (HitX + BALLOON_STOP_OFFSET) * s + vx;
        if (nowSec <= endSec)
            return Math.Max(NoteX(headSec, nowSec, chip._scroll.Real, chip._bpm, s, vx), stopX);
        return NoteX(endSec, nowSec, chip._scroll.Real, chip._bpm, s, vx) + BALLOON_STOP_OFFSET * s;
    }

    private static void DrawNote(NoteType type, float x, float y, float s)
    {
        Vector2 pos = new Vector2(x, y);
        switch (type)
        {
            case NoteType.Don: NotesTexture.DrawNote(1, pos, s, _combo); break;
            case NoteType.Ka: NotesTexture.DrawNote(2, pos, s, _combo); break;
            case NoteType.DON: NotesTexture.DrawNote(3, pos, s, _combo); break;
            case NoteType.KA: NotesTexture.DrawNote(4, pos, s, _combo); break;
            case NoteType.RollStart: NotesTexture.DrawNote(5, pos, s, _combo); break;
            case NoteType.RollBigStart: NotesTexture.DrawNote(6, pos, s, _combo); break;
            case NoteType.BalloonStart: NotesTexture.DrawNote(7, pos, s, _combo); break;
            case NoteType.Kusudama: NotesTexture.DrawNote(8, pos, s, _combo); break;
        }
    }

}
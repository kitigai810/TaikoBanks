using System.Numerics;

namespace TaikoNauts.Core.Taiko.Charts;

public sealed class Chip
{
    public double _time;
    public double _timeForHBScroll;
    public bool _isHBScroll;
    public NoteType _noteType;
    public SeNoteType _seNoteType;
    public Branch _branch;
    public RollType _rollType;
    public double _bpm;
    public Complex _scroll = Complex.One;
    public double _measure;
    public int _rollColor;
    public bool _isHit;
    public bool _isFailed;
    public bool _isPassing;
    public int _branchIndex;
    public bool _isGoGo;
    public bool _isGoGoStart;
    public bool _isGoGoEnd;
    public bool _isBarVisible;
    public bool _isInvisible;
    public int _baseBalloonCount;
    public int _playerRollCount;
    public int _playerBalloonCount;
    public Chip? _rollEnd;
    public double _suddenShowTime;
    public double _suddenMoveTime;
    public int _layer;
}

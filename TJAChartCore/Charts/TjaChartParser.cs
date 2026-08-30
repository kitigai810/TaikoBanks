#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using TaikoNauts.Core.Taiko.Charts.Models.Commands;
using TaikoNauts.Core.Taiko.Helper;

namespace TaikoNauts.Core.Taiko.Charts
{
    public sealed class TjaChartReader
    {
        public enum LoadType
        {
            Normal,
            HeaderOnly
        }

        public Song GetSongDataFromTja(string path, LoadType type, Song basesong = null)
        {
            this._commonBalloon = null;
            this._nowCourse = 0;
            this._isParsing = false;

            Song song = basesong ?? new Song();
            song._header = song._header ?? new SongHeader();
            song._songCourses = new SongCourse[5];
            song._header._path = path;

            if (!Path.Exists(path))
                return null;

            string data = FileReader.ReadTextAuto(path);

            string[] delimiter = { "\n" };
            var dataLines = data.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            song._header._sevol = 1.0;
            song._header._songvol = 1.0;

            foreach (string rawline in dataLines)
            {
                var line = rawline.Trim();

                if (!_isParsing)
                {
                    string[] lineSplit = line.Split(new[] { ':' }, 2);
                    lineSplit[0] = lineSplit[0].Trim();
                    string parameter = lineSplit.Length >= 2 ? lineSplit[1] : "";

                    var index = parameter.IndexOf("//");

                    if (index >= 0)
                        parameter = parameter.Substring(0, index).Trim();

                    #region [ Parsing Header ]

                    if (lineSplit[0] == "TITLE")
                    {
                        song._header._title = parameter;
                    }
                    else if (lineSplit[0] == "TITLEJA")
                    {
                        song._header._title = parameter;
                    }
                    else if (lineSplit[0] == "SUBTITLE")
                    {
                        if (parameter.StartsWith("--") || parameter.StartsWith("++"))
                            parameter = parameter.Remove(0, 2);

                        song._header._subTitle = parameter;
                    }
                    else if (lineSplit[0] == "SUBTITLEJA")
                    {
                        if (parameter.StartsWith("--") || parameter.StartsWith("++"))
                            parameter = parameter.Remove(0, 2);

                        song._header._subTitle = parameter;
                    }
                    else if (lineSplit[0] == "BPM")
                    {
                        song._header._bpm = ParseParameter(parameter, 60);
                    }
                    else if (lineSplit[0] == "WAVE")
                    {
                        song._header._wave = parameter;
                    }
                    else if (lineSplit[0] == "DEMOSTART")
                    {
                        song._header._demoStart = ParseParameter(parameter, 0);
                    }
                    else if (lineSplit[0] == "OFFSET")
                    {
                        song._header._offset = ParseParameter(parameter, 0);
                    }
                    else if (lineSplit[0] == "GENRE")
                    {
                        song._header._genre = parameter;
                    }
                    else if (lineSplit[0] == "SONGVOL")
                    {
                        song._header._songvol = ParseParameter(parameter, 100) / 100.0;
                    }
                    else if (lineSplit[0] == "SEVOL")
                    {
                        song._header._sevol = ParseParameter(parameter, 100) / 100.0;
                    }
                    else if (lineSplit[0] == "SONGGENREID")
                    {
                        song._header._songGenreID = parameter;
                    }
                    else if (lineSplit[0] == "BGMOVIE")
                    {
                        song._header._moviePath = parameter;
                    }
                    else if (lineSplit[0] == "MOVIEOFFSET")
                    {
                        song._header._movieOffset = ParseParameter(parameter, 0);
                    }
                    else if (lineSplit[0] == "BGIMAGE")
                    {
                        song._header._backgroundImagePath = parameter;
                    }
                    else if (lineSplit[0] == "LYRICS")
                    {
                        song._header._lyricsPath = parameter;
                    }
                    else if (lineSplit[0] == "MAKER")
                    {
                        song._header._maker = parameter;
                    }
                    else if (lineSplit[0] == "COURSE")
                    {
                        this._nowHBScroll = false;
                        int course = GetCourseFromData(parameter);
                        if (song._songCourses[course] == null)
                        {
                            song._songCourses[course] = new SongCourse();
                            song._songCourses[course]._balloon = _commonBalloon ?? new List<int>();
                            song._songCourses[course]._chips = new List<Chip>();
                            song._songCourses[course]._commands = new List<Command>();
                            song._songCourses[course]._branchCommands = new List<BranchCommand>();
                        }
                        this._nowCourse = course;
                    }
                    else if (lineSplit[0] == "LEVEL")
                    {
                        if (song._songCourses[_nowCourse] == null)
                        {
                            _nowCourse = 3;
                            song._songCourses[_nowCourse] = new SongCourse();
                            song._songCourses[_nowCourse]._balloon = new List<int>();
                            song._songCourses[_nowCourse]._chips = new List<Chip>();
                            song._songCourses[_nowCourse]._commands = new List<Command>();
                            song._songCourses[_nowCourse]._branchCommands = new List<BranchCommand>();
                        }
                        song._songCourses[_nowCourse]._level = (int)ParseParameter(parameter, 1);
                    }
                    else if (lineSplit[0] == "BALLOON")
                    {
                        if (!string.IsNullOrEmpty(parameter))
                        {
                            if (song._songCourses[_nowCourse] != null)
                            {
                                song._songCourses[_nowCourse]._balloon = new List<int>();
                            }
                            else
                            {
                                _commonBalloon = new List<int>();
                            }

                            string[] balloonCounts = parameter.Split(',');

                            foreach (var count in balloonCounts)
                            {
                                if (song._songCourses[_nowCourse] != null)
                                {
                                    song._songCourses[_nowCourse]._balloon.Add((int)ParseParameter(count, 5));
                                }
                                else
                                {
                                    _commonBalloon.Add((int)ParseParameter(count, 5));
                                }
                            }
                        }
                    }
                    else if (lineSplit[0] == "BALLOONMAS")
                    {
                        song._songCourses[_nowCourse]._balloonMaster = new List<int>();
                        if (!string.IsNullOrEmpty(parameter))
                        {
                            string[] balloonCounts = parameter.Split(',');

                            foreach (var count in balloonCounts)
                            {
                                song._songCourses[_nowCourse]._balloonMaster.Add((int)ParseParameter(count, 5));
                            }
                        }
                    }
                    else if (lineSplit[0] == "BALLOONEXP")
                    {
                        song._songCourses[_nowCourse]._balloonExpert = new List<int>();
                        if (!string.IsNullOrEmpty(parameter))
                        {
                            string[] balloonCounts = parameter.Split(',');

                            foreach (var count in balloonCounts)
                            {
                                song._songCourses[_nowCourse]._balloonExpert.Add((int)ParseParameter(count, 5));
                            }
                        }
                    }
                    else if (lineSplit[0] == "BALLOONNOR")
                    {
                        song._songCourses[_nowCourse]._balloonNormal = new List<int>();
                        if (!string.IsNullOrEmpty(parameter))
                        {
                            string[] balloonCounts = parameter.Split(',');

                            foreach (var count in balloonCounts)
                            {
                                song._songCourses[_nowCourse]._balloonNormal.Add((int)ParseParameter(count, 5));
                            }
                        }
                    }
                    else if (lineSplit[0] == "#HBSCROLL")
                    {
                        if (_nowMeasureCommands == null)
                        {
                            _nowMeasureCommands = new List<Command>();
                        }

                        _nowMeasureCommands.Add(
                            new Command()
                            {
                                _cmdType = CommandType.HBScroll,
                                _branch = _nowProcessingBranch,
                                _time = 0,
                                _notePosition = 0
                            });

                        _nowHBScroll = true;
                    }
                    else if (lineSplit[0] == "#START")
                    {
                        if (song._songCourses[_nowCourse] == null)
                        {
                            song._songCourses[_nowCourse] = new SongCourse();
                            song._songCourses[_nowCourse]._chips = new List<Chip>();
                            song._songCourses[_nowCourse]._commands = new List<Command>();
                            song._songCourses[_nowCourse]._branchCommands = new List<BranchCommand>();
                            song._songCourses[_nowCourse]._balloon = _commonBalloon ?? new List<int>();
                            song._songCourses[_nowCourse]._balloonNormal = new List<int>();
                            song._songCourses[_nowCourse]._balloonExpert = new List<int>();
                            song._songCourses[_nowCourse]._balloonMaster = new List<int>();
                        }

                        this._isParsing = true;
                        this._nowTime = 0;
                        this._nowTimeForHBScroll = 0;
                        this._nowBpm = song._header._bpm;
                        this._nowScroll = 1.0;
                        this._nowMeasure = 1.0;
                        this._nowBarVisible = true;
                        this._nowBalloonCount = 0;
                        this._nowGoGo = false;
                        this._measureFirstNote = new Chip();
                        this._nowBranch = Branch.None;
                        this._nowMeasureChips = new List<Chip>();
                        this._isNowSearchingRollEnd = false;
                        if (_nowMeasureCommands == null)
                        {
                            this._nowMeasureCommands = new List<Command>();
                        }
                        this._nowBranchBalloonCount = new int[3];
                        this._suddenShowTime = 0;
                        this._suddenMoveTime = 0;
                        this._nowSeNote = SeNoteType.None;
                        this._nowLayer = 0;

                        #region [ 小節線の最初のノーツの設定 ]

                        _measureFirstNote._time = _nowTime;
                        _measureFirstNote._bpm = _nowBpm;
                        _measureFirstNote._scroll = _nowScroll;
                        _measureFirstNote._measure = _nowMeasure;
                        _measureFirstNote._branch = _nowBranch;
                        _measureFirstNote._isGoGo = _nowGoGo;
                        _measureFirstNote._isBarVisible = _nowBarVisible;

                        #endregion

                        #region [ 小節線関連 ]

                        _nowBranchCount = -1;
                        _branchChangeTime = 0;
                        _nowBranchStartTime = 0;
                        _isNowProcessingBranch = false;
                        _nowProcessingBranch = Branch.Normal;
                        song._songCourses[_nowCourse]._chipsNormal = new List<Chip>();
                        song._songCourses[_nowCourse]._chipsExpert = new List<Chip>();
                        song._songCourses[_nowCourse]._chipsMaster = new List<Chip>();
                        if (song._songCourses[_nowCourse]._balloonNormal == null || song._songCourses[_nowCourse]._balloonNormal.Count == 0)
                        {
                            _isUsingCommonBallonValue = true;
                        }
                        if (song._songCourses[_nowCourse]._balloonExpert == null || song._songCourses[_nowCourse]._balloonExpert.Count == 0)
                        {
                            _isUsingCommonBallonValue = true;
                        }
                        if (song._songCourses[_nowCourse]._balloonMaster == null || song._songCourses[_nowCourse]._balloonMaster.Count == 0)
                        {
                            _isUsingCommonBallonValue = true;
                        }

                        #endregion
                    }

                    #endregion
                }
                else
                {
                    if (type == LoadType.HeaderOnly)
                    {
                        if (line.StartsWith("#END", StringComparison.Ordinal))
                        {
                            _isParsing = false;
                        }
                        else if (line.StartsWith("#BRANCHSTART", StringComparison.Ordinal))
                        {
                            song._songCourses[_nowCourse]._hasBranch = true;
                        }
                    }
                    else
                    {
                        //コマンド行以外(=ノーツ)を先に処理
                        if (!string.IsNullOrEmpty(line) && line[0] != '#')
                        {
                            string noteLine = line.Split(new String[] { "//" }, StringSplitOptions.None)[0];

                            for (int i = 0; i < noteLine.Length; i++)
                            {
                                if (!((noteLine[i] >= '0' && noteLine[i] <= '9') || noteLine[i] == ','))
                                    continue;

                                if (noteLine[i] == ',')
                                {
                                    ProcessMeasure(song);
                                }
                                else
                                {
                                    Chip chip = new Chip();
                                    chip._noteType = (NoteType)(int.Parse(noteLine[i].ToString()));
                                    if (chip._noteType == NoteType.Kusudama)
                                        chip._noteType = NoteType.BalloonStart;
                                    chip._time = _nowTime;
                                    chip._timeForHBScroll = _nowTime;
                                    chip._bpm = _nowBpm;
                                    chip._branch = _nowBranch;
                                    chip._branchIndex = _nowBranchCount;
                                    chip._measure = _nowMeasure;
                                    chip._scroll = _nowScroll;
                                    chip._isGoGo = _nowGoGo;
                                    chip._isHit = false;
                                    chip._isPassing = false;
                                    chip._rollType = _nowRollType;
                                    chip._isBarVisible = _nowBarVisible;
                                    chip._suddenShowTime = _suddenShowTime;
                                    chip._suddenMoveTime = _suddenMoveTime;
                                    chip._isHBScroll = _nowHBScroll;
                                    chip._layer = _nowLayer;

                                    if (noteLine[i] != '0' && _nowSeNote != SeNoteType.None)
                                    {
                                        chip._seNoteType = _nowSeNote;
                                        _nowSeNote = SeNoteType.None;
                                    }

                                    #region [ 各種特殊ノーツ ]

                                    if ((chip._noteType == NoteType.BalloonStart || chip._noteType == NoteType.RollStart || chip._noteType == NoteType.RollBigStart || chip._noteType == NoteType.Kusudama) && _nowRollType != RollType.None)
                                    {
                                        //重複しているので無効化
                                        chip._noteType = NoteType.None;
                                    }

                                    if (chip._noteType == NoteType.RollStart)
                                    {
                                        _nowRollType = RollType.Normal;
                                    }
                                    else if (chip._noteType == NoteType.RollBigStart)
                                    {
                                        _nowRollType = RollType.Big;
                                    }
                                    else if (chip._noteType == NoteType.BalloonStart)
                                    {
                                        _nowRollType = RollType.Balloon;

                                        #region [ 風船の処理 ]

                                        if (_isUsingCommonBallonValue)
                                        {
                                            //配列が足りていなかったらとりあえず5打くらい加えとく
                                            if (song._songCourses[_nowCourse]._balloon.Count <= _nowBalloonCount)
                                            {
                                                chip._baseBalloonCount = 5;
                                                chip._playerBalloonCount = 5;
                                            }
                                            else
                                            {
                                                chip._baseBalloonCount = song._songCourses[_nowCourse]._balloon[_nowBalloonCount];
                                                chip._playerBalloonCount = song._songCourses[_nowCourse]._balloon[_nowBalloonCount];
                                                _nowBalloonCount++;
                                            }
                                        }
                                        else if (song._songCourses[_nowCourse]._hasBranch)
                                        {
                                            switch (_nowProcessingBranch)
                                            {
                                                case Branch.Normal:
                                                    chip._baseBalloonCount = song._songCourses[_nowCourse]._balloonNormal.Count > _nowBranchBalloonCount[0] ? song._songCourses[_nowCourse]._balloonNormal[_nowBranchBalloonCount[0]] : 5;
                                                    chip._playerBalloonCount = song._songCourses[_nowCourse]._balloonNormal.Count > _nowBranchBalloonCount[0] ? song._songCourses[_nowCourse]._balloonNormal[_nowBranchBalloonCount[0]] : 5;
                                                    _nowBranchBalloonCount[0]++;
                                                    break;
                                                case Branch.Expert:
                                                    chip._baseBalloonCount = song._songCourses[_nowCourse]._balloonExpert.Count > _nowBranchBalloonCount[1] ? song._songCourses[_nowCourse]._balloonExpert[_nowBranchBalloonCount[1]] : 5;
                                                    chip._playerBalloonCount = song._songCourses[_nowCourse]._balloonExpert.Count > _nowBranchBalloonCount[1] ? song._songCourses[_nowCourse]._balloonExpert[_nowBranchBalloonCount[1]] : 5;
                                                    _nowBranchBalloonCount[1]++;
                                                    break;
                                                case Branch.Master:
                                                    chip._baseBalloonCount = song._songCourses[_nowCourse]._balloonMaster.Count > _nowBranchBalloonCount[2] ? song._songCourses[_nowCourse]._balloonMaster[_nowBranchBalloonCount[2]] : 5;
                                                    chip._playerBalloonCount = song._songCourses[_nowCourse]._balloonMaster.Count > _nowBranchBalloonCount[2] ? song._songCourses[_nowCourse]._balloonMaster[_nowBranchBalloonCount[2]] : 5;
                                                    _nowBranchBalloonCount[2]++;
                                                    break;
                                            }
                                        }
                                        else
                                        {
                                            //配列が足りていなかったらとりあえず5打くらい加えとく
                                            if (song._songCourses[_nowCourse]._balloon.Count <= _nowBalloonCount)
                                            {
                                                chip._baseBalloonCount = 5;
                                                chip._playerBalloonCount = 5;
                                            }
                                            else
                                            {
                                                chip._baseBalloonCount = song._songCourses[_nowCourse]._balloon[_nowBalloonCount];
                                                chip._playerBalloonCount = song._songCourses[_nowCourse]._balloon[_nowBalloonCount];
                                                _nowBalloonCount++;
                                            }
                                        }

                                        #endregion
                                    }
                                    else if (chip._noteType == NoteType.RollEnd)
                                    {
                                        _nowRollType = RollType.None;
                                    }

                                    #endregion

                                    this._nowMeasureChips.Add(chip);

                                    if (this._nowMeasureChips.Count == 1)
                                    {
                                        _measureFirstNote._time = _nowTime;
                                        _measureFirstNote._bpm = _nowBpm;
                                        _measureFirstNote._scroll = _nowScroll;
                                        _measureFirstNote._measure = _nowMeasure;
                                        _measureFirstNote._branch = _nowBranch;
                                        _measureFirstNote._isGoGo = _nowGoGo;
                                        _measureFirstNote._isBarVisible = _nowBarVisible;
                                    }
                                }
                            }
                        }
                        else
                        {
                            string[] lineSplit = line.Trim().Split(' ');
                            string parameter = lineSplit.Length >= 2 ? lineSplit[lineSplit.Length - 1] : "";

                            if (lineSplit[0] == "#END")
                            {
                                if (this._nowMeasureChips.Count > 0)
                                {
                                    ProcessMeasure(song);
                                }

                                #region [ chipの掃除 ]

                                song._songCourses[_nowCourse]._chips?.RemoveAll(x => x._noteType == NoteType.None);

                                if (song._songCourses[_nowCourse]._hasBranch)
                                {
                                    song._songCourses[_nowCourse]._chipsNormal?.RemoveAll(x => x._noteType == NoteType.None);
                                    song._songCourses[_nowCourse]._chipsExpert?.RemoveAll(x => x._noteType == NoteType.None);
                                    song._songCourses[_nowCourse]._chipsMaster?.RemoveAll(x => x._noteType == NoteType.None);
                                }

                                #endregion

                                _isParsing = false;
                            }
                            else if (line.StartsWith("#GOGOSTART", StringComparison.CurrentCulture))
                            {
                                _nowMeasureCommands.Add(
                                    new Command()
                                    {
                                        _cmdType = CommandType.GogoStart,
                                        _branch = _nowProcessingBranch,
                                        _time = _nowTime,
                                        _notePosition = _nowMeasureChips.Count
                                    });

                                _nowGoGo = true;
                            }
                            else if (line.StartsWith("#GOGOEND", StringComparison.CurrentCulture))
                            {
                                _nowMeasureCommands.Add(
                                    new Command()
                                    {
                                        _cmdType = CommandType.GogoEnd,
                                        _branch = _nowProcessingBranch,
                                        _time = _nowTime,
                                        _notePosition = _nowMeasureChips.Count
                                    });

                                _nowGoGo = false;
                            }
                            else if (line.StartsWith("#JPOSSCROLL", StringComparison.CurrentCulture))
                            {
                                var duration = lineSplit.Length >= 2 ? ParseParameter(lineSplit[1], 0) * 1000 : 0;
                                Complex moveOffset = Complex.Zero;

                                // 仕様: #JPOSSCROLL <duration(sec)> <distance(complex)> <direction>
                                // direction: 1=右, 0=左 (X成分にのみ適用)
                                if (lineSplit.Length >= 4)
                                {
                                    var moveDirection = (int)ParseParameter(lineSplit[3], 0);
                                    var moveSign = moveDirection == 0 ? -1 : 1;

                                    var distanceRaw = lineSplit[2].Trim();
                                    if (!string.IsNullOrWhiteSpace(distanceRaw))
                                    {
                                        var moveOffsetComplex = ComplexParser.Parse(distanceRaw);
                                        var correctedMoveOffset = new Complex(moveOffsetComplex.Real, moveOffsetComplex.Imaginary);
                                        moveOffset = new Complex(correctedMoveOffset.Real * moveSign, correctedMoveOffset.Imaginary) * 1.5;
                                    }
                                }

                                _nowMeasureCommands.Add(
                                    new JPosScroll()
                                    {
                                        _cmdType = CommandType.JPosScroll,
                                        _branch = _nowProcessingBranch,
                                        _time = _nowTime,
                                        _notePosition = _nowMeasureChips.Count,
                                        _duration = duration,
                                        _moveOffset = moveOffset,
                                    });

                                _nowGoGo = false;
                            }
                            else if (line.StartsWith("#SENOTECHANGE", StringComparison.CurrentCulture))
                            {
                                if (lineSplit.Length == 1)
                                {
                                    parameter = line.Remove(0, 13);
                                }

                                _nowSeNote = SeNoteFromValue((int)ParseParameter(parameter, 0));
                            }
                            else if (line.StartsWith("#NOTESCHANGE", StringComparison.CurrentCulture))
                            {
                                if (lineSplit.Length == 1)
                                {
                                    parameter = line.Remove(0, 12);
                                }

                                _nowSeNote = SeNoteFromValue((int)ParseParameter(parameter, -1) + 1);
                            }
                            else if (line.StartsWith("#LAYER", StringComparison.CurrentCulture))
                            {
                                if (lineSplit.Length == 1)
                                {
                                    parameter = line.Remove(0, 6);
                                }

                                _nowLayer = (int)ParseParameter(parameter, 0);
                            }
                            else if (line.StartsWith("#SUDDEN", StringComparison.CurrentCulture))
                            {
                                _suddenShowTime = ParseParameter(lineSplit[1], 0) * 1000;
                                _suddenMoveTime = ParseParameter(lineSplit[2], 0) * 1000;
                            }
                            else if (line.StartsWith("#HBSCROLL", StringComparison.CurrentCulture))
                            {
                                _nowMeasureCommands.Add(
                                    new Command()
                                    {
                                        _cmdType = CommandType.HBScroll,
                                        _branch = _nowProcessingBranch,
                                        _time = _nowTime,
                                        _notePosition = _nowMeasureChips == null ? 0 : _nowMeasureChips.Count
                                    });

                                _nowHBScroll = true;
                            }
                            else if (line.StartsWith("#SCROLL", StringComparison.CurrentCulture))
                            {
                                if (lineSplit.Length == 1)
                                {
                                    parameter = line.Remove(0, 7);
                                }

                                if (parameter.EndsWith(".", StringComparison.Ordinal))
                                    parameter = parameter.TrimEnd('.');

                                try
                                {
                                    if (string.IsNullOrWhiteSpace(parameter))
                                    {
                                        _nowScroll = 1.0;
                                    }
                                    else
                                    {
                                        Complex scrollComplex = ComplexParser.Parse(parameter);
                                        Complex correctScrollCoplex = new Complex(scrollComplex.Real, -scrollComplex.Imaginary);
                                        _nowScroll = correctScrollCoplex;
                                    }
                                }
                                catch
                                {
                                    Debug.Print(parameter);
                                }
                            }
                            else if (line.StartsWith("#MEASURE", StringComparison.CurrentCulture))
                            {
                                if (lineSplit.Length == 1)
                                {
                                    parameter = line.Remove(0, 8);
                                }

                                string numerator = parameter.Split('/')[1];
                                string denominator = parameter.Split('/')[0];
                                _nowMeasure = ParseParameter(numerator, 4) / ParseParameter(denominator, 1);
                            }
                            else if (line.StartsWith("#BPMCHANGE", StringComparison.CurrentCulture))
                            {
                                if (lineSplit.Length == 1)
                                {
                                    parameter = line.Remove(0, 10);
                                }

                                _nowBpm = ParseParameter(parameter, song._header._bpm);

                                _nowMeasureCommands.Add(
                                    new BPMChange()
                                    {
                                        _cmdType = CommandType.BPMChange,
                                        _branch = _nowProcessingBranch,
                                        _time = _nowTime,
                                        _notePosition = _nowMeasureChips.Count,
                                        _newBPM = _nowBpm
                                    });
                            }
                            else if (line.StartsWith("#BARLINEOFF", StringComparison.CurrentCulture))
                            {
                                _nowBarVisible = false;
                                _measureFirstNote._isBarVisible = false;
                            }
                            else if (line.StartsWith("#BARLINEON", StringComparison.CurrentCulture))
                            {
                                _nowBarVisible = true;
                                _measureFirstNote._isBarVisible = true;
                            }
                            else if (line.StartsWith("#DELAY", StringComparison.CurrentCulture))
                            {
                                if (lineSplit.Length == 1)
                                {
                                    parameter = line.Remove(0, 6);
                                }

                                _nowMeasureCommands.Add(
                                    new Delay()
                                    {
                                        _cmdType = CommandType.BPMChange,
                                        _branch = _nowProcessingBranch,
                                        _time = _nowTime,
                                        _notePosition = _nowMeasureChips.Count,
                                        _delayTime = ParseParameter(parameter, 0) * 1000
                                    });
                            }
                            else if (line.StartsWith("#SECTION", StringComparison.CurrentCulture))
                            {
                                BranchCommand cmd = new BranchCommand();
                                cmd._branchCmdType = BranchCommandType.Section;
                                cmd._time = _nowTime;
                                song._songCourses[_nowCourse]._branchCommands.Add(cmd);
                            }
                            else if (line.StartsWith("#BRANCHSTART"))
                            {
                                #region [ BRANCHSTART ]

                                parameter = line.Remove(0, 12).Replace(" ", "");
                                song._songCourses[_nowCourse]._hasBranch = true;

                                this._nowBranchStartTime = _nowTime;

                                this._bpmBeforeBranch = _nowBpm;
                                this._scrollBeforeBranch = _nowScroll;
                                this._measureBeforeBranch = _nowMeasure;
                                this._barVisibleBeforeBranch = _nowBarVisible;
                                this._goGoBeforeBranch = _nowGoGo;
                                this._layerBeforeBranch = _nowLayer;

                                _nowBranchCount++;
                                parameter = parameter.Trim();
                                string condition = parameter.Split(',')[0];
                                string expCon = "";
                                string masCon = "";

                                if (parameter.Length >= 2)
                                    expCon = parameter.Split(',')[1];

                                if (parameter.Length >= 3)
                                    masCon = parameter.Split(',')[2];

                                BranchCommand cmd = new BranchCommand();
                                cmd._time = _branchChangeTime;
                                cmd._startTime = _nowBranchStartTime;
                                // 判定時刻は #BRANCHSTART の1小節前に置く
                                var oneMeasureTime = (15000.0 / _bpmBeforeBranch / _measureBeforeBranch) * 16.0;
                                cmd._judgeTime = Math.Max(0, _nowBranchStartTime - oneMeasureTime);
                                cmd._branchIndex = _nowBranchCount;
                                cmd._branchCmdType = BranchCommandType.Branchstart;
                                cmd._conditionsJudgementValues[0] = ParseParameter(expCon, 0);
                                cmd._conditionsJudgementValues[1] = ParseParameter(masCon, 0);

                                this._isNowProcessingBranch = true;

                                if (condition == "p")
                                {
                                    cmd._branchCondition = BranchCondition.Perfect;
                                }
                                else if (condition == "r")
                                {
                                    cmd._branchCondition = BranchCondition.Roll;

                                    // 仕様:
                                    // 直前小節に連打がある場合、連打終了(8)の1拍後を分岐判定時刻にする
                                    var prevMeasureStart = _nowBranchStartTime - oneMeasureTime;
                                    var beatTime = oneMeasureTime / 4.0;

                                    double lastRollEndTime = double.NegativeInfinity;
                                    var chips = song._songCourses[_nowCourse]._chips;
                                    if (chips != null)
                                    {
                                        for (int idx = 0; idx < chips.Count; idx++)
                                        {
                                            var chip = chips[idx];
                                            if (chip._noteType == NoteType.RollEnd &&
                                                chip._time >= prevMeasureStart &&
                                                chip._time < _nowBranchStartTime)
                                            {
                                                lastRollEndTime = chip._time;
                                            }
                                        }
                                    }

                                    if (!double.IsNegativeInfinity(lastRollEndTime))
                                    {
                                        cmd._judgeTime = Math.Max(0, lastRollEndTime + beatTime);
                                    }
                                }
                                else if (condition == "s")
                                {
                                    cmd._branchCondition = BranchCondition.Score;
                                }
                                else if (condition == "m")
                                {
                                    cmd._branchCondition = BranchCondition.Miss;
                                }

                                song._songCourses[_nowCourse]._branchCommands.Add(cmd);

                                if (_isUsingCommonBallonValue)
                                {
                                    _nowBranchBalloonCount[0] = _nowBalloonCount;
                                    _nowBranchBalloonCount[1] = _nowBalloonCount;
                                    _nowBranchBalloonCount[2] = _nowBalloonCount;
                                }

                                #endregion
                            }
                            else if (line.StartsWith("#BRANCHEND", StringComparison.CurrentCulture))
                            {
                                BranchCommand cmd = new BranchCommand();
                                cmd._time = _nowTime;
                                cmd._branchIndex = _nowBranchCount;
                                cmd._branchCmdType = BranchCommandType.Branchend;

                                song._songCourses[_nowCourse]._branchCommands.Add(cmd);
                                this._isNowProcessingBranch = false;
                                this._nowProcessingBranch = Branch.None;
                                this._nowBranch = Branch.None;

                                if (_isUsingCommonBallonValue)
                                {
                                    _nowBalloonCount = _nowBranchBalloonCount[2];
                                }
                            }
                            else if (lineSplit[0] == "#N")
                            {
                                this._nowProcessingBranch = Branch.Normal;
                                this._nowTime = _nowBranchStartTime;
                                this._nowBpm = _bpmBeforeBranch;
                                this._nowScroll = _scrollBeforeBranch;
                                this._nowMeasure = _measureBeforeBranch;
                                this._nowBarVisible = _barVisibleBeforeBranch;
                                this._nowGoGo = _goGoBeforeBranch;
                                this._nowLayer = _layerBeforeBranch;
                                this._nowBranch = Branch.Normal;
                            }
                            else if (lineSplit[0] == "#E")
                            {
                                this._nowProcessingBranch = Branch.Expert;
                                this._nowTime = _nowBranchStartTime;
                                this._nowBpm = _bpmBeforeBranch;
                                this._nowScroll = _scrollBeforeBranch;
                                this._nowMeasure = _measureBeforeBranch;
                                this._nowBarVisible = _barVisibleBeforeBranch;
                                this._nowGoGo = _goGoBeforeBranch;
                                this._nowLayer = _layerBeforeBranch;
                                this._nowBranch = Branch.Expert;
                            }
                            else if (lineSplit[0] == "#M")
                            {
                                this._nowProcessingBranch = Branch.Master;
                                this._nowTime = _nowBranchStartTime;
                                this._nowBpm = _bpmBeforeBranch;
                                this._nowScroll = _scrollBeforeBranch;
                                this._nowMeasure = _measureBeforeBranch;
                                this._nowBarVisible = _barVisibleBeforeBranch;
                                this._nowGoGo = _goGoBeforeBranch;
                                this._nowLayer = _layerBeforeBranch;
                                this._nowBranch = Branch.Master;
                            }
                            else if (line.StartsWith("#LEVELHOLD", StringComparison.CurrentCulture))
                            {
                                BranchCommand cmd = new BranchCommand();
                                cmd._branchCmdType = BranchCommandType.Levelhold;
                                cmd._branch = this._nowProcessingBranch;
                                cmd._time = _nowTime;
                                song._songCourses[_nowCourse]._branchCommands.Add(cmd);
                            }
                        }
                    }
                }
            }

            return song;
        }

        private bool _isParsing = false;
        private int _nowCourse = 0;
        private double _nowTime = 0;
        private double _nowTimeForHBScroll = 0;
        private double _nowBpm = 0;
        private Complex _nowScroll = 1.0;
        private double _nowMeasure = 1.0;
        private bool _nowBarVisible = true;
        private bool _nowGoGo = false;
        private bool _nowHBScroll = false;

        private double _bpmBeforeBranch = 0;
        private Complex _scrollBeforeBranch = 1.0;
        private double _measureBeforeBranch = 1.0;
        private bool _barVisibleBeforeBranch = true;
        private bool _goGoBeforeBranch = false;

        private int _nowBalloonCount;
        private int[] _nowBranchBalloonCount = new int[3];
        private List<int> _commonBalloon;
        private Branch _nowBranch;
        private RollType _nowRollType;
        private Chip[] _nowRollStartNote = new Chip[3];
        private Chip _measureFirstNote;
        private List<Chip> _nowMeasureChips;
        private List<Command> _nowMeasureCommands;
        private int _nowBranchCount = -1;
        private double _branchChangeTime = 0;
        private double _nowBranchStartTime = 0;
        private bool _isNowProcessingBranch = false;
        private bool _isNowSearchingRollEnd = false;
        private Branch _nowProcessingBranch = Branch.Normal;
        private bool _isUsingCommonBallonValue = false;

        private double _suddenShowTime = 0;
        private double _suddenMoveTime = 0;
        private SeNoteType _nowSeNote = SeNoteType.None;
        private int _nowLayer = 0;
        private int _layerBeforeBranch = 0;

        private static SeNoteType SeNoteFromValue(int value)
        {
            return value switch
            {
                1 => SeNoteType.Don,
                2 => SeNoteType.Do,
                3 => SeNoteType.Ko,
                4 => SeNoteType.Katsu,
                5 => SeNoteType.Ka,
                6 => SeNoteType.DON,
                7 => SeNoteType.KA,
                8 or 11 => SeNoteType.RollStart,
                10 => SeNoteType.RollEnd,
                12 => SeNoteType.Balloon,
                _ => SeNoteType.None,
            };
        }

        private double ParseParameter(string str, double defaultVal)
        {
            if (str.StartsWith("inf"))
                return double.PositiveInfinity;

            double ret = 0;
            bool isParsed = double.TryParse(str, out ret);

            return isParsed ? ret : defaultVal;
        }

        private int GetCourseFromData(string data)
        {
            string[] courses = { "easy", "normal", "hard", "oni", "edit" };
            int ret = 0;
            var result = 0;

            if (!int.TryParse(data, out result))
            {
                data = data.Trim().ToLower();

                for (int i = 0; i < 5; i++)
                {
                    if (courses[i] == data)
                    {
                        ret = i;
                        break;
                    }
                }
            }
            else
            {
                ret = result;
            }

            return ret;
        }

        private void ProcessMeasure(Song song)
        {
            Chip measure = new Chip();

            _branchChangeTime = _nowTime;

            if (_nowMeasureChips.Count == 0)
            {
                #region [ 小節線 ]

                measure._noteType = NoteType.Measure;
                measure._time = _nowTime;
                measure._timeForHBScroll = _nowTimeForHBScroll;
                measure._bpm = _nowBpm;
                measure._scroll = _nowScroll;
                measure._measure = _nowMeasure;
                measure._branch = _nowBranch;
                measure._isGoGo = _nowGoGo;
                measure._isBarVisible = _nowBarVisible;
                measure._branchIndex = _nowBranchCount;
                measure._suddenShowTime = _suddenShowTime;
                measure._suddenMoveTime = _suddenMoveTime;
                measure._isHBScroll = _nowHBScroll;

                #endregion

                #region [ コマンドの時間処理 ]

                foreach (var command in _nowMeasureCommands)
                {
                    if (command is Delay)
                    {
                        _nowTime += ((Delay)command)._delayTime;
                    }
                    else
                    {
                        command._time = _nowTime;
                        song._songCourses[_nowCourse]._commands.Add(command);
                    }
                }
                _nowMeasureCommands.Clear();

                #endregion

                _nowTime += (15000.0 / _nowBpm / _nowMeasure) * 16.0;
                _nowTimeForHBScroll += (15000.0 / song._header._bpm / _nowMeasure) * 16.0;

                #region [ 次のノーツのデータ ]

                _measureFirstNote._time = _nowTime;
                _measureFirstNote._timeForHBScroll = _nowTimeForHBScroll;
                _measureFirstNote._bpm = _nowBpm;
                _measureFirstNote._scroll = _nowScroll;
                _measureFirstNote._measure = _nowMeasure;
                _measureFirstNote._branch = _nowBranch;
                _measureFirstNote._isGoGo = _nowGoGo;
                _measureFirstNote._isBarVisible = _nowBarVisible;
                _measureFirstNote._suddenMoveTime = _suddenMoveTime;
                _measureFirstNote._suddenShowTime = _suddenShowTime;
                _measureFirstNote._isHBScroll = _nowHBScroll;

                #endregion
            }
            else
            {
                #region [ 小節線 ]

                measure._noteType = NoteType.Measure;
                measure._time = _measureFirstNote._time;
                measure._bpm = _measureFirstNote._bpm;
                measure._scroll = _measureFirstNote._scroll;
                measure._measure = _measureFirstNote._measure;
                measure._branch = _measureFirstNote._branch;
                measure._isGoGo = _measureFirstNote._isGoGo;
                measure._isBarVisible = _measureFirstNote._isBarVisible;
                measure._branchIndex = _nowBranchCount;
                measure._suddenShowTime = _suddenShowTime;
                measure._suddenMoveTime = _suddenMoveTime;
                measure._timeForHBScroll = _nowTimeForHBScroll;
                measure._isHBScroll = _nowHBScroll;

                #endregion

                for (int j = 0; j < _nowMeasureChips.Count; j++)
                {
                    #region [ Delayのみ先に処理 ]

                    var commands = _nowMeasureCommands.Where(x => x._notePosition == j).ToList();

                    if (commands != null && commands.Count != 0)
                    {
                        foreach (var command in commands)
                        {
                            if (command is Delay)
                            {
                                _nowTime += ((Delay)command)._delayTime;
                                _nowMeasureCommands.Remove(command);
                            }
                        }
                    }

                    #endregion

                    if (this._nowMeasureChips[j]._noteType != NoteType.None)
                    {
                        this._nowMeasureChips[j]._time = _nowTime;
                        this._nowMeasureChips[j]._timeForHBScroll = _nowTimeForHBScroll;

                        #region [ チップの追加 ]

                        if (_isNowProcessingBranch)
                        {
                            switch (_nowProcessingBranch)
                            {
                                case Branch.Normal:
                                    song._songCourses[_nowCourse]._chipsNormal.Add(this._nowMeasureChips[j]);
                                    break;
                                case Branch.Expert:
                                    song._songCourses[_nowCourse]._chipsExpert.Add(this._nowMeasureChips[j]);
                                    break;
                                case Branch.Master:
                                    song._songCourses[_nowCourse]._chipsMaster.Add(this._nowMeasureChips[j]);
                                    break;
                            }
                        }
                        else
                        {
                            song._songCourses[_nowCourse]._chips.Add(this._nowMeasureChips[j]);
                        }

                        #endregion

                        #region [ 連打始点・終点ノーツの決定 ]

                        var chiplist = song._songCourses[_nowCourse]._chips;
                        if (_isNowProcessingBranch)
                        {
                            switch (_nowProcessingBranch)
                            {
                                case Branch.Normal:
                                    chiplist = song._songCourses[_nowCourse]._chipsNormal;
                                    break;
                                case Branch.Expert:
                                    chiplist = song._songCourses[_nowCourse]._chipsExpert;
                                    break;
                                case Branch.Master:
                                    chiplist = song._songCourses[_nowCourse]._chipsMaster;
                                    break;
                            }
                        }

                        var nowchip = chiplist[chiplist.Count - 1];


                        if (nowchip._noteType == NoteType.RollStart || nowchip._noteType == NoteType.RollBigStart || nowchip._noteType == NoteType.BalloonStart || nowchip._noteType == NoteType.Kusudama)
                        {
                            _isNowSearchingRollEnd = true;
                            _nowRollStartNote[_nowProcessingBranch == Branch.None ? 0 : (int)_nowProcessingBranch] = chiplist[chiplist.Count - 1];
                        }
                        else if (nowchip._noteType == NoteType.RollEnd && _isNowSearchingRollEnd)
                        {
                            // ロール終了は、現在処理中のコンテキスト内の開始ノーツにのみ付ける
                            int rollStartIndex = _isNowProcessingBranch ? (int)_nowProcessingBranch : 0;

                            if (_nowRollStartNote[rollStartIndex] != null)
                            {
                                _nowRollStartNote[rollStartIndex]._rollEnd = chiplist[chiplist.Count - 1];
                                _isNowSearchingRollEnd = false;
                            }
                        }

                        #endregion
                    }

                    #region [ コマンドの時間処理 ]

                    if (commands != null && commands.Count != 0)
                    {
                        foreach (var command in commands)
                        {
                            if (command is not Delay)
                            {
                                command._time = _nowTime;
                                song._songCourses[_nowCourse]._commands.Add(command);
                                _nowMeasureCommands.Remove(command);
                            }
                        }
                    }

                    #endregion

                    this._nowTime += (15000.0 / this._nowMeasureChips[j]._bpm / this._nowMeasureChips[j]._measure) * (16.0 / this._nowMeasureChips.Count);
                    this._nowTimeForHBScroll += (15000.0 / song._header._bpm / this._nowMeasureChips[j]._measure) * (16.0 / this._nowMeasureChips.Count);
                }

                int measureChipCount = this._nowMeasureChips.Count;
                this._nowMeasureChips.Clear();

                #region [ 小節末尾コマンド ]

                // notePosition が小節内ノーツ数以上のコマンドは小節末尾時刻で処理
                // （次小節先頭へ繰り越すと、先頭のDELAY等の影響を受けて時刻がずれる）
                if (_nowMeasureCommands.Count > 0)
                {
                    var tailCommands = _nowMeasureCommands
                        .Where(x => x._notePosition >= measureChipCount)
                        .ToList();

                    foreach (var command in tailCommands)
                    {
                        if (command is Delay)
                        {
                            _nowTime += ((Delay)command)._delayTime;
                        }
                        else
                        {
                            command._time = _nowTime;
                            song._songCourses[_nowCourse]._commands.Add(command);
                        }

                        _nowMeasureCommands.Remove(command);
                    }
                }

                _nowMeasureCommands.Clear();

                #endregion
            }

            if (_isNowProcessingBranch)
            {
                switch (_nowProcessingBranch)
                {
                    case Branch.Normal:
                        song._songCourses[_nowCourse]._chipsNormal.Add(measure);
                        break;
                    case Branch.Expert:
                        song._songCourses[_nowCourse]._chipsExpert.Add(measure);
                        break;
                    case Branch.Master:
                        song._songCourses[_nowCourse]._chipsMaster.Add(measure);
                        break;
                }
            }
            else
            {
                song._songCourses[_nowCourse]._chips.Add(measure);
            }
        }

    }
}

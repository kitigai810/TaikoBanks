using System;
using System.Collections.Generic;
using TaikoNauts.Core.Taiko.Charts;

public static class SeNote
{
    public static void Assign(IReadOnlyList<Chip> chips)
    {
        var notes = new List<Chip>();
        foreach (var chip in chips)
        {
            switch (chip._noteType)
            {
                case NoteType.Don:
                case NoteType.Ka:
                case NoteType.DON:
                case NoteType.KA:
                case NoteType.RollStart:
                case NoteType.RollBigStart:
                case NoteType.BalloonStart:
                case NoteType.Kusudama:
                case NoteType.RollEnd:
                    notes.Add(chip);
                    break;
            }
        }

        int n = notes.Count;
        if (n == 0) return;

        var chainFinal = new bool[n];
        var nonLong = new bool[n];
        var vdNext = new double[n];

        double Td(int a, int b) =>
            (a < 0 || b >= n) ? double.PositiveInfinity : notes[b]._time - notes[a]._time;

        for (int i = 0; i < n; i++)
        {
            double tdPrev = Td(i - 1, i);
            double tdNext = Td(i, i + 1);
            double tdNext2 = Td(i + 1, i + 2);

            chainFinal[i] = tdNext >= tdPrev * 4.0 / 3.0 || tdNext2 <= tdNext * 2.0 / 3.0;

            double scroll = Math.Abs(notes[i]._scroll.Real);
            double beatMs = 60000.0 / Math.Max(1.0, notes[i]._bpm);
            double vdPrev = double.IsInfinity(tdPrev)
                ? double.PositiveInfinity
                : scroll * (tdPrev / beatMs);
            vdNext[i] = double.IsInfinity(tdNext)
                ? double.PositiveInfinity
                : Math.Min(scroll, 1.0) * (tdNext / beatMs);

            // 1/16音符 = 0.25拍, 1/8音符 = 0.5拍
            nonLong[i] = vdPrev < 0.25 - 1e-6 || vdNext[i] < 0.5 - 1e-6;
        }

        int start = 0;
        for (int i = 0; i < n; i++)
        {
            if (!chainFinal[i] && i + 1 < n) continue;
            AssignChain(notes, start, i, chainFinal, nonLong, vdNext);
            start = i + 1;
        }
    }

    private static void AssignChain(
        List<Chip> notes, int start, int end,
        bool[] chainFinal, bool[] nonLong, double[] vdNext)
    {
        bool isAltChain = IsAlternativeChain(notes, start, end, nonLong);

        for (int i = start; i <= end; i++)
        {
            var chip = notes[i];
            if (chip._seNoteType != SeNoteType.None) continue;

            switch (chip._noteType)
            {
                case NoteType.DON: chip._seNoteType = SeNoteType.DON; continue;
                case NoteType.KA: chip._seNoteType = SeNoteType.KA; continue;
                case NoteType.RollStart:
                case NoteType.RollBigStart: chip._seNoteType = SeNoteType.RollStart; continue;
                case NoteType.BalloonStart: chip._seNoteType = SeNoteType.Balloon; continue;
                case NoteType.Kusudama: chip._seNoteType = SeNoteType.Kusudama; continue;
                case NoteType.RollEnd:
                    if (chip._rollType != RollType.Balloon)
                        chip._seNoteType = SeNoteType.RollEnd;
                    continue;
            }

            bool isDon = chip._noteType == NoteType.Don;
            bool alt = isAltChain && (i - start) % 2 == 1;
            bool longForm = !alt &&
                ((i == end && chainFinal[i] && !nonLong[i]) ||
                 (!isAltChain && vdNext[i] > 0.5 + 1e-6));

            if (alt) chip._seNoteType = isDon ? SeNoteType.Ko : SeNoteType.Ka;
            else if (longForm) chip._seNoteType = isDon ? SeNoteType.Don : SeNoteType.Katsu;
            else chip._seNoteType = isDon ? SeNoteType.Do : SeNoteType.Ka;
        }
    }

    private static bool IsAlternativeChain(List<Chip> notes, int start, int end, bool[] nonLong)
    {
        int count = end - start + 1;
        if (count < 3 || count % 2 == 0) return false;

        var type = notes[start]._noteType;
        if (type != NoteType.Don && type != NoteType.Ka) return false;

        if (notes[end]._time - notes[start]._time > 500.0 + 1e-6) return false;
        if (nonLong[end]) return false;

        double firstGap = notes[start + 1]._time - notes[start]._time;
        for (int i = start; i <= end; i++)
        {
            if (notes[i]._noteType != type) return false;
            if (i > start && Math.Abs(notes[i]._time - notes[i - 1]._time - firstGap) > 3.0)
                return false;
        }
        return true;
    }
}

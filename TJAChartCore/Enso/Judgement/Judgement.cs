using TaikoNauts.Core.Taiko.Charts;

namespace TaikoNauts.Core.Taiko.Enso.Judgement;

public static class Judgement
{
    /// <summary>
    /// Get the nearest chip based on the current time and the list of chips.
    /// </summary>
    /// <param name="time">The current time.</param>
    /// <param name="startIndex">The index to start searching from.</param>
    /// <param name="chips">The list of chips to search through.</param>
    /// <param name="course">The course index.</param>
    /// <param name="don">Indicates if the chip is a "don" type.</param>
    /// <returns>The nearest chip, or null if none is found.</returns>
    public static Chip? GetNearestChip(double time, int startIndex, IReadOnlyList<Chip> chips, bool don)
    {
        Chip? nearestchip = new Chip();
        var count = chips.Count;
        var startPosition = startIndex;
        Chip? pastChip;
        Chip? futureChip;
        var pastJudge = NoteJudge.Bad;
        var futureJudge = NoteJudge.Bad;

        bool IsDon(Chip chip)
        {
            return chip._noteType == NoteType.Don || chip._noteType == NoteType.DON;
        }

        bool IsKa(Chip chip)
        {
            return chip._noteType == NoteType.Ka || chip._noteType == NoteType.KA;
        }

        bool IsRoll(Chip chip)
        {
            return chip._noteType == NoteType.RollStart ||
                chip._noteType == NoteType.RollBigStart ||
                chip._noteType == NoteType.BalloonStart;
        }

        if (count <= 0)
        {
            return null;
        }

        if (startPosition >= count)
        {
            startPosition -= 1;
        }

        Chip? afterChip = null;
        for (var pastNote = startPosition - 1; ; pastNote--)
        {
            if (pastNote < 0)
            {
                pastChip = afterChip != null ? afterChip : null;
                break;
            }

            var processingChip = chips[pastNote];
            if (!processingChip._isHit)
            {
                if (!IsRoll(processingChip))
                {
                    if (don ? IsDon(processingChip) : IsKa(processingChip))
                    {
                        var processingChipJudge = GetJudgeFromTime(time, processingChip, true);

                        if (processingChipJudge != NoteJudge.Miss)
                        {
                            afterChip = processingChip;
                            pastJudge = processingChipJudge;
                            continue;
                        }

                        pastChip = afterChip;
                        break;
                    }
                }
                else if (time <= processingChip._rollEnd!._time)
                {
                    return processingChip;
                }
            }
        }

        for (var futureNote = startPosition; ; futureNote++)
        {
            if (futureNote >= count)
            {
                futureChip = null;
                break;
            }

            var processingChip = chips[futureNote];
            if (!processingChip._isHit)
            {
                if (!IsRoll(processingChip))
                {
                    if (don ? IsDon(processingChip) : IsKa(processingChip))
                    {
                        var processingChipJudge = GetJudgeFromTime(time, processingChip, true);

                        if (processingChipJudge != NoteJudge.Miss)
                        {
                            futureChip = processingChip;
                            futureJudge = processingChipJudge;
                            break;
                        }

                        futureChip = null;
                        break;
                    }
                }
            }
        }

        if ((pastJudge == NoteJudge.Miss || pastJudge == NoteJudge.Bad) &&
            (futureJudge != NoteJudge.Miss && futureJudge != NoteJudge.Bad))
        {
            nearestchip = futureChip;
        }
        else if (futureChip == null && pastChip != null)
        {
            nearestchip = pastChip;
        }
        else if (pastChip == null && futureChip != null)
        {
            nearestchip = futureChip;
        }
        else
        {
            nearestchip = pastChip;
        }

        return nearestchip;
    }

    /// <summary>
    /// Get the judgment result based on the time difference between the current time and the chip's time.
    /// </summary>
    /// <param name="time">The current time.</param>
    /// <param name="chip">The chip to judge.</param>
    /// <param name="debug">Indicates if debug information should be displayed.</param>
    /// <returns>The judgment result.</returns>
    /// <returns></returns>
    public static NoteJudge GetJudgeFromTime(double time, Chip? chip, bool debug = false)
    {
        var ret = NoteJudge.Miss;

        if (chip != null)
        {
            var deltaTime = Math.Abs(time - chip._time);

            if (chip._noteType == NoteType.BalloonStart ||
                chip._noteType == NoteType.RollStart ||
                chip._noteType == NoteType.RollBigStart)
            {
                ret = NoteJudge.Roll;
            }
            else
            {
                double perfectWindow = 30;
                double goodWindow = 80;
                double badWindow = 110;

                if (deltaTime <= perfectWindow)
                {
                    ret = NoteJudge.Great;
                }
                else if (deltaTime <= goodWindow)
                {
                    ret = NoteJudge.Good;
                }
                else if (deltaTime <= badWindow)
                {
                    ret = NoteJudge.Bad;
                }
            }
        }

        return ret;
    }

    /// <summary>
    /// Calculate the accuracy percentage based on the number of perfect, good, and miss judgments.
    /// </summary>
    /// <param name="perfect">The number of perfect judgments.</param>
    /// <param name="good">The number of good judgments.</param>
    /// <param name="miss">The number of miss judgments.</param>
    /// <returns>The accuracy percentage.</returns>
    public static double GetAccuracy(int perfect, int good, int miss)
    {
        var total = perfect + good + miss;
        return total == 0 ? 0 : (perfect + good * 0.5) / total * 100.0;
    }
}

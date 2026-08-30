namespace TaikoNauts.Core.Taiko.Charts.Models.Commands;

public sealed class BranchCommand
{
    public bool _isPassing;
    public double _time;
    public double _judgeTime;
    public double _startTime;
    public int _branchIndex;
    public Branch _branch;
    public BranchCommandType _branchCmdType;
    public BranchCondition _branchCondition;
    public double[] _conditionsJudgementValues = new double[2];
}

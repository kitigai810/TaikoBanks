using TaikoNauts.Core.Taiko.Charts.Models.Commands;

namespace TaikoNauts.Core.Taiko.Charts;

public sealed class SongCourse
{
    public int _level { get; set; }
    public bool _hasBranch { get; set; }
    public List<int> _balloon { get; set; } = new();
    public List<int> _balloonNormal { get; set; } = new();
    public List<int> _balloonExpert { get; set; } = new();
    public List<int> _balloonMaster { get; set; } = new();
    public List<Chip> _chips { get; set; } = new();
    public List<Chip> _chipsNormal { get; set; } = new();
    public List<Chip> _chipsExpert { get; set; } = new();
    public List<Chip> _chipsMaster { get; set; } = new();
    public List<Command> _commands { get; set; } = new();
    public List<BranchCommand> _branchCommands { get; set; } = new();
}

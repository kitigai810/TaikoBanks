using System.Numerics;

namespace TaikoNauts.Core.Taiko.Charts.Models.Commands;

public sealed class JPosScroll : Command
{
    public double _duration;
    public Complex _moveOffset;
}

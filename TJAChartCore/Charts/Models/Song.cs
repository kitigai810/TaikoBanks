namespace TaikoNauts.Core.Taiko.Charts;

public sealed class Song
{
    public SongHeader _header = new();
    public SongCourse?[] _songCourses = new SongCourse?[5];
}

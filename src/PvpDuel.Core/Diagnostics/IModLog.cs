namespace PvpDuel.Core.Diagnostics;

/// <summary>
/// Game-independent logging seam so Core logic can emit diagnostics without
/// referencing the game's logging stack. The game project binds this to the
/// <c>[pvpduel]</c> channel; unit tests can capture output instead.
/// </summary>
public interface IModLog
{
    void Info(string message);

    void Warn(string message);

    void Error(string message);
}

/// <summary>Swallow-everything logger used when no sink is wired.</summary>
public sealed class NullModLog : IModLog
{
    public static readonly NullModLog Instance = new();

    private NullModLog()
    {
    }

    public void Info(string message)
    {
    }

    public void Warn(string message)
    {
    }

    public void Error(string message)
    {
    }
}

namespace Neox.Core.Models;

public sealed record TweakResult(bool Success, string Message, RestartRequirement Restart = RestartRequirement.None)
{
    public static TweakResult Ok(string message, RestartRequirement restart = RestartRequirement.None) => new(true, message, restart);
    public static TweakResult Fail(string message) => new(false, message);
}

public sealed record TweakContext(string? GameExePath = null);

public sealed class TweakException : Exception
{
    public TweakException(string message) : base(message) { }
    public TweakException(string message, Exception inner) : base(message, inner) { }
}

using System;

namespace WebDisplay.Services;

public sealed class RecoveryPolicy
{
    public int Failures { get; private set; }
    public TimeSpan NextDelay() { Failures = Math.Min(Failures + 1, 30); return TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(2, Math.Min(Failures - 1, 4)))); }
    public void Reset() => Failures = 0;
}

using System;

namespace ObsInterface;

public sealed class ObsInterfaceOptions
{
    public bool StrictMode { get; set; }
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

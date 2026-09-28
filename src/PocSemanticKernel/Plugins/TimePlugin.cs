using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace PocSemanticKernel.Plugins;

/// <summary>
/// A native plugin: plain C# methods exposed to the model as tools.
/// The model only sees the names and [Description]s, so write them for the model to read.
/// </summary>
public sealed class TimePlugin
{
    [KernelFunction, Description("Gets the current local date and time, including the time zone.")]
    public string GetCurrentDateTime() =>
        $"{DateTimeOffset.Now:dddd, yyyy-MM-dd HH:mm:ss zzz} ({TimeZoneInfo.Local.DisplayName})";
}

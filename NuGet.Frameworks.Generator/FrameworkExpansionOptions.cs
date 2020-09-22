using System;

namespace NuGet.Frameworks.Generator
{
    [Flags]
    public enum FrameworkExpansionOptions
    {
        None = 1 << 0,
        RoundTripDotNetFrameworkName = 1 << 1,
        RoundTripShortFolderName = 1 << 2,
        FrameworkExpander = 1 << 3,
        MinimumVersion = 1 << 4,
        RemoveProfile = 1 << 5,
        All = ~0,
    }
}

using System;

namespace Polaris.Map
{
    public sealed class InvalidLiveMapException : InvalidOperationException
    {
        internal InvalidLiveMapException(string key)
            : base($"This live map is no longer current: {key ?? "<unknown>"}.") { }
    }

    public sealed class InvalidMapElementException : InvalidOperationException
    {
        internal InvalidMapElementException()
            : base("This map element was removed or its map is no longer current.") { }
    }
}

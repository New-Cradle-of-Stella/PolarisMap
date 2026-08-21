using System;
using System.Collections.Generic;
using Polaris.Map.Authoring;

namespace Polaris.Map.Debugging
{
    internal sealed class MapDebugSnapshot
    {
        internal string CurrentKey;
        internal string Activity;
        internal DateTime CapturedAt;
        internal IReadOnlyList<MapDebugEntry> Maps;
    }

    internal sealed class MapDebugEntry
    {
        internal string Key;
        internal string Owner;
        internal string Xml;
        internal PmapDocument Document;
        internal bool IsCurrent;
        internal bool IsLoading;

        internal int ElementCount
        {
            get
            {
                int count = 0;
                foreach (PmapLayer layer in Document.Layers)
                    count += layer.Elements.Count;
                return count;
            }
        }
    }
}

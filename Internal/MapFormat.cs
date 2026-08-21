using System;
using System.Text.RegularExpressions;

namespace Polaris.Map.Internal
{
    /// <summary>TMAP v4 在公开模型与原版对象之间共享的数值规则。</summary>
    internal static class MapFormat
    {
        internal const byte Version = 4;
        internal const int CellPixels = 28;
        internal const byte DefaultCollectionCapacity = 4;

        static readonly Regex SafeMapKey = new(
            @"^[A-Za-z0-9_-][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant);

        internal static bool IsSafeMapKey(string key)
            => !string.IsNullOrEmpty(key) && SafeMapKey.IsMatch(key);

        internal static int NormalizeQuarterTurns(int value)
        {
            int normalized = value % 4;
            return normalized < 0 ? normalized + 4 : normalized;
        }

        internal static int RequireOpacity(int value, string parameterName)
        {
            if (value < 0 || value > 100)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName, value, "Opacity must be between 0 and 100.");
            }
            return value;
        }

        internal static void RequireFinite(float x, float y, string parameterName)
        {
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y))
            {
                throw new ArgumentException("Map coordinates must be finite.", parameterName);
            }
        }
    }
}

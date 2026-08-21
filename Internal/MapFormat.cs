using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Polaris.Map.Internal
{
    /// <summary>
    /// TMAP v4 在公开模型与原版对象之间共享的数值规则。<see cref="Authoring.PmapDocument.Validate"/>（XML 编写层）
    /// 与 <see cref="MapDraftValidator"/>（编译草稿层）各自校验一份几乎相同的规则集，这里是两边共用的唯一出处，
    /// 避免正则、字节上限这类常量各写一份、悄悄失步。
    /// </summary>
    internal static class MapFormat
    {
        internal const byte Version = 4;
        internal const int CellPixels = 28;
        internal const byte DefaultCollectionCapacity = 4;

        /// <summary>Pascal 字符串（Key、图层名）的 UTF-8 字节上限：TMAP 用一个字节存长度。</summary>
        internal const int MaxPascalStringBytes = byte.MaxValue;

        /// <summary>普通字符串（注释）的 UTF-8 字节上限：TMAP 用两个字节存长度。</summary>
        internal const int MaxLongStringBytes = ushort.MaxValue;

        static readonly Regex SafeMapKey = new(
            @"^[A-Za-z0-9_-][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant);

        internal static bool IsSafeMapKey(string key)
            => !string.IsNullOrEmpty(key) && SafeMapKey.IsMatch(key);

        internal static bool ExceedsUtf8ByteLimit(string value, int maxBytes)
            => Encoding.UTF8.GetByteCount(value ?? "") > maxBytes;

        /// <summary>宽/高是否落在 TMAP 的 u16 像素范围内（&gt; 0 且乘 <see cref="CellPixels"/> 不溢出）。</summary>
        internal static bool IsValidDimensionPixels(int value)
            => value > 0 && value <= ushort.MaxValue / CellPixels;

        internal static bool IsValidOpacity(int value) => value >= 0 && value <= 100;

        internal static bool IsValidRotation(int value) => value >= short.MinValue && value <= short.MaxValue;

        internal static int NormalizeQuarterTurns(int value)
        {
            int normalized = value % 4;
            return normalized < 0 ? normalized + 4 : normalized;
        }

        internal static int RequireOpacity(int value, string parameterName)
        {
            if (!IsValidOpacity(value))
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

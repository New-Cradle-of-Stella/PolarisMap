using System;

namespace Polaris.Map
{
    /// <summary>与游戏内部类型解耦的 8 位 RGBA 颜色。</summary>
    public readonly struct MapColor : IEquatable<MapColor>
    {
        public MapColor(byte red, byte green, byte blue, byte alpha = byte.MaxValue)
        {
            Red = red;
            Green = green;
            Blue = blue;
            Alpha = alpha;
        }

        public byte Red { get; }
        public byte Green { get; }
        public byte Blue { get; }
        public byte Alpha { get; }

        public static MapColor DefaultBackground => new(245, 255, 238);
        public static MapColor DefaultLayer => new(127, 127, 127);

        public bool Equals(MapColor other)
            => Red == other.Red && Green == other.Green && Blue == other.Blue && Alpha == other.Alpha;

        public override bool Equals(object obj) => obj is MapColor other && Equals(other);

        public override int GetHashCode()
            => (((Red * 397) ^ Green) * 397 ^ Blue) * 397 ^ Alpha;

        public static bool operator ==(MapColor left, MapColor right) => left.Equals(right);
        public static bool operator !=(MapColor left, MapColor right) => !left.Equals(right);
    }
}

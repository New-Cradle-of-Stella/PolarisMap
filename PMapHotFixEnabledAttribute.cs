using System;

namespace Polaris.Map
{
    /// <summary>允许程序集使用 .pmap 热重载和 F11 调试页。</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class PMapHotFixEnabledAttribute : Attribute
    {
    }
}

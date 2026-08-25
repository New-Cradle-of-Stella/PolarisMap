namespace AicTmapToPmap;

internal sealed class CliOptions
{
    internal string? InputPath { get; private set; }
    internal string? OutputPath { get; private set; }
    internal string? GameRoot { get; private set; }
    internal string? ChipsPath { get; private set; }
    internal string? MapKey { get; private set; }
    internal bool Recursive { get; private set; }
    internal bool Strict { get; private set; }
    internal bool Force { get; private set; }
    internal bool ShowHelp { get; private set; }

    internal static bool TryParse(string[] args, out CliOptions options, out string? error)
    {
        options = new CliOptions();
        error = null;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h": case "--help": options.ShowHelp = true; break;
                case "-o": case "--output": if (!Value(args, ref i, arg, out string? output, out error)) return false; options.OutputPath = output; break;
                case "--game-root": if (!Value(args, ref i, arg, out string? game, out error)) return false; options.GameRoot = game; break;
                case "--chips": if (!Value(args, ref i, arg, out string? chips, out error)) return false; options.ChipsPath = chips; break;
                case "--key": if (!Value(args, ref i, arg, out string? key, out error)) return false; options.MapKey = key; break;
                case "-r": case "--recursive": options.Recursive = true; break;
                case "--strict": options.Strict = true; break;
                case "-f": case "--force": options.Force = true; break;
                default:
                    if (arg.StartsWith('-')) { error = "未知选项：" + arg; return false; }
                    if (options.InputPath != null) { error = "只能指定一个输入文件或目录。"; return false; }
                    options.InputPath = arg;
                    break;
            }
        }
        if (!options.ShowHelp && options.InputPath == null) { error = "缺少输入 .tmap 文件或目录。"; return false; }
        return true;
    }

    private static bool Value(string[] args, ref int index, string option, out string? value, out string? error)
    {
        value = null; error = null;
        if (++index >= args.Length) { error = $"选项 `{option}` 缺少参数。"; return false; }
        value = args[index]; return true;
    }
}

using System.Text;
using Polaris.Map.Authoring;

namespace AicTmapToPmap;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (!CliOptions.TryParse(args, out CliOptions options, out string? error))
        {
            Console.Error.WriteLine(error); PrintHelp(); return 2;
        }
        if (options.ShowHelp) { PrintHelp(); return 0; }

        string input = Path.GetFullPath(options.InputPath!);
        if (!File.Exists(input) && !Directory.Exists(input))
        {
            Console.Error.WriteLine("输入不存在：" + input); return 2;
        }
        string? gameRoot = WorkspaceDefaults.ResolveGameRoot(options.GameRoot);
        string? chips = options.ChipsPath;
        if (string.IsNullOrWhiteSpace(chips) && gameRoot != null)
            chips = Path.Combine(gameRoot, "AliceInCradle_Data", "StreamingAssets", "m2d", "__m2d_chips.dat");
        if (string.IsNullOrWhiteSpace(chips) || !File.Exists(chips))
        {
            Console.Error.WriteLine("找不到 __m2d_chips.dat；请检查 E:\\Projects\\Polaris\\aic_path.txt，或使用 --game-root / --chips。");
            return 2;
        }

        IReadOnlyList<string> files = ResolveInputs(input, options.Recursive);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("没有找到 .tmap 文件。目录输入如需递归请添加 --recursive。"); return 2;
        }
        if (files.Count > 1 && options.MapKey != null)
        {
            Console.Error.WriteLine("--key 只能用于单文件转换。"); return 2;
        }

        ChipCatalog catalog;
        try
        {
            catalog = ChipCatalog.Load(Path.GetFullPath(chips));
            Console.WriteLine($"Loaded chip catalog: {catalog.Count} concrete images");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{chips}: error TMAP9001: {ex.GetType().Name}: {ex.Message}"); return 1;
        }

        int failed = 0;
        int warnings = 0;
        foreach (string file in files)
        {
            var diagnostics = new DiagnosticSink();
            try
            {
                string output = ResolveOutput(input, file, options.OutputPath);
                if (File.Exists(output) && !options.Force)
                {
                    diagnostics.Error("TMAP1001", output, -1, "输出已存在；使用 --force 才会覆盖");
                }
                else
                {
                    PmapDocument document = new TmapConverter(file, catalog, diagnostics).Convert();
                    if (!string.IsNullOrWhiteSpace(options.MapKey)) document.Key = options.MapKey;
                    string xml = document.ToXml(); // 使用 PolarisMap 正式 PMAP 校验器
                    if (!diagnostics.HasErrors)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                        File.WriteAllText(output, xml, new UTF8Encoding(false));
                        Console.WriteLine($"Converted: {file} -> {output}");
                    }
                }
            }
            catch (Exception ex)
            {
                diagnostics.Error("TMAP9999", file, -1, $"{ex.GetType().Name}: {ex.Message}");
            }
            PrintDiagnostics(diagnostics.Items);
            if (diagnostics.HasErrors) failed++;
            if (diagnostics.HasWarnings) warnings++;
        }
        Console.WriteLine($"Done: {files.Count - failed}/{files.Count} converted, {failed} failed.");
        if (failed > 0) return 1;
        return options.Strict && warnings > 0 ? 3 : 0;
    }

    private static IReadOnlyList<string> ResolveInputs(string input, bool recursive)
    {
        if (File.Exists(input)) return new[] { input };
        return Directory.EnumerateFiles(input, "*.tmap", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(x => !x.EndsWith(".old.tmap", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ResolveOutput(string inputRoot, string inputFile, string? outputOption)
    {
        if (File.Exists(inputRoot))
        {
            if (string.IsNullOrWhiteSpace(outputOption)) return Path.ChangeExtension(inputFile, ".pmap");
            string output = Path.GetFullPath(outputOption);
            return Directory.Exists(output) ? Path.Combine(output, Path.GetFileNameWithoutExtension(inputFile) + ".pmap") : output;
        }
        string root = string.IsNullOrWhiteSpace(outputOption) ? Path.Combine(inputRoot, "pmap-out") : Path.GetFullPath(outputOption);
        string relative = Path.GetRelativePath(inputRoot, inputFile);
        return Path.Combine(root, Path.ChangeExtension(relative, ".pmap"));
    }

    private static void PrintDiagnostics(IEnumerable<ConversionDiagnostic> diagnostics)
    {
        foreach (ConversionDiagnostic diagnostic in diagnostics)
        {
            TextWriter writer = diagnostic.Severity == DiagnosticSeverity.Info ? Console.Out : Console.Error;
            writer.WriteLine(diagnostic);
        }
    }

    private static void PrintHelp() => Console.WriteLine("""
AicTmapToPmap - AliceInCradle 原版 TMAP v4 → PolarisMap PMAP 转换器

Usage:
  AicTmapToPmap <file.tmap> [-o file.pmap] [--key new_map_key] [--force]
  AicTmapToPmap <directory> [-o output-directory] [--recursive] [--force]

Options:
  -o, --output <path>   输出文件或目录
  -r, --recursive       递归扫描目录
  -f, --force           允许覆盖已有 PMAP（永远不会修改输入 TMAP）
  --game-root <path>    AliceInCradle 游戏根目录
  --chips <path>        显式指定 __m2d_chips.dat
  --key <map-key>       单文件转换时改用新 key（便于避开原版 key 的所有权保护）
  --strict              任何转换警告均返回退出码 3
  -h, --help            显示帮助

默认从 E:\Projects\Polaris\aic_path.txt 定位游戏。转换器只读取 TMAP 与芯片目录元数据；
PMAP 仅记录 PixelLiner 源路径和地图数据，不复制、导出或打包原版图片素材。
""");
}

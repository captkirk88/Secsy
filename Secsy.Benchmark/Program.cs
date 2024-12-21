using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Secsy.Testing;

internal class Program
{
    private static void Main(string[] args)
    {
        var config = new BenchmarkDotNet.Configs.ManualConfig();
        config.WithOption(ConfigOptions.LogBuildOutput, false);
        config.WithOption(ConfigOptions.LogBuildOutput, false);
        config.WithOption(ConfigOptions.JoinSummary, true);
        config.AddExporter(MarkdownExporter.GitHub, BenchmarkReportExporter.Default);
        config.AddColumnProvider(BenchmarkDotNet.Columns.DefaultColumnProviders.Instance);
        config.AddLogger(ConsoleLogger.Default);
        SummaryStyle style = new(null, true, Perfolizer.Metrology.SizeUnit.KB, null);
        config.WithSummaryStyle(style);

        var sum = BenchmarkRunner.Run<Benchmarks>(config);

        Console.WriteLine(sum.TotalTime);
    }
}
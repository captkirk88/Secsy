using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using ECS;
using ECS.Testing;

internal class Program
{
    private static void Main(string[] args)
    {
        var config = new BenchmarkDotNet.Configs.ManualConfig();
        config.WithOption(ConfigOptions.LogBuildOutput, false);
        config.WithOption(ConfigOptions.JoinSummary, false);
        config.AddExporter(MarkdownExporter.GitHub, BenchmarkReportExporter.Default, HtmlExporter.Default);
        config.AddColumnProvider(BenchmarkDotNet.Columns.DefaultColumnProviders.Instance);
        config.AddLogger(ConsoleLogger.Default);
        SummaryStyle style = new(null, true, Perfolizer.Metrology.SizeUnit.KB, null);
        config.WithSummaryStyle(style);

        BenchmarkRunner.Run<CreateEntitiesBenchmark>(config);
        BenchmarkRunner.Run<SystemsBenchmark>(config);

    }
}
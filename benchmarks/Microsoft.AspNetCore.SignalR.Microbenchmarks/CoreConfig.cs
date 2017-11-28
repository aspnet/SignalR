using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Validators;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks
{
    public class CoreConfig : ManualConfig
    {
        public CoreConfig() : this(Job.Core)
        {
            Add(JitOptimizationsValidator.FailOnError);
        }

        public CoreConfig(Job job)
        {
            Add(DefaultConfig.Instance);
            Add(MemoryDiagnoser.Default);
            Add(StatisticColumn.OperationsPerSecond);

            //Add(Job.Default
            //    .WithRemoveOutliers(false)
            //    .With(new GcMode() { Server = true })
            //    .With(RunStrategy.Throughput)
            //    .WithLaunchCount(3)
            //    .WithWarmupCount(5)
            //    .WithTargetCount(10));

            Add(job
                .With(RunStrategy.Throughput)
                .WithRemoveOutliers(false)
                .With(new GcMode() { Server = true })
                .WithLaunchCount(3)
                .WithWarmupCount(5)
                .WithTargetCount(10));
        }
    }
}

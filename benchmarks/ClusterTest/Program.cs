using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Proto;

namespace ClusterTest;

class Program
{
	private static async Task Main()
	{
		Log.SetLoggerFactory(
			LoggerFactory.Create(
				c =>
					c.SetMinimumLevel(LogLevel.Debug)
						.AddFilter("Microsoft", LogLevel.None)
						.AddFilter("Grpc", LogLevel.None)
						//.AddFilter("Proto.Remote.EndpointManager", LogLevel.Information)
						//.AddFilter("Proto.Remote.EndpointReader", LogLevel.Warning)
						//.AddFilter("Proto.Remote.ServerConnector", LogLevel.Warning)
						.AddFilter("Proto.Context.ActorContext", LogLevel.Information)
						.AddFilter("Proto.Cluster.Gossip", LogLevel.Information)
						.AddFilter("Proto.Cluster.MemberList", LogLevel.Warning)
						.AddFilter("Proto.Cluster.Partition", LogLevel.Information)
						.AddFilter("Proto.Cluster.Partition.PartitionIdentityActor", LogLevel.Error)
						.AddFilter("Proto.CustomDecorator", LogLevel.Information)
						.AddFilter("Proto.CustomDecorator.System", LogLevel.Information)
						.AddFilter("Proto.Cluster.DefaultClusterContext", LogLevel.Information)
						.AddFilter("Proto.Diagnostics.DiagnosticsStore", LogLevel.Warning)
						.AddSimpleConsole(o => o.SingleLine = true)
			)
		);

		var logger = Log.CreateLogger("Main");
		_ = SafeTask.Run(
			async () =>
			{
				while (true)
				{
					ThreadPool.GetAvailableThreads(out var availWorkerThreads, out var availCompletionPortThreads);
					ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var maxCompletionPortThreads);
					var pendingWorkItemCount = ThreadPool.PendingWorkItemCount;
					var threadCount = ThreadPool.ThreadCount;
					logger.LogDebug("Thread data: [{availWorkerThreads}, {availCompletionPortThreads}, {maxWorkerThreads}, {maxCompletionPortThreads}, {threadCount}, {pendingWorkItemCount}]",
						availWorkerThreads, availCompletionPortThreads, maxWorkerThreads, maxCompletionPortThreads, threadCount, pendingWorkItemCount);
					logger.LogInformation("Requests: {CreatedCount} {StartedCount} {HandledCount} {ErrorCount}", ClusterHelper.CreatedCount, ClusterHelper.StartedCount, ClusterHelper.HandledCount, ClusterHelper.ErrorCount);
					await Task.Delay(1000);
				}
			});
		
		var isClient = bool.Parse(Environment.GetEnvironmentVariable("PROTO_CLIENT", EnvironmentVariableTarget.Process));
	
		logger.LogInformation("Starting cluster");	
		var cluster = await ClusterHelper.CreateAsync(isClient);

		if (isClient)
		{
			var rand = Random.Shared.Next(10000);
			var parallelCount = 100;
			var repeatCount = 10;
			var tasks = new List<Task>();
			for (var i = 0; i < parallelCount; i++)
			{
				var i1 = i;
				tasks.Add(SafeTask.Run(async () =>
				{
					var j = 0;
					while (true)
					{
						j++;
						try
						{
							await cluster.GetEchoGrain($"echo-{rand}-{i1}").SendPing(new Ping(), CancellationToken.None);
						}
						catch (Exception ex)
						{
							Interlocked.Increment(ref ClusterHelper.ErrorCount);
						}
					}
				}));
			}
				
			await Task.WhenAll(tasks);
		}
		
		while (true)
		{
			await Task.Delay(1000);
		}
	}
}
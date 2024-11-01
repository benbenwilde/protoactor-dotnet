using System;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.Cluster;
using Proto.Cluster.Partition;
using Proto.Remote;
using Proto.Remote.GrpcNet;

namespace ClusterTest;

public static class ClusterHelper
{
	public static int CreatedCount;

	public static int StartedCount;

	public static int HandledCount;

	public static int ErrorCount;
	
	public static async Task<Cluster> CreateAsync(bool isClient)
	{
		var actorSystemConfig = new ActorSystemConfig()
			.WithDeadLetterRequestLogging(true)
			.WithDeveloperThreadPoolStatsLogging(true)
			.WithDeveloperSupervisionLogging(true)
			.WithConfigureProps(props => props
				.WithStartDeadline(TimeSpan.FromMilliseconds(1000))
				.WithChildSupervisorStrategy(new CustomExponentialBackoffStrategy())
				.WithContextDecorator(ctx => new CustomActorContextDecorator(ctx, Log.CreateLogger("Proto.CustomDecorator"))))
			.WithConfigureSystemProps((_, props) => props
				.WithGuardianSupervisorStrategy(Supervision.AlwaysRestartStrategy)
				.WithChildSupervisorStrategy(new CustomExponentialBackoffStrategy())
				.WithContextDecorator(ctx => new CustomActorContextDecorator(ctx, Log.CreateLogger("Proto.CustomDecorator.System"))));

		var kind = Environment.GetEnvironmentVariable("PROTO_KIND", EnvironmentVariableTarget.Process);
		
		var kinds = kind == "echo"
			? new[] { new ClusterKind(nameof(EchoGrain), Props.FromProducer(() => new EchoGrainActor((cc, id) => new EchoGrain(cc)))) }
			: null;
		
		var ns = Environment.GetEnvironmentVariable("POD_NAMESPACE", EnvironmentVariableTarget.Process)
		         ?? throw new InvalidOperationException("POD_NAMESPACE environment variable not set");
		var serviceName = Environment.GetEnvironmentVariable("POD_SERVICENAME", EnvironmentVariableTarget.Process)
		                  ?? throw new InvalidOperationException("POD_SERVICENAME environment variable not set");
		var ipDns = Environment.GetEnvironmentVariable("POD_IP", EnvironmentVariableTarget.Process)?.Replace('.', '-')
		            ?? throw new InvalidOperationException("POD_IP environment variable not set");
		
		var hostname = $"{ipDns}.{serviceName}-proto.{ns}.svc.cluster.local";
		Log.CreateLogger("ClusterHelper").LogInformation("Using hostname {Hostname}", hostname);

		var remoteConfig = GrpcNetRemoteConfig
			.BindToAllInterfaces(hostname, 4020)
			.WithLogLevelForDeserializationErrors(LogLevel.Error)
			.WithProtoMessages(MessagesReflection.Descriptor);
			// with
			// {
			// 	UseHttps = true,
			// 	ConfigureKestrel = listenOptions =>
			// 	{
			// 		listenOptions.Protocols = HttpProtocols.Http2;
			// 		listenOptions.UseHttps(o =>
			// 		{
			// 			var cert = X509Certificate2.CreateFromPemFile("./tls.crt", "./tls.key");
			// 			o.ServerCertificateSelector = (_, _) => cert;
			// 			o.SslProtocols = SslProtocols.Tls12;
			// 		});
			// 	}
			// };

		var clusterConfig = ClusterConfig.Setup("test-proto", new CustomClusterProvider(), new PartitionIdentityLookup(new PartitionConfig {DeveloperLogging = false}))
			.WithActorRequestTimeout(TimeSpan.FromSeconds(20))
			.WithExitOnShutdown()
			.WithClusterContextProducer(cluster1 => new ClusterContextWrapper(new DefaultClusterContext(cluster1)))
			.WithClusterKinds(kinds ?? []);
		
		var actorSystem = new ActorSystem(actorSystemConfig)
			.WithRemote(remoteConfig)
			.WithCluster(clusterConfig);
		
		var cluster = actorSystem.Cluster();

		await (isClient 
			? cluster.StartClientAsync() 
			: cluster.StartMemberAsync());
		
		return cluster;
	}
}
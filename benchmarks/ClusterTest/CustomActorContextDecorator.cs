using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.Cluster;
using Proto.Extensions;

namespace ClusterTest;

public class CustomActorContextDecorator : ActorContextDecorator
{
	private readonly ILogger _logger;

	public CustomActorContextDecorator(IContext context, ILogger logger) : base(context)
	{
		_logger = logger;
	}

	private string ActorType => Actor?.GetType().Name ?? "None";
	
	public override async Task Receive(MessageEnvelope envelope)
	{
		var message = envelope.Message;
		try
		{
			if (message is InfrastructureMessage)
			{
				await base.Receive(envelope).ConfigureAwait(false);
				return;
			}
			
			var actorType = ActorType;
			if (actorType != "FunctionActor" && actorType != "PubSubMemberDeliveryActor" && actorType != "TopicActor")
			{
				_logger.Log(LogLevel.Debug, "Actor {ActorId} {ActorType} got message {MessageType}", Self.ToString(), actorType, message.GetMessageTypeName());
			}
			
			var sw = Stopwatch.StartNew();
			var task = base.Receive(envelope);
			await Task.WhenAny(Task.Delay(5000), task);
			
			if (task.IsCompleted)
			{
				await task.ConfigureAwait(false);
				if (actorType != "FunctionActor" && actorType != "PubSubMemberDeliveryActor" && actorType != "TopicActor")
				{
					_logger.Log(LogLevel.Debug, "Actor {ActorId} {ActorType} completed message {MessageType}", Self.ToString(), actorType, message.GetMessageTypeName());
				}
				
				return;
			}
			
			_logger.Log(LogLevel.Warning, "Actor {ActorId} {ActorType} long running message more than 5 seconds for message {MessageType}", Self.ToString(), ActorType, message.GetMessageTypeName());
			
			await task.ConfigureAwait(false);
			_logger.Log(LogLevel.Warning, "Actor {ActorId} {ActorType} completed long running message after {Seconds:N2} for message {MessageType}", Self.ToString(), ActorType, sw.Elapsed.TotalSeconds, message.GetMessageTypeName());
		}
		catch (Exception x)
		{
			_logger.Log(LogLevel.Error, x, "Actor {ActorId} {ActorType} failed during message {MessageType}", Self.ToString(), ActorType, message.GetMessageTypeName());
			throw;
		}
	}
	
	public override async Task<T> RequestAsync<T>(PID target, object message, CancellationToken cancellationToken)
	{
		try
		{
			if (message is InfrastructureMessage)
			{
				return await base.RequestAsync<T>(target, message, cancellationToken).ConfigureAwait(false);
			}
			
			var envelope = message as MessageEnvelope ?? MessageEnvelope.Wrap(message);
			
			//logger.Log(LogLevel.Debug, "Actor {ActorId} {ActorType} Sending RequestAsync {MessageType}", Self.ToString(), ActorType, message.GetMessageTypeName());
			var response = await base.RequestAsync<T>(target, envelope, cancellationToken).ConfigureAwait(false);
			//logger.Log(LogLevel.Debug, "Actor {ActorId} {ActorType} Got response {ResponseType} for {MessageType}", Self.ToString(), ActorType, response.GetMessageTypeName(), message.GetMessageTypeName());
			return response;
		}
		catch (Exception x)
		{
			//logger.Log(LogLevel.Error, x, "Actor {ActorId} {ActorType} Got exception waiting for RequestAsync response of {MessageType}", Self.ToString(), ActorType, message.GetMessageTypeName());
			throw;
		}
	}
	
	public override void Request(PID target, object message, PID? sender)
	{
		if (message is InfrastructureMessage)
		{
			base.Request(target, message, sender);
			return;
		}
		
		var envelope = message as MessageEnvelope ?? MessageEnvelope.Wrap(message);
		
		//logger.Log(LogLevel.Debug, "Actor {ActorId} {ActorType} Sending Request {MessageType}", Self.ToString(), ActorType, message.GetMessageTypeName());
		base.Request(target, envelope, sender);
	}
	
	public override void Send(PID target, object message)
	{
		if (message is InfrastructureMessage)
		{
			base.Send(target, message);
			return;
		}
		
		var envelope = message as MessageEnvelope ?? MessageEnvelope.Wrap(message);
		
		//logger.Log(LogLevel.Debug, "Actor {ActorId} {ActorType} Sending {MessageType}", Self.ToString(), ActorType, message.GetMessageTypeName());
		base.Send(target, envelope);
	}
	
	// public override PID SpawnNamed(Props props, string name, Action<IContext>? callback = null)
	// {
	// 	var id = context.Get<ClusterIdentity>();
	// 	try
	// 	{
	// 		var pid = base.SpawnNamed(props, name, callback);
	// 		logger.Log(LogLevel.Debug, "Actor {ActorId} {ActorType} Spawned child actor {Name}", Self.ToString(), ActorType, name);
	// 		return pid;
	// 	}
	// 	catch (Exception x)
	// 	{
	// 		logger.Log(LogLevel.Error, x, "Actor {ActorId} {ActorType} failed when spawning child actor {Name}", Self.ToString(), ActorType, name);
	// 		throw;
	// 	}
	// }
}

public class Nugget
{
	public string Name { get; set; } = string.Empty;
}

public class CustomExponentialBackoffStrategy : ISupervisorStrategy
{
	private readonly TimeSpan _oneHour = TimeSpan.FromHours(1);
	private readonly Random _random = new();

	public void HandleFailure(
		ISupervisor supervisor,
		PID child,
		RestartStatistics rs,
		Exception reason,
		object? message
	)
	{
		// if no failures for an hour, reset the failure count
		if (rs.NumberOfFailures(_oneHour) == 0)
		{
			rs.Reset();
		}

		// limit the delay
		var fails = Math.Min(rs.FailureCount, 10);

		// 1, 2, 4, 8, 16, 32, 64, 128, 256, 512 (>8 min)
		// 7 bit - 256 seconds (4.26 min) max delay before offset (2^8 = 256s)
		// Examples of backoff calculation:
		// - 0 failures: 2^0 = 1 second
		// - 1 failure:  2^1 = 2 seconds
		// - 2 failures: 2^2 = 4 seconds
		// - 3 failures: 2^3 = 8 seconds
		// - 4 failures: 2^4 = 16 seconds
		// - 5 failures: 2^5 = 32 seconds
		// - 6 failures: 2^6 = 64 seconds (~1.07 min, range: 1.07 to 1.34 min)
		// - 7 failures: 2^7 = 128 seconds (~2.13 min, range: 2.13 to 2.66 min)
		// - 8 failures: 2^8 = 256 seconds (~4.26 min, range: 4.26 to 5.33 min)
		// - 9 failures: 2^9 = 512 seconds (~8.53 min, range: 8.53 to 10.66 min)
		// - 10 failures: 2^10 = 1024 seconds (~17.07 min, range: 17 to 21.25 min)
		// - 11 failures: 2^11 = 2048 seconds (~34.13 min, range: 34.13 to 42.66 min)
		var backoff = 1 << fails;
		rs.Fail();

		// randomly increase duration up to 25% 
		var duration = TimeSpan.FromSeconds(backoff * (_random.NextDouble() * .25 + 1));

		// restart actor after the duration
		Task.Delay(duration).ContinueWith(_ => supervisor.RestartChildren(reason, child));
	}
}

public static class CancellationTokenExtensions
{
	public static CancellationTokenSource ToSourceWithTimeout(this CancellationToken token, TimeSpan timeout)
	{
		var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
		cts.CancelAfter(timeout);
		return cts;
	}
}

public class ClusterContextWrapper(IClusterContext baseContext) : IClusterContext
{
	public async Task<T?> RequestAsync<T>(ClusterIdentity clusterIdentity, object message, ISenderContext context, CancellationToken ct)
	{
		using var cts = ct.ToSourceWithTimeout(TimeSpan.FromSeconds(20));
		if (typeof(T) == typeof(object))
		{
			return await baseContext.RequestAsync<T>(clusterIdentity, message, context, cts.Token);
		}
		
		var result = await baseContext.RequestAsync<Proto.MessageEnvelope>(clusterIdentity, message, context, cts.Token);
		if (result == null) return default;
		return result.Message switch
		{
			T t => t,
			_ => throw new InvalidOperationException("Unexpected message type")
			{
				Data =
				{
					["ActualType"] = result.Message.GetType().FullName,
					["ExpectedType"] = typeof(T).FullName
				}
			}
		};
	}
}
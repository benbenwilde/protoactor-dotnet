using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Proto;

namespace ClusterTest;

public class EchoGrain : EchoGrainBase
{
	private readonly ILogger _logger = Log.CreateLogger("EchoGrain");

	public EchoGrain(IContext context) : base(context)
	{
		Interlocked.Increment(ref ClusterHelper.CreatedCount);
	}

	public override async Task OnStarted()
	{
		Context.SetReceiveTimeout(TimeSpan.FromMinutes(30));
		Interlocked.Increment(ref ClusterHelper.StartedCount);
		await Task.Delay(150);
	}

	public override async Task<Pong> SendPing(Ping request)
	{
		Interlocked.Increment(ref ClusterHelper.HandledCount);
		await Task.Delay(150);
		return new Pong();
	}
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.Cluster;

namespace ClusterTest;

public class CustomClusterProvider() : IClusterProvider
{
	private MemberObj _member;
	public Cluster Cluster { get; set; }

	public async Task StartMemberAsync(Cluster cluster)
	{
		Cluster = cluster;
		var (host, port) = cluster.System.GetAddress();
		_member = new MemberObj
		{
			Host = host,
			Port = port,
			Id = cluster.System.Id,
			Kinds = cluster.GetClusterKinds(),
		};

		StartTtlLoop();
		StartLoop();
	}

	public async Task StartClientAsync(Cluster cluster)
	{
		Cluster = cluster;
		StartLoop();
	}

	private void StartTtlLoop()
	{
		var logger = Log.CreateLogger("Provider");
		logger.LogInformation("Starting TTL loop");
		_ = SafeTask.Run(async () =>
		{
			while (!Cluster.MemberList.Stopping)
			{
				try
				{
					_member.Timestamp = DateTime.UtcNow;
					await File.WriteAllTextAsync($"/app/members/{Cluster.System.Id}.json", System.Text.Json.JsonSerializer.Serialize(_member));
				}
				catch (Exception x)
				{
					logger.LogInformation("ERROR: {x}", x);
				}

				await Task.Delay(3000);
			}
		});
	}
	
	private void StartLoop()
	{
		var logger = Log.CreateLogger("Provider");
		logger.LogInformation("Starting loop");
		_ = SafeTask.Run(async () =>
		{
			while (!Cluster.MemberList.Stopping)
			{
				try
				{
					var members = new List<Member>();
					foreach (var file in Directory.GetFiles("/app/members").Where(f => f.EndsWith(".json")))
					{
						var member = System.Text.Json.JsonSerializer.Deserialize<MemberObj>(await File.ReadAllTextAsync(file));
						if (member.Timestamp.AddSeconds(15) < DateTime.UtcNow)
						{
							File.Delete(file);
							continue;
						}
						
						members.Add(new Member
						{
							Id = member.Id,
							Host = member.Host,
							Port = member.Port,
							Kinds = {member.Kinds}
						});
					}

					Cluster.MemberList.UpdateClusterTopology(members);
				}
				catch (Exception x)
				{
					logger.LogInformation("ERROR: {x}", x);
				}

				await Task.Delay(1000);
			}
		});
	}

	public async Task ShutdownAsync(bool graceful)
	{
		File.Delete($"/app/members/{Cluster.System.Id}.json");
	}

	private class MemberObj
	{
		public string Id { get; set; }
		public string Host { get; set; }
		public int Port { get; set; }
		public string[] Kinds { get; set; }
		public DateTime Timestamp { get; set; }
	}
}
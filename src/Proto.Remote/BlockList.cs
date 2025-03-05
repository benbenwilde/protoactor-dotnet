// -----------------------------------------------------------------------
// <copyright file="BlockList.cs" company="Asynkron AB">
//      Copyright (C) 2015-2024 Asynkron AB All rights reserved
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Proto.Remote;

public record MemberBlocked(string MemberId, string Reason);

/// <summary>
///     <see cref="BlockList" /> contains all members that have been blocked from communication, e.g. due to
///     unresponsiveness. Entries on this list expire after <see cref="ActorSystemConfig.BlockedMemberDuration"/>,
///     defaults to 1 hour.
/// </summary>
public class BlockList
{
    private readonly object _lock = new();
    private readonly ActorSystem _system;

    private ImmutableDictionary<string, DateTime> _blockedMembers = ImmutableDictionary<string, DateTime>.Empty;

    public BlockList(ActorSystem system)
    {
        _system = system;
    }

    /// <summary>
    ///     List of all blocked members ids (their <see cref="ActorSystem.Id" />). Entries on this list expire after
    ///     <see cref="ActorSystemConfig.BlockedMemberDuration"/>, defaults to 1 hour.
    /// </summary>
    public ImmutableHashSet<string> BlockedMembers => _blockedMembers
        .Where(kvp => kvp.Value > DateTime.UtcNow.Add(-_system.Config.BlockedMemberDuration))
        .Select(kvp => kvp.Key)
        .ToImmutableHashSet();

    internal void Block(IEnumerable<string> memberIds, string reason)
    {
        lock (_lock)
        {
            var newIds = memberIds.ToHashSet().Except(_blockedMembers.Keys.ToHashSet());
          
            foreach (var member in newIds)
            {
                _blockedMembers = _blockedMembers.ContainsKey(member) switch
                {
                    false => _blockedMembers.Add(member, DateTime.UtcNow),
                    _     => _blockedMembers.SetItem(member, DateTime.UtcNow)
                };

                _system.EventStream.Publish(new MemberBlocked(member, reason));
            }
        }
    }

    /// <summary>
    ///     Checks if the specified member is blocked. A blocked member will remain blocked for
    ///     <see cref="ActorSystemConfig.BlockedMemberDuration"/>, defaults to 1 hour.
    /// </summary>
    /// <param name="memberId">Member id - same as member's <see cref="ActorSystem.Id" /></param>
    /// <returns></returns>
    public bool IsBlocked(string memberId) => BlockedMembers.Contains(memberId);
}
using System;

namespace Proto.Cluster;

#pragma warning disable RCS1194
public class GrainException : Exception
{
	public GrainException(string message, string? code = null) : base(message)
	{
		Code = code;
	}

	public string? Code { get; }
}
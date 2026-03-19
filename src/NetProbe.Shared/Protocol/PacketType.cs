namespace NetProbe.Shared.Protocol;

/// <summary>
/// Identifies the type of a NetProbe packet.
/// </summary>
public enum PacketType : byte
{
    /// <summary>Client-to-server probe packet.</summary>
    Probe = 0,

    /// <summary>Server-to-client echo response.</summary>
    Echo = 1,

    /// <summary>Reserved for future use.</summary>
    Reserved2 = 2,

    /// <summary>Reserved for future use.</summary>
    Reserved3 = 3,
}

namespace AbleKit.Tags;

/// <summary>
/// A folder's tag store changed while it was being read or written, most likely because Live was
/// writing it. Nothing was written; trying again fixes it.
/// </summary>
public sealed class TagStoreChangedException : IOException
{
    /// <summary>Creates one with a default message.</summary>
    public TagStoreChangedException()
        : base("The tag store changed while it was being read or written.") { }

    /// <summary>Creates one that says what changed.</summary>
    public TagStoreChangedException(string message)
        : base(message) { }

    /// <summary>Creates one that says what changed, with the exception that showed it.</summary>
    public TagStoreChangedException(string message, Exception innerException)
        : base(message, innerException) { }
}

namespace ExchangeMail.Core.Ews;

public class EwsException : Exception
{
    public EwsException(string message, string? responseCode = null, Exception? inner = null)
        : base(message, inner)
    {
        ResponseCode = responseCode;
    }

    /// <summary>EWS response code such as ErrorItemNotFound, if the server returned one.</summary>
    public string? ResponseCode { get; }
}

public sealed class EwsAuthenticationException(string message) : EwsException(message, "Unauthorized");

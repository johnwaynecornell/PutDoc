// Services/ConcurrencyException.cs
namespace PutDoc.Services;

public sealed class ConcurrencyException : Exception
{
    public ConcurrencyException(string message) : base(message) { }
}
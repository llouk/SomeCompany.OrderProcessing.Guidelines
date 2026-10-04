namespace SomeCompany.OrderProcessing.SharedKernel;

/// <summary>The category decides how the API layer maps the error (e.g. Validation → 400).</summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unavailable
}

public sealed record Error(ErrorType Type, string Code, string Message);

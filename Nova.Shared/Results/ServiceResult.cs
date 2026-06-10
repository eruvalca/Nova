using OneOf;

namespace Nova.Shared.Results;

/// <summary>
/// Represents a service operation result that is either successful with a value
/// or failed with a <see cref="ServiceProblem"/>.
/// </summary>
/// <typeparam name="TSuccess">The type of the successful result value.</typeparam>
[GenerateOneOf]
public partial class ServiceResult<TSuccess> : OneOfBase<TSuccess, ServiceProblem>
{
    /// <summary>
    /// Gets a value indicating whether the result represents a successful operation.
    /// </summary>
    public bool IsSuccess => IsT0;

    /// <summary>
    /// Gets a value indicating whether the result represents a failed operation.
    /// </summary>
    public bool IsProblem => IsT1;

    /// <summary>
    /// Gets the successful result value. Throws if the result is a problem.
    /// </summary>
    public new TSuccess Value => AsT0;

    /// <summary>
    /// Gets the problem. Throws if the result is successful.
    /// </summary>
    public ServiceProblem Problem => AsT1;
}

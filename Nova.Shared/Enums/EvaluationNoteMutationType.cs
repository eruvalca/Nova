namespace Nova.Shared.Enums;

/// <summary>
/// Identifies the kind of evaluation-note mutation durably recorded by a receipt.
/// </summary>
public enum EvaluationNoteMutationType
{
    /// <summary>
    /// Indicates that an evaluation note was added.
    /// </summary>
    Added = 0,

    /// <summary>
    /// Indicates that an evaluation note was edited.
    /// </summary>
    Edited = 1,
}

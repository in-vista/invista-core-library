using Newtonsoft.Json;

namespace GeeksCoreLibrary.Core.Models;

public class DeletedItemResult
{
    /// <summary>
    /// The amount of affected rows during the deletion.
    /// </summary>
    public int AffectedRows { get; set; }
    
    /// <summary>
    /// Indicates whether the deletion of the item was allowed.
    /// </summary>
    [JsonProperty("allow_deletion")]
    public bool AllowDeletion { get; set; }
    
    /// <summary>
    /// The error message that appears when the deletion was not allowed.
    /// </summary>
    [JsonProperty("error_message")]
    public string ErrorMessage { get; set; }

    private DeletedItemResult(int affectedRows, bool allowDeletion, string errorMessage = null)
    {
        AffectedRows = affectedRows;
        AllowDeletion = allowDeletion;
        ErrorMessage = errorMessage;
    }
    
    /// <summary>
    /// Creates a <see cref="DeletedItemResult"/> instance based on a successful execution.
    /// </summary>
    /// <returns>A <see cref="DeletedItemResult"/> where the deletion was allowed.</returns>
    public static DeletedItemResult FromSuccess(int affectedRows) => new DeletedItemResult(affectedRows, true);
    
    /// <summary>
    /// Creates a <see cref="DeletedItemResult"/> instance based on a failed execution with an error message.
    /// </summary>
    /// <param name="errorMessage">The error message that appears when the item was attempted to be deleted.</param>
    /// <returns>A <see cref="DeletedItemResult"/> where the deletion was disallowed.</returns>
    public static DeletedItemResult FromFailure(string errorMessage) => new DeletedItemResult(0, false, errorMessage);
}
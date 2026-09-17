using Newtonsoft.Json;

namespace GeeksCoreLibrary.Core.Models;

/// <summary>
/// A model containing error information.
/// </summary>
public class InvistaError
{
    /// <summary>
    /// The detailed error message to be returned to the client.
    /// </summary>
    [JsonProperty("message")]
    public string Message { get; set; }
    
    /// <summary>
    /// The title of the error, to be displayed inside a notification box.
    /// </summary>
    [JsonProperty("title")]
    public string Title { get; set; }

    public InvistaError(string message, string title)
    {
        Message = message;
        Title = title;
    }
    
    /// <summary>
    /// Implicitly converts a <see cref="string"/> value to a <see cref="InvistaError"/> instance.
    /// </summary>
    /// <param name="value">The message of the error.</param>
    /// <returns>A <see cref="InvistaError"/> instance with the <see cref="value"/> being the message of the error.</returns>
    public static implicit operator InvistaError(string value) => new InvistaError(value, null);
}
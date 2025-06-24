namespace GeeksCoreLibrary.Components.Account.Models;

public enum HttpFormType
{
    /// <summary>
    /// Indicates that the context HTTP request is not using a body.
    /// </summary>
    None = 0,
    
    /// <summary>
    /// Form type for <c>multipart/form-data</c>.
    /// </summary>
    MultipartFormData = 1,
    
    /// <summary>
    /// Form type for <c>application/x-www-form-urlencoded</c>.
    /// </summary>
    FormUrlEncodedContent = 2,
    
    /// <summary>
    /// Form type for a raw JSON string.
    /// </summary>
    RawJson = 3
}
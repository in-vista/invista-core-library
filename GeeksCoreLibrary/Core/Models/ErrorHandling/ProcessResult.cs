using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GeeksCoreLibrary.Core.Models.ErrorHandling;

/// <summary>
/// A generic model containing information on the result of a specific process.
/// </summary>
/// <typeparam name="T">The type of which this result holds the model.</typeparam>
public class ProcessResult<T>
{
    /// <summary>
    /// Indicates whether this process has been performed successfully.
    /// </summary>
    public bool Success { get; private set; }
    
    /// <summary>
    /// The model held by this result that was fed from the process.
    /// </summary>
    public T Model { get; private set; }
    
    /// <summary>
    /// A user-friendly message, which is only set if the result was unsuccessful.
    /// </summary>
    public string Message { get; private set; }
    
    /// <summary>
    /// An additional inner-exception to describe in-code where this process was unsuccessful.
    /// </summary>
    public Exception InnerException { get; private set; }
    
    /// <summary>
    /// The full <see cref="Exception"/> that combines the message of this instance with the inner-exception.
    /// </summary>
    public Exception Exception => new(Message, InnerException);
    
    /// <summary>
    /// The <see cref="IActionResult"/> response transformed from this process result.
    /// </summary>
    public IActionResult Response => Success
        ? new JsonResult(new { result = Model })
        : new ObjectResult(Exception) { StatusCode = StatusCodes.Status500InternalServerError };
    
    /// <summary>
    /// Constructor for the <see cref="ProcessResult{T}"/> class.
    /// </summary>
    /// <param name="success"><inheritdoc cref="Success"/></param>
    /// <param name="model"><inheritdoc cref="Model"/></param>
    /// <param name="message"><inheritdoc cref="Message"/></param>
    /// <param name="innerException"><inheritdoc cref="InnerException"/></param>
    private ProcessResult(bool success, T model, string message, Exception innerException)
    {
        Success = success;
        Model = model;
        Message = message;
        InnerException = innerException;
    }
    
    /// <summary>
    /// Creates a successful instance for the process result.
    /// </summary>
    /// <param name="model"><inheritdoc cref="Model"/></param>
    /// <returns>A <see cref="ProcessResult{T}"/> instance to indicate the success result of the performed process.</returns>
    public static ProcessResult<T> FromSuccess(T model) => new(true, model, null, null);
    
    /// <summary>
    /// Creates a successful instance for the process result.
    /// </summary>
    /// <param name="message"><inheritdoc cref="Message"/></param>
    /// <param name="innerException"><inheritdoc cref="InnerException"/></param>
    /// <returns>A <see cref="ProcessResult{T}"/> instance to indicate the unsuccessful result of the performed process.</returns>
    public static ProcessResult<T> FromFailure(string message, Exception innerException = null) => new(false, default, message, innerException);
}
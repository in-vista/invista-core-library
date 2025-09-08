using System;
using System.Threading.Tasks;
using GeeksCoreLibrary.Components.OrderProcess.Interfaces;
using GeeksCoreLibrary.Core.Extensions;
using GeeksCoreLibrary.Core.Models;
using GeeksCoreLibrary.Modules.GclReplacements.Extensions;
using GeeksCoreLibrary.Modules.Payments.Interfaces;
using GeeksCoreLibrary.Modules.Payments.PayNl.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using static GeeksCoreLibrary.Modules.Payments.PayNl.Services.PayNlService;

namespace GeeksCoreLibrary.Modules.Payments.Controllers;

[Area("PayNl")]
[Route("/payment-services/pay-nl")]
public class PayNlController : Controller
{
    private readonly GclSettings gclSettings;
    private readonly IOrderProcessesService orderProcessesService;
 
    public PayNlController(IOptions<GclSettings> gclSettings, IOrderProcessesService orderProcessesService)
    {
        this.gclSettings = gclSettings.Value;
        this.orderProcessesService = orderProcessesService;
    }
    
    /// <summary>
    /// Get URL for Pay. softpos app
    /// </summary>
    /// <returns>Softpos URL with variables, which can be used to process softpos transactions</returns>
    [HttpGet, Route("get-softpos-url")]
    public async Task<IActionResult> GetSoftposUrl([FromQuery] ulong paymentMethodId, [FromQuery] string returnUrl, [FromQuery] string exchangeUrl)
    {
        var paymentMethodIdInternal = paymentMethodId; //Convert.ToUInt64(paymentMethodId.DecryptWithAesWithSalt(withDateTime: false)); 
        
        if (paymentMethodIdInternal == 0)
        {
            return BadRequest(new { message = $"Invalid payment method '{paymentMethodIdInternal}'" });
        }
        
        var paymentMethodSettings = await orderProcessesService.GetPaymentMethodAsync(paymentMethodIdInternal);
        
        if (paymentMethodSettings == null)
        {
            return BadRequest(new { message = $"Invalid payment method '{paymentMethodIdInternal}'" });
        }

        // Add necessary querystrings to exchange URL if exchange URL is given
        if (!string.IsNullOrEmpty(exchangeUrl) && !exchangeUrl.Contains('?')) exchangeUrl = exchangeUrl.Contains('?') ? $"{exchangeUrl}&paymentMethodId={paymentMethodIdInternal}&reference={{invoiceNumber}}" : $"{exchangeUrl}?paymentMethodId={paymentMethodIdInternal}&reference={{invoiceNumber}}";

        try
        {
            var payNlSettings = (PayNlSettingsModel) paymentMethodSettings.PaymentServiceProvider;
            var result = HandleSoftPos(payNlSettings, "", 0, gclSettings, true, returnUrl, exchangeUrl);
            return Ok(new { softposurl = result});
        }
        catch (Exception e)
        {
            // Geeft statuscode 500 terug met een foutmelding
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Error getting softpos URL",
                details = e.Message
            });
        }
    }
}
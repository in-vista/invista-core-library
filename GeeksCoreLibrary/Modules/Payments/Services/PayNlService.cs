using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using GeeksCoreLibrary.Components.OrderProcess.Models;
using GeeksCoreLibrary.Components.ShoppingBasket;
using GeeksCoreLibrary.Components.ShoppingBasket.Interfaces;
using GeeksCoreLibrary.Core.DependencyInjection.Interfaces;
using GeeksCoreLibrary.Core.Enums;
using GeeksCoreLibrary.Core.Extensions;
using GeeksCoreLibrary.Core.Helpers;
using GeeksCoreLibrary.Core.Interfaces;
using GeeksCoreLibrary.Core.Models;
using GeeksCoreLibrary.Modules.Databases.Interfaces;
using GeeksCoreLibrary.Modules.GclReplacements.Extensions;
using GeeksCoreLibrary.Modules.Payments.Enums;
using GeeksCoreLibrary.Modules.Payments.Interfaces;
using GeeksCoreLibrary.Modules.Payments.Models;
using GeeksCoreLibrary.Modules.Payments.PayNl.Models;
using GeeksCoreLibrary.Modules.Payments.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using PayNlConstants = GeeksCoreLibrary.Modules.Payments.PayNl.Models.Constants;
using Constants = GeeksCoreLibrary.Components.OrderProcess.Models.Constants;

namespace GeeksCoreLibrary.Modules.Payments.PayNl.Services;

/// <inheritdoc cref="IPaymentServiceProviderService" />
public class PayNlService : PaymentServiceProviderBaseService, IPaymentServiceProviderService, IScopedService
{
    private const string BaseUrl = "https://connect.pay.nl";
    private readonly IDatabaseConnection databaseConnection;
    private readonly ILogger<PaymentServiceProviderBaseService> logger;
    private readonly IHttpContextAccessor? httpContextAccessor;
    private readonly GclSettings gclSettings;
    private IWiserItemsService wiserItemsService;
    private readonly IShoppingBasketsService shoppingBasketsService;

    public PayNlService(IDatabaseHelpersService databaseHelpersService,
        IDatabaseConnection databaseConnection,
        ILogger<PaymentServiceProviderBaseService> logger,
        IOptions<GclSettings> gclSettings,
        IShoppingBasketsService shoppingBasketsService,
        IWiserItemsService wiserItemsService,
        IHttpContextAccessor httpContextAccessor = null)
        : base(databaseHelpersService, databaseConnection, logger, httpContextAccessor)
    {
        this.databaseConnection = databaseConnection;
        this.logger = logger;
        this.httpContextAccessor = httpContextAccessor;
        this.gclSettings = gclSettings.Value;
        this.wiserItemsService = wiserItemsService;
        this.shoppingBasketsService = shoppingBasketsService;
    }

    /// <inheritdoc />
    public async Task<PaymentRequestResult> HandlePaymentRequestAsync(
        ICollection<(WiserItemModel Main, List<WiserItemModel> Lines)> conceptOrders,
        WiserItemModel userDetails,
        PaymentMethodSettingsModel paymentMethodSettings, string invoiceNumber)
    {
        var payNlSettings = (PayNlSettingsModel) paymentMethodSettings.PaymentServiceProvider;
        var validationResult = ValidatePayNlSettings(payNlSettings);
        if (!validationResult.Valid)
        {
            logger.LogError("Validation in 'HandlePaymentRequestAsync' of 'PayNlService' failed because: {Message}", validationResult.Message);
            return new PaymentRequestResult
            {
                Successful = false,
                Action = PaymentRequestActions.Redirect,
                ActionData = payNlSettings.FailUrl
            };
        }

        var totalPrice = await CalculatePriceAsync(conceptOrders);
        var error = string.Empty;
        
        
        if (paymentMethodSettings.ExternalName == "softpos")
        {
            var finalUrl = string.Empty;
            try
            {
                finalUrl = HandleSoftPos(payNlSettings, invoiceNumber, totalPrice);
                return new PaymentRequestResult
                {
                    Successful = true,
                    Action = PaymentRequestActions.ReturnUrl,
                    ActionData = finalUrl
                };
            }
            catch (Exception e)
            {
                error = e.ToString();
                return new PaymentRequestResult
                {
                    Action = PaymentRequestActions.Redirect,
                    ActionData = paymentMethodSettings.PaymentServiceProvider.FailUrl,
                    Successful = false,
                    ErrorMessage = e.Message
                };
            }
            finally
            {
                await AddLogEntryAsync(PaymentServiceProviders.PayNl, invoiceNumber, requestBody: $"softpos: {finalUrl}", error: error, isIncomingRequest: false);
            }
        }

        // Else: iDEAL
        RestResponse restResponse = null;
        JObject responseJson = null;

        // Build and execute payment request.
        var restRequest = new RestRequest("/v1/orders", Method.Post);
        restRequest = AddRequestHeaders(restRequest, payNlSettings);
        
        var requestBody = new PayNLOrderCreateRequestModel()
        {
            Amount = new Amount { Value = (int) Math.Round(totalPrice * 100) },
            ServiceId = payNlSettings.ServiceId,
            Description = $"Order #{invoiceNumber}",
            Reference = invoiceNumber.Replace("-","X"), // dash is not allowed
            ReturnUrl = payNlSettings.SuccessUrl,
            ExchangeUrl = payNlSettings.WebhookUrl,
            Integration = new Integration { Test = gclSettings.Environment.InList(Environments.Test, Environments.Development)},
            PaymentMethod = new PaymentMethod { Id = 10 }
        };
        restRequest.AddJsonBody(requestBody);
        
        try
        {
            var restClient = new RestClient(BaseUrl);
            restResponse = await restClient.ExecuteAsync(restRequest);
            responseJson = String.IsNullOrWhiteSpace(restResponse.Content) ? new JObject() : JObject.Parse(restResponse.Content);
            var responseSuccessful = restResponse.StatusCode == HttpStatusCode.Created;

            // Save transaction id, because we need it for the status update.
            foreach (var order in conceptOrders)
            {
                var transactionId = new WiserItemDetailModel()
                {
                    Key = "uniquePaymentNumber",
                    Value = responseJson["orderId"]?.ToString()
                };
                await wiserItemsService.SaveItemDetailAsync(transactionId, order.Main.Id, entityType: "ConceptOrder");
            }

            return new PaymentRequestResult
            {
                Successful = responseSuccessful,
                Action = PaymentRequestActions.Redirect,
                ActionData = responseSuccessful ? responseJson["links"]?["redirect"]?.ToString() : payNlSettings.FailUrl
            };
        }
        catch (Exception e)
        {
            error = e.ToString();
            return new PaymentRequestResult
            {
                Action = PaymentRequestActions.Redirect,
                ActionData = paymentMethodSettings.PaymentServiceProvider.FailUrl,
                Successful = false,
                ErrorMessage = e.Message
            };
        }
        finally
        {
            var resp = responseJson == null ? null : JsonConvert.SerializeObject(responseJson);
            await AddLogEntryAsync(PaymentServiceProviders.PayNl, invoiceNumber, requestBody: JsonConvert.SerializeObject(requestBody), responseBody: resp, error: error, isIncomingRequest: false);
        }
    }
    
    // Build and return URL for softpos payment
    private string HandleSoftPos(PayNlSettingsModel payNlSettings, string invoiceNumber, decimal totalPrice)
    {
        // Define the transaction and layout objects
        
        // Add the order id to the webhook URL, because the softpos exchange doesn't return it.
        //payNlSettings.WebhookUrl =  QueryHelpers.AddQueryString(payNlSettings.WebhookUrl, "object[orderId]", invoiceNumber);
        
        var transaction = new
        {
            serviceId = payNlSettings.ServiceId,
            description = $"Order #{invoiceNumber}",
            reference = invoiceNumber.Replace("-","X"), // dash is not allowed
            returnUrl = payNlSettings.WebhookUrl,
            //returnUrl = payNlSettings.SuccessUrl,
            //exchangeUrl = payNlSettings.WebhookUrl, --> succes url is payment_in geworden, zodat direct de status verwerkt wordt (status ophalen via API voor softpos transacties werkt niet)
            //notification: {"type":"email","recipient":"sandbox@pay.nl"},
            amount = new
            {
                value = (int) Math.Round(totalPrice * 100),
                currency = "EUR"
            },
            paymentMethod = new
            {
                id = "10"
            },
            integration = new
            {
                testMode = gclSettings.Environment.InList(Environments.Test, Environments.Development)
            }
        };

        var layout = new
        {
            icon = "data:image/png;base64,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" // Invista logo
        };

        // Serialize JSON parts
        var transactionJson = JsonConvert.SerializeObject(transaction);
        var layoutJson = JsonConvert.SerializeObject(layout);

        // Encode each parameter
        var authenticationEncoded = HttpUtility.UrlEncode(invoiceNumber);
        var transactionEncoded = HttpUtility.UrlEncode(transactionJson);
        var layoutEncoded = HttpUtility.UrlEncode(layoutJson);

        // Build final URL
        return $"paycpoc://?authentication={authenticationEncoded}&transaction={transactionEncoded}&layout={layoutEncoded}";
    }
    
    /// <inheritdoc />
    public async Task<StatusUpdateResult> ProcessStatusUpdateAsync(OrderProcessSettingsModel orderProcessSettings, PaymentMethodSettingsModel paymentMethodSettings)
    {
        if (httpContextAccessor?.HttpContext == null)
        {
            return new StatusUpdateResult
            {
                Successful = false,
                Status = "Error retrieving status: No HttpContext available."
            };
        }
        
        if (paymentMethodSettings.ExternalName ==  "softpos")
        {
            try
            {
                // Save Pay. transaction id
                //foreach (var order in conceptOrders)
                //{
                //    var transactionId = new WiserItemDetailModel()
                //    {
                //        Key = "uniquePaymentNumber",
                //        Value = HttpContextHelpers.GetRequestValue(httpContextAccessor?.HttpContext, "orderId")
                //    };
                //    await wiserItemsService.SaveItemDetailAsync(transactionId, order.Main.Id, entityType: "ConceptOrder");
                //}
                
                await LogIncomingPaymentActionAsync(PaymentServiceProviders.PayNl, GetInvoiceNumberFromRequest(), 0);
                var status = string.Empty;
                var url = HttpContextHelpers.GetOriginalRequestUri(httpContextAccessor.HttpContext).ToString();
               
                // Workaround for Pay. bug in return URL, double ? 
                // https://localhost:5001/directpaymentin.gcl?paymentMethodId=24314?statusAction=CHANGE&reference=276215X20250528112419
                if (url.Split(".gcl?")[1].Contains('?'))
                {
                    url = $"{url.Split(".gcl?")[0]}.gcl?{url.Split(".gcl?")[1].Replace("?","&")}";
                    
                }
                
                var uri = new Uri(url);
                var query = HttpUtility.ParseQueryString(uri.Query);
                status = query["statusAction"]; 

                return new StatusUpdateResult
                {
                    Successful = status == "PAID",
                    Status = status,
                    StatusCode = status == "PAID" ? 100 : -1
                };
            }
            catch (Exception e)
            {
                await LogIncomingPaymentActionAsync(PaymentServiceProviders.PayNl, GetInvoiceNumberFromRequest(), 0, error: e.Message);
                throw;
            }  
        }

        // Not softpos, iDEAL and other payment methods
        var payNlTransactionId = string.Empty;

        try
        {
            payNlTransactionId = GetInvoiceNumberFromRequest();
          
            var payNlSettings = (PayNlSettingsModel) paymentMethodSettings.PaymentServiceProvider;
            var restClient = new RestClient(BaseUrl);
            var restRequest = new RestRequest($"/v1/orders/{payNlTransactionId}/status");
            restRequest = AddRequestHeaders(restRequest, payNlSettings);
            var restResponse = await restClient.ExecuteAsync(restRequest);
        
            if (restResponse.StatusCode != HttpStatusCode.OK || String.IsNullOrWhiteSpace(restResponse.Content))
            {
                await LogIncomingPaymentActionAsync(PaymentServiceProviders.PayNl, payNlTransactionId, (int) restResponse.StatusCode, responseBody: restResponse.Content);
                return new StatusUpdateResult
                {
                    Successful = false,
                    Status = "error"
                };
            }

            var responseJson = JObject.Parse(restResponse.Content);
            var invoiceNumber = responseJson["orderId"]?.ToString();
            await LogIncomingPaymentActionAsync(PaymentServiceProviders.PayNl, invoiceNumber, (int) restResponse.StatusCode, responseBody: restResponse.Content);
            return new StatusUpdateResult
            {
                Successful = responseJson["status"]?["code"]?.ToString() == "100",
                Status = responseJson["status"]?["action"]?.ToString(),
                StatusCode = Convert.ToInt32(responseJson["status"]?["code"])
            };
        }
        catch (Exception e)
        {
            await LogIncomingPaymentActionAsync(PaymentServiceProviders.PayNl, payNlTransactionId, 0, error: e.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<PaymentServiceProviderSettingsModel> GetProviderSettingsAsync(PaymentServiceProviderSettingsModel paymentServiceProviderSettings)
    {
        databaseConnection.AddParameter("id", paymentServiceProviderSettings.Id);

        var query = $@"SELECT
            payNlAccountCode.`value` AS payNlAccountCode,
            payNlToken.`value` AS payNlToken,
            payNlServiceId.`value` AS payNlServiceId
        FROM {WiserTableNames.WiserItem} AS paymentServiceProvider
        LEFT JOIN {WiserTableNames.WiserItemDetail} AS payNlAccountCode ON payNlAccountCode.item_id = paymentServiceProvider.id AND payNlAccountCode.`key` = '{PayNlConstants.PayNlAccountCodeProperty}'
        LEFT JOIN {WiserTableNames.WiserItemDetail} AS payNlToken ON payNlToken.item_id = paymentServiceProvider.id AND payNlToken.`key` = '{PayNlConstants.PayNlTokenProperty}'
        LEFT JOIN {WiserTableNames.WiserItemDetail} AS payNlServiceId ON payNlServiceId.item_id = paymentServiceProvider.id AND payNlServiceId.`key` = '{PayNlConstants.PayNlServiceIdProperty}'
        WHERE paymentServiceProvider.id = ?id
        AND paymentServiceProvider.entity_type = '{Constants.PaymentServiceProviderEntityType}'";

        var result = new PayNlSettingsModel
        {
            Id = paymentServiceProviderSettings.Id,
            Title = paymentServiceProviderSettings.Title,
            Type = paymentServiceProviderSettings.Type,
            LogAllRequests = paymentServiceProviderSettings.LogAllRequests,
            OrdersCanBeSetDirectlyToFinished = paymentServiceProviderSettings.OrdersCanBeSetDirectlyToFinished,
            SkipPaymentWhenOrderAmountEqualsZero = paymentServiceProviderSettings.SkipPaymentWhenOrderAmountEqualsZero,
            SuccessUrl = paymentServiceProviderSettings.SuccessUrl,
            PendingUrl = paymentServiceProviderSettings.PendingUrl,
            FailUrl = paymentServiceProviderSettings.FailUrl
        };

        var dataTable = await databaseConnection.GetAsync(query);
        if (dataTable.Rows.Count == 0)
        {
            return result;
        }

        var row = dataTable.Rows[0];
        result.AccountCode = row.GetAndDecryptSecretKey($"payNlAccountCode");
        result.Token = row.GetAndDecryptSecretKey($"payNlToken");
        result.ServiceId = row.GetAndDecryptSecretKey($"payNlServiceId");
        return result;
    }

    /// <inheritdoc />
    public string GetInvoiceNumberFromRequest()
    {
        var invoiceNumber = HttpContextHelpers.GetRequestValue(httpContextAccessor?.HttpContext, PayNlConstants.WebhookInvoiceNumberProperty);
        //if (string.IsNullOrEmpty(invoiceNumber)) // For softpos exchanges use order_id, the object[orderId] is for online payments like iDEAL
        //    invoiceNumber = HttpContextHelpers.GetRequestValue(httpContextAccessor?.HttpContext, "order_id"); --> Endpoint order:status werkt niet voor softpos transacties
        if (string.IsNullOrEmpty(invoiceNumber))
        {
            // Get the order by reference on the softpos return URL, because the ID of Pay. is unknown on this point.
            var url = HttpContextHelpers.GetOriginalRequestUri(httpContextAccessor.HttpContext).ToString();
               
            // Workaround for Pay. bug in return URL, double ? 
            // https://localhost:5001/directpaymentin.gcl?paymentMethodId=24314?statusAction=CHANGE&reference=276215X20250528112419
            if (url.Split(".gcl?")[1].Contains('?'))
            {
                url = $"{url.Split(".gcl?")[0]}.gcl?{url.Split(".gcl?")[1].Replace("?","&")}";
            }
                
            var uri = new Uri(url);
            var query = HttpUtility.ParseQueryString(uri.Query);
            invoiceNumber = query["reference"]; 
            
            if (!string.IsNullOrEmpty(invoiceNumber))
                invoiceNumber = invoiceNumber.Replace("X", "-"); // Replace the X by a dash, so we have the original unique payment number
        } 
        return invoiceNumber;
    }

    private async Task<decimal> CalculatePriceAsync(ICollection<(WiserItemModel Main, List<WiserItemModel> Lines)> conceptOrders)
    {
        var basketSettings = await shoppingBasketsService.GetSettingsAsync();

        var totalPrice = 0M;
        foreach (var (main, lines) in conceptOrders)
        {
            totalPrice += await shoppingBasketsService.GetPriceAsync(main, lines, basketSettings, ShoppingBasket.PriceTypes.PspPriceInVat);
        }

        return totalPrice;
    }

    private static (bool Valid, string Message) ValidatePayNlSettings(PayNlSettingsModel payNlSettings)
    {
        if (String.IsNullOrEmpty(payNlSettings.AccountCode) || String.IsNullOrEmpty(payNlSettings.Token))
        {
            return (false, "Pay. misconfigured: No account code or token set.");
        }

        if (payNlSettings.AccountCode.StartsWith("AT-") && String.IsNullOrEmpty(payNlSettings.ServiceId))
        {
            return (false, "Pay. misconfigured: Account code is an AT-code but no ServiceId is set.");
        }

        return (true, String.Empty);
    }



    private static RestRequest AddRequestHeaders(RestRequest restRequest, PayNlSettingsModel payNlSettings)
    {
        restRequest.AddHeader("authorization", $"Basic {$"{payNlSettings.AccountCode}:{payNlSettings.Token}".Base64()}");
        restRequest.AddHeader("accept", "application/json");
        return restRequest;
    }

}
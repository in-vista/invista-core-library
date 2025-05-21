using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using PayNlConstants = GeeksCoreLibrary.Modules.Payments.PayNl.Models.Constants;
using Constants = GeeksCoreLibrary.Components.OrderProcess.Models.Constants;

namespace GeeksCoreLibrary.Modules.Payments.PayNl.Services;

/// <inheritdoc cref="IPaymentServiceProviderService" />
public class PayNlService(
    IDatabaseHelpersService databaseHelpersService,
    IDatabaseConnection databaseConnection,
    ILogger<PaymentServiceProviderBaseService> logger,
    IOptions<GclSettings> gclSettings,
    IShoppingBasketsService shoppingBasketsService,
    IWiserItemsService wiserItemsService,
    IHttpContextAccessor httpContextAccessor = null)
    : PaymentServiceProviderBaseService(databaseHelpersService, databaseConnection, logger, httpContextAccessor), IPaymentServiceProviderService, IScopedService
{
    private const string BaseUrl = "https://rest-api.pay.nl/";
    private readonly IDatabaseConnection databaseConnection = databaseConnection;
    private readonly ILogger<PaymentServiceProviderBaseService> logger = logger;
    private readonly IHttpContextAccessor? httpContextAccessor = httpContextAccessor;
    private readonly GclSettings gclSettings = gclSettings.Value;
    private IWiserItemsService wiserItemsService = wiserItemsService;

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
        var error = String.Empty;
        RestResponse restResponse = null;
        JObject responseJson = null;

        // Build and execute payment request.
        var restRequest = new RestRequest("/v13/Transaction/start/json", Method.Post);
        restRequest = AddRequestHeaders(restRequest, payNlSettings);
        restRequest.AddParameter("serviceId", payNlSettings.ServiceId);
        restRequest.AddParameter("amount", (int) Math.Round(totalPrice * 100));
        restRequest.AddParameter("ipAddress", HttpContextHelpers.GetUserIpAddress(httpContextAccessor?.HttpContext));
        restRequest.AddParameter("finishUrl", payNlSettings.SuccessUrl);
        restRequest.AddParameter("testmode", (gclSettings.Environment.InList(Environments.Test, Environments.Development) ? 1 : 0));
        restRequest.AddParameter("paymentOptionId", 10);
        //restRequest.AddParameter("paymentOptionSubId", xxx);→ Bankkeuze bij iDEAL
        restRequest.AddParameter("transaction[description]", $"Order #{invoiceNumber}");
        restRequest.AddParameter("transaction[orderNumber]", invoiceNumber);
        restRequest.AddParameter("transaction[orderExchangeUrl]", payNlSettings.WebhookUrl);
        
        try
        {
            var restClient = new RestClient(BaseUrl);
            restResponse = await restClient.ExecuteAsync(restRequest);
            responseJson = String.IsNullOrWhiteSpace(restResponse.Content) ? new JObject() : JObject.Parse(restResponse.Content);
            var responseSuccessful = restResponse.StatusCode == HttpStatusCode.OK;

            // Save transaction id, because we need it for the status update.
            foreach (var order in conceptOrders)
            {
                var transactionId = new WiserItemDetailModel()
                {
                    Key = "PayNlTransactionId",
                    Value = responseJson["transaction"]?["transactionId"]?.ToString()
                };
                await wiserItemsService.SaveItemDetailAsync(transactionId, order.Main.Id, entityType: "ConceptOrder");
            }

            return new PaymentRequestResult
            {
                Successful = responseSuccessful,
                Action = PaymentRequestActions.Redirect,
                ActionData = responseSuccessful ? responseJson["transaction"]?["paymentURL"]?.ToString() : payNlSettings.FailUrl
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
            
            var parameters = restRequest.Parameters
                .Where(p => p.Type == ParameterType.RequestBody)
                .Select(p => $"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(p.Value?.ToString() ?? "")}");

            await AddLogEntryAsync(PaymentServiceProviders.PayNl, invoiceNumber, requestBody: string.Join("&", parameters), responseBody: resp, error: error, isIncomingRequest: false);
        }
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

        // The settings have been checked during transaction creation so we don't do so again
        var payNlSettings = (PayNlSettingsModel) paymentMethodSettings.PaymentServiceProvider;
        var restClient = new RestClient(BaseUrl);
        var payNlTransactionId = httpContextAccessor.HttpContext.Request.Query["order_id"];
        var restRequest = new RestRequest("/v13/Transaction/status/json");
        restRequest = AddRequestHeaders(restRequest, payNlSettings);
        restRequest.AddParameter("transactionId", payNlTransactionId);
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

        if (responseJson["request"]?["result"]?.ToString() == "0")
        {
            await LogIncomingPaymentActionAsync(PaymentServiceProviders.PayNl, payNlTransactionId, (int) restResponse.StatusCode, responseBody: restResponse.Content, error: $"ErrorId: {responseJson["request"]?["errorId"]}");
            return new StatusUpdateResult
            {
                Successful = false,
                Status = "error"
            }; 
        }
        
        var invoiceNumber = responseJson["paymentDetails"]?["orderId"]?.ToString();
        await LogIncomingPaymentActionAsync(PaymentServiceProviders.PayNl, invoiceNumber, (int) restResponse.StatusCode, responseBody: restResponse.Content);

        return new StatusUpdateResult
        {
            Successful = responseJson["paymentDetails"]?["state"]?.ToString() == "100",
            Status = responseJson["paymentDetails"]?["stateName"]?.ToString()
        };
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
        return HttpContextHelpers.GetRequestValue(httpContextAccessor?.HttpContext, PayNlConstants.WebhookInvoiceNumberProperty);
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
        restRequest.AddHeader("cache-control", "no-cache");
        restRequest.AddHeader("content-type", "application/x-www-form-urlencoded");
        return restRequest;
    }

}
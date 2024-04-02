using System;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using System.Threading;
using System.Threading.Tasks;
using GRP_Bot_Agent.Helpers;
using GRP_Bot_Agent.Models;
using GRP_Bot_Agent.Services;
using System.Text.RegularExpressions;
using Microsoft.Rest;
using Returns.Oms.Api.Client;
using Returns.Oms.Api.Client.Models;

namespace GRP_Bot_Agent.Dialogs;

public class LogInDialog : ComponentDialog
{
    private readonly StateService _stateService;

    public LogInDialog(string dialogId, StateService stateService)
        : base(dialogId)
    {
        _stateService = stateService;

        InitializeWaterfallDialog();
    }

    private void InitializeWaterfallDialog()
    { 
        var waterfallSteps = new WaterfallStep[]
        {
            AskTenantCodeStepAsync,
            AskOrderReferenceStepAsync,
            AskEmailStepAsync,
            PleaseWaitStepAsync,
            FinishLogInStepAsync,
        };

        AddDialog(new WaterfallDialog($"{nameof(LogInDialog)}.mainFlow", waterfallSteps));
        AddDialog(new TextPrompt($"{nameof(LogInDialog)}.tenantCode"));
        AddDialog(new TextPrompt($"{nameof(LogInDialog)}.orderReference"));
        AddDialog(new TextPrompt($"{nameof(LogInDialog)}.email", ValidateEmailAsync));

        InitialDialogId = $"{nameof(LogInDialog)}.mainFlow";
    }

    private async Task<DialogTurnResult> PleaseWaitStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
    {
        stepContext.Values[nameof(LogInData.EmailAddress)] = (string)stepContext.Result;

        var cluResult = (CluResponse)stepContext.Options;

        //simply display a message to the user and go to the next step
        await stepContext.Context.SendActivityAsync(MessageFactory.Text("Please wait while we are looking for your order..."), cancellationToken);
        await Task.Delay(1000, cancellationToken);
        return await stepContext.NextAsync(cluResult, cancellationToken);
    }

    private async Task<DialogTurnResult> AskTenantCodeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
    {
        var cluResult = (CluResponse)stepContext.Options;

        if (string.IsNullOrEmpty(cluResult?.Store))
        {
            return await stepContext.PromptAsync($"{nameof(LogInDialog)}.tenantCode",
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Please enter your order store."),
                }, cancellationToken);
        }

        return await stepContext.NextAsync(cluResult.Store, cancellationToken);
    }

    private async Task<DialogTurnResult> AskOrderReferenceStepAsync(WaterfallStepContext stepContext,
        CancellationToken cancellationToken)
    {
        stepContext.Values[nameof(LogInData.TenantCode)] = BrandHelper.GetTenantCode((string)stepContext.Result);

        var cluResult = (CluResponse)stepContext.Options;

        if (string.IsNullOrEmpty(cluResult?.OrderReference))
        {
            return await stepContext.PromptAsync($"{nameof(LogInDialog)}.orderReference",
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Please enter your order reference.")
                }, cancellationToken);
        }

        return await stepContext.NextAsync(cluResult.OrderReference, cancellationToken);
    }

    private async Task<DialogTurnResult> AskEmailStepAsync(WaterfallStepContext stepContext,
        CancellationToken cancellationToken)
    {
        stepContext.Values[nameof(LogInData.OrderReference)] = (string)stepContext.Result;

        var cluResult = (CluResponse)stepContext.Options;
        if (string.IsNullOrEmpty(cluResult?.EmailAddress))
        {
            return await stepContext.PromptAsync($"{nameof(LogInDialog)}.email",
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Please enter your email address."),
                    RetryPrompt = MessageFactory.Text("The value entered must be a valid email address."),
                }, cancellationToken);
        }

        return await stepContext.NextAsync(cluResult.EmailAddress, cancellationToken);
    }

    public Task<bool> ValidateEmailAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
    {
        var valid = false;

        if (promptContext.Recognized.Succeeded)
        {
            // Regular expression for validating email
            var emailRegex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
            
            valid = emailRegex.IsMatch(promptContext.Recognized.Value);
        }

        return Task.FromResult(valid);
    }

    private async Task<DialogTurnResult> FinishLogInStepAsync(WaterfallStepContext stepContext,
        CancellationToken cancellationToken)
    {
        LogInData logInData = await _stateService.LogInDataAccessor.GetAsync(stepContext.Context, () => new LogInData(), cancellationToken);
        logInData.TenantCode = (string)stepContext.Values[nameof(LogInData.TenantCode)];
        logInData.OrderReference = (string)stepContext.Values[nameof(LogInData.OrderReference)];
        logInData.EmailAddress = (string)stepContext.Values[nameof(LogInData.EmailAddress)];

        var cluResult = (CluResponse)stepContext.Options;

        //Invoke log in
        var token = await GetTokenAsync(logInData);
        if (string.IsNullOrEmpty(token))
        {
            return await GetErrorAsync(stepContext, cancellationToken);
        }

        //Invoke get order
        var order = await GetOrderAsync(logInData);
        if (order == null)
        {
            return await GetErrorAsync(stepContext, cancellationToken);
        }

        //Invoke brand country configuration
        var brandCountryConfiguration = await GetBrandCountryConfigurationAsync(logInData);
        if (brandCountryConfiguration == null)
        {
            return await GetErrorAsync(stepContext, cancellationToken);
        }

        await _stateService.LogInDataAccessor.SetAsync(stepContext.Context, logInData, cancellationToken);

        return await stepContext.EndDialogAsync(cluResult, cancellationToken);
    }

    private async Task<DialogTurnResult> GetErrorAsync(WaterfallStepContext stepContext,
        CancellationToken cancellationToken)
    {
        await stepContext.Context.SendActivityAsync(MessageFactory.Text("Sorry, we couldn't find your order. Please try again."), cancellationToken);
        return await stepContext.EndDialogAsync(null, cancellationToken);
    }

    private async Task<string> GetTokenAsync(LogInData logInData)
    {
        var client = new ReturnsOmsApi(new Uri(OmsConstants.ApiPath), new TokenCredentials("Bearer"));

        var result = await client.LogInWithHttpMessagesAsync(new ReturnsOmsApiUseCasesLogInLogInWebRequest
        {
            Email = logInData.EmailAddress,
            OrderReference = logInData.OrderReference,
            TenantCode = logInData.TenantCode
        });

        if (!result.Response.IsSuccessStatusCode)
        {
            return null;
        }

        var response =
            Newtonsoft.Json.JsonConvert
                .DeserializeObject<ReturnsOmsApiUseCasesLogInLogInWebResponse>(
                    await result.Response.Content.ReadAsStringAsync());

        logInData.AuthToken = response.Token;

        return response.Token;
    }

    private async Task<ReturnsOmsApiUseCasesCommonOrderWebResponse> GetOrderAsync(LogInData logInData)
    {
        var client = new ReturnsOmsApi(new Uri(OmsConstants.ApiPath), new TokenCredentials(logInData.AuthToken));
        var result = await client.GetOrderWithHttpMessagesAsync(logInData.TenantCode, logInData.OrderReference, "not-returned");

        if (!result.Response.IsSuccessStatusCode)
        {
            return null;
        }

        var response =
            Newtonsoft.Json.JsonConvert
                .DeserializeObject<ReturnsOmsApiUseCasesCommonOrderWebResponse>(
                    await result.Response.Content.ReadAsStringAsync());

        logInData.Order = response;

        return response;
    }

    private async Task<ReturnsOmsApplicationUseCasesGetCountryConfigurationGetCountryConfigurationResponse> GetBrandCountryConfigurationAsync(LogInData logInData)
    {
        var client = new ReturnsOmsApi(new Uri(OmsConstants.ApiPath), new TokenCredentials(logInData.AuthToken));
        var result = await client.GetCountryConfigurationWithHttpMessagesAsync(logInData.TenantCode, logInData.Order.CountryIso);
        if (!result.Response.IsSuccessStatusCode)
        {
            return null;
        }

        var response =
            Newtonsoft.Json.JsonConvert
                .DeserializeObject<ReturnsOmsApplicationUseCasesGetCountryConfigurationGetCountryConfigurationResponse>(
                    await result.Response.Content.ReadAsStringAsync());

        logInData.BrandCountryConfiguration = response;

        return response;
    }
}
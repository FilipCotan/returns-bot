using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using System.Threading;
using System.Threading.Tasks;
using GRP_Bot_Agent.Helpers;
using GRP_Bot_Agent.Models;
using GRP_Bot_Agent.Services;
using System.Text.RegularExpressions;
using AdaptiveCards;
using Microsoft.Rest;
using Returns.Oms.Api.Client;
using Returns.Oms.Api.Client.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Bot.Schema;

namespace GRP_Bot_Agent.Dialogs;

public class TrackReturnOrderDialog : ComponentDialog
{
    private readonly StateService _stateService;

    public TrackReturnOrderDialog(string dialogId, StateService stateService)
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
            AskTrackingReferenceStepAsync,
            DisplayTrackingStepAsync
        };

        AddDialog(new WaterfallDialog($"{nameof(LogInDialog)}.mainFlow", waterfallSteps));
        AddDialog(new TextPrompt($"{nameof(LogInDialog)}.tenantCode"));
        AddDialog(new TextPrompt($"{nameof(LogInDialog)}.orderReference"));
        AddDialog(new TextPrompt($"{nameof(LogInDialog)}.email", ValidateEmailAsync));
        AddDialog(new TextPrompt($"{nameof(LogInDialog)}.trackingReference"));

        InitialDialogId = $"{nameof(LogInDialog)}.mainFlow";
    }

  private async Task<DialogTurnResult> PleaseWaitStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
    {
        stepContext.Values[nameof(LogInData.EmailAddress)] = (string)stepContext.Result;

        return await stepContext.NextAsync(null, cancellationToken);
    }

    private async Task<DialogTurnResult> AskTenantCodeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
    {
        var logInData = await _stateService.LogInDataAccessor.GetAsync(stepContext.Context, () => new LogInData(), cancellationToken);
        if (logInData.TenantCode != null)
        {
            return await stepContext.NextAsync(logInData, cancellationToken);
        }

        return await stepContext.PromptAsync($"{nameof(LogInDialog)}.tenantCode",
            new PromptOptions
            {
                Prompt = MessageFactory.Text("Please enter your order store."),
            }, cancellationToken);
    }

    private async Task<DialogTurnResult> AskOrderReferenceStepAsync(WaterfallStepContext stepContext,
        CancellationToken cancellationToken)
    {
        var logInData = await _stateService.LogInDataAccessor.GetAsync(stepContext.Context, () => new LogInData(), cancellationToken);
        if (logInData.OrderReference != null)
        {
            return await stepContext.NextAsync(logInData.TenantCode, cancellationToken);
        }

        stepContext.Values[nameof(LogInData.TenantCode)] = BrandHelper.GetTenantCode((string)stepContext.Result);

        return await stepContext.PromptAsync($"{nameof(LogInDialog)}.orderReference",
            new PromptOptions
            {
                Prompt = MessageFactory.Text("Please enter your order reference.")
            }, cancellationToken);
    }
    
    private async Task<DialogTurnResult> AskEmailStepAsync(WaterfallStepContext stepContext,
        CancellationToken cancellationToken)
    {
        var logInData = await _stateService.LogInDataAccessor.GetAsync(stepContext.Context, () => new LogInData(), cancellationToken);
        if (logInData.EmailAddress != null)
        {
            return await stepContext.NextAsync(logInData.TenantCode, cancellationToken);
        }

        stepContext.Values[nameof(LogInData.OrderReference)] = (string)stepContext.Result;

        return await stepContext.PromptAsync($"{nameof(LogInDialog)}.email",
            new PromptOptions
            {
                Prompt = MessageFactory.Text("Please enter your email address."),
                RetryPrompt = MessageFactory.Text("The value entered must be a valid email address."),
            }, cancellationToken);
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

        if (logInData.TenantCode == null || logInData.OrderReference == null || logInData.EmailAddress == null)
        {
            logInData.TenantCode = (string)stepContext.Values[nameof(LogInData.TenantCode)];
            logInData.OrderReference = (string)stepContext.Values[nameof(LogInData.OrderReference)];
            logInData.EmailAddress = (string)stepContext.Values[nameof(LogInData.EmailAddress)];

            var token = await GetTokenAsync(logInData);
            if (string.IsNullOrEmpty(token))
            {
                return await GetErrorAsync(stepContext, cancellationToken);
            }
            
            await _stateService.LogInDataAccessor.SetAsync(stepContext.Context, logInData, cancellationToken);
        }

        return await stepContext.NextAsync(null, cancellationToken);
    }

    private async Task<DialogTurnResult> AskTrackingReferenceStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
    {
        var cluResult = (CluResponse)stepContext.Options;

        if (cluResult.ReturnOrderNumber != null)
        {
            return await stepContext.NextAsync(cluResult.ReturnOrderNumber, cancellationToken);
        }

        return await stepContext.PromptAsync($"{nameof(LogInDialog)}.trackingReference",
            new PromptOptions
            {
                Prompt = MessageFactory.Text("Please enter your return order number."),
            }, cancellationToken);
    }

    private async Task<DialogTurnResult> DisplayTrackingStepAsync(WaterfallStepContext stepContext,
        CancellationToken cancellationToken)
    {
        var cluResult = (CluResponse)stepContext.Options;

        var returnOrderNumber = (string)stepContext.Result;
        LogInData logInData = await _stateService.LogInDataAccessor.GetAsync(stepContext.Context, () => new LogInData(), cancellationToken);

        var trackingData = await GetTrackingDataAsync(logInData, returnOrderNumber);
        if (trackingData is null)
        {
            return await GetErrorAsync(stepContext, cancellationToken);
        }

        var trackingCard = CreateOrderTrackingCard(trackingData, returnOrderNumber);
        var reply = MessageFactory.Attachment(trackingCard);
        await stepContext.Context.SendActivityAsync(reply, cancellationToken);

        cluResult.IsComplete = trackingData.Milestones.Any(m => m.Code == "ReturnProcessed" && m.IsCurrentMilestone.GetValueOrDefault());
        

        return await stepContext.EndDialogAsync(cluResult, cancellationToken);
    }

    public Attachment CreateOrderTrackingCard(ReturnsOmsApplicationUseCasesGetTrackingEventsGetTrackingEventsResponse trackingInfo, string returnOrderNumber)
    {
        // Add progress bar based on milestones
        var milestones = new Dictionary<string, string>
        {
            { "ReturnCreated", "Created" },
            { "ReturnReceivedByCarrier", "Received by carrier" },
            { "ReturnReceived", "Received" },
            { "ReturnProcessed", "Processed" }
        };

        var card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 3))
        {
            Body = new List<AdaptiveElement>
            {
                new AdaptiveTextBlock { Text = $"Return {returnOrderNumber} shipping progress", Weight = AdaptiveTextWeight.Bolder, Size = AdaptiveTextSize.Large },
                new AdaptiveTextBlock { Text = $"Carrier: {trackingInfo.Carrier}", Weight = AdaptiveTextWeight.Bolder, Size = AdaptiveTextSize.Medium },
                new AdaptiveColumnSet
                {
                    Columns = new List<AdaptiveColumn>()
                }
            }
        };

        var returnWasProcessed  = trackingInfo.Milestones.Any(m => m.Code == "ReturnProcessed" && m.IsCurrentMilestone.GetValueOrDefault());

        foreach (var milestone in milestones)
        {
            var shippingMilestone = trackingInfo.Milestones.FirstOrDefault(m => m.Code == milestone.Key);

            var isCurrentMilestone = shippingMilestone is {IsCurrentMilestone: true};
            var fontWeight = isCurrentMilestone ? AdaptiveTextWeight.Bolder : AdaptiveTextWeight.Default;
            var imageUrl = shippingMilestone?.Location is null
                ? "https://cdn-icons-png.flaticon.com/512/5720/5720434.png"
                : "https://cdn-icons-png.flaticon.com/512/9426/9426997.png";

            if (returnWasProcessed)
            {
                imageUrl = "https://cdn-icons-png.flaticon.com/512/9426/9426997.png";
            }

            if (isCurrentMilestone)
            {
                imageUrl = "https://cdn-icons-png.flaticon.com/512/14035/14035769.png";
            }

            var column = new AdaptiveColumn
            {
                Width = "auto",
                Items = new List<AdaptiveElement>
                {
                    new AdaptiveImage
                    {
                        Url = new Uri(imageUrl),
                        Size = AdaptiveImageSize.Small,
                        Style = AdaptiveImageStyle.Person,
                        HorizontalAlignment = AdaptiveHorizontalAlignment.Center
                    },
                    new AdaptiveTextBlock
                    {
                        Text = milestone.Value,
                        Weight = fontWeight,
                        HorizontalAlignment = AdaptiveHorizontalAlignment.Center
                    }
                }
            };

            ((AdaptiveColumnSet)card.Body[2]).Columns.Add(column);
        }

        // Add list of events
        card.Body.Add(new AdaptiveTextBlock { Text = "Events", Weight = AdaptiveTextWeight.Bolder, Size = AdaptiveTextSize.Medium});
        foreach (var eventInfo in trackingInfo.Events)
        {
            card.Body.Add(new AdaptiveTextBlock
            {
                Text = $"{_trackingEventCodes[eventInfo.Code]} - {eventInfo.DateTime} - {eventInfo.Location}",
                Wrap = true
            });
        }

        return new Attachment
        {
            ContentType = AdaptiveCard.ContentType,
            Content = card
        };
    }

    private async Task<ReturnsOmsApplicationUseCasesGetTrackingEventsGetTrackingEventsResponse> GetTrackingDataAsync(LogInData logInData, string returnOrderNumber)
    {
        var client = new ReturnsOmsApi(new Uri(OmsConstants.ApiPath), new TokenCredentials(logInData.AuthToken));

        var result = await client.GetTrackingEventsWithHttpMessagesAsync(logInData.TenantCode,
            new ReturnsOmsApiUseCasesGetTrackingEventsGetTrackingEventsWebRequest
            {
                EmailAddress = logInData.EmailAddress,
                TrackingReference = long.Parse(returnOrderNumber)
            });

        if (!result.Response.IsSuccessStatusCode)
        {
            return null;
        }

        return
            Newtonsoft.Json.JsonConvert
                .DeserializeObject<ReturnsOmsApplicationUseCasesGetTrackingEventsGetTrackingEventsResponse>(
                    await result.Response.Content.ReadAsStringAsync());
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

    private readonly IDictionary<string, string> _trackingEventCodes = new Dictionary<string, string>
    {
        // add system codes here
        {"CODE1", "Return created"},
        {"CODE2", "Return arrived at return center"},
        {"CODE3", "Return processed"}
    };
}
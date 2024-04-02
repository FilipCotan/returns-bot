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
using Microsoft.Bot.Schema;
using Microsoft.Rest;
using Returns.Oms.Api.Client;
using Returns.Oms.Api.Client.Models;
using AdaptiveCards;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GRP_Bot_Agent.Dialogs;

public class CreateReturnDialog : ComponentDialog
{
    private readonly StateService _stateService;

    public CreateReturnDialog(string dialogId, StateService stateService)
        : base(dialogId)
    {
        _stateService = stateService;

        InitializeWaterfallDialog();
    }

    private void InitializeWaterfallDialog()
    { 
        var waterfallSteps = new WaterfallStep[]
        {
            AskOrderItemsSelectionStepAsync,
            AskForConfirmationStepAsync,
            SelectReturnMethodStepAsync,
            FinishCreateReturnStepAsync,
        };

        AddDialog(new WaterfallDialog($"{nameof(CreateReturnDialog)}.mainFlow", waterfallSteps));
        AddDialog(new TextPrompt($"{nameof(CreateReturnDialog)}.returnItems"));
        AddDialog(new ConfirmPrompt(nameof(ConfirmPrompt)));

        InitialDialogId = $"{nameof(CreateReturnDialog)}.mainFlow";
    }

    private async Task<DialogTurnResult> AskForConfirmationStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
    {
        var messageText = "Are you done selecting the items for return?";
        var promptMessage = MessageFactory.Text(messageText, messageText, InputHints.ExpectingInput);

        return await stepContext.PromptAsync(nameof(ConfirmPrompt), new PromptOptions { Prompt = promptMessage }, cancellationToken);
    }

    private async Task<DialogTurnResult> SelectReturnMethodStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
    {
        if (!(bool)stepContext.Result)
        {
            return await stepContext.ReplaceDialogAsync(InitialDialogId, null, cancellationToken);
        }

        var selectionsState = await _stateService.OrderItemSelectionsAccessor.GetAsync(stepContext.Context, () => new Dictionary<string, List<OrderItemSelection>>(), cancellationToken);

        if (!selectionsState.TryGetValue(stepContext.Context.Activity.From.Id, out var userItemsToReturn) || !userItemsToReturn.Any())
        {
            await stepContext.Context.SendActivityAsync(MessageFactory.Text("You have not selected any items for return."), cancellationToken);
            return await stepContext.ReplaceDialogAsync(InitialDialogId, null, cancellationToken);
        }

        var logInData = await _stateService.LogInDataAccessor.GetAsync(stepContext.Context, () => new LogInData(), cancellationToken);

        var availableShippingMethods =
            await GetAvailableReturnMethodsAsync(logInData, selectionsState, cancellationToken);

        await _stateService.ConversationState.SaveChangesAsync(stepContext.Context, false, cancellationToken);
        await _stateService.UserState.SaveChangesAsync(stepContext.Context, false, cancellationToken);

        if (availableShippingMethods == null)
        {
            await stepContext.Context.SendActivityAsync(MessageFactory.Text("There was an error while retrieving the available return methods."), cancellationToken);
            return await stepContext.ReplaceDialogAsync(InitialDialogId, null, cancellationToken);
        }

        var attachment = CreateShippingMethodCard(availableShippingMethods);
        var reply = MessageFactory.Attachment(attachment);
        reply.AttachmentLayout = AttachmentLayoutTypes.List;
        await stepContext.Context.SendActivityAsync(reply, cancellationToken);
        
        return EndOfTurn;
    }

    private async Task<ReturnsOmsApiUseCasesSearchReturnMethodsSearchReturnMethodsWebResponse> GetAvailableReturnMethodsAsync(
        LogInData logInData, 
        Dictionary<string, List<OrderItemSelection>> selectionsState, 
        CancellationToken cancellationToken)
    {
        var client = new ReturnsOmsApi(new Uri(OmsConstants.ApiPath), new TokenCredentials(logInData.AuthToken));
        var result = await client.SearchReturnMethodsWithHttpMessagesAsync(logInData.TenantCode,
            logInData.OrderReference, new ReturnsOmsApiUseCasesSearchReturnMethodsSearchReturnMethodsWebRequest
            {
                ReturnItems = selectionsState.SelectMany(kvp => kvp.Value).Select(selection =>
                    new ReturnsOmsApplicationUseCasesCommonReturnItemDto
                    {
                        Id = selection.ItemId
                    }).ToList()
            }, cancellationToken: cancellationToken);

        if (!result.Response.IsSuccessStatusCode)
        {
            return null;
        }

        var response =
            JsonConvert
                .DeserializeObject<ReturnsOmsApiUseCasesSearchReturnMethodsSearchReturnMethodsWebResponse>(
                    await result.Response.Content.ReadAsStringAsync(cancellationToken));

        logInData.AvailableReturnMethods = response;

        return response;
    }

    private async Task<DialogTurnResult> AskOrderItemsSelectionStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
    {
        await stepContext.Context.SendActivityAsync(MessageFactory.Text("Select the items you want to return."), cancellationToken);

        var logInData = await _stateService.LogInDataAccessor.GetAsync(stepContext.Context, () => new LogInData(), cancellationToken);
        var orderItemsAvailableForReturn = logInData.Order.Items.Where(item => item.AvailableForReturns ?? false).ToList();

        await DisplayItemsAsCarouselAsync(stepContext.Context, orderItemsAvailableForReturn, cancellationToken);

        // Move to the next step or end the dialog
        return await stepContext.NextAsync(null, cancellationToken);
    }

    public Attachment CreateShippingMethodCard(ReturnsOmsApiUseCasesSearchReturnMethodsSearchReturnMethodsWebResponse shippingMethods)
    {
        var adaptiveCard = new AdaptiveCard(new AdaptiveSchemaVersion(1, 3))
        {
            Body = new List<AdaptiveElement> {new AdaptiveTextBlock { Text = "Please select the return method for your return.", Height = AdaptiveHeight.Auto}}
        };

        foreach (var method in shippingMethods.ReturnMethods)
        {
            string displayText = $"{method.ReturnMethod} - {method.CarrierServiceRoute.CarrierName}";
            if (method.CarrierServiceRoute.IsPaperlessRoute.GetValueOrDefault())
            {
                displayText += " 🍃"; // Adding a leaf emoji as an icon for paperless routes
            }

            adaptiveCard.Actions.Add(new AdaptiveSubmitAction
            {
                Title = displayText,
                DataJson = JsonConvert.SerializeObject(new { action = "select_shipping_method", selectedMethod = method.CarrierServiceRoute.CarrierServiceRouteId })
            });
        }

        return new Attachment
        {
            ContentType = AdaptiveCard.ContentType,
            Content = adaptiveCard
        };
    }

    public async Task DisplayItemsAsCarouselAsync(ITurnContext turnContext,
        List<ReturnsOmsApplicationUseCasesCommonOrderResponseOrderItemDto> orderItems,
        CancellationToken cancellationToken)
    {
        var attachments = new List<Attachment>();
        var logInData = await _stateService.LogInDataAccessor.GetAsync(turnContext, () => new LogInData(), cancellationToken);

        var returnReasons = logInData.BrandCountryConfiguration.PortalSettings.CustomerReturnReasonCodes
            .Select(reason => new AdaptiveChoice {Title = reason.Description, Value = reason.Code})
            .ToList();

        foreach (var item in orderItems)
        {
            var adaptiveCard = new AdaptiveCard(new AdaptiveSchemaVersion(1, 3))
            {
                Body = new List<AdaptiveElement>
                {
                    new AdaptiveImage { Url = new Uri(item.ProductImageUrl), Size = AdaptiveImageSize.Auto },
                    new AdaptiveTextBlock { Text = item.ProductDescription, Weight = AdaptiveTextWeight.Bolder, Size = AdaptiveTextSize.Medium },
                    new AdaptiveTextBlock { Text = item.ProductCode, Wrap = true },
                    new AdaptiveTextBlock { Text = $"{item.UnitPrice.Amount} {item.UnitPrice.Currency}", Wrap = true },
                    new AdaptiveToggleInput
                    {
                        Id = "addToReturn",
                        Title = "Add to return",
                        ValueOn = "true",
                        ValueOff = "false",
                        Value = "false"  // Default value
                    },
                    new AdaptiveChoiceSetInput
                    {
                        Id = "returnReason",
                        Choices = returnReasons,
                        Style = AdaptiveChoiceInputStyle.Compact,
                        IsMultiSelect = false,
                        Placeholder = "Select a return reason"
                    }
                },
                Actions = new List<AdaptiveAction>
                {
                    new AdaptiveSubmitAction
                    {
                        Title = "Submit selection",
                        DataJson = JsonConvert.SerializeObject(new { action = "submit_selection", item_id = item.Id })
                    }
                }
            };

            var adaptiveCardAttachment = new Attachment
            {
                ContentType = AdaptiveCard.ContentType,
                Content = adaptiveCard
            };

            attachments.Add(adaptiveCardAttachment);
        }

        var reply = MessageFactory.Attachment(attachments);
        reply.AttachmentLayout = AttachmentLayoutTypes.Carousel;
        await turnContext.SendActivityAsync(reply, cancellationToken);
    }

    private async Task<DialogTurnResult> FinishCreateReturnStepAsync(WaterfallStepContext stepContext,
        CancellationToken cancellationToken)
    {
        var activity = stepContext.Context.Activity;

        // Check if the incoming activity is an Adaptive Card submission
        if (activity.Type == ActivityTypes.Message && activity.Value != null)
        {
            var submittedData = JsonConvert.DeserializeObject<JObject>(activity.Value.ToString());
            var action = submittedData["action"]?.ToString();

            if (action == "select_shipping_method")
            {
                var logInData = await _stateService.LogInDataAccessor.GetAsync(stepContext.Context, () => new LogInData(),
                    cancellationToken);
                var selectionsState = await _stateService.OrderItemSelectionsAccessor.GetAsync(stepContext.Context, () => new Dictionary<string, List<OrderItemSelection>>(), cancellationToken);


                var createdReturnOrder = await CreateReturnOrderAsync(logInData, selectionsState, cancellationToken);
                if (createdReturnOrder == null)
                {
                    await stepContext.Context.SendActivityAsync("Something went wrong while creating the return order. Please try again.", cancellationToken: cancellationToken);
                    return await stepContext.ReplaceDialogAsync($"{nameof(MainDialog)}.main", "What else can I do for you?", cancellationToken);
                }

                var returnSummaryCard = CreateReturnSummaryCard(logInData, selectionsState.First().Value, createdReturnOrder.ReturnOrderNumber);
                var reply = MessageFactory.Attachment(returnSummaryCard);
                await stepContext.Context.SendActivityAsync(reply, cancellationToken);

                var promptMessage = "What else can I do for you?";
                return await stepContext.ReplaceDialogAsync($"{nameof(MainDialog)}.main", promptMessage, cancellationToken);
            }

            // Handle other actions or unexpected submissions
            await stepContext.Context.SendActivityAsync("Unexpected action received. Please try again.", cancellationToken: cancellationToken);
        }

        return await stepContext.ReplaceDialogAsync($"{nameof(MainDialog)}.main", "What else can I do for you?", cancellationToken);
    }

    public Attachment CreateReturnSummaryCard(LogInData logInData, List<OrderItemSelection> selectionState, string returnOrderNumber)
    {
        var adaptiveCard = new AdaptiveCard(new AdaptiveSchemaVersion(1, 3))
        {
            Body = new List<AdaptiveElement>
            {
                new AdaptiveTextBlock { Text = "Your return order was successfully created!", Weight = AdaptiveTextWeight.Bolder, Size = AdaptiveTextSize.Large },
                new AdaptiveTextBlock { Text = $"Return Order Number: {returnOrderNumber}", Weight = AdaptiveTextWeight.Bolder, Size = AdaptiveTextSize.Medium },
                new AdaptiveTextBlock { Text = $"{logInData.Order.ShopperDetails.FirstName} {logInData.Order.ShopperDetails.LastName} - {logInData.Order.ShopperDetails.Address.CountryName}, {logInData.Order.ShopperDetails.Address.City}, {logInData.Order.ShopperDetails.Address.Address1}, {logInData.Order.ShopperDetails.Address.PostalCode}", Wrap = true },
            }
        };

        foreach (var item in selectionState)
        {
            var orderItem = logInData.Order.Items.FirstOrDefault(i => i.Id == item.ItemId);

            adaptiveCard.Body.Add(new AdaptiveColumnSet
            {
                Columns = new List<AdaptiveColumn>
                {
                    new()
                    {
                        Width = "auto",
                        Items = { new AdaptiveImage { Url = new Uri(orderItem.ProductImageUrl), Size = AdaptiveImageSize.Small } }
                    },
                    new()
                    {
                        Width = "stretch",
                        Items = { new AdaptiveTextBlock { Text = $"{orderItem.ProductDescription}. Return Reason: {logInData.BrandCountryConfiguration.PortalSettings.CustomerReturnReasonCodes.First(c => c.Code == item.ReturnReasonCode).Description}", Wrap = true } }
                    }
                }
            });
        }

        adaptiveCard.Body.Add(new AdaptiveTextBlock { Text = $"We sent an email to {logInData.EmailAddress} with instructions on how to proceed with your return. Thank you for using ESW's Bot Assistant.", Wrap = true });

        return new Attachment
        {
            ContentType = AdaptiveCard.ContentType,
            Content = adaptiveCard
        };
    }

    private async Task<ReturnsOmsApiUseCasesCreateReturnOrderCreateReturnOrderWebResponse> CreateReturnOrderAsync(
        LogInData logInData, Dictionary<string, 
            List<OrderItemSelection>> selectionsState, 
        CancellationToken cancellationToken)
    {
        var client = new ReturnsOmsApi(new Uri(OmsConstants.ApiPath), new TokenCredentials(logInData.AuthToken));
        var result = await client.CreateReturnOrderWithHttpMessagesAsync(logInData.TenantCode,
            logInData.OrderReference,
            new ReturnsOmsApiUseCasesCreateReturnOrderCreateReturnOrderWebRequest
            {
                CarrierIdentifier = logInData.SelectedReturnMethod.CarrierServiceRoute.EswCarrierIdentifier,
                ConsumerEmailAddress = logInData.Order.ShopperDetails.Email,
                CultureLanguageIso = logInData.Order.ShopperDetails.Locale,
                ShippingReference = logInData.Order.Items.First(i => i.Id == selectionsState.First().Value.First().ItemId).ShippingInformation.ShippingReference,
                IsPaperlessRoute = logInData.SelectedReturnMethod.CarrierServiceRoute.IsPaperlessRoute.GetValueOrDefault(),
                PaidBy = logInData.SelectedReturnMethod.PaidBy,
                ReturnMethod = logInData.SelectedReturnMethod.ReturnMethod,
                ReturnType = 1,
                ReturnItems = selectionsState.First().Value.Select(i => new ReturnsOmsApplicationUseCasesCreateReturnOrderReturnItemRequestDto
                {
                    Id = i.ItemId,
                    ReasonCode = i.ReturnReasonCode
                }).ToList()

            }, cancellationToken: cancellationToken);

        if (!result.Response.IsSuccessStatusCode)
        {
            return null;
        }

        return
            Newtonsoft.Json.JsonConvert
                .DeserializeObject<ReturnsOmsApiUseCasesCreateReturnOrderCreateReturnOrderWebResponse>(
                    await result.Response.Content.ReadAsStringAsync(cancellationToken));
    }
}
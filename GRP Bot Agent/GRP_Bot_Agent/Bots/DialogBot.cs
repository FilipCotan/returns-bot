// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
//
// Generated with Bot Builder V4 SDK Template for Visual Studio CoreBot v4.18.1

using System;
using System.Collections.Generic;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using GRP_Bot_Agent.Services;
using Newtonsoft.Json;
using System.IO;
using System.Linq;
using GRP_Bot_Agent.Models;
using Newtonsoft.Json.Linq;
using GRP_Bot_Agent.Dialogs;

namespace GRP_Bot_Agent.Bots;
// This IBot implementation can run any type of Dialog. The use of type parameterization is to allows multiple different bots
// to be run at different endpoints within the same project. This can be achieved by defining distinct Controller types
// each with dependency on distinct IBot types, this way ASP Dependency Injection can glue everything together without ambiguity.
// The ConversationState is used by the Dialog system. The UserState isn't, however, it might have been used in a Dialog implementation,
// and the requirement is that all BotState objects are saved at the end of a turn.
public class DialogBot<T> : ActivityHandler
    where T : Dialog
{
#pragma warning disable SA1401 // Fields should be private
    protected readonly Dialog Dialog;
    protected readonly ILogger Logger;
    protected readonly StateService StateService;
    private readonly DialogSet _dialogs;

#pragma warning restore SA1401 // Fields should be private

    public DialogBot(
        T dialog, 
        ILogger<DialogBot<T>> logger, 
        StateService stateService)
    {
        Dialog = dialog;
        Logger = logger;
        StateService = stateService ?? throw new ArgumentNullException(nameof(stateService));

        _dialogs = new DialogSet(StateService.ConversationState.CreateProperty<DialogState>("DialogState"));
        _dialogs.Add(new LogInDialog("LogInDialog", StateService));
    }

    protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
    {
        foreach (var member in membersAdded)
        {
            // Greet anyone that was not the target (recipient) of this message.
            // To learn more about Adaptive Cards, see https://aka.ms/msbot-adaptivecards for more details.
            if (member.Id != turnContext.Activity.Recipient.Id)
            {
                var welcomeCard = CreateAdaptiveCardAttachment();
                var response = MessageFactory.Attachment(welcomeCard, ssml: "Welcome to our returns bot agent!");
                await turnContext.SendActivityAsync(response, cancellationToken);

                await Dialog.RunAsync(turnContext, StateService.DialogStateAccessor, cancellationToken);
            }
        }
    }

    // Load attachment from embedded resource.
    private Attachment CreateAdaptiveCardAttachment()
    {
        var cardResourcePath = GetType().Assembly.GetManifestResourceNames().First(name => name.EndsWith("welcomeCard.json"));

        using var stream = GetType().Assembly.GetManifestResourceStream(cardResourcePath);
        using var reader = new StreamReader(stream);
        var adaptiveCard = reader.ReadToEnd();
        return new Attachment
        {
            ContentType = "application/vnd.microsoft.card.adaptive",
            Content = JsonConvert.DeserializeObject(adaptiveCard, new JsonSerializerSettings { MaxDepth = null }),
        };
    }

    public override async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default)
    {
        //handle order item selections. This can use some refactoring :D
        if (turnContext.Activity.Type == ActivityTypes.Message)
        {
            if (turnContext.Activity.Value != null)
            {
                // Parse the incoming data
                var submittedData = JsonConvert.DeserializeObject<JObject>(turnContext.Activity.Value.ToString());
                var action = submittedData["action"]?.ToString();
                var itemId = submittedData["item_id"]?.ToString();
                var returnReason = submittedData["returnReason"]?.ToString();
                var addToReturn = submittedData["addToReturn"]?.ToString();

                if (action == "submit_selection")
                {
                    var selectionsState = await StateService.OrderItemSelectionsAccessor.GetAsync(turnContext, () => new Dictionary<string, List<OrderItemSelection>>(), cancellationToken);

                    if (addToReturn == "true")
                    {
                        if (selectionsState.TryGetValue(turnContext.Activity.From.Id, out var userItemSelections))
                        {
                            var selection = userItemSelections.FirstOrDefault(s => s.ItemId == itemId);
                            if (selection == null)
                            {
                                userItemSelections.Add(new OrderItemSelection { ItemId = itemId, ReturnReasonCode = returnReason });
                            }
                        }
                        else
                        {
                            selectionsState.Add(turnContext.Activity.From.Id,
                                new List<OrderItemSelection>
                                {
                                    new()
                                    {
                                        ItemId = itemId,
                                        ReturnReasonCode = returnReason
                                    }
                                });
                        }

                    }
                    else if (addToReturn == "false")
                    {
                        if (selectionsState.TryGetValue(turnContext.Activity.From.Id, out var userItemSelections))
                        {
                            var selection = userItemSelections.FirstOrDefault(s => s.ItemId == itemId);
                            if (selection != null)
                            {
                                userItemSelections.Remove(selection);
                            }
                        }
                    }
                }
                else if (action == "select_shipping_method")
                {
                    var selectedReturnMethod = submittedData["selectedMethod"]?.ToString();

                    // Persist the user's choice
                    var logInData = await StateService.LogInDataAccessor.GetAsync(turnContext, () => new LogInData(), cancellationToken);
                    logInData.SelectedReturnMethod = logInData.AvailableReturnMethods.ReturnMethods.First(m => m.CarrierServiceRoute.CarrierServiceRouteId == selectedReturnMethod);
                    await base.OnTurnAsync(turnContext, cancellationToken);
                }
                else
                {
                    await base.OnTurnAsync(turnContext, cancellationToken);
                }
            }
            else
            {
                await base.OnTurnAsync(turnContext, cancellationToken);
            }
        }
        else
        {
            await base.OnTurnAsync(turnContext, cancellationToken); //prevent the dialog from being reset when the user sends the submit on adaptive cards action
        }

        // Save any state changes that might have occured during the turn.
        await StateService.ConversationState.SaveChangesAsync(turnContext, false, cancellationToken);
        await StateService.UserState.SaveChangesAsync(turnContext, false, cancellationToken);
    }
 
    protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Running dialog with Message Activity.");

        // Run the Dialog with the new message Activity.
        await Dialog.RunAsync(turnContext, StateService.DialogStateAccessor, cancellationToken);
    }
}

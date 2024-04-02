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

public class FeedbackDialog : ComponentDialog
{
    private readonly StateService _stateService;
    private readonly BotService _botService;

    public FeedbackDialog(string dialogId, StateService stateService, BotService botService)
        : base(dialogId)
    {
        _stateService = stateService;
        _botService = botService;

        InitializeWaterfallDialog();
    }

    private void InitializeWaterfallDialog()
    { 
        var waterfallSteps = new WaterfallStep[]
        {
            AnalyzeFeedbackStepAsync
        };

        AddDialog(new WaterfallDialog($"{nameof(FeedbackDialog)}.mainFlow", waterfallSteps));

        InitialDialogId = $"{nameof(FeedbackDialog)}.mainFlow";
    }

    private async Task<DialogTurnResult> AnalyzeFeedbackStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
    {
        var sentence = (string)stepContext.Options;

        var cluResult = await _botService.AnalyzeSentimentAsync(sentence);

        var positiveResponse =
            "We're thrilled to hear that you had a great experience with our support team! Your satisfaction is our top priority. That's a fantastic suggestion! We're always excited to hear ideas from our valued customers. We'll definitely consider it.";

        var negativeResponse =
            "We're sorry to hear that you had a bad experience with our support team. We're always looking to improve our customer service, so we appreciate your feedback. We'll definitely consider it.";

        if (cluResult.IsPositiveFeedback)
        {
            await stepContext.Context.SendActivityAsync(MessageFactory.Text(positiveResponse), cancellationToken);
        }
        else
        {
            await stepContext.Context.SendActivityAsync(MessageFactory.Text(negativeResponse), cancellationToken);
        }

        return await stepContext.ReplaceDialogAsync($"{nameof(MainDialog)}.main", "Thank you for sharing your feedback! What else can I do for you?", cancellationToken);
    }
}
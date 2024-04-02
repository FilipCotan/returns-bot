// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
//
// Generated with Bot Builder V4 SDK Template for Visual Studio CoreBot v4.18.1

using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using GRP_Bot_Agent.Services;
using Newtonsoft.Json;

namespace GRP_Bot_Agent.Dialogs;
public class MainDialog : ComponentDialog
{
    private readonly ILogger _logger;
    private readonly StateService _stateService;
    private readonly BotService _botService;

    public MainDialog(
        ILogger<MainDialog> logger, 
        StateService stateService,
        BotService botService)
        : base(nameof(MainDialog))
    {
        _logger = logger;
        _stateService = stateService;
        _botService = botService;

        AddDialog(new TextPrompt(nameof(TextPrompt)));
        AddDialog(new LogInDialog($"{nameof(MainDialog)}.logInDialog", _stateService));
        AddDialog(new CreateReturnSmartDialog($"{nameof(MainDialog)}.createReturnDialog", _stateService));
        AddDialog(new TrackReturnOrderDialog($"{nameof(MainDialog)}.trackReturnOrderDialog", _stateService));
        AddDialog(new FeedbackDialog($"{nameof(MainDialog)}.feedbackDialog", _stateService, _botService));

        var waterfallSteps = new WaterfallStep[]
        {
            IntroStepAsync,
            OutroStepAsync
        };

        AddDialog(new WaterfallDialog($"{nameof(MainDialog)}.main", waterfallSteps));

        // The initial child Dialog to run.
        InitialDialogId = $"{nameof(MainDialog)}.main";
    }

    private async Task<DialogTurnResult> IntroStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
    {
        var sentence = stepContext.Context.Activity?.Text;
        var cluResult = await _botService.AnalyzeConversationAsync(sentence);

        _logger.LogInformation($"CLU result: {JsonConvert.SerializeObject(cluResult)}");

        switch (cluResult.Intent)
        {
            case Intents.CreateReturn:
                return await stepContext.BeginDialogAsync($"{nameof(MainDialog)}.logInDialog", cluResult, cancellationToken);
            case Intents.TrackReturn:
                return await stepContext.BeginDialogAsync($"{nameof(MainDialog)}.trackReturnOrderDialog", cluResult, cancellationToken);
            case Intents.SendFeedback:
                if (stepContext.Parent.ActiveDialog.Id == $"{nameof(MainDialog)}.feedbackDialog")
                {
                    goto default;
                }

                return await stepContext.BeginDialogAsync($"{nameof(MainDialog)}.feedbackDialog", sentence, cancellationToken);
            default:
            {
                const string messageText = "What can I help you with today?";
                var promptMessage = MessageFactory.Text(messageText, messageText, InputHints.ExpectingInput);

                return await stepContext.PromptAsync(nameof(TextPrompt), new PromptOptions { Prompt = promptMessage }, cancellationToken);
            }
        }
    }

    private async Task<DialogTurnResult> OutroStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
    {
        if (stepContext.Result is CluResponse { Intent: Intents.CreateReturn } cluResponse)
        {
            return await stepContext.BeginDialogAsync($"{nameof(MainDialog)}.createReturnDialog", cluResponse, cancellationToken);
        }

        if (stepContext.Result is CluResponse { Intent: Intents.TrackReturn, IsComplete: true})
        {
            var message = "Thank you for using our bot service. Don't forget to leave a feedback of your experience. Is there anything else I can do for you?";
            return await stepContext.ReplaceDialogAsync(InitialDialogId, message, cancellationToken);
        }

        // Restart the main dialog with a different message the second time around
        var promptMessage = "What else can I do for you?";
        return await stepContext.ReplaceDialogAsync(InitialDialogId, promptMessage, cancellationToken);
    }
}

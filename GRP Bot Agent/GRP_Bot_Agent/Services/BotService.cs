using Azure.AI.Language.Conversations;
using Azure;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs;
using Azure.Core;
using System.Text.Json;
using System.Collections.Generic;
using Azure.AI.TextAnalytics;

namespace GRP_Bot_Agent.Services;

public enum Intents
{
    None,
    CreateReturn,
    TrackReturn,
    SendFeedback
}

public class CluResponse
{
    public Intents Intent { get; set; }

    public string EmailAddress { get; set; }

    public string OrderReference { get; set; }

    public string ProductDescription { get; set; }

    public string ReturnOrderNumber { get; set; }

    public string ReturnReason { get; set; }

    public string Store { get; set; }

    public bool IsComplete { get; set; }

    public bool IsPositiveFeedback { get; set; }
}

public class BotService
{
    private readonly ConversationAnalysisClient _client;
    private readonly TextAnalyticsClient _textAnalyticsClient;

    private const string ProjectName = "returns-donard-bot-clu";
    private const string DeploymentName = "bot-clu-deploy-1";

    public BotService()
    {
        var endpoint = new Uri("https://returns-bot-clu.cognitiveservices.azure.com/");
        var credential = new AzureKeyCredential("<add-credentials-here>");

        _client = new ConversationAnalysisClient(endpoint, credential);
        _textAnalyticsClient = new TextAnalyticsClient(endpoint, credential);
    }

    public async Task<CluResponse> AnalyzeConversationAsync(string sentence)
    {
        if (sentence is null || sentence.Length < 20)
        {
            return new CluResponse
            {
                Intent = Intents.None
            };
        }

        var data = new
        {
            analysisInput = new
            {
                conversationItem = new
                {
                    text = sentence,
                    id = "1",
                    participantId = "1",
                }
            },
            parameters = new
            {
                projectName = ProjectName,
                deploymentName = DeploymentName,

                // Use Utf16CodeUnit for strings in .NET.
                stringIndexType = "Utf16CodeUnit",
            },
            kind = "Conversation",
        };

        var response = await _client.AnalyzeConversationAsync(RequestContent.Create(data));

        using var result = await JsonDocument.ParseAsync(response.ContentStream);
        var conversationalTaskResult = result.RootElement;
        var conversationPrediction = conversationalTaskResult.GetProperty("result").GetProperty("prediction");

        var topIntent = conversationPrediction.GetProperty("topIntent").GetString();
        var score = conversationPrediction
            .GetProperty("intents")
            .EnumerateArray()
            .First(e => e.GetProperty("category").GetString() == topIntent)
            .GetProperty("confidenceScore").GetDouble();

        if (score < 0.7)
        {
            return new CluResponse
            {
                Intent = Intents.None
            };
        }

        var intent = Enum.Parse<Intents>(topIntent);

        var entities = conversationPrediction.GetProperty("entities").EnumerateArray().ToList();

        if (entities.Count == 0)
        {
            return new CluResponse
            {
                Intent = intent
            };
        }

        var storeObject = entities.FirstOrDefault(e => e.GetProperty("category").GetString() == "Store");
        var store = storeObject.ValueKind is JsonValueKind.Undefined ? null : storeObject.GetProperty("text").GetString();

        var returnReasonObject = entities.FirstOrDefault(e => e.GetProperty("category").GetString() == "ReturnReason");
        var returnReason = returnReasonObject.ValueKind is JsonValueKind.Undefined ? null : returnReasonObject.GetProperty("extraInformation").EnumerateArray().FirstOrDefault().GetProperty("key").GetString();

        var productDescriptionObject = entities.FirstOrDefault(e => e.GetProperty("category").GetString() == "ProductDescription");
        var productDescription = productDescriptionObject.ValueKind is JsonValueKind.Undefined ? null : productDescriptionObject.GetProperty("text").GetString();

        var orderReferenceObject = entities.FirstOrDefault(e => e.GetProperty("category").GetString() == "OrderReference");
        var orderReference = orderReferenceObject.ValueKind is JsonValueKind.Undefined ? null : orderReferenceObject.GetProperty("text").GetString();

        var emailAddressObject = entities.FirstOrDefault(e => e.GetProperty("category").GetString() == "EmailAddress");
        var emailAddress = emailAddressObject.ValueKind is JsonValueKind.Undefined ? null : emailAddressObject.GetProperty("text").GetString();

        var returnOrderNumberObject = entities.FirstOrDefault(e => e.GetProperty("category").GetString() == "ReturnOrderNumber");
        var returnOrderNumber = returnOrderNumberObject.ValueKind is JsonValueKind.Undefined ? null : returnOrderNumberObject.GetProperty("text").GetString();

        return new CluResponse
        {
            Intent = intent,
            Store = store,
            ProductDescription = productDescription,
            ReturnReason = returnReason,
            OrderReference = orderReference,
            EmailAddress = emailAddress,
            ReturnOrderNumber = returnOrderNumber
        };
    }

    public async Task<CluResponse> AnalyzeSentimentAsync(string userSentence)
    {
        var documents = new List<string>
        {
            userSentence
        };

        AnalyzeSentimentResultCollection reviews = await _textAnalyticsClient.AnalyzeSentimentBatchAsync(documents, options: new AnalyzeSentimentOptions
        {
            IncludeOpinionMining = true
        });

        var positiveScore = reviews.First().DocumentSentiment.ConfidenceScores.Positive;
        var negativeScore = reviews.First().DocumentSentiment.ConfidenceScores.Negative;

        return new CluResponse
        {
            Intent = Intents.SendFeedback,
            IsPositiveFeedback = positiveScore > negativeScore
        };
    }
}
using System.Collections.Generic;
using GRP_Bot_Agent.Models;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;

namespace GRP_Bot_Agent.Services;

public class StateService
{
    public UserState UserState { get; }

    public ConversationState ConversationState { get; }

    public static string UserProfileId => $"{nameof(StateService)}.UserProfile";

    public static string LogInDataId => $"{nameof(StateService)}.LogInData";

    public static string ConversationDataId => $"{nameof(StateService)}.ConversationData";

    public static string DialogStateId => $"{nameof(StateService)}.DialogState";

    public static string OrderItemSelectionsId => $"{nameof(StateService)}.OrderItemSelections";

    public IStatePropertyAccessor<UserProfile> UserProfileAccessor { get; set; }

    public IStatePropertyAccessor<LogInData> LogInDataAccessor { get; set; }
    
    public IStatePropertyAccessor<ConversationData> ConversationDataAccessor { get; set; }

    public IStatePropertyAccessor<DialogState> DialogStateAccessor { get; set; }

    //each user has a list of order item selections
    public IStatePropertyAccessor<Dictionary<string, List<OrderItemSelection>>> OrderItemSelectionsAccessor { get; set; }

    public StateService(ConversationState conversationState, UserState userState)
    {
        ConversationState = conversationState;
        UserState = userState;
        InitializeAccessors();
    }

    private void InitializeAccessors()
    {
        UserProfileAccessor = UserState.CreateProperty<UserProfile>(UserProfileId);
        LogInDataAccessor = UserState.CreateProperty<LogInData>(LogInDataId);
        ConversationDataAccessor = ConversationState.CreateProperty<ConversationData>(ConversationDataId);
        DialogStateAccessor = ConversationState.CreateProperty<DialogState>(DialogStateId);
        OrderItemSelectionsAccessor = ConversationState.CreateProperty<Dictionary<string, List<OrderItemSelection>>>(OrderItemSelectionsId);
    }
}
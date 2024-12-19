using System;
using System.Collections.Generic;

namespace TwitchChatHueControls.Models;

/// <summary>
/// Represents a Twitch ChannelResubscription  event
/// </summary>
internal record ChannelResubscriptionPayload(
    ChannelResubscriptionDetails ChannelResubscription,
    ChannelResubscriptionEvent Event
);

/// <summary>
/// Details of the ChannelResubscription  configuration
/// </summary>
internal record ChannelResubscriptionDetails(
    string Id,
    string Type,
    string Version,
    string Status,
    decimal Cost,
    Condition Condition,
    Transport Transport,
    DateTime created_at
);

/// <summary>
/// Represents a ChannelResubscription event
/// </summary>
internal record ChannelResubscriptionEvent(
    string user_id,
    string user_login,
    string user_name,
    string broadcaster_user_id,
    string broadcaster_user_login,
    string broadcaster_user_name,
    string Tier,
    ChannelResubscriptionMessage Message,
    int cumulative_months,
    int? streak_months,
    int duration_months
);

/// <summary>
/// ChannelResubscription message details
/// </summary>
internal record ChannelResubscriptionMessage(
    string Text,
    List<EmoteDetails> Emotes
);

/// <summary>
/// Emote details within a ChannelResubscription message
/// </summary>
internal record EmoteDetails(
    int Begin,
    int End,
    string Id
);

using System;

namespace TwitchChatHueControls.Models;

/// <summary>
/// Represents a Twitch ChannelGiftedsubscription  event
/// </summary>
internal record ChannelGiftedSubscriptionPayload(
    ChannelGiftedSubscriptionDetails ChannelGiftedSubscription,
    ChannelGiftedSubscriptionEvent Event
);

/// <summary>
/// Details of the ChannelGiftedsubscription  configuration
/// </summary>
internal record ChannelGiftedSubscriptionDetails(
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
/// Represents a ChannelGiftedsubscription event
/// </summary>
internal record ChannelGiftedSubscriptionEvent(
    string user_id,
    string user_login,
    string user_name,
    string broadcaster_user_id,
    string broadcaster_user_login,
    string broadcaster_user_name,
    string total,
    string Tier,
    int cumulative_total,
    bool is_anonymous
);
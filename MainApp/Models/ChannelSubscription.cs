namespace TwitchChatHueControls.Models;

/// <summary>
/// Represents a Twitch Channelsubscription  event
/// </summary>
internal record ChannelSubscriptionPayload(
    ChannelSubscriptionDetails ChannelSubscription,
    ChannelSubscriptionEvent Event
);

/// <summary>
/// Details of the Channelsubscription  configuration
/// </summary>
internal record ChannelSubscriptionDetails(
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
/// Represents a Channelsubscription event
/// </summary>
internal record ChannelSubscriptionEvent(
    string user_id,
    string user_login,
    string user_name,
    string broadcaster_user_id,
    string broadcaster_user_login,
    string broadcaster_user_name,
    string tier,
    bool is_gift
);


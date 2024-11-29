using Makaretu.Dns;

namespace TwitchChatHueControls.Models;

internal record SubscribeEventPayload(
    string? type,
    string? version,
    Condition? condition,
    Transport? transport
);

internal record Condition(
    string? broadcaster_user_id,
    string? user_id
);

internal record Transport(
    string? method,
    string? session_id
);

internal record EventSubMetadata(
    string MessageId,
    string MessageType,
    DateTime MessageTimestamp,
    string SubscriptionType,
    string SubscriptionVersion
);

internal record EventSubWebsocketSessionInfoMessage(
    EventSubMetadata Metadata,
    EventSubWebsocketSessionInfoPayload Payload
);

internal record EventSubWebsocketSessionInfoPayload(
    EventSubWebsocketSessionInfo Session
);

internal record EventSubWebsocketSessionInfo(
    string Id,
    string Status,
    string DisconnectReason,
    int? KeepaliveTimeoutSeconds,
    string ReconnectUrl,
    DateTime ConnectedAt,
    DateTime? DisconnectedAt,
    DateTime? ReconnectingAt
);

internal record Notification<T>(
    EventSubMetadata Metadata,
    NotificationPayload<T> Payload
);

internal record NotificationPayload<T>(
    NotificationPayloadSubscription Subscription,
    T Event
);

internal record NotificationPayloadSubscription(
    string Id,
    string Status,
    string Type,
    string Version,
    int Cost,
    Condition Condition,
    Transport Transport,
    DateTime CreatedAt
);

internal record StreamOnline(
    string id,
    string broadcaster_user_id,
    string broadcaster_user_login,
    string broadcaster_user_name,
    string type,
    DateTime started_at
);

internal record StreamOffline(
    string broadcaster_user_id,
    string broadcaster_user_login,
    string broadcaster_user_name
);

internal record ChannelPointsCustomRewardRedemptionAdd(
    string id,
    string broadcaster_user_id,
    string broadcaster_user_login,
    string broadcaster_user_name,
    string user_id,
    string user_login,
    string user_name,
    string user_input,
    string status,
    ChannelPointsCustomRewardRedemptionAddReward reward,
    DateTime redeemed_at
);

internal record ChannelPointsCustomRewardRedemptionAddReward(
    string id,
    string title,
    int cost,
    string prompt
);

internal record TwitchChatMessage(
    string broadcaster_user_id,
    string broadcaster_user_login,
    string broadcaster_user_name,
    string source_broadcaster_user_id,
    string source_broadcaster_user_login,
    string source_broadcaster_user_name,
    string chatter_user_id,
    string chatter_user_login,
    string chatter_user_name,
    string message_id,
    string source_message_id,
    ChatMessage message,
    string color,
    string message_type,
    object cheer,
    object reply,
    string channel_points_custom_reward_id,
    string channel_points_animation_id
);

internal record ChatMessage(
    string text,
    List<Fragment> fragments
);

internal record Fragment(
    string type,
    string text,
    object? cheermote,
    object? emote,
    object? mention
);

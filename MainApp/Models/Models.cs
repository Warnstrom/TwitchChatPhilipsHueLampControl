using HueApi.ColorConverters;

namespace TwitchChatHueControls.Models;

internal record SubscribeEventPayload(
    string? type,
    string? version,
    Condition? condition,
    Transport? transport
);

internal record Condition(
    string? broadcaster_user_id,
    string? user_id,
    string? from_broadcaster_user_id,
    string? to_broadcaster_user_id
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
internal record LightRequest
(
    string[] LampIdentifiers,
    string? EffectType,
    RGBColor ColorName
);
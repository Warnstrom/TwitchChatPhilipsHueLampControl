using System;

namespace TwitchChatHueControls.Models;

/// <summary>
/// Represents a Twitch ChannelCheer  event
/// </summary>
internal record ChannelCheerPayload(
    ChannelCheerDetails ChannelCheer,
    ChannelCheerEvent Event
);

/// <summary>
/// Details of the ChannelCheer  configuration
/// </summary>
internal record ChannelCheerDetails(
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
/// Represents a ChannelCheer event
/// </summary>
internal record ChannelCheerEvent(
    string user_id,
    string user_login,
    string user_name,
    string broadcaster_user_id,
    string broadcaster_user_login,
    string broadcaster_user_name,
    string message,
    int bits
);
namespace TwitchChatHueControls.Models;

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

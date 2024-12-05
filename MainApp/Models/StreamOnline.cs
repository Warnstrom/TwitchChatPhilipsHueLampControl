namespace TwitchChatHueControls.Models;
internal record StreamOnline(
    string id,
    string broadcaster_user_id,
    string broadcaster_user_login,
    string broadcaster_user_name,
    string type,
    DateTime started_at
);

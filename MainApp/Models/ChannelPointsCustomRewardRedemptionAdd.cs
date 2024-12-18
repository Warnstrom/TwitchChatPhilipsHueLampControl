using System;

namespace TwitchChatHueControls.Models;

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

using VampireCommandFramework;
using Unity.Entities;
using ProjectM.Network;
using BountyForge.Systems;
using BountyForge.Utils;
using BountyForge.Config;
using BepInEx.Configuration; 
using System.Linq;
using BountyForge.Data;
using BepInEx; 
using System.IO; 
using System;

namespace BountyForge.Commands
{
    public static class BountyCommands
    {
        [Command("addbounty", shortHand: "ab", usage: ".addbounty <PlayerName> <Amount>", description: "Places a bounty of a specified amount on a player.", adminOnly: false)]
        public static void AddBountyCommand(ChatCommandContext ctx, string targetPlayerName, int bountyAmount)
        {
            if (!BountyConfig.ModEnabled.Value)
            {
                ctx.Reply(ChatColors.ErrorText("Bounty system is currently disabled."));
                return;
            }

            if (bountyAmount < BountyConfig.MinimumBountyAmount.Value)
            {
                ctx.Reply(ChatColors.ErrorText($"Bounty amount must be at least {BountyConfig.MinimumBountyAmount.Value} {BountyConfig.PaymentItemDisplayName.Value}."));
                return;
            }
            if (bountyAmount <= 0)
            {
                ctx.Reply(ChatColors.ErrorText("Bounty amount must be a positive value."));
                return;
            }

            Entity userEntity = ctx.Event.SenderUserEntity;
            Entity charEntity = ctx.Event.SenderCharacterEntity;

            if (userEntity == Entity.Null || charEntity == Entity.Null)
            {
                ctx.Reply(ChatColors.ErrorText("Could not identify your player/character entity."));
                return;
            }
            if (!VWorld.IsServerWorldReady())
            {
                ctx.Reply(ChatColors.ErrorText("Server world is not ready. Please try again shortly."));
                return;
            }

            User placerUserData = VWorld.EntityManager.GetComponentData<User>(userEntity);
            string placerName = placerUserData.CharacterName.ToString();
            ulong placerSteamID = placerUserData.PlatformId;

            BountyManager.TryPlaceBounty(charEntity, placerName, placerSteamID, targetPlayerName, bountyAmount, ctx);
        }

        [Command("bountylist", shortHand: "bl", description: "Lists all active player-placed bounties.", adminOnly: false)]
        public static void ListBountiesCommand(ChatCommandContext ctx)
        {
            if (!BountyConfig.ModEnabled.Value)
            {
                ctx.Reply(ChatColors.ErrorText("Bounty system is currently disabled."));
                return;
            }
            BountyManager.ListActiveBounties(ctx);
        }

        [Command("bountyme", description: "Shows your current bounty status (if targeted) and self-claim progress.", adminOnly: false)]
        public static void BountyMeCommand(ChatCommandContext ctx)
        {
            if (!BountyConfig.ModEnabled.Value)
            {
                ctx.Reply(ChatColors.ErrorText("Bounty system is currently disabled."));
                return;
            }

            Entity userEntity = ctx.Event.SenderUserEntity;
            if (userEntity == Entity.Null)
            {
                ctx.Reply(ChatColors.ErrorText("Could not identify your player entity."));
                return;
            }
            if (!VWorld.IsServerWorldReady())
            {
                ctx.Reply(ChatColors.ErrorText("Server world is not ready. Please try again shortly."));
                return;
            }

            User senderUserData = VWorld.EntityManager.GetComponentData<User>(userEntity);
            string senderName = senderUserData.CharacterName.ToString();
            ulong senderSteamID = senderUserData.PlatformId;

            ctx.Reply(ChatColors.HighlightText($"--- Your Bounty Stats ({ChatColors.BountyPosterNameText(senderName)}) ---"));

            long totalClaimed = 0;
            var leaderboardEntry = LeaderboardSystem.GetLeaderboard(int.MaxValue) 
                                                 .FirstOrDefault(e => e.SteamID == senderSteamID);

            if (leaderboardEntry.SteamID != 0) 
            {
                totalClaimed = leaderboardEntry.TotalBountyAmountClaimed;
            }
            ctx.Reply($"{ChatColors.InfoText("Total Bounty Rewards Claimed: ")}{ChatColors.SuccessText(totalClaimed.ToString())} {ChatColors.BountyItemText(BountyConfig.PaymentItemDisplayName.Value)}");
            if (BountyManager.IsPlayerBountied(senderSteamID, out BountyManager.BountyDetails bountyOnSelf))
            {
                ctx.Reply(ChatColors.WarningText($"You have an active bounty on YOU!"));
                ctx.Reply($" -> Placed by: {ChatColors.BountyPosterNameText(bountyOnSelf.PlacerName)}");
                ctx.Reply($" -> Reward: {ChatColors.BountyItemText(bountyOnSelf.PaymentAmount + " " + bountyOnSelf.PaymentItemName)}");

                if (BountyConfig.EnableTargetSelfClaimViaKills.Value)
                {
                    int killsRequired = BountyConfig.KillsForTargetSelfClaim.Value;
                    int killsAchieved = bountyOnSelf.KillsAchievedWhileBountied;
                    int killsRemaining = Math.Max(0, killsRequired - killsAchieved);

                    if (killsRemaining > 0)
                    {
                        ctx.Reply(ChatColors.InfoText($" -> Kills remaining to self-claim: {ChatColors.HighlightText(killsRemaining.ToString())}"));
                    }
                    else
                    {
                        ctx.Reply(ChatColors.SuccessText(" -> You have met the kill requirement to self-claim this bounty!"));
                    }
                }
                else
                {
                    ctx.Reply(ChatColors.MutedText(" -> Self-claim via kills is currently disabled by server settings."));
                }
            }
            else
            {
                ctx.Reply(ChatColors.InfoText("You do not currently have an active player-placed bounty on you."));
            }
        }

        [Command("contract", shortHand: "ac", description: "Shows your current assassin contract status.", adminOnly: false)]
        public static void AssassinContractStatusCommand(ChatCommandContext ctx)
        {
            if (!BountyConfig.ModEnabled.Value || !BountyConfig.EnableAssassinContracts.Value)
            {
                ctx.Reply(ChatColors.ErrorText("Assassin contract system is currently disabled."));
                return;
            }

            Entity userEntity = ctx.Event.SenderUserEntity;
            if (userEntity == Entity.Null)
            {
                ctx.Reply(ChatColors.ErrorText("Could not identify your player entity."));
                return;
            }

            if (!VWorld.IsServerWorldReady())
            {
                ctx.Reply(ChatColors.ErrorText("Server world is not ready."));
                return;
            }

            User senderUserData = VWorld.EntityManager.GetComponentData<User>(userEntity);
            ulong senderSteamID = senderUserData.PlatformId;
            string senderName = senderUserData.CharacterName.ToString();

            if (BountyManager.TryGetActiveAssassinContract(senderSteamID, out AssassinContractDetails contract))
            {
                long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                long timeLeftSeconds = contract.ContractEndTimeUnixTimestamp - currentTime;

                if (timeLeftSeconds > 0)
                {
                    ctx.Reply(ChatColors.HighlightText($"--- Your Active Contract ({ChatColors.BountyPosterNameText(senderName)}) ---"));
                    ctx.Reply($"Objective: Eliminate {ChatColors.WarningText(contract.KillsRequired.ToString())} players.");
                    ctx.Reply($"Progress: {ChatColors.InfoText(contract.KillsAchieved.ToString())} / {contract.KillsRequired.ToString()} eliminated.");
                    ctx.Reply($"Time Remaining: {ChatColors.WarningText($"{timeLeftSeconds / 60}m {timeLeftSeconds % 60}s")}.");
                    ctx.Reply($"Reward: {ChatColors.BountyItemText(contract.RewardAmount + " " + BountyConfig.PaymentItemDisplayName.Value)}.");
                }
                else
                {
                    ctx.Reply(ChatColors.InfoText("Your contract has expired or was recently failed."));
                }
            }
            else
            {
                ctx.Reply(ChatColors.InfoText("You do not have an active assassin contract."));
            }
        }


        [Command("reloadbountyforge", description: "Reloads the BountyForge configuration and data.", adminOnly: true)]
        public static void ReloadConfigCommand(ChatCommandContext ctx)
        {
            try
            {
                ConfigFile currentConfig = Plugin.Config; 
                if (currentConfig == null)
                {
                    ctx.Reply(ChatColors.ErrorText("Configuration file instance is not available."));
                    LoggingHelper.Error("[BountyForge] ReloadConfigCommand: Plugin.Config is null.");
                    return;
                }
                currentConfig.Reload();
                BountyConfig.Initialize(currentConfig); 
                ItemUtils.Initialize();

               
                BountyMapIcons.Initialize();

                string pluginBaseConfigPath = Path.Combine(Paths.ConfigPath, PluginInfo.PLUGIN_NAME);
                string dataStorageBasePath = Path.Combine(pluginBaseConfigPath, "Data");
                Directory.CreateDirectory(pluginBaseConfigPath);
                Directory.CreateDirectory(dataStorageBasePath);

               
                BountyManager.Initialize(dataStorageBasePath);
                LeaderboardSystem.Initialize(dataStorageBasePath); 

                ctx.Reply(ChatColors.SuccessText("BountyForge configuration and data reloaded. Map icons refreshed. Timers reset."));
                LoggingHelper.Info("[BountyForge] Configuration and data reloaded via command. Map icons refreshed. Timers reset.");
            }
            catch (System.Exception e)
            {
                ctx.Reply(ChatColors.ErrorText("An error occurred while reloading. Check server logs."));
                LoggingHelper.Error($"[BountyForge] Error reloading: {e.Message}\n{e.StackTrace}");
            }
        }

        [Command("bfclearicons", description: "Admin: Clears bounty map icons. Usage: .bfclearicons <all|PlayerName>", adminOnly: true)]
        public static void ClearBountyIconsCommand(ChatCommandContext ctx, string target)
        {
            if (!BountyConfig.EnableMapIcons.Value)
            {
                ctx.Reply(ChatColors.InfoText("Map icons are currently disabled in the configuration. No icons to clear."));
                return;
            }
            if (!VWorld.IsServerWorldReady()) { ctx.Reply(ChatColors.ErrorText("Server not ready.")); return; }

            if (string.Equals(target, "all", System.StringComparison.OrdinalIgnoreCase))
            {
                BountyMapIcons.RemoveAllMapIcons();
                ctx.Reply(ChatColors.SuccessText("Attempted to remove all currently tracked bounty map icons."));
            }
            else
            {
                if (UserUtils.TryGetUserAndCharacterEntity(target, out Entity targetUserEntity, out _, out User targetUserData, out string resolvedName))
                {
                    LoggingHelper.Info($"[BountyCommands.bfclearicons] Admin trying to clear icons for player {resolvedName} (SteamID: {targetUserData.PlatformId}, UserEntity: {targetUserEntity})");
                    BountyMapIcons.RemoveMapIcon(targetUserData.PlatformId); 
                    int clearedByScan = BountyMapIcons.ClearPlayerIconsByScan(targetUserEntity, resolvedName); 
                    ctx.Reply(ChatColors.SuccessText($"Attempted to remove bounty map icons for player {ChatColors.HighlightText(resolvedName)}. Cleared {clearedByScan} icons via aggressive scan."));
                }
                else
                {
                    ctx.Reply(ChatColors.ErrorText($"Player '{target}' not found or is not online/valid."));
                }
            }
        }

        [Command("bountytop", shortHand: "bt", usage: ".bountytop or .bt", description: "Displays the top 5 bounty hunters by total bounty claimed.", adminOnly: false)]
        public static void BountyTopCommand(ChatCommandContext ctx)
        {
            if (!BountyConfig.ModEnabled.Value)
            {
                ctx.Reply(ChatColors.ErrorText("Bounty system is currently disabled."));
                return;
            }

            const int topPlayersToShow = 5;
            var leaderboardEntries = LeaderboardSystem.GetLeaderboard(topPlayersToShow);

            if (!leaderboardEntries.Any())
            {
                ctx.Reply(ChatColors.InfoText("The bounty hunter leaderboard is empty."));
                return;
            }

            ctx.Reply(ChatColors.HighlightText("--- Top Bounty Hunters ---"));
            int rank = 1;
            foreach (var entry in leaderboardEntries)
            {
                string rankText = ChatColors.HighlightText(rank.ToString() + ".");
                string playerNameText = ChatColors.InfoText(entry.PlayerName);
                string claimedLabelText = ChatColors.HighlightText("- Claimed:");
                string claimedValueText = ChatColors.InfoText(entry.TotalBountyAmountClaimed.ToString() + " " + BountyConfig.PaymentItemDisplayName.Value);
                ctx.Reply($"{rankText} {playerNameText} {claimedLabelText} {claimedValueText}");
                rank++;
            }
        }
    }
}
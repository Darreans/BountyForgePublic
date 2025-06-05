// BountyForge/Systems/BountyManager.cs
using Unity.Entities;
using ProjectM;
using ProjectM.Network;
using VampireCommandFramework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System;
using BountyForge.Config;
using BountyForge.Utils;
using Stunlock.Core;
using BountyForge.Data;

namespace BountyForge.Systems
{
    public static class BountyManager
    {
        public class BountyDetails // For player-placed bounties
        {
            public ulong TargetSteamID { get; set; }
            public string TargetName { get; set; }
            public ulong PlacerSteamID { get; set; }
            public string PlacerName { get; set; }
            public int PaymentAmount { get; set; }
            public string PaymentItemName { get; set; }
            public int KillsAchievedWhileBountied { get; set; }

            public BountyDetails() { }
            public BountyDetails(ulong targetId, string targetName, ulong placerId, string placerName, int amount, string itemName)
            {
                TargetSteamID = targetId; TargetName = targetName; PlacerSteamID = placerId; PlacerName = placerName; PaymentAmount = amount; PaymentItemName = itemName; KillsAchievedWhileBountied = 0;
            }
        }

        // --- Player-Placed Bounties ---
        private static Dictionary<ulong, BountyDetails> _activeBounties = new();
        private static string _activeBountiesFilePath;

        // --- Assassin Contracts ---
        private static Dictionary<ulong, AssassinContractDetails> _activeContracts = new();
        private static string _activeContractsFilePath;

        // --- Clan Betrayal Prevention ---
        private static Dictionary<ulong, Dictionary<ulong, long>> _recentClanBetrayalCache = new();
        private static string _clanBetrayalCacheFilePath;
        private static System.Threading.Timer _clanBetrayalCacheCleanupTimer = null;
        private const double CLAN_BETRAYAL_CACHE_CLEANUP_INTERVAL_SECONDS = 3600.0;

        private static Random _rng = new Random();

        private static System.Threading.Timer _contractOfferCycleTimer = null;
        private static System.Threading.Timer _expiredContractsCheckTimer = null;
        private const double EXPIRED_CONTRACT_CHECK_INTERVAL_SECONDS = 30.0;


        public static void Initialize(string dataStorageBasePath)
        {
            _activeBounties.Clear();
            _activeBountiesFilePath = Path.Combine(dataStorageBasePath, "active_bounties.csv");
            LoadActiveBounties();

            _activeContracts.Clear();
            _activeContractsFilePath = Path.Combine(dataStorageBasePath, "active_assassin_contracts.csv");
            LoadActiveContracts();

            _recentClanBetrayalCache.Clear();
            _clanBetrayalCacheFilePath = Path.Combine(dataStorageBasePath, "clan_betrayal_cache.csv");
            LoadClanBetrayalCache();

            ScheduleNextContractOfferAttempt(true);

            if (_expiredContractsCheckTimer != null) _expiredContractsCheckTimer.Dispose();
            _expiredContractsCheckTimer = BountyTaskScheduler.RunActionEveryInterval(
                CheckForExpiredAssassinContracts,
                EXPIRED_CONTRACT_CHECK_INTERVAL_SECONDS,
                EXPIRED_CONTRACT_CHECK_INTERVAL_SECONDS
            );

            if (_clanBetrayalCacheCleanupTimer != null) _clanBetrayalCacheCleanupTimer.Dispose();
            _clanBetrayalCacheCleanupTimer = BountyTaskScheduler.RunActionEveryInterval(
                CleanupExpiredClanBetrayalCache,
                CLAN_BETRAYAL_CACHE_CLEANUP_INTERVAL_SECONDS,
                CLAN_BETRAYAL_CACHE_CLEANUP_INTERVAL_SECONDS
            );
        }

        #region Clan Betrayal System
        public static void RecordClanLeave(ulong leaverSteamID, List<ulong> formerClanmateSteamIDs)
        {
            if (!BountyConfig.EnableClanBetrayalPrevention.Value || BountyConfig.ClanBetrayalCooldownSeconds.Value <= 0)
            {
                return;
            }

            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long expiryTime = currentTime + BountyConfig.ClanBetrayalCooldownSeconds.Value;

            if (!_recentClanBetrayalCache.ContainsKey(leaverSteamID))
            {
                _recentClanBetrayalCache[leaverSteamID] = new Dictionary<ulong, long>();
            }
            foreach (ulong mateID in formerClanmateSteamIDs)
            {
                if (leaverSteamID == mateID) continue;
                _recentClanBetrayalCache[leaverSteamID][mateID] = expiryTime;

                if (!_recentClanBetrayalCache.ContainsKey(mateID))
                {
                    _recentClanBetrayalCache[mateID] = new Dictionary<ulong, long>();
                }
                _recentClanBetrayalCache[mateID][leaverSteamID] = expiryTime;
            }
            SaveClanBetrayalCache();
        }

        private static bool IsClanBetrayal(ulong killerSteamID, ulong victimSteamID)
        {
            if (!BountyConfig.EnableClanBetrayalPrevention.Value) return false;

            if (_recentClanBetrayalCache.TryGetValue(killerSteamID, out var victimMap))
            {
                if (victimMap.TryGetValue(victimSteamID, out long expiryTime))
                {
                    if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() < expiryTime)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void CleanupExpiredClanBetrayalCache()
        {
            if (!VWorld.IsServerWorldReady() || !_recentClanBetrayalCache.Any()) return;

            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            bool changed = false;
            List<ulong> outerKeysToRemove = new List<ulong>();

            foreach (var outerEntry in _recentClanBetrayalCache)
            {
                List<ulong> innerKeysToRemove = new List<ulong>();
                foreach (var innerEntry in outerEntry.Value)
                {
                    if (currentTime >= innerEntry.Value)
                    {
                        innerKeysToRemove.Add(innerEntry.Key);
                    }
                }
                foreach (var innerKey in innerKeysToRemove)
                {
                    outerEntry.Value.Remove(innerKey);
                    changed = true;
                }
                if (outerEntry.Value.Count == 0)
                {
                    outerKeysToRemove.Add(outerEntry.Key);
                }
            }
            foreach (var outerKey in outerKeysToRemove)
            {
                _recentClanBetrayalCache.Remove(outerKey);
                changed = true;
            }

            if (changed)
            {
                SaveClanBetrayalCache();
            }
        }

        #endregion

        #region Assassin Contract System
        private static void ScheduleNextContractOfferAttempt(bool isInitialCall = false)
        {
            if (!BountyConfig.EnableAssassinContracts.Value)
            {
                return;
            }

            int minTime = BountyConfig.AssassinContractOfferMinIntervalSeconds.Value;
            int maxTime = BountyConfig.AssassinContractOfferMaxIntervalSeconds.Value;

            if (minTime <= 0 || maxTime <= 0 || minTime > maxTime)
            {
                return;
            }

            double delayInSeconds = _rng.Next(minTime, maxTime + 1);

            _contractOfferCycleTimer?.Dispose();

            _contractOfferCycleTimer = BountyTaskScheduler.RunActionOnceAfterDelay(AttemptContractOfferCycle, delayInSeconds);
        }

        private static void AttemptContractOfferCycle()
        {
            if (BountyConfig.ModEnabled.Value && BountyConfig.EnableAssassinContracts.Value)
            {
                TryOfferAssassinContract();
            }

            if (BountyConfig.ModEnabled.Value && BountyConfig.EnableAssassinContracts.Value)
            {
                ScheduleNextContractOfferAttempt(false);
            }
        }

        private static void TryOfferAssassinContract()
        {
            if (!VWorld.IsServerWorldReady() || !BountyConfig.EnableAssassinContracts.Value)
            {
                return;
            }

            int maxContracts = BountyConfig.MaxActiveAssassinContracts.Value;
            if (maxContracts > 0 && _activeContracts.Count >= maxContracts)
            {
                return;
            }

            var em = VWorld.EntityManager;
            var onlinePlayers = UserUtils.GetAllOnlineUsers(em);

            var eligiblePlayers = onlinePlayers.Where(user =>
                                        !_activeContracts.ContainsKey(user.PlatformId) &&
                                        !_activeBounties.ContainsKey(user.PlatformId) &&
                                        user.IsConnected &&
                                        !user.IsAdmin)
                                    .ToList();

            if (!eligiblePlayers.Any())
            {
                return;
            }

            User targetUser = eligiblePlayers[_rng.Next(eligiblePlayers.Count)];
            string playerName = targetUser.CharacterName.ToString();
            ulong playerSteamID = targetUser.PlatformId;
            Entity playerUserEntity = UserUtils.GetUserEntityBySteamID(playerSteamID);


            if (playerUserEntity == Entity.Null)
            {
                return;
            }

            int killsToAssign = _rng.Next(BountyConfig.AssassinContractMinKillsRequired.Value, BountyConfig.AssassinContractMaxKillsRequired.Value + 1);
            int missionDurationSeconds = BountyConfig.AssassinContractTimeLimitSeconds.Value;
            long contractEndTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + missionDurationSeconds;

            int rewardMin = BountyConfig.AssassinContractMinRewardAmount.Value;
            int rewardMax = BountyConfig.AssassinContractMaxRewardAmount.Value;
            int actualReward = (rewardMin >= rewardMax) ? rewardMin : _rng.Next(rewardMin, rewardMax + 1);

            if (actualReward <= 0 || killsToAssign <= 0)
            {
                return;
            }

            AssassinContractDetails newContract = new AssassinContractDetails(
                playerSteamID, playerName, killsToAssign, contractEndTime, actualReward);

            _activeContracts[playerSteamID] = newContract;
            SaveActiveContracts();

            string timeString = $"{missionDurationSeconds / 60}m {missionDurationSeconds % 60}s";

            ModChatUtils.SendSystemMessageToClient(em, playerUserEntity,
                ChatColors.SuccessText("You have received a contract from the association!"));
            ModChatUtils.SendSystemMessageToClient(em, playerUserEntity,
                string.Format("{0}{1}{2}{3}{4}",
                    ChatColors.InfoText("Eliminate "),
                    ChatColors.ErrorText(killsToAssign.ToString()),
                    ChatColors.InfoText(" players within "),
                    ChatColors.ErrorText(timeString),
                    ChatColors.InfoText(".")));
            ModChatUtils.SendSystemMessageToClient(em, playerUserEntity,
                ChatColors.InfoText($"Reward for success: {actualReward} {BountyConfig.PaymentItemDisplayName.Value}. Failure means no reward."));
        }

        private static void CheckForExpiredAssassinContracts()
        {
            if (!VWorld.IsServerWorldReady() || !_activeContracts.Any() || !BountyConfig.ModEnabled.Value || !BountyConfig.EnableAssassinContracts.Value)
            {
                return;
            }

            List<ulong> expiredContractPlayerIds = new List<ulong>();
            long currentUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var em = VWorld.EntityManager;

            foreach (var contractEntry in _activeContracts.ToList())
            {
                if (currentUnixTime >= contractEntry.Value.ContractEndTimeUnixTimestamp)
                {
                    if (!expiredContractPlayerIds.Contains(contractEntry.Key))
                        expiredContractPlayerIds.Add(contractEntry.Key);
                }
            }

            bool contractsChanged = false;
            if (expiredContractPlayerIds.Any())
            {
                foreach (ulong playerId in expiredContractPlayerIds)
                {
                    if (_activeContracts.TryGetValue(playerId, out AssassinContractDetails expiredContract))
                    {
                        Entity playerUserEntity = UserUtils.GetUserEntityBySteamID(playerId);
                        if (playerUserEntity != Entity.Null)
                        {
                            ModChatUtils.SendSystemMessageToClient(em, playerUserEntity, ChatColors.ErrorText("Time's up! Your contract has expired. You have failed the association's requests."));
                        }
                        _activeContracts.Remove(playerId);
                        contractsChanged = true;
                    }
                }
            }

            if (contractsChanged)
            {
                SaveActiveContracts();
            }
        }

        public static bool TryGetActiveAssassinContract(ulong playerSteamID, out AssassinContractDetails contractDetails)
        {
            return _activeContracts.TryGetValue(playerSteamID, out contractDetails);
        }

        #endregion

        #region Player-Placed Bounty System
        public static bool IsPlayerBountied(ulong playerSteamID, out BountyDetails bountyDetails)
        {
            return _activeBounties.TryGetValue(playerSteamID, out bountyDetails);
        }

        public static void TryPlaceBounty(Entity placerCharacterEntity, string placerName, ulong placerSteamID, string targetPlayerName, int bountyAmount, ChatCommandContext ctx)
        {
            if (!VWorld.IsServerWorldReady())
            {
                ctx.Reply(ChatColors.ErrorText("Server is not ready."));
                return;
            }

            if (bountyAmount < BountyConfig.MinimumBountyAmount.Value)
            {
                ctx.Reply(ChatColors.ErrorText($"Internal error: Bounty amount {bountyAmount} is less than minimum {BountyConfig.MinimumBountyAmount.Value}."));
                return;
            }
            if (bountyAmount <= 0)
            {
                ctx.Reply(ChatColors.ErrorText("Internal error: Bounty amount must be positive."));
                return;
            }

            var em = VWorld.EntityManager;

            if (!UserUtils.TryGetUserAndCharacterEntity(targetPlayerName, out _, out Entity targetCharacterEntity, out User targetUserData, out string resolvedTargetName))
            {
                ctx.Reply(ChatColors.ErrorText($"Player '{targetPlayerName}' not found or is not online."));
                return;
            }

            if (targetUserData.PlatformId == placerSteamID)
            {
                ctx.Reply(ChatColors.HighlightText("You can't backstab your friend this early!"));
                return;
            }

            if (_activeBounties.ContainsKey(targetUserData.PlatformId))
            {
                ctx.Reply(ChatColors.ErrorText($"{ChatColors.BountyTargetNameText(resolvedTargetName)} already has an active bounty."));
                return;
            }

            PrefabGUID paymentItemGuid = ItemUtils.GetBountyPaymentItemGUID();
            string paymentItemConfiguredName = ItemUtils.GetBountyPaymentItemName();

            if (!InventoryUtils.PlayerHasEnoughItems(placerCharacterEntity, paymentItemGuid, bountyAmount))
            {
                ctx.Reply($"<color={ChatColors.ErrorHex}>You do not have enough {paymentItemConfiguredName} <color={ChatColors.AccentHex}>{bountyAmount}</color> required.</color>");
                return;
            }

            if (!InventoryUtils.TryRemoveItemsFromPlayer(placerCharacterEntity, paymentItemGuid, bountyAmount))
            {
                ctx.Reply(ChatColors.ErrorText($"Failed to take {ChatColors.HighlightText(bountyAmount.ToString())} {ChatColors.BountyItemText(paymentItemConfiguredName)}."));
                return;
            }

            BountyDetails newBounty = new BountyDetails(
                targetUserData.PlatformId, resolvedTargetName, placerSteamID, placerName,
                bountyAmount, paymentItemConfiguredName);
            _activeBounties[targetUserData.PlatformId] = newBounty;
            SaveActiveBounties();

            if (BountyConfig.EnableMapIcons.Value && targetCharacterEntity != Entity.Null)
            {
                BountyMapIcons.AddMapIcon(targetCharacterEntity, targetUserData.PlatformId);
            }

            ctx.Reply(string.Format("{0}{1}{2}{3}.",
                ChatColors.InfoText($"Bounty of {bountyAmount} {paymentItemConfiguredName} "),
                ChatColors.SuccessText("successfully"),
                ChatColors.InfoText(" placed on "),
                ChatColors.BountyTargetNameText(resolvedTargetName)
            ));

            ModChatUtils.SendSystemMessageToAllClients(em,
                string.Format("{0}{1}{2}{3}{4}!",
                    ChatColors.BountyTargetNameText(placerName),
                    ChatColors.InfoText(" has placed a bounty on "),
                    ChatColors.BountyTargetNameText(resolvedTargetName),
                    ChatColors.InfoText(" for "),
                    ChatColors.SuccessText(bountyAmount + " " + paymentItemConfiguredName)
            ));
        }
        #endregion

        public static void HandlePvPKillEvent(
            Entity victimUserEntity, string victimName, ulong victimSteamID,
            Entity killerUserEntity, string killerName, ulong killerSteamID)
        {
            if (!VWorld.IsServerWorldReady()) return;
            var em = VWorld.EntityManager;
            bool activeBountiesFileChanged = false;
            bool leaderboardFileChanged = false;
            bool activeContractsFileChanged = false;

            bool wasClanBetrayalKill = IsClanBetrayal(killerSteamID, victimSteamID);

            if (wasClanBetrayalKill && _activeBounties.ContainsKey(victimSteamID))
            {
                ModChatUtils.SendSystemMessageToClient(em, killerUserEntity, ChatColors.HighlightText("You can't backstab your friend this early!"));
            }

            if (_activeBounties.TryGetValue(victimSteamID, out BountyDetails bountyOnVictim))
            {
                if (!wasClanBetrayalKill)
                {
                    if (killerSteamID == bountyOnVictim.PlacerSteamID)
                    {
                        if (BountyConfig.ForbidPlacerReclaimingPayment.Value)
                        {
                            ModChatUtils.SendSystemMessageToClient(em, killerUserEntity, ChatColors.InfoText($"You killed {ChatColors.BountyTargetNameText(victimName)}, your bounty target. No payment will be given."));
                            ModChatUtils.SendSystemMessageToAllClients(em, $"{ChatColors.BountyPosterNameText(killerName)} neutralized their own bounty on {ChatColors.BountyTargetNameText(victimName)}. They shall not get paid.");
                        }
                        else
                        {
                            ModChatUtils.SendSystemMessageToClient(em, killerUserEntity, ChatColors.SuccessText($"You killed {ChatColors.BountyTargetNameText(victimName)}, your bounty target. Bounty cleared, payment reclaimed."));
                            ModChatUtils.SendSystemMessageToAllClients(em, $"{ChatColors.BountyPosterNameText(killerName)} cleared their own bounty on {ChatColors.BountyTargetNameText(victimName)} and reclaimed the payment!");
                            if (UserUtils.TryGetCharacterEntityFromUserEntity(killerUserEntity, out Entity placerCharacterEntity))
                            {
                                InventoryUtils.TryAddItemsToPlayer(placerCharacterEntity, ItemUtils.GetBountyPaymentItemGUID(), bountyOnVictim.PaymentAmount);
                            }
                            if (BountyConfig.PlacerReclaimAddsToScore.Value)
                            {
                                LeaderboardSystem.UpdatePlayerClaimedBounty(killerSteamID, killerName, bountyOnVictim.PaymentAmount);
                                leaderboardFileChanged = true;
                            }
                        }
                    }
                    else
                    {
                        if (UserUtils.TryGetCharacterEntityFromUserEntity(killerUserEntity, out Entity killerCharacterEntity))
                        {
                            InventoryUtils.TryAddItemsToPlayer(killerCharacterEntity, ItemUtils.GetBountyPaymentItemGUID(), bountyOnVictim.PaymentAmount);
                        }
                        ModChatUtils.SendSystemMessageToClient(em, killerUserEntity, ChatColors.SuccessText($"You collected the {ChatColors.BountyItemText(bountyOnVictim.PaymentAmount + " " + bountyOnVictim.PaymentItemName)} bounty on {ChatColors.BountyTargetNameText(victimName)}!"));
                        ModChatUtils.SendSystemMessageToAllClients(em, $"{ChatColors.BountyTargetNameText(killerName)} collected the bounty on {ChatColors.BountyTargetNameText(victimName)} for {ChatColors.BountyItemText(bountyOnVictim.PaymentAmount + " " + bountyOnVictim.PaymentItemName)}!");
                        LeaderboardSystem.UpdatePlayerClaimedBounty(killerSteamID, killerName, bountyOnVictim.PaymentAmount);
                        leaderboardFileChanged = true;
                    }
                    _activeBounties.Remove(victimSteamID);
                    activeBountiesFileChanged = true;
                    if (BountyConfig.EnableMapIcons.Value) BountyMapIcons.RemoveMapIcon(victimSteamID);
                }
            }

            if (BountyConfig.EnableTargetSelfClaimViaKills.Value && _activeBounties.TryGetValue(killerSteamID, out BountyDetails bountyOnKillerItself))
            {
                if (victimUserEntity != Entity.Null && victimSteamID != killerSteamID)
                {
                    bountyOnKillerItself.KillsAchievedWhileBountied++;
                    _activeBounties[killerSteamID] = bountyOnKillerItself;
                    activeBountiesFileChanged = true;

                    if (bountyOnKillerItself.KillsAchievedWhileBountied >= BountyConfig.KillsForTargetSelfClaim.Value)
                    {
                        ProcessTargetSelfClaim(killerUserEntity, killerName, killerSteamID, bountyOnKillerItself);
                    }
                    else
                    {
                        int killsRemaining = Math.Max(0, BountyConfig.KillsForTargetSelfClaim.Value - bountyOnKillerItself.KillsAchievedWhileBountied);
                        ModChatUtils.SendSystemMessageToClient(em, killerUserEntity, ChatColors.InfoText($"You have an active bounty on you. Get {ChatColors.HighlightText(killsRemaining.ToString())} kill(s) to claim it!"));
                    }
                }
            }

            if (_activeContracts.TryGetValue(killerSteamID, out AssassinContractDetails killerContract))
            {
                if (!killerContract.IsExpired())
                {
                    bool voidKillForContractDueToBetrayal = false;
                    if (wasClanBetrayalKill && BountyConfig.EnableClanBetrayalPrevention.Value)
                    { // Check config again for contract context
                        voidKillForContractDueToBetrayal = true;
                        ModChatUtils.SendSystemMessageToClient(em, killerUserEntity, ChatColors.HighlightText("This kill on a former clanmate does not count towards your contract."));
                    }

                    if (!voidKillForContractDueToBetrayal)
                    {
                        killerContract.KillsAchieved++;
                        if (killerContract.KillsAchieved >= killerContract.KillsRequired)
                        {
                            ModChatUtils.SendSystemMessageToClient(em, killerUserEntity, ChatColors.SuccessText($"Contract complete! You have eliminated {killerContract.KillsRequired} targets."));
                            ModChatUtils.SendSystemMessageToClient(em, killerUserEntity, ChatColors.SuccessText($"Reward: {ChatColors.BountyItemText(killerContract.RewardAmount + " " + BountyConfig.PaymentItemDisplayName.Value)} has been paid."));

                            if (UserUtils.TryGetCharacterEntityFromUserEntity(killerUserEntity, out Entity killerCharacterEntity))
                            {
                                InventoryUtils.TryAddItemsToPlayer(killerCharacterEntity, ItemUtils.GetBountyPaymentItemGUID(), killerContract.RewardAmount);
                            }

                            LeaderboardSystem.UpdatePlayerClaimedBounty(killerSteamID, killerName, killerContract.RewardAmount);
                            leaderboardFileChanged = true;

                            _activeContracts.Remove(killerSteamID);
                        }
                        else
                        {
                            _activeContracts[killerSteamID] = killerContract;
                            int killsRemaining = killerContract.KillsRequired - killerContract.KillsAchieved;
                            ModChatUtils.SendSystemMessageToClient(em, killerUserEntity, ChatColors.InfoText($"Contract progress: {killerContract.KillsAchieved}/{killerContract.KillsRequired} targets eliminated. {ChatColors.HighlightText(killsRemaining.ToString())} more to go."));
                        }
                        activeContractsFileChanged = true;
                    }
                }
                else
                {
                    ModChatUtils.SendSystemMessageToClient(em, killerUserEntity, ChatColors.ErrorText("Too slow! Your contract had already expired. This kill does not count."));
                    _activeContracts.Remove(killerSteamID);
                    activeContractsFileChanged = true;
                }
            }

            if (_activeContracts.TryGetValue(victimSteamID, out AssassinContractDetails victimContractAsAssassin))
            {
                ModChatUtils.SendSystemMessageToClient(em, victimUserEntity, ChatColors.ErrorText("You were eliminated. Your contract has been voided. You have failed the association's requests."));
                _activeContracts.Remove(victimSteamID);
                activeContractsFileChanged = true;
            }

            if (activeBountiesFileChanged) SaveActiveBounties();
            if (leaderboardFileChanged) LeaderboardSystem.SaveLeaderboard();
            if (activeContractsFileChanged) SaveActiveContracts();
        }

        private static void ProcessTargetSelfClaim(Entity targetUserEntity, string targetName, ulong targetSteamID, BountyDetails bountyDetails)
        {
            var em = VWorld.EntityManager;
            ModChatUtils.SendSystemMessageToAllClients(em, $"{ChatColors.BountyTargetNameText(targetName)} achieved {ChatColors.HighlightText(BountyConfig.KillsForTargetSelfClaim.Value.ToString())} kills and has " + ChatColors.SuccessText("claimed their own bounty") + $" of {ChatColors.BountyItemText(bountyDetails.PaymentAmount + " " + bountyDetails.PaymentItemName)}!");

            if (UserUtils.TryGetCharacterEntityFromUserEntity(targetUserEntity, out Entity targetCharacterEntity))
            {
                InventoryUtils.TryAddItemsToPlayer(targetCharacterEntity, ItemUtils.GetBountyPaymentItemGUID(), bountyDetails.PaymentAmount);
            }

            if (BountyConfig.TargetSelfClaimAddsToScore.Value)
            {
                LeaderboardSystem.UpdatePlayerClaimedBounty(targetSteamID, targetName, bountyDetails.PaymentAmount);
                LeaderboardSystem.SaveLeaderboard();
            }

            _activeBounties.Remove(targetSteamID);
            SaveActiveBounties();

            if (BountyConfig.EnableMapIcons.Value) BountyMapIcons.RemoveMapIcon(targetSteamID);
        }

        #region CSV Persistence 
        public static void SaveActiveBounties()
        {
            try
            {
                var lines = new List<string> { "TargetSteamID,TargetName,PlacerSteamID,PlacerName,PaymentAmount,PaymentItemName,KillsAchievedWhileBountied" };
                foreach (var bounty in _activeBounties.Values)
                {
                    lines.Add($"{bounty.TargetSteamID},{EscapeCSV(bounty.TargetName)},{bounty.PlacerSteamID},{EscapeCSV(bounty.PlacerName)},{bounty.PaymentAmount},{EscapeCSV(bounty.PaymentItemName)},{bounty.KillsAchievedWhileBountied}");
                }
                File.WriteAllLines(_activeBountiesFilePath, lines);
            }
            catch (Exception) { }
        }

        private static void LoadActiveBounties()
        {
            if (!File.Exists(_activeBountiesFilePath)) return;
            try
            {
                var lines = File.ReadAllLines(_activeBountiesFilePath);
                if (lines.Length <= 1) return;

                _activeBounties.Clear();
                bool hasKillsAchievedColumn = lines[0].Contains("KillsAchievedWhileBountied");

                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var parts = SplitCsvLine(lines[i]);
                    if (parts.Count >= (hasKillsAchievedColumn ? 7 : 6))
                    {
                        if (ulong.TryParse(parts[0], out ulong targetId) &&
                            ulong.TryParse(parts[2], out ulong placerId) &&
                            int.TryParse(parts[4], out int amount))
                        {
                            int killsAchieved = 0;
                            if (hasKillsAchievedColumn && parts.Count >= 7 && int.TryParse(parts[6], out int parsedKills))
                            {
                                killsAchieved = parsedKills;
                            }

                            BountyDetails bounty = new BountyDetails(targetId, UnescapeCSV(parts[1]), placerId, UnescapeCSV(parts[3]), amount, UnescapeCSV(parts[5]))
                            {
                                KillsAchievedWhileBountied = killsAchieved
                            };
                            _activeBounties[targetId] = bounty;

                            if (BountyConfig.EnableMapIcons.Value && VWorld.IsServerWorldReady())
                            {
                                if (UserUtils.TryGetUserAndCharacterEntity(bounty.TargetName, out _, out Entity charEntity, out User targetUser, out _))
                                {
                                    if (charEntity != Entity.Null && targetUser.IsConnected)
                                    {
                                        BountyMapIcons.AddMapIcon(charEntity, bounty.TargetSteamID);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception) { _activeBounties.Clear(); }
        }

        public static void SaveActiveContracts()
        {
            try
            {
                var lines = new List<string> { "PlayerSteamID,PlayerName,KillsRequired,KillsAchieved,ContractEndTimeUnixTimestamp,RewardAmount" };
                foreach (var contract in _activeContracts.Values)
                {
                    lines.Add($"{contract.PlayerSteamID},{EscapeCSV(contract.PlayerName)},{contract.KillsRequired},{contract.KillsAchieved},{contract.ContractEndTimeUnixTimestamp},{contract.RewardAmount}");
                }
                File.WriteAllLines(_activeContractsFilePath, lines);
            }
            catch (Exception) { }
        }

        private static void LoadActiveContracts()
        {
            if (!File.Exists(_activeContractsFilePath)) return;
            try
            {
                var lines = File.ReadAllLines(_activeContractsFilePath);
                if (lines.Length <= 1) return;

                _activeContracts.Clear();
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var parts = SplitCsvLine(lines[i]);
                    if (parts.Count == 6)
                    {
                        if (ulong.TryParse(parts[0], out ulong playerId) &&
                            int.TryParse(parts[2], out int killsRequired) &&
                            int.TryParse(parts[3], out int killsAchieved) &&
                            long.TryParse(parts[4], out long endTime) &&
                            int.TryParse(parts[5], out int reward))
                        {
                            AssassinContractDetails contract = new AssassinContractDetails(playerId, UnescapeCSV(parts[1]), killsRequired, endTime, reward)
                            {
                                KillsAchieved = killsAchieved
                            };
                            if (!contract.IsExpired())
                            {
                                _activeContracts[playerId] = contract;
                            }
                        }
                    }
                }
            }
            catch (Exception) { _activeContracts.Clear(); }
        }

        private static void SaveClanBetrayalCache()
        {
            if (!BountyConfig.EnableClanBetrayalPrevention.Value) return;
            try
            {
                var lines = new List<string> { "PlayerA_SteamID,PlayerB_SteamID,ExpiryTimestamp" };
                HashSet<Tuple<ulong, ulong>> savedPairs = new HashSet<Tuple<ulong, ulong>>();

                foreach (var outerEntry in _recentClanBetrayalCache)
                {
                    ulong playerA = outerEntry.Key;
                    foreach (var innerEntry in outerEntry.Value)
                    {
                        ulong playerB = innerEntry.Key;
                        long expiry = innerEntry.Value;

                        Tuple<ulong, ulong> pair = playerA < playerB ? Tuple.Create(playerA, playerB) : Tuple.Create(playerB, playerA);
                        if (!savedPairs.Contains(pair))
                        {
                            lines.Add($"{pair.Item1},{pair.Item2},{expiry}");
                            savedPairs.Add(pair);
                        }
                    }
                }
                File.WriteAllLines(_clanBetrayalCacheFilePath, lines);
            }
            catch (Exception) { }
        }

        private static void LoadClanBetrayalCache()
        {
            if (!BountyConfig.EnableClanBetrayalPrevention.Value || !File.Exists(_clanBetrayalCacheFilePath))
            {
                _recentClanBetrayalCache.Clear();
                return;
            }
            try
            {
                var lines = File.ReadAllLines(_clanBetrayalCacheFilePath);
                if (lines.Length <= 1) return;

                _recentClanBetrayalCache.Clear();
                long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var parts = SplitCsvLine(lines[i]);
                    if (parts.Count == 3)
                    {
                        if (ulong.TryParse(parts[0], out ulong playerA_ID) &&
                            ulong.TryParse(parts[1], out ulong playerB_ID) &&
                            long.TryParse(parts[2], out long expiryTimestamp))
                        {
                            if (expiryTimestamp > currentTime)
                            {
                                if (!_recentClanBetrayalCache.ContainsKey(playerA_ID))
                                    _recentClanBetrayalCache[playerA_ID] = new Dictionary<ulong, long>();
                                _recentClanBetrayalCache[playerA_ID][playerB_ID] = expiryTimestamp;

                                if (!_recentClanBetrayalCache.ContainsKey(playerB_ID))
                                    _recentClanBetrayalCache[playerB_ID] = new Dictionary<ulong, long>();
                                _recentClanBetrayalCache[playerB_ID][playerA_ID] = expiryTimestamp;
                            }
                        }
                    }
                }
            }
            catch (Exception) { _recentClanBetrayalCache.Clear(); }
        }


        private static string EscapeCSV(string value) { if (string.IsNullOrEmpty(value)) return ""; if (value.Contains(',') || value.Contains('"') || value.Contains('\n')) { return $"\"{value.Replace("\"", "\"\"")}\""; } return value; }
        private static string UnescapeCSV(string value) { if (string.IsNullOrEmpty(value)) return ""; if (value.StartsWith("\"") && value.EndsWith("\"")) { value = value.Substring(1, value.Length - 2).Replace("\"\"", "\""); } return value; }

        private static List<string> SplitCsvLine(string line)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(line)) return result;

            System.Text.StringBuilder currentField = new System.Text.StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"')
                {
                    if (i + 1 < line.Length && line[i+1] == '\"')
                    {
                        currentField.Append('\"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(currentField.ToString());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(c);
                }
            }
            result.Add(currentField.ToString());
            return result;
        }
        #endregion

        public static void ListActiveBounties(ChatCommandContext ctx)
        {
            if (_activeBounties.Count == 0)
            {
                ctx.Reply(ChatColors.InfoText("No active bounties at this time."));
                return;
            }

            ctx.Reply(ChatColors.HighlightText("--- Active  Bounties ---"));
            int count = 1;
            foreach (var bounty in _activeBounties.Values.OrderByDescending(b => b.PaymentAmount))
            {
                string rankText = ChatColors.HighlightText(count.ToString() + ".");
                string targetLabel = ChatColors.HighlightText("Target:");
                string targetNameText = ChatColors.InfoText(bounty.TargetName);
                string placerLabel = ChatColors.HighlightText("Placed by:");
                string placerNameText = ChatColors.InfoText(bounty.PlacerName);
                string rewardLabel = ChatColors.HighlightText("Reward:");
                string rewardText = ChatColors.InfoText(bounty.PaymentAmount + " " + bounty.PaymentItemName);
                ctx.Reply($"{rankText} {targetLabel} {targetNameText}, {placerLabel} {placerNameText}, {rewardLabel} {rewardText}");
                count++;
            }
        }
    }
}

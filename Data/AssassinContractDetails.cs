using System;

namespace BountyForge.Data
{
    public class AssassinContractDetails
    {
        public ulong PlayerSteamID { get; set; }
        public string PlayerName { get; set; } 
        public int KillsRequired { get; set; }
        public int KillsAchieved { get; set; }
        public long ContractEndTimeUnixTimestamp { get; set; }
        public int RewardAmount { get; set; } 
        public AssassinContractDetails() { }

        public AssassinContractDetails(ulong steamID, string playerName, int killsRequired, long contractEndTime, int rewardAmount)
        {
            PlayerSteamID = steamID;
            PlayerName = playerName ?? "UnknownContractHolder";
            KillsRequired = killsRequired;
            KillsAchieved = 0;
            ContractEndTimeUnixTimestamp = contractEndTime;
            RewardAmount = rewardAmount;
        }

        public bool IsExpired()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= ContractEndTimeUnixTimestamp;
        }
    }
}
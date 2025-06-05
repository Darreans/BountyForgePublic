namespace BountyForge.Data
{
    public struct LeaderboardEntry
    {
        public ulong SteamID;
        public string PlayerName; 
        public long TotalBountyAmountClaimed;

        public LeaderboardEntry(ulong steamID, string playerName, long totalBountyAmountClaimed)
        {
            SteamID = steamID;
            PlayerName = playerName;
            TotalBountyAmountClaimed = totalBountyAmountClaimed;
        }
    }
}
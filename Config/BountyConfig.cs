using BepInEx.Configuration;

namespace BountyForge.Config
{
    public static class BountyConfig
    {
        public static ConfigEntry<bool> ModEnabled { get; private set; }

        public static ConfigEntry<int> PaymentItemPrefabGUID { get; private set; }
        public static ConfigEntry<string> PaymentItemDisplayName { get; private set; }
        public static ConfigEntry<int> MinimumBountyAmount { get; private set; }

        public static ConfigEntry<bool> ForbidPlacerReclaimingPayment { get; private set; }
        public static ConfigEntry<bool> EnableTargetSelfClaimViaKills { get; private set; }
        public static ConfigEntry<int> KillsForTargetSelfClaim { get; private set; }

        public static ConfigEntry<bool> TargetSelfClaimAddsToScore { get; private set; }
        public static ConfigEntry<bool> PlacerReclaimAddsToScore { get; private set; }

        public static ConfigEntry<bool> EnableMapIcons { get; private set; }
        public static ConfigEntry<int> BountyMapIconGUID { get; private set; }

        public static ConfigEntry<bool> EnableAssassinContracts { get; private set; }
        public static ConfigEntry<int> AssassinContractOfferMinIntervalSeconds { get; private set; }
        public static ConfigEntry<int> AssassinContractOfferMaxIntervalSeconds { get; private set; }
        public static ConfigEntry<int> AssassinContractMinKillsRequired { get; private set; }
        public static ConfigEntry<int> AssassinContractMaxKillsRequired { get; private set; }
        public static ConfigEntry<int> AssassinContractTimeLimitSeconds { get; private set; }
        public static ConfigEntry<int> AssassinContractMinRewardAmount { get; private set; }
        public static ConfigEntry<int> AssassinContractMaxRewardAmount { get; private set; }
        public static ConfigEntry<int> MaxActiveAssassinContracts { get; private set; }

        public static ConfigEntry<bool> EnableClanBetrayalPrevention { get; private set; }
        public static ConfigEntry<int> ClanBetrayalCooldownSeconds { get; private set; }


        public static ConfigEntry<bool> DebugLoggingEnabled { get; private set; } 

        public static void Initialize(ConfigFile config)
        {
            ModEnabled = config.Bind("General", "Enabled", true, "Enable or disable the BountyForge mod.");
            DebugLoggingEnabled = config.Bind("Debug", "DebugLogging", false, "Enable detailed debug logging for development (currently mostly affects non-BountyManager logs).");

            PaymentItemPrefabGUID = config.Bind("Bounty.Placement", "PaymentItemGUID", 576389135, "Prefab GUID of the item for bounty payment (Default: Greater Stygian Shard).");
            PaymentItemDisplayName = config.Bind("Bounty.Placement", "PaymentItemName", "Greater Stygian Shard", "Display name for the payment item.");
            MinimumBountyAmount = config.Bind("Bounty.Placement", "MinimumBountyAmount", 500,
                "The minimum amount of the payment item required to place a bounty.");

            ForbidPlacerReclaimingPayment = config.Bind("Bounty.Claiming", "ForbidPlacerReclaimingPayment", true,
                "If TRUE, a player who placed a bounty does NOT get their payment back if they kill their target (bounty is voided, items lost).\nIf FALSE, the bounty is cleared and they effectively 'reclaim' their stake (items returned by default, bounty is settled without payout to others).");
            EnableTargetSelfClaimViaKills = config.Bind("Bounty.Claiming", "EnableTargetSelfClaimViaKills", true,
                "If TRUE, a player with a bounty ON THEM can claim their own bounty reward by achieving a set number of kills.");
            KillsForTargetSelfClaim = config.Bind("Bounty.Claiming", "KillsForTargetSelfClaim", 5,
                "Number of kills a bountied player must achieve to self-claim their bounty (if EnableTargetSelfClaimViaKills is true).");

            TargetSelfClaimAddsToScore = config.Bind("Leaderboard", "TargetSelfClaimAddsToScore", true,
                "If TRUE, when a target self-claims their bounty via kills, the bounty amount is added to their leaderboard score.");
            PlacerReclaimAddsToScore = config.Bind("Leaderboard", "PlacerReclaimAddsToScore", false,
                "If TRUE (and ForbidPlacerReclaimingPayment is FALSE), when a placer kills their own target and reclaims the payment, the bounty amount is added to their leaderboard score.");

            EnableMapIcons = config.Bind("MapIcons", "EnableMapIcons", true, "Enable map icons for players with (player-placed) bounties.");
            BountyMapIconGUID = config.Bind("MapIcons", "BountyMapIconGUID", 1501929529, "Prefab GUID for the map icon (Default: Hostile Bounty Player POI).");

            EnableAssassinContracts = config.Bind("AssassinContractSystem", "EnableAssassinContracts", true,
                "Enable the system to periodically offer assassination contracts to random players.");
            AssassinContractOfferMinIntervalSeconds = config.Bind("AssassinContractSystem", "OfferMinIntervalSeconds", 7200,
                "Minimum time (in seconds) before a new assassin contract is offered to a player.");
            AssassinContractOfferMaxIntervalSeconds = config.Bind("AssassinContractSystem", "OfferMaxIntervalSeconds", 14400,
                "Maximum time (in seconds) before a new assassin contract is offered to a player.");
            AssassinContractMinKillsRequired = config.Bind("AssassinContractSystem", "MinKillsRequired", 3,
                "Minimum number of player kills required for an assassin contract.");
            AssassinContractMaxKillsRequired = config.Bind("AssassinContractSystem", "MaxKillsRequired", 7,
                "Maximum number of player kills required for an assassin contract.");
            AssassinContractTimeLimitSeconds = config.Bind("AssassinContractSystem", "TimeLimitSeconds", 2700,
                "Time (in seconds) a player has to complete an assassin contract.");
            AssassinContractMinRewardAmount = config.Bind("AssassinContractSystem", "MinRewardAmount", 1000,
                "Minimum reward amount (of the global PaymentItem) for completing an assassin contract.");
            AssassinContractMaxRewardAmount = config.Bind("AssassinContractSystem", "MaxRewardAmount", 5000,
                "Maximum reward amount (of the global PaymentItem) for completing an assassin contract. A random amount between Min and Max will be chosen.");
            MaxActiveAssassinContracts = config.Bind("AssassinContractSystem", "MaxActiveContracts", 3,
                "Maximum number of players that can have an active assassin contract at the same time. Set to 0 for unlimited.");

            EnableClanBetrayalPrevention = config.Bind("Social", "EnableClanBetrayalPrevention", true,
                "If true, prevents players from claiming bounties on former clanmates for a configured duration after leaving/being kicked.");
            ClanBetrayalCooldownSeconds = config.Bind("Social", "ClanBetrayalCooldownSeconds", 14400, 
                "Duration (in seconds) for which the clan betrayal bounty claim prevention lasts.");
        }
    }
}

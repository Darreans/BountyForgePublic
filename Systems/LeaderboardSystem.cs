// BountyForge/Systems/LeaderboardSystem.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BountyForge.Data;
using BountyForge.Utils;
using System.Text; // For StringBuilder

namespace BountyForge.Systems
{
    public static class LeaderboardSystem
    {
        private static Dictionary<ulong, LeaderboardEntry> _leaderboard = new();
        private static string _leaderboardFilePath;

        public static void Initialize(string dataStorageBasePath)
        {
            _leaderboardFilePath = Path.Combine(dataStorageBasePath, "leaderboard.csv");
            LoggingHelper.Info($"[LeaderboardSystem] Leaderboard file path: {_leaderboardFilePath}");
            LoadLeaderboard();
        }

        public static void UpdatePlayerClaimedBounty(ulong killerSteamID, string killerName, int amountClaimed)
        {
            if (amountClaimed <= 0) return;

            if (_leaderboard.TryGetValue(killerSteamID, out LeaderboardEntry entry))
            {
                entry.TotalBountyAmountClaimed += amountClaimed;
                entry.PlayerName = killerName; // Update name in case it changed
                _leaderboard[killerSteamID] = entry;
            }
            else
            {
                _leaderboard[killerSteamID] = new LeaderboardEntry(killerSteamID, killerName, amountClaimed);
            }
            LoggingHelper.Debug($"[LeaderboardSystem] Updated leaderboard for {killerName} (ID: {killerSteamID}). New total: {_leaderboard[killerSteamID].TotalBountyAmountClaimed}.");
        }

        public static List<LeaderboardEntry> GetLeaderboard(int topN = 10)
        {
            return _leaderboard.Values
                .OrderByDescending(e => e.TotalBountyAmountClaimed)
                .Take(topN)
                .ToList();
        }

        public static void SaveLeaderboard()
        {
            try
            {
                var lines = new List<string> { "SteamID,PlayerName,TotalBountyAmountClaimed" };
                foreach (var entry in _leaderboard.Values.OrderByDescending(e => e.TotalBountyAmountClaimed))
                {
                    lines.Add($"{entry.SteamID},{EscapeCSV(entry.PlayerName)},{entry.TotalBountyAmountClaimed}");
                }
                File.WriteAllLines(_leaderboardFilePath, lines);
                LoggingHelper.Info($"[LeaderboardSystem] Leaderboard saved to {_leaderboardFilePath}. Count: {_leaderboard.Count}");
            }
            catch (Exception ex)
            {
                LoggingHelper.Error($"[LeaderboardSystem] Failed to save leaderboard: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void LoadLeaderboard()
        {
            if (!File.Exists(_leaderboardFilePath))
            {
                LoggingHelper.Info($"[LeaderboardSystem] Leaderboard file not found at {_leaderboardFilePath}. Starting fresh.");
                return;
            }
            try
            {
                var lines = File.ReadAllLines(_leaderboardFilePath);
                if (lines.Length <= 1)
                {
                    LoggingHelper.Info("[LeaderboardSystem] Leaderboard file is empty or contains only header.");
                    return;
                }

                _leaderboard.Clear();
                var header = SplitCsvLine(lines[0]);
                Func<string, int> GetColumnIndex = (name) => header.IndexOf(name); 

                int steamIDIdx = GetColumnIndex("SteamID");
                int playerNameIdx = GetColumnIndex("PlayerName");
                int totalBountyAmountClaimedIdx = GetColumnIndex("TotalBountyAmountClaimed");

                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var parts = SplitCsvLine(lines[i]);

                    if (steamIDIdx != -1 && playerNameIdx != -1 && totalBountyAmountClaimedIdx != -1 &&
                        parts.Count > Math.Max(steamIDIdx, Math.Max(playerNameIdx, totalBountyAmountClaimedIdx)))
                    {
                        try
                        {
                            if (ulong.TryParse(parts[steamIDIdx], out ulong steamId) &&
                                long.TryParse(parts[totalBountyAmountClaimedIdx], out long totalAmount))
                            {
                                _leaderboard[steamId] = new LeaderboardEntry(steamId, UnescapeCSV(parts[playerNameIdx]), totalAmount);
                            }
                            else { LoggingHelper.Warning($"[LeaderboardSystem] Failed to parse leaderboard line (type conversion): {lines[i]}"); }
                        }
                        catch (Exception ex)
                        {
                            LoggingHelper.Warning($"[LeaderboardSystem] Failed to parse leaderboard line (exception during parsing): {lines[i]} - {ex.Message}");
                        }
                    }
                    else { LoggingHelper.Warning($"[LeaderboardSystem] Malformed leaderboard line (missing columns or invalid header): {lines[i]}"); }
                }
                LoggingHelper.Info($"[LeaderboardSystem] Loaded {_leaderboard.Count} leaderboard entries from {_leaderboardFilePath}.");
            }
            catch (Exception ex)
            {
                LoggingHelper.Error($"[LeaderboardSystem] Failed to load leaderboard: {ex.Message}\n{ex.StackTrace}");
                _leaderboard.Clear();
            }
        }

        private static string EscapeCSV(string value)
        {
            if (value == null) return "";
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }
            return value;
        }

        private static string UnescapeCSV(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (value.StartsWith("\"") && value.EndsWith("\""))
            {
                value = value.Substring(1, value.Length - 2).Replace("\"\"", "\"");
            }
            return value;
        }

        private static List<string> SplitCsvLine(string line)
        {
            List<string> result = new List<string>();
            StringBuilder currentField = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '\"')
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
    }
}

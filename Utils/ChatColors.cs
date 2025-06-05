namespace BountyForge.Utils
{
    public static class ChatColors
    {
        public const string ErrorHex = "#FF4136";
        public const string SuccessHex = "#2ECC40";
        public const string InfoHex = "#FFFFFF";
        public const string WarningHex = "#FF851B";     
        public const string HighlightHex = "#7FDBFF";
        public const string AccentHex = "#FFDC00";
        public const string MutedHex = "#AAAAAA";

        public const string BountyTargetNameHex = "#E74C3C";
        public const string BountyPosterNameHex = "#3498DB";
        public const string BountyItemHex = "#F1C40F";

        public const string CommandSyntaxHex = "#95A5A6";
        public const string CommandDescriptionHex = "#BDC3C7";

        private static string Format(string message, string colorHex)
        {
            if (string.IsNullOrEmpty(colorHex) || string.IsNullOrEmpty(message))
            {
                return message ?? string.Empty; 
            }
            return $"<color={colorHex}>{message}</color>";
        }

        public static string ErrorText(string message) => Format(message, ErrorHex);
        public static string SuccessText(string message) => Format(message, SuccessHex);
        public static string InfoText(string message) => Format(message, InfoHex);
        public static string WarningText(string message) => Format(message, WarningHex);
        public static string HighlightText(string message) => Format(message, HighlightHex);
        public static string AccentText(string message) => Format(message, AccentHex);
        public static string MutedText(string message) => Format(message, MutedHex);

        public static string BountyTargetNameText(string playerName) => Format(playerName, BountyTargetNameHex);
        public static string BountyPosterNameText(string playerName) => Format(playerName, BountyPosterNameHex);
        public static string BountyItemText(string itemName) => Format(itemName, BountyItemHex);

        public static string CommandSyntaxText(string syntax) => Format(syntax, CommandSyntaxHex);
        public static string CommandDescriptionText(string description) => Format(description, CommandDescriptionHex);

        
    }
}
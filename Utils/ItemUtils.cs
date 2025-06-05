using ProjectM;
using Stunlock.Core;
using BountyForge.Config;
using BountyForge.Utils;

namespace BountyForge.Utils
{
    public static class ItemUtils
    {
        public static void Initialize()
        {
            
        }

        public static PrefabGUID GetBountyPaymentItemGUID()
        {
         
            if (BountyConfig.PaymentItemPrefabGUID == null)
            {
                LoggingHelper.Error("[ItemUtils] PaymentItemPrefabGUID from BountyConfig is null! Check BountyConfig initialization.");
                return new PrefabGUID(0); 
            }
            return new PrefabGUID(BountyConfig.PaymentItemPrefabGUID.Value);
        }

        public static string GetBountyPaymentItemName()
        {
            if (BountyConfig.PaymentItemDisplayName == null)
            {
                LoggingHelper.Error("[ItemUtils] PaymentItemDisplayName from BountyConfig is null! Check BountyConfig initialization.");
                return "ErrorItem";
            }
            return BountyConfig.PaymentItemDisplayName.Value;
        }

      
        public static string GetItemNameFromGUID(PrefabGUID itemGuid)
        {
           
            if (BountyConfig.PaymentItemPrefabGUID != null && itemGuid.GuidHash == BountyConfig.PaymentItemPrefabGUID.Value)
            {
                return GetBountyPaymentItemName();
            }
            return $"ItemGUID({itemGuid.GuidHash})";
        }
    }
}

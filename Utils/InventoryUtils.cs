using ProjectM;
using Stunlock.Core;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using System;

namespace BountyForge.Utils
{
    public static class InventoryUtils
    {
        private static bool TryGetItemData(PrefabGUID itemGuid, out ItemData itemData)
        {
            itemData = default;
            if (!VWorld.IsServerWorldReady())
            {
                LoggingHelper.Error("[InventoryUtils.TryGetItemData] Server world not ready.");
                return false;
            }
            GameDataSystem gameDataSystem = VWorld.Server.GetExistingSystemManaged<GameDataSystem>();
            if (gameDataSystem == null || !gameDataSystem.ItemHashLookupMap.IsCreated)
            {
                LoggingHelper.Warning($"[InventoryUtils.TryGetItemData] GameDataSystem not available or ItemHashLookupMap not ready for {itemGuid.GuidHash}.");
                return false;
            }
            if (!gameDataSystem.ItemHashLookupMap.TryGetValue(itemGuid, out itemData))
            {
                LoggingHelper.Warning($"[InventoryUtils.TryGetItemData] ItemData not found in ItemHashLookupMap for GUID {itemGuid.GuidHash}.");
                return false;
            }
            return true;
        }

        private static bool TryGetItemDataLookup(out NativeParallelHashMap<PrefabGUID, ItemData> itemDataLookup)
        {
            itemDataLookup = default;
            if (!VWorld.IsServerWorldReady()) { LoggingHelper.Error("[InventoryUtils.TryGetItemDataLookup] Server world not ready."); return false; }
            GameDataSystem gameDataSystem = VWorld.Server.GetExistingSystemManaged<GameDataSystem>();
            if (gameDataSystem == null) { LoggingHelper.Error("[InventoryUtils.TryGetItemDataLookup] GameDataSystem not found."); return false; }
            itemDataLookup = gameDataSystem.ItemHashLookupMap;
            if (!itemDataLookup.IsCreated)
            {
                LoggingHelper.Error("[InventoryUtils.TryGetItemDataLookup] ItemHashLookupMap is not created.");
                return false;
            }
            return true;
        }

        private static bool TryGetCommandBuffer(out EntityCommandBuffer commandBuffer)
        {
            commandBuffer = default;
            if (!VWorld.IsServerWorldReady()) { LoggingHelper.Error("[InventoryUtils.TryGetCommandBuffer] Server world not ready."); return false; }
            var ecbSystem = VWorld.Server.GetExistingSystemManaged<EndSimulationEntityCommandBufferSystem>();
            if (ecbSystem == null) { LoggingHelper.Error("[InventoryUtils.TryGetCommandBuffer] EndSimulationEntityCommandBufferSystem not found."); return false; }
            commandBuffer = ecbSystem.CreateCommandBuffer();
            return true;
        }

        public static bool PlayerHasEnoughItems(Entity playerCharacterEntity, PrefabGUID itemGuid, int neededAmount)
        {
            if (neededAmount <= 0) return true;
            if (!VWorld.IsServerWorldReady()) { LoggingHelper.Error("[InventoryUtils.PlayerHasEnoughItems] Server world not ready."); return false; }
            EntityManager em = VWorld.EntityManager;

            if (playerCharacterEntity == Entity.Null || !em.Exists(playerCharacterEntity))
            {
                LoggingHelper.Warning($"[InventoryUtils.PlayerHasEnoughItems] Invalid or non-existent playerCharacterEntity.");
                return false;
            }

            if (!ProjectM.InventoryUtilities.TryGetInventoryEntity(em, playerCharacterEntity, out var inventoryEntity))
            {
                LoggingHelper.Debug($"[InventoryUtils.PlayerHasEnoughItems] Could not get inventory entity for player {playerCharacterEntity}.");
                return false;
            }
            if (!em.HasBuffer<InventoryBuffer>(inventoryEntity))
            {
                LoggingHelper.Debug($"[InventoryUtils.PlayerHasEnoughItems] Inventory entity {inventoryEntity} for player {playerCharacterEntity} has no InventoryBuffer.");
                return false;
            }

            var buffer = em.GetBuffer<InventoryBuffer>(inventoryEntity);
            int count = 0;
            foreach (var slot in buffer) { if (slot.ItemType == itemGuid) { count += slot.Amount; } }
            LoggingHelper.Debug($"[InventoryUtils.PlayerHasEnoughItems] Player {playerCharacterEntity} has {count} of item {itemGuid.GuidHash}. Needed: {neededAmount}.");
            return (count >= neededAmount);
        }

        public static bool TryRemoveItemsFromPlayer(Entity playerCharacterEntity, PrefabGUID itemGuid, int amountToRemove)
        {
            if (amountToRemove <= 0) return true;
            if (!VWorld.IsServerWorldReady()) { LoggingHelper.Error("[InventoryUtils.TryRemoveItemsFromPlayer] Server world not ready."); return false; }

            EntityManager em = VWorld.EntityManager;
            if (playerCharacterEntity == Entity.Null || !em.Exists(playerCharacterEntity))
            {
                LoggingHelper.Warning($"[InventoryUtils.TryRemoveItemsFromPlayer] Invalid or non-existent playerCharacterEntity.");
                return false;
            }

            bool success = InventoryUtilitiesServer.TryRemoveItem(em, playerCharacterEntity, itemGuid, amountToRemove);
            if (!success)
            {
                LoggingHelper.Warning($"[InventoryUtils] InventoryUtilitiesServer.TryRemoveItem failed to remove {amountToRemove} of item {itemGuid.GuidHash} from player {playerCharacterEntity}.");
            }
            else
            {
                LoggingHelper.Info($"[InventoryUtils] Successfully removed {amountToRemove} of item {itemGuid.GuidHash} from player {playerCharacterEntity}.");
            }
            return success;
        }

        public static ItemGiveStatus TryAddItemsToPlayer(Entity playerCharacterEntity, PrefabGUID itemGuid, int amountToAdd)
        {
            LoggingHelper.Info($"[InventoryUtils.TryAddItemsToPlayer] Entered. TargetChar: {playerCharacterEntity}, ItemGUID: {itemGuid.GuidHash}, Amount: {amountToAdd}");

            if (amountToAdd <= 0)
            {
                LoggingHelper.Debug("[InventoryUtils.TryAddItemsToPlayer] Amount to add is 0 or less. Returning AddedToInventory (no action).");
                return ItemGiveStatus.AddedToInventory;
            }

            if (!VWorld.IsServerWorldReady())
            {
                LoggingHelper.Error("[InventoryUtils.TryAddItemsToPlayer] Server world not ready.");
                return ItemGiveStatus.Failed;
            }
            EntityManager em = VWorld.EntityManager;

            if (playerCharacterEntity == Entity.Null || !em.Exists(playerCharacterEntity))
            {
                LoggingHelper.Error($"[InventoryUtils.TryAddItemsToPlayer] Player character entity {playerCharacterEntity} does not exist or is null.");
                return ItemGiveStatus.Failed;
            }

            if (itemGuid.GuidHash == 0)
            {
                LoggingHelper.Error($"[InventoryUtils.TryAddItemsToPlayer] Invalid itemGuid (0) passed. Cannot add item.");
                return ItemGiveStatus.Failed;
            }


            int remainingAmountToAdd = amountToAdd;
            bool itemsWereAddedToInventorySlots = false;

            LoggingHelper.Debug($"[InventoryUtils] Attempting to add {amountToAdd} of {itemGuid.GuidHash} to {playerCharacterEntity}.");

            if (ProjectM.InventoryUtilities.TryGetInventoryEntity(em, playerCharacterEntity, out var inventoryEntity) &&
                em.HasBuffer<InventoryBuffer>(inventoryEntity))
            {
                var buffer = em.GetBuffer<InventoryBuffer>(inventoryEntity);
                int maxStackSize = 1;

                if (TryGetItemData(itemGuid, out ItemData itemData) && itemData.MaxAmount > 0)
                {
                    maxStackSize = itemData.MaxAmount;
                }
                else
                {
                    LoggingHelper.Warning($"[InventoryUtils] Could not determine valid MaxAmount for {itemGuid.GuidHash}. Defaulting stack size to {maxStackSize}. Stacking might be imperfect.");
                }
                LoggingHelper.Debug($"[InventoryUtils] Max stack size for {itemGuid.GuidHash} is {maxStackSize}. Attempting to add {remainingAmountToAdd}.");

                for (int i = 0; i < buffer.Length && remainingAmountToAdd > 0; i++)
                {
                    if (buffer[i].ItemType == itemGuid && buffer[i].Amount < maxStackSize)
                    {
                        int canAddToStack = Math.Min(remainingAmountToAdd, maxStackSize - buffer[i].Amount);
                        InventoryBuffer slot = buffer[i];
                        slot.Amount += canAddToStack;
                        buffer[i] = slot;
                        remainingAmountToAdd -= canAddToStack;
                        itemsWereAddedToInventorySlots = true;
                        LoggingHelper.Debug($"[InventoryUtils] Added {canAddToStack} to existing stack in slot {i}. Remaining: {remainingAmountToAdd}. Slot now: {slot.Amount}");
                    }
                }
                if (remainingAmountToAdd > 0)
                {
                    for (int i = 0; i < buffer.Length && remainingAmountToAdd > 0; i++)
                    {
                        if (buffer[i].ItemType.Equals(PrefabGUID.Empty))
                        {
                            int canPlaceInNewStack = Math.Min(remainingAmountToAdd, maxStackSize);
                            buffer[i] = new InventoryBuffer { ItemType = itemGuid, Amount = canPlaceInNewStack };
                            remainingAmountToAdd -= canPlaceInNewStack;
                            itemsWereAddedToInventorySlots = true;
                            LoggingHelper.Debug($"[InventoryUtils] Added {canPlaceInNewStack} to new stack in empty slot {i}. Remaining: {remainingAmountToAdd}.");
                        }
                    }
                }

                if (remainingAmountToAdd == 0)
                {
                    LoggingHelper.Info($"[InventoryUtils] Successfully added all {amountToAdd} of item {itemGuid.GuidHash} to player {playerCharacterEntity}'s inventory slots.");
                    return ItemGiveStatus.AddedToInventory;
                }
                else
                {
                    LoggingHelper.Warning($"[InventoryUtils] Inventory full or no suitable slots after direct add for {itemGuid.GuidHash}. Remaining {remainingAmountToAdd} to be dropped.");
                }
            }
            else
            {
                LoggingHelper.Warning($"[InventoryUtils] Could not get inventory buffer for {playerCharacterEntity}. Proceeding to drop attempt for all {amountToAdd} items.");
                remainingAmountToAdd = amountToAdd;
            }

            if (remainingAmountToAdd > 0)
            {
                int totalToDropInitially = remainingAmountToAdd;
                LoggingHelper.Info($"[InventoryUtils] Attempting to drop {totalToDropInitially} of {itemGuid.GuidHash} for player {playerCharacterEntity}.");

                try
                {
                    if (!em.HasComponent<LocalToWorld>(playerCharacterEntity))
                    {
                        LoggingHelper.Error($"[InventoryUtils] Player character {playerCharacterEntity} missing LocalToWorld. Cannot get drop position.");
                        return itemsWereAddedToInventorySlots ? ItemGiveStatus.PartiallyAddedAndDropped : ItemGiveStatus.Failed;
                    }
                    float3 dropPosition = em.GetComponentData<LocalToWorld>(playerCharacterEntity).Position;
                    dropPosition.y += 0.3f;

                    if (!TryGetItemData(itemGuid, out ItemData itemDataForDrop))
                    {
                        LoggingHelper.Error($"[InventoryUtils] Could not get ItemData for {itemGuid.GuidHash} for dropping (MaxAmount needed). Aborting drop.");
                        return itemsWereAddedToInventorySlots ? ItemGiveStatus.PartiallyAddedAndDropped : ItemGiveStatus.Failed;
                    }
                    if (!TryGetItemDataLookup(out var itemDataLookup))
                    {
                        LoggingHelper.Error("[InventoryUtils] Drop: Could not get ItemDataLookup. Cannot drop items.");
                        return itemsWereAddedToInventorySlots ? ItemGiveStatus.PartiallyAddedAndDropped : ItemGiveStatus.Failed;
                    }
                    if (!TryGetCommandBuffer(out var commandBuffer))
                    {
                        LoggingHelper.Error("[InventoryUtils] Drop: Could not create EntityCommandBuffer. Cannot drop items.");
                        return itemsWereAddedToInventorySlots ? ItemGiveStatus.PartiallyAddedAndDropped : ItemGiveStatus.Failed;
                    }

                    int maxStackForDrop = itemDataForDrop.MaxAmount > 0 ? itemDataForDrop.MaxAmount : 1;
                    LoggingHelper.Debug($"[InventoryUtils] Dropping {totalToDropInitially} of {itemGuid.GuidHash} in stacks up to {maxStackForDrop} at {dropPosition}.");

                    int actuallyDroppedAmount = 0;
                    int amountLeftToDropIteration = totalToDropInitially;

                    while (amountLeftToDropIteration > 0)
                    {
                        int currentDropAmount = Math.Min(amountLeftToDropIteration, maxStackForDrop);
                        LoggingHelper.Info($"[InventoryUtils] Queuing drop via CreateDroppedItemEntity: ItemGUID={itemGuid.GuidHash}, Amount={currentDropAmount}, Pos={dropPosition}");
                        InventoryUtilitiesServer.CreateDroppedItemEntity(em, commandBuffer, itemDataLookup, dropPosition, itemGuid, currentDropAmount);

                        actuallyDroppedAmount += currentDropAmount;
                        amountLeftToDropIteration -= currentDropAmount;
                        if (amountLeftToDropIteration > 0) dropPosition.x += 0.2f;
                    }
                    LoggingHelper.Info($"[InventoryUtils] Successfully queued drop of {actuallyDroppedAmount} items for {itemGuid.GuidHash} near player {playerCharacterEntity}.");
                    return itemsWereAddedToInventorySlots ? ItemGiveStatus.PartiallyAddedAndDropped : ItemGiveStatus.DroppedOnGround;
                }
                catch (System.Exception ex)
                {
                    LoggingHelper.Error($"[InventoryUtils] Drop: Exception: {ex.Message}\n{ex.StackTrace}");
                    return itemsWereAddedToInventorySlots ? ItemGiveStatus.PartiallyAddedAndDropped : ItemGiveStatus.Failed;
                }
            }
            return ItemGiveStatus.AddedToInventory;
        }
    }
}

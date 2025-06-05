// BountyForge/Systems/BountyMapIcons.cs
using Unity.Entities;
using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using System.Collections.Generic;
using BountyForge.Config;
using BountyForge.Utils;
using System.Linq;
using Unity.Collections;
using System;

namespace BountyForge.Systems
{
    public static class BountyMapIcons
    {
        private static PrefabGUID _configuredDefaultBountyIconStyleGuid;
        private static bool _mapIconsSystemEnabled;
        private static Dictionary<PrefabGUID, Entity> _iconStylePrefabEntityCache = new Dictionary<PrefabGUID, Entity>();
        private static Dictionary<ulong, Entity> _activePlayerBountyIcons = new Dictionary<ulong, Entity>();

        public static void Initialize()
        {
            _activePlayerBountyIcons.Clear();
            _iconStylePrefabEntityCache.Clear();

            _mapIconsSystemEnabled = BountyConfig.EnableMapIcons.Value;
            if (!_mapIconsSystemEnabled)
            {
                return;
            }

            _configuredDefaultBountyIconStyleGuid = new PrefabGUID(BountyConfig.BountyMapIconGUID.Value);

            if (_configuredDefaultBountyIconStyleGuid.GuidHash != 0)
            {
                TryCacheIconStylePrefab(_configuredDefaultBountyIconStyleGuid, out _);
            }
        }

        private static bool TryCacheIconStylePrefab(PrefabGUID iconStyleGuid, out Entity prefabEntity)
        {
            prefabEntity = Entity.Null;
            if (iconStyleGuid.GuidHash == 0)
            {
                return false;
            }
            if (_iconStylePrefabEntityCache.TryGetValue(iconStyleGuid, out Entity cachedEntity))
            {
                if (VWorld.IsServerWorldReady() && VWorld.EntityManager.Exists(cachedEntity))
                {
                    prefabEntity = cachedEntity;
                    return true;
                }
                else { _iconStylePrefabEntityCache.Remove(iconStyleGuid); }
            }
            if (!VWorld.IsServerWorldReady()) return false;
            var prefabCollectionSystem = VWorld.Server.GetExistingSystemManaged<PrefabCollectionSystem>();
            if (prefabCollectionSystem != null && prefabCollectionSystem._PrefabGuidToEntityMap.IsCreated && prefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(iconStyleGuid, out Entity sourcePrefabEntity))
            {
                _iconStylePrefabEntityCache[iconStyleGuid] = sourcePrefabEntity;
                prefabEntity = sourcePrefabEntity;
                return true;
            }
            return false;
        }

        public static void AddMapIcon(Entity targetCharacterEntity, ulong targetSteamID)
        {
            AddMapIcon(targetCharacterEntity, targetSteamID, _configuredDefaultBountyIconStyleGuid);
        }

        public static void AddMapIcon(Entity targetCharacterEntity, ulong targetSteamID, PrefabGUID iconStyleGuidToUse)
        {
            if (!_mapIconsSystemEnabled) return;

            PrefabGUID finalIconStyleGuid = iconStyleGuidToUse;

            if (finalIconStyleGuid.GuidHash == 0)
            {
                return;
            }

            if (!VWorld.IsServerWorldReady()) return;
            if (!TryCacheIconStylePrefab(finalIconStyleGuid, out Entity iconPrefabEntity) || iconPrefabEntity == Entity.Null)
            {
                return;
            }

            if (_activePlayerBountyIcons.TryGetValue(targetSteamID, out Entity existingIconEntity))
            {
                RemoveMapIconInternal(targetSteamID, existingIconEntity, true);
            }

            var em = VWorld.EntityManager;
            if (!em.Exists(targetCharacterEntity) || !em.HasComponent<PlayerCharacter>(targetCharacterEntity))
            {
                return;
            }

            try
            {
                Entity spawnedIconEntity = em.Instantiate(iconPrefabEntity);

                if (!em.HasComponent<MapIconData>(spawnedIconEntity)) em.AddComponent<MapIconData>(spawnedIconEntity);
                MapIconData mapIconData = em.GetComponentData<MapIconData>(spawnedIconEntity);

                PlayerCharacter pc = em.GetComponentData<PlayerCharacter>(targetCharacterEntity);
                if (em.HasComponent<User>(pc.UserEntity))
                {
                    mapIconData.TargetUser = pc.UserEntity;
                    em.SetComponentData(spawnedIconEntity, mapIconData);
                }

                if (!em.HasComponent<MapIconTargetEntity>(spawnedIconEntity)) em.AddComponent<MapIconTargetEntity>(spawnedIconEntity);
                em.SetComponentData(spawnedIconEntity, new MapIconTargetEntity
                {
                    TargetEntity = targetCharacterEntity,
                    TargetNetworkId = em.GetComponentData<NetworkId>(targetCharacterEntity)
                });

                if (em.HasComponent<SyncToUserBitMask>(spawnedIconEntity)) em.RemoveComponent<SyncToUserBitMask>(spawnedIconEntity);
                if (em.HasBuffer<SyncToUserBuffer>(spawnedIconEntity)) em.GetBuffer<SyncToUserBuffer>(spawnedIconEntity).Clear();
                if (em.HasComponent<OnlySyncToUsersTag>(spawnedIconEntity)) em.RemoveComponent<OnlySyncToUsersTag>(spawnedIconEntity);

                _activePlayerBountyIcons[targetSteamID] = spawnedIconEntity;
            }
            catch (System.Exception)
            {
                // Error occurred
            }
        }

        private static void RemoveMapIconInternal(ulong targetSteamID, Entity iconEntity, bool isReplacing = false)
        {
            if (!VWorld.IsServerWorldReady()) return;
            var em = VWorld.EntityManager;
            if (!em.Exists(iconEntity))
            {
                if (!isReplacing)
                {
                    _activePlayerBountyIcons.Remove(targetSteamID);
                }
                return;
            }
            try
            {
                StatChangeUtility.KillOrDestroyEntity(em, iconEntity, iconEntity, Entity.Null, 0, StatChangeReason.Any, true);
            }
            catch (System.Exception) { }
            finally
            {
                if (!isReplacing)
                {
                    _activePlayerBountyIcons.Remove(targetSteamID);
                }
            }
        }

        public static void RemoveMapIcon(ulong targetSteamID)
        {
            if (!_mapIconsSystemEnabled)
            {
                return;
            }

            bool removedFromDict = false;
            if (_activePlayerBountyIcons.TryGetValue(targetSteamID, out Entity trackedIconEntity))
            {
                RemoveMapIconInternal(targetSteamID, trackedIconEntity, false);
                removedFromDict = true;
            }

            if (!VWorld.IsServerWorldReady())
            {
                return;
            }
            var em = VWorld.EntityManager;

            Entity targetUserEntity = UserUtils.GetUserEntityBySteamID(targetSteamID);

            if (targetUserEntity == Entity.Null)
            {
                return;
            }

            List<PrefabGUID> managedIconGuids = new List<PrefabGUID>();
            if (_configuredDefaultBountyIconStyleGuid.GuidHash != 0) managedIconGuids.Add(_configuredDefaultBountyIconStyleGuid);

            if (!managedIconGuids.Any())
            {
                return;
            }

            var queryDesc = new EntityQueryDesc
            {
                All = new ComponentType[] {
                    ComponentType.ReadOnly<MapIconData>(),
                    ComponentType.ReadOnly<MapIconTargetEntity>(),
                    ComponentType.ReadOnly<PrefabGUID>()
                }
            };
            EntityQuery mapIconEntityQuery = em.CreateEntityQuery(queryDesc);
            NativeArray<Entity> allMapIcons = mapIconEntityQuery.ToEntityArray(Allocator.TempJob);
            int scannedIconsRemoved = 0;

            foreach (Entity iconEntityInScan in allMapIcons)
            {
                if (!em.Exists(iconEntityInScan)) continue;
                PrefabGUID iconPrefabGuid = em.GetComponentData<PrefabGUID>(iconEntityInScan);
                if (!managedIconGuids.Contains(iconPrefabGuid)) continue;

                MapIconData mapIconData = em.GetComponentData<MapIconData>(iconEntityInScan);
                if (mapIconData.TargetUser == targetUserEntity)
                {
                    RemoveMapIconInternal(targetSteamID, iconEntityInScan, false);
                    scannedIconsRemoved++;
                }
            }
            allMapIcons.Dispose();
            mapIconEntityQuery.Dispose();

            if (!removedFromDict && _activePlayerBountyIcons.ContainsKey(targetSteamID) && scannedIconsRemoved > 0)
            {
                _activePlayerBountyIcons.Remove(targetSteamID);
            }
        }

        public static void RemoveAllMapIcons(bool calledOnShutdown = false)
        {
            if (!_mapIconsSystemEnabled && !calledOnShutdown)
            {
                return;
            }
            List<ulong> steamIDsToRemove = new List<ulong>(_activePlayerBountyIcons.Keys);
            foreach (ulong steamID in steamIDsToRemove)
            {
                RemoveMapIcon(steamID);
            }
            if (_activePlayerBountyIcons.Count > 0)
            {
                _activePlayerBountyIcons.Clear();
            }
        }

        public static void CleanupOrphanedIcons(IReadOnlyDictionary<ulong, BountyManager.BountyDetails> currentActivePlayerBounties)
        {
            if (!VWorld.IsServerWorldReady() || !_mapIconsSystemEnabled)
            {
                return;
            }

            var em = VWorld.EntityManager;

            List<PrefabGUID> managedIconGuids = new List<PrefabGUID>();
            if (_configuredDefaultBountyIconStyleGuid.GuidHash != 0)
                managedIconGuids.Add(_configuredDefaultBountyIconStyleGuid);

            if (!managedIconGuids.Any())
            {
                return;
            }

            var queryDesc = new EntityQueryDesc
            {
                All = new ComponentType[] {
                    ComponentType.ReadOnly<MapIconData>(),
                    ComponentType.ReadOnly<MapIconTargetEntity>(),
                    ComponentType.ReadOnly<PrefabGUID>()
                }
            };
            EntityQuery query = em.CreateEntityQuery(queryDesc);
            NativeArray<Entity> allMapIcons = query.ToEntityArray(Allocator.TempJob);
            int orphansRemoved = 0;

            HashSet<Entity> validTrackedIcons = new HashSet<Entity>(_activePlayerBountyIcons.Values);

            foreach (Entity iconEntity in allMapIcons)
            {
                if (!em.Exists(iconEntity)) continue;

                PrefabGUID iconPrefabGuid = em.GetComponentData<PrefabGUID>(iconEntity);
                if (!managedIconGuids.Contains(iconPrefabGuid))
                {
                    continue;
                }

                MapIconData mapIconData = em.GetComponentData<MapIconData>(iconEntity);
                Entity targetUserEntityFromIcon = mapIconData.TargetUser;

                if (targetUserEntityFromIcon == Entity.Null || !em.HasComponent<User>(targetUserEntityFromIcon))
                {
                    RemoveMapIconInternal(0, iconEntity);
                    orphansRemoved++;
                    continue;
                }

                User targetUserData = em.GetComponentData<User>(targetUserEntityFromIcon);
                ulong targetSteamID = targetUserData.PlatformId;
                bool shouldIconExist = false;

                if (currentActivePlayerBounties.TryGetValue(targetSteamID, out _))
                {
                    if (iconPrefabGuid == _configuredDefaultBountyIconStyleGuid)
                    {
                        if (_activePlayerBountyIcons.TryGetValue(targetSteamID, out Entity trackedIconEntity) && trackedIconEntity == iconEntity)
                        {
                            shouldIconExist = true;
                        }
                        else
                        {
                            // Icon exists for an active bounty but isn't what we're tracking (or we're not tracking one)
                            // Potentially remove this specific one, and let BountyManager re-add the correct one if needed.
                            // Or re-track it if it's the only one for this player.
                            _activePlayerBountyIcons[targetSteamID] = iconEntity; // Re-track this specific icon entity if it matches an active bounty.
                            shouldIconExist = true;
                        }
                    }
                }

                if (!shouldIconExist)
                {
                    RemoveMapIconInternal(targetSteamID, iconEntity);
                    orphansRemoved++;
                }
            }
            allMapIcons.Dispose();
            query.Dispose();
        }

        public static int ClearPlayerIconsByScan(Entity targetUserEntityFromCommand, string targetCharacterNameForLog)
        {
            if (!VWorld.IsServerWorldReady() || !_mapIconsSystemEnabled)
            {
                return 0;
            }
            if (targetUserEntityFromCommand == Entity.Null)
            {
                return 0;
            }

            var em = VWorld.EntityManager;

            List<PrefabGUID> managedIconGuids = new List<PrefabGUID>();
            if (_configuredDefaultBountyIconStyleGuid.GuidHash != 0) managedIconGuids.Add(_configuredDefaultBountyIconStyleGuid);

            if (!managedIconGuids.Any())
            {
                return 0;
            }

            var queryDesc = new EntityQueryDesc
            {
                All = new ComponentType[] {
                    ComponentType.ReadOnly<MapIconData>(),
                    ComponentType.ReadOnly<MapIconTargetEntity>(),
                    ComponentType.ReadOnly<PrefabGUID>()
                }
            };
            EntityQuery query = em.CreateEntityQuery(queryDesc);
            NativeArray<Entity> allMapIcons = query.ToEntityArray(Allocator.TempJob);
            int iconsCleared = 0;

            ulong targetSteamIDToClear = 0;
            if (em.HasComponent<User>(targetUserEntityFromCommand))
            {
                targetSteamIDToClear = em.GetComponentData<User>(targetUserEntityFromCommand).PlatformId;
            }

            foreach (Entity iconEntity in allMapIcons)
            {
                if (!em.Exists(iconEntity)) continue;

                PrefabGUID iconPrefabGuid = em.GetComponentData<PrefabGUID>(iconEntity);
                if (!managedIconGuids.Contains(iconPrefabGuid)) continue;

                MapIconData mapIconData = em.GetComponentData<MapIconData>(iconEntity);
                if (mapIconData.TargetUser == targetUserEntityFromCommand)
                {
                    RemoveMapIconInternal(targetSteamIDToClear, iconEntity);
                    iconsCleared++;
                }
            }

            if (targetSteamIDToClear != 0 && _activePlayerBountyIcons.ContainsKey(targetSteamIDToClear))
            {
                _activePlayerBountyIcons.Remove(targetSteamIDToClear);
            }

            allMapIcons.Dispose();
            query.Dispose();
            return iconsCleared;
        }
    }
}

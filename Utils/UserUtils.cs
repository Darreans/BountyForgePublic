using ProjectM;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BountyForge.Utils
{
    public static class UserUtils
    {
        public static bool TryGetUserAndCharacterEntity(string characterNameInput,
                                                        out Entity foundUserEntity,
                                                        out Entity foundCharacterEntity,
                                                        out User foundUserData,
                                                        out string resolvedCharacterName)
        {
            foundUserEntity = Entity.Null;
            foundCharacterEntity = Entity.Null;
            foundUserData = default;
            resolvedCharacterName = null;

            if (!VWorld.IsServerWorldReady())
            {
                LoggingHelper.Error("[BountyForge] UserUtils.TryGetUserAndCharacterEntity: Server world not ready.");
                return false;
            }
            EntityManager entityManager = VWorld.EntityManager;

            EntityQuery userQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<User>());
            NativeArray<Entity> userEntities = userQuery.ToEntityArray(Allocator.TempJob);
            bool playerFound = false;

            try
            {
                for (int i = 0; i < userEntities.Length; i++)
                {
                    Entity currentUserEntity = userEntities[i];
                    if (!entityManager.Exists(currentUserEntity))
                    {
                        continue;
                    }

                    User currentUserData = entityManager.GetComponentData<User>(currentUserEntity);
                    string currentUserName = currentUserData.CharacterName.ToString();

                    if (currentUserName.Equals(characterNameInput, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!currentUserData.IsConnected)
                        {
                            continue;
                        }

                        Entity characterEntityFromUser = currentUserData.LocalCharacter._Entity;
                        if (entityManager.Exists(characterEntityFromUser) &&
                            entityManager.HasComponent<PlayerCharacter>(characterEntityFromUser))
                        {
                            foundUserEntity = currentUserEntity;
                            foundUserData = currentUserData;
                            foundCharacterEntity = characterEntityFromUser;
                            resolvedCharacterName = currentUserName;
                            playerFound = true;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error($"[BountyForge] UserUtils: Exception while iterating user entities: {ex.Message}\n{ex.StackTrace}");
                playerFound = false;
            }
            finally
            {
                if (userEntities.IsCreated) userEntities.Dispose();
                userQuery.Dispose(); 
            }
            return playerFound;
        }

        public static bool TryGetUserDataFromEntity(Entity userEntity, out User userData)
        {
            userData = default;
            if (!VWorld.IsServerWorldReady() || userEntity == Entity.Null) return false;
            EntityManager em = VWorld.EntityManager;
            if (em.Exists(userEntity) && em.HasComponent<User>(userEntity)) // Added Exists check
            {
                userData = em.GetComponentData<User>(userEntity);
                return true;
            }
            return false;
        }

        public static bool TryGetCharacterEntityFromUserEntity(Entity userEntity, out Entity characterEntity)
        {
            characterEntity = Entity.Null;
            if (!VWorld.IsServerWorldReady() || userEntity == Entity.Null) return false;
            EntityManager em = VWorld.EntityManager;
            if (TryGetUserDataFromEntity(userEntity, out User userData))
            {
                characterEntity = userData.LocalCharacter._Entity;
                if (em.Exists(characterEntity))
                {
                    return true;
                }
            }
            return false;
        }

        public static List<User> GetAllOnlineUsers(EntityManager entityManager) 
        {
            List<User> onlineUsers = new List<User>();
            if (!VWorld.IsServerWorldReady() || entityManager == default) return onlineUsers;

            EntityQuery userQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<User>());
            NativeArray<Entity> userEntities = userQuery.ToEntityArray(Allocator.TempJob);

            try
            {
                foreach (Entity userEntity in userEntities)
                {
                    if (entityManager.Exists(userEntity))
                    {
                        User userData = entityManager.GetComponentData<User>(userEntity);
                        if (userData.IsConnected)
                        {
                            onlineUsers.Add(userData);
                        }
                    }
                }
            }
            finally
            {
                if (userEntities.IsCreated) userEntities.Dispose();
                userQuery.Dispose(); 
            }
            return onlineUsers;
        }

        public static Entity GetUserEntityBySteamID(ulong steamID)
        {
            if (!VWorld.IsServerWorldReady())
            {
                return Entity.Null;
            }
            EntityManager em = VWorld.EntityManager;

            EntityQuery userQuery = em.CreateEntityQuery(ComponentType.ReadOnly<User>());
            NativeArray<Entity> userEntities = userQuery.ToEntityArray(Allocator.TempJob);
            Entity foundEntity = Entity.Null;

            try
            {
                foreach (Entity userEntity in userEntities)
                {
                    if (em.Exists(userEntity) && em.HasComponent<User>(userEntity))
                    {
                        User userData = em.GetComponentData<User>(userEntity);
                        if (userData.PlatformId == steamID)
                        {
                            foundEntity = userEntity;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error($"[UserUtils.GetUserEntityBySteamID] Exception finding user for SteamID {steamID}: {ex.Message}\n{ex.StackTrace}");
                foundEntity = Entity.Null;
            }
            finally
            {
                if (userEntities.IsCreated) userEntities.Dispose();
                userQuery.Dispose(); 
            }
            return foundEntity;
        }
    }
}
using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using Unity.Collections; 
using Unity.Entities;
using BountyForge.Systems;
using BountyForge.Utils;
using BountyForge.Config;
using System;

namespace BountyForge.Hooks
{
    [HarmonyPatch(typeof(VampireDownedServerEventSystem), nameof(VampireDownedServerEventSystem.OnUpdate))]
    public static class BountyClaimHook
    {
        public static void Prefix(VampireDownedServerEventSystem __instance)
        {
            if (!VWorld.IsServerWorldReady() || !BountyConfig.ModEnabled.Value) return;

            EntityManager em = VWorld.EntityManager;
            EntityQuery downedEventsQuery;

            try
            {
                downedEventsQuery = __instance.__query_1174204813_0; 
            }
            catch (Exception ex)
            {
                LoggingHelper.Error($"[BountyClaimHook] Could not access downed events query: {ex.Message}");
                return;
            }

            if (downedEventsQuery.IsEmpty) return;

            NativeArray<Entity> downedEventEntities = default; 
            try
            {
                downedEventEntities = downedEventsQuery.ToEntityArray(Allocator.TempJob);
                if (downedEventEntities.Length == 0)
                {
                    return;
                }

                foreach (Entity eventEntity in downedEventEntities)
                {
                    if (!em.Exists(eventEntity) || !em.HasComponent<VampireDownedBuff>(eventEntity)) continue;

                    VampireDownedBuff downedBuff = em.GetComponentData<VampireDownedBuff>(eventEntity);

                    if (!VampireDownedServerEventSystem.TryFindRootOwner(eventEntity, 1, em, out Entity victimCharacterEntity) ||
                        !VampireDownedServerEventSystem.TryFindRootOwner(downedBuff.Source, 1, em, out Entity killerCharacterEntity))
                    {
                        continue;
                    }

                    if (!em.HasComponent<PlayerCharacter>(victimCharacterEntity) ||
                        !em.HasComponent<PlayerCharacter>(killerCharacterEntity))
                    {
                        continue;
                    }

                    PlayerCharacter victimPC = em.GetComponentData<PlayerCharacter>(victimCharacterEntity);
                    PlayerCharacter killerPC = em.GetComponentData<PlayerCharacter>(killerCharacterEntity);

                    if (victimPC.UserEntity == killerPC.UserEntity) continue;

                    if (!em.HasComponent<User>(victimPC.UserEntity) || !em.HasComponent<User>(killerPC.UserEntity))
                    {
                        continue;
                    }

                    User victimUser = em.GetComponentData<User>(victimPC.UserEntity);
                    User killerUser = em.GetComponentData<User>(killerPC.UserEntity);

                    BountyManager.HandlePvPKillEvent(
                        victimPC.UserEntity, victimUser.CharacterName.ToString(), victimUser.PlatformId,
                        killerPC.UserEntity, killerUser.CharacterName.ToString(), killerUser.PlatformId
                    );
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error($"[BountyClaimHook] Error processing downed events: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                if (downedEventEntities.IsCreated)
                {
                    downedEventEntities.Dispose();
                }
            }
        }
    }
}
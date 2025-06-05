using HarmonyLib;
using ProjectM;
using ProjectM.Network; 
using Unity.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using BountyForge.Systems; 
using BountyForge.Utils;   
using BountyForge.Config; 
using Unity.Collections;  
using ProjectM.Gameplay.Clan;


namespace BountyForge.Hooks
{
    [HarmonyPatch(typeof(ClanSystem_Server), nameof(ClanSystem_Server.LeaveClan))]
    public static class ClanLeaveHook
    {
        private static readonly System.Threading.ThreadLocal<List<ulong>> _steamIDsOfClanmatesBeforeLeave = new System.Threading.ThreadLocal<List<ulong>>(() => null);

        [HarmonyPrefix]
        public static void Prefix(ClanSystem_Server __instance, Entity clanEntity, Entity userToLeave)
        {
            try
            {
                if (!BountyConfig.EnableClanBetrayalPrevention.Value) return;

                EntityManager entityManager = __instance.EntityManager;
                if (clanEntity == Entity.Null || userToLeave == Entity.Null || !entityManager.Exists(clanEntity))
                {
                    _steamIDsOfClanmatesBeforeLeave.Value = null;
                    return;
                }

                NativeList<Entity> memberUserEntities = new NativeList<Entity>(Allocator.Temp);
                try
                {
                    TeamUtility.GetClanMembers(entityManager, clanEntity, memberUserEntities);

                    List<ulong> currentClanmateSteamIDs = new List<ulong>();
                    for (int i = 0; i < memberUserEntities.Length; i++)
                    {
                        Entity memberUserEntity = memberUserEntities[i];
                        if (entityManager.Exists(memberUserEntity) && entityManager.HasComponent<User>(memberUserEntity))
                        {
                            User memberUserData = entityManager.GetComponentData<User>(memberUserEntity);
                            currentClanmateSteamIDs.Add(memberUserData.PlatformId);
                        }
                    }
                    _steamIDsOfClanmatesBeforeLeave.Value = currentClanmateSteamIDs;
                }
                finally
                {
                    if (memberUserEntities.IsCreated)
                    {
                        memberUserEntities.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error($"[ClanLeaveHook Prefix] Exception: {ex.Message}\n{ex.StackTrace}");
                _steamIDsOfClanmatesBeforeLeave.Value = null;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(ClanSystem_Server __instance, Entity userToLeave)
        {
            List<ulong> formerClanmateSteamIDsPlusLeaver = null;
            try
            {
                if (!BountyConfig.EnableClanBetrayalPrevention.Value) return;

                formerClanmateSteamIDsPlusLeaver = _steamIDsOfClanmatesBeforeLeave.Value;
                if (formerClanmateSteamIDsPlusLeaver == null)
                {
                    return;
                }

                EntityManager entityManager = __instance.EntityManager;
                if (userToLeave == Entity.Null || !entityManager.Exists(userToLeave) || !entityManager.HasComponent<User>(userToLeave))
                {
                    return;
                }

                User leaverUserData = entityManager.GetComponentData<User>(userToLeave);
                ulong leaverSteamID = leaverUserData.PlatformId;

                List<ulong> actualFormerClanmates = formerClanmateSteamIDsPlusLeaver
                                                    .Where(id => id != leaverSteamID)
                                                    .ToList();

                if (actualFormerClanmates.Any())
                {
                    BountyManager.RecordClanLeave(leaverSteamID, actualFormerClanmates);
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error($"[ClanLeaveHook Postfix] Exception: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                if (formerClanmateSteamIDsPlusLeaver != null)
                {
                    _steamIDsOfClanmatesBeforeLeave.Value = null;
                }
            }
        }
    }
}

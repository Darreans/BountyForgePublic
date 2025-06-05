using ProjectM;
using ProjectM.Network; 
using Unity.Entities;
using Unity.Collections; 

namespace BountyForge.Utils
{
  
    public static class ModChatUtils
    {
       
        public static void SendSystemMessageToAllClients(EntityManager entityManager, string message)
        {
            if (!VWorld.IsServerWorldReady())
            {
                LoggingHelper.Error("ModChatUtils.SendSystemMessageToAllClients: Server world not ready.");
                return;
            }

            FixedString512Bytes fsMessage = new FixedString512Bytes(message);

           
            ProjectM.ServerChatUtils.SendSystemMessageToAllClients(entityManager, ref fsMessage);
        }

    
        public static void SendSystemMessageToClient(EntityManager entityManager, Entity targetUserEntity, string message)
        {
            if (!VWorld.IsServerWorldReady())
            {
                LoggingHelper.Error("ModChatUtils.SendSystemMessageToClient: Server world not ready.");
                return;
            }
            if (targetUserEntity == Entity.Null)
            {
                LoggingHelper.Warning("ModChatUtils.SendSystemMessageToClient: targetUserEntity is Null.");
                return;
            }

            if (entityManager.HasComponent<User>(targetUserEntity))
            {
                User userComponent = entityManager.GetComponentData<User>(targetUserEntity); 
                if (userComponent.IsConnected)
                {
                    FixedString512Bytes fsMessage = new FixedString512Bytes(message);
                    ProjectM.ServerChatUtils.SendSystemMessageToClient(entityManager, userComponent, ref fsMessage);
                }
                else
                {
                    LoggingHelper.Debug($"ModChatUtils.SendSystemMessageToClient: Target user {targetUserEntity} is not connected.");
                }
            }
            else
            {
                LoggingHelper.Warning($"ModChatUtils.SendSystemMessageToClient: Target entity {targetUserEntity} does not have a User component.");
            }
        }
    }
}

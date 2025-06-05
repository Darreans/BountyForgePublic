using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using VampireCommandFramework;
using HarmonyLib;
using System;
using System.IO;
using System.Reflection;
using BountyForge.Config;
using BountyForge.Utils;
using BountyForge.Systems;

namespace BountyForge
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency("gg.deca.VampireCommandFramework")]
    public class Plugin : BasePlugin
    {
        public static Plugin Instance { get; private set; }
        public static BepInEx.Logging.ManualLogSource LogInstance { get; private set; } 
        private Harmony _harmony;
        public new static ConfigFile Config { get; private set; }

        public override void Load()
        {
            Instance = this;
            LogInstance = Log; 
            LoggingHelper.Initialize(LogInstance); 


            string pluginBaseConfigPath = Path.Combine(Paths.ConfigPath, PluginInfo.PLUGIN_NAME);
            Directory.CreateDirectory(pluginBaseConfigPath);
            string mainConfigFilePath = Path.Combine(pluginBaseConfigPath, $"{PluginInfo.PLUGIN_GUID}.cfg");
            Config = new ConfigFile(mainConfigFilePath, true);
            string dataStorageBasePath = Path.Combine(pluginBaseConfigPath, "Data");
            Directory.CreateDirectory(dataStorageBasePath);

            BountyConfig.Initialize(Config);
            ItemUtils.Initialize();
            BountyMapIcons.Initialize();

            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            try
            {
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception)
            {
            }

            BountyManager.Initialize(dataStorageBasePath);
            LeaderboardSystem.Initialize(dataStorageBasePath);


            try
            {
                CommandRegistry.RegisterAll();
            }
            catch (Exception)
            {
            }

            LoggingHelper.Info($"[BountyForge] Plugin (v{PluginInfo.PLUGIN_VERSION}) loaded successfully.");
        }

        public void Update()
        {
        }

        public override bool Unload()
        {
            if (Config != null) Config.Save();

            BountyTaskScheduler.DisposeAllTimers();

            CommandRegistry.UnregisterAssembly();
            _harmony?.UnpatchSelf();

            if (VWorld.IsServerWorldReady())
            {
                BountyManager.SaveActiveBounties();
                BountyManager.SaveActiveContracts();
                LeaderboardSystem.SaveLeaderboard();
                if (BountyConfig.ModEnabled.Value && BountyConfig.EnableMapIcons.Value)
                {
                    BountyMapIcons.RemoveAllMapIcons(true);
                }
            }
            return true;
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;
using ProjectM; 
using BountyForge.Utils; 

namespace BountyForge.Systems
{
    [HarmonyPatch]
    public static class BountyTaskScheduler
    {
        private static ConcurrentQueue<Action> _actionsToExecuteOnMainThread = new ConcurrentQueue<Action>();
        private static List<Timer> _activeTimers = new List<Timer>();
        private static bool _isInitialized = false;

        [HarmonyPatch(typeof(ServerBootstrapSystem), nameof(ServerBootstrapSystem.OnUpdate))]
        [HarmonyPostfix]
        public static void ServerBootstrapSystem_OnUpdate_Postfix()
        {
            while (_actionsToExecuteOnMainThread.TryDequeue(out Action action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    LoggingHelper.Error($"[BountyTaskScheduler] Error executing action on main thread: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

     
        public static void Initialize(Harmony harmonyInstance)
        {
            if (_isInitialized) return;
            try
            {
                
                harmonyInstance.PatchAll(typeof(BountyTaskScheduler));
                LoggingHelper.Info("[BountyTaskScheduler] Initialized and patched into ServerBootstrapSystem.");
                _isInitialized = true;
            }
            catch (Exception e)
            {
                LoggingHelper.Error($"[BountyTaskScheduler] Failed to initialize or apply patches: {e.Message}\n{e.StackTrace}");
            }
        }

        private static void EnqueueAction(Action action)
        {
            _actionsToExecuteOnMainThread.Enqueue(action);
        }

        public static Timer RunActionOnceAfterDelay(Action action, double delayInSeconds)
        {
            if (delayInSeconds < 0) delayInSeconds = 0;

            Timer timer = null;
            timer = new Timer(_ =>
            {
                EnqueueAction(() =>
                {
                    try
                    {
                        action.Invoke();
                    }
                    finally
                    {
                        lock (_activeTimers) { _activeTimers.Remove(timer); }
                        timer?.Dispose();
                    }
                });
            }, null, TimeSpan.FromSeconds(delayInSeconds), Timeout.InfiniteTimeSpan);

            return timer;
        }

        public static Timer RunActionEveryInterval(Action action, double intervalInSeconds, double initialDelayInSeconds = -1)
        {
            if (intervalInSeconds <= 0)
            {
                return null;
            }
            if (initialDelayInSeconds < 0) initialDelayInSeconds = intervalInSeconds;

            Timer timer = new Timer(_ =>
            {
                EnqueueAction(action);
            }, null, TimeSpan.FromSeconds(initialDelayInSeconds), TimeSpan.FromSeconds(intervalInSeconds));

            lock (_activeTimers) { _activeTimers.Add(timer); }
            return timer;
        }

        public static void DisposeAllTimers()
        {
            List<Timer> timersToDispose;
            lock (_activeTimers)
            {
                timersToDispose = new List<Timer>(_activeTimers);
                _activeTimers.Clear();
            }

            foreach (var timer in timersToDispose)
            {
                try
                {
                    timer?.Dispose();
                }
                catch (Exception)
                {
                }
            }
        }
    }
}

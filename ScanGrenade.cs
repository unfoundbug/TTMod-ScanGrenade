using System;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using BepInEx.Configuration;
using System.Linq;
using PropStreaming;
using Voxeland5;
using System.Threading.Tasks;

namespace RightDrag
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class ScanGrenade : BaseUnityPlugin
    {
        public const string pluginGuid = "ScanGrenade.nhickling.co.uk";
        public const string pluginName = "ScanGrenade";
        public const string pluginVersion = "0.0.0.3";
        private static ConfigEntry<int> ScanRange;
        private static BepInEx.Logging.ManualLogSource ModLogger;
        private static List<int> activatedSlots = new List<int>();
        private static bool readyToRun  = false;
        private static bool isProcessing = false;
        private static ScannableData currentlyScanning;
        private static Queue<InstanceLookup> instanceLookups = new Queue<InstanceLookup>();

        static int betweenSends = 3;
        static int waitingFor = 0;

        public void Awake()
        {
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            Logger.LogInfo("ScanGrenade: started");

            Harmony harmony = new Harmony(pluginGuid);
            Logger.LogInfo("ScanGrenade: Fetching patch references");
            {
                Logger.LogInfo("Patching Outbound");
                MethodInfo original = AccessTools.Method(typeof(ScannableData), "CompleteScan", new Type[] { typeof(InstanceLookup) });
                MethodInfo patch = AccessTools.Method(typeof(ScanGrenade), "CompleteScan_MyPatch");
                Logger.LogInfo("ScanGrenade: Starting Patch");
                harmony.Patch(original, null, new HarmonyMethod(patch));
                Logger.LogInfo("ScanGrenade: Patched");
            }

            ScanRange = ((BaseUnityPlugin)this).Config.Bind<int>("Config", "ScanRange", 15, new ConfigDescription("Distance to scan. (0 to disable)", (AcceptableValueBase)(object)new AcceptableValueRange<int>(0, 30), Array.Empty<object>()));

            ModLogger = Logger;
        }

        public void FixedUpdate()
        {
            if (readyToRun)
            {
                if (waitingFor <= 0)
                {
                    Func<bool> retryImmediate = new Func<bool>( () => 
                    {
                        if (instanceLookups.Any())
                        {
                            var toDo = instanceLookups.Dequeue();
                            var props = PropManager.instance;
                            if (props.GetPropData(in toDo, out PropState data))
                            {
                                if (data.isAlive)
                                {
                                    ScanAction action = new ScanAction()
                                    {
                                        info = new ScanInfo() { lookup = toDo }
                                    };
                                    NetworkMessageRelay.instance.SendNetworkAction((NetworkAction)action);
                                    waitingFor = betweenSends;
                                    return false;
                                }
                                else
                                {
                                    return true;
                                }
                            }
                            return false;
                        }
                        else
                        {
                            readyToRun = false;
                            isProcessing = false;
                            return false;
                        }
                    });

                    while (retryImmediate()) ;

                }
                else
                {
                    if(waitingFor > 0)
                        waitingFor -= 1;
                }
            }
        }


        public static void CompleteScan_MyPatch(ScannableData __instance, InstanceLookup lookup)
        {
            if (isProcessing)
            {
                return;
            }

            isProcessing = true;
            currentlyScanning = __instance;

            if (__instance.unlock?.discovered ?? true)
            {
                var props = PropManager.instance;
                var frobs = props.frobManager;
                
                List<ScannableData> foundProps = new List<ScannableData>();

                List<InstanceLookup[]> foundInstances = new List<InstanceLookup[]>();

                foreach(var chunk in props.chunkDataArray)
                {
                    foreach(var prop in chunk.prefabData)
                    {
                        for(int i = 0; i < prop.totalCount; i++)
                        {
                            if (prop.propStates[i].isAlive)
                            {
                                if (props.GetScannableData(prop.typeID, out var scannable))
                                {
                                    if (scannable.name == __instance.name)
                                    {
                                        foundProps.Add(scannable);
                                        foundInstances.Add(prop.lookups);
                                    }
                                }
                            }
                        }
                    }
                }
                    
                var squarLimit = Math.Pow(ScanRange.Value, 2);
                props.GetPropData(in lookup, out SpawnData refdata);
                var refPos = refdata.matrix.GetPositionFast();
                List<Vector3Int> captured = new List<Vector3Int>();
                for (int i = 0; i < foundProps.Count; i++)
                {
                    var scannableData = foundProps[i];
                    for (int j = 0; j < foundInstances[i].Count(); j++)
                    {
                        var instance = foundInstances[i][j];

                        if (lookup.chunkID == instance.chunkID)
                        if (lookup.instanceIndex == instance.instanceIndex)
                        {
                            continue;
                        }

                        if (props.GetPropData(in instance, out SpawnData data))
                        {
                            var loc = data.matrix.GetPositionFast();
                            var sqDis = loc.SqrDistance(refPos);
                            if (sqDis > 0.1 && sqDis < squarLimit)
                            {
                                instanceLookups.Enqueue(instance);
                            }
                        }
                    }
                }
            }
            readyToRun = true;
        }
    }
}

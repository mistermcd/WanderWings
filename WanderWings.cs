// Wander Wings - A Cities: Skylines mod to randomize hot air balloon & touring plane loiter targets
// October 2026 - Idea and testing by MisterMcD, coding by Gemini (Single Destination per Flight + 7x7 Purchased Core)

using ColossalFramework;
using HarmonyLib;
using ICities;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace WanderWings
{
    public class ModEntry : IUserMod
    {
        public string Name => "Wander Wings";
        public string Description => "Randomized hot air balloon and touring plane destinations within the center 7x7 purchased core.";
    }

    public class WanderWingsLoadingExtension : LoadingExtensionBase
    {
        private const string HarmonyId = "com.mistermcd.cities.wanderwings";
        private static Harmony _harmony;
        private static bool _isPatched = false;

        public override void OnLevelLoaded(LoadMode mode)
        {
            if (mode == LoadMode.LoadGame || mode == LoadMode.NewGame)
            {
                ApplyPatches();
            }
        }

        public override void OnLevelUnloading()
        {
            if (!ReferenceEquals(_harmony, null) && _isPatched)
            {
                _harmony.UnpatchAll(HarmonyId);
                _harmony = null;
                _isPatched = false;
                WanderWingsPatch.Cleanup();
            }
        }

        private static void ApplyPatches()
        {
            if (_isPatched) return;

            _harmony = new Harmony(HarmonyId);
            var postfixMethod = AccessTools.Method(typeof(WanderWingsPatch), nameof(WanderWingsPatch.SimulationStepPostfix));
            var releaseVehicleMethod = AccessTools.Method(typeof(VehicleManager), nameof(VehicleManager.ReleaseVehicle));
            var releaseVehiclePostfix = AccessTools.Method(typeof(WanderWingsPatch), nameof(WanderWingsPatch.ReleaseVehiclePostfix));

            string[] targetClassNames = { "BalloonAI", "PrivatePlaneAI" };
            int patchedCount = 0;

            foreach (string className in targetClassNames)
            {
                Type aiType = AccessTools.TypeByName(className);
                if (ReferenceEquals(aiType, null)) continue;

                MethodInfo originalMethod = null;
                foreach (var method in aiType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (method.Name == "SimulationStep")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length >= 3 &&
                            ReferenceEquals(parameters[0].ParameterType, typeof(ushort)) &&
                            parameters[1].ParameterType.Name.Contains("Vehicle") &&
                            parameters[2].ParameterType.Name.Contains("Frame"))
                        {
                            originalMethod = method;
                            break;
                        }
                    }
                }

                if (!ReferenceEquals(originalMethod, null))
                {
                    _harmony.Patch(originalMethod, postfix: new HarmonyMethod(postfixMethod));
                    patchedCount++;
                    Debug.Log($"[Wander Wings] SUCCESS: Patched {className}.SimulationStep!");
                }
            }

            // Patch VehicleManager.ReleaseVehicle to instantly clean up recycled IDs
            if (!ReferenceEquals(releaseVehicleMethod, null) && !ReferenceEquals(releaseVehiclePostfix, null))
            {
                _harmony.Patch(releaseVehicleMethod, postfix: new HarmonyMethod(releaseVehiclePostfix));
            }

            if (patchedCount > 0)
            {
                _isPatched = true;
            }
        }
    }

    public static class WanderWingsPatch
    {
        private static readonly Dictionary<ushort, Vector3> _customTargets = new Dictionary<ushort, Vector3>(64);
        private static readonly List<Rect> _reusableRects = new List<Rect>(64);

        // Center 7x7 tiles span limit (3.5 tiles * 1920m = +/- 6720m from map center)
        private const float SevenTileMaxExtent = 6720f;

        public static void Cleanup()
        {
            _customTargets.Clear();
            _reusableRects.Clear();
        }

        // Hook into VehicleManager.ReleaseVehicle to prevent ID recycling bugs
        public static void ReleaseVehiclePostfix(ushort vehicle)
        {
            if (_customTargets.ContainsKey(vehicle))
            {
                _customTargets.Remove(vehicle);
            }
        }

        public static void SimulationStepPostfix(ushort vehicleID, ref Vehicle vehicleData, ref Vehicle.Frame frameData)
        {
            try
            {
                // Safety: Ignore landing, returning home, or deleted vehicles
                const Vehicle.Flags safeIgnoreFlags = Vehicle.Flags.Landing | Vehicle.Flags.GoingBack | Vehicle.Flags.Deleted;
                if ((vehicleData.m_flags & safeIgnoreFlags) != 0 || (vehicleData.m_flags & Vehicle.Flags.Created) == 0)
                {
                    if (_customTargets.ContainsKey(vehicleID))
                    {
                        _customTargets.Remove(vehicleID);
                    }
                    return;
                }

                // Intercept only when airborne and cruising
                bool isFlying = (vehicleData.m_flags & Vehicle.Flags.Flying) != 0;
                bool isTakingOff = (vehicleData.m_flags & Vehicle.Flags.TakingOff) != 0;

                if (!isFlying || isTakingOff)
                {
                    return;
                }

                // RETURN SAFEGUARD: Release control if heading back home
                if (vehicleData.m_sourceBuilding != 0 && Singleton<BuildingManager>.exists)
                {
                    Vector3 sourcePos = Singleton<BuildingManager>.instance.m_buildings.m_buffer[vehicleData.m_sourceBuilding].m_position;
                    if (Vector3.Distance(frameData.m_position, sourcePos) < 1500f && (vehicleData.m_flags & Vehicle.Flags.GoingBack) != 0)
                    {
                        if (_customTargets.ContainsKey(vehicleID))
                        {
                            _customTargets.Remove(vehicleID);
                        }
                        return;
                    }
                }

                // SINGLE DESTINATION PER FLIGHT: Assign one randomized, purchased-core target upon reaching cruise altitude.
                if (!_customTargets.TryGetValue(vehicleID, out Vector3 assignedTarget))
                {
                    assignedTarget = GeneratePurchasedCoreCoordinate();
                    _customTargets[vehicleID] = assignedTarget;
                }

                // ENFORCE TARGET EVERY FRAME: Native AI overwrites m_targetPos0 during SimulationStep,
                // so we re-apply our custom target every tick until the tour timer expires and it goes home.
                vehicleData.m_targetPos0 = new Vector4(assignedTarget.x, assignedTarget.y, assignedTarget.z, vehicleData.m_targetPos0.w);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Wander Wings] Error in Postfix for vehicle {vehicleID}: {ex.Message}");
            }
        }

        private static Vector3 GeneratePurchasedCoreCoordinate()
        {
            _reusableRects.Clear();

            if (Singleton<GameAreaManager>.exists)
            {
                try
                {
                    var areaManager = Singleton<GameAreaManager>.instance;
                    int gridLength = areaManager.m_areaGrid != null ? areaManager.m_areaGrid.Length : 81;
                    int gridSize = Mathf.RoundToInt(Mathf.Sqrt(gridLength));
                    if (gridSize < 5) gridSize = 5;

                    for (int x = 0; x < gridSize; x++)
                    {
                        for (int z = 0; z < gridSize; z++)
                        {
                            int index = x + z * gridSize;
                            // Check if tile exists and is purchased (!= 0)
                            if (index < areaManager.m_areaGrid.Length && areaManager.m_areaGrid[index] != 0)
                            {
                                float bx1, bz1, bx2, bz2;
                                areaManager.GetAreaBounds(x, z, out bx1, out bz1, out bx2, out bz2);

                                // Intersect with center 7x7 tile boundaries (+/- 6720m)
                                float cx1 = Mathf.Max(bx1, -SevenTileMaxExtent);
                                float cx2 = Mathf.Min(bx2, SevenTileMaxExtent);
                                float cz1 = Mathf.Max(bz1, -SevenTileMaxExtent);
                                float cz2 = Mathf.Min(bz2, SevenTileMaxExtent);

                                if (cx1 < cx2 && cz1 < cz2)
                                {
                                    _reusableRects.Add(new Rect(cx1, cz1, cx2 - cx1, cz2 - cz1));
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Fallback handled below
                }
            }

            float randomX, randomZ;

            if (_reusableRects.Count > 0)
            {
                // Pick a random purchased rectangle within the center 7x7 core
                Rect selectedRect = _reusableRects[UnityEngine.Random.Range(0, _reusableRects.Count)];
                randomX = UnityEngine.Random.Range(selectedRect.xMin, selectedRect.xMax);
                randomZ = UnityEngine.Random.Range(selectedRect.yMin, selectedRect.yMax);
            }
            else
            {
                // Fallback to center 7x7 span if no tiles are registered
                randomX = UnityEngine.Random.Range(-SevenTileMaxExtent, SevenTileMaxExtent);
                randomZ = UnityEngine.Random.Range(-SevenTileMaxExtent, SevenTileMaxExtent);
            }

            float groundHeight = 40f;
            if (Singleton<TerrainManager>.exists)
            {
                groundHeight = Singleton<TerrainManager>.instance.SampleDetailHeight(new Vector3(randomX, 0f, randomZ));
            }

            // Safe cruising clearance: 60m to 100m above local terrain
            float cruiseAltitude = groundHeight + UnityEngine.Random.Range(60f, 100f);

            return new Vector3(randomX, cruiseAltitude, randomZ);
        }
    }
}
﻿using System;
using System.Collections.Generic;
using HarmonyLib;


namespace TransferController
{
    /// <summary>
    /// Simple building pair struct.
    /// </summary>
    public struct BuildingPair
    {
        public ushort sourceBuilding;
        public ushort targetBuilding;
    }


    /// <summary>
    // Harmony patch to block transfers bewtween buildings with recent pathfind failures.
    /// </summary>
    [HarmonyPatch(typeof(CarAI), "PathfindFailure")]
    public static class PathFindFailure
    {
        // Timeout period.
        private const long Timeout = 5 * TimeSpan.TicksPerMinute;


        // Enable recording of failures.
        private static bool enableFailureTracking = true;


        // Dictionary of failed pathfinds (building pair plus timestamp in ticks).
        private static Dictionary<BuildingPair, long> pathFails = new Dictionary<BuildingPair, long>();


        /// <summary>
        /// Enables or disables pathfind failure tracking
        /// </summary>
        internal static bool EnableFailTracking
        {
            get => enableFailureTracking;

            set
            {
                enableFailureTracking = value;

                // If pathfinding is disabled, clear the dictionary.
                if (!value)
                {
                    pathFails.Clear();
                }
            }
        }


        /// <summary>
        /// Harmony Postfix to CarAI.PathFindFailure to record failed pathfinds.
        /// </summary>
        /// <param name="data">Vehicle data record</param>
        public static void Postfix(ref Vehicle data)
        {
            // Don't do anything if not recording fails.
            if (!enableFailureTracking)
            {
                return;
            }

            // See if the failed pathfind has a valid source and target building.
            if (data.m_targetBuilding != 0 && data.m_sourceBuilding != 0)
            {
                // Create BuildingPair.
                BuildingPair thisPair = new BuildingPair { sourceBuilding = data.m_sourceBuilding, targetBuilding = data.m_targetBuilding };
                Logging.Message("vehicle pathfinding failure between buildings ", thisPair.sourceBuilding, " and ", thisPair.targetBuilding);

                // Check if we already have an entry for this building pair.
                if (pathFails.ContainsKey(thisPair))
                {
                    // Yes - update failure time with current times.
                    pathFails[thisPair] = DateTime.Now.Ticks;
                }
                else
                {
                    // No - add new entry with this value.
                    pathFails.Add(thisPair, DateTime.Now.Ticks);
                }
            }
        }


        /// <summary>
        /// Checks to see if a pathfinding failure has been recorded between the given building pairs within the past 5 minutes.
        /// </summary>
        /// <param name="sourceBuilding">Source building ID</param>
        /// <param name="targetBuilding">Target building ID</param>
        /// <returns>True if a pathfinding failure between these buildings has been registered in the past five minutes, false otherwise</returns>
        internal static bool HasFailure(ushort sourceBuilding, ushort targetBuilding)
        {
            // Create BuildingPair.
            BuildingPair thisPair = new BuildingPair { sourceBuilding = sourceBuilding, targetBuilding = targetBuilding };

            // Check if we've got a pathfind failure record for this building pair.
            if (pathFails.TryGetValue(thisPair, out long time))
            {
                // Yes - check to see if five minutes have passed.
                if (time + Timeout > DateTime.Now.Ticks)
                {
                    // Five minutes haven't passed - block the transfer.
                    Logging.Message("blocked transfer between buildings ", sourceBuilding, " and ", targetBuilding, " due to pathfinding failure");
                    return true;
                }
                else
                {
                    // More than five minutes have passed since the last failure - remove the record (now stale) and fall through.
                    pathFails.Remove(thisPair);
                }
            }

            // If we got here, it's all good; no recent failure recorded.
            return false;
        }


        /// <summary>
        /// Remove all records relating to the given building, plus any that have expried.
        /// </summary>
        /// <param name="buildingID">Building ID to release</param>
        /// <returns></returns>
        internal static void ReleaseBuilding(ushort buildingID)
        {
            // List of records to remove.
            List<BuildingPair> recordList = new List<BuildingPair>();

            // Remove all records older than this.
            long expiryTick = DateTime.Now.Ticks - Timeout;

            // Iterate through dictionary looking for records to remove..
            foreach (KeyValuePair<BuildingPair, long> entry in pathFails)
            {
                if (entry.Key.sourceBuilding == buildingID || entry.Key.targetBuilding == buildingID || entry.Value < expiryTick)
                {
                    // Found a record referring to the building - add to list.
                    recordList.Add(entry.Key);
                }
            }

            // Now, remove all identified records from the dictionary.
            foreach (BuildingPair record in recordList)
            {
                pathFails.Remove(record);
            }
        }
    }
}
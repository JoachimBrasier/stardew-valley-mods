﻿using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley;

namespace Bouhm.Shared.Locations
{
    // Library for methods the map out all the locations in SDV
    // and other helpful functions
    internal class LocationUtil
    {
        /// <summary>The maximum method call depth when recursively scanning locations.</summary>
        /// <remarks>This is a last resort to prevent stack overflows. Normally the mod should prevent infinite recursion automatically by tracking locations it already visited.</remarks>
        private const int MaxRecursionDepth = 500;

        public static Dictionary<string, LocationContext> LocationContexts { get; set; }

        public static Dictionary<string, LocationContext> GetLocationContexts()
        {
            LocationContexts = new Dictionary<string, LocationContext>();
            foreach (var location in Game1.locations)
            {
                // Get outdoor neighbors
                if (location.IsOutdoors)
                {
                    if (!LocationContexts.ContainsKey(location.Name))
                    {
                        LocationContexts.Add(location.Name, new LocationContext() { Root = location.Name, Type = LocationType.Outdoors });
                    }

                    foreach (var warp in location.warps)
                    {
                        if (warp == null || Game1.getLocationFromName(warp.TargetName) == null) continue;
                        var warpLocation = Game1.getLocationFromName(warp.TargetName);

                        if (warpLocation.IsOutdoors)
                        {
                            if (!LocationContexts[location.Name].Neighbors.ContainsKey(warp.TargetName))
                                LocationContexts[location.Name].Neighbors.Add(warp.TargetName, new Vector2(warp.X, warp.Y));
                        }
                    }
                }
                // Get root locations from indoor locations
                else
                    MapRootLocations(location);
            }

            foreach (var location in Game1.getFarm().buildings)
                MapRootLocations(location.indoors.Value);

            return LocationContexts;
        }

        /// <summary>Recursively traverse all locations accessible through warps from a given location, and map all locations to the root (outdoor) locations they can be reached from.</summary>
        /// <param name="location">The location to start searching from.</param>
        /// <remarks>This traverses in indoor-to-outdoor order because warps and doors are not complete subsets of Game1.locations, which means there will be some rooms left out unless all the locations are iterated.</remarks>
        private static void MapRootLocations(GameLocation location)
        {
            static string ScanRecursively(GameLocation location, GameLocation prevLocation, string root, bool hasOutdoorWarp, Vector2 warpPosition, ISet<string> seen, int depth)
            {
                // break infinite loops
                if (location == null || !seen.Add(location.NameOrUniqueName))
                    return root;
                if (depth > LocationUtil.MaxRecursionDepth)
                    throw new InvalidOperationException($"Infinite recursion detected in location scan. Technical details:\n{nameof(location)}: {location?.NameOrUniqueName}\n{nameof(root)}: {root}\n{nameof(hasOutdoorWarp)}: {hasOutdoorWarp}\n{nameof(warpPosition)}: {warpPosition}\n{nameof(depth)}: {depth}\n\n{Environment.StackTrace}");

                // get location info
                string curLocationName = location.NameOrUniqueName;
                string prevLocationName = prevLocation?.NameOrUniqueName;

                // track contexts
                if (!LocationContexts.ContainsKey(curLocationName))
                    LocationContexts.Add(curLocationName, new LocationContext());
                if (prevLocation != null && !warpPosition.Equals(Vector2.Zero))
                {
                    LocationContexts[prevLocationName].Warp = warpPosition;
                    if (root != curLocationName)
                        LocationContexts[prevLocationName].Parent = curLocationName;
                }

                // pass root location back recursively
                if (root != null)
                {
                    LocationContexts[curLocationName].Root = root;
                    return root;
                }

                // root location found, set as root and return
                if (location.IsOutdoors)
                {
                    LocationContexts[curLocationName].Type = LocationType.Outdoors;
                    LocationContexts[curLocationName].Root = curLocationName;

                    if (prevLocation != null)
                    {
                        if (LocationContexts[curLocationName].Children == null)
                            LocationContexts[curLocationName].Children = new List<string> { prevLocationName };
                        else if (!LocationContexts[curLocationName].Children.Contains(prevLocationName))
                            LocationContexts[curLocationName].Children.Add(prevLocationName);
                    }

                    return curLocationName;
                }

                // recursively traverse warps from current location
                foreach (var warp in location.warps)
                {
                    // avoid circular loop
                    if (curLocationName == warp.TargetName || prevLocationName == warp.TargetName)
                        continue;

                    // get target location
                    var warpLocation = Game1.getLocationFromName(warp.TargetName);
                    if (warpLocation == null)
                        continue;

                    // if one of the warps is a root location, current location is an indoor building
                    if (warpLocation.IsOutdoors)
                        hasOutdoorWarp = true;

                    // if all warps are indoors, then the current location is a room
                    LocationContexts[curLocationName].Type = hasOutdoorWarp ? LocationType.Building : LocationType.Room;

                    // update contexts
                    if (prevLocation != null)
                    {
                        LocationContexts[prevLocationName].Parent = curLocationName;

                        if (LocationContexts[curLocationName].Children == null)
                            LocationContexts[curLocationName].Children = new List<string> { prevLocationName };
                        else if (!LocationContexts[curLocationName].Children.Contains(prevLocationName))
                            LocationContexts[curLocationName].Children.Add(prevLocationName);
                    }
                    root = ScanRecursively(warpLocation, location, root, hasOutdoorWarp, new Vector2(warp.TargetX, warp.TargetY), seen, depth + 1);
                    LocationContexts[curLocationName].Root = root;

                    return root;
                }

                return root;
            }

            ScanRecursively(location, null, null, false, Vector2.Zero, new HashSet<string>(), 1);
        }

        /// <summary>Find the uppermost indoor location for a building.</summary>
        /// <param name="loc">The location to scan.</param>
        public static string GetBuilding(string loc)
        {
            static string GetRecursively(string loc, ISet<string> seen, int depth)
            {
                // break infinite loops
                if (!seen.Add(loc))
                    return loc;
                if (depth > LocationUtil.MaxRecursionDepth)
                    throw new InvalidOperationException($"Infinite recursion detected in location scan. Technical details:\n{nameof(loc)}: {loc}\n{nameof(depth)}: {depth}\n\n{Environment.StackTrace}");

                // handle mines
                if (loc.Contains("UndergroundMine"))
                    return GetMinesLocationName(loc);

                // found root building
                if (LocationContexts[loc].Type == LocationType.Building)
                    return loc;
                string building = LocationContexts[loc].Parent;
                if (building == null)
                    return null;
                if (building == LocationContexts[loc].Root)
                    return loc;

                // scan recursively
                return GetRecursively(building, seen, depth + 1);
            }

            return GetRecursively(loc, new HashSet<string>(), 1);
        }

        // Get Mines name from floor level
        public static string GetMinesLocationName(string locationName)
        {
            string mine = locationName.Substring("UndergroundMine".Length, locationName.Length - "UndergroundMine".Length);
            if (int.TryParse(mine, out int mineLevel))
            {
                // Skull cave
                if (mineLevel > 120)
                    return "SkullCave";
                // Mines
                return "Mine";
            }

            return null;
        }

        public static bool IsOutdoors(string locationName)
        {
            if (locationName == null) return false;

            if (LocationContexts.TryGetValue(locationName, out var locCtx))
            {
                return locCtx.Type == LocationType.Outdoors;
            }

            return false;
        }
    }
}

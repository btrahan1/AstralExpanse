using System;
using System.Collections.Generic;

namespace AstralExpanse.Services;



public class ShipMission
{
    public string ShipId { get; set; } = string.Empty;
    public GameUnitType Type { get; set; } = GameUnitType.MinerUnit;
    public string AsteroidId { get; set; } = string.Empty; // Also used for SubStation ID when towing
    public float[] AsteroidPosition { get; set; } = new float[3]; // Target destination
    public float[] Position { get; set; } = new float[3]; // Current world position
    public float[] Rotation { get; set; } = new float[3]; // Current world rotation
    public ShipState State { get; set; } = ShipState.Idle;
    public bool StopRequested { get; set; } = false;
    public float[] CurrentTarget { get; set; } = new float[3];
    public string? TowedEntityId { get; set; }
    public int Cargo { get; set; } = 0;
    public int MaxCargo { get; set; } = 100;
    public string ParentStationId { get; set; } = "Ark_Colonization_Station";
    public float ConstructionProgress { get; set; } = 0; // 0-100
    public float ConstructionTarget { get; set; } = 0; // Total seconds
}

public class ModelLoadData
{
    public string JsonData { get; set; } = "";
    public float[] Position { get; set; } = new float[] { 0, 0, 0 };
    public float Scale { get; set; } = 1.0f;
    public float[] Rotation { get; set; } = new float[] { 0, 0, 0 };
    public string Id { get; set; } = "";
}

public class RadarEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = ""; // "Station", "Ship", "Asteroid", "SubStation"
    public float[] Pos { get; set; } = new float[] { 0, 0 };
}

public class AsteroidData
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float[] Position { get; set; } = new float[3];
    public float[] Rotation { get; set; } = new float[3];
    public float Scale { get; set; } = 1.0f;
    public int Capacity { get; set; } = 10000;
}

public class ColonyBuilding
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "FarmHouse", "ResearchCenter"
    public float[] Position { get; set; } = new float[3];
    public float[] Rotation { get; set; } = new float[3];
}

public enum FarmingPhase
{
    Planting,
    Watering,
    Fertilizing,
    Harvesting
}

public class FarmingBot
{
    public string Id { get; set; } = string.Empty;
    public string ParentFarmHouseId { get; set; } = string.Empty;
    public float[] Position { get; set; } = new float[3];
    public string? CurrentTargetFieldId { get; set; }
    public FarmingPhase CurrentPhase { get; set; } = FarmingPhase.Planting;
    public float PhaseProgress { get; set; } = 0; // 0 to 1
    public float[] HomePosition { get; set; } = new float[3];
    public bool IsTraveling { get; set; } = false;
    public string? LastTargetFieldId { get; set; }
}

public class PlanetData
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float[] Position { get; set; } = new float[3];
    public bool IsDiscovered { get; set; } = false;
    public bool IsColonized { get; set; } = false;
    public string Type { get; set; } = "Terrestrial";
    public List<ColonyBuilding> Buildings { get; set; } = new List<ColonyBuilding>();
    public List<FarmingBot> Bots { get; set; } = new List<FarmingBot>();
}

public class GameStateService
{
    public int Ore { get; set; } = 1000;
    public List<string> Fleet { get; set; } = new List<string>();
    public List<string> Stations { get; set; } = new List<string>();
    public Dictionary<string, float[]> StationPositions { get; set; } = new Dictionary<string, float[]>();
    public List<AsteroidData> Asteroids { get; set; } = new List<AsteroidData>();
    public List<PlanetData> Planets { get; set; } = new List<PlanetData>();
    public Dictionary<string, ShipMission> ActiveMissions { get; set; } = new Dictionary<string, ShipMission>();

    public int Wheat { get; set; } = 0;
    public int Potato { get; set; } = 0;
    public int Corn { get; set; } = 0;

    public event Action? OnStateChanged;

    public void AddOre(int amount)
    {
        Ore += amount;
        Notify();
    }

    public void RegisterShip(string id)
    {
        if (!Fleet.Contains(id))
        {
            Fleet.Add(id);
            Notify();
        }
    }

    public void RegisterStation(string id, float[] position)
    {
        if (!Stations.Contains(id))
        {
            Stations.Add(id);
            StationPositions[id] = position;
            Notify();
        }
    }

    public bool TryBuildShip(string shipType, int cost)
    {
        if (Ore >= cost)
        {
            Ore -= cost;
            Notify();
            return true;
        }
        return false;
    }

    public bool TryBuildColonyBuilding(string planetId, string buildingType, int cost, float[] pos)
    {
        if (Ore >= cost)
        {
            var planet = Planets.Find(p => p.Id == planetId);
            if (planet != null)
            {
                Ore -= cost;
                var buildingId = $"{buildingType}_{Guid.NewGuid().ToString()[..8]}";
                planet.Buildings.Add(new ColonyBuilding 
                { 
                    Id = buildingId, 
                    Type = buildingType, 
                    Position = pos 
                });
                Notify();
                return true;
            }
        }
        return false;
    }

    public void DepleteAsteroid(string id, int amount)
    {
        var asteroid = Asteroids.Find(a => a.Id == id);
        if (asteroid != null)
        {
            asteroid.Capacity = Math.Max(0, asteroid.Capacity - amount);
            if (asteroid.Capacity <= 0)
            {
                // Note: We might want to remove it from the list only after visual destruction
                // or keep it in the list with 0 capacity to prevent re-spawning
            }
            Notify();
        }
    }

    public void ResetState()
    {
        Ore = 1000;
        Fleet.Clear();
        Stations.Clear();
        StationPositions.Clear();
        Asteroids.Clear();
        Planets.Clear();
        ActiveMissions.Clear();
        Wheat = 0;
        Potato = 0;
        Corn = 0;
        Notify();
    }

    public void LoadFromState(GameStateService? savedState)
    {
        if (savedState == null) return;
        Ore = savedState.Ore;
        Fleet = savedState.Fleet;
        Stations = savedState.Stations;
        StationPositions = savedState.StationPositions ?? new Dictionary<string, float[]>();
        Asteroids = savedState.Asteroids ?? new List<AsteroidData>();
        Planets = savedState.Planets ?? new List<PlanetData>();
        ActiveMissions = savedState.ActiveMissions;
        Wheat = savedState.Wheat;
        Potato = savedState.Potato;
        Corn = savedState.Corn;
        Notify();
    }

    public void Notify() => OnStateChanged?.Invoke();
}

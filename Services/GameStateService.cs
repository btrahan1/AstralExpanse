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
}

public class RadarEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "Station", "Ship", "Asteroid", "SubStation"
    public float[] Pos { get; set; } = new float[2];
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

public class GameStateService
{
    public int Ore { get; set; } = 1000;
    public List<string> Fleet { get; set; } = new List<string>();
    public List<string> Stations { get; set; } = new List<string>();
    public Dictionary<string, float[]> StationPositions { get; set; } = new Dictionary<string, float[]>();
    public List<AsteroidData> Asteroids { get; set; } = new List<AsteroidData>();
    public Dictionary<string, ShipMission> ActiveMissions { get; set; } = new Dictionary<string, ShipMission>();

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
        ActiveMissions.Clear();
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
        ActiveMissions = savedState.ActiveMissions;
        Notify();
    }

    private void Notify() => OnStateChanged?.Invoke();
}

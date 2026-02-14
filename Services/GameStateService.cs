using System;
using System.Collections.Generic;

namespace AstralExpanse.Services;



public class ShipMission
{
    public string ShipId { get; set; } = string.Empty;
    public GameUnitType Type { get; set; } = GameUnitType.MinerUnit;
    public string AsteroidId { get; set; } = string.Empty; // Also used for SubStation ID when towing
    public float[] AsteroidPosition { get; set; } = new float[3]; // Target destination
    public ShipState State { get; set; } = ShipState.Idle;
    public bool StopRequested { get; set; } = false;
    public float[] CurrentTarget { get; set; } = new float[3];
    public string? TowedEntityId { get; set; }
}

public class RadarEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "Station", "Ship", "Asteroid", "SubStation"
    public float[] Pos { get; set; } = new float[2];
}

public class GameStateService
{
    public int Ore { get; set; } = 1000;
    public List<string> Fleet { get; } = new List<string>();
    public List<string> Stations { get; } = new List<string>();
    public Dictionary<string, ShipMission> ActiveMissions { get; } = new Dictionary<string, ShipMission>();

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

    public void RegisterStation(string id)
    {
        if (!Stations.Contains(id))
        {
            Stations.Add(id);
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

    private void Notify() => OnStateChanged?.Invoke();
}

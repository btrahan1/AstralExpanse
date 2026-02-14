using System;
using System.Collections.Generic;

namespace AstralExpanse.Services;

public enum ShipType { Miner, Scout, Fighter }

public enum ShipState { Idle, MovingToAsteroid, Mining, ReturningToStation, DroppingOff, Scouting, Patrolling }

public class ShipMission
{
    public string ShipId { get; set; } = string.Empty;
    public ShipType Type { get; set; } = ShipType.Miner;
    public string AsteroidId { get; set; } = string.Empty;
    public float[] AsteroidPosition { get; set; } = new float[3];
    public ShipState State { get; set; } = ShipState.Idle;
    public bool StopRequested { get; set; } = false;
    public float[] CurrentTarget { get; set; } = new float[3];
}

public class RadarEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public float[] Pos { get; set; } = new float[2];
}

public class GameStateService
{
    public int Ore { get; set; } = 1000;
    public List<string> Fleet { get; } = new List<string>();
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

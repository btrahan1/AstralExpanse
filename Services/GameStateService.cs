using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

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
    public string? TargetWormholeId { get; set; }
    public string SectorId { get; set; } = "Home_Sector";
    public float Health { get; set; } = 100;
    public float MaxHealth { get; set; } = 100;
    public float Armor { get; set; } = 0; // Damage reduction
    public float Firepower { get; set; } = 10;
    public string? CombatTargetId { get; set; }
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

public class ConstructionProject
{
    public string Id { get; set; } = string.Empty;
    public string BuildingType { get; set; } = string.Empty;
    public float Progress { get; set; } = 0; // 0 to 1
    public float RemainingSeconds { get; set; } = 0;
    public float[] Position { get; set; } = new float[3];
}

public class RoverData
{
    public string Id { get; set; } = string.Empty;
    public float[] Position { get; set; } = new float[3];
    public float RotationY { get; set; } = 0;
    public bool IsActive { get; set; } = false;
}

public class MonolithData
{
    public string Id { get; set; } = string.Empty;
    public float[] Position { get; set; } = new float[3];
    public bool IsDiscovered { get; set; } = false;
}

public class WormholeData
{
    public string Id { get; set; } = string.Empty;
    public float[] Position { get; set; } = new float[3];
    public bool IsDiscovered { get; set; } = false;
    public string TargetSectorId { get; set; } = string.Empty;
}

public class SurfaceMinerData
{
    public string Id { get; set; } = string.Empty;
    public float[] Position { get; set; } = new float[3];
    public string TargetMonolithId { get; set; } = string.Empty;
    public string OwnerPlanetId { get; set; } = string.Empty;
    public ShipState State { get; set; } = ShipState.Idle;
    public int Cargo { get; set; } = 0;
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
    public List<ConstructionProject> ConstructionProjects { get; set; } = new List<ConstructionProject>();
    public int Population { get; set; } = 0;
    public RoverData? Rover { get; set; }
    public List<MonolithData> Monoliths { get; set; } = new List<MonolithData>();
    public List<SurfaceMinerData> SurfaceMiners { get; set; } = new List<SurfaceMinerData>();
    public bool MonolithsSeeded { get; set; } = false;
}

public class NPCShipData
{
    public string Id { get; set; } = string.Empty;
    public string FactionId { get; set; } = string.Empty;
    public float[] Position { get; set; } = new float[3];
    public ShipState State { get; set; } = ShipState.Idle;
    public string TargetId { get; set; } = string.Empty;
    public float Health { get; set; } = 100;
    public float MaxHealth { get; set; } = 100;
    public float Armor { get; set; } = 0;
    public float Firepower { get; set; } = 5;
}

public class NPCFactionData
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float Hostility { get; set; } = 0.5f; // 0 to 1
    public List<string> Ships { get; set; } = new List<string>();
    public List<string> Stations { get; set; } = new List<string>();
}

public class SectorData
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float ThreatLevel { get; set; } = 0;

    // Sector-Local Resources
    public int Ore { get; set; } = 1000;
    public int Wheat { get; set; } = 0;
    public int Potato { get; set; } = 0;
    public int Corn { get; set; } = 0;
    public int AetherShards { get; set; } = 0;

    public List<string> Fleet { get; set; } = new List<string>();
    public List<string> Stations { get; set; } = new List<string>();
    public Dictionary<string, float[]> StationPositions { get; set; } = new Dictionary<string, float[]>();
    public List<AsteroidData> Asteroids { get; set; } = new List<AsteroidData>();
    public List<PlanetData> Planets { get; set; } = new List<PlanetData>();
    public List<WormholeData> Wormholes { get; set; } = new List<WormholeData>();
    public List<NPCFactionData> Factions { get; set; } = new List<NPCFactionData>();
    public Dictionary<string, NPCShipData> NPCShips { get; set; } = new Dictionary<string, NPCShipData>();
    public Dictionary<string, ShipMission> ActiveMissions { get; set; } = new Dictionary<string, ShipMission>();
}

public class GameStateService
{
    public const int VictoryPopulationTarget = 5000;
    public string CurrentSectorId { get; set; } = "Home_Sector";
    public List<SectorData> Sectors { get; set; } = new List<SectorData>();

    public event Action? OnStateChanged;

    [JsonIgnore]
    public int TotalPopulation => Sectors.Sum(s => s.Planets.Sum(p => p.Population));

    [JsonIgnore]
    public bool IsVictoryAchieved => TotalPopulation >= VictoryPopulationTarget;

    [JsonIgnore]
    public SectorData CurrentSector => Sectors.Find(s => s.Id == CurrentSectorId) ?? InitializeHomeSector();

    private SectorData InitializeHomeSector()
    {
        var existing = Sectors.Find(s => s.Id == "Home_Sector");
        if (existing != null) return existing;

        var home = new SectorData { Id = "Home_Sector", Name = "Solana System", Ore = 1000 };
        Sectors.Add(home);

        // Spawn initial asteroids for Home Sector
        var rnd = new Random();
        for (int i = 0; i < 15; i++)
        {
            float angle = (float)(rnd.NextDouble() * Math.PI * 2);
            float dist = 700 + (float)(rnd.NextDouble() * 500);
            float x = (float)Math.Cos(angle) * dist;
            float z = (float)Math.Sin(angle) * dist;
            float y = (float)(rnd.NextDouble() - 0.5) * 100;
            
            home.Asteroids.Add(new AsteroidData
            {
                Id = $"Asteroid_{Guid.NewGuid().ToString()[..4]}",
                Position = new float[] { x, y, z },
                Rotation = new float[] { (float)rnd.NextDouble() * 360, (float)rnd.NextDouble() * 360, (float)rnd.NextDouble() * 360 },
                Scale = 5.0f + (float)rnd.NextDouble() * 15.0f,
                Capacity = 10000
            });
        }
        
        return home;
    }

    // Helper accessors for current sector (Internal logic use, not serialized)
    [JsonIgnore] public int Ore => CurrentSector.Ore;
    [JsonIgnore] public int Wheat => CurrentSector.Wheat;
    [JsonIgnore] public int Potato => CurrentSector.Potato;
    [JsonIgnore] public int Corn => CurrentSector.Corn;
    [JsonIgnore] public int AetherShards => CurrentSector.AetherShards;

    [JsonIgnore] public List<AsteroidData> Asteroids => CurrentSector.Asteroids;
    [JsonIgnore] public List<PlanetData> Planets => CurrentSector.Planets;
    [JsonIgnore] public List<WormholeData> Wormholes => CurrentSector.Wormholes;
    [JsonIgnore] public List<string> Stations => CurrentSector.Stations;
    [JsonIgnore] public Dictionary<string, float[]> StationPositions => CurrentSector.StationPositions;
    [JsonIgnore] public Dictionary<string, ShipMission> ActiveMissions => CurrentSector.ActiveMissions;
    [JsonIgnore] public Dictionary<string, NPCShipData> NPCShips => CurrentSector.NPCShips;
    [JsonIgnore] public List<string> Fleet => CurrentSector.Fleet;

    public void AddOre(int amount)
    {
        CurrentSector.Ore += amount;
        Notify();
    }

    public void AddAetherShards(int amount)
    {
        CurrentSector.AetherShards += amount;
        Notify();
    }

    public void AddWheat(int amount)
    {
        CurrentSector.Wheat += amount;
        Notify();
    }

    public void AddPotato(int amount)
    {
        CurrentSector.Potato += amount;
        Notify();
    }

    public void AddCorn(int amount)
    {
        CurrentSector.Corn += amount;
        Notify();
    }

    public void RegisterAsteroid(AsteroidData asteroid)
    {
        if (!Asteroids.Any(a => a.Id == asteroid.Id))
        {
            Asteroids.Add(asteroid);
        }
    }

    public void RegisterShip(string id, string? sectorId = null)
    {
        var sector = sectorId != null ? Sectors.Find(s => s.Id == sectorId) : CurrentSector;
        if (sector != null && !sector.Fleet.Contains(id))
        {
            sector.Fleet.Add(id);
            Notify();
        }
    }

    public void RegisterStation(string id, float[] position, string? sectorId = null)
    {
        // Special case: The Ark Colonization Station MUST stay in the Home Sector
        if (id == "Ark_Colonization_Station")
        {
            var home = InitializeHomeSector();
            if (!home.Stations.Contains(id))
            {
                home.Stations.Add(id);
                home.StationPositions[id] = position;
                Notify();
            }
            return;
        }

        var sector = sectorId != null ? Sectors.Find(s => s.Id == sectorId) : CurrentSector;
        if (sector != null && !sector.Stations.Contains(id))
        {
            sector.Stations.Add(id);
            sector.StationPositions[id] = position;
            Notify();
        }
    }

    public bool TryBuildShip(string shipType, int cost)
    {
        if (CurrentSector.Ore >= cost)
        {
            CurrentSector.Ore -= cost;
            Notify();
            return true;
        }
        return false;
    }

    public bool TryDeductResources(int ore, int wheat, int potato, int corn)
    {
        var s = CurrentSector;
        if (s.Ore >= ore && s.Wheat >= wheat && s.Potato >= potato && s.Corn >= corn)
        {
            s.Ore -= ore;
            s.Wheat -= wheat;
            s.Potato -= potato;
            s.Corn -= corn;
            Notify();
            return true;
        }
        return false;
    }

    public bool TryBuildColonyBuilding(string planetId, string buildingType, int cost, float[] pos, int populationBoost = 0)
    {
        if (cost > 0 && CurrentSector.Ore < cost) return false;

        var planet = Planets.Find(p => p.Id == planetId);
        if (planet != null)
        {
            if (cost > 0) CurrentSector.Ore -= cost;
            
            var buildingId = $"{buildingType}_{Guid.NewGuid().ToString()[..8]}";
            planet.Buildings.Add(new ColonyBuilding 
            { 
                Id = buildingId, 
                Type = buildingType, 
                Position = pos 
            });

            planet.Population += populationBoost;
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
            Notify();
        }
    }

    public void ResetState()
    {
        Sectors.Clear();
        CurrentSectorId = "Home_Sector";
        InitializeHomeSector();
        Notify();
    }

    public void LoadFromState(GameStateService? savedState)
    {
        if (savedState == null) return;
        CurrentSectorId = savedState.CurrentSectorId ?? "Home_Sector";
        Sectors = savedState.Sectors ?? new List<SectorData>();

        // Cleanup: The Ark Station should ONLY be in the Home Sector.
        // Legacy bug may have registered it in every sector the player visited.
        foreach (var s in Sectors)
        {
            if (s.Id != "Home_Sector")
            {
                if (s.Stations.Contains("Ark_Colonization_Station"))
                    s.Stations.Remove("Ark_Colonization_Station");
                
                if (s.StationPositions.ContainsKey("Ark_Colonization_Station"))
                    s.StationPositions.Remove("Ark_Colonization_Station");
            }
        }

        if (!Sectors.Any()) InitializeHomeSector();
        
        // Explicitly purge NPCs if they are causing "zombie" issues in the save data
        PurgeAllNPCShips();
        
        Notify();
    }

    public void PurgeAllNPCShips()
    {
        foreach (var sector in Sectors)
        {
            sector.NPCShips.Clear();
            foreach (var faction in sector.Factions)
            {
                faction.Ships.Clear();
            }

            // Force player fighters into Patrolling and clear ghost combat targets
            foreach (var mission in sector.ActiveMissions.Values)
            {
                if (mission.Type == GameUnitType.FighterUnit)
                {
                    mission.State = ShipState.Patrolling;
                    mission.CombatTargetId = null;
                    mission.StopRequested = false;
                }
            }
        }
        Console.WriteLine("[GameState] All NPC ships purged and Fighter states reset.");
    }

    public SectorData? GetSectorByStation(string stationId)
    {
        return Sectors.FirstOrDefault(s => s.Stations.Contains(stationId));
    }

    public ShipMission? FindMission(string shipId, out SectorData? sector)
    {
        foreach (var s in Sectors)
        {
            if (s.ActiveMissions.TryGetValue(shipId, out var mission))
            {
                sector = s;
                return mission;
            }
        }
        sector = null;
        return null;
    }

    public void Notify() => OnStateChanged?.Invoke();

    public void PruneDuplicateShips(string stationId)
    {
        var sector = GetSectorByStation(stationId);
        if (sector == null) return;

        var shipsByGroup = sector.ActiveMissions.Values
            .Where(m => m.ParentStationId == stationId && (m.State == ShipState.Idle || m.State == ShipState.Building))
            .GroupBy(m => m.Type)
            .ToList();

        foreach (var group in shipsByGroup)
        {
            // If we have more than 3 idle/building ships of the same type at one station, 
            // it's likely a result of the spillover bug.
            if (group.Count() > 3)
            {
                var toRemove = group.Skip(2).ToList(); // Keep the first 2
                foreach (var ship in toRemove)
                {
                    sector.ActiveMissions.Remove(ship.ShipId);
                    sector.Fleet.Remove(ship.ShipId);
                }
            }
        }
        Notify();
    }
}

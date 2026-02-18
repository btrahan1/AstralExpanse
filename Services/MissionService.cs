using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace AstralExpanse.Services;

public class MissionService
{
    private readonly GameStateService _gameState;
    private readonly BabylonService _babylon;
    private readonly HttpClient _http;
    
    public MissionService(GameStateService gameState, BabylonService babylon, HttpClient http)
    {
        _gameState = gameState;
        _babylon = babylon;
        _http = http;
    }

    public async Task StartMiningMission(string shipId, string asteroidId, float[] asteroidPos)
    {
        var mission = _gameState.FindMission(shipId, out var sector);
        if (mission == null || sector == null) return;

        mission.State = ShipState.MovingToAsteroid;
        mission.AsteroidId = asteroidId;
        mission.AsteroidPosition = asteroidPos;
        mission.CurrentTarget = asteroidPos;

        if (sector.Id == _gameState.CurrentSectorId)
        {
            var shipPos = await _babylon.GetModelPosition(shipId) ?? mission.Position;
            await _babylon.MoveModel(shipId, asteroidPos, GetMoveDuration(shipPos, asteroidPos, 20.0f));
        }
    }

    public async Task StartProbeSearch(string shipId)
    {
        var mission = _gameState.FindMission(shipId, out var sector);
        if (mission == null || sector == null) return;

        mission.State = ShipState.Searching;
        if (sector.Id == _gameState.CurrentSectorId)
        {
            var target = new float[] { 2000, 0, 2000 };
            await _babylon.MoveModel(shipId, target, 180.0f); // 3 minutes
        }
    }

    public async Task StartWormholeScout(string shipId)
    {
        var mission = _gameState.FindMission(shipId, out var sector);
        if (mission == null || sector == null) return;

        mission.State = ShipState.Searching;
        // Scouts search further out than probes
        if (sector.Id == _gameState.CurrentSectorId)
        {
            var rnd = new Random();
            float angle = (float)(rnd.NextDouble() * Math.PI * 2);
            float dist = 4000.0f + (float)(rnd.NextDouble() * 500.0f);
            var target = new float[] { (float)Math.Cos(angle) * dist, 0, (float)Math.Sin(angle) * dist };
            await _babylon.MoveModel(shipId, target, 240.0f); // 4 minutes
        }
    }

    public async Task StartColonizationMission(string shipId, string planetId)
    {
        var mission = _gameState.FindMission(shipId, out var sector);
        if (mission == null || sector == null) return;

        var planet = sector.Planets.Find(p => p.Id == planetId);
        if (planet == null) return;

        mission.State = ShipState.MovingToAsteroid; // Using this as "Moving To Target"
        mission.AsteroidId = planetId;
        mission.AsteroidPosition = planet.Position;
        mission.CurrentTarget = planet.Position;

        if (sector.Id == _gameState.CurrentSectorId)
        {
            var shipPos = await _babylon.GetModelPosition(shipId) ?? mission.Position;
            await _babylon.MoveModel(shipId, planet.Position, GetMoveDuration(shipPos, planet.Position, 15.0f)); // Colony ships are slow
        }
    }

    public async Task StartStellarGatekeeperMission(string shipId, string wormholeId)
    {
        var mission = _gameState.FindMission(shipId, out var sector);
        if (mission == null || sector == null) return;

        var wormhole = sector.Wormholes.Find(w => w.Id == wormholeId);
        if (wormhole == null) return;

        mission.State = ShipState.MovingToAsteroid; // Generic "moving to target"
        mission.AsteroidId = wormholeId;
        mission.AsteroidPosition = wormhole.Position;
        mission.CurrentTarget = wormhole.Position;

        if (sector.Id == _gameState.CurrentSectorId)
        {
            var shipPos = await _babylon.GetModelPosition(shipId) ?? mission.Position;
            await _babylon.MoveModel(shipId, wormhole.Position, GetMoveDuration(shipPos, wormhole.Position, 40.0f));
        }
    }

    public async Task HandleMoveComplete(string id)
    {
        var mission = _gameState.FindMission(id, out var sector);
        if (mission == null || sector == null) return;

        if (mission.State == ShipState.MovingToAsteroid && mission.Type == GameUnitType.MinerUnit)
        {
            mission.State = ShipState.Mining;
            _ = HandleMiningSequence(id, mission.AsteroidId);
        }
        else if (mission.State == ShipState.ReturningToStation)
        {
            mission.State = ShipState.Unloading;
            _ = HandleUnloadingSequence(id);
        }
        else if (mission.State == ShipState.MovingToAsteroid && mission.Type == GameUnitType.TugboatUnit)
        {
            mission.State = ShipState.Towing;
            if (sector.Id == _gameState.CurrentSectorId)
            {
                await _babylon.AttachToParent(mission.AsteroidId, id);
                await _babylon.MoveModel(id, mission.AsteroidPosition, GetMoveDuration(mission.Position, mission.AsteroidPosition, 10.0f));
            }
        }
        else if (mission.State == ShipState.Towing)
        {
            mission.State = ShipState.Idle;
            if (sector.Id == _gameState.CurrentSectorId)
            {
                await _babylon.DetachFromParent(mission.AsteroidId, mission.AsteroidPosition);
            }
        }
        else if (mission.State == ShipState.Searching)
        {
            if (mission.Type == GameUnitType.ProbeUnit)
            {
                await DiscoverPlanet(id);
            }
            else if (mission.Type == GameUnitType.WormholeScoutUnit)
            {
                await DiscoverWormhole(id);
            }
            if (mission.StopRequested || mission.Type == GameUnitType.FighterUnit)
            {
                mission.State = ShipState.Patrolling;
                mission.StopRequested = false;
                _ = Task.Run(async () => { await Task.Delay(200); await HandleMoveComplete(id); });
            }
            else
            {
                mission.State = ShipState.Idle;
            }
        }
        else if (mission.State == ShipState.Patrolling)
        {
            if (mission.StopRequested && mission.Type != GameUnitType.FighterUnit)
            {
                mission.State = ShipState.Idle;
                mission.StopRequested = false;
                return;
            }
            mission.StopRequested = false; // Fighters ignore stop and keep patrolling

            var randomPos = GetRandomWaypoint(150, 300);
            mission.CurrentTarget = randomPos;
            if (sector.Id == _gameState.CurrentSectorId)
            {
                var shipPos = await _babylon.GetModelPosition(id) ?? mission.Position;
                await _babylon.MoveModel(id, randomPos, GetMoveDuration(shipPos, randomPos, 30.0f));
            }
        }
        else if (mission.State == ShipState.Searching)
        {
            if (mission.StopRequested && mission.Type != GameUnitType.FighterUnit)
            {
                mission.State = ShipState.Idle;
                mission.StopRequested = false;
                return;
            }
            if (mission.Type == GameUnitType.FighterUnit)
            {
                mission.State = ShipState.Patrolling;
                _ = Task.Run(async () => { await Task.Delay(200); await HandleMoveComplete(id); });
                return;
            }
            // Probes/Scouts keep searching if not stopped
            if (mission.Type == GameUnitType.ProbeUnit) await StartProbeSearch(id);
            else if (mission.Type == GameUnitType.WormholeScoutUnit) await StartWormholeScout(id);
        }
        else if (mission.State == ShipState.MovingToAsteroid && mission.Type == GameUnitType.ColonyShipUnit)
        {
            mission.State = ShipState.Colonizing;
            _ = HandleColonizationSequence(id, mission.AsteroidId);
        }
        else if (mission.State == ShipState.MovingToAsteroid && mission.Type == GameUnitType.StellarGatekeeperUnit)
        {
            if (sector.Id == _gameState.CurrentSectorId)
            {
                await JumpToNewSector(mission.AsteroidId);
                sector.ActiveMissions.Remove(id);
                sector.Fleet.Remove(id); // Ensure complete removal
                await _babylon.DestroyModel(id, "collapse");
            }
        }
    }

    private async Task HandleMiningSequence(string id, string asteroidId)
    {
        var mission = _gameState.FindMission(id, out var sector);
        if (mission == null || sector == null) return;

        while (mission.State == ShipState.Mining && mission.Cargo < mission.MaxCargo)
        {
            await Task.Delay(1000);
            mission.Cargo = Math.Min(mission.MaxCargo, mission.Cargo + 10);
            
            var asteroid = sector.Asteroids.Find(a => a.Id == asteroidId);
            if (asteroid != null) asteroid.Capacity = Math.Max(0, asteroid.Capacity - 10);

            _gameState.Notify();
        }

        if (mission.State == ShipState.Mining)
        {
            await ReturnToStation(id);
        }
    }

    private async Task HandleUnloadingSequence(string id)
    {
        var mission = _gameState.FindMission(id, out var sector);
        if (mission == null || sector == null) return;

        for (int i = 0; i < 5; i++)
        {
            await Task.Delay(1000);
            if (mission.State != ShipState.Unloading) return; 
            _gameState.Notify();
        }

        sector.Ore += mission.Cargo;
        mission.Cargo = 0;

        if (mission.StopRequested)
        {
            mission.State = ShipState.Idle;
            mission.StopRequested = false;
        }
        else
        {
            await StartMiningMission(id, mission.AsteroidId, mission.AsteroidPosition);
        }
    }

    private async Task HandleColonizationSequence(string shipId, string planetId)
    {
        await Task.Delay(5000);
        
        var mission = _gameState.FindMission(shipId, out var sector);
        if (mission == null || sector == null) return;

        var planet = sector.Planets.Find(p => p.Id == planetId);
        if (planet != null)
        {
            planet.IsColonized = true;
            sector.Ore -= 500;
            
            if (sector.Id == _gameState.CurrentSectorId)
            {
                var colonyJson = await _http.GetStringAsync("assets/colony.json");
                var hubPos = new float[] { planet.Position[0], planet.Position[1] + 26.5f, planet.Position[2] };
                await _babylon.LoadModel(colonyJson, hubPos, id: $"Hub_{planetId}");
            }
        }

        sector.ActiveMissions.Remove(shipId);
        if (sector.Id == _gameState.CurrentSectorId)
        {
            await _babylon.DestroyModel(shipId, "collapse");
        }
    }

    public async Task ReturnToStation(string id)
    {
        var mission = _gameState.FindMission(id, out var sector);
        if (mission == null || sector == null) return;

        mission.State = ShipState.ReturningToStation;
        var stationPos = sector.StationPositions.TryGetValue(mission.ParentStationId, out var p) ? p : new float[] { 0, 0, 0 };
        mission.CurrentTarget = stationPos;
        if (sector.Id == _gameState.CurrentSectorId)
        {
            var shipPos = await _babylon.GetModelPosition(id) ?? mission.Position;
            await _babylon.MoveModel(id, stationPos, GetMoveDuration(shipPos, stationPos, 20.0f));
        }
    }

    private float GetMoveDuration(float[] start, float[] end, float speed)
    {
        float dx = start[0] - end[0];
        float dy = start[1] - end[1];
        float dz = start[2] - end[2];
        float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        return Math.Max(0.5f, dist / speed);
    }

    private float[] GetRandomWaypoint(float minRadius, float maxRadius)
    {
        var random = new Random();
        var angle = random.NextDouble() * Math.PI * 2;
        var radius = minRadius + (random.NextDouble() * (maxRadius - minRadius));
        return new float[] { 
            (float)(Math.Cos(angle) * radius), 
            0, 
            (float)(Math.Sin(angle) * radius) 
        };
    }

    private async Task DiscoverPlanet(string probeId)
    {
        var mission = _gameState.FindMission(probeId, out var sector);
        if (mission == null || sector == null) return;

        var rnd = new Random();
        string id = $"Planet_{Guid.NewGuid().ToString()[..8]}";
        float angle = (float)(rnd.NextDouble() * Math.PI * 2);
        float[] pos = { (float)Math.Cos(angle) * 2500.0f, 0, (float)Math.Sin(angle) * 2500.0f };
        var planet = new PlanetData { Id = id, Name = "Aethelgard Prime", Position = pos, IsDiscovered = true, MonolithsSeeded = true };
        
        // Seed Monoliths around the base location
        for (int i = 0; i < 3; i++)
        {
            float mAngle = (float)(rnd.NextDouble() * Math.PI * 2);
            float mDist = 200 + (float)(rnd.NextDouble() * 400); // 200-600 units away
            planet.Monoliths.Add(new MonolithData 
            { 
                Id = $"Monolith_{id}_{i}",
                Position = new float[] { pos[0] + (float)Math.Cos(mAngle) * mDist, pos[1] + 26.5f, pos[2] + (float)Math.Sin(mAngle) * mDist },
                IsDiscovered = false
            });
        }

        sector.Planets.Add(planet);
        sector.Fleet.Remove(probeId);
        sector.ActiveMissions.Remove(probeId);
        
        if (sector.Id == _gameState.CurrentSectorId)
        {
            await _babylon.LoadModel(GeneratePlanetJson(planet.Name), pos, scale: 50.0f, id: id);
            await _babylon.DestroyModel(probeId, "collapse");
        }
        
        _gameState.Notify();
    }

    private async Task DiscoverWormhole(string scoutId)
    {
        var mission = _gameState.FindMission(scoutId, out var sector);
        if (mission == null || sector == null) return;

        var rnd = new Random();
        string id = $"Wormhole_{Guid.NewGuid().ToString()[..8]}";
        float angle = (float)(rnd.NextDouble() * Math.PI * 2);
        float dist = 4000.0f + (float)(rnd.NextDouble() * 1000.0f); // 4k-5k units out
        float[] pos = { (float)Math.Cos(angle) * dist, 0, (float)Math.Sin(angle) * dist };
        
        var wormhole = new WormholeData 
        { 
            Id = id, 
            Position = pos, 
            IsDiscovered = true,
            TargetSectorId = $"Sector_{Guid.NewGuid().ToString()[..4]}"
        };

        if (sector.Wormholes.Any(w => w.Id == id)) return;
        sector.Wormholes.Add(wormhole);
        sector.ActiveMissions.Remove(scoutId);
        sector.Fleet.Remove(scoutId); // Ensure full cleanup
        
        if (sector.Id == _gameState.CurrentSectorId)
        {
            var wormholeJson = await _http.GetStringAsync("assets/wormhole.json");
            await _babylon.LoadModel(wormholeJson, pos, scale: 1.0f, id: id);
            await _babylon.DestroyModel(scoutId, "collapse");
        }
        
        _gameState.Notify();
    }

    public async Task JumpToNewSector(string wormholeId)
    {
        var wormhole = _gameState.Wormholes.Find(w => w.Id == wormholeId);
        if (wormhole == null) return;

        string targetId = wormhole.TargetSectorId;
        
        // If sector doesn't exist, it will be initialized by the accessor
        if (!_gameState.Sectors.Any(s => s.Id == targetId))
        {
            var newSector = new SectorData 
            { 
                Id = targetId, 
                Name = $"Deep Space {targetId.Split('_').Last()}",
                ThreatLevel = 0.8f, // Hostile expansion sector
                Ore = 1000,
                Wheat = 0, Potato = 0, Corn = 0
            };

            // Spawn Rogue Faction
            var faction = new NPCFactionData { Id = "Alien_Rogue", Name = "The Void Remnant", Hostility = 0.9f };
            newSector.Factions.Add(faction);

            // Spawn 3 alien fighters around a point
            for (int i = 0; i < 3; i++)
            {
                string shipId = $"Alien_Ship_{targetId}_{i}";
                var ship = new NPCShipData 
                { 
                    Id = shipId, 
                    FactionId = faction.Id, 
                    Position = new float[] { 200, 0, 200 + (i * 100) }, 
                    State = ShipState.Patrolling,
                    Health = 200, // Alien fighters are tougher
                    MaxHealth = 200,
                    Firepower = 8, // But human interceptors hit harder
                    Armor = 5
                };
                newSector.NPCShips[shipId] = ship;
                faction.Ships.Add(shipId);
            }

            // Spawn random asteroids for the new sector
            var rnd = new Random();
            for (int i = 0; i < 20; i++)
            {
                float angle = (float)(rnd.NextDouble() * Math.PI * 2);
                float dist = 800 + (float)(rnd.NextDouble() * 1200);
                float x = (float)Math.Cos(angle) * dist;
                float z = (float)Math.Sin(angle) * dist;
                float y = (float)(rnd.NextDouble() - 0.5) * 150;
                
                newSector.Asteroids.Add(new AsteroidData
                {
                    Id = $"Asteroid_{Guid.NewGuid().ToString()[..4]}",
                    Position = new float[] { x, y, z },
                    Rotation = new float[] { (float)rnd.NextDouble() * 360, (float)rnd.NextDouble() * 360, (float)rnd.NextDouble() * 360 },
                    Scale = 5.0f + (float)rnd.NextDouble() * 15.0f,
                    Capacity = rnd.Next(5000, 20000)
                });
            }

            // Establish initial arrival station for the player
            string arrivalStationId = $"Station_Outpost_{targetId.Split('_')[^1]}";
            newSector.Stations.Add(arrivalStationId);
            newSector.StationPositions[arrivalStationId] = new float[] { 0, 0, 0 };

            _gameState.Sectors.Add(newSector);
        }

        // Change sector
        _gameState.CurrentSectorId = targetId;

        _gameState.Notify();
    }

    public string GeneratePlanetJson(string name) => $@"{{ 
        ""Name"": ""{name}"", 
        ""Type"": ""Planet"", 
        ""Parts"": [ 
            {{ ""Id"": ""body"", ""Shape"": ""Sphere"", ""Position"": [0,0,0], ""Rotation"": [0,0,0], ""Scale"": [1,1,1], ""ColorHex"": ""#554433"" }},
            {{ ""Id"": ""atmo"", ""Shape"": ""Sphere"", ""Position"": [0,0,0], ""Rotation"": [0,0,0], ""Scale"": [1.05,1.05,1.05], ""ColorHex"": ""#7eb6ff"", ""Material"": ""Glass"" }}
        ] 
    }}";

    public void RestartMissions()
    {
        _ = RestartMissionsAsync();
    }

    private async Task RestartMissionsAsync()
    {
        foreach (var sector in _gameState.Sectors)
        {
            foreach (var mission in sector.ActiveMissions.Values)
            {
                // Stagger starts to prevent bridge flooding
                await Task.Delay(50);
                
                if (mission.State == ShipState.Mining)
                    _ = HandleMiningSequence(mission.ShipId, mission.AsteroidId);
                else if (mission.State == ShipState.Unloading)
                    _ = HandleUnloadingSequence(mission.ShipId);
                else if (mission.State == ShipState.Colonizing)
                    _ = HandleColonizationSequence(mission.ShipId, mission.AsteroidId);
                else if (mission.State == ShipState.Patrolling || mission.State == ShipState.Searching || mission.State == ShipState.MovingToAsteroid)
                    _ = ResumeShipMission(mission.ShipId);
            }
        }
    }

    public async Task ResumeShipMission(string id)
    {
        var mission = _gameState.FindMission(id, out var sector);
        if (mission == null || sector == null) return;

        if (mission.State == ShipState.Patrolling)
        {
            await HandleMoveComplete(id);
        }
        else if (mission.State == ShipState.Searching)
        {
            if (mission.Type == GameUnitType.ProbeUnit) await StartProbeSearch(id);
            else if (mission.Type == GameUnitType.WormholeScoutUnit) await StartWormholeScout(id);
        }
        else if (mission.State == ShipState.MovingToAsteroid)
        {
            if (sector.Id == _gameState.CurrentSectorId)
            {
                var shipPos = await _babylon.GetModelPosition(id) ?? mission.Position;
                var targetPos = mission.CurrentTarget ?? mission.Position;
                await _babylon.MoveModel(id, targetPos, GetMoveDuration(shipPos, targetPos, 25.0f));
            }
        }
    }
}

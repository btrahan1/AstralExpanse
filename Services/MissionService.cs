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
        if (!_gameState.ActiveMissions.TryGetValue(shipId, out var mission)) return;

        mission.State = ShipState.MovingToAsteroid;
        mission.AsteroidId = asteroidId;
        mission.AsteroidPosition = asteroidPos;
        mission.CurrentTarget = asteroidPos;

        var shipPos = await _babylon.GetModelPosition(shipId) ?? mission.Position;
        await _babylon.MoveModel(shipId, asteroidPos, GetMoveDuration(shipPos, asteroidPos, 20.0f));
    }

    public async Task StartProbeSearch(string shipId)
    {
        if (!_gameState.ActiveMissions.TryGetValue(shipId, out var mission)) return;
        mission.State = ShipState.Searching;
        var target = new float[] { 2000, 0, 2000 };
        await _babylon.MoveModel(shipId, target, 180.0f); // 3 minutes
    }

    public async Task StartColonizationMission(string shipId, string planetId)
    {
        var planet = _gameState.Planets.Find(p => p.Id == planetId);
        if (planet == null) return;

        if (_gameState.ActiveMissions.TryGetValue(shipId, out var mission))
        {
            mission.State = ShipState.MovingToAsteroid; // Using this as "Moving To Target"
            mission.AsteroidId = planetId;
            mission.AsteroidPosition = planet.Position;
            mission.CurrentTarget = planet.Position;

            var shipPos = await _babylon.GetModelPosition(shipId) ?? mission.Position;
            await _babylon.MoveModel(shipId, planet.Position, GetMoveDuration(shipPos, planet.Position, 15.0f)); // Colony ships are slow
        }
    }

    public async Task HandleMoveComplete(string id)
    {
        if (_gameState.ActiveMissions.TryGetValue(id, out var mission))
        {
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
                await _babylon.AttachToParent(mission.AsteroidId, id);
                await _babylon.MoveModel(id, mission.AsteroidPosition, GetMoveDuration(mission.Position, mission.AsteroidPosition, 10.0f));
            }
            else if (mission.State == ShipState.Towing)
            {
                mission.State = ShipState.Idle;
                await _babylon.DetachFromParent(mission.AsteroidId, mission.AsteroidPosition);
            }
            else if (mission.State == ShipState.Searching)
            {
                if (mission.Type == GameUnitType.ProbeUnit)
                {
                    await DiscoverPlanet(id);
                }
                else
                {
                    mission.State = ShipState.Idle;
                }
            }
            else if (mission.State == ShipState.Patrolling)
            {
                var randomPos = GetRandomWaypoint(150, 300);
                mission.CurrentTarget = randomPos;
                var shipPos = await _babylon.GetModelPosition(id) ?? mission.Position;
                await _babylon.MoveModel(id, randomPos, GetMoveDuration(shipPos, randomPos, 30.0f));
            }
            else if (mission.State == ShipState.MovingToAsteroid && mission.Type == GameUnitType.ColonyShipUnit)
            {
                mission.State = ShipState.Colonizing;
                _ = HandleColonizationSequence(id, mission.AsteroidId);
            }
        }
    }

    private async Task HandleMiningSequence(string id, string asteroidId)
    {
        if (_gameState.ActiveMissions.TryGetValue(id, out var mission))
        {
            while (mission.State == ShipState.Mining && mission.Cargo < mission.MaxCargo)
            {
                await Task.Delay(1000);
                mission.Cargo = Math.Min(mission.MaxCargo, mission.Cargo + 10);
                _gameState.DepleteAsteroid(asteroidId, 10);
                _gameState.Notify();
            }

            if (mission.State == ShipState.Mining)
            {
                await ReturnToStation(id);
            }
        }
    }

    private async Task HandleUnloadingSequence(string id)
    {
        if (_gameState.ActiveMissions.TryGetValue(id, out var mission))
        {
            for (int i = 0; i < 5; i++)
            {
                await Task.Delay(1000);
                if (mission.State != ShipState.Unloading) return; 
                _gameState.Notify();
            }

            _gameState.AddOre(mission.Cargo);
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
    }

    private async Task HandleColonizationSequence(string shipId, string planetId)
    {
        await Task.Delay(5000);
        
        var planet = _gameState.Planets.Find(p => p.Id == planetId);
        if (planet != null)
        {
            planet.IsColonized = true;
            _gameState.AddOre(-500); // Placeholder cost if not already deducted
            
            var colonyJson = await _http.GetStringAsync("assets/colony.json");
            var hubPos = new float[] { planet.Position[0], planet.Position[1] + 26.5f, planet.Position[2] };
            await _babylon.LoadModel(colonyJson, hubPos, id: $"Hub_{planetId}");
        }

        _gameState.ActiveMissions.Remove(shipId);
        await _babylon.DestroyModel(shipId, "collapse");
    }

    public async Task ReturnToStation(string id)
    {
        if (_gameState.ActiveMissions.TryGetValue(id, out var mission))
        {
            mission.State = ShipState.ReturningToStation;
            var stationPos = _gameState.StationPositions.TryGetValue(mission.ParentStationId, out var p) ? p : new float[] { 0, 0, 0 };
            mission.CurrentTarget = stationPos;
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

        _gameState.Planets.Add(planet);
        _gameState.Fleet.Remove(probeId);
        _gameState.ActiveMissions.Remove(probeId);
        
        await _babylon.LoadModel(GeneratePlanetJson(planet.Name), pos, scale: 50.0f, id: id);
        await _babylon.DestroyModel(probeId, "collapse");
        
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
        foreach (var mission in _gameState.ActiveMissions.Values)
        {
            if (mission.State == ShipState.Mining)
                _ = HandleMiningSequence(mission.ShipId, mission.AsteroidId);
            else if (mission.State == ShipState.Unloading)
                _ = HandleUnloadingSequence(mission.ShipId);
            else if (mission.State == ShipState.Colonizing)
                _ = HandleColonizationSequence(mission.ShipId, mission.AsteroidId);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace AstralExpanse.Services;

public class ConstructionService
{
    private readonly GameStateService _gameState;
    private readonly BabylonService _babylon;
    private readonly HttpClient _http;
    private readonly MissionService _missionService;

    public ConstructionService(GameStateService gameState, BabylonService babylon, HttpClient http, MissionService missionService)
    {
        _gameState = gameState;
        _babylon = babylon;
        _http = http;
        _missionService = missionService;
    }

    public async Task BuildNewShipFromStation(GameUnitType unitType, string builderStationId)
    {
        int cost = GetUnitCost(unitType);

        if (_gameState.TryBuildShip(unitType.ToString(), cost))
        {
            var id = unitType == GameUnitType.SubStationUnit ? 
                $"Station_Sub_{Guid.NewGuid().ToString()[..8]}" : 
                $"Ship_{unitType}_{Guid.NewGuid().ToString()[..8]}";
            
            var asset = GetUnitAsset(unitType);
            
            var stationPos = _gameState.StationPositions.TryGetValue(builderStationId, out var sPos) ? sPos : new float[] { 0, 0, 0 };
            var spawnPos = unitType == GameUnitType.SubStationUnit ? 
                new float[] { stationPos[0], stationPos[1], stationPos[2] + 120 } : 
                new float[] { stationPos[0], stationPos[1], stationPos[2] };

            var json = await _http.GetStringAsync($"assets/{asset}");
            
            if (unitType == GameUnitType.ProbeUnit) json = json.Replace("\"Name\": \"Venture_Mining_Vessel\"", "\"Name\": \"Stellar_Probe\"");

            float buildDuration = cost / 10.0f;
            
            var mission = new ShipMission { 
                ShipId = id, 
                Type = unitType, 
                State = ShipState.Building,
                Position = spawnPos,
                ParentStationId = builderStationId,
                ConstructionProgress = 0,
                ConstructionTarget = buildDuration
            };
            
            _gameState.ActiveMissions[id] = mission;
            
            if (unitType != GameUnitType.SubStationUnit)
            {
                _gameState.RegisterShip(id);
            }
            else
            {
                _gameState.RegisterStation(id, spawnPos);
            }

            await _babylon.LoadModel(json, spawnPos, id: id);
            
            _ = HandleConstructionSequence(id, buildDuration);
        }
    }

    public async Task HandleConstructionSequence(string id, float durationSeconds)
    {
        if (_gameState.ActiveMissions.TryGetValue(id, out var mission))
        {
            float elapsed = (mission.ConstructionProgress / 100.0f) * durationSeconds;
            int steps = 20; 
            float interval = durationSeconds / steps;

            while (mission.ConstructionProgress < 100 && mission.State == ShipState.Building)
            {
                await Task.Delay((int)(interval * 1000));
                elapsed += interval;
                mission.ConstructionProgress = Math.Min(100, (elapsed / durationSeconds) * 100);
                
                // We don't necessarily need to trigger state change here if UI binds directly to mission object
                // but let's notify via GameState if needed.
            }

            if (mission.ConstructionProgress >= 100)
            {
                mission.State = ShipState.Idle;
                mission.ConstructionProgress = 100;

                if (mission.Type == GameUnitType.ProbeUnit)
                {
                    _ = LaunchProbe(id);
                }
            }
        }
    }

    private async Task LaunchProbe(string id)
    {
        if (_gameState.ActiveMissions.TryGetValue(id, out var mission))
        {
            mission.State = ShipState.Searching;
            // Far off vector
            var target = new float[] { 2000, 0, 2000 }; 
            await _babylon.MoveModel(id, target, 180.0f); // 3 minutes
        }
    }

    public int GetUnitCost(GameUnitType unitType) => unitType switch {
        GameUnitType.MinerUnit => 100,
        GameUnitType.ScoutUnit => 150,
        GameUnitType.FighterUnit => 250,
        GameUnitType.TugboatUnit => 300,
        GameUnitType.SubStationUnit => 500,
        GameUnitType.ProbeUnit => 200, // Assuming this is the probe pack cost
        GameUnitType.ColonyShipUnit => 1000,
        _ => 100
    };

    private string GetUnitAsset(GameUnitType unitType) => unitType switch {
        GameUnitType.MinerUnit => "miner.json",
        GameUnitType.ScoutUnit => "scout.json",
        GameUnitType.FighterUnit => "fighter.json",
        GameUnitType.TugboatUnit => "tugboat.json",
        GameUnitType.SubStationUnit => "substation.json",
        GameUnitType.ProbeUnit => "miner.json", 
        GameUnitType.ColonyShipUnit => "colony_ship.json",
        _ => "miner.json"
    };
}

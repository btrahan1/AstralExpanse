using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace AstralExpanse.Services;

public class ConstructionService
{
    private readonly GameStateService _gameState;
    private readonly BabylonService _babylon;
    private readonly HttpClient _http;
    private readonly MissionService _missionService;
    private readonly HashSet<string> _activeSequences = new();

    public ConstructionService(GameStateService gameState, BabylonService babylon, HttpClient http, MissionService missionService)
    {
        _gameState = gameState;
        _babylon = babylon;
        _http = http;
        _missionService = missionService;
    }

    public async Task BuildNewShipFromStation(GameUnitType unitType, string builderStationId, string? targetId = null)
    {
        int cost = GetUnitCost(unitType);

        if (_gameState.TryBuildShip(unitType.ToString(), cost))
        {
            var id = unitType == GameUnitType.SubStationUnit ? 
                $"Station_Sub_{Guid.NewGuid().ToString().Split('-').Last()}" : 
                $"Ship_{unitType}_{Guid.NewGuid().ToString().Split('-').Last()}";
            
            var sector = _gameState.GetSectorByStation(builderStationId) ?? _gameState.CurrentSector;
            
            var asset = GetUnitAsset(unitType);
            
            var stationPos = sector.StationPositions.TryGetValue(builderStationId, out var sPos) ? sPos : new float[] { 0, 0, 0 };
            float[] spawnPos;

            if (builderStationId == "Ark_Colonization_Station")
            {
                // Randomly pick one of the 4 "feet" (docking hubs)
                var rndSpawn = new Random();
                int bay = rndSpawn.Next(4);
                spawnPos = bay switch
                {
                    0 => new float[] { stationPos[0], stationPos[1] - 10, stationPos[2] + 45 },
                    1 => new float[] { stationPos[0], stationPos[1] - 10, stationPos[2] - 45 },
                    2 => new float[] { stationPos[0] + 45, stationPos[1] - 10, stationPos[2] },
                    _ => new float[] { stationPos[0] - 45, stationPos[1] - 10, stationPos[2] }
                };
            }
            else
            {
                spawnPos = unitType == GameUnitType.SubStationUnit ? 
                    new float[] { stationPos[0], stationPos[1], stationPos[2] + 120 } : 
                    new float[] { stationPos[0], stationPos[1], stationPos[2] + 15 }; // Default offset for substations
            }

            var json = await _http.GetStringAsync($"assets/{asset}");
            
            if (unitType == GameUnitType.ProbeUnit) json = json.Replace("\"Name\": \"Venture_Mining_Vessel\"", "\"Name\": \"Stellar_Probe\"");

            float buildDuration = cost / 10.0f;
            
            var mission = new ShipMission { 
                ShipId = id, 
                Type = unitType, 
                State = ShipState.Building,
                Position = spawnPos,
                SectorId = sector.Id,
                ParentStationId = builderStationId,
                ConstructionProgress = 0,
                ConstructionTarget = buildDuration,
                TargetWormholeId = targetId,
                Health = GetMaxHealth(unitType),
                MaxHealth = GetMaxHealth(unitType),
                Armor = GetUnitArmor(unitType),
                Firepower = GetUnitFirepower(unitType)
            };
            
            sector.ActiveMissions[id] = mission;
            
            if (unitType != GameUnitType.SubStationUnit)
            {
                if (!sector.Fleet.Contains(id)) sector.Fleet.Add(id);
            }
            else
            {
                if (!sector.Stations.Contains(id))
                {
                    sector.Stations.Add(id);
                    sector.StationPositions[id] = spawnPos;
                }
            }

            if (sector.Id == _gameState.CurrentSectorId)
            {
                var meta = new Dictionary<string, object> { { "unitType", unitType.ToString() }, { "state", "Building" }, { "faction", "Player" } };
                await _babylon.LoadModel(json, spawnPos, id: id, metadata: meta);
            }
            
            _ = HandleConstructionSequence(id, buildDuration, sector.Id);
        }
    }

    public async Task HandleConstructionSequence(string id, float durationSeconds, string sectorId)
    {
        if (!_activeSequences.Add(id)) return;
        try {
            var sector = _gameState.Sectors.FirstOrDefault(s => s.Id == sectorId);
            if (sector == null) return;

            if (sector.ActiveMissions.TryGetValue(id, out var mission))
            {
                float elapsed = (mission.ConstructionProgress / 100.0f) * durationSeconds;
                int steps = 20; 
                float interval = durationSeconds / steps;

                while (mission.ConstructionProgress < 100 && mission.State == ShipState.Building)
                {
                    await Task.Delay((int)(interval * 1000));
                    elapsed += interval;
                    mission.ConstructionProgress = Math.Min(100, (elapsed / durationSeconds) * 100);
                    _gameState.Notify();
                }

                if (mission.ConstructionProgress >= 100)
                {
                    mission.State = ShipState.Idle;
                    mission.ConstructionProgress = 100;
                    
                    // Clear "Building" state metadata so JS can take over if needed (or move to Idle)
                    if (sector.Id == _gameState.CurrentSectorId) 
                        _ = _babylon.UpdateModelMetadata(id, "state", "Idle");

                    if (mission.Type == GameUnitType.ProbeUnit)
                    {
                        _ = _missionService.StartProbeSearch(id);
                    }
                    else if (mission.Type == GameUnitType.WormholeScoutUnit)
                    {
                        _ = _missionService.StartWormholeScout(id);
                    }
                    else if (mission.Type == GameUnitType.StellarGatekeeperUnit && !string.IsNullOrEmpty(mission.TargetWormholeId))
                    {
                        _ = _missionService.StartStellarGatekeeperMission(id, mission.TargetWormholeId);
                    }
                }
            }
        } finally {
            _activeSequences.Remove(id);
        }
    }

    public int GetUnitCost(GameUnitType unitType) => unitType switch {
        GameUnitType.MinerUnit => 100,
        GameUnitType.ScoutUnit => 150,
        GameUnitType.FighterUnit => 250,
        GameUnitType.TugboatUnit => 300,
        GameUnitType.SubStationUnit => 500,
        GameUnitType.ProbeUnit => 200, 
        GameUnitType.ColonyShipUnit => 1000,
        GameUnitType.WormholeScoutUnit => 400,
        GameUnitType.StellarGatekeeperUnit => 1500,
        _ => 100
    };

    private float GetMaxHealth(GameUnitType unitType) => unitType switch {
        GameUnitType.ColonyShipUnit => 500,
        GameUnitType.FighterUnit => 150,
        GameUnitType.SubStationUnit => 1000,
        _ => 100
    };

    private float GetUnitArmor(GameUnitType unitType) => unitType switch {
        GameUnitType.FighterUnit => 10,
        GameUnitType.ColonyShipUnit => 20,
        GameUnitType.SubStationUnit => 50,
        _ => 0
    };

    private float GetUnitFirepower(GameUnitType unitType) => unitType switch {
        GameUnitType.FighterUnit => 15,
        GameUnitType.MinerUnit => 2, 
        GameUnitType.TugboatUnit => 5,
        GameUnitType.StellarGatekeeperUnit => 25,
        _ => 1
    };

    private string GetUnitAsset(GameUnitType unitType) => unitType switch {
        GameUnitType.MinerUnit => "miner.json",
        GameUnitType.ScoutUnit => "scout.json",
        GameUnitType.FighterUnit => "fighter.json",
        GameUnitType.TugboatUnit => "tugboat.json",
        GameUnitType.SubStationUnit => "substation.json",
        GameUnitType.ProbeUnit => "miner.json", 
        GameUnitType.ColonyShipUnit => "colony_ship.json",
        GameUnitType.WormholeScoutUnit => "wormhole_scout.json",
        GameUnitType.StellarGatekeeperUnit => "stellar_gatekeeper.json",
        _ => "miner.json"
    };
}

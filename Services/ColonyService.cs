using Microsoft.JSInterop;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AstralExpanse.Services;

public class ColonyService
{
    private readonly GameStateService _gameState;
    private readonly BabylonService _babylon;
    private readonly HttpClient _http;

    public ColonyService(GameStateService gameState, BabylonService babylon, HttpClient http)
    {
        _gameState = gameState;
        _babylon = babylon;
        _http = http;
    }

    public bool IsPlacing { get; private set; }

    public async Task<bool> BuildBuilding(string planetId, string type)
    {
        int cost = type switch
        {
            "FarmHouse" => 500,
            "ResearchCenter" => 1200,
            "WheatField" => 100,
            "PotatoField" => 150,
            "CornField" => 200,
            "Habitat" => 1000,
            _ => 9999
        };

        if (type == "Habitat")
        {
            if (_gameState.Ore < 1000 || _gameState.Wheat < 1000 || _gameState.Potato < 1000 || _gameState.Corn < 1000)
                return false;
        }
        else if (_gameState.Ore < cost) return false;

        IsPlacing = true;
        try
        {
            var asset = GetAssetForType(type);
            var json = await _http.GetStringAsync($"assets/{asset}");
            
            var position = await _babylon.StartPlacement(json);
            if (position != null)
            {
                // Reset IsPlacing immediately after placement is confirmed
                IsPlacing = false;

                if (type == "Habitat")
                {
                    // For Habitat, we deduct all resources
                    if (_gameState.TryDeductResources(1000, 1000, 1000, 1000))
                    {
                        var planet = _gameState.Planets.Find(p => p.Id == planetId);
                        if (planet != null)
                        {
                            var projectId = $"Build_{Guid.NewGuid().ToString()[..4]}";
                            var project = new ConstructionProject 
                            { 
                                Id = projectId, 
                                BuildingType = type,
                                Progress = 0,
                                RemainingSeconds = 100,
                                Position = position
                            };
                            planet.ConstructionProjects.Add(project);
                            _gameState.Notify();

                            // Run construction progress feedback in a non-blocking background task
                            _ = Task.Run(async () => 
                            {
                                try
                                {
                                    const int totalSeconds = 100;
                                    for (int i = 1; i <= totalSeconds; i++)
                                    {
                                        await Task.Delay(1000);
                                        project.RemainingSeconds = totalSeconds - i;
                                        project.Progress = (float)i / totalSeconds;
                                        
                                        // Update world-space UI
                                        await _babylon.UpdateConstructionProgress(project.Id, type, project.Progress, position);
                                        
                                        _gameState.Notify();
                                    }
                                    
                                    // Complete construction
                                    planet.ConstructionProjects.Remove(project);
                                    await _babylon.RemoveConstructionProgress(project.Id);

                                    if (_gameState.TryBuildColonyBuilding(planetId, type, 0, position, populationBoost: 100))
                                    {
                                        var buildingId = $"ColonyBuilding_{planetId}_{type}_{Guid.NewGuid().ToString()[..4]}";
                                        await _babylon.LoadModel(json, position, id: buildingId);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    // Log or handle error if needed
                                    System.Console.WriteLine($"Error during background construction: {ex.Message}");
                                }
                            });
                            
                            return true;
                        }
                    }
                }
                else
                {
                    if (_gameState.TryBuildColonyBuilding(planetId, type, cost, position))
                    {
                        var buildingId = $"ColonyBuilding_{planetId}_{type}_{System.Guid.NewGuid().ToString()[..4]}";
                        await _babylon.LoadModel(json, position, id: buildingId);
                        return true;
                    }
                }
            }
        }
        finally
        {
            IsPlacing = false;
        }

        return false;
    }

    private string GetAssetForType(string type) => type switch
    {
        "FarmHouse" => "farmhouse.json",
        "ResearchCenter" => "research_center.json",
        "WheatField" => "wheat_field.json",
        "PotatoField" => "potato_field.json",
        "CornField" => "corn_field.json",
        "Habitat" => "habitat.json",
        _ => "unit.json"
    };

    public async Task<bool> BuildFarmingBot(string planetId)
    {
        if (_gameState.Ore < 300) return false;

        var planet = _gameState.Planets.Find(p => p.Id == planetId);
        if (planet == null) return false;

        // Find a FarmHouse to act as the home base
        var farmhouse = planet.Buildings.Find(b => b.Type == "FarmHouse");
        if (farmhouse == null) return false;

        _gameState.Ore -= 300;
        
        var botId = $"Bot_{planetId}_{System.Guid.NewGuid().ToString()[..4]}";
        var botPos = new float[] { farmhouse.Position[0] + 5, farmhouse.Position[1], farmhouse.Position[2] + 5 };
        var bot = new FarmingBot
        {
            Id = botId,
            ParentFarmHouseId = farmhouse.Id,
            Position = botPos,
            HomePosition = (float[])botPos.Clone(),
            CurrentPhase = FarmingPhase.Planting,
            PhaseProgress = 0
        };

        planet.Bots.Add(bot);
        
        var botJson = await _http.GetStringAsync("assets/farming_bot.json");
        await _babylon.LoadModel(botJson, bot.Position, scale: 2.5f, id: botId);
        
        _gameState.Notify();
        return true;
    }

    public async Task<bool> BuildRover(string planetId)
    {
        if (_gameState.Ore < 1200) return false;

        var planet = _gameState.Planets.Find(p => p.Id == planetId);
        if (planet == null || planet.Rover != null) return false;

        _gameState.Ore -= 1200;
        
        var roverId = $"Rover_{planetId}_{System.Guid.NewGuid().ToString()[..4]}";
        var spawnPos = new float[] { 0, 0, 0 }; // Default center
        
        // Find a ResearchCenter or FarmHouse to spawn near
        var spawnNear = planet.Buildings.Find(b => b.Type == "ResearchCenter") ?? planet.Buildings.Find(b => b.Type == "FarmHouse");
        if (spawnNear != null)
        {
            spawnPos = new float[] { spawnNear.Position[0] + 25, spawnNear.Position[1], spawnNear.Position[2] + 25 };
        }

        planet.Rover = new RoverData
        {
            Id = roverId,
            Position = spawnPos,
            RotationY = 0,
            IsActive = false
        };

        var roverJson = await _http.GetStringAsync("assets/rover.json");
        await _babylon.SpawnRover(roverJson, spawnPos);
        await _babylon.SetCameraTarget(spawnPos[0], spawnPos[1], spawnPos[2]);
        
        _gameState.Notify();
        return true;
    }

    public async Task CancelPlacement()
    {
        await _babylon.CancelPlacement();
        IsPlacing = false;
    }

    public async Task LoadColonyBuildings(string planetId)
    {
        var planet = _gameState.Planets.Find(p => p.Id == planetId);
        if (planet == null) return;

        foreach (var building in planet.Buildings)
        {
            var asset = GetAssetForType(building.Type);
            var json = await _http.GetStringAsync($"assets/{asset}");
            await _babylon.LoadModel(json, building.Position, id: building.Id);
        }

        var botJson = await _http.GetStringAsync("assets/farming_bot.json");
        foreach (var bot in planet.Bots)
        {
            await _babylon.LoadModel(botJson, bot.Position, scale: 2.5f, id: bot.Id);
        }

        if (planet.Rover != null)
        {
            var roverJson = await _http.GetStringAsync("assets/rover.json");
            await _babylon.SpawnRover(roverJson, planet.Rover.Position);
        }
    }
}

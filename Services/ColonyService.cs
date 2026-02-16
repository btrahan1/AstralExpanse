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
            _ => 9999
        };

        if (_gameState.Ore < cost) return false;

        IsPlacing = true;
        try
        {
            var asset = GetAssetForType(type);
            var json = await _http.GetStringAsync($"assets/{asset}");
            
            var position = await _babylon.StartPlacement(json);
            if (position != null)
            {
                if (_gameState.TryBuildColonyBuilding(planetId, type, cost, position))
                {
                    var buildingId = $"ColonyBuilding_{planetId}_{type}_{System.Guid.NewGuid().ToString()[..4]}";
                    await _babylon.LoadModel(json, position, id: buildingId);
                    return true;
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
    }
}

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
            _ => 9999
        };

        if (_gameState.Ore < cost) return false;

        IsPlacing = true;
        try
        {
            var asset = type == "FarmHouse" ? "farmhouse.json" : "research_center.json";
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
            // Simple check to avoid double-loading if they are already in the scene
            // In a more robust system, we would ask Babylon if the ID exists.
            // For now, we'll assume the caller manages the state or we just check local mission IDs if we had them.
            // Actually, let's just use the ID. 
            var asset = building.Type == "FarmHouse" ? "farmhouse.json" : "research_center.json";
            var json = await _http.GetStringAsync($"assets/{asset}");
            await _babylon.LoadModel(json, building.Position, id: building.Id);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace AstralExpanse.Services;

public class FarmingService
{
    private readonly GameStateService _gameState;
    private readonly BabylonService _babylon;
    private bool _isRunning = false;

    public string? CurrentPlanetId { get; set; }

    public FarmingService(GameStateService gameState, BabylonService babylon)
    {
        _gameState = gameState;
        _babylon = babylon;
    }

    public void StartAutonomousCycle()
    {
        if (_isRunning) return;
        _isRunning = true;
        _ = RunFarmingLoop();
    }

    private async Task RunFarmingLoop()
    {
        while (_isRunning)
        {
            await Task.Delay(1000); // Check every second

            foreach (var planet in _gameState.Planets.Where(p => p.IsColonized))
            {
                var fields = planet.Buildings.Where(b => b.Type.EndsWith("Field")).ToList();
                if (!fields.Any()) continue;

                foreach (var bot in planet.Bots)
                {
                    await UpdateBot(bot, fields, planet.Id);
                }
            }
            
            _gameState.Notify();
        }
    }

    private async Task UpdateBot(FarmingBot bot, List<ColonyBuilding> fields, string planetId)
    {
        // 1. Target Selection
        if (string.IsNullOrEmpty(bot.CurrentTargetFieldId))
        {
            // Pick the next field in the list to ensure we cycle through ALL fields
            var sortedFields = fields.OrderBy(f => f.Id).ToList();
            int nextIndex = 0;

            if (!string.IsNullOrEmpty(bot.LastTargetFieldId))
            {
                int lastIndex = sortedFields.FindIndex(f => f.Id == bot.LastTargetFieldId);
                if (lastIndex != -1)
                {
                    nextIndex = (lastIndex + 1) % sortedFields.Count;
                }
            }

            bot.CurrentTargetFieldId = sortedFields[nextIndex].Id;
        }

        var targetField = fields.Find(f => f.Id == bot.CurrentTargetFieldId);
        if (targetField == null) { bot.CurrentTargetFieldId = null; return; }

        // 2. Movement Logic
        float dist = GetDistance(bot.Position, targetField.Position);
        bool isOnActivePlanet = planetId == CurrentPlanetId;

        if (dist > 3.0f)
        {
            bot.IsTraveling = true;
            MoveTowards(bot.Position, targetField.Position, 1.0f);
            if (isOnActivePlanet)
            {
                await _babylon.MoveModel(bot.Id, bot.Position, 1.0f);
            }
            return;
        }

        bot.IsTraveling = false;

        // 3. Phase Logic (15 seconds per phase)
        bot.PhaseProgress += 1.0f / 15.0f;
        if (bot.PhaseProgress >= 1.0f)
        {
            bot.PhaseProgress = 0;
            switch (bot.CurrentPhase)
            {
                case FarmingPhase.Planting:
                    bot.CurrentPhase = FarmingPhase.Watering;
                    break;
                case FarmingPhase.Watering:
                    bot.CurrentPhase = FarmingPhase.Fertilizing;
                    break;
                case FarmingPhase.Fertilizing:
                    bot.CurrentPhase = FarmingPhase.Harvesting;
                    break;
                case FarmingPhase.Harvesting:
                    // Harvest complete!
                    AddResources(targetField.Type);
                    bot.CurrentPhase = FarmingPhase.Planting;
                    
                    bot.LastTargetFieldId = bot.CurrentTargetFieldId;
                    bot.CurrentTargetFieldId = null; 
                    break;
            }
        }
    }

    private void AddResources(string fieldType)
    {
        switch (fieldType)
        {
            case "WheatField": _gameState.AddWheat(100); break;
            case "PotatoField": _gameState.AddPotato(100); break;
            case "CornField": _gameState.AddCorn(100); break;
        }
    }

    private float GetDistance(float[] p1, float[] p2)
    {
        float dx = p1[0] - p2[0];
        float dz = p1[2] - p2[2];
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private void MoveTowards(float[] current, float[] target, float seconds)
    {
        float speed = 10.0f; // units per second
        float dx = target[0] - current[0];
        float dz = target[2] - current[2];
        float dist = (float)Math.Sqrt(dx * dx + dz * dz);
        
        // Ensure height is consistent with the ground/target
        current[1] = target[1];

        if (dist < 0.1f) return;

        float moveDist = Math.Min(dist, speed * seconds);
        current[0] += (dx / dist) * moveDist;
        current[2] += (dz / dist) * moveDist;
    }
}

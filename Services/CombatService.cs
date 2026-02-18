using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AstralExpanse.Services;

public class CombatService
{
    private readonly GameStateService _gameState;
    private readonly BabylonService _babylon;
    private readonly HttpClient _http;
    private readonly MissionService _missionService;
    private bool _isRunning = false;
    private DateTime _lastRespawnTime = DateTime.Now;

    public CombatService(GameStateService gameState, BabylonService babylon, HttpClient http, MissionService missionService)
    {
        _gameState = gameState;
        _babylon = babylon;
        _http = http;
        _missionService = missionService;
    }

    public void StartCombatLoop()
    {
        if (_isRunning) return;
        _isRunning = true;
        _ = RunLoop();
    }

    private async Task RunLoop()
    {
        while (_isRunning)
        {
            try
            {
                await ProcessCombatTick();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CombatService Error: {ex.Message}");
            }
            await Task.Delay(500);
        }
    }

    private async Task ProcessCombatTick()
    {
        foreach (var sector in _gameState.Sectors)
        {
            bool isCurrentSector = sector.Id == _gameState.CurrentSectorId;

            try
            {
                // Real-Time Position Sync (Parallelized for performance)
                if (isCurrentSector)
                {
                    var syncTasks = new List<Task>();
                    foreach (var m in sector.ActiveMissions.Values)
                    {
                        syncTasks.Add(Task.Run(async () => {
                            var pos = await _babylon.GetModelPosition(m.ShipId);
                            if (pos != null) m.Position = pos;
                        }));
                    }
                    foreach (var npc in sector.NPCShips.Values)
                    {
                        syncTasks.Add(Task.Run(async () => {
                            var pos = await _babylon.GetModelPosition(npc.Id);
                            if (pos != null) npc.Position = pos;
                        }));
                    }
                    if (syncTasks.Any()) await Task.WhenAll(syncTasks);
                }

                // Player Ships Engagement
                foreach (var mission in sector.ActiveMissions.Values.ToList())
                {
                    if (mission.Type != GameUnitType.FighterUnit || 
                        mission.Health <= 0 || 
                        mission.State == ShipState.Destroyed || 
                        mission.State == ShipState.Building ||
                        mission.State == ShipState.Repairing) continue;

                    var target = FindNearestEnemy(mission, sector);
                    if (target != null)
                    {
                        // Fighters fire while maintaining their current mission (Patrolling, MovingToAsteroid, etc.)
                        mission.CombatTargetId = target.Id;
                        await EngageTarget(mission.ShipId, target, sector);
                    }
                    else 
                    {
                        mission.CombatTargetId = null;
                    }
                }

                // NPC Ships Engagement
                foreach (var npc in sector.NPCShips.Values.ToList())
                {
                    if (npc.Health <= 0 || npc.State == ShipState.Destroyed) continue;

                    var targetMission = FindNearestPlayerShip(npc, sector);
                    if (targetMission != null)
                    {
                        // NPCs fire while maintaining their mission (Patrolling, etc.)
                        npc.TargetId = targetMission.ShipId;
                        await EngagePlayerShip(npc.Id, targetMission, sector);
                    }
                    else 
                    {
                        npc.TargetId = string.Empty;
                    }
                }

                // Automated Repairs: If sector is clear, send damaged ships home
                if (!sector.NPCShips.Any())
                {
                    foreach (var mission in sector.ActiveMissions.Values.ToList())
                    {
                        if (mission.Health < mission.MaxHealth && 
                            mission.State != ShipState.Repairing && 
                            mission.State != ShipState.ReturningToStation &&
                            mission.State != ShipState.Building &&
                            mission.State != ShipState.Destroyed)
                        {
                            Console.WriteLine($"[CombatService] Sending damaged ship {mission.ShipId} to base for repairs.");
                            _ = _missionService.ReturnToStation(mission.ShipId);
                        }
                    }
                }

                // NPC Respawn Logic (If it's a hostile sector and empty)
                if (sector.ThreatLevel > 0.5f && !sector.NPCShips.Any())
                {
                    if ((DateTime.Now - _lastRespawnTime).TotalSeconds > 30)
                    {
                        _lastRespawnTime = DateTime.Now;
                        Console.WriteLine($"[CombatService] Respawning NPC wave in {sector.Name}...");
                        await RespawnNPCWave(sector);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CombatService] Tick Error in sector {sector.Id}: {ex.Message}");
            }
        }
        _gameState.Notify();
    }

    private async Task RespawnNPCWave(SectorData sector)
    {
        var faction = sector.Factions.FirstOrDefault(f => f.Id == "Alien_Rogue");
        if (faction == null) return;

        for (int i = 0; i < 3; i++)
        {
            string shipId = $"Alien_Ship_{sector.Id}_{Guid.NewGuid().ToString()[..4]}";
            var ship = new NPCShipData 
            { 
                Id = shipId, 
                FactionId = faction.Id, 
                Position = new float[] { 
                    (float)(2500 * Math.Cos(i * 2.0)), 
                    (float)((new Random().NextDouble() - 0.5) * 200), 
                    (float)(2500 * Math.Sin(i * 2.0)) 
                }, 
                State = ShipState.Patrolling,
                Health = 200,
                MaxHealth = 200,
                Firepower = 8,
                Armor = 5
            };
            sector.NPCShips[shipId] = ship;
            faction.Ships.Add(shipId);

            if (sector.Id == _gameState.CurrentSectorId)
            {
                var npcJson = await _http.GetStringAsync("assets/alien_fighter.json");
                var meta = new Dictionary<string, object> { { "unitType", "FighterUnit" }, { "type", "NPC" }, { "faction", faction.Id } };
                await _babylon.LoadModel(npcJson, ship.Position, scale: 3.0f, id: shipId, metadata: meta);
            }
        }
    }

    private ShipMission? FindNearestPlayerShip(NPCShipData npc, SectorData sector)
    {
        ShipMission? nearest = null;
        float minDist = 1000; // Large aggro sweep

        var toRemove = new List<string>();
        foreach (var mission in sector.ActiveMissions.Values)
        {
            if (mission.Health <= 0 || mission.State == ShipState.Destroyed || mission.State == ShipState.Building) 
            {
                if (mission.Health <= 0) toRemove.Add(mission.ShipId);
                continue;
            }
            float dist = GetDistance(npc.Position, mission.Position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = mission;
            }
        }
        
        // Safety Clean: Remove dead ships found during search
        foreach (var id in toRemove) 
        {
             sector.ActiveMissions.Remove(id);
             sector.Fleet.Remove(id);
             Console.WriteLine($"[Combat] Pruned dead Player Ship {id} during search.");
        }

        return nearest;
    }

    private NPCShipData? FindNearestEnemy(ShipMission mission, SectorData sector)
    {
        NPCShipData? nearest = null;
        float minDist = 1000;

        var toRemove = new List<string>();
        foreach (var npc in sector.NPCShips.Values)
        {
            if (npc.Health <= 0 || npc.State == ShipState.Destroyed) 
            {
                toRemove.Add(npc.Id);
                continue;
            }
            float dist = GetDistance(mission.Position, npc.Position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = npc;
            }
        }

        // Safety Clean: Remove zombie NPCs found during search
        foreach (var id in toRemove)
        {
            sector.NPCShips.Remove(id);
            var faction = sector.Factions.Find(f => f.Ships.Contains(id));
            faction?.Ships.Remove(id);
            Console.WriteLine($"[Combat] Pruned zombie NPC {id} during search.");
        }

        return nearest;
    }

    private async Task EngageTarget(string shipId, NPCShipData target, SectorData sector)
    {
        float dist = GetDistance(sector.ActiveMissions[shipId].Position, target.Position);
        if (dist > 700) 
        {
            // Console.WriteLine($"[Combat] Player {shipId} target {target.Id} out of range ({dist:F1})");
            return; 
        }

        // Fire Laser!
        if (sector.Id == _gameState.CurrentSectorId)
        {
            try {
                await _babylon.FireLaser(shipId, target.Id, "#00ccff"); 
                await _babylon.UpdateCombatUI(target.Id, target.Health, target.MaxHealth);
            } catch { /* Interop failure shouldn't stop damage */ }
        }

        // Deal Damage
        var mission = sector.ActiveMissions[shipId];
        float firepower = mission.Firepower > 0 ? mission.Firepower : 10; 
        float damage = firepower;
        target.Health -= damage * (1.0f - target.Armor / 100f);
        
        if (target.Health <= 0)
        {
            target.Health = 0;
            target.State = ShipState.Destroyed;
            _lastRespawnTime = DateTime.Now;

            // Explicit State Pruning (DO THIS FIRST)
            sector.NPCShips.Remove(target.Id);
            var faction = sector.Factions.Find(f => f.Id == target.FactionId);
            faction?.Ships.Remove(target.Id);

            Console.WriteLine($"[Combat] Player {shipId} KILLED NPC {target.Id}. Purging from state.");

            if (sector.Id == _gameState.CurrentSectorId)
            {
                try {
                    await _babylon.DestroyModel(target.Id, "explode");
                } catch { 
                    Console.WriteLine($"[Combat] FAILED to destroy visual for {target.Id} - interop error.");
                }
            }
        }
    }

    private async Task EngagePlayerShip(string npcId, ShipMission target, SectorData sector)
    {
        float dist = GetDistance(sector.NPCShips[npcId].Position, target.Position);
        if (dist > 700) return; // Firing range limit matched to 700

        // Fire Laser!
        if (sector.Id == _gameState.CurrentSectorId)
        {
            try {
                _ = _babylon.FireLaser(npcId, target.ShipId, "#ff3300"); 
                _ = _babylon.UpdateCombatUI(target.ShipId, target.Health, target.MaxHealth);
            } catch { }
        }

        // Deal Damage
        var npc = sector.NPCShips[npcId];
        float firepower = npc.Firepower > 0 ? npc.Firepower : 8; 
        float damage = firepower;
        target.Health -= damage * (1.0f - target.Armor / 100f);

        // Defensive Retreat for non-combat ships
        if (target.Type != GameUnitType.FighterUnit && 
            target.State != ShipState.ReturningToStation && 
            target.State != ShipState.Repairing &&
            target.State != ShipState.Destroyed)
        {
            Console.WriteLine($"[Combat] Miner/Non-combat {target.ShipId} UNDER ATTACK! Retracting to station.");
            _ = _missionService.ReturnToStation(target.ShipId);
        }

        if (target.Health <= 0)
        {
            target.Health = 0;
            target.State = ShipState.Destroyed;
            _lastRespawnTime = DateTime.Now;

            // Player ships stay in the state but are marked destroyed for the Fleet Panel
            Console.WriteLine($"[Combat] NPC {npcId} KILLED Player {target.ShipId}!");

            if (sector.Id == _gameState.CurrentSectorId)
            {
                try {
                    await _babylon.DestroyModel(target.ShipId, "explode");
                } catch { }
            }
        }
    }

    private float GetDistance(float[] p1, float[] p2)
    {
        float dx = p1[0] - p2[0];
        float dy = p1[1] - p2[1];
        float dz = p1[2] - p2[2];
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
